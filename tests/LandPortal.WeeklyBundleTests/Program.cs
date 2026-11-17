using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Xml.Linq;
using LandPortal.Infrastructure;
using LandPortal.WeeklyImport;
using Microsoft.EntityFrameworkCore;

if (args.Length != 3 || !OperatingSystem.IsWindows()) throw new ArgumentException("Windows: dotnet, source ZIP, new isolated test directory");
var dotnet = Path.GetFullPath(args[0]); var zip = Path.GetFullPath(args[1]); var run = Path.GetFullPath(args[2]);
if (Directory.Exists(run)) throw new Exception("Use a fresh fixture directory");
Directory.CreateDirectory(run);
var checks = new List<string>();
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); Console.WriteLine("PASS " + name); }
void Denied(Action action, string code, string name)
{
    try { action(); } catch (WeeklyIisException e) when (e.Code == code) { Check(true, name); return; }
    throw new Exception("Expected " + code + ": " + name);
}
var root = Path.Combine(run, "publish", "TTDL-IIS"); var web = Path.Combine(root, "web"); var runtime = Path.Combine(web, ".runtime");
var home = Path.Combine(root, "private"); var output = Path.Combine(run, "operator", "results");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
foreach (var dir in new[] { "private", "worker" }) Directory.CreateDirectory(Path.Combine(runtime, dir));
foreach (var dir in new[] { "media", "snapshots", "temp", "keys" }) Directory.CreateDirectory(Path.Combine(home, dir));
var binaryPaths = new[] { "Web.dll", "Infrastructure.dll", "web.config", ".runtime/worker/Worker.dll", ".runtime/worker/Infrastructure.dll" };
foreach (var p in binaryPaths) File.WriteAllText(Path.Combine(web, p), "isolated-release-no-executable");
var webConfig = XDocument.Parse("""
<configuration><system.webServer>
<security><requestFiltering><hiddenSegments><add segment=".runtime" /></hiddenSegments></requestFiltering></security>
<aspNetCore><environmentVariables>
<environmentVariable name="DOTNET_ENVIRONMENT" value="Staging" />
<environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Staging" />
</environmentVariables></aspNetCore></system.webServer></configuration>
""");
File.WriteAllText(Path.Combine(web, "web.config"), webConfig.ToString());
// Synthetic values only. No test in this executable connects to any SQL endpoint.
var values = new Dictionary<string, string?>
{
    ["Deployment:Profile"] = "UAT", ["Deployment:SqlTlsExceptionApproved"] = "true", ["Deployment:SqlTlsExceptionReference"] = "TEST-ONLY-FIXTURE",
    ["Deployment:ExpectedSqlServer"] = "45.120.229.43,1433", ["Deployment:ExpectedSqlServerIdentity"] = "WIN-S1IBJB84OOO",
    ["Deployment:SqlAuthentication"] = "SqlPassword", ["Deployment:ExpectedSqlLogin"] = "website_tin_tuc_app",
    ["Deployment:ExpectedHost"] = "gdla.gov.vn", ["Sql:ExpectedDatabase"] = "website_tin_tuc", ["Sql:CompatibilityLevel"] = "160",
    ["ConnectionStrings:Portal"] = "Server=45.120.229.43,1433;Database=website_tin_tuc;User ID=website_tin_tuc_app;Password=" + Guid.NewGuid().ToString("N") + ";Encrypt=True;TrustServerCertificate=True;Pooling=False",
    ["Runtime:RolePassword"] = Guid.NewGuid().ToString("N"), ["Media:AllowManagedDocumentsWithoutAv"] = "true",
    ["Public:BaseUrl"] = "", ["Storage:MediaRoot"] = "", ["Storage:TempRoot"] = "", ["Storage:KeysRoot"] = "", ["Storage:SnapshotRoot"] = ""
};
var configPath = Path.Combine(runtime, "private", "uat.json"); var manifestPath = Path.Combine(runtime, "manifest.json");
string Hash(string p) => WeeklyPackage.Hash(File.ReadAllBytes(p));
void Seal()
{
    File.WriteAllText(configPath, JsonSerializer.Serialize(values));
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { privateMode = "WebBundle", privateConfiguration = new { path = "private/uat.json", confidential = true, sha256 = Hash(configPath) },
        files = binaryPaths.Select(p => new { path = p, sha256 = Hash(Path.Combine(web, p)) }) }));
}
Seal();
string sites = "<appcmd><SITE SITE.NAME='WWW_GDLA' state='Started' bindings='https/*:443:gdla.gov.vn' /></appcmd>";
string Vdirs(params string[] roots) => new XDocument(new XElement("appcmd", roots.Select((r, i) => new XElement("VDIR", new XAttribute("VDIR.NAME", i == 0 ? "WWW_GDLA/" : "other/" + i + "/"), new XAttribute("physicalPath", r))))).ToString();
var vdirs = Vdirs(web);
var poolFile = Path.Combine(run, "test-only-pool.xml");
File.WriteAllText(poolFile, new XDocument(new XElement("configuration", new XElement("virtualDirectory", new XAttribute("physicalPath", web)))).ToString());
var oldPool = Environment.GetEnvironmentVariable("APP_POOL_CONFIG"); var oldPolicy = Environment.GetEnvironmentVariable("Media__AllowManagedDocumentsWithoutAv");
var oldConfig = Environment.GetEnvironmentVariable("LANDPORTAL_CONFIG");
var originalZipHash = Hash(zip);
Dictionary<string, string> Snapshot() => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, Hash);
try
{
    Environment.SetEnvironmentVariable("APP_POOL_CONFIG", poolFile); // Shared boundary validator's existing offline test seam.
    Environment.SetEnvironmentVariable("Media__AllowManagedDocumentsWithoutAv", "false");
    Environment.SetEnvironmentVariable("LANDPORTAL_CONFIG", Path.Combine(run, "must-not-read.json"));
    IisBundleRuntime Load() => IisBundleRuntime.LoadCore(web, () => (sites, vdirs));
    var before = Snapshot();
    using (var bundle = Load())
    {
        Check(bundle.Site == "WWW_GDLA" && bundle.Configuration["Public:BaseUrl"] == "https://gdla.gov.vn/", "Blank portable BaseUrl resolved from exact installed site HTTPS metadata");
        Check(bundle.Storage.MediaRoot == Path.Combine(home, "media") && bundle.Storage.TempRoot == Path.Combine(home, "temp"), "Blank storage resolves existing sibling private under publish");
        Check(MediaUploadOptions.FromConfiguration(bundle.Configuration, "Staging").ApprovedManagedDocumentsWithoutAv, "Bound policy wins over stale ambient false; no secret config overlay");
        Check(bundle.Configuration.GetSection("ConnectionStrings")["Portal"] == values["ConnectionStrings:Portal"] && bundle.Configuration["Runtime:RolePassword"] == values["Runtime:RolePassword"], "Secrets reused in memory unchanged");
        bundle.ValidateOperatorPaths(zip, output);
        Check(!Directory.Exists(output), "Operator path/access preflight creates no result folder or probe file");
        foreach (var path in new[] { configPath, manifestPath, Path.Combine(web, "Infrastructure.dll") })
        {
            try { using var changed = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); throw new Exception("Lock missing"); }
            catch (IOException) { Check(true, "Bound release read lock prevents mid-command modification: " + Path.GetFileName(path)); }
        }
        Denied(() => bundle.ValidateOperatorPaths(zip, Path.Combine(web, "output")), "OPERATOR_LOCATION", "No results below IIS webroot");
        Denied(() => bundle.ValidateOperatorPaths(Path.Combine(home, "source.zip"), output), "OPERATOR_LOCATION", "No operator source/results mixed into live data home");
    }
    Check(before.OrderBy(x => x.Key).SequenceEqual(Snapshot().OrderBy(x => x.Key)), "Full read-only bundle load preserves every fixture byte");
    Denied(() => IisBundleRuntime.LoadCore(web, () => throw new WeeklyIisException("IIS_METADATA", "denied")), "IIS_METADATA", "Missing IIS metadata permission distinguished from hash/storage failure");
    File.AppendAllText(configPath, " ");
    var metadataCalled = false;
    Denied(() => IisBundleRuntime.LoadCore(web, () => { metadataCalled = true; return (sites, vdirs); }), "BUNDLE_HASH", "Tampered private JSON rejected before reading IIS metadata");
    Check(!metadataCalled, "Private hash is an initial gate before other resources");
    // Real CLI process: bad bundle must fail without output creation or SQL/authentication.
    var processInfo = new ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in new[] { typeof(WeeklyPackage).Assembly.Location, "preflight", "--iis-web", web, "--zip", zip, "--out", output }) processInfo.ArgumentList.Add(a);
    using (var process = Process.Start(processInfo)!)
    {
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000)) { process.Kill(true); throw new Exception("CLI did not fail early"); }
        var message = (await stdout) + (await stderr);
        Check(process.ExitCode == 1 && message.Contains("BUNDLE_HASH") && !Directory.Exists(output), "CLI entrypoint hash failure: no SQL prompt, no outputs, nonzero exit");
        Check(!message.Contains(values["Runtime:RolePassword"]!) && !message.Contains(values["ConnectionStrings:Portal"]!), "Failure diagnostics contain no secret");
    }
    Seal();
    File.AppendAllText(Path.Combine(web, "Infrastructure.dll"), "mixed");
    Denied(() => Load(), "BUNDLE_HASH", "Mixed critical binary refused");
    File.WriteAllText(Path.Combine(web, "Infrastructure.dll"), "isolated-release-no-executable"); Seal();
    var configBackup = File.ReadAllBytes(configPath); File.Delete(configPath);
    Denied(() => Load(), "BUNDLE_READ", "Missing private snapshot refused without fallback to another package");
    File.WriteAllBytes(configPath, configBackup);
    sites = sites.Replace("Started", "Stopped"); Denied(() => Load(), "IIS_SITE", "Stopped/wrong IIS site refused"); sites = sites.Replace("Stopped", "Started");
    vdirs = Vdirs(Path.Combine(run, "other-web")); Denied(() => Load(), "IIS_SITE", "Copied package not installed as IIS root refused");
    vdirs = Vdirs(web, home); Denied(() => Load(), "IIS_STORAGE_BOUNDARY", "A second IIS site exposing private storage refused");
    vdirs = Vdirs(web, runtime); Denied(() => Load(), "IIS_STORAGE_BOUNDARY", "Independent runtime virtual directory refused"); vdirs = Vdirs(web);
    sites = sites.Replace("gdla.gov.vn", "wrong.example"); Denied(() => Load(), "IIS_HTTPS", "Wrong HTTPS host refused"); sites = sites.Replace("wrong.example", "gdla.gov.vn");
    values["Public:BaseUrl"] = "https://wrong.example/"; Seal(); Denied(() => Load(), "IIS_HTTPS", "Nonempty private origin must match actual binding");
    values["Public:BaseUrl"] = "https://gdla.gov.vn/"; Seal(); using (var b = Load()) Check(b.Configuration["Public:BaseUrl"] == values["Public:BaseUrl"], "Configured HTTPS origin retained unchanged");
    values["Public:BaseUrl"] = ""; values["Deployment:Profile"] = "Production"; Seal(); Denied(() => Load(), "BUNDLE_ENVIRONMENT", "Production is never converted to UAT");
    values["Deployment:Profile"] = "UAT"; values["Storage:MediaRoot"] = Path.Combine(web, "wwwroot", "media"); Seal(); Denied(() => Load(), "BUNDLE_CONFIG", "Inside-web media storage refused");
    values["Storage:MediaRoot"] = ""; Seal();
    var media = new DirectoryInfo(Path.Combine(home, "media")); var originalAcl = media.GetAccessControl();
    try
    {
        var restricted = media.GetAccessControl(); restricted.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory | FileSystemRights.WriteData, AccessControlType.Deny));
        media.SetAccessControl(restricted);
        Denied(() => Load(), "STORAGE_ACCESS", "Actual Windows directory access denial reported without ACL repair or SQL");
    }
    finally { media.SetAccessControl(originalAcl); }
    // Readback fact validation uses production code; no connection to shared SQL in this fixture.
    var migrations = new PortalDbContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PortalDbContext>().UseSqlServer("Server=(localdb)\\never-open;Database=unused;Integrated Security=true").Options).Database.GetMigrations().ToArray();
    var identity = new TargetIdentity("WIN-S1IBJB84OOO", "website_tin_tuc", "LandPortalRuntime", migrations);
    IisBundleRuntime.VerifySqlFacts(identity, "website_tin_tuc_app", "LandPortalRuntime", 70); Check(true, "Exact physical target/app login/runtime role/schema 18 facts accepted");
    Denied(() => IisBundleRuntime.VerifySqlFacts(identity with { Database = "other" }, "website_tin_tuc_app", "LandPortalRuntime", 70), "SQL_IDENTITY", "Wrong database readback denied");
    Denied(() => IisBundleRuntime.VerifySqlFacts(identity, "sa", "LandPortalRuntime", 70), "SQL_IDENTITY", "DBA identity is not runtime login");
    Denied(() => IisBundleRuntime.VerifySqlFacts(identity, "website_tin_tuc_app", "dbo", 70), "SQL_IDENTITY", "Inactive/wrong application role denied");
    Denied(() => IisBundleRuntime.VerifySqlFacts(identity with { Migrations = migrations[..^1] }, "website_tin_tuc_app", "LandPortalRuntime", 70), "SQL_IDENTITY", "Wrong migration count denied");
    Check(Hash(zip) == originalZipHash && !Directory.Exists(output), "Original ZIP and operator outputs remain untouched throughout preflight tests");
    await File.WriteAllTextAsync(Path.Combine(run, "result.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, sqlConnections = 0, operationalIisTouched = false, scope = "Windows filesystem plus isolated IIS metadata; not live IIS acceptance" }, WeeklyPackage.Json));
    Console.WriteLine($"TOTAL {checks.Count} PASS; SQL connections=0; live IIS untouched");
}
finally
{
    Environment.SetEnvironmentVariable("APP_POOL_CONFIG", oldPool); Environment.SetEnvironmentVariable("Media__AllowManagedDocumentsWithoutAv", oldPolicy); Environment.SetEnvironmentVariable("LANDPORTAL_CONFIG", oldConfig);
}
