using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

public sealed partial class WorkflowService
{
    // One CMS command transaction owns the new draft, source identity, aliases
    // and queue receipt. Never delegates to the legacy published importer.
    public Task<WorkflowResult> CreateSourceDraftAsync(Guid entryId,string expectedHash,ContentActor caller,SourceSyncMedia transfer,CancellationToken ct=default,string? assetHash=null) =>
        ExecuteAsync(null,caller,null,entryId,"SourceSyncDraft",expectedHash+(assetHash??""),async _=>
        {
            var actor=await new SourceSyncStore(db).RequireAsync(caller.UserId,draft:true);
            var item=await db.CrawlItems.SingleAsync(x=>x.Id==entryId,ct);
            var run=await db.CrawlRuns.SingleAsync(x=>x.Id==item.CrawlRunId&&x.Status==SourceSyncPolicy.RunStatus,ct);
            var checkpoint=SourceSyncPolicy.Read<SourceSyncCheckpoint>(run.Checkpoint);
            var source=checkpoint.Source??throw new InvalidOperationException("Bài chưa được thu thập đầy đủ.");
            if(item.Status is not("Ready" or "Review")||checkpoint.Hash!=expectedHash||SourceSyncPolicy.Hash(source)!=expectedHash||
                SourceSyncPolicy.Id(source.CanonicalUrl)!=item.SourceId||source.Kind is not(ContentKind.Article or ContentKind.Announcement or ContentKind.StaticPage))
                throw new InvalidOperationException("Bài nguồn đã đổi hoặc chưa sẵn sàng; xem lại trước khi tạo nháp.");
            if(source.Kind==ContentKind.StaticPage&&(source.PrimaryCategoryId!=76||checkpoint.WeeklyReviewRequired||checkpoint.ReconcileContentIds.Count>0||assetHash!=checkpoint.AssetHash))
                throw new InvalidOperationException("Lịch cần đối soát đơn vị/tuần, PDF và lịch đã nhập trước đây; chưa tạo bản trùng.");
            if(await SourceSyncStore.ExistingAsync(db,source,ct)!=null)throw new InvalidOperationException("Bài đã có trong CMS; không tạo lại hoặc ghi đè.");
            var settings=await new SourceSyncStore(db).SettingsAsync(ct);
            if(source.PrimaryCategoryId is not int sourceCategory||!settings.Categories.TryGetValue(sourceCategory,out var category)||!actor.Covers([category]))
                throw new InvalidOperationException("Chưa có ánh xạ chuyên mục hợp lệ trong phạm vi của bạn.");
            if(sourceCategory==76&&(await new SourceSyncStore(db).WeeklyCandidatesAsync(source,category,ct)).Count>0)
                throw new InvalidOperationException("Lịch tương ứng đã có trong CMS; cần đối soát trước khi tạo mới.");
            var content=new Content{Kind=source.Kind,OwnerUserId=actor.UserId,PrimaryCategoryId=category,HasEditorialChanges=true,
                SourcePublishedDate=source.PublishedDate,OriginalDateText=source.OriginalDateText,DatePrecision=source.DatePrecision};
            db.Contents.Add(content);await db.SaveChangesAsync(ct);
            var input=await transfer.PrepareAsync(content.Id,checkpoint,category,actor,ct);
            if(sourceCategory==76&&input.Assets.Count(x=>x.Usage=="Attachment")!=checkpoint.Assets.Where(x=>x.Mime=="application/pdf").Select(x=>x.Sha256).Distinct().Count())
                throw new InvalidOperationException("Chưa tiếp nhận đủ PDF lịch tuần; không tạo bản nháp thiếu tệp.");
            // Upload deduplication may clear tracking after a raced unique insert.
            // Keep the CMS command's entities tracked inside this same transaction.
            if(db.Entry(content).State==EntityState.Detached)db.Attach(content);
            if(db.Entry(run).State==EntityState.Detached)db.Attach(run);
            if(db.Entry(item).State==EntityState.Detached)db.Attach(item);
            actor=await new SourceSyncStore(db).RequireAsync(caller.UserId,draft:true);
            var draft=new ContentDraft{ContentId=content.Id};db.ContentDrafts.Add(draft);
            await ApplyAsync(draft,input,actor,content);
            content.CurrentRevisionId=await SnapshotAsync(content,draft,actor);
            db.SourceRecords.Add(new(){Source="qldd.mae.gov.vn",Kind=source.Kind,SourceId=source.SourceId,ContentId=content.Id,
                CanonicalUrl=source.CanonicalUrl,LastContentHash=expectedHash});
            foreach(var url in new[]{source.SourceUrl,source.CanonicalUrl}.Distinct())
            {
                var hash=TextNormalizer.Hash(url);
                if(await db.SourceUrlAliases.AnyAsync(x=>x.UrlHash==hash,ct))throw new InvalidOperationException("Alias nguồn đã tồn tại; cần đối soát.");
                db.SourceUrlAliases.Add(new(){ContentId=content.Id,Url=url,UrlHash=hash});
            }
            checkpoint.ContentId=content.Id;checkpoint.BaselineHash=expectedHash;
            checkpoint.BaselineAssetHash=checkpoint.AssetHash;
            run.Checkpoint=SourceSyncPolicy.Serialize(checkpoint);item.Status="Drafted";item.UpdatedUtc=DateTimeOffset.UtcNow;
            var job=await db.Jobs.SingleAsync(x=>x.Id==entryId,ct);job.Status="Drafted";
            item.Error=checkpoint.Warnings.Count==0?null:string.Join(" ",checkpoint.Warnings);
            return content;
        },before:()=>SourceSyncStore.LockAsync(db,"SourceSync.Item."+entryId,ct));

