using System.Text.Json;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;

public sealed class WorkflowService(PortalDbContext db, HtmlPolicy html, MediaUploadOptions? mediaOptions = null, StorageOptions? storage = null, TimeProvider? clock = null)
{
    public Task<WorkflowResult> SaveAsync(Guid? id, DraftInput input, ContentActor actor, byte[]? version, Guid commandId, bool autosave = false)
    {
        var action = autosave ? "Autosave" : "SaveDraft";
        return ExecuteAsync(id, actor, version, commandId, action, JsonSerializer.Serialize(input), async content =>
        {
            if (content is null)
            {
                Require(actor, Permissions.Create);
                if (!Enum.IsDefined(input.Kind)) throw new InvalidOperationException("Mẫu nội dung không được hỗ trợ.");
                if(input.Kind==ContentKind.Video)Require(actor,Permissions.ManageVideos);
                content = new Content { Kind = input.Kind, OwnerUserId = actor.UserId, HasEditorialChanges = true };
                db.Contents.Add(content); await db.SaveChangesAsync();
            }
            else { Edit(actor, content); if (input.Kind != content.Kind) throw new InvalidOperationException("Không đổi loại của nội dung đã có."); }
            if (content.IsTrashed || content.State is not (WorkflowState.Draft or WorkflowState.Rejected))
                throw new DbUpdateConcurrencyException("Bản nháp đã đóng hoặc chuyển trạng thái. Mở bản sửa mới; autosave cũ không được thay đổi trạng thái.");
            var draft = await DraftAsync(content.Id);
            if (draft is null) { draft = new ContentDraft { ContentId = content.Id }; db.ContentDrafts.Add(draft); }
            await ApplyAsync(draft, input, actor, content);
            content.HasEditorialChanges = true;
            content.ApprovedRevisionId = null; content.ApprovalHash = null; content.SubmittedRevisionId = null;
            if (!autosave) content.CurrentRevisionId = await SnapshotAsync(content, draft, actor);
            // A rejected working draft remains rejected until explicitly saved/submitted.
            if (!autosave) content.State = WorkflowState.Draft;
            return content;
        });
    }

    public Task<WorkflowResult> BeginEditAsync(Guid id, ContentActor actor, byte[] version, Guid commandId) =>
        ExecuteAsync(id, actor, version, commandId, "BeginEdit", "", async content =>
        {
            var c = Existing(content); Edit(actor, c);
            if (c.IsTrashed) throw new InvalidOperationException("Khôi phục bài khỏi thùng rác trước khi biên tập.");
            if (c.State is not (WorkflowState.Draft or WorkflowState.Rejected))
            {
                var revision = await RevisionAsync(c.Id, c.CurrentRevisionId ?? c.PublishedRevisionId);
                await CopyToDraftAsync(c, revision, actor);
            }
            c.State = WorkflowState.Draft; c.HasEditorialChanges = true;
            c.SubmittedRevisionId = null; c.ApprovedRevisionId = null; c.ApprovalHash = null;
            return c;
        });

    public Task<WorkflowResult> SubmitAsync(Guid id, Guid? expectedRevisionId, ContentActor actor, byte[] version, Guid commandId)
    {
        Require(actor, Permissions.Submit);
        return ExecuteAsync(id, actor, version, commandId, "Submit", expectedRevisionId?.ToString() ?? "", async content =>
        {
            var c = Existing(content); Edit(actor, c); Active(c);
            if (c.State is not (WorkflowState.Draft or WorkflowState.Rejected) || c.CurrentRevisionId != expectedRevisionId)
                throw new DbUpdateConcurrencyException("Phiên bản không còn ở trạng thái gửi duyệt.");
            var draft = await DraftAsync(c.Id) ?? throw new InvalidOperationException("Chưa có bản nháp làm việc.");
            await ValidateRequiredAsync(c, draft.Title, draft.BodyHtml, draft.PrimaryCategoryId, draft.Assets.Count, draft.Summary, draft.Author, draft.CoverAssetId);
            c.CurrentRevisionId = await SnapshotAsync(c, draft, actor);
            c.SubmittedRevisionId = c.CurrentRevisionId; c.ApprovedRevisionId = null; c.ApprovalHash = null;
            c.State = WorkflowState.PendingReview;
            return c;
        });
    }

