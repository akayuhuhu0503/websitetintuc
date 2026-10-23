using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

public sealed record EditorialDocumentRow(MediaAsset Asset, EditorialDocument Proof);
public sealed record EditorialDocumentsPage(Guid ContentId, string Title, bool CanUpload, IReadOnlyList<EditorialDocumentRow> Files, IReadOnlyList<MediaAsset> LegacyPdfs);

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EditorialDocumentsController(PortalDbContext db, CurrentPermissionService rights, EditorialDocumentService documents,
    StorageOptions storage, IWebHostEnvironment environment, IConfiguration config) : Controller
{
    private Task<ContentActor> Actor() => rights.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("admin/documents/{id:guid}")]
    public async Task<IActionResult> Index(Guid id)
    {
        var actor = await Actor(); var content = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (content == null || !await rights.CanReadAsync(actor, content)) return NotFound();
        var files = await (from d in db.EditorialDocuments.AsNoTracking() join a in db.MediaAssets.AsNoTracking() on d.MediaAssetId equals a.Id where d.ContentId == id orderby a.CreatedUtc descending select new EditorialDocumentRow(a, d)).Take(100).ToListAsync();
        var legacy = await db.MediaAssets.AsNoTracking().Where(a => a.Origin == "Uploaded" && a.MimeType == "application/pdf" && a.ScanStatus == "NotScanned" && !a.IsQuarantined && a.ValidationStatus == "Validated" && a.ByteLength <= EditorialDocumentService.Maximum
            && (db.DraftAssets.Any(x => x.ContentId == id && x.MediaAssetId == a.Id) || db.ContentAssets.Any(x => x.Revision.ContentId == id && x.MediaAssetId == a.Id))
            && !db.DraftAssets.Any(x => x.ContentId != id && x.MediaAssetId == a.Id) && !db.ContentAssets.Any(x => x.Revision.ContentId != id && x.MediaAssetId == a.Id)).Take(100).ToListAsync();
        return View(new EditorialDocumentsPage(id, content.CurrentRevisionId == null ? "Bản nháp" : await db.Revisions.Where(x => x.Id == content.CurrentRevisionId).Select(x => x.Title).SingleAsync(), actor.Has(Permissions.ManageMedia) && await rights.CanEditAsync(actor, content), files, legacy));
    }
    [HttpPost("admin/documents/{id:guid}/enqueue/{assetId:guid}")]
    public async Task<IActionResult> Enqueue(Guid id, Guid assetId, CancellationToken ct)
    {
        try { EditorialDocumentService.RequireTestInfrastructure(config, environment.EnvironmentName); await documents.EnqueueLegacyPdfAsync(id, assetId, await Actor(), ct); TempData["Success"] = "PDF tải lên cũ đã vào hàng đợi riêng tư chưa quét an toàn. Byte và phiên bản được giữ; cần tác vụ nền quét thật trước công khai."; return Redirect("/admin/documents/" + id); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException error) { return BadRequest(error.Message); }
    }
    [HttpPost("admin/documents/{id:guid}"), RequestSizeLimit(11 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    public async Task<IActionResult> Upload(Guid id, IFormFile? file, CancellationToken ct)
    {
        try
        {
            EditorialDocumentService.RequireTestInfrastructure(config, environment.EnvironmentName);
            if (file == null || file.Length is < 1 or > EditorialDocumentService.Maximum || Request.Form.Files.Count != 1) return BadRequest("Chọn đúng một tài liệu tối đa 10 MiB.");
            await using var stream = file.OpenReadStream(); var asset = await documents.UploadAsync(id, stream, file.FileName, file.ContentType, await Actor(), ct);
            TempData["Success"] = "Đã lưu SQL và kho cách ly, trạng thái chưa quét an toàn. Chạy tác vụ nền quét thật trước khi đọc hoặc dùng tài liệu.";
            return Redirect("/admin/documents/" + id);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException error) { Response.StatusCode = 400; return Content(error.Message); }
        catch (DbUpdateException) { return StatusCode(503, "SQL chưa xác nhận lưu tài liệu. Kiểm tra trước khi gửi lại."); }
    }
    [HttpGet("admin/documents/file/{id:guid}"), HttpHead("admin/documents/file/{id:guid}")]
    public async Task<IActionResult> Download(Guid id, Guid? contentId)
    {
        Response.Headers.CacheControl = "no-store, private";
        var d = await db.EditorialDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.MediaAssetId == id);
        if (d == null) return NotFound();
        async Task<bool> Allowed()
        {
            var actor = await Actor(); var target = contentId ?? d.ContentId;
            var current = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == target);
            if (current == null || !await rights.CanReadAsync(actor, current)) return false;
            if (target == d.ContentId) return true;
            var readable = rights.ScopeRevisions(db.Revisions.AsNoTracking(), actor).Where(x => x.ContentId == target).Select(x => x.Id);
            return await db.DraftAssets.AnyAsync(x => x.ContentId == target && x.MediaAssetId == id) || await db.ContentAssets.AnyAsync(x => x.MediaAssetId == id && readable.Contains(x.RevisionId));
        }
        if (!await Allowed()) return NotFound();
        var asset = await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == id);
        var stream = await EditorialDocumentService.OpenVerifiedAsync(db, storage, asset, environment.IsDevelopment());
        if (stream == null) return StatusCode(423, "Tài liệu chưa Clean đúng byte, đang cách ly hoặc không có bằng chứng quét hợp lệ.");
        if (!await Allowed()) { await stream.DisposeAsync(); return NotFound(); }
        Response.Headers["X-Content-Type-Options"] = "nosniff"; Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        Request.Headers.Remove("If-None-Match"); Request.Headers.Remove("If-Modified-Since"); Request.Headers.Remove("If-Range");
        return File(stream, asset.MimeType, asset.OriginalName, enableRangeProcessing: true);
    }
}
