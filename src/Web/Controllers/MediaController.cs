using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

public sealed class MediaController(PortalDbContext db, StorageOptions storage, MediaUploadOptions mediaOptions) : Controller
{
    [HttpGet("media/{id:guid}")]
    [HttpHead("media/{id:guid}")]
    public async Task<IActionResult> File(Guid id, bool download = false)
    {
        // Recheck the current publication grant on every request, including conditional requests.
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsQuarantined);
        if (asset is null || !mediaOptions.CanServe(asset)) return NotFound();
        if (asset.MimeType == "application/pdf" && asset.Origin is not ("Source" or "EditorialDocument")) return NotFound();
        if (asset.Origin == "EditorialDocument")
        {
            async Task<bool> Grant() => await db.ContentAssets.AnyAsync(a => a.MediaAssetId == id && a.Usage == "Attachment"
                && db.Contents.Any(c => c.IsPublic && !c.IsTrashed && c.PublishedRevisionId == a.RevisionId)
                && db.Revisions.Any(r => r.Id == a.RevisionId && (r.Metadata.DisclosureGroupId == null || db.DisclosureGroups.Any(g => g.Id == r.Metadata.DisclosureGroupId && g.Status == "NameApproved"))
                    && (asset.MimeType != OfficeDocumentPolicy.Docx || r.Metadata.WeeklyOriginalAssetId == id && r.Metadata.WeeklyOriginalReviewEvidence != null && r.Metadata.WeeklyOriginalReviewEvidence != "")));
            if (!await Grant()) return NotFound();
            var stream = await EditorialDocumentService.OpenVerifiedAsync(db, storage, asset, mediaOptions.IsDevelopmentLoopback);
            if (stream == null) return NotFound();
            if (!await Grant()) { await stream.DisposeAsync(); return NotFound(); }
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
            Request.Headers.Remove("If-None-Match"); Request.Headers.Remove("If-Modified-Since"); Request.Headers.Remove("If-Range");
            return base.File(stream, asset.MimeType, download || asset.MimeType != "application/pdf" ? asset.OriginalName : null, enableRangeProcessing: true);
        }
        var isPublished = await db.ContentAssets.AnyAsync(a => a.MediaAssetId == id
            && db.Contents.Any(c => c.IsPublic && !c.IsTrashed && c.PublishedRevisionId == a.RevisionId));
        if (!isPublished) isPublished = await PublicSiteService.HasPublicAssetGrantAsync(db, id, DateTimeOffset.UtcNow);
        if (!isPublished) return NotFound();
        // Active HTML/SVG is never served on the application origin.
        if (asset.MimeType is not ("image/jpeg" or "image/png" or "image/gif" or "image/webp" or "application/pdf" or "video/mp4")) return NotFound();
        string path;
        try { path = MediaService.ResolveMediaPath(storage, asset.StorageKey); }
        catch (InvalidOperationException) { return NotFound(); }
        catch (MediaUploadException) { return NotFound(); }
        if (!System.IO.File.Exists(path)) return NotFound();
        if (asset.ValidationStatus == "Phase3BValidated")
        {
            // Batch scan proof binds the original bytes, also after materialization.
            try
            {
                var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (asset.ScanStatus != "Clean" || input.Length != asset.ByteLength || Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(input)) != asset.Sha256) { await input.DisposeAsync(); return NotFound(); }
                if (!await db.ContentAssets.AnyAsync(a => a.MediaAssetId == id && db.Contents.Any(c => c.IsPublic && !c.IsTrashed && c.PublishedRevisionId == a.RevisionId)) && !await PublicSiteService.HasPublicAssetGrantAsync(db,id,DateTimeOffset.UtcNow)) { await input.DisposeAsync(); return NotFound(); }
                input.Position = 0;
                Response.Headers["X-Content-Type-Options"] = "nosniff";
                if (asset.MimeType == "application/pdf") Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
                Request.Headers.Remove("If-None-Match"); Request.Headers.Remove("If-Modified-Since"); Request.Headers.Remove("If-Range");
                return base.File(input, asset.MimeType, download ? Path.GetFileName(asset.OriginalName) : null, enableRangeProcessing: true);
            }
            catch (IOException) { return NotFound(); }
        }
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (asset.MimeType == "application/pdf") Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        Request.Headers.Remove("If-None-Match");
        Request.Headers.Remove("If-Modified-Since");
        Request.Headers.Remove("If-Range");
        return PhysicalFile(path, asset.MimeType, download ? Path.GetFileName(asset.OriginalName) : null, enableRangeProcessing: true);
    }
}
