using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.MigrationCli;

// Read-only operator check. Does not provision SQL, initialize Identity, process
// queues, send mail or contact the source websites.
public static class UatPreflightCommand
{
    public static async Task VerifySqlTargetAsync(PortalDbContext db,IConfiguration config)
    {
        if(!UatDeployment.Validate(config,Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??"Production"))throw new InvalidOperationException("Approved isolated UAT profile required before SQL target verification.");
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command=db.Database.GetDbConnection().CreateCommand();
            command.CommandText="SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),DB_NAME(),(SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory),compatibility_level FROM sys.databases WHERE name=DB_NAME()";
            await using var reader=await command.ExecuteReaderAsync();
            if(!await reader.ReadAsync()||!string.Equals(reader.GetString(0),config["Deployment:ExpectedSqlServer"],StringComparison.OrdinalIgnoreCase)||reader.GetString(1)!=config["Sql:ExpectedDatabase"]||reader.GetString(2)!=db.Database.GetMigrations().Last()||reader.GetByte(3)<160)throw new InvalidOperationException("UAT physical SQL target/schema mismatch; stop before writes.");
        }
        finally{await db.Database.CloseConnectionAsync();}
    }
    public static async Task<int> ExecuteAsync(string[] args, IConfiguration config)
    {
        if (!UatDeployment.Validate(config, Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"))
            throw new InvalidOperationException("Explicit approved UAT configuration required");
        var at = Array.IndexOf(args, "--report");
        if (at < 0 || at + 1 >= args.Length) throw new InvalidOperationException("New private --report required");
        var report = Path.GetFullPath(args[at + 1]);
        var home = Path.GetFullPath(config["WorkspaceRoot"]!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!report.StartsWith(home, StringComparison.OrdinalIgnoreCase) || File.Exists(report))
            throw new InvalidOperationException("Report must be new and under private data home");
        SourceInventory.NoReparse(report);
        var storage = StorageOptions.FromConfiguration(config);
        foreach (var root in new[] { storage.MediaRoot, storage.SnapshotRoot, storage.TempRoot, storage.KeysRoot })
            if (!Directory.Exists(root)) throw new InvalidOperationException("Operator must provision all four private stores first");
        string sqlStatus = "NOT RUN";
        if (args.Contains("--online"))
        {
            // Runtime role is exercised; no operator privilege fallback.
            await using var db = new PortalDbContext(DatabaseSettings.Options(config));
            await VerifySqlTargetAsync(db,config);
            sqlStatus = "PASS_READ_ONLY_TARGET_SCHEMA_RUNTIME_ROLE";
        }
        var result = new
        {
            status = args.Contains("--online") ? "READ_ONLY_SQL_PREFLIGHT_PASS" : "OFFLINE_CONFIG_PASS",
            database = config["Sql:ExpectedDatabase"], server = config["Deployment:ExpectedSqlServer"],
            approvalReference = config["Deployment:TargetApprovalReference"], sql = sqlStatus,
            scanner = "NOT RUN: use InquiryScan/Documents/Video --health on target", smtp = "NOT RUN: approved relay acceptance required",
            iisHttps = "NOT RUN", authentication = "Password + configured CAPTCHA; ATTT acceptance pending", writes = 0, productionReady = false
        };
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        await using var file = new FileStream(report, FileMode.CreateNew);
        await JsonSerializer.SerializeAsync(file, result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(result.status + "; no provisioning, mail, Identity or application data writes.");
        return 0;
    }
}
