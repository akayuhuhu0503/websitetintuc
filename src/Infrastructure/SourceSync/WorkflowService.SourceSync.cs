using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

public sealed partial class WorkflowService
{
    // One CMS command transaction owns the new draft, source identity, aliases
    // and queue receipt. Never delegates to the legacy published importer.
    public Task<WorkflowResult> CreateSourceDraftAsync(Guid entryId,string expectedHash,ContentActor caller,SourceSyncMedia transfer,CancellationToken ct=default) =>
        ExecuteAsync(null,caller,null,entryId,"SourceSyncDraft",expectedHash,async _=>
        {
            var actor=await new SourceSyncStore(db).RequireAsync(caller.UserId,draft:true);
            var item=await db.CrawlItems.SingleAsync(x=>x.Id==entryId,ct);
            var run=await db.CrawlRuns.SingleAsync(x=>x.Id==item.CrawlRunId&&x.Status==SourceSyncPolicy.RunStatus,ct);
            var checkpoint=SourceSyncPolicy.Read<SourceSyncCheckpoint>(run.Checkpoint);
            var source=checkpoint.Source??throw new InvalidOperationException("Bài chưa được thu thập đầy đủ.");
            if(item.Status is not("Ready" or "Review")||checkpoint.Hash!=expectedHash||SourceSyncPolicy.Hash(source)!=expectedHash||
                SourceSyncPolicy.Id(source.CanonicalUrl)!=item.SourceId||source.Kind is not(ContentKind.Article or ContentKind.Announcement))
                throw new InvalidOperationException("Bài nguồn đã đổi hoặc chưa sẵn sàng; xem lại trước khi tạo nháp.");
            if(await SourceSyncStore.ExistingAsync(db,source,ct)!=null)throw new InvalidOperationException("Bài đã có trong CMS; không tạo lại hoặc ghi đè.");
            var settings=await new SourceSyncStore(db).SettingsAsync(ct);
            if(source.PrimaryCategoryId is not int sourceCategory||!settings.Categories.TryGetValue(sourceCategory,out var category)||!actor.Covers([category]))
                throw new InvalidOperationException("Chưa có ánh xạ chuyên mục hợp lệ trong phạm vi của bạn.");
            var content=new Content{Kind=source.Kind,OwnerUserId=actor.UserId,PrimaryCategoryId=category,HasEditorialChanges=true,
                SourcePublishedDate=source.PublishedDate,OriginalDateText=source.OriginalDateText,DatePrecision=source.DatePrecision};
            db.Contents.Add(content);await db.SaveChangesAsync(ct);
            var input=await transfer.PrepareAsync(content.Id,checkpoint,category,actor,ct);
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
            run.Checkpoint=SourceSyncPolicy.Serialize(checkpoint);item.Status="Drafted";item.UpdatedUtc=DateTimeOffset.UtcNow;
            item.Error=checkpoint.Warnings.Count==0?null:string.Join(" ",checkpoint.Warnings);
            return content;
        },before:()=>SourceSyncStore.LockAsync(db,"SourceSync.Item."+entryId,ct));
}