    public Task<WorkflowResult> ApproveAsync(Guid id, Guid revisionId, ContentActor actor, byte[] version, Guid commandId)
    {
        Require(actor, Permissions.Review);
        return ExecuteAsync(id, actor, version, commandId, "Approve", revisionId.ToString(), async content =>
        {
            var c = Existing(content); Active(c);
            if (c.State != WorkflowState.PendingReview || c.SubmittedRevisionId != revisionId || c.CurrentRevisionId != revisionId)
                throw new DbUpdateConcurrencyException("Bản gửi duyệt đã thay đổi; chỉ duyệt đúng phiên bản đang chờ.");
            var revision = await RevisionAsync(c.Id, revisionId);
            c.ApprovedRevisionId = revision.Id; c.ApprovalHash = revision.ContentHash; c.ApprovedByUserId = actor.UserId; c.State = WorkflowState.Approved;
            return c;
        });
    }
    public Task<WorkflowResult> RejectAsync(Guid id, Guid revisionId, string reason, ContentActor actor, byte[] version, Guid commandId)
    {
        Require(actor, Permissions.Review);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw new InvalidOperationException("Nhập lý do trả bài, tối đa 2.000 ký tự.");
        return ExecuteAsync(id, actor, version, commandId, "Reject", revisionId + "\n" + reason, async content =>
        {
            var c = Existing(content); Active(c);
            if (c.State != WorkflowState.PendingReview || c.SubmittedRevisionId != revisionId || c.CurrentRevisionId != revisionId)
                throw new DbUpdateConcurrencyException("Chỉ trả đúng phiên bản đang chờ duyệt.");
            await CopyToDraftAsync(c, await RevisionAsync(c.Id, revisionId), actor);
            c.State = WorkflowState.Rejected; c.ApprovedRevisionId = null; c.ApprovalHash = null;
            return c;
        }, reason);
    }
    public Task<WorkflowResult> PublishApprovedAsync(Guid id, Guid revisionId, ContentActor actor, byte[] version, Guid commandId)
    {
        Require(actor, Permissions.PublishApproved);
        return PublishAsync(id, revisionId, actor, version, commandId, false);
    }
    public Task<WorkflowResult> PublishDirectAsync(Guid id, Guid revisionId, ContentActor actor, byte[] version, Guid commandId)
    {
        Require(actor, Permissions.PublishDirect);
        return PublishAsync(id, revisionId, actor, version, commandId, true);
    }
    public Task<WorkflowResult> PublishScheduledAsync(Guid id, Guid revisionId, ContentActor actor, byte[] version, Guid commandId, bool direct, Func<Task> beforePublishInTransaction, Func<Task> afterPublishInTransaction)
    {
        Require(actor, direct ? Permissions.PublishDirect : Permissions.PublishApproved);
        return PublishAsync(id, revisionId, actor, version, commandId, direct, beforePublishInTransaction, afterPublishInTransaction);
    }
    private Task<WorkflowResult> PublishAsync(Guid id, Guid revisionId, ContentActor actor, byte[] version, Guid commandId, bool direct, Func<Task>? before = null, Func<Task>? after = null) =>
        ExecuteAsync(id, actor, version, commandId, direct ? "PublishDirect" : "PublishApproved", revisionId.ToString(), async content =>
        {
            var c = Existing(content); Active(c);
            if (!CurrentPermissionService.CanRead(actor, c)) throw new UnauthorizedAccessException();
            if (c.CurrentRevisionId != revisionId) throw new DbUpdateConcurrencyException("Phiên bản xuất bản không phải phiên bản hiện tại.");
            if (!direct && (c.State != WorkflowState.Approved || c.ApprovedRevisionId != revisionId))
                throw new InvalidOperationException("Chỉ xuất bản phiên bản đã được duyệt đúng ID.");
            if (direct && c.State is not (WorkflowState.Draft or WorkflowState.Rejected or WorkflowState.PendingReview or WorkflowState.Approved))
                throw new InvalidOperationException("Phiên bản không ở trạng thái có thể xuất bản.");
            var revision = await RevisionAsync(c.Id, revisionId);
            if (!direct && c.ApprovalHash != revision.ContentHash) throw new InvalidOperationException("Dấu duyệt không còn khớp nội dung phiên bản.");
            if(revision.Metadata.DisclosureGroupId is string group)
            {
                if(direct||!actor.Has(Permissions.PublishDisclosure))throw new UnauthorizedAccessException("Công khai thông tin phải qua hai bước và quyền phát hành riêng.");
                if(!await db.DisclosureGroups.AnyAsync(x=>x.Id==group&&x.Status=="NameApproved"))throw new InvalidOperationException("Tên nhóm chưa được phê duyệt; chưa công khai.");
                var m=revision.Metadata;if(string.IsNullOrWhiteSpace(m.Province)||string.IsNullOrWhiteSpace(m.ProvinceCode)||m.DisclosureYear==null||string.IsNullOrWhiteSpace(m.PublicationSource)||string.IsNullOrWhiteSpace(m.LegalBasisReference)||string.IsNullOrWhiteSpace(m.PublicationReviewEvidence))throw new InvalidOperationException("Cần tỉnh/mã tỉnh/năm, nguồn chính thức, căn cứ phát hành và xác nhận rà soát PII/thẩm quyền.");
                if(revision.Assets.Any(x=>x.MediaAsset.ScanStatus!="Clean"))throw new InvalidOperationException("Tệp công khai thông tin phải được quét thật trước phát hành; không dùng ngoại lệ NotScanned.");
            }
            var draft = await DraftAsync(c.Id);
            if (draft is not null && Hash(draft) != revision.ContentHash)
                throw new InvalidOperationException("Bản làm việc có thay đổi chưa lưu thành phiên bản; lưu và duyệt lại trước khi đăng.");
            await ValidateRequiredAsync(c, revision.Title, revision.BodyHtml, revision.PrimaryCategoryId, revision.Assets.Count, revision.Summary, revision.Author, revision.CoverAssetId);
            if(c.Kind==ContentKind.Video&&!revision.IsSourceRevision&&!revision.Assets.Any(x=>x.Usage=="Video"))throw new InvalidOperationException("Video mới phải có tệp nội bộ đã xử lý; không xuất bản metadata như video đã chuyển.");
            foreach (var asset in revision.Assets)
            {
                if (asset.MediaAsset.MimeType == "application/pdf" && asset.MediaAsset.Origin is not ("Source" or "EditorialDocument"))
                    throw new InvalidOperationException("PDF upload phải qua hàng đợi tài liệu và scanner thật trước khi công khai; ngoại lệ Development không áp dụng cho PDF mới.");
                if (asset.MediaAsset.Origin == "EditorialDocument")
                {
                    if (storage == null) throw new InvalidOperationException("Chưa có kho tài liệu.");
                    await using var verified = await EditorialDocumentService.OpenVerifiedAsync(db, storage, asset.MediaAsset, (mediaOptions ?? new MediaUploadOptions()).IsDevelopmentLoopback);
                    if (verified == null || asset.MediaAsset.MimeType == OfficeDocumentPolicy.Docx && !await db.EditorialDocuments.AnyAsync(x => x.MediaAssetId == asset.MediaAssetId && x.ContentId == c.Id)) throw new InvalidOperationException("Tài liệu chưa Clean đúng byte hoặc Word không thuộc hồ sơ này.");
                }
                if(asset.Usage=="Subtitle")
                {
                    var track=await db.Set<VideoSubtitle>().AsNoTracking().SingleOrDefaultAsync(x=>x.MediaAssetId==asset.MediaAssetId);
                    if(storage==null||track==null||!revision.Assets.Any(x=>x.Usage=="Video"&&x.MediaAssetId==track.VideoMediaAssetId)||!await VideoSubtitleService.ReadyAsync(db,storage,asset.MediaAssetId,(mediaOptions??new MediaUploadOptions()).IsDevelopmentLoopback))throw new InvalidOperationException("Phụ đề chưa Clean đúng byte, quá thời lượng hoặc video chưa Ready.");
                    continue;
                }
                else if(asset.Usage=="Video")
                {
                    if(storage==null||!await VideoReadiness.ReadyAsync(db,storage,asset.MediaAsset,(mediaOptions??new MediaUploadOptions()).IsDevelopmentLoopback))throw new InvalidOperationException("Video chưa Ready, chưa quét đúng byte hoặc thiếu rendition/poster; không xuất bản.");
                    continue;
                }
                if (!(mediaOptions ?? new MediaUploadOptions()).CanServe(asset.MediaAsset) || !AllowedMime(asset.MediaAsset.MimeType))
                    throw new InvalidOperationException("Tài nguyên chưa đủ điều kiện công khai; kiểm tra định dạng, cách ly và chính sách scanner/development.");
                if(asset.MediaAsset.ValidationStatus==BatchMediaProof.Validation&&(storage==null||!await BatchMediaProof.ValidAsync(storage,asset.MediaAsset,(mediaOptions??new MediaUploadOptions()).IsDevelopmentLoopback)))
                    throw new InvalidOperationException("Tài nguyên batch Test chưa Clean đúng byte hoặc môi trường không được phép.");
                if (storage is null) throw new InvalidOperationException("Chưa cấu hình kho tài nguyên để kiểm tra tệp trước khi xuất bản.");
                var original = new FileInfo(storage.ResolveMedia(asset.MediaAsset.StorageKey));
                if (!original.Exists || original.Length != asset.MediaAsset.ByteLength)
                    throw new InvalidOperationException("Tệp tài nguyên không còn trong kho hoặc sai kích thước; chưa xuất bản.");
            }
            if(revision.Metadata.WeeklyYear is int year&&revision.Metadata.WeeklyWeek is int week)
            {
                if (revision.Metadata.WeeklyOriginalAssetId is Guid originalId &&
                    (!revision.Assets.Any(x => x.MediaAssetId == originalId && x.Usage == "Attachment" && x.MediaAsset.MimeType == OfficeDocumentPolicy.Docx) || string.IsNullOrWhiteSpace(revision.Metadata.WeeklyOriginalReviewEvidence)))
                    throw new InvalidOperationException("Word gốc phải được gắn trong đúng revision và có xác nhận rà soát metadata/comments/thẩm quyền trước khi công khai.");
                await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource={"weekly-release-"+year+"-"+week},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;IF @r<0 THROW 51000,'Weekly release lock unavailable',1;");
                if(await db.WeeklyScheduleReleases.AnyAsync(x=>x.ContentId==c.Id&&(x.IsoYear!=year||x.IsoWeek!=week))||await db.WeeklyScheduleReleases.AnyAsync(x=>x.IsoYear==year&&x.IsoWeek==week&&x.ContentId!=c.Id))throw new InvalidOperationException("Tuần đã có hồ sơ lịch hoặc không được chuyển hồ sơ đã phát hành sang tuần khác. Sửa từ hồ sơ lịch hiện hữu.");
                var version=(await db.WeeklyScheduleReleases.Where(x=>x.IsoYear==year&&x.IsoWeek==week).MaxAsync(x=>(int?)x.Version)??0)+1;
                db.WeeklyScheduleReleases.Add(new(){ContentId=c.Id,RevisionId=revision.Id,IsoYear=year,IsoWeek=week,Version=version,PublishedUtc=(clock??TimeProvider.System).GetUtcNow(),PublisherId=actor.UserId,ChangeNote=revision.Metadata.WeeklyChangeNote??""});
            }
            c.PublishedRevisionId = revision.Id; c.IsPublic = true; c.State = WorkflowState.Published;
            c.PublishedAtUtc ??= (clock ?? TimeProvider.System).GetUtcNow();
            // Retain original source date/time precision. Compatibility mirrors only update at publish.
            c.PrimaryCategoryId = revision.PrimaryCategoryId;
            var old = await db.ContentCategories.Where(x => x.ContentId == c.Id).ToListAsync();
            var ids = revision.Categories.Select(x => x.CategoryId).ToHashSet();
            foreach (var row in old.Where(x => !ids.Contains(x.CategoryId))) db.ContentCategories.Remove(row);
            foreach (var cat in ids.Except(old.Select(x => x.CategoryId))) db.ContentCategories.Add(new() { ContentId = c.Id, CategoryId = cat });
            return c;
        }, before: before, after: after, scheduled: before is not null);

