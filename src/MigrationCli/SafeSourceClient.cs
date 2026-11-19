using System.Net;
using System.Net.Sockets;

namespace LandPortal.MigrationCli;

public sealed record SourceResponse(string Url, int Status, string ContentType, byte[] Bytes, DateTimeOffset FetchedUtc)
{
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
    public DateTimeOffset? RetryAfterUtc { get; init; }
}
public sealed class RequestBudgetException : Exception { public RequestBudgetException() : base("PausedByBudget: source request budget reached") { } }

public sealed class SafeSourceClient : IDisposable
{
    public static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase) { "qldd.mae.gov.vn", "media-qldd.mae.gov.vn", "media.mae.gov.vn", "i.ex-cdn.com" };
    private readonly HttpClient client;
    private readonly int budget;
    private readonly Func<string, Task>? beforeRequest;
    private DateTimeOffset lastRequest = DateTimeOffset.MinValue;
    private readonly HashSet<string> hosts;
    private readonly HashSet<string> exactResources;
    public int RequestsUsed { get; private set; }
    public SafeSourceClient(int requestBudget, Func<string, Task>? beforeRequest = null, IEnumerable<string>? reviewedResourceUrls = null)
    {
        budget = requestBudget;
        this.beforeRequest = beforeRequest;
        exactResources=(reviewedResourceUrls??[]).ToHashSet(StringComparer.Ordinal);
        hosts=AllowedHosts.Concat(exactResources.Select(x=>new Uri(x).Host)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, MaxConnectionsPerServer = 1, UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(20), MaxResponseHeadersLength = 32,
            ConnectCallback = async (context, cancellation) =>
            {
                if (!hosts.Contains(context.DnsEndPoint.Host)) throw new InvalidDataException("Destination outside source allowlist.");
                IPAddress[] addresses;
                try { addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellation); }
                catch (SocketException e) { throw new SourceTransportException("DNS", e); }
                if (addresses.Length == 0 || addresses.Any(ip => !IsPublicAddress(ip))) throw new InvalidDataException("Private or reserved DNS address blocked.");
                Exception? last = null;
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellation); return new NetworkStream(socket, ownsSocket: true); }
                    catch (Exception exception) { socket.Dispose(); last = exception; }
                }
                cancellation.ThrowIfCancellationRequested();
                throw new SourceTransportException("TCP", last);
            }
        };
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LandPortalMigration/0.1 (bounded public content migration)");
    }
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224 && !(bytes[0] == 169 && bytes[1] == 254) &&
                !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) && !(bytes[0] == 192 && bytes[1] == 168) &&
                !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127) && !(bytes[0] == 198 && bytes[1] is 18 or 19) &&
                !(bytes[0] == 192 && bytes[1] == 0) && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) &&
                !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        return address.AddressFamily == AddressFamily.InterNetworkV6 && !address.IsIPv6LinkLocal && !address.IsIPv6Multicast &&
            !address.IsIPv6SiteLocal && (bytes[0] & 0xe0) == 0x20 && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
    }
    public async Task<SourceResponse> FetchAsync(string url, int maxBytes = 8 * 1024 * 1024, CancellationToken cancellation = default, bool pagesOnly = false, string? etag = null, string? lastModified = null)
    {
        var uri = new Uri(url);
        for (var redirect = 0; redirect <= 4; redirect++)
        {
            ValidateDestination(uri,pagesOnly,redirect);
            if (RequestsUsed >= budget) throw new RequestBudgetException();
            var pause = TimeSpan.FromSeconds(2.05) - (DateTimeOffset.UtcNow - lastRequest);
            if (pause > TimeSpan.Zero) await Task.Delay(pause + TimeSpan.FromMilliseconds(Random.Shared.Next(20, 120)), cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (beforeRequest is not null) await beforeRequest(uri.AbsoluteUri);
            RequestsUsed++;
            try
            {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            if (lastModified is not null) request.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location is not null)
            { uri = new Uri(uri, response.Headers.Location); continue; }
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is DateTimeOffset retryDate ? retryDate - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(10));
                if (retry < TimeSpan.Zero) retry = TimeSpan.Zero;
                if (retry > TimeSpan.FromSeconds(60)) throw new SourceTransportException("HTTP_RETRY_AFTER");
                await Task.Delay(retry, cancellation);
            }
            if (response.Content.Headers.ContentLength > maxBytes) throw new SourceTransportException("BYTE_LIMIT",stage:"tệp nguồn");
            await using var input = await response.Content.ReadAsStreamAsync(cancellation);
            using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int count;
            while ((count = await input.ReadAsync(buffer, cancellation)) > 0)
            {
                if (output.Length + count > maxBytes) throw new SourceTransportException("BYTE_LIMIT",stage:"tệp nguồn");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
            }
            return new(uri.AbsoluteUri, (int)response.StatusCode, response.Content.Headers.ContentType?.MediaType ?? "", output.ToArray(), DateTimeOffset.UtcNow)
            {
                ETag = response.Headers.ETag?.ToString(), LastModified = response.Content.Headers.LastModified?.ToString("R"),
                RetryAfterUtc = response.Headers.RetryAfter?.Date ?? (response.Headers.RetryAfter?.Delta is TimeSpan delta ? DateTimeOffset.UtcNow + delta : null)
            };
            }
            catch (HttpRequestException e)
            {
                throw new SourceTransportException(SourceTransportException.Classify(e), e,
                    uri.AbsolutePath == "/robots.txt" ? "robots.txt" : pagesOnly ? "trang nguồn" : "tệp nguồn");
            }
            catch (OperationCanceledException e) when (!cancellation.IsCancellationRequested)
            { throw new SourceTransportException("TIMEOUT",e,uri.AbsolutePath=="/robots.txt"?"robots.txt":pagesOnly?"trang nguồn":"tệp nguồn"); }
            finally { lastRequest = DateTimeOffset.UtcNow; }
        }
        throw new SourceTransportException("REDIRECT_LIMIT", stage: "chuyển hướng");
    }
    void ValidateDestination(Uri uri,bool pagesOnly,int redirect)
    {
        if (uri.Scheme is not ("https" or "http") || !AllowedHosts.Contains(uri.Host)&&!exactResources.Contains(uri.AbsoluteUri) || pagesOnly && uri.Host != "qldd.mae.gov.vn" || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            throw new SourceTransportException(redirect==0?"URL_POLICY":"REDIRECT_BLOCKED",stage:redirect==0?"địa chỉ nguồn":"chuyển hướng");
    }
    public void Dispose() => client.Dispose();
}

