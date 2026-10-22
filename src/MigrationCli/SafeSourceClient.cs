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
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellation);
                if (addresses.Length == 0 || addresses.Any(ip => !IsPublicAddress(ip))) throw new InvalidDataException("Private or reserved DNS address blocked.");
                Exception? last = null;
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellation); return new NetworkStream(socket, ownsSocket: true); }
                    catch (Exception exception) { socket.Dispose(); last = exception; }
                }
                throw new HttpRequestException("Source connection failed.", last);
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
            if (uri.Scheme is not ("https" or "http") || !AllowedHosts.Contains(uri.Host)&&!exactResources.Contains(uri.AbsoluteUri) || pagesOnly && uri.Host != "qldd.mae.gov.vn" || !uri.IsDefaultPort || uri.UserInfo.Length != 0) throw new InvalidDataException("Source URI blocked by allowlist/scheme/port policy.");
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
                if (retry > TimeSpan.FromSeconds(60)) throw new HttpRequestException("Source Retry-After exceeds bounded run wait; retry later.");
                await Task.Delay(retry, cancellation);
            }
            if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("Source Content-Length exceeds response budget.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellation);
            using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int count;
            while ((count = await input.ReadAsync(buffer, cancellation)) > 0)
            {
                if (output.Length + count > maxBytes) throw new InvalidDataException("Source decompressed response exceeds byte budget.");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
            }
            return new(uri.AbsoluteUri, (int)response.StatusCode, response.Content.Headers.ContentType?.MediaType ?? "", output.ToArray(), DateTimeOffset.UtcNow)
            {
                ETag = response.Headers.ETag?.ToString(), LastModified = response.Content.Headers.LastModified?.ToString("R"),
                RetryAfterUtc = response.Headers.RetryAfter?.Date ?? (response.Headers.RetryAfter?.Delta is TimeSpan delta ? DateTimeOffset.UtcNow + delta : null)
            };
            }
            finally { lastRequest = DateTimeOffset.UtcNow; }
        }
        throw new HttpRequestException("Source redirect limit exceeded.");
    }
    public void Dispose() => client.Dispose();
}
