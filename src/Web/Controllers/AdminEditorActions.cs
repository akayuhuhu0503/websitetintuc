using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

public sealed partial class AdminController
{
    [HttpPost("admin/editor-weekly/{id:guid}/{assetId:guid}")]
    public async Task<IActionResult> ImportWeeklyPreview(Guid id, Guid assetId)
    {
        var actor = await Actor(); var content = await Find(id);
        if (content == null || !actor.Has(Permissions.ManageMedia) || !await permissions.CanEditAsync(actor, content) || content.State is not (WorkflowState.Draft or WorkflowState.Rejected)) return Forbid();
        var draft = await db.ContentDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.ContentId == id);
        if (draft?.Metadata.WeeklyYear is not int year || draft.Metadata.WeeklyWeek is not int week) return BadRequest(new { message = "Chọn tuần và lưu bản nháp lịch trước khi nhập Word." });
        var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId && x.MimeType == OfficeDocumentPolicy.Docx && db.EditorialDocuments.Any(d => d.MediaAssetId == x.Id && d.ContentId == id));
        if (asset == null) return NotFound();
        await using var stream = await EditorialDocumentService.OpenVerifiedAsync(db, storage, asset, environment.IsDevelopment(), policy: mediaOptions);
        if (stream == null) return StatusCode(423, new { message = "Tài liệu chưa đạt kiểm định. Chọn Word hợp lệ đã tải cho bài này." });
        try { var preview = WeeklyDocxReader.Parse(stream, asset.OriginalName, year, week); return Json(new { rows = preview.Rows, warnings = preview.Warnings, assetId }); }
        catch (InvalidOperationException error) { return BadRequest(new { message = error.Message }); }
    }
    [HttpPost("admin/approve-publish/{id:guid}")]
    public async Task<IActionResult> ApproveAndPublish(Guid id, WorkflowModel model)
    {
        var actor = await Actor(); var content = await Find(id);
        if (content == null) return NotFound();
        if (!actor.Has(Permissions.Review) || !actor.Has(Permissions.PublishApproved) || !await permissions.CanReadAsync(actor, content)) return Forbid();
        var version = Version(model.RowVersion, true);
        if (!ModelState.IsValid || model.RequestId == Guid.Empty || model.RevisionId == null) return BadRequest(new { message = "Phiên bản cần duyệt chưa đầy đủ. Mở lại bài để kiểm tra." });
        WorkflowResult? approved = null;
        try
        {
            approved = await workflow.ApproveAsync(id, model.RevisionId.Value, actor, version!, FollowupCommand(model.RequestId, "Approve"));
            var publishCommand = FollowupCommand(model.RequestId, "Publish");
            var receipt = await db.ContentCommands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == publishCommand);
            if (receipt != null)
            {
                // ApproveAsync above has already checked the exact original request
                // fingerprint. A completed child must not be replayed with today's
                // rowversion: workflow receipts deliberately return the current state.
                if (receipt.ContentId != id || receipt.ActorUserId != actor.UserId || receipt.Action != "PublishApproved" || receipt.ResultRevisionId != model.RevisionId)
                    throw new InvalidOperationException("Mã thao tác không khớp lần xuất bản của phiên bản này.");
                var current = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == id);
                actor = await Actor();
                if (!actor.Has(Permissions.Review) || !actor.Has(Permissions.PublishApproved) || !await permissions.CanReadAsync(actor, current)) return Forbid();
                return Json(Result(new WorkflowResult(current.Id, current.CurrentRevisionId, current.PublishedRevisionId, current.RowVersion, current.State, current.IsPublic, current.IsTrashed, true, DateTimeOffset.UtcNow)));
            }
            // Each command keeps its own audit/idempotency and refreshes permissions.
            var published = await workflow.PublishApprovedAsync(id, model.RevisionId.Value, await Actor(), approved.RowVersion, publishCommand);
            return Json(Result(published));
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidOperationException or DbUpdateConcurrencyException)
        {
            return StatusCode(error is UnauthorizedAccessException ? 403 : error is DbUpdateConcurrencyException ? 409 : 400,
                new { message = (approved == null ? "Chưa duyệt bài. " : "Đã duyệt nhưng chưa xuất bản. ") + (error is UnauthorizedAccessException ? "Quyền thực hiện đã thay đổi." : error.Message), savedDraft = approved == null ? null : Result(approved) });
        }
    }

    [HttpPost("admin/editor-schedule/{id:guid}")]
    public async Task<IActionResult> ScheduleFromEditor(Guid id, ScheduleForm model, [FromServices] SchedulerService scheduler)
    {
        var actor = await Actor(); var content = await Find(id);
        if (content == null) return NotFound();
        if (!actor.Has(Permissions.Schedule) || !actor.Has(model.Direct ? Permissions.PublishDirect : Permissions.PublishApproved) || !await permissions.CanReadAsync(actor, content)) return Forbid();
        if (!ModelState.IsValid || id != model.ContentId || model.RevisionId == Guid.Empty) return BadRequest(new { message = "Chọn thời điểm đăng theo giờ Việt Nam và lưu đúng phiên bản trước khi hẹn giờ." });
        try
        {
            var due = SchedulerService.VietnamToUtc(model.DueVietnam!.Value);
            var active = await db.PublicationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.ContentId == id && x.IsActive);
            // A retry may acknowledge exactly the same owned schedule, never replace another schedule.
            if (active == null || active.RevisionId != model.RevisionId || active.DueUtc != due || active.Direct != model.Direct || active.RequestedByUserId != actor.UserId)
                active = await scheduler.ScheduleAsync(id, model.RevisionId, due, model.Direct, actor, Convert.FromBase64String(model.ContentVersion));
            else if (content.CurrentRevisionId != model.RevisionId || !content.RowVersion.SequenceEqual(Convert.FromBase64String(model.ContentVersion))) throw new DbUpdateConcurrencyException();
            return Json(new { jobId = active.Id, message = "Đã hẹn giờ đúng phiên bản đã lưu. Bạn có thể sửa hoặc hủy tại Lịch xuất bản.", url = "/admin/schedules" });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (FormatException) { return BadRequest(new { message = "Phiên bản đã thay đổi. Mở lại bài trước khi đặt lịch." }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Bài hoặc lịch đã thay đổi. Kiểm tra Lịch xuất bản; không tạo lịch trùng." }); }
        catch (InvalidOperationException error) { return BadRequest(new { message = error.Message }); }
    }

    [HttpGet("admin/editor-readiness/{id:guid}")]
    public async Task<IActionResult> EditorReadiness(Guid id, [FromServices] VideoOptions videoOptions)
    {
        var actor = await Actor(); var content = await Find(id);
        if (content == null || !await permissions.CanReadAsync(actor, content)) return NotFound();
        var errors = await workflow.InspectRequirementsAsync(id);
        var hasDraft = await db.ContentDrafts.AsNoTracking().AnyAsync(x => x.ContentId == id);
        var assets = hasDraft
            ? await db.DraftAssets.AsNoTracking().Where(x => x.ContentId == id).Select(x => new { x.MediaAssetId, x.Usage }).ToListAsync()
            : await db.ContentAssets.AsNoTracking().Where(x => x.RevisionId == content.CurrentRevisionId).Select(x => new { x.MediaAssetId, x.Usage }).ToListAsync();
        var videoIds = assets.Where(x => x.Usage == "Video").Select(x => x.MediaAssetId).ToArray();
        var videos = await db.Set<VideoFile>().AsNoTracking().Include(x => x.Original).Where(x => videoIds.Contains(x.MediaAssetId)).ToListAsync();
        // Originals remain quarantined even after a video is ready: only its verified derivatives are served.
        // This inexpensive advisory reads proof metadata; publication still verifies every byte itself.
        var missingVideo = content.Kind == ContentKind.Video && videoIds.Length == 0;
        var waitingVideo = missingVideo || videos.Count != videoIds.Distinct().Count() || videos.Any(x => !VideoReadiness.HasProof(x, environment.IsDevelopment(), videoOptions));
        var otherIds = assets.Where(x => x.Usage != "Video").Select(x => x.MediaAssetId).ToArray();
        var blocked = await db.MediaAssets.AsNoTracking().AnyAsync(x => otherIds.Contains(x.Id) && x.IsQuarantined);
        return Json(new { errors, fileNotice = missingVideo ? "Chọn tệp trong khối Video và chờ xử lý trước khi xuất bản." : waitingVideo ? "Video chưa xử lý xong. Chờ kết quả trong khối Video trước khi xuất bản." : blocked ? "Có tệp đang cách ly hoặc chờ kiểm định. Kiểm tra tệp đính kèm trước khi xuất bản." : "Máy chủ sẽ kiểm tra lại tệp, quyền và phiên bản khi phát hành.", canSubmit = errors.Count == 0, canPublish = errors.Count == 0 && !waitingVideo && !blocked });
    }
}
