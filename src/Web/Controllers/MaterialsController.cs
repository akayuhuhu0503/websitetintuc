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
    public async Task<IActionResult> Index(string? q, int page = 1, int? categoryId = null)
    {
        if (q?.Length > 200) return BadRequest();
        page = Math.Clamp(page, 1, 10000);
        var site = await sites.GetAsync();
        var scope = site is null ? null : CategoryScope(site);
        if (scope is null) return View(new MaterialsModel([], 0, page, q));
        // A deliberately configured catalog scope, never all attachments or an
        // assumed ID 24. Only the current published revision supplies documents.
        var documents = db.MediaAssets.AsNoTracking().Where(x => !x.IsQuarantined && x.Origin == "EditorialDocument"
            && (x.MimeType == "application/pdf" || x.MimeType == OfficeDocumentPolicy.Docx || x.MimeType == OfficeDocumentPolicy.Xlsx || x.MimeType == EditorialZipPolicy.Mime)
            && (x.ScanStatus == "Clean" && x.ValidationStatus == "Validated"
                || policy.ApprovedManagedDocumentsWithoutAv && x.MimeType != EditorialZipPolicy.Mime && x.ScanStatus == "NotScanned" && x.ValidationStatus == MediaUploadOptions.ManagedDocumentNoAvValidation
                || policy.NativeUatUploads && (x.MimeType == "application/pdf" || x.MimeType == EditorialZipPolicy.Mime) && x.ScanStatus == "NotScanned" && x.ValidationStatus == TrustedInternalDocumentPolicy.Validation)
            && db.Set<EditorialDocument>().Any(d => d.MediaAssetId == x.Id &&
                (x.ScanStatus == "Clean" && d.ScanState == InquiryScanState.Clean && d.ScannedSha256 == x.Sha256 && d.ScannedUtc != null
                    && d.EngineVersion != null && d.EngineVersion != "" && (policy.IsDevelopmentLoopback || !d.EngineVersion.StartsWith("Defender/"))
                || (policy.ApprovedManagedDocumentsWithoutAv && x.MimeType != EditorialZipPolicy.Mime && x.ValidationStatus == MediaUploadOptions.ManagedDocumentNoAvValidation
                    || policy.NativeUatUploads && (x.MimeType == "application/pdf" || x.MimeType == EditorialZipPolicy.Mime) && x.ValidationStatus == TrustedInternalDocumentPolicy.Validation
                        && db.AuditLogs.Any(a => a.Action == TrustedInternalDocumentPolicy.GrantAction && a.Actor == d.OwnerId && a.Actor == x.UploadedByUserId && a.ContentId == d.ContentId
                            && a.Detail == x.Sha256 + "|" + x.StorageKey + "|" + x.MimeType + "|" + x.Id.ToString()))
                    && x.ScanStatus == "NotScanned" && d.ScanState == InquiryScanState.NotScanned
                    && d.Attempts == 0 && d.ScannedSha256 == null && d.ScannedUtc == null && d.EngineVersion == null)));
        var catalog = query.Published().Where(x => x.Kind != ContentKind.LegalDocument && x.Kind != ContentKind.DraftDocument
            && (x.CategoryId == scope || db.RevisionCategories.Any(c => c.RevisionId == x.RevisionId && c.CategoryId == scope))
            && db.ContentAssets.Any(a => a.RevisionId == x.RevisionId && a.Usage == "Attachment" && documents.Any(m => m.Id == a.MediaAssetId)));
        // Discover labels only from the live catalog and eligible published revisions.
        // A category filter narrows the Materials scope; it never replaces it.
        var usedIds = await catalog.Select(x => x.CategoryId).Distinct().ToListAsync();
        var secondaryIds = await db.RevisionCategories.AsNoTracking()
            .Where(c => catalog.Any(x => x.RevisionId == c.RevisionId)).Select(c => c.CategoryId).Distinct().ToListAsync();
        var categories = FilterCategories(site!, scope.Value, usedIds.Where(x => x.HasValue).Select(x => x!.Value).Concat(secondaryIds));
        if (categoryId.HasValue && !categories.Any(x => x.Id == categoryId)) return NotFound();
        if (categoryId.HasValue)
            catalog = catalog.Where(x => x.CategoryId == categoryId || db.RevisionCategories.Any(c => c.RevisionId == x.RevisionId && c.CategoryId == categoryId));
        var rows = query.Filter(catalog, q);
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
        return View(new MaterialsModel(items, total, page, q) { CategoryId = categoryId, Categories = categories });
    }

    public static IReadOnlyList<EditorCategory> FilterCategories(SitePublicConfiguration site, int scope, IEnumerable<int> publishedCategoryIds)
    {
        var visible = PublicNavigation.VisibleCategories(site.Revision);
        var selected = publishedCategoryIds.ToHashSet();
        var descendants = new HashSet<int> { scope };
        // Include configured children even before their first publication, but
        // never expose a draft/hidden category or an unrelated empty catalog.
        while (true)
        {
            var additions = visible.Where(x => x.ParentId.HasValue && descendants.Contains(x.ParentId.Value)).Select(x => x.Id).ToArray();
            var before = descendants.Count; descendants.UnionWith(additions);
            if (before == descendants.Count) break;
        }
        selected.UnionWith(descendants);
        return visible.Where(x => x.Id != scope && selected.Contains(x.Id)).Select(x => new EditorCategory(x.Id, x.Name)).ToArray();
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
