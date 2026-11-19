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
        async Task<IReadOnlyList<ContentCard>> Take(ContentKind kind, int count) => (await query.CardsPageAsync(PublicContentQuery.Recent(query.Published(requireCleanThumbnail: true).Where(x=>x.Kind==kind)),0,count,requireCleanThumbnail: true)).Select(PublicContentQuery.Card).ToList();
        var featured = await query.CardsPageAsync(query.FeaturedFirst(query.HomeArticles()),0,3,requireCleanThumbnail: true);
        var featuredIds = featured.Select(x=>x.Id).ToArray();
        var recent = await query.CardsPageAsync(PublicContentQuery.Recent(query.HomeArticles().Where(x=>!featuredIds.Contains(x.Id))),0,4,requireCleanThumbnail: true);
        var news = featured.Concat(recent).Select(PublicContentQuery.Card).ToArray();
        var notices = await Take(ContentKind.Announcement, 5);
        var documents = await Take(ContentKind.LegalDocument, 4);
        var categories=await db.Categories.AsNoTracking().Where(c=>c.SourceUrl!=""&&c.SortOrder>=0&&c.SortOrder<14)
            .OrderBy(c=>c.SortOrder).ThenBy(c=>c.Id).ToListAsync();
        var categoryRows=await query.CategoryCardsAsync(categories.Select(c=>c.Id).ToArray());
        var sections=categories.Select(c=>new CategorySection(c.Name,c.Slug,categoryRows.GetValueOrDefault(c.Id,[]).Select(PublicContentQuery.Card).ToList())).ToList();
        return View(new HomeModel { News = news, Announcements = notices, Documents = documents, Sections = sections, ImportedCount = await db.SourceRecords.CountAsync() });
    }
}
