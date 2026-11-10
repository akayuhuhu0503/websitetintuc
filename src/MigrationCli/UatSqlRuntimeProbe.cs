using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.MigrationCli;

// Independent SQL-only probe for an allocated UAT target whose IIS/storage/scanner
// inputs may still be missing. It never validates those missing inputs as ready.
public static class UatSqlRuntimeProbe
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        string stage="CONFIG";string? report=null;
        try
        {
            string Arg(string key){var at=Array.IndexOf(args,key);return at>=0&&at+1<args.Length?args[at+1]:throw new InvalidDataException("Private home/config/new report required.");}
            var home=Path.GetFullPath(Arg("--home")).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            var file=Path.GetFullPath(Arg("--config"));var requestedReport=Path.GetFullPath(Arg("--report"));
            var application=Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!file.StartsWith(home,StringComparison.OrdinalIgnoreCase)||!requestedReport.StartsWith(home,StringComparison.OrdinalIgnoreCase)||
                home.StartsWith(application,StringComparison.OrdinalIgnoreCase)||application.StartsWith(home,StringComparison.OrdinalIgnoreCase)||File.Exists(requestedReport)||
                !Directory.Exists(Path.GetDirectoryName(requestedReport)))throw new InvalidDataException("Existing private home/config and new report outside application required.");
            SourceInventory.NoReparse(home);SourceInventory.NoReparse(file);SourceInventory.NoReparse(requestedReport);report=requestedReport;
            var environment=Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
            var aspEnvironment=Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if(environment!="Staging"||aspEnvironment!="Staging")throw new InvalidOperationException("SQL-only probe requires explicit Staging/UAT.");
            var c=new ConfigurationBuilder().AddJsonFile(file,false,false).AddInMemoryCollection(new Dictionary<string,string?>{
                ["DOTNET_ENVIRONMENT"]=environment,["ASPNETCORE_ENVIRONMENT"]=aspEnvironment}).Build();
            var sql=new SqlConnectionStringBuilder(DatabaseSettings.Connection(c));
            if(!UatDeployment.Enabled(c)||sql.DataSource!="45.120.229.43,1433"||sql.InitialCatalog!="website_tin_tuc"||
                c["Deployment:ExpectedSqlServer"]!=sql.DataSource||UatDeployment.PhysicalServer(c)!="WIN-S1IBJB84OOO"||
                sql.UserID!="website_tin_tuc_app"||!UatDeployment.AuthenticationMatches(c,sql)||c["Runtime:RolePassword"] is not {Length:>=24})
                throw new InvalidOperationException("Exact UAT SQL target and approval required.");
            stage="SQL_OPEN_AND_RUNTIME_ROLE";
            await using var db=new PortalDbContext(DatabaseSettings.Options(c));
            await db.Database.OpenConnectionAsync(); // The same runtime role interceptor used by Web/Worker.
            try
            {
                stage="READBACK";
                await using var command=db.Database.GetDbConnection().CreateCommand();command.CommandTimeout=20;
                command.CommandText="""
SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME(),ORIGINAL_LOGIN(),USER_NAME(),
 (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0),
 COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','ALTER'),0),
 COALESCE(HAS_PERMS_BY_NAME(N'dbo.AuditLogs',N'OBJECT',N'UPDATE'),0),
 (SELECT COUNT(*) FROM sys.columns WHERE object_id IN(OBJECT_ID(N'dbo.DraftMetadata'),OBJECT_ID(N'dbo.RevisionMetadata'))
 AND name=N'ConsultationEmail' AND system_type_id=231 AND max_length=-1 AND is_nullable=1),
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SiteBanner') AND is_nullable=0
 AND ((name=N'Description' AND system_type_id=231 AND max_length=1000) OR (name=N'Enabled' AND system_type_id=104 AND max_length=1)));
""";
                await using(var reader=await command.ExecuteReaderAsync())
                {
                    if(!await reader.ReadAsync()||reader.GetString(0)!="WIN-S1IBJB84OOO"||reader.GetString(1)!="website_tin_tuc"||
                        reader.GetString(2)!="website_tin_tuc_app"||reader.GetString(3)!="LandPortalRuntime"||reader.GetInt32(4)!=70||
                        reader.GetInt32(5)!=0||reader.GetInt32(6)!=0||reader.GetInt32(7)!=2||reader.GetInt32(8)!=2)
                        throw new InvalidOperationException("Runtime identity/schema/effective permission readback differs.");
                }
                command.CommandText="SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;";
                var actual=new List<string>();
                await using(var reader=await command.ExecuteReaderAsync())while(await reader.ReadAsync())actual.Add(reader.GetString(0));
                var expected=db.Database.GetMigrations().ToArray();
                if(expected.Length!=UatDeployment.RuntimeMigrationCount||expected[^1]!=UatDeployment.RuntimeLatestMigration||!actual.SequenceEqual(expected,StringComparer.Ordinal))throw new InvalidOperationException("Approved runtime migration history differs.");
                await using var output=new FileStream(report,FileMode.CreateNew);
                await JsonSerializer.SerializeAsync(output,new{
                    status="UAT_SQL_RUNTIME_PROBE_PASS",endpoint=sql.DataSource,physicalServer="WIN-S1IBJB84OOO",database=sql.InitialCatalog,
                    originalLogin="website_tin_tuc_app",runtimeRole="LandPortalRuntime",privateSecret="MATCH",tables=70,migrations=actual.Count,latestMigration=actual[^1],exactHistory=true,bannerColumns=true,
                    encryption="Mandatory",certificateTrust=sql.TrustServerCertificate?"EXPLICIT_APPROVED_UAT_EXCEPTION":"CA_VALIDATION",
                    approvalReference=c["Deployment:SqlTlsExceptionReference"],writes=0,provisioning=false,fullIisPreflight="NOT_RUN_MISSING_INFRASTRUCTURE",
                    utc=DateTimeOffset.UtcNow},new JsonSerializerOptions{WriteIndented=true});
                Console.WriteLine($"UAT_SQL_RUNTIME_PROBE_PASS; exact app/role/physical target/70tables/{actual.Count}migrations; 0 writes; IIS preflight NOT RUN.");
                return 0;
            }
            finally{await db.Database.CloseConnectionAsync();}
        }
        catch(Exception ex)
        {
            int? number=null;for(Exception? node=ex;node is not null;node=node.InnerException)if(node is SqlException error){number=error.Number;break;}
            if(report is not null&&!File.Exists(report))
            {
                await using var output=new FileStream(report,FileMode.CreateNew);
                await JsonSerializer.SerializeAsync(output,new{status="BLOCKED",stage,sqlNumber=number,errorType=ex.GetType().Name,writes=0,autoRetry=false});
            }
            Console.Error.WriteLine($"SQL-only runtime probe BLOCKED at {stage}; SQL number={number}; values omitted.");return 2;
        }
    }
}
