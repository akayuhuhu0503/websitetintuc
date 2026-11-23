using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

public sealed record EditorialDocumentRow(MediaAsset Asset, EditorialDocument Proof);
public sealed record EditorialDocumentsPage(Guid ContentId, string Title, bool CanUpload, IReadOnlyList<EditorialDocumentRow> Files, IReadOnlyList<MediaAsset> LegacyPdfs, bool ManagedNoAvEnabled)
{
    public bool TrustedInternalEnabled { get; init; }
    public bool CanConfirmTrustedInternal { get; init; }
}

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EditorialDocumentsController(PortalDbContext db, CurrentPermissionService rights, EditorialDocumentService documents,
    StorageOptions storage, IWebHostEnvironment environment, IConfiguration config, MediaUploadOptions options) : Controller
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
        return View(new EditorialDocumentsPage(id, content.CurrentRevisionId == null ? "Bản nháp" : await db.Revisions.Where(x => x.Id == content.CurrentRevisionId).Select(x => x.Title).SingleAsync(), actor.Has(Permissions.ManageMedia) && await rights.CanEditAsync(actor, content), files, legacy, options.ApprovedManagedDocumentsWithoutAv)
        { TrustedInternalEnabled = TrustedInternalDocumentPolicy.Enabled(options), CanConfirmTrustedInternal = TrustedInternalDocumentPolicy.Enabled(options) && TrustedInternalDocumentPolicy.AllowedActor(actor) });
    }
    [HttpPost("admin/documents/{id:guid}/enqueue/{assetId:guid}")]
    public async Task<IActionResult> Enqueue(Guid id, Guid assetId, CancellationToken ct)
    {
        try { EditorialDocumentService.RequireTestInfrastructure(config, environment.EnvironmentName); await documents.EnqueueLegacyPdfAsync(id, assetId, await Actor(), ct); TempData["Success"] = "PDF tải lên cũ đã vào hàng đợi riêng tư chưa quét an toàn. Byte và phiên bản được giữ; cần tác vụ nền quét thật trước công khai."; return Redirect("/admin/documents/" + id); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException error) { return BadRequest(error.Message); }
    }
    [HttpPost("admin/documents/{id:guid}"), RequestSizeLimit(EditorialDocumentPolicy.RequestMaximum), RequestFormLimits(MultipartBodyLengthLimit = EditorialDocumentPolicy.RequestMaximum, MemoryBufferThreshold = 65536)]
    public async Task<IActionResult> Upload(Guid id, IFormFile? file, CancellationToken ct, string? format = null, bool trustedInternal = false, string? trustedSource = null)
    {
        try
        {
            EditorialDocumentService.RequireTestInfrastructure(config, environment.EnvironmentName);
            if (trustedInternal && string.IsNullOrWhiteSpace(trustedSource)) return BadRequest("Nhập nguồn tài liệu nội bộ đã kiểm soát trước khi xác nhận không quét antivirus.");
            if (!trustedInternal && !string.IsNullOrWhiteSpace(trustedSource)) return BadRequest("Chưa chọn xác nhận tài liệu nội bộ.");
            if (file == null || Request.Form.Files.Count != 1) return BadRequest("Chọn đúng một tài liệu.");
            EditorialDocumentPolicy.RequireSize(file.Length, EditorialDocumentPolicy.Mime(file.FileName));
            await using var stream = file.OpenReadStream(); var asset = await documents.UploadAsync(id, stream, file.FileName, file.ContentType, await Actor(), ct, trustedInternal ? trustedSource : null);
            if (format == "json") return Json(Describe(asset));
            TempData["Success"] = asset.ValidationStatus==TrustedInternalDocumentPolicy.Validation
                ? "Đã tiếp nhận tài liệu nội bộ theo xác nhận quản trị, kiểm định kỹ thuật và lưu audit. CHƯA quét antivirus; chưa công khai."
                : asset.ValidationStatus==MediaUploadOptions.ManagedDocumentNoAvValidation
                ? "Đã lưu tài liệu và kiểm định định dạng (CHƯA quét antivirus). Chỉ phát hành theo quyền duyệt bài."
                : "Đã lưu SQL và kho cách ly, trạng thái chưa quét an toàn. Chạy tác vụ nền quét thật trước khi đọc hoặc dùng tài liệu.";
            return Redirect("/admin/documents/" + id);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException error) { Response.StatusCode = 400; return Content(error.Message); }
        catch (InvalidDataException) { return BadRequest("Tài liệu không đạt kiểm tra định dạng hoặc chứa thành phần không được phép."); }
        catch (System.Xml.XmlException) { return BadRequest("Cấu trúc tệp Office không hợp lệ."); }
        catch (MediaUploadException error) { return StatusCode(error.StatusCode,error.Message); }
        catch (DbUpdateException) { return StatusCode(503, "SQL chưa xác nhận lưu tài liệu. Kiểm tra trước khi gửi lại."); }
    }
    [HttpGet("admin/documents/{id:guid}/items")]
    public async Task<IActionResult> Items(Guid id)
    {
        var actor = await Actor(); var content = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (content == null || !actor.Has(Permissions.ManageMedia) || !await rights.CanReadAsync(actor, content)) return NotFound();
        var assets = await db.MediaAssets.AsNoTracking().Where(a => db.EditorialDocuments.Any(d => d.ContentId == id && d.MediaAssetId == a.Id)).OrderByDescending(a => a.CreatedUtc).Take(100).ToListAsync();
        return Json(new { items = assets.Select(Describe) });
    }
    // Reconcile a lost upload response without receiving, storing and validating the same bytes again.
    // Knowing a hash never grants access: only this uploader's document on this editable article is visible.
    [HttpGet("admin/documents/{id:guid}/result")]
    public async Task<IActionResult> Result(Guid id, string? sha256, CancellationToken ct)
    {
        if (sha256 == null || sha256.Length != 64 || sha256.Any(c => !char.IsAsciiHexDigit(c))) return BadRequest();
        var actor = await Actor();
        var content = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (content == null || content.IsTrashed || !actor.Has(Permissions.ManageMedia) || !await rights.CanEditAsync(actor, content)) return NotFound();
        var hash = sha256.ToLowerInvariant();
        var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(a => a.Sha256 == hash
            && a.Origin == "EditorialDocument" && a.UploadedByUserId == actor.UserId
            && db.EditorialDocuments.Any(d => d.MediaAssetId == a.Id && d.ContentId == id && d.OwnerId == actor.UserId), ct);
        return asset == null ? NotFound() : Json(Describe(asset));
    }
    private object Describe(MediaAsset asset) => new { asset.Id, asset.OriginalName, asset.MimeType, asset.ByteLength, asset.ScanStatus,
        usable = options.CanServe(asset),
        previewUrl = "/admin/documents/file/" + asset.Id, url = "/media/" + asset.Id,
        notice = asset.ValidationStatus == TrustedInternalDocumentPolicy.Validation ? "Nguồn nội bộ được quản trị xác nhận; đã kiểm định kỹ thuật, chưa quét antivirus. Chưa công khai."
            : asset.ValidationStatus == MediaUploadOptions.ManagedDocumentNoAvValidation ? "Đã kiểm tra định dạng; chưa quét antivirus. Chưa công khai." : "Tài liệu được giữ trong bài này; chỉ đính kèm khi đạt kiểm định." };
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
        var stream = await EditorialDocumentService.OpenVerifiedAsync(db, storage, asset, environment.IsDevelopment(), policy: options);
        if (stream == null) return StatusCode(423, "Tài liệu chưa đủ điều kiện xem hoặc tải theo chính sách kiểm định hiện hành.");
        if (!await Allowed()) { await stream.DisposeAsync(); return NotFound(); }
        Response.Headers["X-Content-Type-Options"] = "nosniff"; Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        Request.Headers.Remove("If-None-Match"); Request.Headers.Remove("If-Modified-Since"); Request.Headers.Remove("If-Range");
        return File(stream, asset.MimeType, asset.OriginalName, enableRangeProcessing: true);
    }
}
