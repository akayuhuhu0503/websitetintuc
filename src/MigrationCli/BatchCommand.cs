using System.Text;
using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.MigrationCli;
public static class BatchCommand
{
    public static async Task<int> ExecuteAsync(string[] args,string workspace,IConfiguration config,PortalDbContext db)
    {
        string? Opt(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        var command=args[0];var path=Opt("--checkpoint")??throw new InvalidDataException("--checkpoint required");var hash=Opt("--checkpoint-sha")??throw new InvalidDataException("--checkpoint-sha required");
        var folder=Path.GetFullPath(Opt("--report")??Path.Combine(".local","evidence","phase3b","batch-"+DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ")+"-"+Guid.NewGuid().ToString("N")[..8]),workspace);
        if(!new[]{"phase3b","phase3c","phase3d","phase3e","phase3f"}.Any(x=>folder.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/evidence",x))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))||Directory.Exists(folder))throw new InvalidDataException("Unique batch report directory required.");
        Directory.CreateDirectory(folder);
        var state=await MigrationDiagnostics.StepAsync("preflight.checkpoint",()=>SourceBatch.ReadCheckpointAsync(workspace,path,hash));
        var recoverySha=await MigrationDiagnostics.StepAsync("preflight.recovery-proof",()=>RecoveryEvidence.OverlayAsync(args,workspace,config,state));
        var storage=StorageOptions.FromConfiguration(config);var service=new SourceBatch(db,storage,config,workspace,state,hash,recoverySha);
        async Task Write(string name,object value){await using var f=new FileStream(Path.Combine(folder,name),FileMode.CreateNew);await JsonSerializer.SerializeAsync(f,value,new JsonSerializerOptions{WriteIndented=true});}
        if(command=="batch-preview"){
            Dictionary<string,List<OptionalImageReview>>? optional=null;
            if(Opt("--optional-images") is string reviewPath){
                var reviewFull=Path.GetFullPath(reviewPath,workspace);
                if(!new[]{"phase3b","phase3c","phase3d","phase3e","phase3f"}.Any(x=>reviewFull.StartsWith(Path.Combine(workspace,".local","evidence",x)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Reviewed optional images must be in private batch evidence.");
                SourceInventory.NoReparse(reviewFull);var reviewBytes=await File.ReadAllBytesAsync(reviewFull);
                if(SourceInventory.Hash(reviewBytes)!=Opt("--optional-images-sha"))throw new InvalidDataException("Exact optional-image review SHA required.");
                optional=JsonSerializer.Deserialize<Dictionary<string,List<OptionalImageReview>>>(reviewBytes)??throw new InvalidDataException("Invalid optional image review.");
                if(optional.Keys.Any(key=>!state.Items.Any(x=>x.Source.Kind+":"+x.Source.SourceId==key)))throw new InvalidDataException("Optional review contains unknown source identity.");
            }
            var plan=await service.PreviewAsync(optional);await Write("plan.json",plan);
            var rows=new List<string>{"SourceIdentity,SourceURL,ContentType,ExistingCMSId,ImportAction,Classification,ValidationResult"};
            string Csv(string x)=>"\""+("=+-@\t\r".Contains(x.FirstOrDefault())?"'":"")+x.Replace("\"","\"\"")+"\"";
            rows.AddRange(plan.Rows.Select(x=>string.Join(",",new[]{x.Kind+":"+x.SourceId,x.SourceUrl,x.Kind.ToString(),x.ExistingCmsId?.ToString()??"",x.Action,x.Classification,string.Join(";",x.Validation)}.Select(Csv))));
            await File.WriteAllLinesAsync(Path.Combine(folder,"mapping.csv"),rows,new UTF8Encoding(true));
            var findings=new List<string>{"SourceIdentity,Severity,Code,URL,Role,Resolution"};
            findings.AddRange(plan.Rows.SelectMany(x=>x.Findings.Select(f=>string.Join(",",new[]{x.Kind+":"+x.SourceId,f.Severity,f.Code,f.Url??"",f.Role,f.Resolution}.Select(Csv)))));
            await File.WriteAllLinesAsync(Path.Combine(folder,"findings.csv"),findings,new UTF8Encoding(true));
            await Write("summary.json",new{plan.BatchId,plan.Database,Rows=plan.Rows.Count,Classification=plan.Rows.GroupBy(x=>x.Classification).Select(x=>new{Kind=x.Key,Count=x.Count()}),Actions=plan.Rows.GroupBy(x=>x.Action).Select(x=>new{Action=x.Key,Count=x.Count()}),PlanSha256=SourceInventory.Hash(await File.ReadAllBytesAsync(Path.Combine(folder,"plan.json")))});
        }else{
            var planPath=Path.GetFullPath(Opt("--plan")??throw new InvalidDataException("--plan required"),workspace);
            if(!new[]{"phase3b","phase3c","phase3d","phase3e","phase3f"}.Any(x=>planPath.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/evidence",x))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Plan outside batch evidence.");
            SourceInventory.NoReparse(planPath);var bytes=await File.ReadAllBytesAsync(planPath);var sha=SourceInventory.Hash(bytes);
            if(sha!=Opt("--plan-sha"))throw new InvalidDataException("Reviewed plan SHA required.");
            var plan=JsonSerializer.Deserialize<SourceBatchPlan>(bytes)??throw new InvalidDataException("Invalid plan");
            if(command=="batch-reconcile" || command=="batch-preflight"){
                var reconciliation=await MigrationDiagnostics.StepAsync("preflight.reconcile",()=>service.ReconcileAsync(plan,sha),plan.BatchId,plan.Rows.Count(x=>x.Action!="Blocked"));await Write("reconcile.json",reconciliation);if(reconciliation.Status=="NEEDS_RECONCILIATION")return 2;
            }else if(command=="batch-commit"){
                var result=await MigrationDiagnostics.StepAsync("commit",()=>service.CommitAsync(plan,sha,int.Parse(Opt("--stop-after")??"0")),plan.BatchId,plan.Rows.Count(x=>x.Action!="Blocked"));await Write("commit.json",result);
                if(result.Any(x=>x.Status is "Failed" or "NeedsReconciliation"))return 1;
            }else if(command=="batch-verify"){
                var errors=await MigrationDiagnostics.StepAsync("readback.verify",()=>service.VerifyAsync(plan,sha),plan.BatchId,plan.Rows.Count(x=>x.Action!="Blocked"));await Write("verify.json",new{Status=errors.Count==0?"PASS":"FAIL",Errors=errors});if(errors.Count>0)return 1;
                await Write("readback.json",await MigrationDiagnostics.StepAsync("readback.snapshot",()=>service.ReadbackAsync(plan,sha),plan.BatchId,plan.Rows.Count(x=>x.Action!="Blocked")));
            }else if(command=="batch-rollback")await Write("rollback.json",new{Withdrawn=await service.RollbackPublicationAsync(plan,sha),Policy="Publication rollback only; source revisions/files/audit retained; edited content denied"});
            else throw new InvalidDataException("Unknown batch command.");
        }
        Console.WriteLine("Phase3B report "+Path.GetRelativePath(workspace,folder));return 0;
    }
}
