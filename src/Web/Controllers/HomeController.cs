using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HomeController(PortalDbContext db, PublicContentQuery query, PublicSiteService sites) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        var site = await sites.GetAsync();
        if (site is not null) return View("Configured", await sites.HomeAsync(site));
        async Task<IReadOnlyList<ContentCard>> Take(ContentKind kind, int count) => (await query.CardsPageAsync(PublicContentQuery.Recent(query.Published().Where(x=>x.Kind==kind)),0,count)).Select(PublicContentQuery.Card).ToList();
        var news = await Take(ContentKind.Article, 4);
        var notices = await Take(ContentKind.Announcement, 5);
        var documents = await Take(ContentKind.LegalDocument, 4);
        var videos = await Take(ContentKind.Video, 2);
        // One bounded query for section candidates avoids fetching full article bodies or per-category queries.
        var categoryRows = await query.CardsPageAsync(PublicContentQuery.Recent(query.Published().Where(x=>x.Kind==ContentKind.Article&&x.CategoryId!=null)),0,60);
        var sections = categoryRows.GroupBy(x => new { x.CategoryName, x.CategorySlug }).Take(6).Select(g => new CategorySection(g.Key.CategoryName!, g.Key.CategorySlug!, g.Take(3).Select(PublicContentQuery.Card).ToList())).ToList();
        return View(new HomeModel { News = news, Announcements = notices, Documents = documents, Videos = videos, Sections = sections, ImportedCount = await db.SourceRecords.CountAsync() });
    }
}
