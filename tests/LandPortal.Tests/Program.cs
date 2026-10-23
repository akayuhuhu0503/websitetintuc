using System.Text;
using System.Text.Json;
using System.Data;
using LandPortal.MigrationCli;
using LandPortal.Tests;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// An executable risk-focused suite. It refuses to connect to any database except
// LandPortal_Test; fixtures never become official-looking public production data.
var root = PortalConfiguration.FindWorkspace();
if(args.Contains("--nuke-cache-only"))
{
    await NukeRealTests.RunAsync(root);Console.WriteLine("PASS real cached NukeViet parsing/normalized private drafts; SQL/network NOT RUN");return 0;
}
if(args.Contains("--uat-offline-only"))
{
    var checks=new List<object>();var errors=0;
    foreach(var risk in DeliveryTests.Cases(root,new ConfigurationBuilder().Build()).Where(x=>x.Name.StartsWith("Delivery isolated UAT")||x.Name.StartsWith("Delivery UAT allocated")||x.Name.StartsWith("Delivery SMTP explicit UAT")))
    {try{await risk.Test();checks.Add(new{risk.Name,pass=true});Console.WriteLine("PASS "+risk.Name);}catch(Exception e){errors++;checks.Add(new{risk.Name,pass=false,error=e.GetType().Name});Console.WriteLine("FAIL "+risk.Name);}}
    var reportAt=Array.IndexOf(args,"--report");if(reportAt<0||reportAt+1>=args.Length)throw new InvalidOperationException("--report required");
    var path=Path.GetFullPath(args[reportAt+1],root);var allowed=Path.Combine(root,".local","evidence","phase3f")+Path.DirectorySeparatorChar;
    if(!path.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||File.Exists(path))throw new InvalidOperationException("New private report required");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{checks,failures=errors,sqlConnections=0,networkRequests=0,fullRegression="NOT RUN"}));return errors==0?0:1;
}
var config = PortalConfiguration.Load(root);
var connection = DatabaseSettings.Connection(config);
if (new SqlConnectionStringBuilder(connection).InitialCatalog != "LandPortal_Test")
    throw new InvalidOperationException("Integration tests require exactly LandPortal_Test.");
