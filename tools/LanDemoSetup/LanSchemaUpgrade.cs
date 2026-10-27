using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

// Exact additive upgrade of an existing demo. Never invokes bootstrap, users, grants or seeds.
static class LanSchemaUpgrade
{
    const string From="20261023074121_Srs12MenuAndInquiryLookup",To="20261026095148_LandAreaStatistics";
    public static async Task RunAsync(string root,IConfiguration config,PortalDbContext db,string[] args)
    {
        if(!LanDemoDeployment.Validate(config,"Development")||db.Database.GetDbConnection().Database!=LanDemoDeployment.Database)throw new InvalidOperationException("Isolated LAN database required.");
        string Option(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:throw new InvalidOperationException(key+" required");}
        string Private(string value,string prefix){var path=Path.GetFullPath(value,root);var allowed=Path.GetFullPath(Path.Combine(root,prefix))+Path.DirectorySeparatorChar;if(!path.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Private project scope required.");for(var item=new DirectoryInfo(path);item!=null&&item.FullName.Length>=root.Length;item=item.Parent)if(item.Exists&&(item.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Reparse path refused.");return path;}
        var report=Private(Option("--report-root"),".local/evidence/lan-demo");Directory.CreateDirectory(report);
        var backup=Private(Option("--backup-root"),".local/backups");if(!Directory.Exists(backup)||File.Exists(Path.Combine(backup,"LandPortal_LanDemo.bak")))throw new InvalidOperationException("New existing private backup directory required.");
        await db.Database.OpenConnectionAsync();var connection=db.Database.GetDbConnection();
        using(var target=connection.CreateCommand()){target.CommandText="SELECT DB_NAME(),CAST(SERVERPROPERTY('MachineName') AS nvarchar(200)),CAST(SERVERPROPERTY('ServerName') AS nvarchar(200))";using var reader=await target.ExecuteReaderAsync();if(!await reader.ReadAsync()||reader.GetString(0)!=LanDemoDeployment.Database||!reader.GetString(1).Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase)||!reader.GetString(2).Equals(Environment.MachineName+@"\SQLEXPRESS",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("LAN SQL identity differs.");}
        var pending=(await db.Database.GetPendingMigrationsAsync()).ToArray();var applied=(await db.Database.GetAppliedMigrationsAsync()).ToArray();
        if(applied.LastOrDefault()!=From||pending.Length!=1||pending[0]!=To)throw new InvalidOperationException("Expected exactly the reviewed additive statistics migration; no seed or arbitrary upgrade.");
        var before=await Digest();await Write("before-tables.json",before);
        var bak=Path.Combine(backup,"LandPortal_LanDemo.bak");
        foreach(var sql in new[]{"BACKUP DATABASE [LandPortal_LanDemo] TO DISK=@path WITH COPY_ONLY,CHECKSUM","RESTORE VERIFYONLY FROM DISK=@path WITH CHECKSUM"})
        {using var cmd=connection.CreateCommand();cmd.CommandText=sql;cmd.CommandTimeout=120;var p=cmd.CreateParameter();p.ParameterName="@path";p.Value=bak;cmd.Parameters.Add(p);await cmd.ExecuteNonQueryAsync();}
        using(var cmd=connection.CreateCommand()){cmd.CommandText="RESTORE HEADERONLY FROM DISK=@path";var p=cmd.CreateParameter();p.ParameterName="@path";p.Value=bak;cmd.Parameters.Add(p);using var reader=await cmd.ExecuteReaderAsync();if(!await reader.ReadAsync()||reader["DatabaseName"].ToString()!=LanDemoDeployment.Database||!(bool)reader["HasBackupChecksums"]||!(bool)reader["IsCopyOnly"])throw new InvalidOperationException("LAN backup header mismatch.");}
        await using(var stream=File.OpenRead(bak))await Write("sql-backup.json",new{database=LanDemoDeployment.Database,path=bak,bytes=stream.Length,sha256=Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)),verifyOnly="PASS",sqlRestore="NOT_RUN",utc=DateTimeOffset.UtcNow});
        // Recheck actual source/copy bytes, not merely an operator-supplied PASS label.
        using(var manifest=JsonDocument.Parse(await File.ReadAllBytesAsync(Private(Option("--file-manifest"),".local/backups"))))
        {
            var data=manifest.RootElement;var destination=Private(data.GetProperty("destination").GetString()!,".local/backups");
            if(!destination.StartsWith(backup+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("LAN file snapshot outside this restore point.");
            var stores=StorageOptions.FromConfiguration(config);var home=Path.GetFullPath(config["WorkspaceRoot"]!);
            var expected=new[]{stores.MediaRoot,stores.SnapshotRoot,stores.KeysRoot}.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).Append(Path.Combine(home,"config.json")).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actual=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in data.GetProperty("files").EnumerateArray())
            {
                var source=Private(entry.GetProperty("source").GetString()!,".local/lan-demo");var copy=Private(Path.Combine(destination,entry.GetProperty("key").GetString()!),".local/backups");
                if(!expected.Contains(source)||!actual.Add(source))throw new InvalidOperationException("Unexpected/duplicate LAN snapshot file.");
                foreach(var path in new[]{source,copy}){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Reparse file refused.");await using var stream=File.OpenRead(path);if(stream.Length!=entry.GetProperty("bytes").GetInt64()||Convert.ToHexStringLower(await SHA256.HashDataAsync(stream))!=entry.GetProperty("sha256").GetString())throw new InvalidOperationException("LAN snapshot byte mismatch.");}
            }
            if(!actual.SetEquals(expected))throw new InvalidOperationException("LAN snapshot has missing files.");
        }
        await db.GetService<IMigrator>().MigrateAsync(To);
        var after=await Digest();await Write("after-tables.json",after);
        if(before.Any(x=>!after.TryGetValue(x.Key,out var value)||value!=x.Value))throw new InvalidOperationException("Existing LAN rows changed; stop and reconcile before restarting.");
        if(!after.TryGetValue("LandStatisticsVersions",out var stats)||stats.Rows!=0)throw new InvalidOperationException("Expected new empty statistics table, no official data seeded.");
        await Write("upgrade.json",new{status="PASS",database=LanDemoDeployment.Database,migration=To,preservedTables=before.Count,mismatches=0,identityPasswordsMfaRolesPreserved=true,seeded=false,granted=false});
        Console.WriteLine("LAN exact schema upgrade PASS; existing rows/Identity/MFA/roles preserved; no seed/grant.");
        async Task Write(string name,object data){var path=Path.Combine(report,name);await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write);await JsonSerializer.SerializeAsync(output,data,new JsonSerializerOptions{WriteIndented=true});}
        async Task<Dictionary<string,TableDigest>> Digest()
        {
            var tables=new List<string>();using(var cmd=connection.CreateCommand()){cmd.CommandText="SELECT name FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND name<>'__EFMigrationsHistory' ORDER BY name";using var reader=await cmd.ExecuteReaderAsync();while(await reader.ReadAsync())tables.Add(reader.GetString(0));}
            var result=new Dictionary<string,TableDigest>();foreach(var table in tables){using var cmd=connection.CreateCommand();cmd.CommandText="SELECT COALESCE(NULLIF((SELECT * FROM [dbo].["+table.Replace("]","]]")+"] FOR JSON PATH, INCLUDE_NULL_VALUES),N''),N'[]')";var json=new StringBuilder();using(var reader=await cmd.ExecuteReaderAsync())while(await reader.ReadAsync())json.Append(reader.GetString(0));using var doc=JsonDocument.Parse(json.ToString());var rows=doc.RootElement.EnumerateArray().Select(x=>x.GetRawText()).Order(StringComparer.Ordinal).ToArray();result[table]=new(rows.Length,Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',rows)))));}
            return result;
        }
    }
    sealed record TableDigest(int Rows,string Sha256);
}
