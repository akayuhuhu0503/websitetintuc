using System.Globalization;
using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.Controllers;
public sealed record WeeklySchedulePage(int Year,int Week,WeeklyScheduleRelease? Release,ContentMetadata? Metadata);
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class WeeklyScheduleController(PortalDbContext db,CurrentPermissionService permissions):Controller
{
    [HttpGet("lich-tuan")]
    public async Task<IActionResult> Index(int? year=null,int? week=null)
    {
        var now=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime;var y=year??ISOWeek.GetYear(now);var w=week??ISOWeek.GetWeekOfYear(now);
        if(!ModelState.IsValid||y is <2000 or >2200||w<1||w>ISOWeek.GetWeeksInYear(y))return BadRequest();
        var release=await db.WeeklyScheduleReleases.AsNoTracking().Where(r=>r.IsoYear==y&&r.IsoWeek==w&&db.Contents.Any(c=>c.Id==r.ContentId&&c.IsPublic&&!c.IsTrashed&&c.PublishedRevisionId==r.RevisionId)).OrderByDescending(x=>x.Version).FirstOrDefaultAsync();
        var m=release==null?null:await db.Revisions.AsNoTracking().Where(x=>x.Id==release.RevisionId).Select(x=>x.Metadata).SingleAsync();
        return View(new WeeklySchedulePage(y,w,release,m));
    }
    [Authorize,HttpGet("admin/lich-tuan/{id:guid}")]
    public async Task<IActionResult> History(Guid id)
    {
        var actor=await permissions.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);var c=await db.Contents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id);if(c==null||!await permissions.CanReadAsync(actor,c))return NotFound();
        var scope=permissions.ScopeRevisions(db.Revisions,actor).Select(x=>x.Id);
        return View(await db.WeeklyScheduleReleases.AsNoTracking().Where(x=>x.ContentId==id&&scope.Contains(x.RevisionId)).OrderByDescending(x=>x.Version).ToListAsync());
    }
}
