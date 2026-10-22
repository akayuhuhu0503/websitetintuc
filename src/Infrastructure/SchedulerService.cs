using System.Data;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Infrastructure;

public sealed class SchedulerOptions
{
    public int PollSeconds { get; init; } = 5;
    public int LeaseSeconds { get; init; } = 60;
    public int CatchUpMinutes { get; init; } = 15;
    public int MaxAttempts { get; init; } = 4;
    public static SchedulerOptions FromConfiguration(IConfiguration c) => new()
    {
        PollSeconds = Read(c, "PollSeconds", 5, 1, 60), LeaseSeconds = Read(c, "LeaseSeconds", 60, 15, 600),
        CatchUpMinutes = Read(c, "CatchUpMinutes", 15, 1, 1440), MaxAttempts = Read(c, "MaxAttempts", 4, 1, 10)
    };
    private static int Read(IConfiguration c, string key, int fallback, int min, int max) =>
        int.TryParse(c["Scheduler:" + key], out var value) && value >= min && value <= max ? value : fallback;
}
public sealed record PublicationLease(Guid JobId, Guid ContentId, long Generation, Guid Token, string WorkerId);
public sealed record SchedulerOutcome(string State, string Reason = "");

/// <summary>SQL is the queue and publication fence. A lease is never an authorization grant.</summary>
public sealed class SchedulerService(PortalDbContext db, WorkflowService workflow, CurrentPermissionService permissions,
    SchedulerOptions? options = null, TimeProvider? clock = null)
{
    private readonly SchedulerOptions settings = options ?? new();
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public int CatchUpMinutes => settings.CatchUpMinutes;
    public static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();
    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        var id = OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Ho_Chi_Minh";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
        if (zone.BaseUtcOffset != TimeSpan.FromHours(7)) throw new InvalidOperationException("Vietnam timezone mapping must resolve UTC+07:00 on this host.");
        return zone;
    }
    public static DateTimeOffset VietnamToUtc(DateTime local) => new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).ToUniversalTime();
    public static DateTime ToVietnam(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, VietnamTimeZone).DateTime;

    public async Task<PublicationJob> ScheduleAsync(Guid contentId, Guid revisionId, DateTimeOffset dueUtc, bool direct,
        ContentActor actor, byte[] contentVersion, Guid? replaceJobId = null, byte[]? replaceVersion = null)
    {
        Require(actor, Permissions.Schedule);
        var now = time.GetUtcNow(); dueUtc = dueUtc.ToUniversalTime();
        if (dueUtc <= now) throw new InvalidOperationException("Chọn thời điểm tương lai theo giờ Việt Nam; không tự đăng ngay lịch trong quá khứ.");
        if (dueUtc > now.AddYears(2)) throw new InvalidOperationException("Lịch vượt quá giới hạn hai năm.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var content = await LockContent(contentId);
            if (!content.RowVersion.SequenceEqual(contentVersion)) throw new DbUpdateConcurrencyException("Nội dung đã thay đổi. Đọc lại phiên bản trước khi hẹn giờ.");
            var currentActor = await permissions.GetAsync(actor.UserId);
            Require(currentActor, Permissions.Schedule);
            Require(currentActor, direct ? Permissions.PublishDirect : Permissions.PublishApproved);
            if (!await permissions.CanReadAsync(currentActor, content)) throw new UnauthorizedAccessException();
            if (content.IsTrashed || content.CurrentRevisionId != revisionId) throw new InvalidOperationException("Chỉ hẹn giờ đúng phiên bản hiện tại, ngoài thùng rác.");
            var revision = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == revisionId && x.ContentId == contentId);
            string? reviewer = null;
            if (!direct)
            {
                if (content.State != WorkflowState.Approved || content.ApprovedRevisionId != revisionId || content.ApprovalHash != revision.ContentHash || string.IsNullOrEmpty(content.ApprovedByUserId))
                    throw new InvalidOperationException("Phiên bản phải được duyệt đúng ID/hash và có người duyệt.");
                reviewer = content.ApprovedByUserId;
                var reviewActor=await permissions.GetAsync(reviewer);
                if (!reviewActor.Has(Permissions.Review)||!await permissions.CanAccessContentAsync(reviewActor,content.Id)) throw new InvalidOperationException("Người duyệt không còn quyền hoặc phạm vi chuyên mục.");
            }
            else if (content.State is not (WorkflowState.Draft or WorkflowState.Rejected or WorkflowState.PendingReview or WorkflowState.Approved))
                throw new InvalidOperationException("Mở bản sửa và lưu phiên bản trước khi hẹn giờ.");
            var active = await db.PublicationJobs.SingleOrDefaultAsync(x => x.ContentId == contentId && x.IsActive);
            var generation = active == null ? 1 : checked(active.Generation + 1);
            if (active != null)
            {
                if (replaceJobId != active.Id || replaceVersion == null || !active.RowVersion.SequenceEqual(replaceVersion))
                    throw new DbUpdateConcurrencyException("Đã có lịch đang hoạt động. Thay lịch phải xác nhận đúng phiên bản lịch.");
                active.State = PublicationJobState.Canceled; active.IsActive = false; active.Generation++;
                active.LeaseToken = null; active.LeaseUntilUtc = null; active.UpdatedUtc = now; active.FailureReason = "Được thay bằng lịch mới theo thao tác quản trị.";
                await CloseAttempts(active.Id, now, "Superseded", "Lịch được thay bằng lịch mới; lease cũ đã bị vô hiệu.");
                await db.SaveChangesAsync(); // Release the filtered unique slot inside this same transaction.
            }
            else if (replaceJobId != null)
            {
                var replaced = await db.PublicationJobs.SingleOrDefaultAsync(x => x.Id == replaceJobId && x.ContentId == contentId);
                if (replaced == null || replaceVersion == null || !replaced.RowVersion.SequenceEqual(replaceVersion))
                    throw new DbUpdateConcurrencyException("Lịch đã thay đổi.");
                if (replaced.State == PublicationJobState.Completed) throw new InvalidOperationException("Lịch đã hoàn tất không thể chạy lại; tạo phiên bản và lịch mới.");
                generation = checked(replaced.Generation + 1);
            }
            var job = new PublicationJob
            {
                ContentId = contentId, RevisionId = revisionId, RevisionHash = revision.ContentHash,
                RequestedByUserId = actor.UserId, ReviewerUserId = reviewer, ApprovalHash = direct ? null : content.ApprovalHash,
                Direct = direct, DueUtc = dueUtc, State = PublicationJobState.Pending, Generation = generation, CreatedUtc = now, UpdatedUtc = now
            };
            db.PublicationJobs.Add(job);
            db.AuditLogs.Add(new() { Actor = actor.UserId, Action = "SchedulePublication", ContentId = contentId, RevisionId = revisionId,
                Detail = $"job={job.Id}; dueUtc={dueUtc:O}; mode={(direct ? "direct" : "approved")}" });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return job;
        }
        catch { await tx.RollbackAsync(); db.ChangeTracker.Clear(); throw; }
    }

    public async Task<SchedulerOutcome> CancelAsync(Guid jobId, ContentActor actor, byte[] version)
    {
        Require(actor, Permissions.Schedule);
        var identity = await db.PublicationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId) ?? throw new InvalidOperationException("Không tìm thấy lịch.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var content = await LockContent(identity.ContentId);
            var current = await permissions.GetAsync(actor.UserId);
            Require(current, Permissions.Schedule); Require(current, identity.Direct ? Permissions.PublishDirect : Permissions.PublishApproved);
            if (!await permissions.CanReadAsync(current, content)) throw new UnauthorizedAccessException();
            var job = await db.PublicationJobs.SingleAsync(x => x.Id == jobId);
            if (job.State == PublicationJobState.Completed) { await tx.CommitAsync(); return new("Completed", "Xuất bản đã commit. Hủy lịch không tự gỡ bài."); }
            if (!job.RowVersion.SequenceEqual(version)) throw new DbUpdateConcurrencyException("Lịch đã thay đổi. Đọc lại trước khi hủy.");
            if (!job.IsActive) { await tx.CommitAsync(); return new(job.State.ToString(), "Lịch đã ngừng hoạt động."); }
            job.State = PublicationJobState.Canceled; job.IsActive = false; job.Generation++; job.LeaseToken = null; job.LeaseUntilUtc = null;
            job.FailureReason = "Đã hủy theo thao tác quản trị."; job.UpdatedUtc = time.GetUtcNow();
            await CloseAttempts(job.Id, job.UpdatedUtc, "Canceled", "Người có quyền đã hủy lịch; không suy luận đã xuất bản.");
            db.AuditLogs.Add(new() { Actor = actor.UserId, Action = "CancelPublicationSchedule", ContentId = job.ContentId, RevisionId = job.RevisionId, Detail = "job=" + job.Id });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return new("Canceled");
        }
        catch { await tx.RollbackAsync(); db.ChangeTracker.Clear(); throw; }
    }

    public async Task<PublicationLease?> ClaimAsync(string workerId, CancellationToken ct = default, Guid? onlyJobId = null)
    {
        ValidateWorkerId(workerId);
        var now = time.GetUtcNow(); var until = now.AddSeconds(settings.LeaseSeconds); var token = Guid.NewGuid();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // READPAST skips another claimant's row locks; no process-local queue or select/update gap.
        var claimed = await db.PublicationJobs.FromSqlInterpolated($@"
;WITH candidate AS (
 SELECT TOP (1) * FROM dbo.PublicationJobs WITH (UPDLOCK, READPAST, ROWLOCK)
 WHERE IsActive=1 AND DueUtc<={now} AND ({onlyJobId} IS NULL OR Id={onlyJobId})
 AND ((State=0 AND (NextAttemptUtc IS NULL OR NextAttemptUtc<={now})) OR (State=1 AND LeaseUntilUtc<={now}))
 ORDER BY DueUtc, Id
)
UPDATE candidate SET State=1, LeaseToken={token}, LeaseUntilUtc={until}, AttemptCount=AttemptCount+1, UpdatedUtc={now}
OUTPUT INSERTED.*;").ToListAsync(ct);
        if (claimed.Count == 0) { await tx.CommitAsync(ct); return null; }
        var job = claimed[0];
        var abandoned = await db.PublicationJobAttempts.Where(x => x.JobId == job.Id && x.FinishedUtc == null).ToListAsync(ct);
        foreach (var attempt in abandoned) { attempt.FinishedUtc = now; attempt.Outcome = "LeaseExpired"; attempt.Reason = "Lease trước hết hạn; token mới thay thế, không suy luận hiệu ứng xuất bản."; }
        db.PublicationJobAttempts.Add(new() { JobId = job.Id, Generation = job.Generation, LeaseToken = token, WorkerId = workerId, StartedUtc = now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        db.ChangeTracker.Clear(); return new(job.Id, job.ContentId, job.Generation, token, workerId);
    }

    public async Task<SchedulerOutcome> ExecuteAsync(PublicationLease lease, CancellationToken ct = default)
    {
        var job = await db.PublicationJobs.AsNoTracking().SingleAsync(x => x.Id == lease.JobId, ct);
        if (job.State == PublicationJobState.Completed) return new("Completed", "Hiệu ứng đã commit; không đăng lại.");
        if (!Fence(job, lease)) return new("Superseded", "Lease/generation không còn hiệu lực.");
        if (job.AttemptCount > settings.MaxAttempts) return await FinishFailure(lease, true, "Đã vượt số lần thực thi cho phép; cần người có quyền xem xét.", ct);
        if (time.GetUtcNow() - job.DueUtc > TimeSpan.FromMinutes(settings.CatchUpMinutes))
            return await FinishFailure(lease, false, "Lịch quá ngưỡng chạy bù; cần người có quyền đặt lịch mới.", ct);
        try
        {
            var actor = await permissions.GetAsync(job.RequestedByUserId);
            var content = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == job.ContentId, ct);
            await workflow.PublishScheduledAsync(job.ContentId, job.RevisionId, actor, content.RowVersion, job.PublishCommandId, job.Direct,
                async () =>
                {
                    var locked = await db.PublicationJobs.FromSqlInterpolated($"SELECT * FROM dbo.PublicationJobs WITH (UPDLOCK, ROWLOCK) WHERE Id={job.Id}").SingleAsync(ct);
                    if (!Fence(locked, lease)) throw new DbUpdateConcurrencyException("Lease/generation không còn hiệu lực.");
                    var fresh = await permissions.GetAsync(locked.RequestedByUserId);
                    Require(fresh, Permissions.Schedule); Require(fresh, locked.Direct ? Permissions.PublishDirect : Permissions.PublishApproved);
                    var current = await db.Contents.SingleAsync(x => x.Id == locked.ContentId, ct);
                    if (!await permissions.CanReadAsync(fresh, current) || current.IsTrashed || current.CurrentRevisionId != locked.RevisionId)
                        throw new InvalidOperationException("Nội dung hoặc quyền đọc đã thay đổi; lịch cần xem xét.");
                    var revision = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == locked.RevisionId, ct);
                    if (revision.ContentHash != locked.RevisionHash) throw new InvalidOperationException("Hash phiên bản lịch không khớp.");
                    if (!locked.Direct && (current.ApprovedRevisionId != locked.RevisionId || current.ApprovalHash != locked.ApprovalHash ||
                        current.ApprovedByUserId != locked.ReviewerUserId || string.IsNullOrEmpty(locked.ReviewerUserId) ||
                        !(await permissions.GetAsync(locked.ReviewerUserId)).Has(Permissions.Review)))
                        throw new InvalidOperationException("Approval hoặc quyền người duyệt không còn hiệu lực.");
                    if(!locked.Direct&&!await permissions.CanAccessContentAsync(await permissions.GetAsync(locked.ReviewerUserId!),current.Id))throw new InvalidOperationException("Phạm vi chuyên mục người duyệt đã bị thu hồi.");
                },
                async () =>
                {
                    var completed = await db.PublicationJobs.SingleAsync(x => x.Id == job.Id, ct);
                    if (!Fence(completed, lease)) throw new DbUpdateConcurrencyException("Lease hết hạn trước khi commit; worker cũ không được đăng.");
                    completed.State = PublicationJobState.Completed; completed.IsActive = false; completed.CompletedUtc = time.GetUtcNow();
                    completed.UpdatedUtc = completed.CompletedUtc.Value; completed.LeaseToken = null; completed.LeaseUntilUtc = null; completed.FailureReason = "";
                    var attempt = await db.PublicationJobAttempts.SingleAsync(x => x.JobId == job.Id && x.LeaseToken == lease.Token, ct);
                    attempt.FinishedUtc = completed.CompletedUtc; attempt.Outcome = "Completed";
                });
            return new("Completed");
        }
        catch (Exception error) when (TransientSql(error))
        {
            db.ChangeTracker.Clear();
            // If commit succeeded but acknowledgement was lost, SQL completion is authoritative.
            var committed = await db.PublicationJobs.AsNoTracking().SingleAsync(x => x.Id == lease.JobId, ct);
            if (committed.State == PublicationJobState.Completed) return new("Completed", "Hiệu ứng đã commit; đã đối chiếu SQL.");
            return await FinishFailure(lease, true, "Lỗi SQL tạm thời; thử lại có giới hạn.", ct);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidOperationException or DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return await FinishFailure(lease, false, error is UnauthorizedAccessException ? "Tài khoản đã bị khóa hoặc quyền thực thi đã bị thu hồi." : SafeReason(error.Message), ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await FinishFailure(lease, false, "Không thể lưu dữ liệu theo ràng buộc SQL; cần xem xét lịch.", ct);
        }
    }

    public async Task HeartbeatAsync(string workerId, string environment, CancellationToken ct = default)
    {
        ValidateWorkerId(workerId); var now = time.GetUtcNow();
        var row = await db.WorkerHeartbeats.SingleOrDefaultAsync(x => x.WorkerId == workerId, ct);
        if (row == null) db.WorkerHeartbeats.Add(new() { WorkerId = workerId, StartedUtc = now, LastSeenUtc = now, Environment = environment[..Math.Min(40, environment.Length)] });
        else row.LastSeenUtc = now;
        await db.SaveChangesAsync(ct);
    }
    public static bool TransientSql(Exception exception)
    {
        var sql = exception as SqlException ?? exception.InnerException as SqlException;
        return sql?.Number is -2 or 1205 or 4060 or 40197 or 40501 or 40613 or 10928 or 10929 or 10053 or 10054 or 10060;
    }
    private async Task<SchedulerOutcome> FinishFailure(PublicationLease lease, bool transient, string reason, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await db.PublicationJobs.FromSqlInterpolated($"SELECT * FROM dbo.PublicationJobs WITH (UPDLOCK, ROWLOCK) WHERE Id={lease.JobId}").SingleAsync(ct);
        if (!Fence(job, lease)) { await tx.CommitAsync(ct); return new(job.State == PublicationJobState.Completed ? "Completed" : "Superseded"); }
        var now = time.GetUtcNow();
        if (transient && job.AttemptCount < settings.MaxAttempts)
        {
            job.State = PublicationJobState.Pending; job.NextAttemptUtc = now.AddSeconds(Math.Min(60, 2 * Math.Pow(2, job.AttemptCount - 1)));
        }
        else { job.State = transient ? PublicationJobState.Failed : PublicationJobState.NeedsReview; job.IsActive = false; }
        job.FailureReason = reason; job.LeaseToken = null; job.LeaseUntilUtc = null; job.UpdatedUtc = now;
        var attempt = await db.PublicationJobAttempts.SingleAsync(x => x.JobId == job.Id && x.LeaseToken == lease.Token, ct);
        attempt.FinishedUtc = now; attempt.Outcome = transient && job.IsActive ? "Retry" : job.State.ToString(); attempt.Reason = reason;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(job.State.ToString(), reason);
    }
    private bool Fence(PublicationJob job, PublicationLease lease) => job.IsActive && job.State == PublicationJobState.Running && job.Generation == lease.Generation && job.LeaseToken == lease.Token && job.LeaseUntilUtc > time.GetUtcNow();
    private async Task CloseAttempts(Guid jobId, DateTimeOffset now, string outcome, string reason)
    {
        var attempts = await db.PublicationJobAttempts.Where(x => x.JobId == jobId && x.FinishedUtc == null).ToListAsync();
        foreach (var attempt in attempts) { attempt.FinishedUtc = now; attempt.Outcome = outcome; attempt.Reason = reason; }
    }
    private async Task<Content> LockContent(Guid id) => await db.Contents.FromSqlInterpolated($"SELECT * FROM dbo.Contents WITH (UPDLOCK, ROWLOCK) WHERE Id={id}").AsNoTracking().SingleOrDefaultAsync() ?? throw new InvalidOperationException("Không tìm thấy nội dung.");
    private static void Require(ContentActor actor, string permission) { if (!actor.Has(permission)) throw new UnauthorizedAccessException("Không có quyền hẹn giờ/xuất bản theo nhánh đã chọn."); }
    private static void ValidateWorkerId(string workerId) { if (workerId.Length is < 1 or > 120 || workerId.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_'))) throw new InvalidOperationException("Worker ID không hợp lệ."); }
    private static string SafeReason(string text) => text.Length <= 1000 ? text : text[..1000];
}
