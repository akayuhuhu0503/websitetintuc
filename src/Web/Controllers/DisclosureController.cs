using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.Controllers;
[Route("cong-khai-thong-tin"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DisclosureController(PortalDbContext db,PublicContentQuery query):Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()=>View(await db.DisclosureGroups.AsNoTracking().Where(x=>x.Status=="NameApproved").OrderBy(x=>x.Id).ToListAsync());
    [HttpGet("{group}")]
    public async Task<IActionResult> List(string group,string? province=null,int? year=null,string? q=null,int page=1)
    {
        var definition=await db.DisclosureGroups.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==group&&x.Status=="NameApproved");if(definition==null)return NotFound();
        if(province?.Length>200||q?.Length>200||year is <1900 or >2200||!ModelState.IsValid)return BadRequest();
        var revisions=db.Revisions.AsNoTracking().Where(r=>r.Metadata.DisclosureGroupId==group);
        if(!string.IsNullOrWhiteSpace(province)){var term=province.Trim();revisions=revisions.Where(r=>r.Metadata.ProvinceCode==term||EF.Functions.Collate(r.Metadata.Province!,"Vietnamese_100_CI_AI").Contains(term));}
        if(year!=null)revisions=revisions.Where(r=>r.Metadata.DisclosureYear==year);
        var rows=query.Filter(query.Published().Where(x=>revisions.Any(r=>r.Id==x.RevisionId)),q);page=Math.Clamp(page,1,10000);var total=await rows.CountAsync();var items=await query.CardsPageAsync(PublicContentQuery.Recent(rows),(page-1)*10,10);
        ViewData["Province"]=province;ViewData["Year"]=year;ViewData["Group"]=group;
        return View(new ListingModel{Title=definition.Name,Query=q,Page=page,Total=total,Items=items.Select(PublicContentQuery.Card).ToList()});
    }
}
