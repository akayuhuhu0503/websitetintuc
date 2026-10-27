using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace LandPortal.Infrastructure;
public static class PortalConfiguration
{
    public static string FindWorkspace()
    {
        if (Environment.GetEnvironmentVariable("LANDPORTAL_CONFIG") is { Length: > 0 } && Environment.GetEnvironmentVariable("LANDPORTAL_HOME") is { Length: > 0 } home)
        {
            if (!Path.IsPathFullyQualified(home)) throw new InvalidOperationException("Absolute LANDPORTAL_HOME required.");
            return Path.GetFullPath(home);
        }
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MASTER_PROMPT_LAND_PORTAL.md"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Chạy lệnh trong workspace Land Portal hoặc truyền workspace rõ ràng.");
    }
    public static IConfigurationRoot Load(string? workspace = null)
    {
        if (Environment.GetEnvironmentVariable("LANDPORTAL_CONFIG") is { Length: > 0 } external)
        {
            if (!Path.IsPathFullyQualified(external) || !File.Exists(external)) throw new InvalidOperationException("Existing absolute private UAT config required.");
            workspace = FindWorkspace();
            var full = Path.GetFullPath(external);
            if (!full.StartsWith(workspace.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("UAT config must stay in private data home.");
            for (string? node = full; node != null; node = Path.GetDirectoryName(node))
                if ((File.Exists(node) || Directory.Exists(node)) && (File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Reparse UAT config path refused.");
            var deployed = new ConfigurationBuilder().AddJsonFile(full, optional: false, reloadOnChange: false).AddEnvironmentVariables().AddInMemoryCollection(new Dictionary<string,string?> { ["WorkspaceRoot"] = workspace }).Build();
            if (!UatDeployment.Validate(deployed, Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production") && !LanDemoDeployment.Validate(deployed, Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production")) throw new InvalidOperationException("External configuration requires explicit isolated UAT or LAN demo profile.");
            return deployed; // Never read or copy the developer .env on a target host.
        }
        workspace ??= FindWorkspace();
        var values = new Dictionary<string, string?> { ["WorkspaceRoot"] = workspace };
        var file = Path.Combine(workspace, ".env");
        if (File.Exists(file))
            foreach (var line in File.ReadLines(file))
            {
                var text = line.Trim();
                if (text.Length == 0 || text.StartsWith('#')) continue;
                var eq = text.IndexOf('=');
                if (eq <= 0) throw new InvalidOperationException("Dòng cấu hình .env không hợp lệ (không in giá trị bí mật).");
                values[text[..eq].Trim().Replace("__", ":")] = text[(eq + 1)..].Trim().Trim('"');
            }
        return new ConfigurationBuilder().AddInMemoryCollection(values).AddEnvironmentVariables().Build();
    }
}
public static class DatabaseSettings
{
    public static string Connection(IConfiguration config)
    {
        var raw = config.GetConnectionString("Portal");
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("Thiếu ConnectionStrings__Portal. Cấu hình SQL Server trong .env hoặc environment.");
        var sql = new SqlConnectionStringBuilder(raw);
        var expected = config["Sql:ExpectedDatabase"];
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(sql.InitialCatalog, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("CSDL kết nối không khớp Sql__ExpectedDatabase. Đã chặn thao tác.");
        if (sql.InitialCatalog is "master" or "model" or "msdb" or "tempdb") throw new InvalidOperationException("Không được dùng CSDL hệ thống làm dữ liệu dự án.");
        return raw;
    }
    public static DbContextOptions<PortalDbContext> Options(IConfiguration config, bool forMigration = false)
    {
        var connection = new SqlConnectionStringBuilder(Connection(config));
        if (!forMigration && string.IsNullOrWhiteSpace(config["Runtime:RolePassword"]))
            throw new InvalidOperationException("Thiếu Runtime__RolePassword. Provision application role bằng lệnh migrate; runtime không dùng quyền operator mặc định.");
        var runtimeRole = !forMigration && !string.IsNullOrEmpty(config["Runtime:RolePassword"]);
        // Application-role sessions must not return to a pooled operator connection.
        if (runtimeRole) connection.Pooling = false;
        var options = new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(connection.ConnectionString, o => o.UseCompatibilityLevel(int.Parse(config["Sql:CompatibilityLevel"] ?? "160")));
        if (runtimeRole) options.AddInterceptors(new RuntimeRoleInterceptor(config));
        if (MigrationDiagnostics.Configure(config) is { } diagnostics) options.AddInterceptors(diagnostics);
        return options.Options;
    }
}
public sealed class StorageOptions
{
    public required string MediaRoot { get; init; }
    public required string SnapshotRoot { get; init; }
    public required string TempRoot { get; init; }
    public required string KeysRoot { get; init; }
    public static StorageOptions FromConfiguration(IConfiguration c)
    {
        string Read(string key)
        {
            var path = c["Storage:" + key];
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Thiếu Storage__" + key);
            return Path.GetFullPath(path, c["WorkspaceRoot"] ?? PortalConfiguration.FindWorkspace());
        }
        return new() { MediaRoot = Read("MediaRoot"), SnapshotRoot = Read("SnapshotRoot"), TempRoot = Read("TempRoot"), KeysRoot = Read("KeysRoot") };
    }
    public string ResolveMedia(string key) => ResolveWithin(MediaRoot, key);
    public string ResolveSnapshot(string key) => ResolveWithin(SnapshotRoot, key);
    public static string ResolveWithin(string root, string key)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(fullRoot, key));
        if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Đường dẫn vượt khỏi kho tệp.");
        return target;
    }
}
public class PortalDesignFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    public PortalDbContext CreateDbContext(string[] args) => new(DatabaseSettings.Options(PortalConfiguration.Load(), forMigration: true));
}
public static class SchemaManager
{
    public static async Task MigrateAsync(PortalDbContext db, string expectedDatabase)
    {
        await VerifyLocalProjectAsync(db, expectedDatabase);
        await db.Database.MigrateAsync();
    }
    public static async Task VerifyLocalProjectAsync(PortalDbContext db, string expectedDatabase)
    {
        var sql = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        if (expectedDatabase is not ("LandPortal_Dev" or "LandPortal_Test") ||
            !new[] { @".\SQLEXPRESS", @"localhost\SQLEXPRESS", @"(local)\SQLEXPRESS" }.Contains(sql.DataSource, StringComparer.OrdinalIgnoreCase) || !sql.IntegratedSecurity)
            throw new InvalidOperationException("Migration requires this project's local SQLEXPRESS database and Windows authentication.");
        if (sql.InitialCatalog != expectedDatabase || expectedDatabase is "master" or "model" or "msdb" or "tempdb") throw new InvalidOperationException("Đích migration không đúng CSDL dự án đã chỉ định.");
        var connectionWatch=System.Diagnostics.Stopwatch.StartNew();
        MigrationDiagnostics.Record("CONNECTION_START",new{purpose="local-target-guard",connection=MigrationDiagnostics.ConnectionInfo(db.Database.GetDbConnection())});
        try{await db.Database.OpenConnectionAsync();MigrationDiagnostics.Record("CONNECTION_PASS",new{purpose="local-target-guard",elapsedMs=connectionWatch.Elapsed.TotalMilliseconds,connection=MigrationDiagnostics.ConnectionInfo(db.Database.GetDbConnection())});}
        catch(Exception ex){MigrationDiagnostics.Record("CONNECTION_FAIL",new{purpose="local-target-guard",elapsedMs=connectionWatch.Elapsed.TotalMilliseconds,connection=MigrationDiagnostics.ConnectionInfo(db.Database.GetDbConnection()),error=ex.GetType().Name,sqlNumber=(ex as SqlException)?.Number});throw;}
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT compatibility_level,CAST(SERVERPROPERTY('MachineName') AS nvarchar(200)),DB_NAME() FROM sys.databases WHERE name=DB_NAME()";
        MigrationDiagnostics.Record("DIRECT_SQL_START",new{purpose="local-target-guard",fingerprint=MigrationDiagnostics.Fingerprint(command.CommandText),timeoutSeconds=command.CommandTimeout,transaction="NONE",connection=MigrationDiagnostics.ConnectionInfo(command.Connection)});
        var diagnosticWatch=System.Diagnostics.Stopwatch.StartNew();
        try
        {
        await using(var reader=await command.ExecuteReaderAsync())
        {
            if(!await reader.ReadAsync() || !string.Equals(reader.GetString(1),Environment.MachineName,StringComparison.OrdinalIgnoreCase) || reader.GetString(2) != expectedDatabase) throw new InvalidOperationException("Target is not this machine's named project database.");
            if(reader.GetByte(0)<160) throw new InvalidOperationException("Bản chạy đầu tiên yêu cầu compatibility 160 đã xác minh. Không tự nâng compatibility của CSDL hiện hữu.");
        }
        MigrationDiagnostics.Record("DIRECT_SQL_PASS",new{purpose="local-target-guard",elapsedMs=diagnosticWatch.Elapsed.TotalMilliseconds});
        }
        catch(Exception ex){MigrationDiagnostics.Record("DIRECT_SQL_FAIL",new{purpose="local-target-guard",elapsedMs=diagnosticWatch.Elapsed.TotalMilliseconds,error=ex.GetType().Name,sqlNumber=(ex as SqlException)?.Number});throw;}
        finally{await db.Database.CloseConnectionAsync();}
    }
}
