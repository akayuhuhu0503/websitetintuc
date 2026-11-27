using System.Data;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

public sealed record SourceSyncRow(Guid Id,string Title,string Url,string Status,DateTimeOffset UpdatedUtc,string? Error,string? ContentId=null,string Stamp="");
public sealed record SourceSyncPage(SourceSyncSettings Settings,IReadOnlyList<SourceSyncRow> Items,int Total,int Page,string? Query,string? Status,
    Dictionary<string,int> Counts,bool Running,DateTimeOffset? NextCheck,string Tab="pending",int? SourceCategory=null,DateOnly? From=null,DateOnly? To=null);

public sealed class SourceSyncStore(PortalDbContext db)
{
    public static readonly int[] Categories = SourceCatalog.Categories.Where(c=>c.Id is not(77 or 79)).Select(c=>c.Id).ToArray();
    public static SourceSyncCycle NewCycle(SourceSyncSettings settings)=>new(){Categories=SourceCatalog.Categories.Where(x=>Categories.Contains(x.Id)&&settings.Categories.ContainsKey(x.Id)).Select(x=>x.Id).ToList()};
    public static Task LockAsync(PortalDbContext db,string key,CancellationToken ct=default) =>
        db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={key},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=3000; IF @r<0 THROW 51000,'Source sync busy; retry later',1;",ct);
    public async Task<ContentActor> RequireAsync(string userId,bool draft=false)
    {
        var actor=await new CurrentPermissionService(db).GetAsync(userId);
        if(!actor.Has(Permissions.ManageSite)||draft&&(!actor.Has(Permissions.Create)||!actor.Has(Permissions.ManageMedia)))throw new UnauthorizedAccessException();
        return actor;
    }
    public async Task<SourceSyncSettings> SettingsAsync(CancellationToken ct=default) =>
        SourceSyncPolicy.Read<SourceSyncSettings>(await db.SiteSettings.AsNoTracking().Where(x=>x.Key==SourceSyncPolicy.SettingsKey).Select(x=>x.Value).SingleOrDefaultAsync(ct)??"");
    public async Task WriteSettingsAsync(SourceSyncSettings value,CancellationToken ct=default)
    {
        var row=await db.SiteSettings.SingleOrDefaultAsync(x=>x.Key==SourceSyncPolicy.SettingsKey,ct);
        if(row==null){row=new(){Key=SourceSyncPolicy.SettingsKey};db.SiteSettings.Add(row);}
        row.Value=SourceSyncPolicy.Serialize(value);
    }
    public async Task ConfigureAsync(string user,bool? enabled,int? sourceCategory,int? targetCategory,CancellationToken ct=default)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);await LockAsync(db,SourceSyncPolicy.SettingsKey,ct);
        var actor=await RequireAsync(user);var settings=await SettingsAsync(ct);
        if(enabled.HasValue)settings.Enabled=enabled.Value;
        if(sourceCategory.HasValue)
        {
            if(!Categories.Contains(sourceCategory.Value))throw new InvalidOperationException("Chuyên mục nguồn ngoài phạm vi SYNC-01A.");
            if(targetCategory.HasValue)
            {
                if(sourceCategory==76&&targetCategory!=76)throw new InvalidOperationException("Lịch tuần phải ánh xạ vào chuyên mục Lịch tuần GDLA (76).");
                if(!actor.Covers([targetCategory.Value])||!await db.Categories.AnyAsync(x=>x.Id==targetCategory,ct))throw new UnauthorizedAccessException();
                settings.Categories[sourceCategory.Value]=targetCategory.Value;
            }
            else settings.Categories.Remove(sourceCategory.Value);
        }
        await WriteSettingsAsync(settings,ct);
        db.AuditLogs.Add(new(){Actor=user,Action="SourceSync.Configure",Detail=SourceSyncPolicy.Serialize(new{enabled,sourceCategory,targetCategory})});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task RequestAsync(string user,CancellationToken ct=default)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);await LockAsync(db,SourceSyncPolicy.SweepKey,ct);await RequireAsync(user);
        var job=await db.Jobs.SingleOrDefaultAsync(x=>x.IdempotencyKey==SourceSyncPolicy.SweepKey,ct);
        if(job==null){job=new(){Type=SourceSyncPolicy.SweepType,IdempotencyKey=SourceSyncPolicy.SweepKey};db.Jobs.Add(job);}
        if(job.Status=="Running"&&job.LeaseUntilUtc>DateTimeOffset.UtcNow)throw new InvalidOperationException("Đang kiểm tra nguồn. Vui lòng chờ kết quả.");
        if(job.Status is "Backoff" or "Paused"&&job.AvailableUtc>DateTimeOffset.UtcNow)throw new InvalidOperationException("Nguồn đang lỗi; hệ thống đang chờ trước khi thử lại.");
        await LockAsync(db,SourceSyncPolicy.SettingsKey,ct);
        var settings=await SettingsAsync(ct);settings.ConsecutiveFailures=0;if(settings.Cycle is not {FinishedUtc:null})settings.Cycle=NewCycle(settings);await WriteSettingsAsync(settings,ct);
        job.Status="Requested";job.AvailableUtc=DateTimeOffset.UtcNow;job.LeaseUntilUtc=null;
        db.AuditLogs.Add(new(){Actor=user,Action="SourceSync.Request",Detail="Complete enabled-category cycle, bounded resumable slices"});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task SetItemAsync(Guid id,string user,string action,CancellationToken ct=default)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        // Same lock order as the sweep fence: sweep before item. A retry is also
        // an explicit manual request and must work while periodic sync is off.
        if(action=="retry")await LockAsync(db,SourceSyncPolicy.SweepKey,ct);
        await LockAsync(db,"SourceSync.Item."+id,ct);await RequireAsync(user);
        var item=await db.CrawlItems.SingleAsync(x=>x.Id==id,ct);var job=await db.Jobs.SingleAsync(x=>x.Id==id&&x.Type==SourceSyncPolicy.EntryType,ct);
        if(job.Status=="Running"&&job.LeaseUntilUtc>DateTimeOffset.UtcNow)throw new InvalidOperationException("Bài đang được kiểm tra, thử lại sau.");
        if(action=="ignore"){item.Status="Ignored";job.Status="Ignored";}
        else if(action=="retry"&&item.Status is "Error" or "Review"&&job.AvailableUtc<=DateTimeOffset.UtcNow)
        {
            var sweep=await db.Jobs.SingleAsync(x=>x.IdempotencyKey==SourceSyncPolicy.SweepKey,ct);
            if(sweep.Status is "Backoff" or "Paused"&&sweep.AvailableUtc>DateTimeOffset.UtcNow)throw new InvalidOperationException("Nguồn đang lỗi; chờ hết thời gian thử lại.");
            item.Status="Queued";job.Status="Retry";job.AvailableUtc=DateTimeOffset.UtcNow;job.Attempts=0;
            if(sweep.Status!="Running"||sweep.LeaseUntilUtc<=DateTimeOffset.UtcNow)
            {sweep.Status="Requested";sweep.AvailableUtc=DateTimeOffset.UtcNow;sweep.LeaseUntilUtc=null;}
        }
        else throw new InvalidOperationException("Không thể thử lại trạng thái này hoặc chưa hết thời gian chờ.");
        item.UpdatedUtc=DateTimeOffset.UtcNow;
        db.AuditLogs.Add(new(){Actor=user,Action="SourceSync."+action,Detail=id.ToString()});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<SourceSyncPage> PageAsync(string? q,string? status,int page,CancellationToken ct=default,string tab="pending",int? sourceCategory=null,DateOnly? from=null,DateOnly? to=null)
    {
        page=Math.Clamp(page,1,10000);
        tab=tab is "review" or "history"?tab:"pending";
        var rows=from i in db.CrawlItems.AsNoTracking() join r in db.CrawlRuns.AsNoTracking() on i.CrawlRunId equals r.Id
                 where r.Status==SourceSyncPolicy.RunStatus select new{i,r};
        var counts=await rows.GroupBy(x=>x.i.Status).Select(g=>new{Status=g.Key,Count=g.Count()}).ToDictionaryAsync(x=>x.Status,x=>x.Count,ct);
        rows=tab=="history"?rows.Where(x=>x.i.Status=="Drafted"||x.i.Status=="Existing"||x.i.Status=="Ignored"):
            tab=="review"?rows.Where(x=>x.i.Status=="Review"||x.i.Status=="Error"):
            rows.Where(x=>x.i.Status=="Queued"||x.i.Status=="Ready"||x.i.Status=="Changed");
        if(!string.IsNullOrWhiteSpace(q))rows=rows.Where(x=>PortalDbContext.JsonValue(x.r.Checkpoint,"$.Title")!.Contains(q));
        if(!string.IsNullOrWhiteSpace(status))rows=rows.Where(x=>x.i.Status==status);
        if(sourceCategory.HasValue){var category=sourceCategory.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);rows=rows.Where(x=>PortalDbContext.JsonValue(x.r.Checkpoint,"$.Source.PrimaryCategoryId")==category);}
        if(from.HasValue){var date=from.Value.ToString("yyyy-MM-dd");rows=rows.Where(x=>string.Compare(PortalDbContext.JsonValue(x.r.Checkpoint,"$.Source.PublishedDate"),date)>=0);}
        if(to.HasValue){var date=to.Value.ToString("yyyy-MM-dd");rows=rows.Where(x=>string.Compare(PortalDbContext.JsonValue(x.r.Checkpoint,"$.Source.PublishedDate"),date)<=0);}
        var total=await rows.CountAsync(ct);
        var materialized=await rows.OrderByDescending(x=>x.i.UpdatedUtc).ThenBy(x=>x.i.Id).Skip((page-1)*20).Take(20).ToListAsync(ct);
        var result=materialized.Select(x=>{var data=SourceSyncPolicy.Read<SourceSyncCheckpoint>(x.r.Checkpoint);return new SourceSyncRow(x.i.Id,data.Title,x.i.SourceUrl,x.i.Status,x.i.UpdatedUtc,x.i.Error,data.ContentId?.ToString(),SourceSyncBulk.Stamp(x.i,data));}).ToList();
        var job=await db.Jobs.AsNoTracking().SingleOrDefaultAsync(x=>x.IdempotencyKey==SourceSyncPolicy.SweepKey,ct);
        return new(await SettingsAsync(ct),result,total,page,q,status,counts,job?.Status=="Running"&&job.LeaseUntilUtc>DateTimeOffset.UtcNow,job?.AvailableUtc,tab,sourceCategory,from,to);
    }
    // Wake only an existing unfinished capacity-blocked cycle. Never reset backoff,
    // a live lease, source checkpoint or request budget.
    public async Task ResumeAfterCapacityAsync(CancellationToken ct=default)
    {
        db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db,SourceSyncPolicy.SweepKey,ct);await LockAsync(db,SourceSyncPolicy.SettingsKey,ct);
        var settings=await SettingsAsync(ct);
        var job=await db.Jobs.SingleOrDefaultAsync(x=>x.IdempotencyKey==SourceSyncPolicy.SweepKey,ct);
        if(settings.Cycle is {FinishedUtc:null,WaitingForCapacity:true} cycle && settings.ConsecutiveFailures==0 && job is {Status:"Requested" or "Waiting"})
        {
            var count=await (from i in db.CrawlItems join r in db.CrawlRuns on i.CrawlRunId equals r.Id where r.Status==SourceSyncPolicy.RunStatus && (i.Status=="Queued"||i.Status=="Ready"||i.Status=="Review"||i.Status=="Error") select i.Id).CountAsync(ct);
            if(count<SourceSyncPolicy.QueueLimit){cycle.WaitingForCapacity=false;job.Status="Requested";job.AvailableUtc=DateTimeOffset.UtcNow.AddSeconds(20);await WriteSettingsAsync(settings,ct);await db.SaveChangesAsync(ct);}
        }
        await tx.CommitAsync(ct);
    }
    public async Task<(CrawlItem Item,SourceSyncCheckpoint Data)> ReadAsync(Guid id,CancellationToken ct=default)
    {
        var item=await db.CrawlItems.AsNoTracking().SingleAsync(x=>x.Id==id,ct);
        var run=await db.CrawlRuns.AsNoTracking().SingleAsync(x=>x.Id==item.CrawlRunId&&x.Status==SourceSyncPolicy.RunStatus,ct);
        return(item,SourceSyncPolicy.Read<SourceSyncCheckpoint>(run.Checkpoint));
    }
    public async Task<List<Guid>> WeeklyCandidatesAsync(ParsedSource source,int category,CancellationToken ct=default)
    {
        if(category==0)return [];
        var rows=await (from c in db.Contents.AsNoTracking() join r in db.Revisions.AsNoTracking() on c.CurrentRevisionId equals r.Id
            where c.Kind==ContentKind.StaticPage&&c.PrimaryCategoryId==category
            select new{c.Id,r.Title,r.Metadata.WeeklyYear,r.Metadata.WeeklyWeek}).ToListAsync(ct);
        var period=SourceWeeklySchedule.Period(source.Title);
        return rows.Where(x=>SourceWeeklySchedule.Normalize(x.Title)==SourceWeeklySchedule.Normalize(source.Title)||
            period.HasValue&&SourceWeeklySchedule.IsDepartment(x.Title)&&
            ((x.WeeklyYear.HasValue&&x.WeeklyWeek.HasValue?(x.WeeklyYear.Value,x.WeeklyWeek.Value):SourceWeeklySchedule.Period(x.Title))==period||SourceWeeklySchedule.CouldMatch(x.Title,period.Value)))
            .Select(x=>x.Id).ToList();
    }
    public async Task<HashSet<Guid>> KnownEntriesAsync(List<Guid> entries,CancellationToken ct)
    {
        if(entries.Count==0)return [];
        var rows=await db.CrawlItems.AsNoTracking().Where(x=>entries.Contains(x.Id)).Select(x=>new{x.Id,x.SourceId,x.SourceUrl}).ToListAsync(ct);
        var ids=rows.Select(x=>x.SourceId).Distinct().ToArray();
        var known=await db.SourceRecords.AsNoTracking().Where(x=>x.Source=="qldd.mae.gov.vn"&&ids.Contains(x.SourceId))
            .Select(x=>x.SourceId).Distinct().ToListAsync(ct);
        var hashes=rows.Select(x=>TextNormalizer.Hash(x.SourceUrl)).Distinct().ToArray();
        var aliases=await db.SourceUrlAliases.AsNoTracking().Where(x=>hashes.Contains(x.UrlHash)).Select(x=>x.UrlHash).ToListAsync(ct);
        return rows.Where(x=>known.Contains(x.SourceId)||aliases.Contains(TextNormalizer.Hash(x.SourceUrl))).Select(x=>x.Id).ToHashSet();
    }
    public static async Task<Guid?> ExistingAsync(PortalDbContext db,ParsedSource source,CancellationToken ct=default)
    {
        var urls=new[]{source.SourceUrl,source.CanonicalUrl};var hashes=urls.Select(TextNormalizer.Hash).ToArray();
        var ids=await db.SourceRecords.Where(x=>x.Source=="qldd.mae.gov.vn"&&x.SourceId==source.SourceId&&(x.Kind==ContentKind.Article||x.Kind==ContentKind.Announcement||source.PrimaryCategoryId==76&&x.Kind==ContentKind.StaticPage))
            .Select(x=>x.ContentId).Union(db.SourceUrlAliases.Where(x=>hashes.Contains(x.UrlHash)).Select(x=>x.ContentId)).Distinct().ToListAsync(ct);
        if(ids.Count>1)throw new InvalidDataException("ID/alias nguồn trỏ tới nhiều bài; cần đối soát.");
        return ids.Count==1?ids[0]:null;
    }
}
