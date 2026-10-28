using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace LandPortal.MigrationCli;

// Explicit metadata-only probe for a fresh shared database before role/schema exist.
public static class UatSqlProbe
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        string Arg(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:throw new InvalidDataException("Private --home, --config, --report required.");}
        var home=Path.GetFullPath(Arg("--home")).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        var path=Path.GetFullPath(Arg("--config"));var report=Path.GetFullPath(Arg("--report"));
        if(!path.StartsWith(home,StringComparison.OrdinalIgnoreCase)||!report.StartsWith(home,StringComparison.OrdinalIgnoreCase)||File.Exists(report))throw new InvalidDataException("Existing private config and new report under private home required.");
        var app=Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(home.StartsWith(app,StringComparison.OrdinalIgnoreCase)||app.StartsWith(home,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Probe config/report must be outside application code.");
        SourceInventory.NoReparse(path);SourceInventory.NoReparse(report);
        var config=new ConfigurationBuilder().AddJsonFile(path,false,false).Build();
        var sql=new SqlConnectionStringBuilder(DatabaseSettings.Connection(config));
        if(config["Deployment:Profile"]!="UAT"||config["Deployment:TargetApproved"]!="true"||config["Deployment:TargetApprovalReference"] is not {Length:>4}||!UatDeployment.IsTargetDatabase(sql.InitialCatalog)||sql.DataSource!=config["Deployment:ExpectedSqlServer"]||!UatDeployment.AuthenticationMatches(config,sql)||sql.Encrypt==SqlConnectionEncryptOption.Optional||sql.TrustServerCertificate)throw new InvalidOperationException("Read-only target/authentication/TLS guard failed; no connection attempted.");
        sql.Pooling=false;sql.ConnectTimeout=10;
        await using var connection=new SqlConnection(sql.ConnectionString);
        try
        {
            await connection.OpenAsync();
            await using var cmd=connection.CreateCommand();cmd.CommandTimeout=10;
            cmd.CommandText="SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),DB_NAME(),ORIGINAL_LOGIN(),compatibility_level,IS_SRVROLEMEMBER('sysadmin'),IS_MEMBER('db_owner'),OBJECT_ID(N'dbo.__EFMigrationsHistory'),USER_NAME() FROM sys.databases WHERE name=DB_NAME()";
            await using var reader=await cmd.ExecuteReaderAsync();if(!await reader.ReadAsync())throw new InvalidOperationException("SQL identity metadata unavailable.");
            var physical=reader.GetString(0);var database=reader.GetString(1);var login=reader.GetString(2);
            if(database!=sql.InitialCatalog||(!sql.IntegratedSecurity&&login!=sql.UserID)||reader.GetInt32(4)==1||reader.GetInt32(5)==1)throw new InvalidOperationException("Runtime identity/database differs or has forbidden administrative membership.");
            var expected=config["Deployment:ExpectedSqlServerIdentity"];
            if(!string.IsNullOrWhiteSpace(expected)&&!string.Equals(expected,physical,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Physical SQL identity mismatch.");
            var result=new{status=string.IsNullOrWhiteSpace(expected)?"READ_ONLY_DISCOVERY_NEEDS_DBA_IDENTITY_CONFIRMATION":"READ_ONLY_IDENTITY_PASS",endpoint=sql.DataSource,physicalServer=physical,database,login,mappedUser=reader.GetString(7),connection.ServerVersion,compatibility=reader.GetByte(3),schemaVisible=!reader.IsDBNull(6),writes=0,runtimeRole="NOT RUN",schemaImport="NOT RUN"};
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);await using var file=new FileStream(report,FileMode.CreateNew);await JsonSerializer.SerializeAsync(file,result,new JsonSerializerOptions{WriteIndented=true});
            Console.WriteLine(result.status+"; metadata saved to private report; 0 writes.");return 0;
        }
        catch(SqlException e){Console.Error.WriteLine("Read-only SQL probe failed; SQL number="+e.Number+". Check target credentials, TLS certificate and metadata permission privately; no retry or security bypass.");return 2;}
    }
}
