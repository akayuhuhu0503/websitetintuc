using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.ViewComponents;
public sealed class LandStatisticsViewComponent(PortalDbContext db):ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        try{return View(await db.Set<LandStatisticsVersion>().AsNoTracking().Where(x=>x.IsCurrent).OrderByDescending(x=>x.Year).FirstOrDefaultAsync());}
        catch(Microsoft.Data.SqlClient.SqlException e)when(e.Number==208){return Content("");}
    }
}