    public Task<WorkflowResult> UpdateSourceWeeklyDraftAsync(Guid entryId,Guid contentId,string hash,string assetHash,byte[] version,Guid commandId,ContentActor caller,SourceSyncMedia transfer,CancellationToken ct=default)=>
        ExecuteAsync(contentId,caller,version,commandId,"SourceSyncWeeklyUpdate",entryId+"|"+hash+"|"+assetHash,async content=>
        {
            var actor=await new SourceSyncStore(db).RequireAsync(caller.UserId,draft:true);
            var c=Existing(content);Active(c);Edit(actor,c);
            var item=await db.CrawlItems.SingleAsync(x=>x.Id==entryId,ct);
            var run=await db.CrawlRuns.SingleAsync(x=>x.Id==item.CrawlRunId&&x.Status==SourceSyncPolicy.RunStatus,ct);
            var checkpoint=SourceSyncPolicy.Read<SourceSyncCheckpoint>(run.Checkpoint);
            var source=checkpoint.Source??throw new InvalidOperationException("Thiếu nội dung nguồn.");
            if(item.Status!="Changed"||source.PrimaryCategoryId!=76||checkpoint.ContentId!=c.Id||checkpoint.Hash!=hash||checkpoint.AssetHash!=assetHash||checkpoint.WeeklyReviewRequired||
               c.CurrentRevisionId!=checkpoint.ExpectedRevisionId||await SourceSyncStore.ExistingAsync(db,source,ct)!=c.Id)
                throw new DbUpdateConcurrencyException("Nguồn hoặc bài đã đổi; tải lại để đối soát.");
            // Never overwrite an editor's working revision, rejected draft or scheduled approval.
            // HasEditorialChanges records editorial provenance and remains true
            // after publishing; the workflow state/revision identify an active edit.
            if(c.State!=WorkflowState.Published||c.CurrentRevisionId!=c.PublishedRevisionId)
                throw new InvalidOperationException("Bài đang có bản biên tập/lịch hẹn. Mở bài hiện có để đối soát, không ghi đè.");
            var revision=await RevisionAsync(c.Id,c.CurrentRevisionId);
            await CopyToDraftAsync(c,revision,actor);
            c.State=WorkflowState.Draft;c.HasEditorialChanges=true;c.SubmittedRevisionId=null;c.ApprovedRevisionId=null;c.ApprovalHash=null;
            await db.SaveChangesAsync(ct);
            var draft=await DraftAsync(c.Id)??throw new InvalidOperationException("Không có bản sửa.");
            var incoming=await transfer.PrepareAsync(c.Id,checkpoint,revision.PrimaryCategoryId??throw new InvalidOperationException("Thiếu chuyên mục."),actor,ct);
            if(incoming.Assets.Count(x=>x.Usage=="Attachment")!=checkpoint.Assets.Where(x=>x.Mime=="application/pdf").Select(x=>x.Sha256).Distinct().Count())
                throw new InvalidOperationException("Chưa tiếp nhận đủ PDF lịch tuần; giữ nguyên bản công khai.");
            if(db.Entry(c).State==EntityState.Detached)db.Attach(c);
            if(db.Entry(draft).State==EntityState.Detached)db.Attach(draft);
            if(db.Entry(run).State==EntityState.Detached)db.Attach(run);
            if(db.Entry(item).State==EntityState.Detached)db.Attach(item);
            // Preserve editorial text, dates, captions and all prior PDFs; only add verified new versions.
            foreach(var asset in incoming.Assets.Where(x=>!draft.Assets.Any(a=>a.MediaAssetId==x.MediaAssetId)))
                draft.Assets.Add(new(){ContentId=c.Id,MediaAssetId=asset.MediaAssetId,Usage=asset.Usage,Caption=asset.Caption,SortOrder=draft.Assets.Count});
            if(string.IsNullOrWhiteSpace(draft.Summary))draft.Summary=incoming.Summary;
            draft.UpdatedUtc=DateTimeOffset.UtcNow;draft.UpdatedByUserId=actor.UserId;
            c.CurrentRevisionId=await SnapshotAsync(c,draft,actor);
            checkpoint.BaselineHash=hash;checkpoint.BaselineAssetHash=assetHash;
            run.Checkpoint=SourceSyncPolicy.Serialize(checkpoint);item.Status="Drafted";item.UpdatedUtc=DateTimeOffset.UtcNow;
            (await db.Jobs.SingleAsync(x=>x.Id==entryId,ct)).Status="Drafted";
            return c;
        },before:()=>SourceSyncStore.LockAsync(db,"SourceSync.Item."+entryId,ct),after:()=>
        {
            // ExecuteAsync checked the caller's version under UPDLOCK before this
            // operation. The same transaction still holds that lock. Persisting
            // Draft for document upload advanced our own SQL rowversion; use it
            // for the final save, never a newly read concurrent writer's version.
            var current=db.Contents.Local.Single(x=>x.Id==contentId);
            db.Entry(current).Property(x=>x.RowVersion).OriginalValue=current.RowVersion;
            return Task.CompletedTask;
        });
}
