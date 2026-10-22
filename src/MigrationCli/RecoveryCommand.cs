using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.Extensions.Configuration;
namespace LandPortal.MigrationCli;
public static class RecoveryCommand
{
    public static async Task<int> ExecuteAsync(string[] args,string workspace,IConfiguration config,PortalDbContext db)
    {
        string Opt(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:throw new InvalidDataException(key+" required");}
        var report=Path.GetFullPath(Opt("--report"),workspace);if(!report.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/evidence/phase3c"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(report))throw new InvalidDataException("Unique Phase3C report required.");Directory.CreateDirectory(report);
        var service=new MediaRecovery(db,StorageOptions.FromConfiguration(config),config,workspace);
        if(args[0]=="recovery-public-inventory"){
            var hash=Opt("--checkpoint-sha");var state=await SourceBatch.ReadCheckpointAsync(workspace,Opt("--checkpoint"),hash);
            var parentSha=Opt("--parent-checkpoint-sha");var parent=await SourceBatch.ReadCheckpointAsync(workspace,Opt("--parent-checkpoint"),parentSha);
            var inventory=await service.PublicBatchInventoryAsync(state,hash,parent,parentSha,Path.GetFullPath(Opt("--source-proof"),workspace),Opt("--source-proof-sha"));
            var sha=await MediaRecovery.WriteNewAsync(Path.Combine(report,"inventory.json"),inventory);await service.RegisterAsync(inventory,sha);
            await MediaRecovery.WriteNewAsync(Path.Combine(report,"summary.json"),new{inventory.JobId,InventorySha256=sha,inventory.Scope,Resources=inventory.Resources.Count,OriginalDevPromoted=false});
        }else if(args[0]=="recovery-inventory"){
            var hash=Opt("--checkpoint-sha");var state=await SourceBatch.ReadCheckpointAsync(workspace,Opt("--checkpoint"),hash);var inventory=await service.InventoryAsync(state,hash,Path.GetFullPath(Opt("--source-proof"),workspace),Opt("--source-proof-sha"));
            var sha=await MediaRecovery.WriteNewAsync(Path.Combine(report,"inventory.json"),inventory);await service.RegisterAsync(inventory,sha);await MediaRecovery.WriteNewAsync(Path.Combine(report,"summary.json"),new{inventory.JobId,InventorySha256=sha,ProtectedOriginal=inventory.Resources.Count(x=>x.IsProtectedSource),AdditionalRefresh=inventory.Resources.Count(x=>!x.IsProtectedSource)});
        }else{
            var inventorySha=Opt("--inventory-sha");var inventory=await MediaRecovery.ReadBoundAsync<RecoveryInventory>(Path.GetFullPath(Opt("--inventory"),workspace),inventorySha);
            if(args[0]=="recovery-sql-check"){
                var readiness=await service.SqlReadinessAsync(inventory,inventorySha);
                await MediaRecovery.WriteNewAsync(Path.Combine(report,"sql-readiness.json"),readiness);
            }else if(args[0]=="recovery-register"){
                var state=await SourceBatch.ReadCheckpointAsync(workspace,Opt("--checkpoint"),Opt("--checkpoint-sha"));var fresh=await service.InventoryAsync(state,inventory.CheckpointSha256,Path.GetFullPath(Opt("--source-proof"),workspace),Opt("--source-proof-sha"));fresh.JobId=inventory.JobId;
                if(JsonSerializer.Serialize(fresh)!=JsonSerializer.Serialize(inventory))throw new InvalidDataException("Recovered job manifest differs from original source proof.");
                await service.RegisterAsync(inventory,inventorySha);await MediaRecovery.WriteNewAsync(Path.Combine(report,"registered.json"),new{inventory.JobId,InventorySha256=inventorySha,RevalidatedOriginalManifest=true});
            }else if(args[0]=="recovery-scan"){
                var stop=Array.IndexOf(args,"--stop-after");using var ct=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;ct.Cancel();};
                var set=await service.RunAsync(inventory,inventorySha,stop>=0?int.Parse(args[stop+1]):0,ct.Token,args.Contains("--reuse-only",StringComparer.Ordinal));var sha=await MediaRecovery.WriteNewAsync(Path.Combine(report,"proof-set.json"),set);
                await MediaRecovery.WriteNewAsync(Path.Combine(report,"summary.json"),new{inventory.JobId,Completed=set.Proofs.Count,Expected=inventory.Resources.Count,ProofSetSha256=sha});
            }else if(args[0]=="recovery-stage-test"){
                var set=await MediaRecovery.ReadBoundAsync<RecoveryProofSet>(Path.GetFullPath(Opt("--proof-set"),workspace),Opt("--proof-set-sha"));var changed=await service.StageTestAsync(inventory,inventorySha,set);await MediaRecovery.WriteNewAsync(Path.Combine(report,"stage.json"),new{ChangedTestMedia=changed,DevApplied=false});
            }else throw new InvalidDataException("Unknown recovery command.");
        }Console.WriteLine("Phase3C report "+Path.GetRelativePath(workspace,report));return 0;
    }
}