// Only fixed codes and stages may reach CMS/audit. Never expose exception messages,
// request URLs, proxy credentials, certificate contents or response bodies.
public sealed class SourceTransportException(string code, Exception? inner = null, string stage = "kết nối")
    : HttpRequestException("Source transport failed: " + code, inner)
{
    public string Code { get; } = code;
    public string Stage { get; } = stage;
    public string SafeSummary => Code switch
    {
        "BYTE_LIMIT"=>"Phản hồi nguồn vượt giới hạn dung lượng [BYTE_LIMIT].",
        "REDIRECT_BLOCKED" or "REDIRECT_LIMIT"=>"Chuyển hướng nguồn bị chặn theo chính sách ["+Code+"].",
        "URL_POLICY" or "NETWORK_POLICY"=>"Địa chỉ nguồn bị chặn theo chính sách an toàn ["+Code+"].",
        "TIMEOUT"=>"Nguồn phản hồi quá thời gian cho phép [TIMEOUT].",
        _=>$"Không kết nối được nguồn tại {Stage} [{Code}]. Dữ liệu hàng chờ được giữ nguyên; thử lại có giới hạn."
    };
    public static string Classify(Exception error)
    {
        for (Exception? e = error; e != null; e = e.InnerException)
        {
            if (e is SourceTransportException source) return source.Code;
            if (e is System.Security.Authentication.AuthenticationException) return "TLS";
            if (e is InvalidDataException) return "NETWORK_POLICY";
        }
        if (error is HttpRequestException request)
            return request.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => "DNS",
                HttpRequestError.SecureConnectionError => "TLS",
                HttpRequestError.ConnectionError => "TCP",
                HttpRequestError.ProxyTunnelError => "PROXY",
                HttpRequestError.HttpProtocolError or HttpRequestError.InvalidResponse or HttpRequestError.ResponseEnded => "HTTP_RESPONSE",
                _ => "HTTP_TRANSPORT"
            };
        return error is OperationCanceledException ? "TIMEOUT_OR_CANCELLED" : "NETWORK_UNKNOWN";
    }
}