var options = DatabaseSettings.Options(config, forMigration: true);
var tests = new List<object>();
var failed = 0;
if(args.Contains("--srs12-only"))
{
    var at=Array.IndexOf(args,"--test-backup-receipt");if(at<0||at+1>=args.Length)throw new InvalidOperationException("Verified Test backup required.");
    await using(var backup=new PortalDbContext(options))await ApprovedBatchExecution.VerifyBackupAsync(backup,root,"LandPortal_Test",args[at+1]);
    await Run("SRS1.2 exact role boundaries, menu workflow and private CAPTCHA lookup actual SQL/HTTP",()=>Srs12Tests.Http(root,config,options));
    var folder=TestEvidencePaths.At(root);Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,"srs12-tests.json"),JsonSerializer.Serialize(new{tests,failures=failed,fullRegression="NOT RUN"}));return failed==0?0:1;
}
if(args.Contains("--uat-delivery-only")||args.Contains("--uat-template-only")||args.Contains("--uat-slider-only")||args.Contains("--uat-traffic-only"))
{
    var at=Array.IndexOf(args,"--test-backup-receipt");if(at<0||at+1>=args.Length)throw new InvalidOperationException("Verified Test backup required.");
    await using(var backup=new PortalDbContext(options))await ApprovedBatchExecution.VerifyBackupAsync(backup,root,"LandPortal_Test",args[at+1]);
    var folder=TestEvidencePaths.At(root);Directory.CreateDirectory(folder);
    if(args.Contains("--uat-delivery-only")||args.Contains("--uat-template-only"))await Run("SRS1.1 template required fields revision tags featured and source preservation boundary",()=>UatDeliveryTests.Template(options,config));
    if(args.Contains("--uat-slider-only"))await Run("SRS1.1 actual Test desktop mobile slider and Vietnam clock",()=>UatDeliveryTests.SliderHttp(root,config,options));
    if(args.Contains("--uat-traffic-only"))await Run("SRS1.1 SQL anonymous sessions concurrent dedup durable totals and Vietnam calendar",()=>UatDeliveryTests.TrafficSql(options));
    if(args.Contains("--uat-delivery-only"))
    {
        await Run("UAT native scanner Test Production and unallocated target boundaries",()=>UatDeliveryTests.ScannerBoundaries(root,config));
        await Run("Real Defender PDF Clean EICAR failure plus HTTP inline counters and private ACL",()=>UatDeliveryTests.PdfHttp(root,config,options));
    }
    await File.WriteAllTextAsync(Path.Combine(folder,"focused-tests.json"),JsonSerializer.Serialize(new{tests,failures=failed,database="LandPortal_Test",fullRegression="NOT_RUN",devBusinessWrites=0},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;
}
if(args.Contains("--ba-feedback-only"))
{
    var at=Array.IndexOf(args,"--test-backup-receipt");if(at<0||at+1>=args.Length)throw new InvalidOperationException("Verified Test backup required.");
    await using(var backup=new PortalDbContext(options))await ApprovedBatchExecution.VerifyBackupAsync(backup,root,"LandPortal_Test",args[at+1]);
    await Run("BA Vietnamese date parsing and centralized permission/state labels",BaFeedbackTests.DatesAndLabels);
    await Run("BA exact real week43 Word cells/provenance and 7/8-column safety compatibility",()=>BaFeedbackTests.RealWord(root));
    foreach(var risk in V05FinishTests.Cases(root,options,config).Where(x=>x.Name.StartsWith("V05-Finish SQL FR17 menu")))await Run(risk.Name,risk.Test);
    await Run("BA catalog activation rechecks current endpoint permissions",async()=>
    {
        await using var db=new PortalDbContext(options);var id=await InquiryFinishTests.User(db,Permissions.All,"BA permission revocation QA");var actor=await new CurrentPermissionService(db).GetAsync(id);
        await db.UserClaims.Where(x=>x.UserId==id&&x.ClaimType=="permission"&&x.ClaimValue==Permissions.PublishSite).ExecuteDeleteAsync();
        var site=new SiteConfigurationService(db,StorageOptions.FromConfiguration(config),MediaUploadOptions.FromConfiguration(config,"Development"));var state=(await site.ReadAsync(false))!;
        await V05FinishTests.Denied<SiteConfigurationException>(()=>site.ConfirmDisclosureCatalogAsync(actor,state.Header.RowVersion));
    });
    await Run("BA actual HTTP SQL CAPTCHA privacy plus desktop/mobile forms and admin labels",()=>BaFeedbackTests.Http(root,config,options));
    var folder=TestEvidencePaths.At(root);Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,"ba-tests.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,tests,failures=failed,database="LandPortal_Test",devWrites=0,fullRegression="NOT RUN"},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;
}
if(args.Contains("--p0-ready-only"))
{
    var at=Array.IndexOf(args,"--test-backup-receipt");if(at<0||at+1>=args.Length)throw new InvalidOperationException("Fresh Test backup required.");
    await using(var backup=new PortalDbContext(options))await ApprovedBatchExecution.VerifyBackupAsync(backup,root,"LandPortal_Test",args[at+1]);
    await Run("P0 homepage BR03 empty/failed module privacy and global audit policy",P0ReadyTests.HomeAndAuditPolicy);
    await Run("P0 initial UAT administrator matches Web password policy and rejects missing/weak credentials before writes",()=>P0ReadyTests.BootstrapPolicy(options));
    await Run("P0 Test actual CAPTCHA/Identity SQL inbox audit plus native scanned Word release history and revocation",()=>V05FinishHttpTests.RunAsync(root,config,options,captureBrowser:false,verifyP0:true));
    var folder=TestEvidencePaths.At(root);Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,"p0-tests.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,tests,failures=failed,database="LandPortal_Test",fullRegression="NOT_RUN",screenshots="NOT_RUN",devWrites=0},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;
}
if(args.Contains("--nuke-real-only"))
{
    await Run("Real NukeViet root module 22-Oct snapshots exact SHA headline metadata canonical negatives",()=>NukeRealTests.RunAsync(root));
    foreach(var risk in Phase3DTests.Cases(root,options,config).Where(x=>x.Name.StartsWith("3D NukeViet namespace")))await Run(risk.Name,risk.Test);
    var nukeDirectory=TestEvidencePaths.At(root);Directory.CreateDirectory(nukeDirectory);await File.WriteAllTextAsync(Path.Combine(nukeDirectory,"nuke-tests.json"),JsonSerializer.Serialize(new{tests,failures=failed,sqlWrites=0,networkRequests=0,fullRegression="NOT RUN"}));return failed==0?0:1;
}
if(args.Contains("--delivery-video-only"))
{
    var at=Array.IndexOf(args,"--test-backup-receipt");if(at<0||at+1>=args.Length)throw new InvalidOperationException("Verified Test backup required");
    await using(var backup=new PortalDbContext(options))await ApprovedBatchExecution.VerifyBackupAsync(backup,root,"LandPortal_Test",args[at+1]);
    foreach(var risk in VideoTests.Cases(root,options,config).Where(x=>x.Name.StartsWith("2D genuine Defender video")||x.Name.StartsWith("2D native FFmpeg")))await Run(risk.Name,risk.Test);
    var folder=TestEvidencePaths.At(root);Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,"video-tests.json"),JsonSerializer.Serialize(new{tests,failures=failed,fullRegression="NOT RUN"}));return failed==0?0:1;
}
if(args.Contains("--delivery-f4-only"))
{
    var at=Array.IndexOf(args,"--test-backup-receipt");if(at<0||at+1>=args.Length)throw new InvalidOperationException("Verified Test backup required");
    await using(var backup=new PortalDbContext(options))await ApprovedBatchExecution.VerifyBackupAsync(backup,root,"LandPortal_Test",args[at+1]);
    foreach(var risk in InquiryFinishTests.Cases(root,options,config).Where(x=>x.Name.StartsWith("Finish F4")))await Run(risk.Name,risk.Test);
    var folder=TestEvidencePaths.At(root);Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,"outbox-tests.json"),JsonSerializer.Serialize(new{tests,failures=failed,fullRegression="NOT RUN"}));return failed==0?0:1;
}
if(args.Contains("--delivery-only"))
{
    var at=Array.IndexOf(args,"--test-backup-receipt");if(at<0||at+1>=args.Length)throw new InvalidOperationException("Fresh verified Test backup required");
    await using(var backup=new PortalDbContext(options))await ApprovedBatchExecution.VerifyBackupAsync(backup,root,"LandPortal_Test",args[at+1]);
    foreach(var risk in DeliveryTests.Cases(root,config))await Run(risk.Name,risk.Test);
    foreach(var risk in Phase2aSqlTests.Cases(options,config).Where(x=>x.Name.Contains("exact immutable")||x.Name.Contains("public metadata")))await Run(risk.Name,risk.Test);
    foreach(var risk in InteractionTests.Cases(root,options,config).Where(x=>x.Name.Contains("outbox real TCP")))await Run(risk.Name,risk.Test);
    // Receipt review deliberately accepts only its historical Test sink roots.
    // Run F4 separately under a unique phase3b sink artifact root, not by widening
    // the application's trusted receipt boundary for the new test namespace.
    await Run("Delivery real HTTP Identity CAPTCHA TOTP enrollment login replay recovery reset session revoke",()=>DeliveryTests.MfaHttp(root,config,options));
    foreach(var risk in VideoTests.Cases(root,options,config).Where(x=>x.Name.StartsWith("2D genuine Defender video")||x.Name.StartsWith("2D native FFmpeg")))await Run(risk.Name,risk.Test);
    var folder=TestEvidencePaths.At(root);Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,"delivery-tests.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,failures=failed,tests,fullRegression="NOT RUN",devWrites=0},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;
}
if(args.Contains("--srcset-only"))
{
    foreach(var risk in SrcsetParserTests.Cases(root))await Run(risk.Name,risk.Test);
    var dir=TestEvidencePaths.At(root);Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(Path.Combine(dir,"srcset-tests.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,failures=failed,tests,sqlWrites=0,networkRequests=0},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} srcset focused groups; real cached fixture, no SQL writes or network; full regression NOT RUN.");return failed==0?0:1;
}
if(args.Contains("--batch-tracking-only"))
{
    var receiptIndex=Array.IndexOf(args,"--test-backup-receipt");
    if(receiptIndex<0||receiptIndex+1>=args.Length)throw new InvalidOperationException("Verified fresh SQL Test backup receipt required before fixture writes.");
    await using(var backupDb=new PortalDbContext(options))await ApprovedBatchExecution.VerifyBackupAsync(backupDb,root,"LandPortal_Test",args[receiptIndex+1]);
    var planned=BatchTrackingTests.Cases(root,options,config).ToList();
    foreach(var risk in planned){await Run(risk.Name,risk.Test);if(failed>0)break;}
    var dir=TestEvidencePaths.At(root);Directory.CreateDirectory(dir);
    var notRun=planned.Skip(tests.Count).Select(x=>x.Name).ToList();
    await File.WriteAllTextAsync(Path.Combine(dir,"sql-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests,notRun},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} tracking-focused groups; NOT RUN {notRun.Count}; technical fixtures only, no real source imports; full regression NOT RUN.");return failed==0?0:1;
}
if(args.Contains("--phase3f-only"))
{
    foreach(var risk in Phase3FTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    var dir=TestEvidencePaths.At(root);Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(Path.Combine(dir,"sql-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} Phase3F focused groups; fixtures are not real migration; full regression NOT RUN.");return failed==0?0:1;
}
if(args.Contains("--phase3e-only"))
{
    foreach(var risk in Phase3ETests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    var dir=TestEvidencePaths.At(root);Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(Path.Combine(dir,"sql-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} Phase3E SQL focused groups; real batch and HTTP are separate operations; full regression NOT RUN.");return failed==0?0:1;
}
if(args.Contains("--phase3d-only"))
{
    foreach(var risk in Phase3DTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    await Run("3D secondary HTTP alias .html 301 exact publication conflicting protocols and withdrawn privacy",()=>Phase3DHttpTests.RunAsync(root,config,options));
    foreach(var risk in MediaRecoveryTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    await Run("3D actual scanned source HTTP SHA PDF Range public/private ACL and affected pages",()=>MediaRecoveryTests.HttpAsync(root,config,options));
    var dir=TestEvidencePaths.At(root);Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(Path.Combine(dir,"sql-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} Phase3D focused groups; full regression NOT RUN.");return failed==0?0:1;
}
if (args.Contains("--public-query-probe"))
{
    await PublicQueryProbe.RunAsync(root, DatabaseSettings.Options(config));
    return 0;
}
if(args.Contains("--phase3c-only"))
{
    foreach(var risk in MediaRecoveryTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    await Run("3C HTTP genuine scanned source public/private exact bytes PDF Range and affected page smoke",()=>MediaRecoveryTests.HttpAsync(root,config,options));
    var dir=TestEvidencePaths.At(root);Directory.CreateDirectory(dir);await File.WriteAllTextAsync(Path.Combine(dir,"sql-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} Phase3C focused groups; full regression NOT RUN.");return failed==0?0:1;
}
if(args.Contains("--unblock-only"))
{
    foreach(var risk in BatchUnblockTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    foreach(var risk in BatchImportTests.Cases(root,options,config).Where(x=>x.Name.StartsWith("3B SQL technical")||x.Name.StartsWith("3B SQL fault")||x.Name.StartsWith("3B SQL changed")||x.Name.StartsWith("3B real-photo negative")))await Run(risk.Name,risk.Test);
    await Run("3B-Unblock minimal actual public article category media hash and private ACL smoke",()=>BatchUnblockTests.HttpAsync(root,config,options));
    var dir=TestEvidencePaths.At(root);Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(Path.Combine(dir,"sql-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} importer-focused groups; full regression NOT RUN.");return failed==0?0:1;
}
if(args.Contains("--batch-only"))
{
    foreach(var risk in BatchUnblockTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    foreach(var risk in BatchImportTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    await Run("3B actual real-import public SQL revision media aliases Vietnamese search and desktop mobile without source dependency",()=>BatchPublicHttpTests.RunAsync(root,config,options));
    var dir=TestEvidencePaths.At(root);Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(Path.Combine(dir,"sql-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} batch groups; offline source only.");return failed==0?0:1;
}
if(args.Contains("--inventory-only"))
{
    foreach(var risk in InventoryTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    var dir=TestEvidencePaths.At(root);Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(Path.Combine(dir,"inventory-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} inventory groups; saved source and synthetic faults separated; no live crawl.");return failed==0?0:1;
}
if(args.Contains("--video-only"))
{
    foreach(var risk in VideoTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    foreach(var risk in VideoTests.SubtitleCases(config,options))await Run(risk.Name,risk.Test);
    await Run("2D actual browser upload interruption restart genuine scan worker MP4 poster revision ACL HTTP seek/revoke",()=>VideoHttpTests.RunAsync(root,config,options));
    var videoDir=TestEvidencePaths.At(root);Directory.CreateDirectory(videoDir);await File.WriteAllTextAsync(Path.Combine(videoDir,"sql-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} video focused risk groups; conversion/seek NOT VERIFIED if processor unavailable.");return failed==0?0:1;
}
if(args.Contains("--scan-source-report")){await ScannerTests.SourceReportAsync(root,config,options);return 0;}
if(args.Contains("--scan-only"))
{
    foreach(var risk in ScannerTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    await Run("SCAN actual standalone Defender worker SQL OTP private HTTP Clean/Infected/HEAD/Range/tamper/revocation/browser",()=>InquiryFinishHttpTests.RunAsync(root,config,options,true));
    var scanDirectory=TestEvidencePaths.At(root);Directory.CreateDirectory(scanDirectory);await File.WriteAllTextAsync(Path.Combine(scanDirectory,"sql-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} SCAN focused groups; genuine engine required.");return failed==0?0:1;
}
PortalDbContext Db() => new(options);
void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
async Task Run(string name, Func<Task> test)
{
    try { await test(); tests.Add(new { name, status = "PASS" }); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; tests.Add(new { name, status = "FAIL", error = error.GetType().Name, detail = error.Message }); Console.WriteLine("FAIL " + name + ": " + error.GetType().Name); }
}
async Task Expect<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
}

if(args.Contains("--v05-only"))
{
    foreach(var risk in V05Tests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    await Run("V05-06 bounded DOCX technical fixtures vertical merges ambiguity and private metadata rejection, not AC16.1",()=>V05WordTests.RunAsync(root));
    await Run("V05 actual HTTP CAPTCHA default limit lookup gate mailbox filters sitemap and responsive UI",()=>V05HttpTests.RunAsync(root,config,options));
    var dir=TestEvidencePaths.At(root);Directory.CreateDirectory(dir);await File.WriteAllTextAsync(Path.Combine(dir,"sql-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} V05 focused risk groups; real Word acceptance remains separate.");return failed==0?0:1;
}
if(args.Contains("--finish-sql-only")||args.Contains("--finish-http-only")||args.Contains("--finish-only"))
{
    if(!args.Contains("--finish-http-only"))foreach(var risk in InquiryFinishTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    if(!args.Contains("--finish-sql-only"))await Run("Finish HTTP private attachment assignment lookup reconcile export and browser",()=>InquiryFinishHttpTests.RunAsync(root,config,options));
    var dir=TestEvidencePaths.At(root);Directory.CreateDirectory(dir);await File.WriteAllTextAsync(Path.Combine(dir,"sql-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} Finish SQL groups; real scanner clean/infected BLOCKED if unavailable.");return failed==0?0:1;
}
if(args.Contains("--interactions-only"))
{
    foreach(var risk in InteractionTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    await Run("2C HTTP four public/OTP/SQL/admin flows and actual browser",()=>InteractionHttpTests.RunAsync(root,config,options));
    var dir=TestEvidencePaths.At(root);Directory.CreateDirectory(dir);await File.WriteAllTextAsync(Path.Combine(dir,"sql-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} 2C groups; HTTP/UI/worker counts separate.");return failed==0?0:1;
}
if (args.Contains("--v04-only"))
{
    foreach(var risk in V04Tests.Cases(options,config))await Run(risk.Name,risk.Test);
    await Run("V04 HTTP scopes idle expiry reauth and public revision SEO",()=>V04HttpTests.RunAsync(root,config,options));
    var v04Directory=TestEvidencePaths.At(root,"v04");Directory.CreateDirectory(v04Directory);
    await File.WriteAllTextAsync(Path.Combine(v04Directory,"delta-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"RESULT {tests.Count-failed}/{tests.Count} V04 delta groups; HTTP/UI evidence separate.");return failed==0?0:1;
}
if (args.Contains("--site-http-only"))
{
    await Run("2B site configuration HTTP and browser focused suite", () => Phase2bSiteHttpTests.RunAsync(root, config, options));
    var siteHttpDirectory = TestEvidencePaths.At(root, "phase2b"); Directory.CreateDirectory(siteHttpDirectory);
    await File.WriteAllTextAsync(Path.Combine(siteHttpDirectory, "site-http-tests.json"), JsonSerializer.Serialize(new { database = "LandPortal_Test", utc = DateTimeOffset.UtcNow, failures = failed, tests }, new JsonSerializerOptions { WriteIndented = true }));
    return failed == 0 ? 0 : 1;
}
if (args.Contains("--site-only"))
{
    foreach (var risk in Phase2bSiteTests.Cases(options, config)) await Run(risk.Name, risk.Test);
    var siteDirectory = TestEvidencePaths.At(root, "phase2b"); Directory.CreateDirectory(siteDirectory);
    await File.WriteAllTextAsync(Path.Combine(siteDirectory, "site-sql-tests.json"), JsonSerializer.Serialize(new { database = "LandPortal_Test", utc = DateTimeOffset.UtcNow, failures = failed, tests }, new JsonSerializerOptions { WriteIndented = true }));
    return failed == 0 ? 0 : 1;
}
if (args.Contains("--content-only"))
{
    foreach(var risk in Phase2bContentTests.Cases(options,config)) await Run(risk.Name,risk.Test);
    var dir=TestEvidencePaths.At(root,"phase2b");Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(Path.Combine(dir,"content-search-tests.json"),JsonSerializer.Serialize(new { database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests },new JsonSerializerOptions { WriteIndented=true }));
    return failed == 0 ? 0 : 1;
}
if (args.Contains("--account-http-only"))
{
    await Run("2B Identity account/role HTTP and browser focused suite", () => Phase2bAccountHttpTests.RunAsync(root, config, options));
    var accountHttpDirectory = TestEvidencePaths.At(root, "phase2b"); Directory.CreateDirectory(accountHttpDirectory);
    await File.WriteAllTextAsync(Path.Combine(accountHttpDirectory, "account-http-tests.json"), JsonSerializer.Serialize(new { database = "LandPortal_Test", utc = DateTimeOffset.UtcNow, failures = failed, tests }, new JsonSerializerOptions { WriteIndented = true }));
    return failed == 0 ? 0 : 1;
}

if (args.Contains("--accounts-only"))
{
    foreach (var risk in Phase2bAccountTests.Cases(options)) await Run(risk.Name, risk.Test);
    var accountDirectory = TestEvidencePaths.At(root, "phase2b"); Directory.CreateDirectory(accountDirectory);
    await File.WriteAllTextAsync(Path.Combine(accountDirectory, "account-sql-tests.json"), JsonSerializer.Serialize(new { database = "LandPortal_Test", utc = DateTimeOffset.UtcNow, failures = failed, tests }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"RESULT {tests.Count - failed}/{tests.Count} account SQL groups passed; HTTP/UI not included.");
    return failed == 0 ? 0 : 1;
}

if (args.Contains("--scheduler-http-only"))
{
    await Run("2B scheduler HTTP/UI and independent worker", () => Phase2bSchedulerHttpTests.RunAsync(root, config, options));
    var schedulerHttpDirectory = TestEvidencePaths.At(root, "phase2b"); Directory.CreateDirectory(schedulerHttpDirectory);
    await File.WriteAllTextAsync(Path.Combine(schedulerHttpDirectory, "scheduler-http-tests.json"), JsonSerializer.Serialize(new { database = "LandPortal_Test", utc = DateTimeOffset.UtcNow, failures = failed, tests }, new JsonSerializerOptions { WriteIndented = true }));
    return failed == 0 ? 0 : 1;
}
if (args.Contains("--scheduler-only"))
{
    foreach (var risk in Phase2bSchedulerTests.Cases(options, config)) await Run(risk.Name, risk.Test);
    var schedulerDirectory = TestEvidencePaths.At(root, "phase2b"); Directory.CreateDirectory(schedulerDirectory);
    await File.WriteAllTextAsync(Path.Combine(schedulerDirectory, "scheduler-tests.json"), JsonSerializer.Serialize(new { database = "LandPortal_Test", utc = DateTimeOffset.UtcNow, failures = failed, tests }, new JsonSerializerOptions { WriteIndented = true }));
    return failed == 0 ? 0 : 1;
}
if (args.Contains("--http-only")||args.Contains("--http-no-ui"))
{
    await Run("2A HTTP/UI focused rerun", () => Phase2aHttpTests.RunAsync(root, config, options,runBrowser:!args.Contains("--http-no-ui")));
    var privatePath=TestEvidencePaths.At(root,"phase2a");Directory.CreateDirectory(privatePath);
    await File.WriteAllTextAsync(Path.Combine(privatePath,"http-only-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));
    return failed==0?0:1;
}
if(args.Contains("--retired-media-only"))
{
    foreach(var risk in Phase2aSqlTests.Cases(options,config).Where(x=>x.Name.StartsWith("2A SQL retired media selections")))await Run(risk.Name,risk.Test);
    var retiredDirectory=TestEvidencePaths.At(root,"phase2a");Directory.CreateDirectory(retiredDirectory);await File.WriteAllTextAsync(Path.Combine(retiredDirectory,"retired-media-sql-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;
}
if(args.Contains("--compatibility-only"))
{
    foreach(var risk in Phase2aSqlTests.Cases(options,config).Where(x=>x.Name.StartsWith("2A SQL copied historical source")||x.Name.StartsWith("2A SQL withdrawal audit")))await Run(risk.Name,risk.Test);
    var compatibilityDirectory=TestEvidencePaths.At(root,"phase2a");Directory.CreateDirectory(compatibilityDirectory);await File.WriteAllTextAsync(Path.Combine(compatibilityDirectory,"source-compatibility-tests.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,failures=failed,tests},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;
}

foreach(var risk in InventoryTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
foreach(var risk in BatchUnblockTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
foreach(var risk in BatchImportTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
await Run("3B actual real-import public SQL revision media aliases Vietnamese search and desktop mobile without source dependency",()=>BatchPublicHttpTests.RunAsync(root,config,options));
await Run("SQL migration rerun retains the same applied migrations", async () =>
{
    await using var db = Db();
    await SchemaManager.MigrateAsync(db, "LandPortal_Test");
    var before = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    Assert(before.Length > 0, "No real SQL migration was applied.");
    await SchemaManager.MigrateAsync(db, "LandPortal_Test");
    Assert(before.SequenceEqual(await db.Database.GetAppliedMigrationsAsync()), "Migration rerun changed history.");
});

await Run("SQL runtime application role can read content but cannot alter schema or modify audit history", async () =>
{
    var secret = config["Runtime:RolePassword"] ?? throw new InvalidOperationException("Runtime role secret is required for this test.");
    long auditId;
    await using (var operatorDb = Db())
    {
        await RuntimeRoleInterceptor.ProvisionAsync(operatorDb, secret);
        var audit = new AuditLog { Actor = "qa", Action = "PermissionFixture" };
        operatorDb.AuditLogs.Add(audit); await operatorDb.SaveChangesAsync(); auditId = audit.Id;
    }
    await using var runtimeDb = new PortalDbContext(DatabaseSettings.Options(config));
    await runtimeDb.Contents.CountAsync();
    await runtimeDb.Database.OpenConnectionAsync();
    using var probe = runtimeDb.Database.GetDbConnection().CreateCommand();
    probe.CommandText = "SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER'), HAS_PERMS_BY_NAME('dbo.AuditLogs', 'OBJECT', 'UPDATE'), HAS_PERMS_BY_NAME('dbo.AuditLogs', 'OBJECT', 'DELETE')";
    using (var reader = await probe.ExecuteReaderAsync())
    {
        Assert(await reader.ReadAsync(), "Runtime permissions probe returned no row.");
        Assert(reader.GetInt32(0) == 0 && reader.GetInt32(1) == 0 && reader.GetInt32(2) == 0, "Runtime still has operator/schema/audit update privileges.");
    }
    await Expect<SqlException>(() => runtimeDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.AuditLogs SET Action=N'Unauthorized' WHERE Id={auditId}"));
    await runtimeDb.Database.CloseConnectionAsync();
});

await Run("Vietnamese normalization matches đ/d and composed/decomposed Unicode", () =>
{
    const string expected = "dang ky dat dai 24/2024/nd-cp";
    Assert(TextNormalizer.Search("Đăng ký đất đai 24/2024/NĐ-CP") == expected, "Vietnamese normalization failed.");
    Assert(TextNormalizer.Search("Đăng ký đất đai 24/2024/NĐ-CP".Normalize(NormalizationForm.FormD)) == expected, "Decomposed Unicode failed.");
    return Task.CompletedTask;
});

await Run("Server sanitization removes scripts, event handlers and external images, preserves tables", () =>
{
    var html = new HtmlPolicy().Clean("<script>alert(1)</script><p onclick='alert(2)'>Đất đai</p><a href='javascript:alert(3)'>x</a><img src='https://qldd.mae.gov.vn/x.jpg' onerror='alert(4)'><img src='/media/11111111-1111-1111-1111-111111111111'><img src='/media/../admin/private'><table><caption>Bảng dán từ Word</caption><colgroup><col span='2'></colgroup><tr><th scope='col'>Diện tích</th><td>1.234</td></tr></table><figure><img src='/media/11111111-1111-1111-1111-111111111111'><figcaption>Chú thích ảnh hợp lệ</figcaption></figure>");
    Assert(!html.Contains("<script", StringComparison.OrdinalIgnoreCase) && !html.Contains("onclick", StringComparison.OrdinalIgnoreCase) && !html.Contains("onerror", StringComparison.OrdinalIgnoreCase) && !html.Contains("javascript:", StringComparison.OrdinalIgnoreCase), "Active content survived sanitation.");
    Assert(!html.Contains("qldd.mae.gov.vn"), "A source hotlinked image survived sanitation.");
    Assert(html.Contains("/media/") && html.Contains("<table>") && html.Contains("1.234") && html.Contains("Đất đai"), "Meaningful local assets/table/text were lost.");
    Assert(html.Contains("<caption>")&&html.Contains("<colgroup>")&&html.Contains("<col")&&html.Contains("scope=\"col\"")&&html.Contains("<figcaption>")&&!html.Contains("../admin"),"Semantic table/caption/column structure was lost or media traversal survived.");
    return Task.CompletedTask;
});

await Run("Safe unsupported source wrappers retain all Vietnamese text, tables and local images", () =>
{
    var raw = "<section><article><font color='red'><p>Đăng ký đất đai: 1.234 m²</p><table><tr><td>Không được mất dòng này</td></tr></table><figure><img src='/media/11111111-1111-1111-1111-111111111111'><figcaption>Sơ đồ thửa đất</figcaption></figure></font></article></section>";
    var policy = new HtmlPolicy(); var clean = policy.Clean(raw);
    Assert(policy.PlainText(clean) == policy.PlainText(raw), "Unsupported safe wrapper discarded meaningful source text.");
    Assert(clean.Contains("<table>") && clean.Contains("/media/") && clean.Contains("<figcaption>"), "Unsupported safe wrapper discarded table/image/caption children.");
    return Task.CompletedTask;
});

await Run("Keeping safe wrapper children does not retain script/style text or active elements", () =>
{
    var clean = new HtmlPolicy().Clean("<section>Văn bản công khai<script>SECRET_SCRIPT_TEXT</script><style>SECRET_STYLE_TEXT</style><iframe src='https://example.invalid/'>SECRET_FRAME_TEXT</iframe><object data='x'>SECRET_OBJECT_TEXT</object><p onmouseover='alert(1)'>Nội dung đúng</p></section>");
    Assert(!clean.Contains("SECRET_") && !clean.Contains("<script") && !clean.Contains("<style") && !clean.Contains("onmouseover") && !clean.Contains("<iframe") && !clean.Contains("<object"), "Active content or its text survived wrapper retention.");
    Assert(clean.Contains("Văn bản công khai") && clean.Contains("Nội dung đúng"), "Safe surrounding text was lost.");
    return Task.CompletedTask;
});

await Run("Storage containment rejects relative and absolute path traversal", () =>
{
    var local = Path.Combine(root, ".local", "tests", "media");
    var valid = StorageOptions.ResolveWithin(local, "hash/image.png");
    Assert(valid.StartsWith(Path.GetFullPath(local)), "Valid path was rejected.");
    foreach (var bad in new[] { "../private.txt", "../../outside.txt", Path.GetPathRoot(root)! + "outside.txt" })
    {
        var rejected = false;
        try { StorageOptions.ResolveWithin(local, bad); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "Storage containment accepted traversal.");
    }
    return Task.CompletedTask;
});

await Run("Parser keeps date-only precision and legal dates without inventing publication time or effectiveness", () =>
{
    var dateOnly = new ParsedSource(); SourceParser.SetDate(dateOnly, "12/10/2026");
    Assert(dateOnly.PublishedDate == new DateOnly(2026,10,12) && dateOnly.PublishedUtc is null && dateOnly.DatePrecision == "Day", "Date-only source acquired an invented time.");
    var minute = new ParsedSource(); SourceParser.SetDate(minute, "08:30 12/10/2026");
    Assert(minute.PublishedUtc == new DateTimeOffset(2026,10,12,1,30,0,TimeSpan.Zero), "Vietnamese source minute did not convert from UTC+7.");
    var legal = SourceParser.Parse("<table class='contentvbdh'><tr><td>Trích yếu</td><td>Văn bản thử parser</td></tr><tr><td>Số ký hiệu</td><td>24/2024/NĐ-CP</td></tr><tr><td>Ngày ban hành</td><td>01/08/2024</td></tr></table>", "https://qldd.mae.gov.vn/van-ban-1.htm", ContentKind.LegalDocument, "1");
    Assert(legal.PublishedUtc is null && legal.PublishedDate is null && legal.Fields["Ngày ban hành"] == "01/08/2024", "Issue date was lost or converted into publication date.");
    Assert(!legal.Fields.ContainsKey("Ngày hiệu lực"), "Missing effectiveness field was invented.");
    return Task.CompletedTask;
});

await Run("Crawler refuses SSRF schemes, private/reserved IPs, non-allowlist hosts and exhausted budget before network", async () =>
{
    foreach (var address in new[] { "127.0.0.1", "10.0.0.1", "172.16.0.1", "192.168.1.1", "169.254.169.254", "100.64.0.1", "::1", "fc00::1", "fe80::1", "::ffff:127.0.0.1", "2001:db8::1" }) Assert(!SafeSourceClient.IsPublicAddress(System.Net.IPAddress.Parse(address)), "SSRF IP policy accepted " + address);
    Assert(SafeSourceClient.IsPublicAddress(System.Net.IPAddress.Parse("8.8.8.8")), "Public IP policy rejected ordinary public address.");
    using var client = new SafeSourceClient(0);
    foreach (var url in new[] { "http://127.0.0.1/", "http://example.invalid/", "file:///C:/secret", "https://qldd.mae.gov.vn:8443/", "https://user@qldd.mae.gov.vn/" }) await Expect<InvalidDataException>(() => client.FetchAsync(url));
    await Expect<RequestBudgetException>(() => client.FetchAsync("https://qldd.mae.gov.vn/"));
    Assert(client.RequestsUsed == 0, "Rejected URL/budget consumed a network request.");
});

await Run("Media detection rejects an HTML error masquerading as PDF and unsafe SVG, recognizes byte signatures", () =>
{
    Assert(SampleCollector.DetectType(Encoding.UTF8.GetBytes("<html>404.pdf</html>")) is null, "HTML error classified as PDF.");
    Assert(SampleCollector.DetectType(Encoding.UTF8.GetBytes("<svg onload='alert(1)'></svg>")) is null, "Active SVG was accepted.");
    Assert(SampleCollector.DetectType(Encoding.ASCII.GetBytes("%PDF-1.7\nfixture"))?.Mime == "application/pdf", "PDF signature not recognized.");
    Assert(SampleCollector.DetectType(new byte[] { 0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a,0,0,0,0 })?.Mime == "image/png", "PNG signature not recognized.");
    return Task.CompletedTask;
});

await Run("SQL cutover survives a fresh context even if configuration is enabled, then test setting is restored", async () =>
{
    var keys = new[] { "SyncEnabled", "CutoverUtc" };
    await using var db = Db();
    var previous = await db.SiteSettings.Where(x => keys.Contains(x.Key)).AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value);
    try
    {
        foreach (var key in keys)
        {
            var setting = await db.SiteSettings.FindAsync(key);
            var value = key == "SyncEnabled" ? "true" : DateTimeOffset.UtcNow.ToString("O");
            if (setting is null) db.SiteSettings.Add(new SiteSetting { Key = key, Value = value }); else setting.Value = value;
        }
        await db.SaveChangesAsync();
        await using var restarted = Db();
        var enabled = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Sync:Enabled"] = "true", ["Sync:CutoverUtc"] = "" }).Build();
        await Expect<InvalidOperationException>(() => SynchronizationGuard.EnsureEnabledAsync(restarted, enabled));
    }
    finally
    {
        foreach (var key in keys)
        {
            var setting = await db.SiteSettings.FindAsync(key);
            if (previous.TryGetValue(key, out var value)) setting!.Value = value;
            else if (setting is not null) db.SiteSettings.Remove(setting);
        }
        await db.SaveChangesAsync();
    }
});

await Run("SQL unique source identity allows equal ids across kinds and rejects same-kind duplicate", async () =>
{
    var key = "QA-" + Guid.NewGuid().ToString("N");
    await using var db = Db();
    var article = new Content { Kind = ContentKind.Article };
    var document = new Content { Kind = ContentKind.LegalDocument };
    db.AddRange(article, document);
    await db.SaveChangesAsync();
    db.SourceRecords.AddRange(
        new SourceRecord { ContentId = article.Id, Kind = ContentKind.Article, SourceId = key },
        new SourceRecord { ContentId = document.Id, Kind = ContentKind.LegalDocument, SourceId = key });
    await db.SaveChangesAsync();
    db.SourceRecords.Add(new SourceRecord { ContentId = article.Id, Kind = ContentKind.Article, SourceId = key });
    await Expect<DbUpdateException>(() => db.SaveChangesAsync());
});

await Run("SQL unique media hash rejects duplicate files independent of original names", async () =>
{
    var hash = TextNormalizer.Hash(Guid.NewGuid().ToString());
    await using var db = Db();
    db.MediaAssets.Add(new MediaAsset { Sha256 = hash, OriginalName = "a.pdf", StorageKey = "qa-a", MimeType = "application/pdf", ByteLength = 10 });
    await db.SaveChangesAsync();
    db.MediaAssets.Add(new MediaAsset { Sha256 = hash, OriginalName = "b.pdf", StorageKey = "qa-b", MimeType = "application/pdf", ByteLength = 10 });
    await Expect<DbUpdateException>(() => db.SaveChangesAsync());
});

await Run("SQL rowversion rejects stale concurrent edits", async () =>
{
    var content = new Content();
    await using (var seed = Db()) { seed.Contents.Add(content); await seed.SaveChangesAsync(); }
    await using var first = Db();
    await using var second = Db();
    var a = await first.Contents.SingleAsync(x => x.Id == content.Id);
    var b = await second.Contents.SingleAsync(x => x.Id == content.Id);
    Assert(a.RowVersion.Length == 8, "SQL rowversion is not eight bytes.");
    a.CommentsEnabled = true;
    await first.SaveChangesAsync();
    b.HasEditorialChanges = true;
    await Expect<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
});

foreach (var risk in Phase2aSqlTests.Cases(options, config)) await Run(risk.Name, risk.Test);
if (!args.Contains("--sql-media-only"))
{
    foreach(var risk in Phase2bContentTests.Cases(options,config)) await Run(risk.Name,risk.Test);
    foreach(var risk in Phase2bAccountTests.Cases(options)) await Run(risk.Name,risk.Test);
    foreach(var risk in Phase2bSiteTests.Cases(options,config)) await Run(risk.Name,risk.Test);
    foreach(var risk in Phase2bSchedulerTests.Cases(options,config)) await Run(risk.Name,risk.Test);
    await Phase2bSchedulerTests.WithdrawPublicFixturesAsync(options,config);
    foreach(var risk in V04Tests.Cases(options,config))await Run(risk.Name,risk.Test);
    await Run("V04 HTTP scopes idle expiry reauth and public revision SEO",()=>V04HttpTests.RunAsync(root,config,options));
}

await Run("SQL parameterized Vietnamese search returns correct Unicode content", async () =>
{
    await using var db = Db();
    var unique = Guid.NewGuid().ToString("N");
    var workflow = new WorkflowService(db, new HtmlPolicy());
    var content = await workflow.SaveAsync(null, Phase2aSqlTests.Input("Đăng ký đất đai " + unique, body: "<p>Unicode tổ hợp</p>"), Phase2aSqlTests.Actor(), null, Guid.NewGuid());
    await workflow.PublishDirectAsync(content.ContentId, content.CurrentRevisionId!.Value, Phase2aSqlTests.Actor(), content.RowVersion, Guid.NewGuid());
    var needle = TextNormalizer.Search("dang ky dat dai " + unique);
    var found = await db.Revisions.AsNoTracking().Where(x => x.SearchText.Contains(needle) && x.Content.PublishedRevisionId == x.Id).Select(x => x.Title).ToListAsync();
    Assert(found.Count == 1 && found[0].StartsWith("Đăng ký đất đai"), "Normalized SQL search failed or altered display text.");
});

await Run("Real saved source snapshots parse article, document and consultation types without additional network", async () =>
{
    var storage = StorageOptions.FromConfiguration(config);
    var manifest = JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(Path.Combine(storage.SnapshotRoot,"sample-manifest.json")), SampleCollector.JsonOptions) ?? throw new InvalidDataException("Sample manifest missing.");
    Assert(manifest.Contents.Count >= 30, "Real sample has fewer than 30 staged contents.");
    foreach (var kind in new[] { ContentKind.Article, ContentKind.LegalDocument, ContentKind.DraftDocument }) Assert(manifest.Contents.Any(x => x.Source.Kind == kind), "Required accessible real fixture type missing: " + kind);
    var parsed = 0;
    foreach (var staged in manifest.Contents)
    {
        var snapshot = manifest.Snapshots.LastOrDefault(x => x.Url == staged.Source.SourceUrl || x.FinalUrl == staged.Source.SourceUrl) ?? throw new InvalidDataException("Real source snapshot missing for staged content.");
        var html = await File.ReadAllTextAsync(storage.ResolveSnapshot(snapshot.StorageKey));
        var source = SourceParser.Parse(html, staged.Source.SourceUrl, staged.Source.Kind, staged.Source.SourceId);
        Assert(source.Title == staged.Source.Title && source.SourceId == staged.Source.SourceId, "Offline source parser changed verified identity/title.");
        string Meaningful(string body) => System.Text.RegularExpressions.Regex.Replace(new HtmlPolicy().PlainText(body).Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
        Assert(Meaningful(source.BodyHtml) == Meaningful(staged.Source.BodyHtml), "Staged source lost or changed meaningful Unicode text from the saved raw body.");
        parsed++;
    }
    Assert(parsed == manifest.Contents.Count, "Not every staged source fixture was parsed.");
    var list = manifest.Lists.FirstOrDefault(x => x.CategoryId == 75 && x.Pagination.TotalPages > 1) ?? throw new InvalidDataException("Real source pagination evidence missing.");
    var first = list.SourceIds.ToHashSet();
    var next = manifest.Lists.FirstOrDefault(x => x.Url == SourceCatalog.BaseUrl + list.Pagination.NextTemplate.Replace("{page}","2"));
    Assert(next != null && next.SourceIds.Count > 0 && !first.SetEquals(next.SourceIds), "Page two did not provide a distinct real ID set.");
});

await Run("Real SQL importer rerun creates no duplicate content, source revisions, aliases or assets", async () =>
{
    var storage = StorageOptions.FromConfiguration(config);
    var manifest = JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(Path.Combine(storage.SnapshotRoot,"sample-manifest.json")), SampleCollector.JsonOptions) ?? throw new InvalidDataException("Sample manifest missing.");
    Assert(manifest.Contents.Count >= 30 && manifest.Contents.Any(x => x.Source.Kind == ContentKind.LegalDocument), "Real multi-type manifest is not ready.");
    await using var db = Db();
    await SynchronizationGuard.EnsureEnabledAsync(db, config);
    var importer = new SqlImporter(db, storage);
    await importer.ImportAsync(manifest);
    var ids = manifest.Contents.Select(x => x.Source.SourceId).Distinct().ToArray();
    var before = new[] { await db.Contents.CountAsync(), await db.Revisions.CountAsync(), await db.SourceRecords.CountAsync(), await db.SourceUrlAliases.CountAsync(), await db.MediaAssets.CountAsync() };
    await importer.ImportAsync(manifest);
    var after = new[] { await db.Contents.CountAsync(), await db.Revisions.CountAsync(), await db.SourceRecords.CountAsync(), await db.SourceUrlAliases.CountAsync(), await db.MediaAssets.CountAsync() };
    Assert(before.SequenceEqual(after), "Rerunning real import created duplicate identities/revisions/aliases/assets.");
    foreach (var staged in manifest.Contents) Assert(await db.SourceRecords.AnyAsync(x => x.Source == "qldd.mae.gov.vn" && x.Kind == staged.Source.Kind && x.SourceId == staged.Source.SourceId), "A staged real content did not reach SQL.");
    var verification = JsonSerializer.Serialize(await importer.VerifyAsync(manifest), SampleCollector.JsonOptions);
    Assert(verification.Contains("SampleVerified"), "Real SQL/assets/snapshot reconciliation failed.");
});

await Run("Changed source imports a comparison revision without replacing editor or public revisions", async () =>
{
    var storage = StorageOptions.FromConfiguration(config);
    var manifest = JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(Path.Combine(storage.SnapshotRoot,"sample-manifest.json")), SampleCollector.JsonOptions)!;
    var staged = manifest.Contents.First(x => x.Source.Kind == ContentKind.Article);
    manifest.Contents = [staged]; manifest.Discovered = manifest.Discovered.Where(x => x.Kind == ContentKind.Article && x.SourceId == staged.Source.SourceId).ToList();
    await using var db = Db();
    var source = await db.SourceRecords.SingleAsync(x => x.Source == "qldd.mae.gov.vn" && x.Kind == staged.Source.Kind && x.SourceId == staged.Source.SourceId);
    var content = await db.Contents.SingleAsync(x => x.Id == source.ContentId);
    content.HasEditorialChanges = true; await db.SaveChangesAsync();
    var current = content.CurrentRevisionId; var published = content.PublishedRevisionId;
    var before = await db.Revisions.CountAsync(x => x.ContentId == content.Id);
    staged.Source.Title += " [QA changed source test only]";
    staged.ContentHash = TextNormalizer.Hash(staged.ContentHash + Guid.NewGuid().ToString());
    await new SqlImporter(db,storage).ImportAsync(manifest);
    await using var independent = Db();
    var retained = await independent.Contents.SingleAsync(x => x.Id == content.Id);
    Assert(retained.CurrentRevisionId == current && retained.PublishedRevisionId == published, "Source refresh overwrote editorial/public revision.");
    Assert(await independent.Revisions.CountAsync(x => x.ContentId == content.Id) == before + 1, "Changed source revision was lost.");
    Assert(await independent.ImportIssues.AnyAsync(x => x.CrawlRunId == manifest.CrawlRunId && x.Code == "SOURCE_CHANGE_AWAITING_COMPARISON"), "Comparison exception not retained.");
});

await Run("Offline importer detects metadata-only category and cover changes with same legacy hash without editorial overwrite or rerun duplicates", async () =>
{
    var storage=StorageOptions.FromConfiguration(config);var manifest=JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(Path.Combine(storage.SnapshotRoot,"sample-manifest.json")),SampleCollector.JsonOptions)!;
    var staged=manifest.Contents.First(x=>x.Source.Kind==ContentKind.Article&&x.AssetIds.Count(id=>manifest.Assets.Any(a=>a.Id==id&&a.MimeType.StartsWith("image/")))>=2);manifest.Contents=[staged];manifest.Discovered=manifest.Discovered.Where(x=>x.Kind==staged.Source.Kind&&x.SourceId==staged.Source.SourceId).ToList();
    await using var db=Db();var importer=new SqlImporter(db,storage);await importer.ImportAsync(manifest);var source=await db.SourceRecords.SingleAsync(x=>x.Kind==staged.Source.Kind&&x.SourceId==staged.Source.SourceId);var content=await db.Contents.SingleAsync(x=>x.Id==source.ContentId);content.HasEditorialChanges=true;await db.SaveChangesAsync();
    var current=content.CurrentRevisionId;var published=content.PublishedRevisionId;var primary=content.PrimaryCategoryId;var originalHash=staged.ContentHash;var before=await db.Revisions.CountAsync(x=>x.ContentId==content.Id);
    var other=await db.Categories.Where(x=>x.Id>0&&x.Id!=staged.Source.PrimaryCategoryId).Select(x=>x.Id).FirstAsync();staged.Source.PrimaryCategoryId=other;staged.Source.CategoryIds.Add(other);await importer.ImportAsync(manifest);
    Assert(await db.Revisions.CountAsync(x=>x.ContentId==content.Id)==before+1,"Category-only source change was silently treated unchanged.");Assert(content.CurrentRevisionId==current&&content.PublishedRevisionId==published&&content.PrimaryCategoryId==primary,"Metadata-only source update overwrote editorial/public taxonomy.");
    var categoriesCounts=new[]{await db.Revisions.CountAsync(),await db.SourceRecords.CountAsync(),await db.SourceUrlAliases.CountAsync(),await db.MediaAssets.CountAsync()};await importer.ImportAsync(manifest);var repeatedCounts=new[]{await db.Revisions.CountAsync(),await db.SourceRecords.CountAsync(),await db.SourceUrlAliases.CountAsync(),await db.MediaAssets.CountAsync()};Assert(categoriesCounts.SequenceEqual(repeatedCounts),"Rerun category-only fixture created duplicate revisions/mappings.");
    var previousCover=await db.Revisions.Where(x=>x.ContentId==content.Id&&x.IsSourceRevision).OrderByDescending(x=>x.CreatedUtc).Select(x=>x.CoverAssetId).FirstAsync();staged.AssetIds.Reverse();await importer.ImportAsync(manifest);var cover=await db.Revisions.Where(x=>x.ContentId==content.Id&&x.IsSourceRevision).OrderByDescending(x=>x.CreatedUtc).Select(x=>x.CoverAssetId).FirstAsync();Assert(cover!=previousCover&&staged.ContentHash==originalHash,"Same-hash source cover-only change was ignored or provenance hash rewritten.");
    var afterCover=await db.Revisions.CountAsync(x=>x.ContentId==content.Id);await importer.ImportAsync(manifest);Assert(await db.Revisions.CountAsync(x=>x.ContentId==content.Id)==afterCover&&content.CurrentRevisionId==current&&content.PublishedRevisionId==published,"Cover-only rerun created duplicates or changed publication.");
});

await Run("2A media original byte validation, limits, isolation and honest scanner status", () => MediaValidationTests.RunAsync(root, config));
if (!args.Contains("--sql-media-only")) await Run("2A HTTP Identity, endpoint permissions, revision isolation, uploads, workflow, revocation and UI", () => HttpWorkflowTests.RunAsync(root, config, options));
else tests.Add(new { name = "2A HTTP/UI", status = "NOT RUN", reason = "Explicit interim --sql-media-only run; final suite must include HTTP/UI." });
if(!args.Contains("--sql-media-only"))
{
    await Run("2B account HTTP/UI endpoint delegation session and temporary-password flows",()=>Phase2bAccountHttpTests.RunAsync(root,config,options));
    await Run("2B site configuration HTTP/UI draft/live public media and source safeguards",()=>Phase2bSiteHttpTests.RunAsync(root,config,options));
    await Run("2B scheduler HTTP/UI exact revision and competing standalone worker processes",()=>Phase2bSchedulerHttpTests.RunAsync(root,config,options));
}

foreach(var risk in V05Tests.Cases(root,options,config))await Run(risk.Name,risk.Test);
await Run("V05-06 bounded DOCX technical fixtures vertical merges ambiguity and private metadata rejection, not AC16.1",()=>V05WordTests.RunAsync(root));
await Run("V05 actual HTTP CAPTCHA default limit lookup gate mailbox filters sitemap and responsive UI",()=>V05HttpTests.RunAsync(root,config,options));
foreach(var risk in InteractionTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
if(!args.Contains("--sql-media-only"))await Run("2C HTTP four public/OTP/SQL/admin flows and actual browser",()=>InteractionHttpTests.RunAsync(root,config,options));
foreach(var risk in InquiryFinishTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
if(!args.Contains("--sql-media-only"))await Run("Finish HTTP private attachment assignment lookup reconcile export and browser",()=>InquiryFinishHttpTests.RunAsync(root,config,options));
foreach(var risk in ScannerTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
if(!args.Contains("--sql-media-only"))await Run("SCAN actual standalone Defender worker SQL OTP private HTTP Clean/Infected/HEAD/Range/tamper/revocation/browser",()=>InquiryFinishHttpTests.RunAsync(root,config,options,true));
if(!args.Contains("--sql-media-only"))
{
    foreach(var risk in VideoTests.Cases(root,options,config))await Run(risk.Name,risk.Test);
    foreach(var risk in VideoTests.SubtitleCases(config,options))await Run(risk.Name,risk.Test);
    await Run("2D actual browser upload interruption restart genuine scan worker MP4 poster revision ACL HTTP seek/revoke",()=>VideoHttpTests.RunAsync(root,config,options));
}
var directory = TestEvidencePaths.At(root);
Directory.CreateDirectory(directory);
await File.WriteAllTextAsync(Path.Combine(directory, "sql-tests.json"), JsonSerializer.Serialize(new { schemaVersion = 1, database = "LandPortal_Test", utc = DateTimeOffset.UtcNow, failures = failed, tests }, new JsonSerializerOptions { WriteIndented = true }));
var passed = tests.Count(x => (string?)x.GetType().GetProperty("status")?.GetValue(x) == "PASS");
Console.WriteLine($"RESULT {passed}/{tests.Count} passed; SQL Server LandPortal_Test only.");
return failed == 0 ? 0 : 1;
