using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.Controllers;

[Authorize(Policy = Permissions.Schedule)]
public sealed class AdminSchedulerController(PortalDbContext db, CurrentPermissionService permissions, SchedulerService scheduler) : Controller
{
    private Task<ContentActor> Actor() => permissions.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
    [HttpGet("admin/schedules")]
    public async Task<IActionResult> Index(PublicationJobState? state, ContentKind? kind, int page = 1)
    {
        if ((state != null && !Enum.IsDefined(state.Value)) || (kind != null && !Enum.IsDefined(kind.Value))) return BadRequest();
        var actor = await Actor();
        var rows = from j in db.PublicationJobs.AsNoTracking()
                   join c in permissions.Scope(db.Contents.AsNoTracking(),actor) on j.ContentId equals c.Id
                   join r in permissions.ScopeRevisions(db.Revisions.AsNoTracking(),actor) on j.RevisionId equals r.Id
                   where actor.Has(Permissions.ViewAll) || actor.Has(Permissions.EditAll) || (c.OwnerUserId == actor.UserId &&
                     (actor.Has(Permissions.ViewOwn) || actor.Has(Permissions.EditOwn) || actor.Has(Permissions.Create))) ||
                     (actor.Has(Permissions.Review) && c.SubmittedRevisionId != null) || (actor.Has(Permissions.PublishApproved) && c.ApprovedRevisionId != null)
                   select new ScheduleRow { Id=j.Id, ContentId=j.ContentId, RevisionId=j.RevisionId, Title=r.Title, Kind=c.Kind,
                       DueUtc=j.DueUtc, State=j.State, RequestedByUserId=j.RequestedByUserId, Direct=j.Direct,
                       Attempts=j.AttemptCount, FailureReason=j.FailureReason, CompletedUtc=j.CompletedUtc, RowVersion=j.RowVersion };
        if (state != null) rows = rows.Where(x => x.State == state);
        if (kind != null) rows = rows.Where(x => x.Kind == kind);
        page = Math.Clamp(page, 1, 10000); var total = await rows.CountAsync();
        var data = await rows.OrderByDescending(x => x.DueUtc).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20).ToListAsync();
        var heartbeat = await db.WorkerHeartbeats.Select(x => (DateTimeOffset?)x.LastSeenUtc).MaxAsync();
        return View(new ScheduleList(data, page, total, state, kind, heartbeat, DateTimeOffset.UtcNow));
    }
    [HttpGet("admin/schedules/edit")]
    public async Task<IActionResult> Edit(Guid contentId, Guid? jobId)
    {
        var actor = await Actor(); var content = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contentId);
        if (content == null) return NotFound(); if (!await permissions.CanReadAsync(actor, content)) return Forbid();
        PublicationJob? job = null;
        if (jobId != null) job = await db.PublicationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId && x.ContentId == contentId);
        else job = await db.PublicationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.ContentId == contentId && x.IsActive);
        if (jobId != null && job == null) return NotFound();
        if (job?.State == PublicationJobState.Completed) return BadRequest("Lịch đã hoàn tất không thể chỉnh sửa.");
        var model = new ScheduleForm { ContentId = content.Id, RevisionId = content.CurrentRevisionId ?? Guid.Empty, ContentVersion = Convert.ToBase64String(content.RowVersion),
            ReplaceJobId = job?.Id, JobVersion = job == null ? null : Convert.ToBase64String(job.RowVersion),
            DueVietnam = SchedulerService.ToVietnam(job?.DueUtc ?? DateTimeOffset.UtcNow.AddMinutes(10)), Direct = job?.Direct ?? !actor.Has(Permissions.PublishApproved) };
        await Populate(model, actor); return View(model);
    }
    [HttpPost("admin/schedules/save")]
    public async Task<IActionResult> Save(ScheduleForm model)
    {
        var actor = await Actor(); var content = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == model.ContentId);
        if (content == null) return NotFound(); if (!await permissions.CanReadAsync(actor, content)) return Forbid();
        if (ModelState.IsValid)
        {
            try
            {
                if (model.RevisionId == Guid.Empty) throw new InvalidOperationException("Lưu nội dung thành phiên bản trước khi đặt lịch.");
                await scheduler.ScheduleAsync(model.ContentId, model.RevisionId, SchedulerService.VietnamToUtc(model.DueVietnam!.Value), model.Direct, actor,
                    Convert.FromBase64String(model.ContentVersion), model.ReplaceJobId, model.JobVersion == null ? null : Convert.FromBase64String(model.JobVersion));
                TempData["Success"] = "Đã lưu lịch vào SQL. Tác vụ nền kiểm tra lại quyền, phiên bản và tài nguyên khi thực thi."; return Redirect("/admin/schedules");
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (DbUpdateConcurrencyException) { Response.StatusCode = 409; ModelState.AddModelError("", "Nội dung hoặc lịch đã thay đổi. Mở lại màn hình để xác nhận phiên bản mới."); }
            catch (FormatException) { Response.StatusCode = 400; ModelState.AddModelError("", "Mã phiên bản không hợp lệ."); }
            catch (InvalidOperationException error) { ModelState.AddModelError("", error.Message); }
        }
        await Populate(model, actor); return View("Edit", model);
    }
    [HttpPost("admin/schedules/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 100) return BadRequest();
        try { var result = await scheduler.CancelAsync(id, await Actor(), Convert.FromBase64String(version)); TempData["Success"] = result.State == "Completed" ? result.Reason : "Trạng thái lịch: " + LandPortal.Web.Services.AdminLabels.State(result.State) + ". Bản đang công khai được giữ nguyên."; return Redirect("/admin/schedules"); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (FormatException) { return BadRequest(); }
        catch (DbUpdateConcurrencyException) { return Conflict("Lịch đã thay đổi; đọc lại trước khi hủy."); }
        catch (InvalidOperationException) { return BadRequest("Lịch không còn hợp lệ."); }
    }
    private async Task Populate(ScheduleForm model, ContentActor actor)
    {
        model.Title = await permissions.ScopeRevisions(db.Revisions.AsNoTracking(),actor).Where(x => x.Id == model.RevisionId && x.ContentId == model.ContentId).Select(x => x.Title).SingleOrDefaultAsync() ?? "(Chưa có phiên bản)";
        model.CanDirect = actor.Has(Permissions.PublishDirect); model.CanApproved = actor.Has(Permissions.PublishApproved);
        model.CatchUpMinutes = scheduler.CatchUpMinutes;
    }
}
