using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.MigrationCli;

public sealed record RecoveryResource(string Sha256,long Bytes,string Mime,string CaptureKey,List<string> Urls,List<string> Identities,bool IsProtectedSource=true);
public sealed class RecoveryInventory
{
    public Guid JobId { get; set; }=Guid.NewGuid();
    public Guid InventoryId { get; set; }
    public string CheckpointSha256 { get; set; }="";
    public string SourceProofSha256 { get; set; }="";
    public string Policy { get; set; }=MediaRecovery.Policy;
    public string? Scope { get; set; }
    public string? ParentCheckpointSha256 { get; set; }
    public string? SourceResourcePolicySha256 { get; set; }
    public List<RecoveryResource> Resources { get; set; }=[];
}
public sealed record RecoveryProof(Guid JobId,string Sha256,long Bytes,string Policy,string State,string? Engine,DateTimeOffset ScannedUtc,DateTimeOffset ExpiresUtc,InquiryScanResult Result,string QuarantineKey);
public sealed record RecoveryProofReference(string Sha256,string Path,string ProofSha256);
public sealed record RecoveryProofSet(Guid JobId,string InventorySha256,List<RecoveryProofReference> Proofs);

// Existing scanner and SQL append-only audit are reused. No Dev data or sealed3A state is updated here.
public sealed partial class MediaRecovery(PortalDbContext db,StorageOptions storage,IConfiguration config,string workspace)
{
    public const string Policy="Phase3C.PublicAnonymousExactBytes.DefenderTest.Reuse24h.Definitions72h.v1";
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    static string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
    public static async Task<string> WriteNewAsync<T>(string path,T value)
    {Directory.CreateDirectory(Path.GetDirectoryName(path)!);await using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){await JsonSerializer.SerializeAsync(f,value,Json);await f.FlushAsync();f.Flush(true);}return Hash(await File.ReadAllBytesAsync(path));}
    public static async Task<T> ReadBoundAsync<T>(string path,string sha)
    {SourceInventory.NoReparse(path);var bytes=await File.ReadAllBytesAsync(path);if(Hash(bytes)!=sha)throw new InvalidDataException("Reviewed evidence hash differs.");return JsonSerializer.Deserialize<T>(bytes)??throw new InvalidDataException("Invalid recovery evidence.");}
    async Task Guard()
    {
        if(config["DOTNET_ENVIRONMENT"]!="Development"||config["Sql:ExpectedDatabase"]!="LandPortal_Test"||!Path.GetFullPath(storage.MediaRoot).StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/tests"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Recovery is isolated SQL Test only. Dev scan/application prohibited.");
        await SchemaManager.VerifyLocalProjectAsync(db,"LandPortal_Test");
    }
    string JobRoot(Guid id)=>Path.GetFullPath(Path.Combine(workspace,".local/tests/phase3c",id.ToString("N")));
    DefenderInquiryScanner Scanner(IEnumerable<string> hashes,long maximum=32*1024*1024)
    {
        var c=new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{{"Scanner:Provider","DefenderTest"},{"Scanner:DefenderAnonymousTestOnly","true"},{"Scanner:DefenderAllowedTestHashes",string.Join(',',hashes)}}).Build();
        return new(c,"Development",maximum);
    }
    long ValidateResourcePolicy(RecoveryInventory inventory)
    {
        if(inventory.SourceResourcePolicySha256 is string policy&&SourceResourcePolicy.Read(config)?.Sha!=policy)throw new InvalidDataException("Recovery source policy changed.");
        foreach(var r in inventory.Resources.Where(x=>x.Bytes>SourceResourcePolicy.StandardPdfBytes))
            if(inventory.SourceResourcePolicySha256==null||r.Mime!="application/pdf"||r.Urls.Any(url=>r.Bytes>SourceResourcePolicy.MediaOptions(config,url).MaxPdfBytes))throw new InvalidDataException("Oversized resource outside exact approved PDF policy.");
        return inventory.Resources.Any(x=>x.Bytes>SourceResourcePolicy.StandardPdfBytes)?SourceResourcePolicy.ApprovedPdfBytes:SourceResourcePolicy.StandardPdfBytes;
    }
    public async Task RegisterAsync(RecoveryInventory inventory,string sha)
    {
        await Guard();ValidateShape(inventory);ValidateResourcePolicy(inventory);var existing=await db.AuditLogs.AsNoTracking().Where(x=>x.Action=="Phase3C.InventoryBinding"&&x.Actor==inventory.JobId.ToString()).Select(x=>x.Detail).ToListAsync();
        if(existing.Any(x=>x!=sha))throw new InvalidDataException("Job is bound to another inventory.");
        if(existing.Count==0){db.AuditLogs.Add(new(){Action="Phase3C.InventoryBinding",Actor=inventory.JobId.ToString(),Detail=sha});await db.SaveChangesAsync();}
    }
    async Task Bound(RecoveryInventory inventory,string sha)
    {
        if(inventory.Policy!=Policy||!await MigrationDiagnostics.StepAsync("recovery.inventory-binding",()=>db.AuditLogs.AnyAsync(x=>x.Action=="Phase3C.InventoryBinding"&&x.Actor==inventory.JobId.ToString()&&x.Detail==sha)))throw new InvalidDataException("Inventory policy/hash has no durable binding.");
    }
    async Task<List<string>> ReceiptSnapshotAsync(RecoveryInventory inventory)
    {
        var rows=await MigrationDiagnostics.StepAsync("recovery.receipt-snapshot",()=>db.AuditLogs.AsNoTracking().Where(x=>x.Action=="Phase3C.ScanProof"&&x.Actor==inventory.JobId.ToString()).Select(x=>x.Detail).Take(181).ToListAsync());
        if(rows.Count>180)throw new InvalidDataException("Recovery job exceeds bounded receipt review limit.");
        MigrationDiagnostics.Record("RECEIPTS_MATERIALIZED",new{rows=rows.Count,bytes=rows.Sum(x=>(long)System.Text.Encoding.UTF8.GetByteCount(x))});
        return rows;
    }
    // Read-only SQL readiness deliberately precedes any scanner refresh. This is
    // not proof validation and cannot create a preview, approval or Apply permit.
    public async Task<object> SqlReadinessAsync(RecoveryInventory inventory,string sha)
    {
        await Guard();ValidateShape(inventory);await Bound(inventory,sha);
        var rows=await ReceiptSnapshotAsync(inventory);
        return new{Status="SQL_READINESS_ONLY",inventory.JobId,ReceiptRows=rows.Count,ScannerChecked=false,EligibleForApply=false,DatabaseWrites=0};
    }
    public async Task<RecoveryInventory> InventoryAsync(InventoryState state,string checkpointSha,string sourceProofPath,string sourceProofSha)
    {
        await Guard();var bytes=await File.ReadAllBytesAsync(sourceProofPath);if(Hash(bytes)!=sourceProofSha)throw new InvalidDataException("Source preservation proof differs.");
        using var doc=JsonDocument.Parse(bytes);if(doc.RootElement.GetProperty("database").GetString()!="LandPortal_Dev")throw new InvalidDataException("Expected read-only Dev baseline proof.");
        var media=doc.RootElement.GetProperty("groups").GetProperty("media").EnumerateArray().ToArray();
        if(media.Length!=53||media.Any(x=>x.GetProperty("ScanStatus").GetString()!="NotScanned"))throw new InvalidDataException("Original53 baseline differs; stop for review.");
        var result=new RecoveryInventory{InventoryId=state.Id,CheckpointSha256=checkpointSha,SourceProofSha256=sourceProofSha};
        foreach(var m in media){var sha=m.GetProperty("Sha256").GetString()!;var nodes=state.Urls.Where(x=>x.Role=="File"&&x.CaptureSha==sha&&x.FormatStatus=="Validated").ToList();if(nodes.Count==0)throw new InvalidDataException("Protected media missing from inventory.");var cap=state.Captures.Last(x=>x.Sha256==sha&&x.Status==200&&nodes.Any(n=>n.Url==x.Url));var ids=state.Items.Where(x=>x.Source.AssetUrls.Intersect(nodes.Select(n=>n.Url)).Any()).Select(x=>x.Source.Kind+":"+x.Source.SourceId).Distinct().Order().ToList();
            var clone=await db.MediaAssets.AsNoTracking().SingleAsync(x=>x.Sha256==sha);var file=MediaService.ResolveMediaPath(storage,clone.StorageKey);if(Hash(await File.ReadAllBytesAsync(file))!=sha||new FileInfo(file).Length!=cap.Bytes)throw new InvalidDataException("Test copy does not match protected bytes.");
            result.Resources.Add(new(sha,cap.Bytes,nodes[0].Mime!,cap.Key,nodes.Select(x=>x.Url).Distinct().Order().ToList(),ids));}
        if(result.Resources.SelectMany(x=>x.Identities).Distinct().Count()!=29)throw new InvalidDataException("Expected29 scanner-related identities.");
        // The7 newer public images have old definitions/time; refresh them separately for the proposed Dev batch.
        foreach(var node in state.Urls.Where(x=>x.Role=="File"&&x.FormatStatus=="Validated"&&x.ScanStatus=="Clean"&&!result.Resources.Any(r=>r.Sha256==x.CaptureSha)).ToList()){
            var cap=state.Captures.Last(x=>x.Url==node.Url&&x.Sha256==node.CaptureSha&&x.Status==200&&x.Origin=="Network");var ids=state.Items.Where(x=>x.Source.AssetUrls.Contains(node.Url)).Select(x=>x.Source.Kind+":"+x.Source.SourceId).ToList();if(ids.Count==0)continue;
            result.Resources.Add(new(cap.Sha256,cap.Bytes,node.Mime!,cap.Key,[node.Url],ids,false));
        }
        result.Resources=result.Resources.OrderBy(x=>x.Sha256).ToList();return result;
    }
    async Task<RecoveryProof> Proof(RecoveryInventory inventory,RecoveryProofReference reference,IReadOnlySet<string>? receipts=null)
    {
        var path=Path.GetFullPath(reference.Path,workspace);if(!path.StartsWith(JobRoot(inventory.JobId)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Proof outside exact Test job.");
        var p=await ReadBoundAsync<RecoveryProof>(path,reference.ProofSha256);var resource=inventory.Resources.Single(x=>x.Sha256==reference.Sha256);
        if(p.JobId!=inventory.JobId||p.Sha256!=resource.Sha256||p.Bytes!=resource.Bytes||p.Policy!=Policy||p.Result.State.ToString()!=p.State||p.ScannedUtc>DateTimeOffset.UtcNow.AddMinutes(1)||p.ExpiresUtc!=p.ScannedUtc.AddHours(24))throw new InvalidDataException("Proof binding differs.");
        var detail=JsonSerializer.Serialize(reference);if(receipts is null?!await db.AuditLogs.AnyAsync(x=>x.Action=="Phase3C.ScanProof"&&x.Actor==inventory.JobId.ToString()&&x.Detail==detail):!receipts.Contains(detail))throw new InvalidDataException("Proof has no durable SQL receipt.");
        return p;
    }
    public async Task<RecoveryProofSet> RunAsync(RecoveryInventory inventory,string inventorySha,int stopAfter=0,CancellationToken ct=default,bool reuseOnly=false)
    {
        await Guard();ValidateShape(inventory);
        await Bound(inventory,inventorySha);var set=new RecoveryProofSet(inventory.JobId,inventorySha,[]);if(inventory.Resources.Count==0)return set;
        var scanner=Scanner(inventory.Resources.Select(x=>x.Sha256),ValidateResourcePolicy(inventory));var identity=await scanner.CurrentIdentityAsync(ct);var done=0;
        await db.Database.OpenConnectionAsync(ct);
        try{
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @n int; EXEC @n=sys.sp_getapplock @Resource={"LandPortal.Phase3C.Scan."+inventory.JobId},@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; IF @n<0 THROW 51001,'Recovery worker already running',1;",ct);
            var receipts=await db.AuditLogs.AsNoTracking().Where(x=>x.Actor==inventory.JobId.ToString()&&x.Action=="Phase3C.ScanProof").OrderByDescending(x=>x.CreatedUtc).ThenByDescending(x=>x.Id).Select(x=>x.Detail).Take(181).ToListAsync(ct);
            if(receipts.Count>180)throw new InvalidDataException("Recovery job exceeds bounded receipt review limit.");
            // Fresh operation-local SQL snapshot under the existing exclusive job
            // lease, not a persistent trust cache. Avoid one extra EXISTS per proof.
            var receiptSnapshot=receipts.ToHashSet(StringComparer.Ordinal);
            MigrationDiagnostics.Record("REUSE_RECEIPTS_MATERIALIZED",new{rows=receipts.Count,reuseOnly});
            foreach(var resource in inventory.Resources){
                ct.ThrowIfCancellationRequested();var refs=receipts.Select(x=>JsonSerializer.Deserialize<RecoveryProofReference>(x)!).Where(x=>x.Sha256==resource.Sha256).ToList();RecoveryProofReference? reused=null;
                foreach(var r in refs){var proof=await Proof(inventory,r,receiptSnapshot);if(proof.ExpiresUtc>DateTimeOffset.UtcNow&&proof.Engine==identity&&identity!=null&&proof.State is "Clean" or "Infected"){reused=r;break;}}
                if(reused is not null){set.Proofs.Add(reused);Console.WriteLine("Reuse "+resource.Sha256);continue;}
                if(reuseOnly)throw new InvalidDataException("Reuse-only refused: stale, missing or non-reusable proof; no scanner invoked.");
                if(refs.Count>=3){set.Proofs.Add(refs[0]);Console.WriteLine("Blocked retry limit "+resource.Sha256);continue;}
                var source=StorageOptions.ResolveWithin(Path.Combine(workspace,".local/phase3a",inventory.InventoryId.ToString("N")),resource.CaptureKey);SourceInventory.NoReparse(source);
                var target=StorageOptions.ResolveWithin(JobRoot(inventory.JobId),"quarantine/"+resource.Sha256+".bin");Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var original=await File.ReadAllBytesAsync(source,ct);if(original.LongLength!=resource.Bytes||Hash(original)!=resource.Sha256)throw new InvalidDataException("Inventory source bytes changed.");
                if(!File.Exists(target)){await using var dest=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None);await dest.WriteAsync(original,ct);await dest.FlushAsync(ct);dest.Flush(true);}
                await using var input=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read);var hash=Convert.ToHexStringLower(await SHA256.HashDataAsync(input,ct));if(hash!=resource.Sha256||input.Length!=resource.Bytes)throw new InvalidDataException("Quarantine bytes changed.");input.Position=0;
                var result=identity is null?new InquiryScanResult(InquiryScanState.ScanFailed,"DefenderNotReady",null):await scanner.ScanAsync(input,ct);input.Position=0;if(Convert.ToHexStringLower(await SHA256.HashDataAsync(input,ct))!=hash)result=new(InquiryScanState.ScanFailed,"InputChanged",null);
                var now=DateTimeOffset.UtcNow;var proofNew=new RecoveryProof(inventory.JobId,hash,resource.Bytes,Policy,result.State.ToString(),result.EngineVersion,now,now.AddHours(24),result,Path.GetRelativePath(JobRoot(inventory.JobId),target));
                var proofPath=Path.Combine(JobRoot(inventory.JobId),"proofs",hash+"-"+Guid.NewGuid().ToString("N")+".json");var proofSha=await WriteNewAsync(proofPath,proofNew);var reference=new RecoveryProofReference(hash,Path.GetRelativePath(workspace,proofPath),proofSha);
                db.AuditLogs.Add(new(){Actor=inventory.JobId.ToString(),Action="Phase3C.ScanProof",Detail=JsonSerializer.Serialize(reference)});await db.SaveChangesAsync(ct);set.Proofs.Add(reference);
                Console.WriteLine($"Scan {++done}/{inventory.Resources.Count} {hash} {result.State} {result.Error}");if(stopAfter>0&&done>=stopAfter)break;
            }
        }finally{await db.Database.CloseConnectionAsync();}
        return set;
    }
    public async Task<List<RecoveryProof>> ValidateSetAsync(RecoveryInventory inventory,string inventorySha,RecoveryProofSet set)
    {
        await Guard();ValidateShape(inventory);await Bound(inventory,inventorySha);if(set.InventorySha256!=inventorySha||set.JobId!=inventory.JobId||set.Proofs.Select(x=>x.Sha256).Distinct().Count()!=set.Proofs.Count||inventory.Scope==PublicBatchScope&&set.Proofs.Count!=inventory.Resources.Count)throw new InvalidDataException("Proof set differs or is incomplete.");
        var maximum=ValidateResourcePolicy(inventory);var identity=inventory.Resources.Count==0?null:await Scanner(inventory.Resources.Select(x=>x.Sha256),maximum).CurrentIdentityAsync();var list=new List<RecoveryProof>();
        // One bounded append-only receipt snapshot for this job; never a persistent trust cache.
        var receiptRows=await ReceiptSnapshotAsync(inventory);
        var receipts=receiptRows.ToHashSet(StringComparer.Ordinal);
        foreach(var reference in set.Proofs){var p=await Proof(inventory,reference,receipts);if(p.ExpiresUtc<=DateTimeOffset.UtcNow||p.State is "Clean" or "Infected"&&(p.Engine!=identity||identity is null||p.Result.EngineVersion!=p.Engine))throw new InvalidDataException("Stale/different engine proof; scan again.");list.Add(p);}RequirePublicClean(inventory,list);return list;
    }
    public async Task<int> StageTestAsync(RecoveryInventory inventory,string inventorySha,RecoveryProofSet set)
    {
        var proofs=await ValidateSetAsync(inventory,inventorySha,set);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);var changed=0;
        foreach(var p in proofs){var m=await db.MediaAssets.SingleAsync(x=>x.Sha256==p.Sha256);var file=MediaService.ResolveMediaPath(storage,m.StorageKey);if(new FileInfo(file).Length!=p.Bytes||Hash(await File.ReadAllBytesAsync(file))!=p.Sha256)throw new InvalidDataException("Test media changed before stage.");
            if(p.State=="Clean"&&(p.Result.Provider!="MicrosoftDefender"||p.Result.OutputSha256 is null))throw new InvalidDataException("Unverified Clean refused.");
            if(m.ScanStatus==p.State&&m.ValidationStatus==SourceBatch.AssetValidation&&m.IsQuarantined==(p.State!="Clean"))continue;
            var before=new{m.Id,m.Sha256,m.ScanStatus,m.ValidationStatus,m.IsQuarantined};m.ScanStatus=p.State;m.ValidationStatus=SourceBatch.AssetValidation;m.IsQuarantined=p.State!="Clean";
            db.AuditLogs.Add(new(){Actor=inventory.JobId.ToString(),Action="Phase3C.StageTest",Detail=JsonSerializer.Serialize(new{Before=before,Proof=p,Scope="SQL Test copies only; no Dev apply"})});changed++;
        }await db.SaveChangesAsync();await tx.CommitAsync();db.ChangeTracker.Clear();return changed;
    }
    public async Task OverlayAsync(InventoryState state,RecoveryInventory inventory,string inventorySha,RecoveryProofSet set,bool includeProtected=true)
    {
        if(state.Id!=inventory.InventoryId)throw new InvalidDataException("Overlay inventory differs.");var proofs=await ValidateSetAsync(inventory,inventorySha,set);
        foreach(var p in proofs.Where(x=>x.State=="Clean"&&(includeProtected||!inventory.Resources.Single(r=>r.Sha256==x.Sha256).IsProtectedSource)))foreach(var node in state.Urls.Where(x=>x.Role=="File"&&x.CaptureSha==p.Sha256)){node.ScanStatus="Clean";node.ScanEngine=p.Engine;node.ScannedUtc=p.ScannedUtc;node.ScannedSha256=p.Sha256;}
    }
}
