using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Text.RegularExpressions;
using System.Text.Json;

if(args.Contains("--migration-207-prefix-export"))
{
    var output=Path.GetFullPath(args[Array.IndexOf(args,"--output")+1]);
    var allowed=Path.GetFullPath(Path.Combine(PortalConfiguration.FindWorkspace(),".local"))+Path.DirectorySeparatorChar;
    if(!output.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||File.Exists(output))throw new InvalidDataException("New private offline model artifact required");
    await using var offline=new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer("Server=127.0.0.1;Database=LandPortal_Offline207Only;Integrated Security=true;Encrypt=true").Options);
    var assembly=offline.GetService<IMigrationsAssembly>();
    var migration=assembly.CreateMigration(assembly.Migrations["20261012083717_CmsRevisionWorkflow"],offline.Database.ProviderName!);
    var model=offline.GetService<IModelRuntimeInitializer>().Initialize(migration.TargetModel,designTime:true);
    var tables=model.GetRelationalModel().Tables.Select(t=>new{
        schema=t.Schema??"dbo",name=t.Name,
        columns=t.Columns.Select(c=>new{name=c.Name,type=c.StoreType,nullable=c.IsNullable}),
        primaryKey=t.PrimaryKey?.Columns.Select(c=>c.Name),
        indexes=t.Indexes.Select(i=>new{name=i.Name,unique=i.IsUnique,columns=i.Columns.Select(c=>c.Name)}),
        foreignKeys=t.ForeignKeyConstraints.Select(f=>new{name=f.Name,columns=f.Columns.Select(c=>c.Name),target=f.PrincipalTable.Name,targetColumns=f.PrincipalColumns.Select(c=>c.Name)})
    });
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    await File.WriteAllTextAsync(output,JsonSerializer.Serialize(tables,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("Offline migration2 relational model exported; no SQL connection opened.");return;
}

if(args.Contains("--migration-207-only"))
{
    // Offline only: inspect the actual migration operations and provider SQL.
    // No SQL connection, DDL execution, schema retry or unrelated policy suite.
    var migration=new Infrastructure.Migrations.PortalAdministration();
    var column=migration.UpOperations.OfType<AddColumnOperation>().Single(x=>x.Table=="Contents"&&x.Name=="ApprovedByUserId");
    if(column.ColumnType!="nvarchar(450)"||column.MaxLength!=450||!column.IsNullable)throw new Exception("FAIL migration207: column contract changed");
    Console.WriteLine("PASS migration207 column contract unchanged");
    var raw=migration.UpOperations.OfType<SqlOperation>().ToArray();
    var update=raw.Single(x=>x.Sql.Contains("UPDATE c SET ApprovedByUserId="));
    var match=Regex.Match(update.Sql.Trim(),@"\AEXEC\(N'(?<body>(?:[^']|'')*)'\);\z",RegexOptions.Singleline);
    if(!match.Success||update.SuppressTransaction)throw new Exception("FAIL migration207: deferred compilation/transaction guard");
    var body=match.Groups["body"].Value.Replace("''", "'").Trim();
    const string original="UPDATE c SET ApprovedByUserId=(SELECT TOP(1) a.Actor FROM dbo.AuditLogs a JOIN dbo.AspNetUsers u ON u.Id=a.Actor WHERE a.ContentId=c.Id AND a.RevisionId=c.ApprovedRevisionId AND a.Action=N'Approve' ORDER BY a.CreatedUtc DESC,a.Id DESC) FROM dbo.Contents c WHERE c.ApprovedRevisionId IS NOT NULL;";
    if(Regex.Replace(body,@"\s+"," ")!=original)throw new Exception("FAIL migration207: backfill semantics changed");
    Console.WriteLine("PASS migration207 deferred UPDATE preserves exact backfill and transaction");
    if(!raw.Any(x=>x.Sql.Contains("INSERT dbo.RevisionMetadata")&&x.Sql.Contains("INSERT dbo.DraftMetadata")))throw new Exception("FAIL migration207: provenance insert lost");
    Console.WriteLine("PASS migration207 provenance INSERTs preserved");
    await using var offline=new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer("Server=127.0.0.1;Database=LandPortal_Offline207Only;Integrated Security=true;Encrypt=true").Options);
    var sql=offline.GetService<IMigrator>().GenerateScript("20261012083717_CmsRevisionWorkflow","20261013030300_PortalAdministration",MigrationsSqlGenerationOptions.Idempotent);
    var compileSurface=Regex.Replace(sql,@"N?'(?:[^']|'')*'","''",RegexOptions.Singleline);
    if(compileSurface.Contains("UPDATE c SET ApprovedByUserId=")||!sql.Contains("EXEC(N'")||!sql.Contains("BEGIN TRANSACTION")||!sql.Contains("COMMIT;")||!sql.Contains("INSERT INTO [__EFMigrationsHistory]"))throw new Exception("FAIL migration207 generated SQL compile surface/history/transaction");
    if(offline.Database.GetDbConnection().State!=System.Data.ConnectionState.Closed)throw new Exception("FAIL migration207 unexpectedly opened SQL");
    Console.WriteLine("PASS migration207 provider-generated SQL defers column binding; history/transaction intact; connection CLOSED");
    Console.WriteLine("4 focused migration207 checks PASS; engine DDL/Apply/full regression NOT RUN.");
    return;
}