    public Task<WorkflowResult> UnpublishAsync(Guid id, ContentActor actor, byte[] version, Guid commandId)
    {
        Require(actor, Permissions.Unpublish);
        return ExecuteAsync(id, actor, version, commandId, "Unpublish", "", content =>
        {
            var c = Existing(content); Read(actor, c); Active(c);
            if (!c.IsPublic) throw new InvalidOperationException("Bài hiện không công khai.");
            c.IsPublic = false; c.HasEditorialChanges = true;
            if (c.State == WorkflowState.Published) c.State = WorkflowState.Unpublished;
            return Task.FromResult(c);
        });
    }
    public Task<WorkflowResult> TrashAsync(Guid id, ContentActor actor, byte[] version, Guid commandId)
    {
        Require(actor, Permissions.Trash);
        return ExecuteAsync(id, actor, version, commandId, "Trash", "", content =>
        {
            var c = Existing(content); Read(actor, c); Active(c);
            c.IsTrashed = true; c.IsPublic = false; c.State = WorkflowState.Archived; c.HasEditorialChanges = true;
            c.ApprovedRevisionId = null; c.ApprovalHash = null; c.SubmittedRevisionId = null;
            return Task.FromResult(c);
        });
    }
    public Task<WorkflowResult> RestoreTrashAsync(Guid id, ContentActor actor, byte[] version, Guid commandId)
    {
        Require(actor, Permissions.Restore);
        return ExecuteAsync(id, actor, version, commandId, "RestoreTrash", "", async content =>
        {
            var c = Existing(content); Read(actor, c);
            if (!c.IsTrashed) throw new InvalidOperationException("Bài không ở trong thùng rác.");
            if (c.CurrentRevisionId != null) await CopyToDraftAsync(c, await RevisionAsync(c.Id, c.CurrentRevisionId), actor);
            c.IsTrashed = false; c.IsPublic = false; c.State = WorkflowState.Draft; c.HasEditorialChanges = true;
            c.ApprovedRevisionId = null; c.ApprovalHash = null; c.SubmittedRevisionId = null;
            // The historical pointer may remain for audit; IsPublic is the access grant.
            return c;
        });
    }
    public Task<WorkflowResult> RestoreRevisionAsync(Guid id, Guid revisionId, ContentActor actor, byte[] version, Guid commandId)
    {
        Require(actor, Permissions.Restore);
        return ExecuteAsync(id, actor, version, commandId, "RestoreRevision", revisionId.ToString(), async content =>
        {
            var c = Existing(content); Edit(actor, c); Active(c);
            await CopyToDraftAsync(c, await RevisionAsync(c.Id, revisionId), actor);
            var draft = (await DraftAsync(c.Id))!;
            // Force a fresh immutable snapshot, never overwrite the selected historical revision.
            c.CurrentRevisionId = await SnapshotAsync(c, draft, actor, true);
            c.State = WorkflowState.Draft; c.HasEditorialChanges = true;
            c.ApprovedRevisionId = null; c.ApprovalHash = null; c.SubmittedRevisionId = null;
            return c;
        });
    }
    public Task<WorkflowResult> CloneAsync(Guid id, Guid? revisionId, ContentActor actor, Guid commandId)
    {
        Require(actor, Permissions.Create);
        return ExecuteAsync(null, actor, null, commandId, "Clone", id + "\n" + revisionId, async _ =>
        {
            var source = await db.Contents.SingleOrDefaultAsync(x => x.Id == id) ?? throw new InvalidOperationException("Không tìm thấy bài nguồn nhân bản.");
            Read(actor, source);
            if(!await new CurrentPermissionService(db).CanAccessContentAsync(actor,source.Id))throw new UnauthorizedAccessException("Bài nguồn ngoài phạm vi chuyên mục.");
            var revision = await RevisionAsync(source.Id, revisionId ?? source.CurrentRevisionId ?? source.PublishedRevisionId);
            var c = new Content { Kind = source.Kind, OwnerUserId = actor.UserId, State = WorkflowState.Draft, HasEditorialChanges = true };
            db.Contents.Add(c); await db.SaveChangesAsync();
            await CopyToDraftAsync(c, revision, actor);
            c.CurrentRevisionId = await SnapshotAsync(c, (await DraftAsync(c.Id))!, actor, true);
            return c;
        });
    }

