using System.Data;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.MigrationCli;

public sealed partial class SourceInventory(PortalDbContext db, IConfiguration config, InventoryState state,
    Func<string, int, CancellationToken, bool, string?, string?, Task<SourceResponse>>? fixtureTransport = null)
{
    public InventoryState State => state;
    private string root = "";
    private InventoryLimits limits = new();
    private int requestsAtStart, pages, details, assets;
    private long bytesAtStart;
    private SafeSourceClient? network;
    private CancellationToken operationToken;
    private InventoryApproval? approval;
    private DetailBatch? detailBatch;
    private string? checkpoint;
    public const string CheckpointPrefix = "Phase3A/3:";
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string HashText(string value) => Hash(Encoding.UTF8.GetBytes(value));
    public static void RequireTest(IConfiguration c)
    {
        if (c["Sql:ExpectedDatabase"] != "LandPortal_Test" || c["DOTNET_ENVIRONMENT"] != "Development") throw new InvalidOperationException("Phase3A currently requires Development + named local SQL Test. No Dev business writes or identity grants.");
    }
    private string PathFor(string key) => StorageOptions.ResolveWithin(root, key);
    internal static void NoReparse(string path)
    {
        for (var p = new DirectoryInfo(Path.GetDirectoryName(path)!); p is not null; p = p.Parent)
            if (p.Exists && p.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Reparse storage is not accepted.");
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Reparse file is not accepted.");
    }
    private async Task<string> Blob(byte[] data, string folder)
    {
        var hash = Hash(data); var key = folder + "/" + hash + ".bin"; var path = PathFor(key); NoReparse(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if(new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace<512L*1024*1024)throw new IOException("Inventory storage below 512MiB free; last committed SQL checkpoint retained.");
        if (File.Exists(path)) { if (Hash(await File.ReadAllBytesAsync(path)) != hash) throw new InvalidDataException("Immutable inventory blob was modified."); }
        else { await using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); await f.WriteAsync(data); await f.FlushAsync(); f.Flush(true); }
        return key;
    }
    public async Task InitializeAsync(string workspace)
    {
        RequireTest(config); await SchemaManager.VerifyLocalProjectAsync(db, "LandPortal_Test");
        var expected = Path.Combine(workspace, ".local", "phase3a", state.Id.ToString("N"));
        if (state.Root.Length != 0 && !string.Equals(Path.GetFullPath(state.Root), expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Inventory root must match the project-local session id.");
        root = expected; state.Root = expected; NoReparse(Path.Combine(root,"guard")); Directory.CreateDirectory(root);
        var current=await db.CrawlRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==state.Id);
        if(current is not null)
        {
            var expectedPointer=CheckpointPrefix+(state.LoadedCheckpointHash??Hash(JsonSerializer.SerializeToUtf8Bytes(state,InventoryLimits.Json)));
            if(current.Checkpoint!=expectedPointer)throw new InvalidOperationException("Stale or foreign inventory checkpoint; reload before resuming.");
            checkpoint=current.Checkpoint;
        }
        foreach(var capture in state.Captures)Add(capture.Url,SourceCatalog.BaseUrl+"/");
        foreach(var issue in state.LegacyIssues)Add(issue.SourceUrl,SourceCatalog.BaseUrl+"/");
        foreach(var n in state.Urls.Where(x=>x.Role=="List"))
        {
            var classified=InventoryDiscovery.Classify(n.Url,SourceCatalog.BaseUrl+"/");
            if(classified is null)continue;n.Page=classified.Page;n.CategoryIds=classified.CategoryIds;
            n.ListRoot=state.Urls.FirstOrDefault(x=>x.Role=="List"&&InventoryDiscovery.Classify(x.Url,SourceCatalog.BaseUrl+"/") is {Page:1} first && (classified.CategoryIds.Count>0?first.CategoryIds.Intersect(classified.CategoryIds).Any():new Uri(first.Url).AbsolutePath==new Uri(n.Url).AbsolutePath))?.Url??n.ListRoot;
        }
    }
    public static async Task<InventoryState> LoadAsync(PortalDbContext db, string workspace, Guid id)
    {
        var row = await db.CrawlRuns.AsNoTracking().SingleAsync(x => x.Id == id);
        if (!row.Checkpoint.StartsWith(CheckpointPrefix)) throw new InvalidDataException("Not a Phase3A checkpoint; legacy runs cannot be adopted.");
        var hash = row.Checkpoint[CheckpointPrefix.Length..];
        if (!Regex.IsMatch(hash, "^[a-f0-9]{64}$")) throw new InvalidDataException("Checkpoint digest invalid.");
        var path = Path.Combine(workspace, ".local", "phase3a", id.ToString("N"), "checkpoints", hash + ".bin");NoReparse(path);
        if(new FileInfo(path).Length>128L*1024*1024)throw new InvalidDataException("Checkpoint exceeds safe size.");
        var data = await File.ReadAllBytesAsync(path);if(Hash(data)!=hash)throw new InvalidDataException("Checkpoint hash mismatch.");
        if(data.AsSpan().StartsWith("LP3A-BR1"u8))
        {
            using var compressed=new MemoryStream(data,8,data.Length-8);using var br=new BrotliStream(compressed,CompressionMode.Decompress);using var raw=new MemoryStream();var buffer=new byte[65536];int n;
            while((n=await br.ReadAsync(buffer))>0){if(raw.Length+n>128L*1024*1024)throw new InvalidDataException("Expanded checkpoint exceeds safe bound.");await raw.WriteAsync(buffer.AsMemory(0,n));}data=raw.ToArray();
        }
        var loaded=JsonSerializer.Deserialize<InventoryState>(data,InventoryLimits.Json)!;
        if(loaded.SchemaVersion!=3||loaded.Id!=id)throw new InvalidDataException("Checkpoint identity mismatch.");loaded.LoadedCheckpointHash=hash;return loaded;
    }
    public InventoryUrl? Add(string url, string parent, int? category = null, string? listRoot = null)
    {
        var candidate=InventoryDiscovery.Classify(url,parent,category);if(candidate is null)return null;
        var prior=state.Urls.FirstOrDefault(x=>x.Url==candidate.Url);
        if(prior is null){if(state.Urls.Count>=20000){Issue(url,"URL_QUEUE_LIMIT","Finite inventory frontier full; not source completion.");return null;}prior=candidate;state.Urls.Add(prior);}
        if(!prior.FoundOn.Contains(parent)&&prior.FoundOn.Count<100)prior.FoundOn.Add(parent);
        foreach(var cat in candidate.CategoryIds)if(!prior.CategoryIds.Contains(cat))prior.CategoryIds.Add(cat);
        foreach(var item in state.Items.Where(x=>x.Aliases.Contains(prior.Url)))foreach(var cat in candidate.CategoryIds)if(!item.Source.CategoryIds.Contains(cat))item.Source.CategoryIds.Add(cat);
        if(candidate.Kind==ContentKind.StaticPage||candidate.Kind==ContentKind.Announcement)prior.Kind=candidate.Kind;
        prior.ListRoot??=listRoot??(prior.Role=="List"&&prior.Page==1?prior.Url:null);
        return prior;
    }
    public void Seed()
    {
        foreach(var path in new[]{"/robots.txt","/","/sitemap.xml","/rss.xml","/danh-sach-van-ban.htm","/danh-sach-van-ban-du-thao.htm","/video.htm","/lanh-dao-cuc-1780.htm","/chuc-nang-nhiem-vu-3049.htm","/lien-he.htm","/van-ban-chi-dao-dieu-hanh.htm"})Add(SourceCatalog.BaseUrl+path,SourceCatalog.BaseUrl+"/");
        foreach(var c in SourceCatalog.Categories)Add(SourceCatalog.BaseUrl+c.Path,SourceCatalog.BaseUrl+"/");
    }
    public async Task SeedLegacyAsync(SampleManifest legacy, StorageOptions original)
    {
        if(state.Captures.Count!=0)throw new InvalidOperationException("Legacy cache can only be seeded once into an empty session.");
        state.LegacyIssues=legacy.Issues.ToList();
        foreach(var issue in legacy.Issues)Add(issue.SourceUrl,SourceCatalog.BaseUrl+"/");
        foreach(var s in legacy.Snapshots)
        {
            if(InventoryDiscovery.Classify(s.Url,SourceCatalog.BaseUrl+"/") is null)continue;
            Add(s.Url,SourceCatalog.BaseUrl+"/");
            var path=original.ResolveSnapshot(s.StorageKey);NoReparse(path);var bytes=await File.ReadAllBytesAsync(path);
            if(bytes.LongLength!=s.Bytes||Hash(bytes)!=s.Sha256)throw new InvalidDataException("Legacy snapshot hash mismatch.");
            var key=await Blob(bytes,"responses");state.Captures.Add(new(s.Url,s.FinalUrl,s.Status,s.ContentType,key,s.Sha256,s.Bytes,s.FetchedUtc,"LegacySnapshot"));
        }
        foreach(var a in legacy.Assets)
        {
            var node=Add(a.SourceUrl,SourceCatalog.BaseUrl+"/");if(node is null||node.Status=="OutOfScope")continue;
            var path=original.ResolveMedia(a.StorageKey);NoReparse(path);var bytes=await File.ReadAllBytesAsync(path);
            if(bytes.LongLength!=a.Bytes||Hash(bytes)!=a.Sha256)throw new InvalidDataException("Legacy media hash mismatch.");
            var key=await Blob(bytes,"quarantine");state.Captures.Add(new(a.SourceUrl,a.SourceUrl,200,a.MimeType,key,a.Sha256,a.Bytes,File.GetLastWriteTimeUtc(path),"LegacyPrivateCopy"));
        }
        foreach(var l in legacy.Lists)Add(l.Url,SourceCatalog.BaseUrl+"/",l.CategoryId);
        foreach(var c in legacy.Contents){var node=Add(c.Source.SourceUrl,SourceCatalog.BaseUrl+"/");if(node is not null)node.Kind=c.Source.Kind;}
        await SaveAsync();
    }
    private void Issue(string url,string code,string detail)
    {
        if(!state.Issues.Any(x=>x.Url==url&&x.Code==code&&x.Detail==detail))state.Issues.Add(new(url,code,detail,DateTimeOffset.UtcNow));
    }
    // SQL points at immutable fsynced checkpoint bytes. An interrupted write leaves an orphan,
    // never a half-replaced authoritative checkpoint. The existing importer persists typed crawl rows.
    public async Task SaveAsync()
    {
        state.UpdatedUtc=DateTimeOffset.UtcNow;
        var payload=JsonSerializer.SerializeToUtf8Bytes(state,InventoryLimits.Json);
        if(payload.LongLength>128L*1024*1024)throw new InventoryPolicyException("Checkpoint exceeds finite batch size; review batch partitioning before continuation.");
        using var packed=new MemoryStream();packed.Write("LP3A-BR1"u8);using(var br=new BrotliStream(packed,CompressionLevel.Fastest,true))await br.WriteAsync(payload);
        var key=await Blob(packed.ToArray(),"checkpoints");var hash=Path.GetFileNameWithoutExtension(key);
        var projected=new SampleManifest{CrawlRunId=state.Id,StartedUtc=state.StartedUtc,Status=state.Status,RequestsUsed=state.RequestsReserved,RequestBudget=approval?.MaximumRequests??40,ContentBudget=limits.Contents};
        projected.Discovered=state.Urls.Select(x=>new DiscoveredLink(x.Url,x.CandidateId,x.Kind,x.CategoryIds.Cast<int?>().FirstOrDefault())).ToList();
        projected.ItemStages=state.Urls.ToDictionary(x=>x.Url,x=>new PipelineCheckpoint(x.Status,x.Attempts,state.UpdatedUtc,x.Error));
        projected.Snapshots=state.Captures.Select(x=>new SnapshotEntry(x.Url,x.FinalUrl,x.Status,x.ContentType,Path.GetRelativePath(Path.Combine(root,".."),PathFor(x.Key)),x.Sha256,x.Bytes,x.FetchedUtc)).ToList();
        projected.Issues=state.Issues.Select(x=>new IssueEntry(x.Url,"Phase3A",x.Code,x.Detail)).ToList();
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var current=await db.CrawlRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==state.Id);
        if(current?.Checkpoint!=checkpoint)throw new InvalidOperationException("Concurrent inventory checkpoint changed; no overwrite.");
        await new SqlImporter(db,new(){MediaRoot=root,SnapshotRoot=Path.GetDirectoryName(root)!,TempRoot=root,KeysRoot=root}).PersistCrawlAsync(projected,CancellationToken.None);
        foreach(var item in db.CrawlItems.Local.Where(x=>x.CrawlRunId==state.Id)){var u=state.Urls.Single(x=>x.Url==item.SourceUrl);item.SourceId=u.CandidateId;item.Kind=u.Kind;}
        foreach(var snapshot in db.SourceSnapshots.Local.Where(x=>x.CrawlRunId==state.Id))snapshot.SchemaVersion=3;
        var run=await db.CrawlRuns.FindAsync(state.Id);run!.Checkpoint=CheckpointPrefix+hash;
        await db.SaveChangesAsync();await tx.CommitAsync();checkpoint=CheckpointPrefix+hash;state.LoadedCheckpointHash=hash;db.ChangeTracker.Clear();
    }
    private async Task Reserve(string url)
    {
        operationToken.ThrowIfCancellationRequested();
        if(approval is not null&&approval.ExpiresUtc<=DateTimeOffset.UtcNow)throw new InventoryPolicyException("Source-owner approval expired during the run.");
        try{await SynchronizationGuard.EnsureEnabledAsync(db,config);}catch(InvalidOperationException){throw new InventoryPolicyException("Synchronization disabled; no further source GET is allowed.");}finally{db.ChangeTracker.Clear();}
        if(state.RequestsReserved>= (approval?.MaximumRequests??40)||state.RequestsReserved-requestsAtStart>=limits.Requests||state.ReceivedBytes-bytesAtStart>=limits.Bytes)throw new RequestBudgetException();
        if(state.RobotsState=="Valid"&&!InventoryDiscovery.RobotsAllows(state.RobotsText,url))throw new InvalidDataException("Source robots forbids this URL (including redirect target).");
        // The HTTP client enforces the gap within a process. Preserve it across CLI restarts too.
        if(fixtureTransport is null)
        {
            var last=state.Captures.Where(x=>x.Origin=="Network").Select(x=>(DateTimeOffset?)x.FetchedUtc).Max()??state.Requests.LastOrDefault()?.ReservedUtc;
            if(last.HasValue){var pause=TimeSpan.FromSeconds(2.1)-(DateTimeOffset.UtcNow-last.Value);if(pause>TimeSpan.Zero)await Task.Delay(pause,operationToken);}
        }
        state.RequestsReserved++;state.Requests.Add(new(url,DateTimeOffset.UtcNow));await SaveAsync();
    }
    public async Task RunAsync(InventoryLimits requested,CancellationToken cancellation=default)
    {
        requested.Validate();limits=requested;
        if(root.Length==0)throw new InvalidOperationException("Initialize the guarded session first.");
        if(requested.ApprovalFile is not null)
        {
            var path=Path.GetFullPath(requested.ApprovalFile,config["WorkspaceRoot"]!);
            if(!path.StartsWith(Path.Combine(config["WorkspaceRoot"]!,".local","phase3a","approvals")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Approval must be a local reviewed record in .local/phase3a/approvals.");
            NoReparse(path);var bytes=await File.ReadAllBytesAsync(path);approval=JsonSerializer.Deserialize<InventoryApproval>(bytes,InventoryLimits.Json)!;
            if(approval.Source!=SourceCatalog.BaseUrl||string.IsNullOrWhiteSpace(approval.ApprovedBy)||string.IsNullOrWhiteSpace(approval.Reference)||approval.ExpiresUtc<=DateTimeOffset.UtcNow||approval.MaximumRequests is <1 or >100000||approval.MaximumPages is <1 or >10000||approval.MaximumContents is <1 or >20000||approval.MaximumBytes is <1024 or >100L*1024*1024*1024)throw new InvalidDataException("Invalid/expired bounded source-owner approval.");
            state.ApprovalReference=approval.Reference+";sha256="+Hash(bytes);
        }
        if(requested.DetailBatchFile is not null)
            detailBatch=await DetailBatchPlanner.ReadAsync(config["WorkspaceRoot"]!,requested.DetailBatchFile,requested.DetailBatchSha256!,state);
        await SynchronizationGuard.EnsureEnabledAsync(db,config);db.ChangeTracker.Clear();
        await db.Database.OpenConnectionAsync();
        await using var claim=db.Database.GetDbConnection().CreateCommand();claim.CommandText="DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@name,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0;SELECT @r";
        var name=claim.CreateParameter();name.ParameterName="@name";name.Value="Phase3A:"+state.Id;claim.Parameters.Add(name);
        if(Convert.ToInt32(await claim.ExecuteScalarAsync(cancellation))<0)throw new InvalidOperationException("This inventory session already has an active collector.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromSeconds(limits.Seconds));operationToken=deadline.Token;
        requestsAtStart=state.RequestsReserved;bytesAtStart=state.ReceivedBytes;pages=details=assets=0;using var client=new SafeSourceClient(limits.Requests,Reserve);network=client;
        state.Status="Running";
        foreach(var interrupted in state.Urls.Where(x=>x.Status=="Fetching")){
            var captured=state.Captures.LastOrDefault(x=>x.Url==interrupted.Url&&x.Status==200&&x.Sha256==interrupted.CaptureSha);
            interrupted.Status=captured is null?"Retry":"Discovered";interrupted.AvailableUtc=DateTimeOffset.UtcNow;
            Issue(interrupted.Url,"INTERRUPTED_READ",captured is null?"Recovered a committed pre-request checkpoint; prior GET completion unknown.":"Bound successful response already captured; replay exact private bytes without another GET.");
        }
        try
        {
            Seed();await SaveAsync();
            var processed=new HashSet<string>();
            while(true)
            {
                operationToken.ThrowIfCancellationRequested();
                if(!limits.Offline&&state.RobotsState is "Blocked" or "Unavailable"){state.Status="PausedByRobots";return;}
                var node=state.Urls.Where(x=>!processed.Contains(x.Url)&&x.Status is "Discovered" or "Retry"&&x.AvailableUtc<=DateTimeOffset.UtcNow&&x.Attempts<3)
                    .OrderBy(x=>detailBatch is not null ? x.Role=="Robots"?0:x.Role=="File"?1:2 : x.Role=="Robots"?0:x.Refresh?1:x.Role=="Home"?2:x.Role=="List"?3:x.Role=="Feed"?4:x.Role=="Detail"?5:x.Role=="File"?6:7)
                    .FirstOrDefault(x=>Eligible(x));
                if(node is null)break;
                processed.Add(node.Url);await ProcessAsync(node);
            }
            var outstanding=state.Urls.Any(x=>x.Status is "Discovered" or "Retry" or "DeferredOffline" or "Fetching");
            state.Status=!outstanding?"FrontierExhaustedCoverageUnknown":limits.Offline?"PausedOfflineMissingSnapshots":pages>=limits.Pages||details>=limits.Contents||assets>=limits.Assets?"PausedByBudget":"PausedWithFrontier";
        }
        catch(RequestBudgetException){state.Status="PausedByBudget";}
        catch(OperationCanceledException){state.Status=cancellation.IsCancellationRequested?"PausedByOperator":"PausedByDuration";}
        catch(InventoryPolicyException ex){state.Status="PausedByPolicy";Issue(SourceCatalog.BaseUrl,"POLICY_STOP",ex.Message);}
        finally
        {
            await SaveAsync();
            await using var release=db.Database.GetDbConnection().CreateCommand();release.CommandText="EXEC sys.sp_releaseapplock @Resource=@name,@LockOwner='Session'";var p=release.CreateParameter();p.ParameterName="@name";p.Value="Phase3A:"+state.Id;release.Parameters.Add(p);await release.ExecuteNonQueryAsync();await db.Database.CloseConnectionAsync();network=null;
        }
    }
    private bool Eligible(InventoryUrl n)
    {
        if(detailBatch is not null && !DetailBatchPlanner.Includes(detailBatch,n))return false;
        var cached=state.Captures.LastOrDefault(x=>x.Url==n.Url);
        if(limits.Offline&&(cached is null||n.Refresh))return false;
        return n.Role=="File"?assets<limits.Assets:n.Role=="Detail"?details<limits.Contents:pages<limits.Pages;
    }
}
