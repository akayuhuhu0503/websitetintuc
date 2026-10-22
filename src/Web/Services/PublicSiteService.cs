using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Services;

public sealed record SiteNavigationItem(Guid Id, Guid? ParentId, string Position, string Label, string? Url, bool NewWindow, int SortOrder);
public sealed record SiteMenuTreeModel(IReadOnlyList<SiteNavigationItem> Items, Guid ParentId);
public sealed record SitePublicBanner(Guid Id, string Label, string Alt, string Position, string ImageUrl, string Url, bool NewWindow, int SortOrder);
public sealed record SitePublicConfiguration(SiteConfigurationRevision Revision, IReadOnlyList<SiteNavigationItem> Menus, IReadOnlyList<SitePublicBanner> Banners, string? LogoUrl, bool IsPreview);
public sealed record SiteHomeBlockResult(SiteHomeBlock Block, IReadOnlyList<ContentCard> Items, string? MoreUrl,string? ErrorReference=null);
public sealed class ConfiguredHomeModel
{
    public required SitePublicConfiguration Site { get; init; }
    public IReadOnlyList<SiteHomeBlockResult> Blocks { get; init; } = [];
}

// One immutable snapshot per request: header, navigation, homepage and footer share its revision.
// No process-local public cache. SQL live pointers are read on every request, including worker updates.
public sealed class PublicSiteService(PortalDbContext db, SiteConfigurationService configurations, PublicContentQuery query,
    MediaUploadOptions mediaOptions, IHttpContextAccessor context, TimeProvider? clock = null,ILogger<PublicSiteService>? logger=null)
{
    public const string PreviewKey = "LandPortal.SitePreview";
    private Task<SitePublicConfiguration?>? current;
    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();
    public Task<SitePublicConfiguration?> GetAsync() => current ??= LoadAsync();
    private async Task<SitePublicConfiguration?> LoadAsync()
    {
        if (context.HttpContext?.Items[PreviewKey] is SiteConfigurationRevision preview) return await BuildAsync(preview, true);
        var live = await configurations.ReadAsync(true);
        return live is null ? null : await BuildAsync(live.Revision, false);
    }
    public async Task<SitePublicConfiguration> BuildAsync(SiteConfigurationRevision r, bool preview)
    {
        var targetIds = r.Menus.Where(x => x.ContentId.HasValue).Select(x => x.ContentId!.Value)
            .Concat(r.Banners.Where(x => x.ContentId.HasValue).Select(x => x.ContentId!.Value)).Distinct().ToArray();
        var targets = targetIds.Length == 0 ? [] : await PublicContentQuery.ForDisplay(query.Published().Where(x => targetIds.Contains(x.Id))).ToListAsync();
        var urls = targets.ToDictionary(x => x.Id, x => PublicContentQuery.Card(x).Url);
        var approvedGroups = await db.DisclosureGroups.AsNoTracking().Where(x => x.Status == "NameApproved").Select(x => x.Id).ToListAsync();
        bool RouteAllowed(string kind, string key) => kind != "Route" || !key.StartsWith("Disclosure", StringComparison.Ordinal) || key == "Disclosure" ||
            SiteConfigurationService.Routes.TryGetValue(key, out var route) && approvedGroups.Contains(route.Url.Split('/').Last());
        var menus = r.Menus.Where(x => RouteAllowed(x.TargetKind, x.RouteKey)).Where(x => x.TargetKind != "Category" || r.Categories.Any(c => c.CategoryId == x.CategoryId && c.ShowInNavigation)).Select(x => new SiteNavigationItem(x.Id, x.ParentId, x.Position, x.Label,
            Resolve(x.TargetKind, x.CategoryId, x.ContentId, x.RouteKey, x.ExternalUrl, r, urls), x.OpenNewWindow, x.SortOrder))
            .Where(x => x.Url is not null || r.Menus.Any(y => y.Id == x.Id && y.TargetKind == "Group"))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
        var now = Now;
        var active = r.Banners.Where(x => (!x.StartUtc.HasValue || x.StartUtc <= now) && (!x.EndUtc.HasValue || x.EndUtc > now)).ToList();
        var assetIds = active.Select(x => x.MediaAssetId).Concat(r.LogoAssetId.HasValue ? [r.LogoAssetId.Value] : []).Distinct().ToArray();
        var assets = assetIds.Length == 0 ? [] : await db.MediaAssets.AsNoTracking().Where(x => assetIds.Contains(x.Id)).ToListAsync();
        var validIds = assets.Where(x => mediaOptions.CanServe(x) && x.MimeType.StartsWith("image/", StringComparison.Ordinal)).Select(x => x.Id).ToHashSet();
        string MediaUrl(Guid id) => preview ? "/admin/site/media/" + id : "/media/" + id;
        var banners = new List<SitePublicBanner>();
        foreach (var item in active.Where(x => validIds.Contains(x.MediaAssetId) && RouteAllowed(x.TargetKind, x.RouteKey)))
        {
            var url = Resolve(item.TargetKind, item.CategoryId, item.ContentId, item.RouteKey, item.ExternalUrl, r, urls);
            if (url is not null) banners.Add(new(item.Id, item.Label, item.Alt, item.Position, MediaUrl(item.MediaAssetId), url, item.OpenNewWindow, item.SortOrder));
        }
        return new(r, menus, banners.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList(), r.LogoAssetId.HasValue && validIds.Contains(r.LogoAssetId.Value) ? MediaUrl(r.LogoAssetId.Value) : null, preview);
    }
    public async Task<ConfiguredHomeModel> HomeAsync(SitePublicConfiguration site)
    {
        var blocks = new List<SiteHomeBlockResult>();
        foreach (var block in site.Revision.Blocks.Where(x => x.Enabled).OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
        {
            try
            {
            if (block.Type == "Links") { blocks.Add(new(block, [], "/so-tay")); continue; }
            var kind = block.Type switch { "Announcements" => ContentKind.Announcement, "LegalDocuments" => ContentKind.LegalDocument, "DraftDocuments" => ContentKind.DraftDocument, "Videos" => ContentKind.Video, _ => ContentKind.Article };
            var rows = query.Published().Where(x => x.Kind == kind);
            if(block.Type=="Videos")rows=rows.Where(x=>db.ContentAssets.Any(a=>a.RevisionId==x.RevisionId&&a.MediaAsset.MimeType=="video/mp4"&&!a.MediaAsset.IsQuarantined));
            if (block.Type == "Category") rows = rows.Where(x => db.RevisionCategories.Any(c => c.RevisionId == x.RevisionId && c.CategoryId == block.CategoryId));
            var pins = site.Revision.Pins.Where(x => x.BlockId == block.Id).OrderBy(x => x.SortOrder).ThenBy(x => x.ContentId).Select(x => x.ContentId).Distinct().Take(block.Limit).ToArray();
            var pinned = pins.Length == 0 ? [] : await PublicContentQuery.ForDisplay(rows.Where(x => pins.Contains(x.Id))).ToListAsync();
            var selected = pins.Select(id => pinned.SingleOrDefault(x => x.Id == id)).Where(x => x is not null).Select(x => x!).ToList();
            var selectedIds = selected.Select(x => x.Id).ToArray();
            if (selected.Count < block.Limit) selected.AddRange(await query.CardsPageAsync(PublicContentQuery.Recent(rows.Where(x => !selectedIds.Contains(x.Id))),0,block.Limit-selected.Count));
            var more = block.Type switch { "Announcements" => "/thong-bao", "LegalDocuments" => "/van-ban", "DraftDocuments" => "/du-thao", "Videos" => "/video", "Category" => "/chuyen-muc/" + site.Revision.Categories.Single(x => x.CategoryId == block.CategoryId).Slug, _ => "/tin-tuc" };
            blocks.Add(new(block, selected.Select(PublicContentQuery.Card).ToList(), more));
            }
            catch(Microsoft.Data.SqlClient.SqlException error)
            {
                var correlation=context.HttpContext?.TraceIdentifier??Guid.NewGuid().ToString("N");
                logger?.LogError("Public block unavailable: block {BlockId}; SQL code {SqlCode}; correlation {Correlation}. No fallback content returned.",block.Id,error.Number,correlation);
                // Core featured failure uses the normal friendly 500 route. A failed
                // supplementary block stays visibly distinct from a truly empty block.
                if(block.Type=="Featured")throw;
                blocks.Add(new(block,[],null,correlation));
            }
        }
        return new() { Site = site, Blocks = blocks };
    }
    public static async Task<bool> HasPublicAssetGrantAsync(PortalDbContext db, Guid assetId, DateTimeOffset now)
    {
        var live = db.Set<SiteConfiguration>().Where(x => x.Id == 1).Select(x => x.LiveRevisionId);
        return await db.Set<SiteConfigurationRevision>().AnyAsync(x => live.Contains(x.Id) && x.LogoAssetId == assetId)
            || await db.Set<SiteBanner>().AnyAsync(x => live.Contains(x.RevisionId) && x.MediaAssetId == assetId
                && (x.StartUtc == null || x.StartUtc <= now) && (x.EndUtc == null || x.EndUtc > now)
                && (x.TargetKind != "Content" || db.Contents.Any(c => c.Id == x.ContentId && c.IsPublic && !c.IsTrashed && c.PublishedRevisionId != null)));
    }
    private static string? Resolve(string kind, int? category, Guid? content, string route, string external, SiteConfigurationRevision r, IReadOnlyDictionary<Guid, string> urls) => kind switch
    {
        "Category" => r.Categories.FirstOrDefault(x => x.CategoryId == category) is { } c ? "/chuyen-muc/" + c.Slug : null,
        "Content" => content.HasValue && urls.TryGetValue(content.Value, out var url) ? url : null,
        "Route" => SiteConfigurationService.Routes.TryGetValue(route, out var value) ? value.Url : null,
        "External" => SiteConfigurationService.ExternalUrlValid(external) ? external : null, _ => null
    };
}