    private async Task<WorkflowResult> ExecuteAsync(Guid? id, ContentActor actor, byte[]? version, Guid commandId, string action, string payload,
        Func<Content?, Task<Content>> operation, string auditDetail = "", Func<Task>? before = null, Func<Task>? after = null, bool scheduled = false)
    {
        if (commandId == Guid.Empty) throw new InvalidOperationException("Thiếu mã thao tác chống gửi lặp.");
        var requestHash = TextNormalizer.Hash(action + "\n" + id + "\n" + (version is null ? "" : Convert.ToBase64String(version)) + "\n" + payload);
        var replay = await ReplayAsync(commandId, actor, action, requestHash);
        if (replay != null) return replay;
        await using var tx = await db.Database.BeginTransactionAsync(scheduled ? System.Data.IsolationLevel.Serializable : System.Data.IsolationLevel.ReadCommitted);
        try
        {
            if (id.HasValue) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM dbo.Contents WITH (UPDLOCK, ROWLOCK) WHERE Id={id.Value}");
            var content = id.HasValue ? await db.Contents.SingleOrDefaultAsync(x => x.Id == id) ?? throw new InvalidOperationException("Không tìm thấy bài viết.") : null;
            if (content != null) Version(content, version);
            if(content!=null&&!await new CurrentPermissionService(db).CanAccessContentAsync(actor,content.Id))throw new UnauthorizedAccessException("Bài có chuyên mục chính hoặc phụ ngoài phạm vi được giao.");
            if (before is not null) await before();
            var publicRevisionBefore = content?.IsPublic == true ? content.PublishedRevisionId : null;
            content = await operation(content);
            if (content.ApprovedRevisionId is null) content.ApprovedByUserId = null;
            if (!scheduled && id.HasValue && action is not ("Autosave" or "Approve" or "Submit"))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE a SET FinishedUtc={(clock ?? TimeProvider.System).GetUtcNow()},Outcome=N'Superseded',Reason=N'Phiên bản hoặc trạng thái nội dung đã đổi.' FROM dbo.PublicationJobAttempts a JOIN dbo.PublicationJobs j ON j.Id=a.JobId WHERE j.ContentId={content.Id} AND j.IsActive=1 AND a.FinishedUtc IS NULL");
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.PublicationJobs SET State=4,IsActive=0,Generation=Generation+1,LeaseToken=NULL,LeaseUntilUtc=NULL,FailureReason=N'Nội dung đã đổi trạng thái hoặc phiên bản; cần lập lịch mới.',UpdatedUtc={(clock ?? TimeProvider.System).GetUtcNow()} WHERE ContentId={content.Id} AND IsActive=1");
            }
            content.EditGeneration++;
            if (id.HasValue) db.Entry(content).Property(x => x.RowVersion).OriginalValue = version!;
            db.ContentCommands.Add(new() { Id = commandId, ContentId = content.Id, ActorUserId = actor.UserId, Action = action, RequestHash = requestHash, ResultRevisionId = content.CurrentRevisionId });
            var eventRevision = action is "Unpublish" or "Trash" && publicRevisionBefore is not null ? publicRevisionBefore : content.CurrentRevisionId;
            db.AuditLogs.Add(new() { Actor = actor.UserId, Action = action, ContentId = content.Id, RevisionId = eventRevision,
                Detail = "command=" + commandId + (auditDetail.Length > 0 ? "; reason=" + auditDetail : "") });
            if (after is not null) await after();
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Result(content, false);
        }
        catch (Exception error) when (error is DbUpdateConcurrencyException || error is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } })
        {
            await tx.RollbackAsync(); await tx.DisposeAsync(); db.ChangeTracker.Clear();
            var applied = await ReplayAsync(commandId, actor, action, requestHash);
            if (applied != null) return applied;
            throw;
        }
        catch
        {
            await tx.RollbackAsync();
            // A rejected command must not leave dirty tracked entities that a later
            // operation on the same scoped context could accidentally persist.
            db.ChangeTracker.Clear();
            throw;
        }
    }
    private async Task<WorkflowResult?> ReplayAsync(Guid commandId, ContentActor actor, string action, string hash)
    {
        var replay = await db.ContentCommands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == commandId);
        if (replay == null) return null;
        if (replay.ActorUserId != actor.UserId || replay.Action != action || replay.RequestHash != hash) throw new InvalidOperationException("Mã thao tác đã dùng cho một yêu cầu khác.");
        var content = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == replay.ContentId); Read(actor, content);
        if(!await new CurrentPermissionService(db).CanAccessContentAsync(actor,content.Id))throw new UnauthorizedAccessException("Quyền chuyên mục đã thay đổi; không được đọc lại kết quả cũ.");
        return Result(content, true);
    }
    private static WorkflowResult Result(Content c, bool replay) => new(c.Id, c.CurrentRevisionId, c.PublishedRevisionId, c.RowVersion, c.State, c.IsPublic, c.IsTrashed, replay, DateTimeOffset.UtcNow);
    private static void Require(ContentActor actor, string permission) { if (!actor.Has(permission)) throw new UnauthorizedAccessException("Không có quyền thực hiện thao tác."); }
    private static void Edit(ContentActor actor, Content content) { if (!CurrentPermissionService.CanEdit(actor, content)) throw new UnauthorizedAccessException("Không có quyền sửa bài này."); }
    private static void Read(ContentActor actor, Content content) { if (!CurrentPermissionService.CanRead(actor, content)) throw new UnauthorizedAccessException("Không có quyền xem bài này."); }
    private static Content Existing(Content? c) => c ?? throw new InvalidOperationException("Không tìm thấy bài viết.");
    private static void Active(Content c) { if (c.IsTrashed) throw new InvalidOperationException("Bài đang ở thùng rác."); }
    private void Version(Content c, byte[]? expected)
    {
        if (expected is null || expected.Length != 8) throw new InvalidOperationException("Thiếu phiên bản chống ghi đè.");
        if (!c.RowVersion.AsSpan().SequenceEqual(expected)) throw new DbUpdateConcurrencyException("Bài đã thay đổi; giữ nội dung đang nhập và tải lại để đối chiếu.");
        db.Entry(c).Property(x => x.RowVersion).OriginalValue = expected;
    }
    private async Task<ContentDraft?> DraftAsync(Guid id) => db.ContentDrafts.Local.FirstOrDefault(x => x.ContentId == id) ??
        await db.ContentDrafts.Include(x => x.Categories).Include(x => x.Assets).SingleOrDefaultAsync(x => x.ContentId == id);
    private async Task<ContentRevision> RevisionAsync(Guid contentId, Guid? id) => await db.Revisions.Include(x => x.Categories).Include(x => x.Assets).ThenInclude(x => x.MediaAsset)
        .SingleOrDefaultAsync(x => x.ContentId == contentId && x.Id == id) ?? throw new InvalidOperationException("Phiên bản không thuộc bài viết này.");
    private async Task ApplyAsync(ContentDraft draft, DraftInput input, ContentActor actor, Content content, bool historicalCopy = false)
    {
        if (input.Title.Length > 1000 || input.Summary.Length > 5000 || input.Author.Length > 500 || input.SourceCredit.Length > 500 || input.BodyHtml.Length > 2_000_000)
            throw new InvalidOperationException("Trường nội dung vượt giới hạn cho phép.");
        var categories = input.SecondaryCategoryIds.Append(input.PrimaryCategoryId ?? 0).Where(x => x != 0).Distinct().ToHashSet();
        if(actor.CategoryIds is not null&&(input.PrimaryCategoryId is null||!actor.Covers(categories)))throw new UnauthorizedAccessException("Chuyên mục chính và mọi chuyên mục phụ phải thuộc phạm vi được giao.");
        if (categories.Count > 30 || await db.Categories.CountAsync(x => categories.Contains(x.Id)) != categories.Count) throw new InvalidOperationException("Chuyên mục không hợp lệ.");
        var liveConfiguration = await db.Set<SiteConfiguration>().Where(x=>x.Id == 1).Select(x=>x.LiveRevisionId).FirstOrDefaultAsync();
        if(!historicalCopy && liveConfiguration is not null && await db.Set<SiteCategoryConfiguration>().CountAsync(x=>x.RevisionId == liveConfiguration && categories.Contains(x.CategoryId)) != categories.Count) throw new InvalidOperationException("Chọn chuyên mục đang áp dụng; chuyên mục lịch sử không còn được chọn để đăng mới.");
        V05ContentPolicy.Validate(input.Metadata,input.Kind);
        if(input.Metadata.WeeklyYear!=null&&input.PrimaryCategoryId!=76)throw new InvalidOperationException("Lịch tuần phải thuộc hạng mục Lịch tuần (76).");
        var clean = html.Clean(input.Metadata.WeeklyYear==null?input.BodyHtml:V05ContentPolicy.Table(input.Metadata));
        if (input.Assets.Count > 100) throw new InvalidOperationException("Một phiên bản tối đa100 tài nguyên.");
        var selections = input.Assets.GroupBy(x => x.MediaAssetId).Select(x => x.First()).ToDictionary(x => x.MediaAssetId);
        foreach(var selection in selections.Values) selection.Caption ??= "";
        if (input.CoverAssetId is Guid cover && !selections.ContainsKey(cover)) selections.Add(cover, new() { MediaAssetId = cover, Usage = "Cover" });
        var parsed = new HtmlParser().ParseDocument(clean);
        var bodyReferences = new HashSet<Guid>();
        foreach (var element in parsed.QuerySelectorAll("[src],[href]"))
        {
            foreach (var name in new[] { "src", "href" })
            {
                var value = element.GetAttribute(name);
                if (value != null && value.StartsWith("/media/", StringComparison.Ordinal))
                {
                    var rawId = value[7..].Split('?', '#')[0];
                    if (!Guid.TryParseExact(rawId, "D", out var fileId)) throw new InvalidOperationException("Đường dẫn tài nguyên không hợp lệ.");
                    bodyReferences.Add(fileId);
                    if (!selections.ContainsKey(fileId)) selections.Add(fileId, new() { MediaAssetId = fileId, Usage = element.TagName == "IMG" ? "Inline" : "Attachment" });
                }
            }
        }
        // Stale hidden selections cannot grant public access after their position
        // is removed. Explicit PDF attachments remain valid without a body link.
        foreach (var stale in selections.Values.Where(x => x.Usage is not("Attachment" or "Video" or "Subtitle") &&
                     !bodyReferences.Contains(x.MediaAssetId) && input.CoverAssetId != x.MediaAssetId).ToList())
            selections.Remove(stale.MediaAssetId);
        if (selections.Count > 100) throw new InvalidOperationException("Một phiên bản tối đa100 tài nguyên.");
        var assets = await db.MediaAssets.Where(x => selections.Keys.Contains(x.Id)).ToListAsync();
        if (assets.Count != selections.Count) throw new InvalidOperationException("Tài nguyên chưa tồn tại trong kho.");
        foreach (var asset in assets)
        {
            if (asset.Origin == "EditorialDocument" && (!await db.EditorialDocuments.AnyAsync(x => x.MediaAssetId == asset.Id) || asset.MimeType == OfficeDocumentPolicy.Docx && (input.Metadata.WeeklyYear == null || !await db.EditorialDocuments.AnyAsync(x => x.MediaAssetId == asset.Id && x.ContentId == content.Id))))
                throw new InvalidOperationException("Tài liệu chỉ dùng trong đúng hồ sơ; Word chỉ thuộc lịch tuần.");
            if(selections[asset.Id].Usage=="Subtitle")
            {
                Require(actor,Permissions.ManageVideos);Require(actor,Permissions.ManageMedia);
                var track=await db.Set<VideoSubtitle>().AsNoTracking().SingleOrDefaultAsync(x=>x.MediaAssetId==asset.Id);
                if(asset.Origin!="VideoSubtitle"||track==null||!selections.Values.Any(x=>x.Usage=="Video"&&x.MediaAssetId==track.VideoMediaAssetId))throw new InvalidOperationException("Phụ đề phải thuộc video đã gắn trong cùng revision.");
            }
            else if(selections[asset.Id].Usage=="Video")
            {
                Require(actor,Permissions.ManageVideos);Require(actor,Permissions.ManageMedia);
                if(asset.Origin!="VideoUpload"||!await db.Set<VideoFile>().AnyAsync(x=>x.MediaAssetId==asset.Id))throw new InvalidOperationException("Video phải thuộc pipeline tải trực tiếp nội bộ.");
            }
            else
            {
            if (asset.IsQuarantined || asset.ValidationStatus is not ("Validated" or "SourceValidated" or BatchMediaProof.Validation)) throw new InvalidOperationException("Tài nguyên chưa được xác minh định dạng.");
            if(asset.ValidationStatus==BatchMediaProof.Validation&&(storage==null||!await BatchMediaProof.ValidAsync(storage,asset,(mediaOptions??new MediaUploadOptions()).IsDevelopmentLoopback)))throw new InvalidOperationException("Tài nguyên batch Test chưa Clean đúng byte hoặc môi trường không được phép.");
            if (!AllowedMime(asset.MimeType)) throw new InvalidOperationException("Chỉ được chọn ảnh JPEG/PNG/GIF/WebP hoặc PDF.");
            }
            if (selections[asset.Id].Usage == "Attachment" && !OfficeDocumentPolicy.IsDocument(asset.MimeType) &&
                !bodyReferences.Contains(asset.Id) && input.CoverAssetId != asset.Id)
                throw new InvalidOperationException("Danh sách tài liệu đính kèm chỉ nhận PDF; ảnh phải ở thân bài hoặc ảnh đại diện.");
            var readableRevisions=new CurrentPermissionService(db).ScopeRevisions(db.Revisions.AsNoTracking(),actor).Where(r=>r.ContentId==content.Id).Select(r=>r.Id);
            var related = await db.ContentAssets.AnyAsync(x => x.MediaAssetId == asset.Id && readableRevisions.Contains(x.RevisionId));
            var isPublic = await db.ContentAssets.AnyAsync(x => x.MediaAssetId == asset.Id && db.Contents.Any(c => c.IsPublic && !c.IsTrashed && c.PublishedRevisionId == x.RevisionId));
            if (!related && !isPublic && asset.UploadedByUserId != actor.UserId && !(actor.CategoryIds is null && actor.Has(Permissions.EditAll))) throw new UnauthorizedAccessException("Không được sử dụng tệp riêng tư của người khác.");
            if (selections[asset.Id].Caption.Length > 2000 || selections[asset.Id].Usage is not ("Inline" or "Attachment" or "Cover" or "Thumbnail" or "Video" or "Subtitle")) throw new InvalidOperationException("Thông tin sử dụng tài nguyên không hợp lệ.");
        }
        if (input.CoverAssetId.HasValue && !assets.Single(x => x.Id == input.CoverAssetId).MimeType.StartsWith("image/")) throw new InvalidOperationException("Ảnh đại diện phải là ảnh.");
        if (input.Metadata.WeeklyOriginalAssetId is Guid original && (input.Metadata.WeeklyYear == null || !assets.Any(x => x.Id == original && x.Origin == "EditorialDocument" && x.MimeType == OfficeDocumentPolicy.Docx)))
            throw new InvalidOperationException("Word gốc phải là tài liệu đã chọn trong đúng lịch tuần.");
        draft.Title = input.Title.Trim(); draft.Summary = input.Summary.Trim(); draft.BodyHtml = clean; draft.Author = input.Author.Trim(); draft.SourceCredit = input.SourceCredit.Trim();
        draft.PrimaryCategoryId = input.PrimaryCategoryId; draft.CoverAssetId = input.CoverAssetId;
        draft.Metadata = ValidateMetadata(input.Metadata, content.Kind);
        draft.SearchText = TextNormalizer.Search(draft.Title + " " + draft.Summary + " " + html.PlainText(clean) + " " + draft.Metadata.SearchFields());
        draft.UpdatedUtc = DateTimeOffset.UtcNow; draft.UpdatedByUserId = actor.UserId;
        foreach (var row in draft.Categories.Where(x => !categories.Contains(x.CategoryId)).ToList()) { db.DraftCategories.Remove(row); draft.Categories.Remove(row); }
        foreach (var cat in categories.Except(draft.Categories.Select(x => x.CategoryId))) draft.Categories.Add(new() { ContentId = draft.ContentId, CategoryId = cat });
        foreach (var row in draft.Assets.Where(x => !selections.ContainsKey(x.MediaAssetId)).ToList()) { db.DraftAssets.Remove(row); draft.Assets.Remove(row); }
        foreach (var pair in selections)
        {
            var row = draft.Assets.FirstOrDefault(x => x.MediaAssetId == pair.Key);
            if (row == null) { row = new() { ContentId = draft.ContentId, MediaAssetId = pair.Key }; draft.Assets.Add(row); }
            row.Usage = pair.Value.Usage; row.Caption = pair.Value.Caption; row.SortOrder = pair.Value.SortOrder;
        }
    }
    private async Task CopyToDraftAsync(Content content, ContentRevision revision, ContentActor actor)
    {
        var draft = await DraftAsync(content.Id);
        if (draft == null) { draft = new() { ContentId = content.Id }; db.ContentDrafts.Add(draft); }
        await ApplyAsync(draft, new() { Kind = content.Kind, Metadata = revision.Metadata.Copy(), Title = revision.Title, Summary = revision.Summary, BodyHtml = revision.BodyHtml, Author = revision.Author, SourceCredit = revision.SourceCredit,
            PrimaryCategoryId = revision.PrimaryCategoryId, SecondaryCategoryIds = revision.Categories.Select(x => x.CategoryId).ToList(), CoverAssetId = revision.CoverAssetId,
            Assets = revision.Assets.Select(x => new AssetSelection { MediaAssetId = x.MediaAssetId, Usage = x.Usage, Caption = x.Caption, SortOrder = x.SortOrder }).ToList() }, actor, content, historicalCopy: true);
    }
    private async Task<Guid> SnapshotAsync(Content content, ContentDraft draft, ContentActor actor, bool force = false)
    {
        var hash = Hash(draft);
        if (!force && content.CurrentRevisionId != null && await db.Revisions.AnyAsync(x => x.Id == content.CurrentRevisionId && x.ContentHash == hash)) return content.CurrentRevisionId.Value;
        var revision = new ContentRevision { Metadata = draft.Metadata.Copy(), ContentId = content.Id, Title = draft.Title, Summary = draft.Summary, BodyHtml = draft.BodyHtml,
            SearchText = draft.SearchText, Author = draft.Author, SourceCredit = draft.SourceCredit, PrimaryCategoryId = draft.PrimaryCategoryId, CoverAssetId = draft.CoverAssetId,
            EditedByUserId = actor.UserId, ContentHash = hash };
        foreach (var cat in draft.Categories) revision.Categories.Add(new() { RevisionId = revision.Id, CategoryId = cat.CategoryId });
        foreach (var asset in draft.Assets) revision.Assets.Add(new() { RevisionId = revision.Id, MediaAssetId = asset.MediaAssetId, Usage = asset.Usage, Caption = asset.Caption, SortOrder = asset.SortOrder });
        db.Revisions.Add(revision); db.Add(SearchProjectionService.Create(revision)); return revision.Id;
    }
    private static string Hash(ContentDraft draft)
    {
        var common = JsonSerializer.Serialize(new
    {
        draft.Title, draft.Summary, draft.BodyHtml, draft.Author, draft.SourceCredit, draft.PrimaryCategoryId, draft.CoverAssetId,
        Categories = draft.Categories.Select(x => x.CategoryId).Order().ToArray(),
        Assets = draft.Assets.OrderBy(x => x.MediaAssetId).Select(x => new { x.MediaAssetId, x.Usage, x.Caption, x.SortOrder }).ToArray()
        });
        // Preserve the 2A fingerprint for ordinary articles with no specialized fields.
        var m = draft.Metadata;
        var hasFields = m.PublicationDate is not null || m.Unit.Length != 0 || m.SearchFields().Trim().Length != 0 || m.IssuedDate is not null || m.EffectiveDate is not null || m.EffectiveUntilDate is not null || m.EffectivenessStatus is not null || m.ConsultationStartDate is not null || m.ConsultationEndDate is not null || m.ConsultationStartUtc is not null || m.ConsultationEndUtc is not null || m.ConsultationDateEvidence.Length != 0;
        hasFields = hasFields || m.EffectivenessEvidence.Length != 0 || m.MappingStatus != "Unmapped" || m.DisclosureGroupId!=null || m.WeeklyYear!=null;
        return TextNormalizer.Hash(hasFields ? common + "\n" + JsonSerializer.Serialize(m) : common);
    }
    private static ContentMetadata ValidateMetadata(ContentMetadata value, ContentKind kind)
    {
        var m = value.Copy();
        if(m.Tags?.Length>500||m.Tags?.Any(char.IsControl)==true)throw new InvalidOperationException("Từ khóa tối đa 500 ký tự, không chứa ký tự điều khiển.");
        m.Tags=string.IsNullOrWhiteSpace(m.Tags)?null:string.Join(", ",m.Tags.Split([',',';'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase));
        if(m.Tags!=null&&(m.Tags.Length>500||m.Tags.Split(',').Length>20||m.Tags.Split(',').Any(x=>x.Trim().Length>64)))throw new InvalidOperationException("Tối đa 20 từ khóa, mỗi từ khóa tối đa 64 ký tự và tổng sau chuẩn hóa tối đa 500 ký tự.");
        if(m.Featured==false)m.Featured=null;
        if(kind is not(ContentKind.Article or ContentKind.Announcement)&&(m.Tags!=null||m.Featured!=null))throw new InvalidOperationException("Từ khóa và tin nổi bật chỉ dùng cho tin bài hoặc thông báo.");
        m.Number ??= ""; m.DocumentType ??= ""; m.IssuingBody ??= ""; m.Signer ??= ""; m.Field ??= ""; m.Unit ??= ""; m.ConsultationDateEvidence ??= ""; m.EffectivenessEvidence ??= ""; m.MappingStatus ??= "Unmapped";
        if (m.Number.Length > 200 || m.DocumentType.Length > 300 || m.IssuingBody.Length > 500 || m.Signer.Length > 300 || m.Field.Length > 300 || m.Unit.Length > 500 || m.ConsultationDateEvidence.Length > 2000 || m.EffectivenessEvidence.Length > 2000) throw new InvalidOperationException("Metadata văn bản vượt giới hạn.");
        if (m.MappingStatus is not ("Unmapped" or "Reviewed")) throw new InvalidOperationException("Trạng thái ánh xạ không hợp lệ.");
        if(m.EffectivenessBoundary is not (null or "StartOfVietnamDay" or "EndOfVietnamDay")||m.EffectiveDate>m.EffectiveUntilDate||m.EffectivenessBoundary!=null&&m.EffectiveUntilDate==null)throw new InvalidOperationException("Ngày hoặc quy tắc biên hết hiệu lực chưa hợp lệ.");
        if(m.EffectiveUntilDate!=null&&string.IsNullOrWhiteSpace(m.EffectivenessEvidence))throw new InvalidOperationException("Ngày hết hiệu lực cần căn cứ xác nhận; không suy diễn từ ngày ban hành.");
        if (m.EffectivenessStatus is not (null or "" or "ConfirmedEffective" or "ConfirmedExpired" or "ConfirmedPartial" or "Unknown")) throw new InvalidOperationException("Trạng thái hiệu lực không hợp lệ.");
        if ((m.EffectiveDate is not null || m.EffectivenessStatus is "ConfirmedEffective" or "ConfirmedExpired" or "ConfirmedPartial") && string.IsNullOrWhiteSpace(m.EffectivenessEvidence)) throw new InvalidOperationException("Nhập căn cứ xác nhận ngày/trạng thái hiệu lực; nếu chưa có thì để chưa xác định.");
        if (m.ConsultationStartDate > m.ConsultationEndDate || m.ConsultationStartUtc > m.ConsultationEndUtc) throw new InvalidOperationException("Ngày bắt đầu lấy ý kiến phải không sau ngày kết thúc.");
        var vn=TimeSpan.FromHours(7);
        var start=m.ConsultationStartDate is DateOnly startDate ? new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue),vn).ToUniversalTime() : m.ConsultationStartUtc;
        var end=m.ConsultationEndDate is DateOnly endDate && endDate != DateOnly.MaxValue ? new DateTimeOffset(endDate.AddDays(1).ToDateTime(TimeOnly.MinValue),vn).ToUniversalTime() : m.ConsultationEndUtc;
        if(start>end) throw new InvalidOperationException("Khoảng lấy ý kiến không hợp lệ khi đối chiếu ngày Việt Nam và UTC.");
        if (m.ConsultationEndDate == DateOnly.MaxValue) throw new InvalidOperationException("Ngày kết thúc vượt giới hạn.");
        if (kind is not (ContentKind.LegalDocument or ContentKind.DraftDocument) && (m.Number.Length != 0 || m.DocumentType.Length != 0 || m.IssuingBody.Length != 0 || m.Signer.Length != 0 || m.Field.Length != 0 || m.IssuedDate is not null || m.EffectiveDate is not null || m.EffectiveUntilDate is not null || m.EffectivenessStatus is not null || m.EffectivenessEvidence.Length != 0 || m.ConsultationStartDate is not null || m.ConsultationEndDate is not null || m.ConsultationStartUtc is not null || m.ConsultationEndUtc is not null || m.ConsultationDateEvidence.Length != 0)) throw new InvalidOperationException("Trường văn bản không thuộc mẫu nội dung này.");
        if(kind == ContentKind.LegalDocument && (m.ConsultationStartDate is not null || m.ConsultationEndDate is not null || m.ConsultationStartUtc is not null || m.ConsultationEndUtc is not null || m.ConsultationDateEvidence.Length != 0)) throw new InvalidOperationException("Khoảng lấy ý kiến chỉ thuộc văn bản dự thảo.");
        if(kind == ContentKind.DraftDocument && (m.EffectiveDate is not null || m.EffectiveUntilDate is not null || m.EffectivenessStatus is not null || m.EffectivenessEvidence.Length != 0)) throw new InvalidOperationException("Không suy diễn hiệu lực pháp luật cho dự thảo.");
        m.MetadataOrigin = "Editorial";
        return m;
    }
    private static bool AllowedMime(string mime) => mime is "image/jpeg" or "image/png" or "image/gif" or "image/webp" || OfficeDocumentPolicy.IsDocument(mime);
    private async Task ValidateRequiredAsync(Content content, string title, string body, int? category, int assets, string summary, string author, Guid? cover)
    {
        // Do not force manual repair of missing source metadata. This exception is
        // limited to a proven imported identity whose public category was absent;
        // new articles and clones must meet the new template requirement.
        var legacyMissingCategory = category is null && content.PrimaryCategoryId is null &&
            await db.SourceRecords.AnyAsync(x => x.ContentId == content.Id && x.Source == "qldd.mae.gov.vn");
        if (string.IsNullOrWhiteSpace(title) || (category == null && !legacyMissingCategory) || (string.IsNullOrWhiteSpace(html.PlainText(body)) && assets == 0))
            throw new InvalidOperationException("Bài mới cần tiêu đề, chuyên mục chính và nội dung hoặc tài nguyên.");
        if(category is not null && await db.Set<SiteConfiguration>().AnyAsync(x=>x.Id == 1 && x.LiveRevisionId != null) && !await db.Set<SiteCategoryConfiguration>().AnyAsync(x=>x.CategoryId == category && db.Set<SiteConfiguration>().Any(s=>s.Id == 1 && s.LiveRevisionId == x.RevisionId))) throw new InvalidOperationException("Chuyên mục chính đã ngừng áp dụng; chọn chuyên mục hiện hành trước khi gửi duyệt hoặc đăng.");
        if(content.Kind is ContentKind.Article or ContentKind.Announcement)
        {
            // Preserve only missing fields of the currently published, proven
            // source revision. New content/clones and previously completed fields
            // cannot obtain this exception by posting a metadata flag.
            var source=content.PublishedRevisionId is Guid published&&await db.SourceRecords.AnyAsync(x=>x.ContentId==content.Id)
                ?await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==published&&x.ContentId==content.Id&&x.IsSourceRevision):null;
            var hasBody=!string.IsNullOrWhiteSpace(html.PlainText(body))||new HtmlParser().ParseDocument(body).QuerySelector("img[src],video[src],a[href^='/media/']")!=null;
            if(!hasBody&&(source==null||!string.IsNullOrWhiteSpace(html.PlainText(source.BodyHtml))))throw new InvalidOperationException("Nhập nội dung bài trước khi gửi duyệt hoặc xuất bản; ảnh đại diện không thay nội dung bài.");
            if(string.IsNullOrWhiteSpace(summary)&&(source==null||!string.IsNullOrWhiteSpace(source.Summary)))throw new InvalidOperationException("Nhập sapo (mô tả ngắn) trước khi gửi duyệt hoặc xuất bản.");
            if(string.IsNullOrWhiteSpace(author)&&(source==null||!string.IsNullOrWhiteSpace(source.Author)))throw new InvalidOperationException("Nhập tên tác giả trước khi gửi duyệt hoặc xuất bản; không dùng tên đăng nhập thay tác giả.");
            if(cover==null&&(source==null||source.CoverAssetId!=null))throw new InvalidOperationException("Chọn ảnh đại diện đã được phép sử dụng trước khi gửi duyệt hoặc xuất bản.");
        }
    }
}
