using System.Text;
using System.Text.Json;
using LandPortal.Infrastructure;

namespace LandPortal.MigrationCli;

public static class Phase3FCommand
{
    public static async Task<int> ExecuteAsync(string[] args, string workspace)
    {
        string Opt(string key) { var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:throw new InvalidDataException(key+" required"); }
        var folder=Path.GetFullPath(Opt("--report"),workspace);
        if(!folder.StartsWith(Path.Combine(workspace,".local","evidence","phase3f")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(folder))throw new InvalidDataException("New private Phase3F evidence directory required.");
        SourceInventory.NoReparse(Path.Combine(folder,"guard"));Directory.CreateDirectory(folder);
        if(args[0]=="source-detail-plan")
        {
            var state=await SourceBatch.ReadCheckpointAsync(workspace,Opt("--checkpoint"),Opt("--checkpoint-sha"));
            var coverage=await CoverageAudit.InspectAsync(workspace,state);
            await MediaRecovery.WriteNewAsync(Path.Combine(folder,"coverage.json"),coverage);
            var batches=DetailBatchPlanner.Create(state,coverage,Opt("--checkpoint"),Opt("--checkpoint-sha"));
            var index=new List<object>();var commands=new List<string>();
            foreach(var batch in batches)
            {
                DetailBatchPlanner.Validate(batch,state,state);
                var name=$"batch-{batch.Number:000}.json";var path=Path.Combine(folder,name);
                await MediaRecovery.WriteNewAsync(path,batch);var sha=SourceInventory.Hash(await File.ReadAllBytesAsync(path));
                var relative=Path.GetRelativePath(workspace,path);
                var command=$"rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3f.ps1 -Action Collect -Batch '{relative}' -BatchSha {sha} -ApprovalFile .local/phase3a/approvals/REVIEWED_RECORD_REQUIRED.json";
                index.Add(new{batch.Number,batch.SourceIdentities,batch.Urls,path=relative,sha256=sha,command});commands.Add(command);
            }
            await MediaRecovery.WriteNewAsync(Path.Combine(folder,"batches.json"),new{state.Id,state.RequestsReserved,UnapprovedRemaining=Math.Max(0,40-state.RequestsReserved),Candidates=coverage.CandidateIdentities,Missing=coverage.Candidates.Count(x=>x.Reason=="NO_DETAIL_SNAPSHOT_BUDGET_OR_NOT_FETCHED"),Batches=index,Approval="NOT_PROVIDED; these work orders authorize no network",UnknownCategoryCounts=coverage.Categories.Where(x=>x.Candidates==0).Select(x=>x.Id)});
            await File.WriteAllLinesAsync(Path.Combine(folder,"commands-NOT-APPROVED.txt"),commands,new UTF8Encoding(true));
            Console.WriteLine($"Offline: {coverage.VerifiedItems} staged, {batches.Sum(x=>x.Urls.Length)} detail candidates in {batches.Length} work orders; no SQL/network writes.");
        }
        else if(args[0]=="nukeviet-bundle")
        {
            var manifest=await NukeVietBundle.ReadManifestAsync(workspace,Opt("--bundle"),Opt("--bundle-sha"));
            var state=await SourceBatch.ReadCheckpointAsync(workspace,Opt("--checkpoint"),Opt("--checkpoint-sha"));
            var report=await NukeVietBundle.PrepareAsync(workspace,manifest,folder,state);
            await MediaRecovery.WriteNewAsync(Path.Combine(folder,"checkpoint.json"),report);
            Console.WriteLine("Private NukeViet bundle checkpoint prepared; mapping, scanner and real source acceptance remain gated. No SQL or network.");
        }
        else throw new InvalidDataException("Unsupported Phase3F offline operation.");
        return 0;
    }
}

public sealed record NukeVietLocalFile(string Url,string Path,string Sha256);
public sealed record NukeVietSnapshot(string Url,string Path,string Sha256,NukeVietLocalFile[] Files);
public sealed record NukeVietInput(string Provenance,string Reference,NukeVietSnapshot[] Snapshots);

// A bounded offline handoff. Never trust a supplied Clean flag, mapping decision or SQL source id.
public static class NukeVietBundle
{
    static async Task<byte[]> ReadPrivateAsync(string workspace,string file,string sha,long max)
    {
        var path=System.IO.Path.GetFullPath(file,workspace);
        if(!path.StartsWith(System.IO.Path.Combine(workspace,".local")+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Bundle inputs must remain project private.");
        SourceInventory.NoReparse(path);if(new FileInfo(path).Length>max)throw new InvalidDataException("Bundle file exceeds bound.");
        var bytes=await File.ReadAllBytesAsync(path);if(SourceInventory.Hash(bytes)!=sha)throw new InvalidDataException("Bundle file SHA differs.");return bytes;
    }
    public static async Task<NukeVietInput> ReadManifestAsync(string workspace,string path,string sha)
        =>JsonSerializer.Deserialize<NukeVietInput>(await ReadPrivateAsync(workspace,path,sha,1024*1024))??throw new InvalidDataException("Invalid bundle input.");

    public static async Task<object> PrepareAsync(string workspace,NukeVietInput input,string folder,InventoryState qldd)
    {
        var privateRoot=Path.Combine(workspace,".local","evidence","phase3f")+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(folder).StartsWith(privateRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Private Phase3F destination required.");
        SourceInventory.NoReparse(Path.Combine(folder,"guard"));
        if(input.Provenance is not("ProvidedSnapshot" or "OfficialExport" or "TechnicalFixture")||string.IsNullOrWhiteSpace(input.Reference)||input.Snapshots.Length is <1 or >100||input.Snapshots.Sum(x=>x.Files.Length)>200)throw new InvalidDataException("Finite bundle/provenance required; fixtures are not real source acceptance.");
        var rows=new List<object>();var drafts=new List<NukeVietDraftCandidate>();var identities=new HashSet<string>();long total=0;
        foreach(var snapshot in input.Snapshots)
        {
            if(!Uri.TryCreate(snapshot.Url,UriKind.Absolute,out var source)||source.Host!="gdla.gov.vn"||source.Scheme is not("http" or "https")||!source.IsDefaultPort||source.UserInfo.Length>0)throw new InvalidDataException("Only public gdla source snapshots accepted.");
            var raw=await ReadPrivateAsync(workspace,snapshot.Path,snapshot.Sha256,8*1024*1024);total+=raw.Length;
            if(total>256L*1024*1024)throw new InvalidDataException("Bundle byte bound exceeded.");
            var retained=Path.Combine(folder,"snapshots",snapshot.Sha256+".html");Directory.CreateDirectory(Path.GetDirectoryName(retained)!);
            if(!File.Exists(retained)){await using var file=new FileStream(retained,FileMode.CreateNew);await file.WriteAsync(raw);}
            else if(SourceInventory.Hash(await File.ReadAllBytesAsync(retained))!=snapshot.Sha256)throw new InvalidDataException("Staged snapshot changed.");
            var identity=MigrationSourceIdentityPolicy.FromUrl(snapshot.Url);
            if(identity is null)
            {
                // Do not guess document selectors/routes without an official sample/schema.
                rows.Add(new{snapshot.Url,snapshot.Sha256,PrivateSnapshot=Path.GetRelativePath(folder,retained),Status="NEEDS_DOCUMENT_ADAPTER_OR_ROUTE_REVIEW",SqlImport="BLOCKED"});continue;
            }
            if(!identities.Add(identity.Key))throw new InvalidDataException("Duplicate source identity within bundle; review versions explicitly.");
            var preview=MigrationSourceIdentityPolicy.ParseNukeViet(new UTF8Encoding(false,true).GetString(raw),snapshot.Url,snapshot.Sha256);
            if(snapshot.Files.Select(x=>x.Url).Distinct().Count()!=snapshot.Files.Length||snapshot.Files.Any(x=>!preview.AssetUrls.Contains(x.Url,StringComparer.Ordinal)))throw new InvalidDataException("Asset not bound to this verified body.");
            var files=new List<object>();
            foreach(var local in snapshot.Files)
            {
                var bytes=await ReadPrivateAsync(workspace,local.Path,local.Sha256,32*1024*1024);total+=bytes.Length;
                if(total>256L*1024*1024)throw new InvalidDataException("Bundle byte bound exceeded.");
                var target=Path.Combine(folder,"quarantine",local.Sha256+".bin");Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                SourceInventory.NoReparse(target);
                if(!File.Exists(target)){await using var stream=new FileStream(target,FileMode.CreateNew);await stream.WriteAsync(bytes);}
                else if(SourceInventory.Hash(await File.ReadAllBytesAsync(target))!=local.Sha256)throw new InvalidDataException("Private staged bytes changed.");
                files.Add(new{local.Url,local.Sha256,Bytes=bytes.Length,PrivateKey=Path.GetRelativePath(folder,target),ScanStatus="NotScanned",PublicAccess=false});
            }
            var rawPath=Path.Combine(folder,"snapshots",snapshot.Sha256+".html");Directory.CreateDirectory(Path.GetDirectoryName(rawPath)!);
            if(!File.Exists(rawPath)){await using var f=new FileStream(rawPath,FileMode.CreateNew);await f.WriteAsync(raw);}
            else if(SourceInventory.Hash(await File.ReadAllBytesAsync(rawPath))!=snapshot.Sha256)throw new InvalidDataException("Staged snapshot changed.");
            drafts.Add(NukeVietDraftCandidate.Create(preview,snapshot.Files.Select(x=>x.Url)));
            rows.Add(new{Preview=preview,Files=files,MissingFiles=preview.AssetUrls.Except(snapshot.Files.Select(x=>x.Url)).ToArray(),
                DuplicateReview=qldd.Items.Select(x=>new{QlddIdentity=x.Source.Kind+":"+x.Source.SourceId,Evidence=MigrationSourceIdentityPolicy.DuplicateEvidence(preview,x.Source)}).Where(x=>x.Evidence!="NO_MATCH").ToArray(),
                UrlMapping=new{Old=preview.Url,Canonical=preview.CanonicalUrl,Target=(string?)null,Status="NEEDS_MAPPING_AND_DUPLICATE_REVIEW"},SqlImport="BLOCKED_MAPPING_SCANNER_SOURCE_ACCEPTANCE"});
        }
        await MediaRecovery.WriteNewAsync(Path.Combine(folder,"draft-candidates.json"),new{Status="PRIVATE_NORMALIZED_CANDIDATES_NOT_CMS_IMPORT",Rows=drafts,SqlWrites=0,NetworkRequests=0,PublicAllowed=false});
        return new{SchemaVersion=1,input.Provenance,input.Reference,Source="gdla.gov.vn",Rows=rows,DraftCandidates="draft-candidates.json",TotalBytes=total,SqlWrites=0,NetworkRequests=0,RealSourceAcceptance="NOT_VERIFIED",Mapping="NEEDS_BUSINESS_APPROVAL",Resume="Re-run exact input SHA into a new unique directory; content-addressed private files; no CMS commit"};
    }
}
