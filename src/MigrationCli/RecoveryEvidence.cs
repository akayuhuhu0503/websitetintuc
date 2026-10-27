using LandPortal.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
namespace LandPortal.MigrationCli;
public static class RecoveryEvidence
{
    public static async Task<Dictionary<string,List<OptionalImageReview>>?> ReadOptionalImagesAsync(string[] args,string workspace)
    {
        string? Opt(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        if(Opt("--optional-images") is not string path)return null;
        var full=Path.GetFullPath(path,workspace);
        if(!new[]{"phase3b","phase3c","phase3d","phase3e","phase3f"}.Any(x=>full.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/evidence",x))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Optional review outside private batch evidence.");
        return await MediaRecovery.ReadBoundAsync<Dictionary<string,List<OptionalImageReview>>>(full,Opt("--optional-images-sha")??throw new InvalidDataException("Exact optional-image review SHA required."));
    }
    public static void DemandOptionalBinding(RecoveryInventory inventory,IReadOnlyDictionary<string,List<OptionalImageReview>>? reviews)
    {
        string Canonical(IReadOnlyDictionary<string,List<OptionalImageReview>>? value)=>JsonSerializer.Serialize((value??new Dictionary<string,List<OptionalImageReview>>()).Where(x=>x.Value.Count>0).OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>new{x.Key,Reviews=x.Value.OrderBy(r=>r.Url,StringComparer.Ordinal)}));
        if(inventory.Scope==MediaRecovery.PublicBatchScope&&Canonical(inventory.ReviewedOptionalImages)!=Canonical(reviews))throw new InvalidDataException("Strict proof optional-image reviews differ from the exact preview/plan.");
    }
    public static async Task<string?> OverlayAsync(string[] args,string workspace,IConfiguration config,InventoryState state)
    {
        string? Opt(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        var setPath=Opt("--proof-set");if(setPath is null)return null;
        var setSha=Opt("--proof-set-sha")??throw new InvalidDataException("--proof-set-sha required");var invSha=Opt("--inventory-sha")??throw new InvalidDataException("--inventory-sha required");
        string Bound(string path){var p=Path.GetFullPath(path,workspace);if(!p.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/evidence/phase3c"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Recovery evidence outside Phase3C.");return p;}
        var inventory=await MediaRecovery.ReadBoundAsync<RecoveryInventory>(Bound(Opt("--inventory")??throw new InvalidDataException("--inventory required")),invSha);
        var set=await MediaRecovery.ReadBoundAsync<RecoveryProofSet>(Bound(setPath),setSha);
        if(inventory.Scope==MediaRecovery.PublicBatchScope)
        {
            if(inventory.CheckpointSha256!=Opt("--checkpoint-sha"))throw new InvalidDataException("Public recovery proof checkpoint differs from the exact batch.");
            Dictionary<string,List<OptionalImageReview>>? reviews;
            if(Opt("--plan") is string planPath){
                var plan=await MediaRecovery.ReadBoundAsync<SourceBatchPlan>(Path.GetFullPath(planPath,workspace),Opt("--plan-sha")??throw new InvalidDataException("Exact plan SHA required."));
                if(plan.CheckpointSha256!=inventory.CheckpointSha256||plan.InventoryId!=inventory.InventoryId||plan.RecoveryProofSha256!=setSha||plan.Rows.Count!=state.Items.Count||plan.Rows.Any(r=>!state.Items.Any(x=>x.Source.Kind==r.Kind&&x.Source.SourceId==r.SourceId)))throw new InvalidDataException("Public proof plan/session/scope differs.");
                reviews=plan.Rows.ToDictionary(x=>x.Kind+":"+x.SourceId,x=>x.OptionalImages);
            }else reviews=await ReadOptionalImagesAsync(args,workspace);
            DemandOptionalBinding(inventory,reviews);
        }
        // Only public Test bytes are ever scanned. Dev consumers read their verified Test receipts;
        // protected53 proofs are intentionally excluded from the Dev overlay.
        var connection=new SqlConnectionStringBuilder(DatabaseSettings.Connection(config)){InitialCatalog="LandPortal_Test"};
        var c=new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{{"Sql:ExpectedDatabase","LandPortal_Test"},{"ConnectionStrings:Portal",connection.ConnectionString},{"MigrationDiagnostics:Root",config["MigrationDiagnostics:RecoveryTestRoot"]??config["MigrationDiagnostics:Root"]},{"Storage:MediaRoot",Path.Combine(workspace,".local/tests/phase2a/media")},{"Storage:TempRoot",Path.Combine(workspace,".local/tests/phase2a/temp")},{"Storage:KeysRoot",Path.Combine(workspace,".local/tests/phase2a/keys")},{"Storage:SnapshotRoot",Path.Combine(workspace,".local/tests/phase2a/snapshots")}}).Build();
        await using var testDb=new PortalDbContext(DatabaseSettings.Options(c));await new MediaRecovery(testDb,StorageOptions.FromConfiguration(c),c,workspace).OverlayAsync(state,inventory,invSha,set,config["Sql:ExpectedDatabase"]=="LandPortal_Test");return setSha;
    }
}
