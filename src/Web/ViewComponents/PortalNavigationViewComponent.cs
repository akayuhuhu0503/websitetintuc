using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LandPortal.Web.Services;

namespace LandPortal.Web.ViewComponents;

public sealed class PortalNavigationViewComponent(PortalDbContext db, PublicSiteService sites) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var site = await sites.GetAsync();
        if (site is not null) return View("Configured", site.Menus);
        var categories = await db.Categories.AsNoTracking().Where(x => x.SortOrder < 14).OrderBy(x => x.SortOrder).Select(x => new MenuCategory(x.Name, x.Slug)).ToListAsync();
        return View(categories);
    }
}
