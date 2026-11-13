using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

public sealed record VideoDraftAttachmentResult(WorkflowResult Workflow, Guid? AssetId);

// Existing upload intent + workflow command receipt form the durable queue.
// No browser session, new schema, permission grant or publication is involved.
public sealed class VideoDraftAttachmentService(PortalDbContext db, StorageOptions storage, VideoOptions options,
    CurrentPermissionService permissions, WorkflowService workflow, bool development)
{
    public const string Blocked = "video.editor.attach.blocked";
    public const string Removed = "video.editor.attach.removed";
    internal static readonly string[] DraftBoundaryActions = ["BeginEdit", "Submit", "Approve", "Reject", "PublishApproved", "PublishDirect", "Unpublish", "Trash", "RestoreTrash", "RestoreRevision"];

    public async Task<VideoDraftAttachmentResult> AttachAsync(Guid sessionId, string user, byte[]? expectedVersion = null, CancellationToken ct = default)
    {
        var upload = new VideoUploadService(db, storage, options, permissions);
        var session = await upload.GetAsync(sessionId, user, true, ct);
        if (session.State != VideoUploadState.Completed || session.MediaAssetId == null) throw new MediaUploadException("Video chưa hoàn tất tải cho bản nháp này.", 409);
        var intent = sessionId.ToString("D");
        if (!await db.AuditLogs.AnyAsync(x => x.ContentId == session.ContentId && x.Actor == user && x.Action == VideoUploadService.EditorAttachRequested && x.Detail == intent, ct))
            throw new MediaUploadException("Lần tải này chưa yêu cầu gắn tự động; chọn video qua bộ chọn đã có.", 409);
        var actor = await upload.AuthorizeAsync(user, session.ContentId, true, ct);
        var content = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == session.ContentId, ct);
        if (expectedVersion != null && (expectedVersion.Length != 8 || !content.RowVersion.SequenceEqual(expectedVersion))) throw new DbUpdateConcurrencyException();
        var receipt = await db.ContentCommands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        var historical = await db.ContentAssets.AnyAsync(x => x.MediaAssetId == session.MediaAssetId && db.Revisions.Any(r => r.Id == x.RevisionId && r.ContentId == content.Id), ct);
        var removed = await db.AuditLogs.AnyAsync(x => x.ContentId == content.Id && x.Action == Removed && x.Detail == session.MediaAssetId.Value.ToString("D"), ct);
        if (receipt != null || historical || removed)
        {
            if (receipt != null && (receipt.ContentId != content.Id || receipt.ActorUserId != user || receipt.Action is not ("SaveDraft" or "AttachReadyVideo")))
                throw new MediaUploadException("Mã thao tác không khớp bản nháp.", 409);
            var attached = await db.DraftAssets.AnyAsync(x => x.ContentId == content.Id && x.MediaAssetId == session.MediaAssetId && x.Usage == "Video", ct);
            return new(new(content.Id, content.CurrentRevisionId, content.PublishedRevisionId, content.RowVersion, content.State, content.IsPublic, content.IsTrashed, true, DateTimeOffset.UtcNow), attached ? session.MediaAssetId : null);
        }
        var saved = await workflow.AttachReadyVideoAsync(content.Id, sessionId, actor, expectedVersion ?? content.RowVersion, development, ct);
        var stillAttached = !saved.AlreadyApplied || await db.DraftAssets.AnyAsync(x => x.ContentId == content.Id && x.MediaAssetId == session.MediaAssetId && x.Usage == "Video", ct);
        return new(saved, stillAttached ? session.MediaAssetId : null);
    }

    // Bounded poll recovers Ready-before-commit/crash cases even when tools/scanner
    // are offline now. An invalid intent is recorded once, not retried forever or
    // allowed to starve later uploads. Explicit CMS retry still rechecks all guards.
    public async Task<int> ProcessReadyAsync(CancellationToken ct = default, Guid? onlyAsset = null)
    {
        if (!options.Enabled) return 0;
        var pending = await (from s in db.Set<VideoUploadSession>().AsNoTracking()
            join v in db.Set<VideoFile>().AsNoTracking() on s.Id equals v.UploadSessionId
            where s.State == VideoUploadState.Completed && s.MediaAssetId == v.MediaAssetId && v.State == VideoState.Ready
                && (onlyAsset == null || v.MediaAssetId == onlyAsset)
                && !db.ContentCommands.Any(x => x.Id == s.Id)
                && !db.ContentAssets.Any(x => x.MediaAssetId == s.MediaAssetId && db.Revisions.Any(r => r.Id == x.RevisionId && r.ContentId == s.ContentId))
                && !db.AuditLogs.Any(x => x.ContentId == s.ContentId && x.Action == Removed && x.Detail == v.MediaAssetId.ToString())
                && db.AuditLogs.Any(x => x.ContentId == s.ContentId && x.Actor == s.OwnerUserId && x.Action == VideoUploadService.EditorAttachRequested && x.Detail == s.Id.ToString())
                && !db.AuditLogs.Any(x => x.ContentId == s.ContentId && x.Action == Blocked && x.Detail == s.Id.ToString())
            orderby v.ReadyUtc, s.Id
            select new { s.Id, s.OwnerUserId, s.ContentId }).Take(16).ToListAsync(ct);
        var attached = 0;
        foreach (var item in pending)
        {
            ct.ThrowIfCancellationRequested(); db.ChangeTracker.Clear();
            try
            {
                var result = await AttachAsync(item.Id, item.OwnerUserId, ct: ct);
                if (!result.Workflow.AlreadyApplied) attached++;
            }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); /* Next poll reads the newest version. */ }
            catch (Exception error) when (error is MediaUploadException or UnauthorizedAccessException)
            {
                db.ChangeTracker.Clear(); var intent = item.Id.ToString("D");
                if (!await db.AuditLogs.AnyAsync(x => x.ContentId == item.ContentId && x.Action == Blocked && x.Detail == intent, ct))
                {
                    db.AuditLogs.Add(new() { Actor = item.OwnerUserId, ContentId = item.ContentId, Action = Blocked, Detail = intent });
                    await db.SaveChangesAsync(ct);
                }
            }
        }
        return attached;
    }
}
