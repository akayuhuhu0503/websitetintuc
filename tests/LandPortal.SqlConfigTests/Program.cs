using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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
Console.WriteLine("6 focused offline groups PASS; SQL connections/migrations/import/full regression NOT RUN.");
