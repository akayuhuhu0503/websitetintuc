using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.Extensions.Configuration;
namespace LandPortal.MigrationCli;
public static class SafeBatchCommand
{
    public static async Task<int> ExecuteAsync(string[] args,string workspace,IConfiguration config,PortalDbContext db)
    {
        string Opt(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:throw new InvalidDataException(key+" required");}
        var report=Path.GetFullPath(Opt("--report"),workspace);if(!new[]{"phase3c","phase3d","phase3e","phase3f"}.Any(x=>report.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/evidence",x))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))||Directory.Exists(report))throw new InvalidDataException("Unique Safe Apply report required.");Directory.CreateDirectory(report);
        var planPath=Path.GetFullPath(Opt("--plan"),workspace);if(!new[]{"phase3b","phase3c","phase3d","phase3e","phase3f"}.Any(x=>planPath.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/evidence",x))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Reviewed plan outside batch evidence.");
        var sha=Opt("--plan-sha");var plan=await MediaRecovery.ReadBoundAsync<SourceBatchPlan>(planPath,sha);
        if(plan.Database!=config["Sql:ExpectedDatabase"])throw new InvalidDataException("Plan target differs from connection.");
        var state=await SourceBatch.ReadCheckpointAsync(workspace,Opt("--checkpoint"),Opt("--checkpoint-sha"));var recoverySha=await RecoveryEvidence.OverlayAsync(args,workspace,config,state);
        ApprovedBatchExecution? approved=null;
        if(args[0]!="safe-verify"){
            if(plan.Database=="LandPortal_Dev")approved=await ApprovedBatchExecution.CreateAsync(db,config,workspace,plan,sha,args[0]=="safe-apply"?"Apply":"Rollback",Opt("--approval"),Opt("--backup-receipt"));
            else{if(plan.Database!="LandPortal_Test"||Opt("--confirm-test")!=sha)throw new UnauthorizedAccessException("Explicit exact Test plan confirmation required.");await ApprovedBatchExecution.VerifyBackupAsync(db,workspace,plan.Database,Opt("--backup-receipt"));}
        }
        var service=new SourceBatch(db,StorageOptions.FromConfiguration(config),config,workspace,state,Opt("--checkpoint-sha"),recoverySha,approved);
        if(args[0]=="safe-apply"){
            var result=await service.CommitAsync(plan,sha);await MediaRecovery.WriteNewAsync(Path.Combine(report,"apply.json"),result);return result.Any(x=>x.Status is "Failed" or "NeedsReconciliation")?1:0;
        }
        if(args[0]=="safe-rollback"){
            var changed=await service.RollbackPublicationAsync(plan,sha);await MediaRecovery.WriteNewAsync(Path.Combine(report,"rollback.json"),new{Withdrawn=changed,Policy="Withdraw batch-owned unedited new publications only; preserve source/revisions/files/audit/aliases. No database reset or filesystem deletion."});return 0;
        }
        if(args[0]=="safe-verify"){
            var errors=await service.VerifyAsync(plan,sha);await MediaRecovery.WriteNewAsync(Path.Combine(report,"verify.json"),new{Errors=errors,Status=errors.Count==0?"PASS":"FAIL"});return errors.Count==0?0:1;
        }throw new InvalidDataException("Unknown safe command.");
    }
}
