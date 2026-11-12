using System.Globalization;
using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.Controllers;
public sealed record WeeklyScheduleVersion(int Version,DateTimeOffset PublishedUtc);
public sealed record WeeklySchedulePage(int Year,int Week,WeeklyScheduleRelease? Release,ContentMetadata? Metadata)
{
    public IReadOnlyList<WeeklyScheduleVersion> Versions {get;init;}=[];
    public IReadOnlyList<WeeklyScheduleArticle> WeekArticles {get;init;}=[];
    public IReadOnlyList<WeeklyScheduleArticle> PublishedSchedules {get;init;}=[];
    public bool ExplicitWeek {get;init;}
    public int Page {get;init;}=1;
    public int TotalPages {get;init;}=1;
    public int? RequestedVersion {get;init;}
}
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class WeeklyScheduleController(PortalDbContext db,CurrentPermissionService permissions,PublicContentQuery query):Controller
{
    [HttpGet("lich-tuan")]
    public async Task<IActionResult> Index(int? year=null,int? week=null,int? version=null,int page=1)
    {
        var now=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime;var y=year??ISOWeek.GetYear(now);var w=week??ISOWeek.GetWeekOfYear(now);
        if(!ModelState.IsValid||y is <2000 or >2200||w<1||w>ISOWeek.GetWeeksInYear(y)||version is <=0)return BadRequest();
        var publicReleases=WeeklySchedulePublication.Releases(db);
        var releases=publicReleases.Where(r=>r.IsoYear==y&&r.IsoWeek==w);
        var release=await releases.Where(r=>version==null||r.Version==version).OrderByDescending(x=>x.Version).FirstOrDefaultAsync();
        if(version!=null&&release==null)return NotFound();
        var m=release==null?null:await db.Revisions.AsNoTracking().Where(x=>x.Id==release.RevisionId).Select(x=>x.Metadata).SingleAsync();
        var versions=await releases.OrderByDescending(x=>x.Version).Select(x=>new WeeklyScheduleVersion(x.Version,x.PublishedUtc)).ToListAsync();

        // Use the same public-revision boundary as article pages and /lich-tuan/nguon.
        // Category membership may be primary or secondary. Never read a working draft.
        var headers=from row in query.Published()
                    join revision in db.Revisions.AsNoTracking() on row.RevisionId equals revision.Id
                    where row.CategoryId==76
                        || db.RevisionCategories.Any(c=>c.RevisionId==row.RevisionId&&c.CategoryId==76)
                        || publicReleases.Any(r=>r.RevisionId==row.RevisionId)
                    select new {
                        row.Id,row.Title,row.SourceDate,row.PublishedUtc,
                        Year=revision.Metadata.WeeklyYear,Week=revision.Metadata.WeeklyWeek,
                        Structured=publicReleases.Any(r=>r.RevisionId==row.RevisionId)
                    };
        var yearText=y.ToString(CultureInfo.InvariantCulture);var weekText=w.ToString(CultureInfo.InvariantCulture);
        // SQL prefilter keeps this a narrow header-only read. Exact matching below prevents week 4 matching 42.
        var candidates=await headers.Where(x=>!x.Structured&&(
                x.Year==y&&x.Week==w || x.Year==null&&x.Week==null&&x.Title.Contains(yearText)&&x.Title.Contains(weekText)))
            .OrderByDescending(x=>x.SourceDate).ThenByDescending(x=>x.PublishedUtc).ThenBy(x=>x.Id)
            .Select(x=>new WeeklyScheduleArticle(x.Id,x.Title,x.SourceDate,x.PublishedUtc,x.Year,x.Week,x.Structured)).ToListAsync();
        var weekArticles=candidates.Where(x=>x.Period is { } period&&period.Year==y&&period.Week==w).ToArray();

        // Always offer the published archive, even when the current ISO week has no structured release.
        // Only ten titles are materialized per archive page; bodies and attachment bytes are not loaded here.
        const int pageSize=10;
        var displayedIds=weekArticles.Select(x=>x.Id).ToList();
        if(release is not null)displayedIds.Add(release.ContentId);
        var archive=headers.Where(x=>!displayedIds.Contains(x.Id));
        var total=await archive.CountAsync();var totalPages=Math.Max(1,(int)Math.Ceiling(total/(double)pageSize));
        page=Math.Clamp(page,1,Math.Min(10_000,totalPages));
        var published=await archive.OrderByDescending(x=>x.SourceDate??(x.PublishedUtc==null?(DateOnly?)null:DateOnly.FromDateTime(x.PublishedUtc.Value.AddHours(7).Date)))
            .ThenByDescending(x=>x.PublishedUtc).ThenBy(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize)
            .Select(x=>new WeeklyScheduleArticle(x.Id,x.Title,x.SourceDate,x.PublishedUtc,x.Year,x.Week,x.Structured)).ToListAsync();
        return View(new WeeklySchedulePage(y,w,release,m){Versions=versions,WeekArticles=weekArticles,
            PublishedSchedules=published,ExplicitWeek=year.HasValue||week.HasValue||version.HasValue,
            Page=page,TotalPages=Math.Min(10_000,totalPages),RequestedVersion=version});
    }
    [Authorize,HttpGet("admin/lich-tuan/{id:guid}")]
    public async Task<IActionResult> History(Guid id)
    {
        var actor=await permissions.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);var c=await db.Contents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id);if(c==null||!await permissions.CanReadAsync(actor,c))return NotFound();
        var scope=permissions.ScopeRevisions(db.Revisions,actor).Select(x=>x.Id);
        return View(await db.WeeklyScheduleReleases.AsNoTracking().Where(x=>x.ContentId==id&&scope.Contains(x.RevisionId)).OrderByDescending(x=>x.Version).ToListAsync());
    }
}
