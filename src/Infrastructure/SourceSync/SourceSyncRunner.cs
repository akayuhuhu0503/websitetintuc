using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LandPortal.Domain;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

// The factory is replaced only by isolated tests. Runtime always uses the
// bounded, DNS-pinned, redirect-checked client used by the source collector.
public interface IQlddSourceSession : IDisposable
{
    int RequestsUsed { get; }
    Task<SourceResponse> FetchAsync(string url,int maximum,bool page,CancellationToken ct);
}
public interface IQlddSourceFactory { IQlddSourceSession Open(); }
public sealed class QlddSourceFactory : IQlddSourceFactory
{
    public IQlddSourceSession Open()=>new Session();
    sealed class Session:IQlddSourceSession
    {
        readonly SafeSourceClient client=new(SourceSyncPolicy.RequestsPerRun);
        public int RequestsUsed=>client.RequestsUsed;
        public Task<SourceResponse> FetchAsync(string url,int maximum,bool page,CancellationToken ct)=>client.FetchAsync(url,maximum,ct,page);
        public void Dispose()=>client.Dispose();
    }
}
public sealed class SourceSyncRunner(PortalDbContext db,StorageOptions storage,MediaService media,IQlddSourceFactory factory)
{
    readonly SourceSyncStore store=new(db);
    public async Task<bool> RunOnceAsync(CancellationToken stopping=default)
    {
        var claim=await ClaimAsync(stopping);if(claim==null)return false;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stopping);deadline.CancelAfter(TimeSpan.FromSeconds(100));var ct=deadline.Token;
        using var source=factory.Open();string? error=null;
        try
        {
            var robots=await source.FetchAsync(SourceCatalog.BaseUrl+"/robots.txt",128*1024,true,ct);
            if(robots.Status!=404&&robots.Status!=200)throw new IOException("Không đọc được quy tắc truy cập nguồn.");
            var allowed=new SourceSyncRobots.Session(source,new SourceSyncRobots(robots.Status==200?Encoding.UTF8.GetString(robots.Bytes):""));
            var settings=await store.SettingsAsync(ct);
            var cycle=settings.Cycle??throw new InvalidOperationException("Thiếu checkpoint chu kỳ.");
            // Discover one bounded list page, checkpoint immediately, then drain new work first.
            if(cycle.CategoryIndex<cycle.Categories.Count)
            {
                var category=SourceCatalog.Categories.Single(x=>x.Id==cycle.Categories[cycle.CategoryIndex]);
                var listUrl=cycle.NextListUrl??SourceCatalog.BaseUrl+category.Path;
                var response=await allowed.FetchAsync(listUrl,2*1024*1024,true,ct);RequirePage(response);await SnapshotAsync(claim.Id,response,ct);
                var body=Encoding.UTF8.GetString(response.Bytes);
                var links=SourceParser.DiscoverNewsLinks(body,response.Url,category.Id).Where(x=>SourceSyncPolicy.Id(x.Url)==x.SourceId).ToArray();
                var full=false;
                foreach(var link in links)
                {
                    var known=await db.Jobs.AnyAsync(x=>x.IdempotencyKey==SourceSyncPolicy.Key(link.SourceId),ct);
                    var id=await DiscoverAsync(link,ct);
                    if(id==null){full=true;break;}
                    if(!cycle.Seen.Contains(id.Value))
                    {cycle.Seen.Add(id.Value);cycle.Pending.Add(id.Value);if(!known)cycle.NewEntries++;}
                }
                var pagination=SourceParser.ReadPagination(body);
                cycle.WaitingForCapacity=full;
                if(full){var message="Hàng chờ đạt giới hạn 200 bài; xử lý các bài đang chờ. Trang nguồn này được giữ lại để tiếp tục.";if(!cycle.Warnings.Contains(message))cycle.Warnings.Add(message);}
                else if(cycle.ListPage<Math.Min(pagination.TotalPages,SourceSyncPolicy.ListPagesPerCategory)&&pagination.NextTemplate.Length>0)
                {
                    var next=new Uri(new Uri(response.Url),pagination.NextTemplate.Replace("{page}",(cycle.ListPage+1).ToString()));
                    if(next.Host!="qldd.mae.gov.vn"||next.Scheme!="https"||!next.IsDefaultPort||!next.AbsolutePath.StartsWith("/chuyen-muc/"))throw new InvalidDataException("Liên kết phân trang ngoài phạm vi.");
                    cycle.ListPage++;cycle.NextListUrl=next.AbsoluteUri;
                }
                else
                {
                    if(pagination.TotalPages>SourceSyncPolicy.ListPagesPerCategory)cycle.Warnings.Add(category.Name+": đã kiểm tra 5 trang gần nhất; không tuyên bố bao phủ toàn bộ lịch sử.");
                    cycle.CategoryIndex++;cycle.ListPage=1;cycle.NextListUrl=null;
                }
                await SaveCycleAsync(claim,cycle,ct);
            }
            // Entries discovered in an earlier slice/restart remain eligible even when absent from today's list.
            var now=DateTimeOffset.UtcNow;
            var queued=await db.Jobs.AsNoTracking().Where(x=>x.Type==SourceSyncPolicy.EntryType&&
                (x.Status=="Pending"||x.Status=="Queued"||x.Status=="Retry")&&x.AvailableUtc<=now)
                .OrderBy(x=>x.AvailableUtc).Take(SourceSyncPolicy.QueueLimit).Select(x=>x.Id).ToListAsync(ct);
            foreach(var id in queued)if(!cycle.Pending.Contains(id)){cycle.Pending.Add(id);if(!cycle.Seen.Contains(id))cycle.Seen.Add(id);}
            var pending=await db.Jobs.AsNoTracking().Where(x=>cycle.Pending.Contains(x.Id)).ToListAsync(ct);
            // Ignored/failed/unchanged historical rows never consume the new-item allowance.
            foreach(var job in pending.Where(x=>x.Status=="Ignored"||x.Status=="Error"&&x.Attempts>=3||x.AvailableUtc>now&&x.Status is not("Pending" or "Queued" or "Retry")))cycle.Pending.Remove(job.Id);
            // A first discovery may already exist in CMS from an older import.
            // Classify identities in one batch before allocating the new-item budget.
            var knownCms=await store.KnownEntriesAsync(cycle.Pending,ct);
            var newJobs=pending.Where(x=>cycle.Pending.Contains(x.Id)&&!knownCms.Contains(x.Id)&&x.AvailableUtc<=now&&x.Status is "Pending" or "Queued" or "Retry").OrderBy(x=>x.AvailableUtc).Take(SourceSyncPolicy.DetailsPerRun).ToList();
            var historical=pending.Where(x=>cycle.Pending.Contains(x.Id)&&x.AvailableUtc<=now&&!newJobs.Any(n=>n.Id==x.Id)&&(knownCms.Contains(x.Id)||x.Status is not("Pending" or "Queued" or "Retry"))).OrderBy(x=>x.AvailableUtc).Take(1);
            foreach(var job in newJobs.Concat(historical))
            {
                if(source.RequestsUsed>=SourceSyncPolicy.RequestsPerRun-2)break;
                await ProcessAsync(job,claim,allowed,ct);
                if(!await db.CrawlItems.AnyAsync(x=>x.Id==job.Id&&x.Status=="Queued",ct))cycle.Pending.Remove(job.Id);cycle.DetailsChecked++;
                if(await db.CrawlItems.AnyAsync(x=>x.Id==job.Id&&x.Status=="Error",ct))cycle.Errors++;
                await SaveCycleAsync(claim,cycle,ct);
            }
            if(cycle.CategoryIndex==cycle.Categories.Count&&cycle.Pending.Count==0)cycle.FinishedUtc=DateTimeOffset.UtcNow;
            await SaveCycleAsync(claim,cycle,ct);
        }
        catch(OperationCanceledException)when(stopping.IsCancellationRequested){error="Ứng dụng đã dừng; sẽ tiếp tục từ hàng chờ khi chạy lại.";}
        catch(Exception e)when(e is not OutOfMemoryException){error=SafeError(e);}
        finally{await FinishAsync(claim,error,source.RequestsUsed,CancellationToken.None);}
        return true;
    }
    async Task SaveCycleAsync(Job claim,SourceSyncCycle cycle,CancellationToken ct)
    {
        db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(ct);
        await SourceSyncStore.LockAsync(db,SourceSyncPolicy.SweepKey,ct);
        if(!await db.Jobs.AnyAsync(x=>x.Id==claim.Id&&x.Attempts==claim.Attempts&&x.LeaseUntilUtc>DateTimeOffset.UtcNow,ct))throw new InvalidOperationException("Phiên đồng bộ đã hết hạn.");
        await SourceSyncStore.LockAsync(db,SourceSyncPolicy.SettingsKey,ct);
        var settings=await store.SettingsAsync(ct);settings.Cycle=cycle;await store.WriteSettingsAsync(settings,ct);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);db.ChangeTracker.Clear();
    }
    public async Task<Job?> ClaimAsync(CancellationToken ct=default)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);await SourceSyncStore.LockAsync(db,SourceSyncPolicy.SweepKey,ct);
        var settings=await store.SettingsAsync(ct);var now=DateTimeOffset.UtcNow;
        var job=await db.Jobs.SingleOrDefaultAsync(x=>x.IdempotencyKey==SourceSyncPolicy.SweepKey,ct);
        if(job==null&&!settings.Enabled)return null; // No SQL writes on a new disabled installation.
        if(job==null){job=new(){IdempotencyKey=SourceSyncPolicy.SweepKey,Type=SourceSyncPolicy.SweepType};db.Jobs.Add(job);}
        if(job.Status=="Paused"||job.Status=="Running"&&job.LeaseUntilUtc>now||job.AvailableUtc>now||!settings.Enabled&&job.Status!="Requested"&&job.Status!="Running"&&settings.Cycle is not {FinishedUtc:null})return null;
        await SourceSyncStore.LockAsync(db,SourceSyncPolicy.SettingsKey,ct);
        settings=await store.SettingsAsync(ct);
        if(settings.Cycle is not {FinishedUtc:null})settings.Cycle=SourceSyncStore.NewCycle(settings);
        await store.WriteSettingsAsync(settings,ct);
        job.Status="Running";job.LeaseUntilUtc=now.AddMinutes(3);job.Attempts++;job.Error=null;
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        var result=new Job{Id=job.Id,Attempts=job.Attempts,LeaseUntilUtc=job.LeaseUntilUtc};db.ChangeTracker.Clear();return result;
    }
    async Task FinishAsync(Job claim,string? error,int requests,CancellationToken ct)
    {
        db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(ct);
        await SourceSyncStore.LockAsync(db,SourceSyncPolicy.SweepKey,ct);
        var job=await db.Jobs.SingleAsync(x=>x.Id==claim.Id,ct);
        if(job.Attempts!=claim.Attempts||job.Status!="Running")return;
        await SourceSyncStore.LockAsync(db,SourceSyncPolicy.SettingsKey,ct);
        var settings=await store.SettingsAsync(ct);settings.LastCheckedUtc=DateTimeOffset.UtcNow;settings.LastError=error;
        settings.ConsecutiveFailures=error==null?0:settings.ConsecutiveFailures+1;
        job.Status=error==null?(settings.Cycle is {FinishedUtc:null}?"Requested":"Waiting"):settings.ConsecutiveFailures>=3?"Paused":"Backoff";job.Error=error;job.LeaseUntilUtc=null;
        job.AvailableUtc=error==null&&settings.Cycle is {FinishedUtc:null,WaitingForCapacity:false}?DateTimeOffset.UtcNow.AddSeconds(20):DateTimeOffset.UtcNow.AddMinutes(error==null?5:Math.Min(60,15*settings.ConsecutiveFailures));
        if(job.Status=="Paused")settings.LastError+=" Đã tạm dừng sau 3 lượt lỗi liên tiếp. Sau thời gian chờ, chọn Kiểm tra bài mới ngay để tiếp tục.";
        await store.WriteSettingsAsync(settings,ct);
        db.AuditLogs.Add(new(){Actor="source-sync",Action="SourceSync.Check",Detail=SourceSyncPolicy.Serialize(new{requests,error,claim.Attempts})});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<Guid?> DiscoverAsync(DiscoveredLink link,CancellationToken ct=default)
    {
        if(SourceSyncPolicy.Id(link.Url)!=link.SourceId||link.Kind is not(ContentKind.Article or ContentKind.Announcement))throw new InvalidDataException("Nguồn/định danh không hợp lệ.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await SourceSyncStore.LockAsync(db,SourceSyncPolicy.Key(link.SourceId),ct);
        var existing=await db.Jobs.SingleOrDefaultAsync(x=>x.IdempotencyKey==SourceSyncPolicy.Key(link.SourceId),ct);
        if(existing!=null)return existing.Id;
        var count=await (from i in db.CrawlItems join r in db.CrawlRuns on i.CrawlRunId equals r.Id
            where r.Status==SourceSyncPolicy.RunStatus&&(i.Status=="Queued"||i.Status=="Ready"||i.Status=="Review"||i.Status=="Error") select i.Id).CountAsync(ct);
        if(count>=SourceSyncPolicy.QueueLimit)return null;
        var id=Guid.NewGuid();
        db.Jobs.Add(new(){Id=id,IdempotencyKey=SourceSyncPolicy.Key(link.SourceId),Type=SourceSyncPolicy.EntryType,Status="Queued"});
        db.CrawlRuns.Add(new(){Id=id,Status=SourceSyncPolicy.RunStatus,ContentBudget=1,RequestBudget=SourceSyncPolicy.MaxAssets+1,Checkpoint=SourceSyncPolicy.Serialize(new SourceSyncCheckpoint())});
        db.CrawlItems.Add(new(){Id=id,CrawlRunId=id,Kind=link.Kind,SourceUrl=link.Url,SourceId=link.SourceId,Status="Queued"});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);db.ChangeTracker.Clear();return id;
    }
    async Task ProcessAsync(Job job,Job sweep,IQlddSourceSession source,CancellationToken ct)
    {
        var existing=await store.ReadAsync(job.Id,ct);if(existing.Item.Status=="Ignored")return;
        var data=existing.Data;var item=existing.Item;var originalCheckpoint=SourceSyncPolicy.Serialize(data);var warnings=new List<string>();string status;
        try
        {
            var response=await source.FetchAsync(item.SourceUrl,2*1024*1024,true,ct);RequirePage(response);
            if(SourceSyncPolicy.Id(response.Url)!=item.SourceId)throw new SourceResourceException("SOURCE_IDENTITY");
            await SnapshotAsync(item.Id,response,ct);
            ParsedSource parsed;
            try{parsed=SourceParser.Parse(Encoding.UTF8.GetString(response.Bytes),response.Url,item.Kind,item.SourceId);}
            catch(InvalidDataException){throw new SourceResourceException("ARTICLE_STRUCTURE");}
            if(parsed.SourceId!=item.SourceId||SourceSyncPolicy.Id(parsed.CanonicalUrl)!=item.SourceId)throw new SourceResourceException("SOURCE_IDENTITY");
            if(parsed.PrimaryCategoryId is not int category||!SourceSyncStore.Categories.Contains(category))throw new SourceResourceException("CATEGORY_SCOPE");
            parsed.Kind=category==76?ContentKind.StaticPage:category==52?ContentKind.Announcement:ContentKind.Article;
            var hash=SourceSyncPolicy.Hash(parsed);var oldHash=data.Hash;
            try{data.ContentId=await SourceSyncStore.ExistingAsync(db,parsed,ct);}
            catch(InvalidDataException){throw new SourceResourceException("IDENTITY_CONFLICT");}
            if(data.BaselineHash=="")
            {
                var previous=data.ContentId.HasValue?await PreviousHashAsync(parsed,item.Id,ct):null;
                data.BaselineObservedOnly=data.ContentId.HasValue&&previous==null;
                data.BaselineHash=previous??hash;
            }
            data.Title=parsed.Title;data.Source=parsed;data.Hash=hash;data.CheckedUtc=DateTimeOffset.UtcNow;
            var weekly=category==76;
            var period=weekly?SourceWeeklySchedule.Period(parsed.Title):null;
            data.WeeklyYear=period?.Year;data.WeeklyWeek=period?.Week;
            var previousAssets=data.Assets.ToList();
            data.WeeklyReviewRequired=weekly&&(!SourceWeeklySchedule.IsDepartment(parsed.Title)||SourceWeeklySchedule.Period(parsed.Title)==null);
            if(weekly&&!data.ContentId.HasValue)
            {
                var settings=await store.SettingsAsync(ct);
                data.ReconcileContentIds=await store.WeeklyCandidatesAsync(parsed,settings.Categories.GetValueOrDefault(76),ct);
            }
            if(data.ContentId.HasValue&&!weekly)
            {
                status=hash!=data.BaselineHash?"Changed":"Existing";
                // No downloads, draft mutations, restore or publication for existing CMS content.
                warnings.Add("Bài đã có trên GDLA. Chỉ đối chiếu nguồn; không sửa bản biên tập/công khai.");
                if(data.BaselineObservedOnly)warnings.Add("Không có snapshot cũ để chứng minh nguồn có thay đổi trước lần kiểm tra đầu. Dùng lần quan sát đầu làm mốc so sánh tiếp theo.");
            }
            else
            {
                var activeCycle=(await store.SettingsAsync(ct)).Cycle?.Id;
                if(data.AssetCycleId!=activeCycle||oldHash!=hash||item.Status is "Error" or "Review")
                {data.Assets=[];data.AssetCycleId=activeCycle;}
                if(data.Assets.Count==0||parsed.AssetUrls.Except(data.Assets.Select(x=>x.Url)).Any()||weekly)
                {
                    long total=data.Assets.Sum(x=>x.Bytes);
                    foreach(var url in parsed.AssetUrls.Except(data.Assets.Select(x=>x.Url)).Take(SourceSyncPolicy.MaxAssets))
                    {
                        try
                        {
                            if(source.RequestsUsed>=SourceSyncPolicy.RequestsPerRun-1)break;
                            var asset=await DownloadAsync(source,url,ct);total+=asset.Bytes;
                            if(total>40L*1024*1024){warnings.Add("Tài nguyên vượt 40 MiB/bài; phần còn lại cần kiểm tra.");break;}
                            var prior=previousAssets.FirstOrDefault(x=>x.Url==asset.Url&&x.Sha256==asset.Sha256);
                            data.Assets.Add(prior??asset);
                        }
                        catch(OperationCanceledException){throw;}
                        catch(RequestBudgetException){break;}
                        catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or HttpRequestException or MediaUploadException){warnings.Add("Không nhận được tệp "+SafeName(url)+": "+SafeError(e));}
                    }
                }
                var settings=await store.SettingsAsync(ct);
                warnings.AddRange(parsed.Warnings);
                if(!settings.Categories.TryGetValue(category,out var mapped)||!await db.Categories.AnyAsync(x=>x.Id==mapped,ct))warnings.Add("Chưa ánh xạ chuyên mục nguồn. Chọn chuyên mục đích trong Đồng bộ QLDD.");
                if(weekly)
                {
                    var complete=!parsed.AssetUrls.Except(data.Assets.Select(x=>x.Url)).Any();
                    period=await SourceWeeklyEvidence.ResolveAsync(data,storage,ct);
                    data.WeeklyYear=period?.Year;data.WeeklyWeek=period?.Week;
                    data.WeeklyReviewRequired=period==null;
                    if(period==null&&(complete||warnings.Count>0))warnings.Add("Lịch cần đối soát đơn vị Lãnh đạo Cục và tuần/năm; chưa có bằng chứng từ tiêu đề/PDF đã kiểm định.");
                    if(!data.ContentId.HasValue)
                    {
                        data.ReconcileContentIds=await store.WeeklyCandidatesAsync(parsed,settings.Categories.GetValueOrDefault(76),ct,period);
                        if(complete&&data.ReconcileContentIds.Count>0)warnings.Add("Có lịch CMS cùng tiêu đề/tuần đã nhập trước đây nhưng chưa gắn định danh nguồn. Đối soát bài hiện có; chưa tạo bài trùng.");
                    }
                    if((complete||warnings.Count>0)&&!data.Assets.Any(x=>x.Mime=="application/pdf")){warnings.Add("Chưa nhận được PDF lịch tuần.");data.WeeklyReviewRequired=true;}
                    if(!complete)data.WeeklyReviewRequired=true;
                    data.AssetHash=SourceWeeklySchedule.AssetHash(data.Assets);
                    if(data.BaselineAssetHash.Length==0&&complete)data.BaselineAssetHash=data.AssetHash;
                    if(data.ContentId is Guid existingId)
                    {
                        data.ExpectedRevisionId=await db.Contents.Where(x=>x.Id==existingId).Select(x=>x.CurrentRevisionId).SingleAsync(ct);
                        // For previously imported calendars, compare actual revision attachment hashes even on first observation.
                        var publishedHashes=await db.ContentAssets.Where(x=>x.RevisionId==data.ExpectedRevisionId&&x.Usage=="Attachment")
                            .Join(db.MediaAssets,a=>a.MediaAssetId,m=>m.Id,(a,m)=>m.Sha256).ToListAsync(ct);
                        var differs=hash!=data.BaselineHash||data.AssetHash!=data.BaselineAssetHash||data.Assets.Where(x=>x.Mime=="application/pdf").Any(x=>!publishedHashes.Contains(x.Sha256));
                        status=warnings.Count>0?"Review":differs?"Changed":"Existing";
                    }
                    else status=warnings.Count==0&&data.ReconcileContentIds.Count==0?"Ready":"Review";
                    if(!complete&&warnings.Count==0){status="Queued";data.WeeklyReviewRequired=true;}
                    else if(!complete){status="Review";warnings.Add("Chưa nhận đủ tài nguyên lịch tuần; cần kiểm tra trước khi chuyển CMS.");}
                }
                else
                {
                    var complete=!parsed.AssetUrls.Except(data.Assets.Select(x=>x.Url)).Any();
                    data.AssetHash=SourceWeeklySchedule.AssetHash(data.Assets);
                    status=warnings.Count>0?"Review":complete?"Ready":"Queued";
                    if(!complete&&warnings.Count>0)warnings.Add("Còn tài nguyên nguồn chưa nhận/kiểm định được; cần biên tập bổ sung.");
                }
            }
            warnings.AddRange(parsed.Warnings);data.Warnings=warnings.Distinct().ToList();
        }
        catch(OperationCanceledException){throw;}
        catch(Exception e)when(e is not OutOfMemoryException){status="Error";warnings.Add(SafeError(e));data.Warnings=warnings;}
        db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(ct);
        await SourceSyncStore.LockAsync(db,"SourceSync.Item."+job.Id,ct);
        if(!await db.Jobs.AnyAsync(x=>x.Id==sweep.Id&&x.Status=="Running"&&x.Attempts==sweep.Attempts&&x.LeaseUntilUtc>DateTimeOffset.UtcNow,ct))throw new InvalidOperationException("Phiên đồng bộ đã hết hạn.");
        var current=await db.CrawlItems.SingleAsync(x=>x.Id==job.Id,ct);
        // A manual skip or completed draft always wins over an in-flight fetch.
        if(current.Status=="Ignored"||current.Status=="Drafted"&&item.Status!="Drafted")return;
        var run=await db.CrawlRuns.SingleAsync(x=>x.Id==job.Id,ct);
        // A binding/update committed while the network request was in flight wins.
        // Do not restore stale identity, revision or media evidence from the fetch.
        if(SourceSyncPolicy.Serialize(SourceSyncPolicy.Read<SourceSyncCheckpoint>(run.Checkpoint))!=originalCheckpoint)return;
        run.Checkpoint=SourceSyncPolicy.Serialize(data);
        current.Status=status;current.Kind=data.Source?.Kind??current.Kind;current.Attempts++;current.UpdatedUtc=DateTimeOffset.UtcNow;
        current.Error=data.Warnings.Count==0?null:string.Join(" ",data.Warnings);
        var tracked=await db.Jobs.SingleAsync(x=>x.Id==job.Id,ct);tracked.Status=status;tracked.Attempts=status=="Error"?tracked.Attempts+1:0;
        tracked.AvailableUtc=status=="Queued"?DateTimeOffset.UtcNow.AddSeconds(20):DateTimeOffset.UtcNow.AddMinutes(status=="Error"?Math.Min(120,15*Math.Max(1,tracked.Attempts)):30);
        if(status=="Error")db.ImportIssues.Add(new(){CrawlRunId=job.Id,SourceUrl=current.SourceUrl,Stage="SourceSync",Code="FETCH_FAILED",Message=current.Error??"Lỗi nguồn."});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);db.ChangeTracker.Clear();
    }
    async Task<string?> PreviousHashAsync(ParsedSource parsed,Guid entry,CancellationToken ct)
    {
        var snapshots=await db.SourceSnapshots.AsNoTracking().Where(x=>x.CrawlRunId!=entry&&x.HttpStatus==200&&(x.SourceUrl==parsed.SourceUrl||x.SourceUrl==parsed.CanonicalUrl))
            .OrderByDescending(x=>x.FetchedUtc).Take(3).ToListAsync(ct);
        foreach(var s in snapshots)try
        {
            var bytes=await File.ReadAllBytesAsync(storage.ResolveSnapshot(s.StorageKey),ct);
            if(Convert.ToHexStringLower(SHA256.HashData(bytes))!=s.Sha256)continue;
            return SourceSyncPolicy.Hash(SourceParser.Parse(Encoding.UTF8.GetString(bytes),s.SourceUrl,parsed.Kind,parsed.SourceId));
        }catch(Exception e)when(e is IOException or InvalidDataException){}
        return null;
    }
    async Task<SourceSyncAsset> DownloadAsync(IQlddSourceSession source,string url,CancellationToken ct)
    {
        var name=SafeName(url);var extension=Path.GetExtension(name).ToLowerInvariant();
        string mime;
        try{mime=extension switch{".jpg" or ".jpeg"=>"image/jpeg",".png"=>"image/png",".gif"=>"image/gif",".webp"=>"image/webp",_=>OfficeDocumentPolicy.Mime(name)};}
        catch(InvalidOperationException){throw new SourceResourceException("DOCUMENT_FORMAT");}
        var response=await source.FetchAsync(url,10*1024*1024,false,ct);
        SourceSyncResourcePolicy.ValidateResponse(response,mime);
        var key=await StoreBytesAsync(response.Bytes,"resource",ct);var path=storage.ResolveSnapshot(key);
        try
        {
            if(mime.StartsWith("image/"))media.ValidateContent(path,mime,ct);
            else{OfficeDocumentPolicy.Validate(path,mime,media,managedWithoutAv:true);if(mime=="application/pdf")ManagedPdfPolicy.Validate(path,ct);}
        }
        catch(MediaUploadException e)
        {throw new SourceResourceException(mime.StartsWith("image/")?(e.StatusCode==413?"IMAGE_LIMIT":"IMAGE_INVALID"):"FILE_INVALID");}
        catch(InvalidDataException){throw new SourceResourceException(mime=="application/pdf"?"PDF_STRUCTURE":"DOCUMENT_FORMAT");}
        return new(url,key,Convert.ToHexStringLower(SHA256.HashData(response.Bytes)),response.Bytes.Length,mime,name);
    }
    public async Task SnapshotAsync(Guid run,SourceResponse response,CancellationToken ct)
    {
        var hash=Convert.ToHexStringLower(SHA256.HashData(response.Bytes));
        if(await db.SourceSnapshots.AnyAsync(x=>x.CrawlRunId==run&&x.SourceUrl==response.Url&&x.Sha256==hash,ct))return;
        var key=await StoreBytesAsync(response.Bytes,"html",ct);
        db.SourceSnapshots.Add(new(){CrawlRunId=run,SourceUrl=response.Url,StorageKey=key,Sha256=hash,HttpStatus=response.Status,ContentType=response.ContentType,FetchedUtc=response.FetchedUtc});
        await db.SaveChangesAsync(ct);db.ChangeTracker.Clear();
    }
    async Task<string> StoreBytesAsync(byte[] bytes,string type,CancellationToken ct)
    {
        var key="source-sync/"+type+"/"+Guid.NewGuid().ToString("N")+".bin";var path=storage.ResolveSnapshot(key);
        MediaService.ValidatePrivateStorageRoot(storage.SnapshotRoot,storage);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true);await output.WriteAsync(bytes,ct);return key;
    }
    static void RequirePage(SourceResponse response)
    {if(response.Status!=200||response.ContentType is not("text/html" or "application/xhtml+xml"))throw new IOException("Trang nguồn chưa sẵn sàng (HTTP "+response.Status+").");}
    static string SafeName(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var u)?Path.GetFileName(Uri.UnescapeDataString(u.AbsolutePath)):"tệp";
    static string SafeError(Exception error)=>error switch{
        SourceResourceException resource=>resource.SafeSummary,
        SourceTransportException transport=>transport.SafeSummary,
        RequestBudgetException=>"Đã đạt giới hạn truy cập nguồn; sẽ tiếp tục an toàn ở lượt sau.",
        OperationCanceledException=>"Nguồn phản hồi quá thời gian cho phép; dữ liệu trước đó được giữ nguyên.",
        MediaUploadException=>"Tệp không đạt kiểm định hiện hành.",
        InvalidDataException=>"Dữ liệu nguồn chưa đúng cấu trúc/định danh hoặc tệp không hợp lệ; cần kiểm tra.",
        _=>"Chưa lấy được dữ liệu nguồn ("+error.GetType().Name+"). Hệ thống sẽ thử lại có giới hạn."
    };
}
