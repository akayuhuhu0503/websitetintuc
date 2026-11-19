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
            var catalog=SourceCatalog.Categories.Where(x=>SourceSyncStore.Categories.Contains(x.Id)).ToArray();
            var lists=new List<(string Url,int? Category)>{(SourceCatalog.BaseUrl+"/",null)};
            for(var n=0;n<2;n++){var c=catalog[(settings.Cursor+n)%catalog.Length];lists.Add((SourceCatalog.BaseUrl+c.Path,c.Id));}
            var links=new List<DiscoveredLink>();
            foreach(var list in lists)
            {
                var response=await allowed.FetchAsync(list.Url,2*1024*1024,true,ct);RequirePage(response);await SnapshotAsync(claim.Id,response,ct);
                links.AddRange(SourceParser.DiscoverNewsLinks(Encoding.UTF8.GetString(response.Bytes),response.Url,list.Category).Take(10)
                    .Where(x=>SourceSyncPolicy.Id(x.Url)==x.SourceId));
            }
            var candidates=links.DistinctBy(x=>x.SourceId).Take(SourceSyncPolicy.RecentWindow).ToArray();
            foreach(var link in candidates)await DiscoverAsync(link,ct);
            var now=DateTimeOffset.UtcNow;
            // Recently seen rows only, plus explicit retry; never walk all history.
            var keys=candidates.Select(x=>SourceSyncPolicy.Key(x.SourceId)).ToArray();
            var jobs=await db.Jobs.AsNoTracking().Where(x=>x.Type==SourceSyncPolicy.EntryType&&x.AvailableUtc<=now&&
                x.Status!="Ignored"&&!(x.Status=="Error"&&x.Attempts>=3)&&
                (keys.Contains(x.IdempotencyKey)||x.Status=="Retry")).OrderBy(x=>x.AvailableUtc).Take(SourceSyncPolicy.DetailsPerRun).ToListAsync(ct);
            foreach(var job in jobs)
            {
                if(source.RequestsUsed>=SourceSyncPolicy.RequestsPerRun-2)break;
                await ProcessAsync(job,claim,allowed,ct);
            }
            await using var tx=await db.Database.BeginTransactionAsync(ct);await SourceSyncStore.LockAsync(db,SourceSyncPolicy.SweepKey,ct);
            if(!await db.Jobs.AnyAsync(x=>x.Id==claim.Id&&x.Attempts==claim.Attempts&&x.LeaseUntilUtc>DateTimeOffset.UtcNow,ct))return true;
            await SourceSyncStore.LockAsync(db,SourceSyncPolicy.SettingsKey,ct);
            settings=await store.SettingsAsync(ct);settings.Cursor=(settings.Cursor+2)%catalog.Length;await store.WriteSettingsAsync(settings,ct);
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }
        catch(OperationCanceledException)when(stopping.IsCancellationRequested){error="Ứng dụng đã dừng; sẽ tiếp tục từ hàng chờ khi chạy lại.";}
        catch(Exception e)when(e is not OutOfMemoryException){error=SafeError(e);}
        finally{await FinishAsync(claim,error,source.RequestsUsed,CancellationToken.None);}
        return true;
    }
    public async Task<Job?> ClaimAsync(CancellationToken ct=default)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);await SourceSyncStore.LockAsync(db,SourceSyncPolicy.SweepKey,ct);
        var settings=await store.SettingsAsync(ct);var now=DateTimeOffset.UtcNow;
        var job=await db.Jobs.SingleOrDefaultAsync(x=>x.IdempotencyKey==SourceSyncPolicy.SweepKey,ct);
        if(job==null&&!settings.Enabled)return null; // No SQL writes on a new disabled installation.
        if(job==null){job=new(){IdempotencyKey=SourceSyncPolicy.SweepKey,Type=SourceSyncPolicy.SweepType};db.Jobs.Add(job);}
        if(job.Status=="Paused"||job.Status=="Running"&&job.LeaseUntilUtc>now||job.AvailableUtc>now||!settings.Enabled&&job.Status!="Requested"&&job.Status!="Running")return null;
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
        job.Status=error==null?"Waiting":settings.ConsecutiveFailures>=3?"Paused":"Backoff";job.Error=error;job.LeaseUntilUtc=null;
        job.AvailableUtc=DateTimeOffset.UtcNow.AddMinutes(error==null?5:Math.Min(60,15*settings.ConsecutiveFailures));
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
        db.Jobs.Add(new(){Id=id,IdempotencyKey=SourceSyncPolicy.Key(link.SourceId),Type=SourceSyncPolicy.EntryType});
        db.CrawlRuns.Add(new(){Id=id,Status=SourceSyncPolicy.RunStatus,ContentBudget=1,RequestBudget=SourceSyncPolicy.MaxAssets+1,Checkpoint=SourceSyncPolicy.Serialize(new SourceSyncCheckpoint())});
        db.CrawlItems.Add(new(){Id=id,CrawlRunId=id,Kind=link.Kind,SourceUrl=link.Url,SourceId=link.SourceId,Status="Queued"});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);db.ChangeTracker.Clear();return id;
    }
    async Task ProcessAsync(Job job,Job sweep,IQlddSourceSession source,CancellationToken ct)
    {
        var existing=await store.ReadAsync(job.Id,ct);if(existing.Item.Status=="Ignored")return;
        var data=existing.Data;var item=existing.Item;var warnings=new List<string>();string status;
        try
        {
            var response=await source.FetchAsync(item.SourceUrl,2*1024*1024,true,ct);RequirePage(response);
            if(SourceSyncPolicy.Id(response.Url)!=item.SourceId)throw new InvalidDataException("Nguồn chuyển tới bài khác.");
            await SnapshotAsync(item.Id,response,ct);
            var parsed=SourceParser.Parse(Encoding.UTF8.GetString(response.Bytes),response.Url,item.Kind,item.SourceId);
            if(parsed.SourceId!=item.SourceId||SourceSyncPolicy.Id(parsed.CanonicalUrl)!=item.SourceId||
                parsed.PrimaryCategoryId is not int category||!SourceSyncStore.Categories.Contains(category))
                throw new InvalidDataException("Canonical hoặc chuyên mục chưa có bằng chứng thuộc phạm vi nguồn.");
            parsed.Kind=category==52?ContentKind.Announcement:ContentKind.Article;
            var hash=SourceSyncPolicy.Hash(parsed);var oldHash=data.Hash;
            data.ContentId=await SourceSyncStore.ExistingAsync(db,parsed,ct);
            if(data.BaselineHash=="")
            {
                var previous=data.ContentId.HasValue?await PreviousHashAsync(parsed,item.Id,ct):null;
                data.BaselineObservedOnly=data.ContentId.HasValue&&previous==null;
                data.BaselineHash=previous??hash;
            }
            data.Title=parsed.Title;data.Source=parsed;data.Hash=hash;data.CheckedUtc=DateTimeOffset.UtcNow;
            if(data.ContentId.HasValue)
            {
                status=hash!=data.BaselineHash?"Changed":"Existing";
                // No downloads, draft mutations, restore or publication for existing CMS content.
                warnings.Add("Bài đã có trên GDLA. Chỉ đối chiếu nguồn; không sửa bản biên tập/công khai.");
                if(data.BaselineObservedOnly)warnings.Add("Không có snapshot cũ để chứng minh nguồn có thay đổi trước lần kiểm tra đầu. Dùng lần quan sát đầu làm mốc so sánh tiếp theo.");
            }
            else
            {
                if(oldHash!=hash||item.Status is "Error" or "Review" or "Queued")data.Assets=[];
                if(data.Assets.Count==0)
                {
                    if(parsed.AssetUrls.Count>SourceSyncPolicy.MaxAssets)warnings.Add("Có quá nhiều tài nguyên; chỉ nhận tối đa 12 tệp/lần, cần biên tập phần còn lại.");
                    long total=0;
                    foreach(var url in parsed.AssetUrls.Take(SourceSyncPolicy.MaxAssets))
                    {
                        try
                        {
                            if(source.RequestsUsed>=SourceSyncPolicy.RequestsPerRun-1){warnings.Add("Đã đạt giới hạn truy cập; còn tài nguyên chưa nhận [REQUEST_BUDGET].");break;}
                            var asset=await DownloadAsync(source,url,ct);total+=asset.Bytes;
                            if(total>40L*1024*1024){warnings.Add("Tài nguyên vượt 40 MiB/bài; phần còn lại cần kiểm tra.");break;}
                            data.Assets.Add(asset);
                        }
                        catch(OperationCanceledException){throw;}
                        catch(RequestBudgetException){warnings.Add("Đã hết ngân sách request khi nhận tệp [REQUEST_BUDGET].");break;}
                        catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or HttpRequestException or MediaUploadException){warnings.Add("Không nhận được tệp "+SafeName(url)+": "+SafeError(e));}
                    }
                }
                var settings=await store.SettingsAsync(ct);
                if(parsed.AssetUrls.Except(data.Assets.Select(x=>x.Url)).Any())warnings.Add("Còn tài nguyên nguồn chưa nhận/kiểm định được; cần biên tập bổ sung.");
                warnings.AddRange(parsed.Warnings);
                if(!settings.Categories.TryGetValue(category,out var mapped)||!await db.Categories.AnyAsync(x=>x.Id==mapped,ct))warnings.Add("Chưa ánh xạ chuyên mục nguồn. Chọn chuyên mục đích trong Đồng bộ QLDD.");
                status=warnings.Count==0?"Ready":"Review";
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
        var run=await db.CrawlRuns.SingleAsync(x=>x.Id==job.Id,ct);run.Checkpoint=SourceSyncPolicy.Serialize(data);
        current.Status=status;current.Kind=data.Source?.Kind??current.Kind;current.Attempts++;current.UpdatedUtc=DateTimeOffset.UtcNow;
        current.Error=data.Warnings.Count==0?null:string.Join(" ",data.Warnings);
        var tracked=await db.Jobs.SingleAsync(x=>x.Id==job.Id,ct);tracked.Status=status;tracked.Attempts=status=="Error"?tracked.Attempts+1:0;
        tracked.AvailableUtc=DateTimeOffset.UtcNow.AddMinutes(status=="Error"?Math.Min(120,15*Math.Max(1,tracked.Attempts)):30);
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
        var mime=extension switch{".jpg" or ".jpeg"=>"image/jpeg",".png"=>"image/png",".gif"=>"image/gif",".webp"=>"image/webp",_=>OfficeDocumentPolicy.Mime(name)};
        var response=await source.FetchAsync(url,10*1024*1024,false,ct);
        SourceSyncResourcePolicy.ValidateResponse(response,mime);
        var key=await StoreBytesAsync(response.Bytes,"resource",ct);var path=storage.ResolveSnapshot(key);
        try
        {
            if(mime.StartsWith("image/"))media.ValidateContent(path,mime,ct);
            else{OfficeDocumentPolicy.Validate(path,mime,media,managedWithoutAv:true);if(mime=="application/pdf")ManagedPdfPolicy.Validate(path);}
        }
        catch(MediaUploadException e)
        {throw new SourceResourceException(mime.StartsWith("image/")?(e.StatusCode==413?"IMAGE_LIMIT":"IMAGE_INVALID"):"FILE_INVALID");}
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
