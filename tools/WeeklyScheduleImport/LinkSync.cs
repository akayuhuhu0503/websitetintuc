using System.Net;
using AngleSharp.Html.Parser;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.WeeklyImport;

public sealed record LinkResult(int Stt, Guid? ContentId, Guid? PublishedRevisionId, string? Url, string Status);
public sealed class LinkSync(PortalDbContext db, MediaUploadOptions policy, HttpClient http, Uri publicBase)
{
    public async Task<IReadOnlyList<LinkResult>> VerifyAsync(WeeklyPackage package, ImportReceipt receipt, TargetIdentity target)
    {
        if (receipt.ZipSha256 != package.ZipSha256 || receipt.WorkbookSha256 != package.WorkbookSha256 || !WeeklyImporter.SameTarget(receipt.Target, target)
            || receipt.Rows.GroupBy(x => x.Stt).Any(g => g.Count() != 1) || receipt.Rows.GroupBy(x => x.ContentId).Any(g => g.Count() != 1)
            || receipt.Rows.Any(r => r.Stt is < 1 or > 43)) throw new InvalidDataException("Receipt không khớp nguồn/đích hoặc mapping bị trùng.");
        if (!publicBase.IsAbsoluteUri || publicBase.UserInfo.Length > 0 || publicBase.AbsolutePath != "/" || publicBase.Query != "" || publicBase.Fragment != ""
            || publicBase.Scheme != "https" && !(publicBase.IsLoopback && publicBase.Scheme == "http"))
            throw new InvalidDataException("Public BaseUrl phải là HTTPS đúng host; chỉ HTTP loopback cho kiểm thử.");
        var results = new List<LinkResult>();
        foreach (var row in package.Rows)
        {
            var mapping = receipt.Rows.SingleOrDefault(x => x.Stt == row.Stt);
            if (mapping == null || mapping.MediaId == null) { results.Add(new(row.Stt, mapping?.ContentId, null, null, "Chưa nhập/đính kèm")); continue; }
            var found = await PublishedAsync(mapping, row);
            if (found == null) { results.Add(new(row.Stt, mapping.ContentId, null, null, "Chưa công khai đúng tiêu đề/PDF/phiên bản")); continue; }
            var url = new Uri(publicBase, "bai-viet/" + mapping.ContentId);
            try
            {
                var htmlBytes = await GetBytes(url, 4 * 1024 * 1024, "text/html");
                var html = new HtmlParser().ParseDocument(System.Text.Encoding.UTF8.GetString(htmlBytes));
                var title = html.QuerySelector("h1")?.TextContent.Trim();
                var hasAttachment = html.QuerySelectorAll("a[href]").Any(a => Uri.TryCreate(url, a.GetAttribute("href"), out var href)
                    && href.GetLeftPart(UriPartial.Authority) == publicBase.GetLeftPart(UriPartial.Authority)
                    && href.AbsolutePath == "/media/" + mapping.MediaId);
                if (title != row.Title || !hasAttachment) throw new InvalidDataException("Trang công khai chưa chứa đúng tiêu đề và PDF.");
                var pdfUrl = new Uri(publicBase, $"media/{mapping.MediaId}?download=true&contentId={mapping.ContentId}");
                var bytes = await GetBytes(pdfUrl, EditorialDocumentService.Maximum, "application/pdf");
                if (bytes.LongLength != row.Bytes || WeeklyPackage.Hash(bytes) != row.PdfSha256) throw new InvalidDataException("PDF công khai không khớp byte/hash nguồn.");
                // A stale HTTP response cannot validate a withdrawn or replaced revision.
                if (await PublishedAsync(mapping, row) != found) throw new InvalidDataException("Phiên bản công khai đã đổi trong lúc kiểm tra.");
                results.Add(new(row.Stt, mapping.ContentId, found, url.AbsoluteUri, "PublishedVerified"));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidDataException)
            { results.Add(new(row.Stt, mapping.ContentId, found, null, e is InvalidDataException ? e.Message : "Không xác minh được HTTP; chưa ghi link")); }
        }
        // Final readback before returning any links to the workbook writer.
        for (var i = 0; i < results.Count; i++)
            if (results[i].Url != null && await PublishedAsync(receipt.Rows.Single(r => r.Stt == results[i].Stt), package.Rows[i]) != results[i].PublishedRevisionId)
                results[i] = results[i] with { Url = null, Status = "Đã đổi trạng thái trước khi ghi Excel" };
        return results;
    }
    async Task<Guid?> PublishedAsync(RowReceipt mapping, WeeklyRow row)
    {
        var c = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mapping.ContentId && x.IsPublic && !x.IsTrashed);
        if (c?.PublishedRevisionId == null || c.Kind is not (ContentKind.StaticPage or ContentKind.Article)) return null;
        var r = await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == c.PublishedRevisionId && x.ContentId == c.Id && x.PrimaryCategoryId == 76);
        if (r == null || r.Title != row.Title || r.Metadata.DisclosureGroupId != null || !await db.ContentAssets.AnyAsync(x => x.RevisionId == r.Id && x.MediaAssetId == mapping.MediaId && x.Usage == "Attachment")) return null;
        var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mapping.MediaId);
        if (asset == null || asset.Sha256 != row.PdfSha256 || asset.OriginalName != row.FileName || asset.ByteLength != row.Bytes || asset.MimeType != "application/pdf" || !policy.CanServe(asset)) return null;
        return r.Id;
    }
    async Task<byte[]> GetBytes(Uri url, long maximum, string mime)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode != HttpStatusCode.OK || response.RequestMessage?.RequestUri != url || response.Content.Headers.ContentType?.MediaType != mime
            || response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("HTTP không trả đúng tài nguyên công khai (200/MIME/đường dẫn).");
        await using var input = await response.Content.ReadAsStreamAsync(); using var result = new MemoryStream();
        var buffer = new byte[65536]; int read;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
        { if (result.Length + read > maximum) throw new InvalidDataException("Tài nguyên HTTP vượt giới hạn."); await result.WriteAsync(buffer.AsMemory(0, read), timeout.Token); }
        return result.ToArray();
    }
}