if(args.Contains("--uat-tls-only"))
{
    var tlsHome=Path.Combine(Path.GetTempPath(),"landportal-uat-tls-offline-fixture");
    const string tlsConnection="Server=45.120.229.43,1433;Database=website_tin_tuc;User ID=website_tin_tuc_app;Password=technical-fixture-only;Encrypt=True;TrustServerCertificate=True;Persist Security Info=False";
    var values=new Dictionary<string,string?>{
        ["Deployment:Profile"]="UAT",["Deployment:TargetApproved"]="true",["Deployment:TargetApprovalReference"]="TECHNICAL FIXTURE",
        ["Deployment:SqlTlsExceptionApproved"]="true",["Deployment:SqlTlsExceptionReference"]="OFFLINE POLICY FIXTURE",
        ["Deployment:ExpectedSqlServer"]="45.120.229.43,1433",["Deployment:ExpectedSqlServerIdentity"]="WIN-S1IBJB84OOO",
        ["Deployment:SqlAuthentication"]="SqlPassword",["Deployment:ExpectedSqlLogin"]="website_tin_tuc_app",
        ["Deployment:ExpectedHost"]="portal.invalid",["Public:BaseUrl"]="https://portal.invalid",
        ["DOTNET_ENVIRONMENT"]="Staging",["ASPNETCORE_ENVIRONMENT"]="Staging",["WorkspaceRoot"]=tlsHome,
        ["Sql:ExpectedDatabase"]="website_tin_tuc",["ConnectionStrings:Portal"]=tlsConnection,
        ["Runtime:RolePassword"]=new string('x',32),["Scanner:Provider"]="Clamd",["Scanner:ClamdPort"]="3310",["Scanner:PolicyReference"]="TECHNICAL FIXTURE"
    };
    foreach(var key in new[]{"MediaRoot","SnapshotRoot","TempRoot","KeysRoot"})values["Storage:"+key]=Path.Combine(tlsHome,key);
    IConfiguration TlsConfig(params (string Key,string? Value)[] changes)=>new ConfigurationBuilder().AddInMemoryCollection(values).AddInMemoryCollection(changes.ToDictionary(x=>x.Key,x=>x.Value)).Build();
    var count=0;
    void TlsCheck(bool pass,string name){if(!pass)throw new Exception("FAIL "+name);count++;Console.WriteLine("PASS "+name);}
    bool TlsDenied(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    TlsCheck(UatDeployment.Validate(TlsConfig(),"Staging"),"Explicit exact-target UAT certificate exception accepted");
    TlsCheck(UatDeployment.TemporaryRuntimeIdentityMatches("WIN-S1IBJB84OOO","website_tin_tuc","website_tin_tuc_app","LandPortalRuntime",70,17,"20261102042625_DraftConsultationEmail"),"Actual SQL runtime identity contract accepted");
    for(var field=0;field<7;field++)TlsCheck(!UatDeployment.TemporaryRuntimeIdentityMatches(field==0?"OTHER":"WIN-S1IBJB84OOO",field==1?"OTHER":"website_tin_tuc",field==2?"sa":"website_tin_tuc_app",field==3?"dbo":"LandPortalRuntime",field==4?71:70,field==5?18:17,field==6?"OTHER":"20261102042625_DraftConsultationEmail"),"Actual runtime identity field "+field+" mismatch denied");
    var denyCases=new (string Key,string? Value)[]{
        ("Deployment:SqlTlsExceptionApproved","false"),("Deployment:SqlTlsExceptionReference",null),
        ("Deployment:SqlTlsExceptionReference","invalid\nref"),("Deployment:ExpectedSqlServer","other.invalid,1433"),
        ("Deployment:SqlTlsExceptionReference","     "),
        ("Deployment:ExpectedSqlServerIdentity","OTHER-SQL"),("Deployment:ExpectedSqlLogin","sa"),
        ("Deployment:SqlAuthentication","Windows"),("DOTNET_ENVIRONMENT","Production"),("ASPNETCORE_ENVIRONMENT","Production"),
        ("ConnectionStrings:Portal",tlsConnection.Replace("Encrypt=True","Encrypt=False")),
        ("ConnectionStrings:Portal",tlsConnection.Replace("website_tin_tuc;","other_db;")),
        ("ConnectionStrings:Portal",tlsConnection.Replace("45.120.229.43","45.120.229.44"))
    };
    foreach(var item in denyCases)TlsCheck(TlsDenied(()=>UatDeployment.Validate(TlsConfig(item),"Staging")),"Exception denied for "+item.Key+" variation");
    TlsCheck(TlsDenied(()=>UatDeployment.Validate(TlsConfig(),"Production")),"Production runtime argument refused");
    TlsCheck(TlsDenied(()=>DatabaseSettings.Connection(TlsConfig(("Deployment:Profile","Production"),("DOTNET_ENVIRONMENT","Production"),("ASPNETCORE_ENVIRONMENT","Production")))),"Production cannot reuse exception with another profile");
    TlsCheck(TlsDenied(()=>DatabaseSettings.Connection(TlsConfig(("Deployment:Profile",""),("Deployment:SqlTlsExceptionApproved","false"),("DOTNET_ENVIRONMENT","production"),("ASPNETCORE_ENVIRONMENT","production")))),"Case-insensitive Production cannot reuse legacy certificate trust");
    TlsCheck(TlsDenied(()=>DatabaseSettings.Connection(TlsConfig(("Deployment:Profile","Production"),("Deployment:SqlTlsExceptionApproved","false"),("DOTNET_ENVIRONMENT","Development"),("ASPNETCORE_ENVIRONMENT","Development")))),"Production profile cannot borrow Development transport settings");
    TlsCheck(TlsDenied(()=>DatabaseSettings.Connection(TlsConfig(("Deployment:Profile",""),("Deployment:SqlTlsExceptionApproved","false")))),"Unidentified Staging profile cannot opt into certificate trust");
    TlsCheck(UatDeployment.Validate(TlsConfig(("ConnectionStrings:Portal",tlsConnection.Replace("TrustServerCertificate=True","TrustServerCertificate=False")),("Deployment:SqlTlsExceptionApproved","false")),"Staging"),"CA validation remains default without exception");
    var incomplete=TlsConfig(("Deployment:TargetApproved","false"),("Public:BaseUrl",null));
    TlsCheck(TlsDenied(()=>UatDeployment.Validate(incomplete,"Staging")),"SQL exception does not bypass IIS target and HTTPS requirements");
    await using(var offlineTls=new PortalDbContext(DatabaseSettings.Options(incomplete)))
        TlsCheck(offlineTls.Database.GetDbConnection().State==System.Data.ConnectionState.Closed&&!new SqlConnectionStringBuilder(offlineTls.Database.GetConnectionString()).Pooling,"Independent SQL settings preserve runtime role/nonpooled session; no connection opened");
    var localConfig=TlsConfig(("Deployment:Profile",""),("Deployment:SqlTlsExceptionApproved","false"),("DOTNET_ENVIRONMENT","Development"),("ASPNETCORE_ENVIRONMENT","Development"),("Sql:ExpectedDatabase","LandPortal_Dev"),("ConnectionStrings:Portal",@"Server=.\SQLEXPRESS;Database=LandPortal_Dev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"));
    TlsCheck(new SqlConnectionStringBuilder(DatabaseSettings.Connection(localConfig)).IntegratedSecurity,"Existing local Development SQL policy preserved");
    Console.WriteLine(count+" focused UAT TLS offline checks PASS; 0 SQL connections; full regression NOT RUN.");return;
}

// Offline technical fixtures only. No connection is opened; no real credentials.
var home=Path.Combine(Path.GetTempPath(),"landportal-sql-policy-fixture");
var baseValues=new Dictionary<string,string?>{
 ["Deployment:Profile"]="UAT",["Deployment:TargetApproved"]="true",["Deployment:TargetApprovalReference"]="TECHNICAL FIXTURE ONLY",
 ["Deployment:ExpectedSqlServer"]="fixture.invalid,1433",["Deployment:ExpectedHost"]="portal.invalid",
 ["DOTNET_ENVIRONMENT"]="Staging",["ASPNETCORE_ENVIRONMENT"]="Staging",["Sql:ExpectedDatabase"]="Portal_UatFixture",
 ["Public:BaseUrl"]="https://portal.invalid",["WorkspaceRoot"]=home,["Scanner:Provider"]="Clamd",["Scanner:ClamdPort"]="3310",
 ["Scanner:PolicyReference"]="Technical fixture",["Runtime:RolePassword"]=new string('x',32),
 ["ConnectionStrings:Portal"]="Server=fixture.invalid,1433;Database=Portal_UatFixture;Integrated Security=true;Encrypt=true;TrustServerCertificate=false"
};
foreach(var name in new[]{"MediaRoot","SnapshotRoot","TempRoot","KeysRoot"})baseValues["Storage:"+name]=Path.Combine(home,name);
IConfiguration Config(params (string Key,string? Value)[] changes)=>new ConfigurationBuilder().AddInMemoryCollection(baseValues).AddInMemoryCollection(changes.ToDictionary(x=>x.Key,x=>x.Value)).Build();
void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);}
bool Denied(Action action){try{action();return false;}catch(InvalidOperationException){return true;}catch(InvalidDataException){return true;}}
Check(UatDeployment.Validate(Config(),"Staging"),"Windows authentication backward compatibility");
const string connection="Server=fixture.invalid,1433;Database=Portal_UatFixture;User ID=fixture_app;Password=technical-fixture-only;Encrypt=true;TrustServerCertificate=false";
baseValues["Deployment:SqlAuthentication"]="SqlPassword";baseValues["Deployment:ExpectedSqlLogin"]="fixture_app";baseValues["Deployment:ExpectedSqlServerIdentity"]="FIXTURE-SQL\\INSTANCE";baseValues["ConnectionStrings:Portal"]=connection;
Check(UatDeployment.Validate(Config(),"Staging")&&UatDeployment.PhysicalServer(Config())=="FIXTURE-SQL\\INSTANCE","SQL authentication with separate endpoint/physical identity");
var negatives=new (string,string?)[]{("Deployment:SqlAuthentication","Windows"),("Deployment:ExpectedSqlLogin","other_app"),("Deployment:ExpectedSqlLogin","sa"),("Deployment:ExpectedSqlServerIdentity",null),("Deployment:ExpectedSqlServer","other.invalid"),("Deployment:TargetApproved","false"),("DOTNET_ENVIRONMENT","Production"),("Sql:ExpectedDatabase","LandPortal_Dev"),("ConnectionStrings:Portal",connection.Replace("technical-fixture-only","")),("ConnectionStrings:Portal",connection+";Persist Security Info=true"),("ConnectionStrings:Portal",connection.Replace("TrustServerCertificate=false","TrustServerCertificate=true")),("ConnectionStrings:Portal",connection.Replace("Encrypt=true","Encrypt=false"))};
Check(negatives.All(x=>Denied(()=>UatDeployment.Validate(Config(x),"Staging"))),"12 unsafe authentication/target/TLS variations fail closed");
await using(var db=new PortalDbContext(DatabaseSettings.Options(Config())))Check(!new SqlConnectionStringBuilder(db.Database.GetConnectionString()).Pooling,"Runtime still uses non-pooled application-role sessions; no SQL opened");
var header=UatSqlScripts.Header("Portal_UatFixture","DBA'S-SQL");
var role=UatSqlScripts.RuntimeRole(header,"fixture_app");
Check(header.StartsWith(":ON ERROR EXIT")&&header.Contains("N'DBA''S-SQL'")&&role.Contains("SESSION_CONTEXT")&&!role.Contains("CREATE LOGIN")&&!role.Contains("CREATE DATABASE")&&Denied(()=>UatSqlScripts.Header("master","safe"))&&Denied(()=>UatSqlScripts.RuntimeRole(header,"sa"))&&Denied(()=>UatSqlScripts.RuntimeRole(header,"user];DROP")),"Schema/role artifacts guarded and secret-free; reserved/injected principals refused");
Check(Denied(()=>DatabaseSettings.Connection(Config(("ConnectionStrings:Portal","secret-fragment-no-equals")))) ,"Malformed private connection string rejected without value disclosure");
const string fixtureIndex="EXEC(N'CREATE UNIQUE INDEX [fixture] ON [FixtureTable] ([Name]) WHERE [Name] IS NOT NULL');\nGO\n";
var unbound=UatSqlScripts.UnboundSchema(fixtureIndex);var bound=UatSqlScripts.BoundSchema("Portal_UatFixture","FIXTURE-SQL",fixtureIndex);
var expectedSettings=new[]{"QUOTED_IDENTIFIER ON","ANSI_NULLS ON","ANSI_PADDING ON","ANSI_WARNINGS ON","ARITHABORT ON","CONCAT_NULL_YIELDS_NULL ON","NUMERIC_ROUNDABORT OFF","XACT_ABORT ON"};
Check(new[]{unbound,bound,role}.All(script=>expectedSettings.All(option=>script.Contains("SET "+option+";")))&&bound.IndexOf("SET QUOTED_IDENTIFIER ON;",StringComparison.Ordinal)<bound.IndexOf("CREATE UNIQUE INDEX",StringComparison.Ordinal)&&bound.Contains("SET XACT_ABORT ON;\nGO")&&unbound.IndexOf("-- LANDPORTAL_SCHEMA_BODY",StringComparison.Ordinal)<unbound.IndexOf("SET QUOTED_IDENTIFIER ON",StringComparison.Ordinal),"Generated bound/unbound/role scripts establish SET options before GO and nested filtered-index batches");
Console.WriteLine("7 focused offline groups PASS; SQL connections/migrations/import/full regression NOT RUN.");
