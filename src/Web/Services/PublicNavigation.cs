using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Services;

public sealed record PublicCategory(int Id, int? ParentId, string Name, string Slug, int SortOrder);
public sealed record CategoryDirectoryModel(IReadOnlyList<PublicCategory> Items, int? ParentId, string? ActiveSlug);
public sealed record PublicNavigationModel(IReadOnlyList<SiteNavigationItem> Items, IReadOnlyList<PublicCategory> Categories);

// Share the immutable live/preview catalog within the request. Empty categories
// belong in navigation; article existence is deliberately not queried here.
public sealed class PublicNavigation(PortalDbContext db, PublicSiteService sites)
{
    private Task<PublicNavigationModel>? current;
    public Task<PublicNavigationModel> GetAsync() => current ??= LoadAsync();

    private async Task<PublicNavigationModel> LoadAsync()
    {
        var site = await sites.GetAsync();
        if (site is not null) return Configured(site);
        // A draft-only/withdrawn site configuration must not reveal its catalog.
        if (await db.Set<SiteConfiguration>().AnyAsync()) return new([], []);
        var categories = await db.Categories.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new PublicCategory(x.Id, x.ParentId, x.Name, x.Slug, x.SortOrder)).ToListAsync();
        return Fallback(categories);
    }

    public static IReadOnlyList<PublicCategory> VisibleCategories(SiteConfigurationRevision revision)
    {
        var catalog = revision.Categories.ToDictionary(x => x.CategoryId);
        var disabled = revision.Menus.Where(x => x.TargetKind == "Category" && !x.Enabled && x.CategoryId.HasValue).Select(x => x.CategoryId!.Value).ToHashSet();
        bool Visible(SiteCategoryConfiguration category)
        {
            var seen = new HashSet<int>();
            while (true)
            {
                if (!category.ShowInNavigation || disabled.Contains(category.CategoryId) || !seen.Add(category.CategoryId)) return false;
                if (category.ParentCategoryId is not int parent) return true;
                if (!catalog.TryGetValue(parent, out category!)) return false;
            }
        }
        return revision.Categories.Where(Visible).OrderBy(x => x.SortOrder).ThenBy(x => x.CategoryId)
            .Select(x => new PublicCategory(x.CategoryId, x.ParentCategoryId, x.DisplayName, x.Slug, x.SortOrder)).ToArray();
    }

    public static PublicNavigationModel Configured(SitePublicConfiguration site)
    {
        var categories = VisibleCategories(site.Revision);
        var items = site.Menus.ToList();
        // Identify the configured News route, never infer a category from its name.
        foreach (var news in site.Menus.Where(x => x.Position == "main" && x.Url == "/tin-tuc"))
        {
            var root = news;
            if (news.ParentId is Guid parent)
            {
                var group = site.Menus.SingleOrDefault(x => x.Id == parent);
                // Do not turn a generic parent containing unrelated routes into News.
                var siblings = site.Revision.Menus.Where(x => x.ParentId == parent);
                if (group is not null && !siblings.Any(x => x.TargetKind == "Route" && x.RouteKey != "News")) root = group;
            }
            if (!items.Any(x => x.Id == root.Id)) continue;
            var descendants = new HashSet<Guid> { root.Id };
            for (var depth = 0; depth < 4; depth++)
                descendants.UnionWith(items.Where(x => x.ParentId is Guid p && descendants.Contains(p)).Select(x => x.Id).ToArray());
            items.RemoveAll(x => x.Id != root.Id && descendants.Contains(x.Id));
            items[items.FindIndex(x => x.Id == root.Id)] = root with { Url = "/tin-tuc" };
            items.AddRange(CategoryItems(categories, root.Id));
        }
        return new(items, categories);
    }

    public static PublicNavigationModel Fallback(IReadOnlyList<PublicCategory> categories)
    {
        var revision = new SiteConfigurationRevision { Categories = categories.Select(x => new SiteCategoryConfiguration
            { CategoryId = x.Id, ParentCategoryId = x.ParentId, DisplayName = x.Name, Slug = x.Slug, SortOrder = x.SortOrder }).ToList() };
        var visible = VisibleCategories(revision);
        var news = Guid.NewGuid(); var documents = Guid.NewGuid(); var introduction = Guid.NewGuid();
        var items = new List<SiteNavigationItem>();
        void Route(string key, int order, Guid? parent = null) => items.Add(new(Guid.NewGuid(), parent, "main", SiteConfigurationService.Routes[key].Label, SiteConfigurationService.Routes[key].Url, false, order));
        Route("Home", 0);
        items.Add(new(introduction, null, "main", "Giới thiệu", null, false, 1));
        Route("Leadership", 0, introduction); Route("Functions", 1, introduction); Route("Organization", 2, introduction);
        items.Add(new(news, null, "main", "Tin tức", "/tin-tuc", false, 2));
        items.AddRange(CategoryItems(visible, news));
        items.Add(new(documents, null, "main", "Văn bản", null, false, 4));
        Route("Legal", 0, documents); Route("DraftDocuments", 1, documents); Route("Materials", 2, documents);
        Route("Disclosure", 3); Route("Announcements", 5); Route("Videos", 6); Route("Weekly", 7); Route("Handbook", 8); Route("Questions", 9); Route("Contact", 10);
        return new(items, visible);
    }

    private static IEnumerable<SiteNavigationItem> CategoryItems(IReadOnlyList<PublicCategory> categories, Guid root)
    {
        var ids = categories.ToDictionary(x => x.Id, _ => Guid.NewGuid());
        return categories.Select(x => new SiteNavigationItem(ids[x.Id], x.ParentId is int parent ? ids[parent] : root,
            "main", x.Name, "/chuyen-muc/" + x.Slug, false, x.SortOrder));
    }
}
