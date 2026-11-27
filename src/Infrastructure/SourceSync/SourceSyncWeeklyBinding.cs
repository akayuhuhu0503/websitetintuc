using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

public sealed record WeeklyBindingCandidate(Guid Id,string Title,string Version,int? Year,int? Week,bool CanBind);
public sealed partial class SourceSyncStore
{
    public async Task<List<WeeklyBindingCandidate>> BindingCandidatesAsync(SourceSyncCheckpoint data,ContentActor actor,CancellationToken ct)
    {
        var result=new List<WeeklyBindingCandidate>();
        foreach(var id in data.ReconcileContentIds.Take(50))
        {
            var c=await db.Contents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(c==null||!await new CurrentPermissionService(db).CanReadAsync(actor,c))continue;
            var r=await db.Revisions.AsNoTracking().SingleAsync(x=>x.Id==c.CurrentRevisionId,ct);
            var period=TargetPeriod(r);
            result.Add(new(id,r.Title,Convert.ToBase64String(c.RowVersion),period?.Year,period?.Week,
                !c.IsTrashed&&c.Kind==ContentKind.StaticPage&&r.PrimaryCategoryId==76&&CurrentPermissionService.CanEdit(actor,c)&&period.HasValue));
        }
        return result;
    }
    static (int Year,int Week)? TargetPeriod(ContentRevision r)
    {
        if(!SourceWeeklySchedule.IsDepartment(r.Title))return null;
        if(r.Metadata.WeeklyYear.HasValue||r.Metadata.WeeklyWeek.HasValue)
            return r.Metadata.WeeklyYear is int year&&r.Metadata.WeeklyWeek is int week?SourceWeeklySchedule.Period($"Tuần {week} năm {year}"):null;
        return SourceWeeklySchedule.Period(r.Title);
    }
    public async Task BindWeeklyAsync(Guid entryId,Guid contentId,string hash,string assetHash,byte[] version,Guid commandId,string user,StorageOptions storage,CancellationToken ct)
    {
        if(commandId==Guid.Empty||version.Length!=8)throw new InvalidOperationException("Thiếu phiên bản hoặc mã thao tác.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        // Same lock order as the workflow update: content before source entry.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM dbo.Contents WITH (UPDLOCK, ROWLOCK) WHERE Id={contentId}",ct);
        await LockAsync(db,"SourceSync.Item."+entryId,ct);
        var actor=await RequireAsync(user,draft:true);
        var content=await db.Contents.SingleAsync(x=>x.Id==contentId,ct);
        if(!await new CurrentPermissionService(db).CanEditAsync(actor,content))throw new UnauthorizedAccessException();
        var requestHash=TextNormalizer.Hash(entryId+"|"+contentId+"|"+hash+"|"+assetHash+"|"+Convert.ToBase64String(version));
        var receipt=await db.ContentCommands.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==commandId,ct);
        if(receipt!=null)
        {
            if(receipt.Action!="SourceSyncWeeklyBind"||receipt.ActorUserId!=user||receipt.ContentId!=contentId||receipt.RequestHash!=requestHash)throw new InvalidOperationException("Mã thao tác không khớp.");
            return;
        }
        if(!content.RowVersion.SequenceEqual(version))throw new DbUpdateConcurrencyException("Bài đã thay đổi.");
        var (item,data)=await ReadAsync(entryId,ct);
        var source=data.Source??throw new InvalidOperationException("Thiếu nguồn.");
        if(item.Status is not("Review" or "Ready")||source.PrimaryCategoryId!=76||data.Hash!=hash||SourceSyncPolicy.Hash(source)!=hash||data.AssetHash!=assetHash||
            SourceWeeklySchedule.AssetHash(data.Assets)!=assetHash||data.ContentId.HasValue||!data.ReconcileContentIds.Contains(contentId)||
            content.Kind!=ContentKind.StaticPage||content.IsTrashed||
            (await SettingsAsync(ct)).Categories.GetValueOrDefault(76)!=76)throw new InvalidOperationException("Định danh, ánh xạ hoặc bản đối soát đã đổi; chưa nối nguồn.");
        var sourcePeriod=await SourceWeeklyEvidence.ResolveAsync(data,storage,ct);
        var target=await db.Revisions.AsNoTracking().SingleAsync(x=>x.Id==content.CurrentRevisionId,ct);
        if(target.PrimaryCategoryId!=76||sourcePeriod==null||TargetPeriod(target)!=sourcePeriod)throw new InvalidOperationException("Đơn vị, năm/tuần chưa khớp duy nhất. Không nối theo tên gần giống.");
        await LockAsync(db,SourceSyncPolicy.Key(source.SourceId),ct);
        if(await ExistingAsync(db,source,ct)!=null||await db.SourceRecords.AnyAsync(x=>x.ContentId==contentId&&x.Source=="qldd.mae.gov.vn",ct))throw new InvalidOperationException("Nguồn hoặc hồ sơ đã có định danh; cần đối soát, không nối trùng.");
        db.SourceRecords.Add(new(){Source="qldd.mae.gov.vn",SourceId=source.SourceId,Kind=ContentKind.StaticPage,ContentId=contentId,CanonicalUrl=source.CanonicalUrl,LastContentHash=hash});
        foreach(var url in new[]{source.SourceUrl,source.CanonicalUrl}.Distinct())
        {
            var urlHash=TextNormalizer.Hash(url);
            if(await db.SourceUrlAliases.AnyAsync(x=>x.UrlHash==urlHash,ct))throw new InvalidOperationException("Liên kết đã thuộc hồ sơ khác.");
            db.SourceUrlAliases.Add(new(){ContentId=contentId,Url=url,UrlHash=urlHash});
        }
        data.ContentId=contentId;data.ExpectedRevisionId=content.CurrentRevisionId;data.ReconcileContentIds=[];
        data.WeeklyYear=sourcePeriod.Value.Year;data.WeeklyWeek=sourcePeriod.Value.Week;
        data.Warnings.RemoveAll(x=>x.StartsWith("Có lịch CMS cùng tiêu đề/tuần",StringComparison.Ordinal));
        var complete=!source.AssetUrls.Except(data.Assets.Select(x=>x.Url)).Any()&&data.Assets.Any(x=>x.Mime=="application/pdf");
        data.WeeklyReviewRequired=!complete;
        var hashes=await db.ContentAssets.Where(x=>x.RevisionId==content.CurrentRevisionId&&x.Usage=="Attachment").Join(db.MediaAssets,a=>a.MediaAssetId,m=>m.Id,(a,m)=>m.Sha256).ToListAsync(ct);
        var status=data.Warnings.Count>0||!complete?"Review":data.Assets.Any(x=>!hashes.Contains(x.Sha256))?"Changed":"Existing";
        (await db.CrawlRuns.SingleAsync(x=>x.Id==item.CrawlRunId,ct)).Checkpoint=SourceSyncPolicy.Serialize(data);
        var tracked=await db.CrawlItems.SingleAsync(x=>x.Id==entryId,ct);tracked.Status=status;tracked.UpdatedUtc=DateTimeOffset.UtcNow;tracked.Error=data.Warnings.Count==0?null:string.Join(" ",data.Warnings);
        (await db.Jobs.SingleAsync(x=>x.Id==entryId&&x.Type==SourceSyncPolicy.EntryType,ct)).Status=status;
        db.ContentCommands.Add(new(){Id=commandId,ContentId=contentId,ActorUserId=user,Action="SourceSyncWeeklyBind",RequestHash=requestHash,ResultRevisionId=content.CurrentRevisionId});
        db.AuditLogs.Add(new(){Actor=user,Action="SourceSyncWeeklyBind",ContentId=contentId,RevisionId=content.CurrentRevisionId,
            Detail=SourceSyncPolicy.Serialize(new{commandId,entryId,source.SourceId,hash,assetHash,year=data.WeeklyYear,week=data.WeeklyWeek,sourceHashes=data.Assets.Select(x=>x.Sha256),cmsHashes=hashes})});
        // No revision/state/date/schedule/publication mutation for identity binding.
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
