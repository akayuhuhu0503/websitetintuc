using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MaterialsController(PortalDbContext db, PublicContentQuery query, PublicSiteService sites, MediaUploadOptions policy) : Controller
{
    [HttpGet("tai-lieu")]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        if (q?.Length > 200) return BadRequest();
        page = Math.Clamp(page, 1, 10000);
        var site = await sites.GetAsync();
        var scope = site is null ? null : CategoryScope(site);
        if (scope is null) return View(new MaterialsModel([], 0, page, q));
        // A deliberately configured catalog scope, never all attachments or an
        // assumed ID 24. Only the current published revision supplies documents.
        var documents = db.MediaAssets.AsNoTracking().Where(x => !x.IsQuarantined && x.Origin == "EditorialDocument"
            && (x.MimeType == "application/pdf" || x.MimeType == OfficeDocumentPolicy.Docx || x.MimeType == OfficeDocumentPolicy.Xlsx)
            && (x.ScanStatus == "Clean" && x.ValidationStatus == "Validated"
                || policy.ApprovedManagedDocumentsWithoutAv && x.ScanStatus == "NotScanned" && x.ValidationStatus == MediaUploadOptions.ManagedDocumentNoAvValidation)
            && db.Set<EditorialDocument>().Any(d => d.MediaAssetId == x.Id &&
                (x.ScanStatus == "Clean" && d.ScanState == InquiryScanState.Clean && d.ScannedSha256 == x.Sha256 && d.ScannedUtc != null
                    && d.EngineVersion != null && d.EngineVersion != "" && (policy.IsDevelopmentLoopback || !d.EngineVersion.StartsWith("Defender/"))
                || policy.ApprovedManagedDocumentsWithoutAv && x.ScanStatus == "NotScanned" && d.ScanState == InquiryScanState.NotScanned
                    && d.Attempts == 0 && d.ScannedSha256 == null && d.ScannedUtc == null && d.EngineVersion == null)));
        var rows = query.Filter(query.Published().Where(x => x.Kind != ContentKind.LegalDocument && x.Kind != ContentKind.DraftDocument
            && (x.CategoryId == scope || db.RevisionCategories.Any(c => c.RevisionId == x.RevisionId && c.CategoryId == scope))
            && db.ContentAssets.Any(a => a.RevisionId == x.RevisionId && a.Usage == "Attachment" && documents.Any(m => m.Id == a.MediaAssetId))), q);
        var total = await rows.CountAsync();
        var selected = await query.CardsPageAsync(PublicContentQuery.Recent(rows), (page - 1) * 10, 10);
        var revisionIds = selected.Select(x => x.RevisionId).ToArray();
        var files = await (from a in db.ContentAssets.AsNoTracking()
            join m in documents on a.MediaAssetId equals m.Id
            where revisionIds.Contains(a.RevisionId) && a.Usage == "Attachment"
            orderby a.SortOrder, m.Id
            select new { a.RevisionId, File = new AttachmentModel(m.Id, m.OriginalName, m.MimeType, m.ByteLength) }).ToListAsync();
        var items = selected.Select(x => new MaterialItem(PublicContentQuery.Card(x), files.Where(f => f.RevisionId == x.RevisionId).Select(f => f.File).ToArray()))
            .Where(x => x.Files.Count > 0).ToArray();
        return View(new MaterialsModel(items, total, page, q));
    }

    public static int? CategoryScope(SitePublicConfiguration site)
    {
        var visible = PublicNavigation.VisibleCategories(site.Revision).Select(x => x.Id).ToHashSet();
        var menuIds = site.Menus.Select(x => x.Id).ToHashSet();
        var scopes = site.Revision.Menus.Where(x => menuIds.Contains(x.Id) && x.TargetKind == "Route" && x.RouteKey == "Materials" && x.CategoryId.HasValue)
            .Select(x => x.CategoryId!.Value).Distinct().ToArray();
        return scopes.Length == 1 && visible.Contains(scopes[0]) ? scopes[0] : null;
    }
}
