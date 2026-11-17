using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace LandPortal.WeeklyImport;

// HTTP only: never loads private configuration, opens SQL or persists cookies/passwords.
public sealed class WebCmsClient : IDisposable
{
    readonly HttpClient admin;
    readonly HttpClient anonymous;
    public Uri Origin { get; }
    public string UserId { get; private set; } = "";
    public WebCmsClient(Uri origin) : this(origin, false) { }
    internal WebCmsClient(Uri origin, bool isolatedLoopback)
    {
        if (!origin.IsAbsoluteUri || origin.UserInfo != "" || origin.AbsolutePath != "/" || origin.Query != "" || origin.Fragment != ""
            || origin.Scheme != "https" && !(isolatedLoopback && origin.IsLoopback && origin.Scheme == "http"))
            throw new ArgumentException("WEB IMPORT cần địa chỉ gốc HTTPS; không bỏ kiểm tra chứng chỉ.");
        Origin = origin;
        admin = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new() }) { Timeout = TimeSpan.FromMinutes(3) };
        anonymous = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(45) };
    }
    public Uri Url(string path)
    {
        var uri = new Uri(Origin, path);
        if (uri.GetLeftPart(UriPartial.Authority) != Origin.GetLeftPart(UriPartial.Authority) || uri.UserInfo != "")
            throw new InvalidDataException("Không gửi yêu cầu CMS sang host khác.");
        return uri;
    }
    public async Task LoginAsync(Func<string, Task<(string User, string Password, string Answer)>> prompt)
    {
        var html = await HtmlAsync("/admin/login");
        var form = html.QuerySelector("form[action='/admin/login']") ?? throw new InvalidDataException("Không tìm thấy form đăng nhập CMS hiện hành.");
        var csrf = Value(form, "__RequestVerificationToken"); var captcha = Value(form, "CaptchaId");
        var question = html.QuerySelector("#captcha-question")?.TextContent.Trim();
        if (csrf == "" || captcha == "" || string.IsNullOrWhiteSpace(question)) throw new InvalidDataException("Thiếu CSRF/CAPTCHA đăng nhập; dừng.");
        var credentials = await prompt(question);
        using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["UserName"] = credentials.User, ["Password"] = credentials.Password,
            ["CaptchaAnswer"] = credentials.Answer, ["CaptchaId"] = captcha, ["__RequestVerificationToken"] = csrf, ["ReturnUrl"] = "/admin" });
        credentials = default;
        using var response = await admin.PostAsync(Url("/admin/login"), body);
        // One attempt only. Must-change/MFA/lockout remain managed by the website.
        if (response.StatusCode != HttpStatusCode.Redirect || response.Headers.Location?.ToString() != "/admin")
            throw new UnauthorizedAccessException("Đăng nhập chưa thành công hoặc cần đổi mật khẩu/MFA. Dừng, mở CMS để xử lý; không tự thử lại.");
        await SessionAsync();
    }
    public async Task<string> SessionAsync()
    {
        var json = await JsonAsync("/admin/session");
        var id = json.GetProperty("userId").GetString();
        var token = json.GetProperty("csrf").GetString();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(token) || UserId != "" && id != UserId)
            throw new UnauthorizedAccessException("Phiên CMS không khớp người nhập; dừng.");
        UserId = id; return token;
    }
    public async Task<IDocument> HtmlAsync(string path, bool publicly = false) => new HtmlParser().ParseDocument(Encoding.UTF8.GetString(await BytesAsync(path, "text/html", 8 * 1024 * 1024, publicly)));
    public async Task<JsonElement> JsonAsync(string path)
    {
        var bytes = await BytesAsync(path, "application/json", 8 * 1024 * 1024);
        using var doc = JsonDocument.Parse(bytes); return doc.RootElement.Clone();
    }
    public async Task<JsonElement?> FindUploadAsync(Guid content, string hash)
    {
        using var response = await admin.GetAsync(Url($"/admin/documents/{content}/result?sha256={hash}"), HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        var bytes = await ReadAsync(response, "application/json", 1024 * 1024);
        using var doc = JsonDocument.Parse(bytes); return doc.RootElement.Clone();
    }
    public async Task<JsonElement> SaveAsync(IReadOnlyDictionary<string, string[]> fields)
    {
        var token = await SessionAsync();
        using var body = new FormUrlEncodedContent(fields.SelectMany(p => p.Value.Select(v => new KeyValuePair<string, string>(p.Key, v)))
            .Append(new("__RequestVerificationToken", token)));
        using var response = await admin.PostAsync(Url("/admin/save?format=json"), body);
        using var doc = JsonDocument.Parse(await ReadAsync(response, "application/json", 1024 * 1024));
        return doc.RootElement.Clone();
    }
    public async Task<JsonElement> UploadAsync(Guid content, WeeklyRow row, byte[] bytes)
    {
        var token = await SessionAsync();
        using var body = new MultipartFormDataContent();
        body.Add(new StringContent(token), "__RequestVerificationToken");
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        body.Add(file, "file", row.FileName);
        using var response = await admin.PostAsync(Url($"/admin/documents/{content}?format=json"), body);
        using var doc = JsonDocument.Parse(await ReadAsync(response, "application/json", 1024 * 1024)); return doc.RootElement.Clone();
    }
    public async Task<byte[]> BytesAsync(string path, string mime, long maximum, bool publicly = false, string? expectedRedirect = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(path));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        using var response = await (publicly ? anonymous : admin).SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        // CMS legacy preview redirects editorial documents to their guarded file
        // endpoint. Accept only this caller-bound same-origin asset/content route.
        if (expectedRedirect != null && !publicly && response.StatusCode == HttpStatusCode.Redirect
            && response.Headers.Location != null && Url(response.Headers.Location.ToString()) == Url(expectedRedirect))
        {
            using var redirected = await admin.GetAsync(Url(expectedRedirect), HttpCompletionOption.ResponseHeadersRead);
            return await ReadAsync(redirected, mime, maximum);
        }
        return await ReadAsync(response, mime, maximum);
    }
    static async Task<byte[]> ReadAsync(HttpResponseMessage response, string mime, long maximum)
    {
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidDataException($"CMS HTTP {(int)response.StatusCode} tại {response.RequestMessage?.RequestUri?.AbsolutePath}. Dừng; không tự gửi lại. Nếu 302/401: đăng nhập lại; 403: thiếu quyền; 409: nội dung đã đổi; 423/503: tệp/chính sách chưa sẵn sàng.");
        if (response.Content.Headers.ContentType?.MediaType != mime || response.Content.Headers.ContentLength > maximum)
            throw new InvalidDataException("Phản hồi sai định dạng/kích thước; không coi là thành công.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token); using var result = new MemoryStream();
        var buffer = new byte[65536]; int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token)) != 0)
        { if (result.Length + read > maximum) throw new InvalidDataException("Phản hồi vượt giới hạn."); await result.WriteAsync(buffer.AsMemory(0, read), timeout.Token); }
        return result.ToArray();
    }
    public static string Value(IParentNode form, string name)
    {
        var item = form.QuerySelectorAll("[name]").SingleOrDefault(x => x.GetAttribute("name") == name);
        return item?.LocalName == "textarea" ? item.TextContent : item?.LocalName == "select" ? item.QuerySelector("option[selected]")?.GetAttribute("value") ?? "" : item?.GetAttribute("value") ?? "";
    }
    public void Dispose() { admin.Dispose(); anonymous.Dispose(); }
}
