using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

// One explicit fourth attempt, not a reset of the automatic three-attempt budget.
// The durable Pending marker is consumed by the existing exclusive worker lease.
public sealed class VideoTimeoutRecovery(PortalDbContext db, StorageOptions storage, VideoOptions options,
    CurrentPermissionService permissions, TimeProvider? clock = null)
{
    public const string Authorized = "VIDEO_TIMEOUT_RETRY_AUTHORIZED";
    public const string AuditAction = "video.processing.timeout-retry";

    public async Task QueueAsync(Guid assetId, string userId, CancellationToken ct = default)
    {
        var actor = await permissions.GetAsync(userId);
        if (!actor.Has(Permissions.ManageSettings) || !actor.Has(Permissions.ManageVideos) || !actor.Has(Permissions.ManageMedia))
            throw new MediaUploadException("Chỉ quản trị hệ thống có quyền quản lý video và tệp được xử lý lại lỗi hết thời gian.", 403);
        if (!options.Enabled || options.ProcessSeconds < VideoOptions.DefaultProcessSeconds)
            throw new MediaUploadException("Cần bản Publish có giới hạn chuyển mã 1.800 giây trước khi xử lý lại. Không sửa JSON của gói đang chạy.", 409);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Serialize double clicks; lock ordering agrees with CMS attachment (content before video).
        // Identical uploads can share an asset. Use the job's canonical session,
        // never Single(MediaAssetId), which is not a unique session relationship.
        var session = await (from v in db.Set<VideoFile>().AsNoTracking()
            join s in db.Set<VideoUploadSession>().AsNoTracking() on v.UploadSessionId equals s.Id
            where v.MediaAssetId == assetId select s).SingleOrDefaultAsync(ct)
            ?? throw new MediaUploadException("Không tìm thấy phiên tải video đã hoàn tất.", 404);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM dbo.Contents WITH (UPDLOCK, ROWLOCK) WHERE Id={session.ContentId}", ct);
        var uploads = new VideoUploadService(db, storage, options, permissions, clock);
        await uploads.AuthorizeAsync(userId, session.ContentId, true, ct);
        // Recheck the original uploader too; administrator recovery cannot bypass a revoked owner.
        await uploads.AuthorizeAsync(session.OwnerUserId, session.ContentId, false, ct);
        var file = await db.Set<VideoFile>().Include(x => x.Original).SingleOrDefaultAsync(x => x.MediaAssetId == assetId, ct)
            ?? throw new MediaUploadException("Không tìm thấy tệp video.", 404);
        if (file.UploadSessionId != session.Id || session.State != VideoUploadState.Completed ||
            file.State != VideoState.Failed || file.Attempts != 3 || file.Error != "VIDEO_PROCESSOR_TIMEOUT" ||
            file.LeaseToken != null || file.LeaseUntilUtc != null)
            throw new MediaUploadException("Chỉ video thất bại do hết thời gian sau đúng 3 lượt mới được thêm một lượt. Tải lại trạng thái để đối chiếu.", 409);
        if (file.Original.ScanStatus == "Infected" ||
            (file.Original.ValidationStatus == VideoOptions.NoAvPending && !options.ApprovedManagedVideoWithoutAv))
            throw new MediaUploadException("Chính sách kiểm định hiện tại không cho xử lý lại video này.", 409);
        var path = MediaService.ResolveMediaPath(storage, file.Original.StorageKey);
        await using (var original = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            if (original.Length != file.Original.ByteLength || original.Length > options.MaxFileBytes ||
                session.Sha256 != file.Original.Sha256 || session.ExpectedBytes != original.Length)
                throw new MediaUploadException("Bản gốc không khớp phiên tải; dừng để đối soát, không tải hoặc gắn thay tệp.", 409);
        // Full SHA, quota, current permissions, scanner/no-AV and transcode validation remain in VideoWorker.
        file.State = VideoState.Pending;
        file.Error = Authorized;
        file.AvailableUtc = (clock ?? TimeProvider.System).GetUtcNow();
        db.AuditLogs.Add(new() { Actor = userId, Action = AuditAction, ContentId = session.ContentId,
            Detail = JsonSerializer.Serialize(new { AssetId = assetId, session.Id, PreviousError = "VIDEO_PROCESSOR_TIMEOUT",
                PreviousAttempts = file.Attempts, MaximumAttempts = 4, options.ProcessSeconds, options.Threads,
                file.Original.Sha256, file.Original.ByteLength, Utc = file.AvailableUtc }) });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
