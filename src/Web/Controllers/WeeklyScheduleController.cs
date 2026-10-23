using System.Globalization;
using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.Controllers;
public sealed record WeeklyScheduleVersion(int Version,DateTimeOffset PublishedUtc);
public sealed record WeeklySchedulePage(int Year,int Week,WeeklyScheduleRelease? Release,ContentMetadata? Metadata)
{
    public IReadOnlyList<WeeklyScheduleVersion> Versions {get;init;}=[];
}
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class WeeklyScheduleController(PortalDbContext db,CurrentPermissionService permissions):Controller
{
    [HttpGet("lich-tuan")]
    public async Task<IActionResult> Index(int? year=null,int? week=null,int? version=null)
    {
        var now=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime;var y=year??ISOWeek.GetYear(now);var w=week??ISOWeek.GetWeekOfYear(now);
        if(!ModelState.IsValid||y is <2000 or >2200||w<1||w>ISOWeek.GetWeeksInYear(y)||version is <=0)return BadRequest();
        var releases=WeeklySchedulePublication.Releases(db).Where(r=>r.IsoYear==y&&r.IsoWeek==w);
        var release=await releases.Where(r=>version==null||r.Version==version).OrderByDescending(x=>x.Version).FirstOrDefaultAsync();
        if(version!=null&&release==null)return NotFound();
        var m=release==null?null:await db.Revisions.AsNoTracking().Where(x=>x.Id==release.RevisionId).Select(x=>x.Metadata).SingleAsync();
        var versions=await releases.OrderByDescending(x=>x.Version).Select(x=>new WeeklyScheduleVersion(x.Version,x.PublishedUtc)).ToListAsync();
        return View(new WeeklySchedulePage(y,w,release,m){Versions=versions});
    }
    [Authorize,HttpGet("admin/lich-tuan/{id:guid}")]
    public async Task<IActionResult> History(Guid id)
    {
        var actor=await permissions.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);var c=await db.Contents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id);if(c==null||!await permissions.CanReadAsync(actor,c))return NotFound();
        var scope=permissions.ScopeRevisions(db.Revisions,actor).Select(x=>x.Id);
        return View(await db.WeeklyScheduleReleases.AsNoTracking().Where(x=>x.ContentId==id&&scope.Contains(x.RevisionId)).OrderByDescending(x=>x.Version).ToListAsync());
    }
}
