using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LandPortal.Web.Services;

namespace LandPortal.Web.ViewComponents;

public sealed class PortalNavigationViewComponent(PublicSiteService sites) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var site = await sites.GetAsync();
        if (site is not null) return View("Configured", site.Menus);
        return View("Configured",Array.Empty<SiteNavigationItem>());
    }
}
