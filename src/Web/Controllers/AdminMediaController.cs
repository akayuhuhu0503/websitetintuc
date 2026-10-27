using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace LandPortal.Web.Controllers;

[Authorize]
public sealed class AdminMediaController(PortalDbContext db, StorageOptions storage, MediaUploadOptions options,
    MediaService media, CurrentPermissionService permissions) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();

    [HttpGet("admin/media")]
    public async Task<IActionResult> Index(Guid? contentId, string? kind, string? search, int page = 1)
    {
        Response.Headers.CacheControl = "no-store, private";
        var actor = await permissions.GetAsync(UserId);
        if (!CanAccessMedia(actor)) return StatusCode(403, new { error = "Không có quyền xem kho media." });
        if (search?.Length > 200 || kind is not (null or "" or "image" or "pdf")) return BadRequest();
        if (contentId.HasValue)
        {
            var content = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contentId.Value);
            if (content is null || !await permissions.CanReadAsync(actor, content)) return NotFound();
        }
        var developmentException = options.DevelopmentExceptionEnabled;
        var assets = db.MediaAssets.AsNoTracking().Where(x => !x.IsQuarantined && (x.ScanStatus == "NotScanned" || x.ScanStatus == "Clean") && (x.Origin == "Source"
            || x.Origin == "Uploaded" && x.ValidationStatus == "Validated" && (x.ScanStatus == "Clean" || developmentException)
            || x.Origin == "EditorialDocument" && x.MimeType == "application/pdf" && x.ValidationStatus == "Validated" && x.ScanStatus == "Clean" && db.EditorialDocuments.Any(d => d.MediaAssetId == x.Id && d.ScanState == InquiryScanState.Clean && d.ScannedSha256 == x.Sha256 && d.EngineVersion != null)));
        if (actor.CategoryIds is not null || !actor.Has(Permissions.EditAll) && !actor.Has(Permissions.ViewAll))
        {
            var owner = actor.UserId;
            var readableRevisionIds=permissions.ScopeRevisions(db.Revisions.AsNoTracking(),actor).Where(r=>r.ContentId==contentId).Select(r=>r.Id);
            assets = assets.Where(x => x.UploadedByUserId == owner
                || db.ContentAssets.Any(a => a.MediaAssetId == x.Id && db.Contents.Any(c => c.IsPublic && !c.IsTrashed && c.PublishedRevisionId == a.RevisionId))
                || (contentId != null && (db.ContentAssets.Any(a => a.MediaAssetId == x.Id && readableRevisionIds.Contains(a.RevisionId))
                    || db.DraftAssets.Any(a => a.ContentId == contentId && a.MediaAssetId == x.Id))));
        }
        if (kind == "image") assets = assets.Where(x => x.MimeType.StartsWith("image/"));
        if (kind == "pdf") assets = assets.Where(x => x.MimeType == "application/pdf");
        if (!string.IsNullOrWhiteSpace(search)) assets = assets.Where(x => x.OriginalName.Contains(search));
        page = Math.Clamp(page, 1, 10_000);
        const int pageSize = 20;
        var total = await assets.CountAsync();
        var rows = await assets.OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Json(new { items = rows.Select(x => Describe(x, contentId)), page, total, pageSize,
            uploadAvailable = options.UploadEnabled && actor.Has(Permissions.ManageMedia),
            notice = options.NativeUatUploads?"UAT: ảnh được quét Clamd trước khi cấp quyền dùng; PDF/Word/Excel dùng mục Tài liệu.":options.DevelopmentExceptionEnabled ? "Ngoại lệ Development: tệp tải lên được kiểm tra định dạng, chưa quét mã độc." : "Tải lên chưa mở: thiếu máy quét và chưa bật ngoại lệ Development." });
    }

    [HttpPost("admin/media/upload"), RequireUploadAntiforgeryHeader, DisableUploadFormValueModelBinding, RequestSizeLimit(67_174_400)]
    public async Task<IActionResult> Upload([FromQuery] Guid? contentId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store, private";
        var actor = await permissions.GetAsync(UserId);
        if (!actor.Has(Permissions.ManageMedia)) return StatusCode(403, new { error = "Quyền quản lý media hiện thời không cho phép tải lên." });
        if (!contentId.HasValue && !actor.Has(Permissions.Create) && !actor.Has(Permissions.EditOwn) && !actor.Has(Permissions.EditAll) && !actor.Has(Permissions.ManageSite))
            return StatusCode(403, new { error = "Không có quyền tạo/sửa nội dung hoặc cấu hình để tải lên tài nguyên." });
        if (contentId.HasValue)
        {
            var content = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contentId.Value, cancellationToken);
            if (content is null || (!CanEdit(actor, content)||!await permissions.CanAccessContentAsync(actor,content.Id))) return NotFound();
        }
        if (!options.UploadEnabled)
            return StatusCode(503, new { error = "Thiếu máy quét mã độc. Chỉ mở tải lên với ngoại lệ Development được cấu hình tường minh trên loopback." });
        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var type)
            || !string.Equals(type.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Yêu cầu tải lên phải là multipart/form-data." });
        var boundary = HeaderUtilities.RemoveQuotes(type.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > 200) return BadRequest(new { error = "Multipart boundary không hợp lệ." });
        try
        {
            var reader = new MultipartReader(boundary, Request.Body) { HeadersCountLimit = 8, HeadersLengthLimit = 16_384,
                BodyLengthLimit = Math.Max(options.MaxImageBytes, options.MaxPdfBytes) + 64 * 1024 };
            var section = await reader.ReadNextSectionAsync(cancellationToken);
            if (section is null || !ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                || !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase)
                || HeaderUtilities.RemoveQuotes(disposition.Name).Value != "file")
                return BadRequest(new { error = "Chỉ nhận một tệp trong trường file; contentId đặt trên query URL." });
            var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
            if (string.IsNullOrWhiteSpace(fileName)) return BadRequest(new { error = "Chưa chọn tệp tải lên." });
            // The multipart body streams into a bounded project temporary file, without IFormFile buffering.
            var result = await media.UploadAsync(section.Body, fileName, section.ContentType ?? "application/octet-stream",
                actor.UserId, actor.Has(Permissions.EditAll), cancellationToken,
                async () => { if (await reader.ReadNextSectionAsync(cancellationToken) is not null)
                    throw new MediaUploadException("Mỗi yêu cầu chỉ nhận một tệp; yêu cầu nhiều trường không được lưu.");
                    var fresh=await permissions.GetAsync(UserId);if(!fresh.Has(Permissions.ManageMedia))throw new UnauthorizedAccessException();
                    if(contentId.HasValue){var current=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==contentId.Value,cancellationToken);if(!CanEdit(fresh,current)||!await permissions.CanAccessContentAsync(fresh,current.Id))throw new UnauthorizedAccessException();}
                });
            return Json(new
            {
                result.Asset.Id, result.Asset.OriginalName, result.Asset.MimeType, result.Asset.ByteLength,
                result.Asset.Width, result.Asset.Height, result.Asset.ScanStatus, result.Asset.ValidationStatus,
                url = "/media/" + result.Asset.Id, previewUrl = PreviewUrl(result.Asset.Id, contentId), result.Reused,
                notice = options.NativeUatUploads?"Đã kiểm định và quét Clamd, lưu tệp Clean vào SQL/kho riêng. Chỉ công khai theo phiên bản được xuất bản.":"Đã lưu vào SQL và kho riêng; kiểm tra định dạng đạt. Chưa quét mã độc (ngoại lệ Development), chưa cấp quyền công khai."
            });
        }
        catch (MediaUploadException error) { return StatusCode(error.StatusCode, new { error = error.Message }); }
        catch (UnauthorizedAccessException) {return Forbid();}
        catch (InvalidDataException) { return BadRequest(new { error = "Multipart bị lỗi hoặc vượt giới hạn tải lên." }); }
        catch (IOException) { return StatusCode(503, new { error = "Không ghi được kho tệp; tải lên chưa được xác nhận thành công." }); }
        catch (DbUpdateException) { return StatusCode(503, new { error = "SQL chưa lưu được tệp; tải lên chưa được xác nhận thành công." }); }
    }

    [HttpGet("admin/media/{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, Guid? contentId, bool download = false)
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        var actor = await permissions.GetAsync(UserId);
        if (!CanAccessMedia(actor)) return NotFound();
        var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (asset?.Origin == "EditorialDocument") return LocalRedirect("/admin/documents/file/" + id + (contentId.HasValue ? "?contentId=" + contentId : ""));
        if (asset is null || !options.CanServe(asset)) return NotFound();
        var allowed = actor.CategoryIds is null && (actor.Has(Permissions.EditAll) || actor.Has(Permissions.ViewAll)) || asset.UploadedByUserId == actor.UserId;
        if (contentId.HasValue)
        {
            var content = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contentId.Value);
            if (content is null || !await permissions.CanReadAsync(actor, content)) return NotFound();
            var accessibleRevisionIds=permissions.ScopeRevisions(db.Revisions.AsNoTracking(),actor).Where(r=>r.ContentId==contentId.Value).Select(r=>r.Id);
            var linked = await db.ContentAssets.AnyAsync(x => x.MediaAssetId == id && accessibleRevisionIds.Contains(x.RevisionId))
                || await db.DraftAssets.AnyAsync(x => x.ContentId == contentId.Value && x.MediaAssetId == id);
            allowed = linked || (CanEdit(actor, content) && (asset.UploadedByUserId == actor.UserId || actor.CategoryIds is null && actor.Has(Permissions.EditAll)));
        }
        if (!allowed)
            allowed = await db.ContentAssets.AnyAsync(a => a.MediaAssetId == id
                && db.Contents.Any(c => c.IsPublic && !c.IsTrashed && c.PublishedRevisionId == a.RevisionId));
        if (!allowed || asset.MimeType is not ("image/jpeg" or "image/png" or "image/gif" or "image/webp" or "application/pdf")) return NotFound();
        string path;
        try { path = MediaService.ResolveMediaPath(storage, asset.StorageKey); }
        catch (InvalidOperationException) { return NotFound(); }
        catch (MediaUploadException) { return NotFound(); }
        if (!System.IO.File.Exists(path)) return NotFound();
        if(asset.ValidationStatus==BatchMediaProof.Validation&&!await BatchMediaProof.ValidAsync(storage,asset,options.VerifiedSourceAllowed))return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (asset.MimeType == "application/pdf") Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        Request.Headers.Remove("If-None-Match"); Request.Headers.Remove("If-Modified-Since"); Request.Headers.Remove("If-Range");
        return PhysicalFile(path, asset.MimeType, download ? Path.GetFileName(asset.OriginalName) : null, enableRangeProcessing: true);
    }

    private static bool CanEdit(ContentActor actor, Content content) => CurrentPermissionService.CanEdit(actor, content) && !content.IsTrashed;
    private static bool CanAccessMedia(ContentActor actor) => actor.Has(Permissions.ManageMedia) || actor.Has(Permissions.EditOwn)
        || actor.Has(Permissions.EditAll) || actor.Has(Permissions.ViewOwn) || actor.Has(Permissions.ViewAll)
        || actor.Has(Permissions.Review) || actor.Has(Permissions.PublishApproved) || actor.Has(Permissions.PublishDirect) || actor.Has(Permissions.Create)
        || actor.Has(Permissions.ViewSite) || actor.Has(Permissions.ManageSite) || actor.Has(Permissions.PublishSite);
    private static string PreviewUrl(Guid id, Guid? contentId) => "/admin/media/" + id + "/preview" + (contentId.HasValue ? "?contentId=" + contentId : "");
    private static object Describe(MediaAsset asset, Guid? contentId) => new
    {
        asset.Id, asset.OriginalName, asset.MimeType, asset.ByteLength, asset.Width, asset.Height,
        asset.ScanStatus, asset.ValidationStatus, url = "/media/" + asset.Id, previewUrl = PreviewUrl(asset.Id, contentId)
    };
}

/// <summary>Reject headerless uploads before antiforgery attempts to buffer a multipart body.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireUploadAntiforgeryHeaderAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
{
    public int Order => -1000;
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue("X-CSRF-TOKEN", out var token) || string.IsNullOrWhiteSpace(token.ToString()))
            context.Result = new BadRequestObjectResult(new { error = "Thiếu CSRF token trong header tải lên." });
    }
}

/// <summary>Keep MVC value-provider factories from buffering the streaming multipart body.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableUploadFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        for (var index = context.ValueProviderFactories.Count - 1; index >= 0; index--)
            if (context.ValueProviderFactories[index] is FormValueProviderFactory or FormFileValueProviderFactory or JQueryFormValueProviderFactory)
                context.ValueProviderFactories.RemoveAt(index);
    }
    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
