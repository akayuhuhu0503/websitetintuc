using System.Security.Cryptography;
using System.Text;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

public sealed record SourceBulkSelection(Guid Id, string Stamp, Guid? RequestId = null);
public sealed record SourceBulkIntent(Guid RequestId, string Action, string Stamp);
public sealed record SourceBulkResult(Guid Id, string Outcome, string Message, Guid? ContentId = null);

public sealed class SourceSyncBulk(PortalDbContext db, SourceSyncStore store, WorkflowService workflow, SourceSyncMedia transfer)
{
    public static readonly string[] Actions = ["PublishDirect", "Draft", "Ignore"];
    public static string Stamp(CrawlItem item, SourceSyncCheckpoint data) => TextNormalizer.Hash(SourceSyncPolicy.Serialize(new
        { item.Id, item.Status, item.UpdatedUtc, data.Hash, data.AssetHash, data.ContentId, data.Warnings, data.WeeklyReviewRequired, data.ReconcileContentIds }));
    public static bool Allowed(ContentActor actor, string action) => actor.Has(Permissions.ManageSite) && Actions.Contains(action) &&
        (action == "Ignore" || actor.Has(Permissions.Create) && actor.Has(Permissions.ManageMedia)) &&
        (action != "PublishDirect" || actor.Has(Permissions.PublishDirect));
    public static Guid PublishCommand(Guid requestId) => new(SHA256.HashData(Encoding.UTF8.GetBytes("source-bulk-publish:" + requestId))[..16]);

    public async Task<string?> ReasonAsync(SourceBulkSelection selection, string action, ContentActor actor, CancellationToken ct)
    {
        if (!Allowed(actor, action)) return "Không có đủ quyền thực hiện thao tác.";
        if (!await db.Jobs.AnyAsync(x => x.Id == selection.Id && x.Type == SourceSyncPolicy.EntryType, ct)) return "Không tìm thấy bài nguồn.";
        var (item, data) = await store.ReadAsync(selection.Id, ct);
        if (Stamp(item, data) != selection.Stamp) return "Bài nguồn đã đổi sau khi chọn; tải lại và chọn lại.";
        if (item.Status is "Drafted" or "Existing" or "Ignored") return "Bài đã được xử lý; xem Lịch sử.";
        var settings = await store.SettingsAsync(ct);
        if (data.Source?.PrimaryCategoryId is int sourceCategory && settings.Categories.TryGetValue(sourceCategory, out var target) && !actor.Covers([target]))
            return "Chuyên mục ngoài phạm vi được giao.";
        if (action == "Ignore") return null;
        if (item.Status != "Ready" || data.Source == null || data.Hash != SourceSyncPolicy.Hash(data.Source)) return "Chưa đủ dữ liệu nguồn; mở xem trước để kiểm tra.";
        if (data.Warnings.Count > 0 || data.WeeklyReviewRequired || data.ReconcileContentIds.Count > 0) return "Nguồn hoặc lịch tuần còn cảnh báo cần đối soát.";
        if (data.Source.PrimaryCategoryId is not int category || !settings.Categories.TryGetValue(category, out var mapped) || !actor.Covers([mapped])) return "Chưa có ánh xạ chuyên mục phù hợp.";
        if (await SourceSyncStore.ExistingAsync(db, data.Source, ct) != null) return "SourceId hoặc URL đã có trong CMS; không tạo lại.";
        // Include historical revisions, drafts and trash: missing SourceId must not allow duplicate publication.
        if (await db.Revisions.AnyAsync(x => x.Title == data.Source.Title, ct) || await db.ContentDrafts.AnyAsync(x => x.Title == data.Source.Title, ct))
            return "Có bài cùng tiêu đề trong CMS; mở để đối soát trước khi nhập.";
        if (category == 76 && (await store.WeeklyCandidatesAsync(data.Source, mapped, ct)).Count > 0) return "Lịch tương ứng đã có; cần đối soát, không tạo trùng.";
        if (string.IsNullOrWhiteSpace(data.Source.Title) || string.IsNullOrWhiteSpace(SourceSyncPolicy.Summary(data.Source)) || data.Source.PublishedDate == null)
            return "Thiếu tiêu đề, sapo hoặc ngày nguồn; cần biên tập trước khi đăng hàng loạt.";
        return null;
    }

