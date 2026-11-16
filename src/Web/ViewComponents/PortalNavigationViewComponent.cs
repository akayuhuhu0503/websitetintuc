using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LandPortal.Web.Services;

namespace LandPortal.Web.ViewComponents;

public sealed class PortalNavigationViewComponent(PublicNavigation navigation) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        return View("Configured", (await navigation.GetAsync()).Items);
    }
}
