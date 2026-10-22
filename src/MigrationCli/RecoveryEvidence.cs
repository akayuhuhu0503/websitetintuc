using LandPortal.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
namespace LandPortal.MigrationCli;
public static class RecoveryEvidence
{
    public static async Task<string?> OverlayAsync(string[] args,string workspace,IConfiguration config,InventoryState state)
    {
        string? Opt(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        var setPath=Opt("--proof-set");if(setPath is null)return null;
        var setSha=Opt("--proof-set-sha")??throw new InvalidDataException("--proof-set-sha required");var invSha=Opt("--inventory-sha")??throw new InvalidDataException("--inventory-sha required");
        string Bound(string path){var p=Path.GetFullPath(path,workspace);if(!p.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/evidence/phase3c"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Recovery evidence outside Phase3C.");return p;}
        var inventory=await MediaRecovery.ReadBoundAsync<RecoveryInventory>(Bound(Opt("--inventory")??throw new InvalidDataException("--inventory required")),invSha);
        var set=await MediaRecovery.ReadBoundAsync<RecoveryProofSet>(Bound(setPath),setSha);
        // Only public Test bytes are ever scanned. Dev consumers read their verified Test receipts;
        // protected53 proofs are intentionally excluded from the Dev overlay.
        var connection=new SqlConnectionStringBuilder(DatabaseSettings.Connection(config)){InitialCatalog="LandPortal_Test"};
        var c=new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{{"Sql:ExpectedDatabase","LandPortal_Test"},{"ConnectionStrings:Portal",connection.ConnectionString},{"MigrationDiagnostics:Root",config["MigrationDiagnostics:RecoveryTestRoot"]??config["MigrationDiagnostics:Root"]},{"Storage:MediaRoot",Path.Combine(workspace,".local/tests/phase2a/media")},{"Storage:TempRoot",Path.Combine(workspace,".local/tests/phase2a/temp")},{"Storage:KeysRoot",Path.Combine(workspace,".local/tests/phase2a/keys")},{"Storage:SnapshotRoot",Path.Combine(workspace,".local/tests/phase2a/snapshots")}}).Build();
        await using var testDb=new PortalDbContext(DatabaseSettings.Options(c));await new MediaRecovery(testDb,StorageOptions.FromConfiguration(c),c,workspace).OverlayAsync(state,inventory,invSha,set,config["Sql:ExpectedDatabase"]=="LandPortal_Test");return setSha;
    }
}