    public async Task<SourceBulkResult> ExecuteAsync(SourceBulkSelection item, SourceBulkIntent intent, string user, CancellationToken ct)
    {
        var actor = await store.RequireAsync(user, draft: intent.Action != "Ignore");
        if (!Allowed(actor, intent.Action)) throw new UnauthorizedAccessException();
        if (intent.Action == "Ignore") return await IgnoreAsync(item, intent, actor, ct);
        // Command receipt is written atomically with the draft. A lost response reuses this command,
        // never a previous manual transfer or another operator's draft.
        var (_, checkpoint) = await store.ReadAsync(item.Id, ct);
        var result = await workflow.CreateSourceDraftAsync(item.Id, checkpoint.Hash, actor, transfer, ct, checkpoint.AssetHash, intent);
        if (intent.Action == "Draft") { await store.ResumeAfterCapacityAsync(ct); return new(item.Id, "Drafted", "Đã chuyển vào bản nháp.", result.ContentId); }
        var publishId = PublishCommand(intent.RequestId);
        var receipt = await db.ContentCommands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == publishId, ct);
        if (receipt != null)
        {
            if (receipt.ActorUserId != user || receipt.Action != "PublishDirect" || receipt.ContentId != result.ContentId) throw new InvalidOperationException("Mã thao tác không khớp.");
            await store.ResumeAfterCapacityAsync(ct);
            var stillPublic = await db.Contents.AsNoTracking().AnyAsync(x => x.Id == result.ContentId && x.IsPublic && !x.IsTrashed && x.PublishedRevisionId == receipt.ResultRevisionId, ct);
            return new(item.Id, stillPublic ? "Published" : "AlreadyProcessed", "Thao tác công bố đã thực hiện trước đó; không đăng lại hoặc đổi trạng thái hiện tại.", result.ContentId);
        }
        SourceBulkResult outcome;
        try
        {
            db.ChangeTracker.Clear();
            var created = await db.ContentCommands.AsNoTracking().SingleAsync(x => x.Id == intent.RequestId, ct);
            var c = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == result.ContentId, ct);
            // EditGeneration also detects autosave (which may not create a revision).
            if (c.EditGeneration != 1 || c.CurrentRevisionId != created.ResultRevisionId || c.State != WorkflowState.Draft || c.IsPublic || c.IsTrashed)
                throw new InvalidOperationException("Bản nháp đã được chỉnh sửa hoặc chuyển trạng thái; mở bài để xử lý, không ghi đè.");
            var (_, after) = await store.ReadAsync(item.Id, ct);
            if (after.Warnings.Count > 0) throw new InvalidOperationException("Có tệp chưa tiếp nhận đầy đủ; giữ nháp để kiểm tra.");
            actor = await store.RequireAsync(user, draft: true);
            if (!Allowed(actor, intent.Action)) throw new UnauthorizedAccessException();
            using var guard = workflow.RestrictBulkToUnpublishedUnscheduled();
            await workflow.PublishDirectAsync(c.Id, c.CurrentRevisionId!.Value, actor, c.RowVersion, publishId);
            outcome = new(item.Id, "Published", "Đã đăng đúng phiên bản vừa chuyển từ nguồn.", c.Id);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or MediaUploadException or DbUpdateConcurrencyException or UnauthorizedAccessException)
        {
            db.ChangeTracker.Clear();
            outcome = new(item.Id, "DraftedWithError", "Đã giữ bản nháp; chưa công bố. " + (error is UnauthorizedAccessException ? "Quyền đã thay đổi." : error is IOException ? "Không đọc được tệp đã kiểm định." : error.Message), result.ContentId);
        }
        await store.ResumeAfterCapacityAsync(ct);
        return outcome;
    }

    async Task<SourceBulkResult> IgnoreAsync(SourceBulkSelection item, SourceBulkIntent intent, ContentActor actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SourceSyncStore.LockAsync(db, "SourceSync.Item." + item.Id, ct);
        var binding = intent.RequestId + "|" + item.Id + "|" + intent.Stamp;
        if (!await db.AuditLogs.AnyAsync(x => x.Actor == actor.UserId && x.Action == "SourceSync.BulkIgnore" && x.Detail == binding, ct))
        {
            var reason = await ReasonAsync(item, "Ignore", await store.RequireAsync(actor.UserId), ct);
            if (reason != null) throw new InvalidOperationException(reason);
            var job = await db.Jobs.SingleAsync(x => x.Id == item.Id, ct);
            if (job.Status == "Running" && job.LeaseUntilUtc > DateTimeOffset.UtcNow) throw new InvalidOperationException("Bài đang thu thập; thử lại sau.");
            var row = await db.CrawlItems.SingleAsync(x => x.Id == item.Id, ct);
            row.Status = job.Status = "Ignored"; row.UpdatedUtc = DateTimeOffset.UtcNow;
            db.AuditLogs.Add(new() { Actor = actor.UserId, Action = "SourceSync.BulkIgnore", Detail = binding });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct); await tx.DisposeAsync();
        await store.ResumeAfterCapacityAsync(ct);
        return new(item.Id, "Ignored", "Đã bỏ qua; giữ nguyên bản chụp và lịch sử.");
    }
}
