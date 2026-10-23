using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

public sealed record AuditRow(long Id,string Actor,string Action,Guid? ContentId,Guid? RevisionId,DateTimeOffset CreatedUtc);
public sealed record AuditPage(IReadOnlyList<AuditRow> Items,int Total,int Page,string? Action,DateOnly? From,DateOnly? To)
{
    public IReadOnlyList<string> EventCodes {get;init;}=[];
}

[Authorize,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class AuditController(PortalDbContext db,CurrentPermissionService permissions):Controller
{
    // Existing global administrator capabilities; no new permission or auto-grant.
    public static bool CanRead(ContentActor actor)=>actor.CategoryIds==null&&actor.Has(Permissions.ManageUsers)&&actor.Has(Permissions.ManageRoles);
    [HttpGet("admin/audit")]
    public async Task<IActionResult> Index(string? eventCode=null,DateOnly? from=null,DateOnly? to=null,int page=1)
    {
        var actor=await permissions.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if(!CanRead(actor))return StatusCode(403);
        if(!ModelState.IsValid||eventCode?.Length>120||from>to||from==DateOnly.MinValue||to==DateOnly.MinValue||to==DateOnly.MaxValue)return BadRequest("Bộ lọc không hợp lệ.");
        page=Math.Clamp(page,1,10000);
        var rows=db.AuditLogs.AsNoTracking();
        if(!string.IsNullOrWhiteSpace(eventCode))rows=rows.Where(x=>x.Action==eventCode.Trim());
        if(from is DateOnly first){var start=new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(7));rows=rows.Where(x=>x.CreatedUtc>=start);}
        if(to is DateOnly last){var end=new DateTimeOffset(last.AddDays(1).ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(7));rows=rows.Where(x=>x.CreatedUtc<end);}
        var total=await rows.CountAsync();
        // Detail can contain private workflow reasons. This viewer never selects
        // that column, nor joins identity secrets or public-submission originals.
        var items=await rows.OrderByDescending(x=>x.CreatedUtc).ThenByDescending(x=>x.Id).Skip((page-1)*50).Take(50)
            .Select(x=>new AuditRow(x.Id,x.Actor,x.Action,x.ContentId,x.RevisionId,x.CreatedUtc)).ToListAsync();
        var events=await db.AuditLogs.AsNoTracking().Select(x=>x.Action).Distinct().OrderBy(x=>x).Take(200).ToListAsync();
        return View(new AuditPage(items,total,page,eventCode,from,to){EventCodes=events});
    }
}
