using LandPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;
namespace LandPortal.Web.ViewComponents;
public sealed class PopularArticlesViewComponent(ArticleDiscovery discovery) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync() => View((await discovery.PopularAsync(7),await discovery.PopularAsync(30)));
}
