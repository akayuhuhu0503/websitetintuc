using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.WeeklyImport;
using LandPortal.Web.Controllers;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

if (args.Length != 3) throw new ArgumentException("checkout, source ZIP, new test directory required");
var root = Path.GetFullPath(args[0]); var zip = Path.GetFullPath(args[1]); var run = Path.GetFullPath(args[2]);
if (!run.StartsWith(Path.Combine(root, ".local", "tests") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(run)) throw new Exception("New isolated run required");
Directory.CreateDirectory(run); var instance = "LPWeekly_" + Guid.NewGuid().ToString("N")[..12];
var checks = new List<string>(); WebApplication? app = null;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); Console.WriteLine("PASS " + name); }
async Task Denied(Func<Task> action, string name) { try { await action(); } catch { Check(true, name); return; } throw new Exception("NOT DENIED: " + name); }
async Task LocalDb(string operation)
{
    var p = new ProcessStartInfo("SqlLocalDB.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    p.ArgumentList.Add(operation); p.ArgumentList.Add(instance); using var process = Process.Start(p)!;
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); await output; await error;
    if (process.ExitCode != 0) throw new Exception("LocalDB " + operation + " failed");
}
try
{
    var sourceHash = WeeklyPackage.Hash(await File.ReadAllBytesAsync(zip)); var package = WeeklyPackage.Read(zip);
    Check(package.Rows.Count == 43 && package.Rows.Select(r => r.Stt).SequenceEqual(Enumerable.Range(1, 43)), "43 rows mapped by leading STT, not later filename numbers");
    Check(package.Rows.Sum(r => r.Pages) == 120, "43 PDFs parsed: 120 actual pages");
    Check(package.Rows.Count(r => r.DateType == "n") == 17, "17 numeric Excel dates recognized without conversion");
    Check(package.Rows.Where(r => r.ExcelPeriod != r.PdfPeriod).Select(r => r.Stt).SequenceEqual(new[] { 39, 40, 41, 42 }), "Week/date discrepancies detected for 39-42 without changing titles");
    Check(package.Rows.Count(r => r.Pages != r.ExcelPages) == 1 && package.Rows[41].Pages == 3 && package.Rows[41].ExcelPages == 2, "Only E48 corrected from 2 to 3 pages");
    var reviewed = Path.Combine(run, "reviewed.xlsx"); package.WriteWorkbook(reviewed, new Dictionary<int, string?>());
    CheckWorkbook(package, reviewed, new Dictionary<int, string?>());
    await Denied(() => Task.Run(() => package.WriteWorkbook(reviewed, new Dictionary<int, string?>())), "Existing output workbook is never overwritten");
    foreach (var variant in new[] { "missing", "duplicate", "traversal", "false-pdf" })
    {
        var destination = Path.Combine(run, variant + ".zip"); using var original = ZipFile.OpenRead(zip);
        using (var changed = ZipFile.Open(destination, ZipArchiveMode.Create))
        {
            foreach (var entry in original.Entries)
            {
                if (entry.FullName == package.Rows[0].PdfEntry && variant == "missing") continue;
                using var stream = changed.CreateEntry(entry.FullName).Open();
                if (entry.FullName == package.Rows[0].PdfEntry && variant == "false-pdf") stream.Write("not a PDF"u8);
                else { using var input = entry.Open(); input.CopyTo(stream); }
            }
            if (variant == "duplicate") { using var stream = changed.CreateEntry("Lich Tuan/1_duplicate.pdf").Open(); stream.Write(package.PdfBytes[1]); }
            if (variant == "traversal") { using var stream = changed.CreateEntry("../outside.pdf").Open(); stream.Write(package.PdfBytes[1]); }
        }
        await Denied(() => Task.Run(() => WeeklyPackage.Read(destination)), "Reject source " + variant);
    }
    await LocalDb("create"); await LocalDb("start");
    var cs = new SqlConnectionStringBuilder { DataSource = @"(localdb)\" + instance, InitialCatalog = "LandPortal_Test", IntegratedSecurity = true, Encrypt = true, TrustServerCertificate = true, Pooling = false };
    await using (var sql = new SqlConnection(new SqlConnectionStringBuilder(cs.ConnectionString) { InitialCatalog = "master" }.ConnectionString))
    {
        await sql.OpenAsync(); using var cmd = sql.CreateCommand(); cmd.CommandText = "SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB')),CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),DB_ID('LandPortal_Test')";
        using (var r = await cmd.ExecuteReaderAsync()) { await r.ReadAsync(); Check(r.GetInt32(0) == 1 && r.GetString(1).Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) && r.IsDBNull(2), "Fresh isolated LocalDB: not LAN or operational SQL"); }
        cmd.CommandText = "CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'" + Path.Combine(run, "test.mdf").Replace("'", "''") + "') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'" + Path.Combine(run, "test.ldf").Replace("'", "''") + "')";
        await cmd.ExecuteNonQueryAsync();
    }
    var operatorOptions = new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString).Options;
    var roleSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    await using (var setup = new PortalDbContext(operatorOptions))
    {
        await setup.Database.MigrateAsync(); Check((await setup.Database.GetAppliedMigrationsAsync()).Count() == 18, "Isolated SQL schema 18");
        setup.Categories.Add(new() { Id = 76, Name = "Lịch tuần", Slug = "lich-tuan" });
        foreach (var name in new[] { "writer", "reviewer", "publisher", "outsider" }) setup.Users.Add(new() { Id = "weekly-test-" + name, UserName = "weekly-test-" + name, NormalizedUserName = "WEEKLY-TEST-" + name.ToUpperInvariant() });
        foreach (var permission in new[] { Permissions.Create, Permissions.EditOwn, Permissions.ManageMedia, Permissions.Submit }) setup.UserClaims.Add(new() { UserId = "weekly-test-writer", ClaimType = "permission", ClaimValue = permission });
        setup.UserClaims.Add(new() { UserId = "weekly-test-reviewer", ClaimType = "permission", ClaimValue = Permissions.Review });
        setup.UserClaims.Add(new() { UserId = "weekly-test-publisher", ClaimType = "permission", ClaimValue = Permissions.PublishApproved });
        foreach (var permission in new[] { Permissions.Unpublish, Permissions.ViewAll }) setup.UserClaims.Add(new() { UserId = "weekly-test-publisher", ClaimType = "permission", ClaimValue = permission });
        await setup.SaveChangesAsync(); await RuntimeRoleInterceptor.ProvisionAsync(setup, roleSecret);
        // Isolated SQL restore point before import, including a real checksum/header verification.
        var file = Path.Combine(run, "before-import.bak");
        using var command = setup.Database.GetDbConnection().CreateCommand(); await setup.Database.OpenConnectionAsync();
        command.CommandText = "BACKUP DATABASE [LandPortal_Test] TO DISK=@p WITH COPY_ONLY,CHECKSUM,INIT"; var param = command.CreateParameter(); param.ParameterName = "@p"; param.Value = file; command.Parameters.Add(param); await command.ExecuteNonQueryAsync();
        command.CommandText = "RESTORE VERIFYONLY FROM DISK=@p WITH CHECKSUM"; await command.ExecuteNonQueryAsync();
        command.CommandText = "RESTORE HEADERONLY FROM DISK=@p";
        using (var r = await command.ExecuteReaderAsync()) { await r.ReadAsync(); Check((string)r["DatabaseName"] == "LandPortal_Test" && (bool)r["IsCopyOnly"] && (bool)r["HasBackupChecksums"], "Test backup COPY_ONLY/CHECKSUM/HEADERONLY/VERIFYONLY"); }
        await File.WriteAllTextAsync(Path.Combine(run, "backup-sha256.txt"), WeeklyPackage.Hash(await File.ReadAllBytesAsync(file)));
    }
    var runtimeConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Runtime:RolePassword"] = roleSecret }).Build();
    var options = new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString).AddInterceptors(new RuntimeRoleInterceptor(runtimeConfig)).Options;
    var storage = new StorageOptions { MediaRoot = Path.Combine(run, "media"), TempRoot = Path.Combine(run, "temp"), SnapshotRoot = Path.Combine(run, "snapshots"), KeysRoot = Path.Combine(run, "keys") };
    foreach (var path in new[] { storage.MediaRoot, storage.TempRoot, storage.SnapshotRoot, storage.KeysRoot }) Directory.CreateDirectory(path);
    // Test-only DI, no operational profile or private configuration is loaded or relaxed.
    var policy = new MediaUploadOptions { ApprovedManagedDocumentsWithoutAv = true };
    await using var db = new PortalDbContext(options);
    // One matching record already exists before the batch, created through the same CMS services.
    var existingRow = package.Rows[42]; var fixtureRights = new CurrentPermissionService(db);
    var existingDraft = new DraftInput { Kind = ContentKind.StaticPage, PrimaryCategoryId = 76, Title = existingRow.Title, BodyHtml = "<p>Hồ sơ có sẵn, không được ghi đè.</p>" };
    var existingWorkflow = new WorkflowService(db, new HtmlPolicy(), policy, storage);
    var existing = await existingWorkflow.SaveAsync(null, existingDraft, await fixtureRights.GetAsync("weekly-test-writer"), null, Guid.NewGuid());
    using (var input = new MemoryStream(package.PdfBytes[43]))
    {
        var existingAsset = await new EditorialDocumentService(db, storage, new MediaService(db, storage, policy), fixtureRights, policy)
            .UploadAsync(existing.ContentId, input, existingRow.FileName, "application/pdf", await fixtureRights.GetAsync("weekly-test-writer"));
        existingDraft.Assets.Add(new() { MediaAssetId = existingAsset.Id, Usage = "Attachment", Caption = existingRow.FileName });
        existing = await existingWorkflow.SaveAsync(existing.ContentId, existingDraft, await fixtureRights.GetAsync("weekly-test-writer"), existing.RowVersion, Guid.NewGuid());
    }
    var importer = new WeeklyImporter(db, storage, policy, "weekly-test-writer");
    var plan = await importer.PlanAsync(package);
    Check(plan.Target.Principal == "LandPortalRuntime" && plan.Rows.Count(r => r.Action == "Create") == 42 && plan.Rows[42].Action == "Reuse", "Runtime role and dry-run: 42 new + 1 exact existing match, no writes");
    Check(await db.Contents.CountAsync() == 1 && await db.MediaAssets.CountAsync() == 1, "Dry-run did not write SQL/media");
    await WeeklyImporter.AtomicJson(Path.Combine(run, "plan.json"), plan);
    await Denied(() => new WeeklyImporter(db, storage, policy, "weekly-test-outsider").PlanAsync(package), "CMS account without permissions denied");
    await Denied(() => importer.ImportAsync(package, plan with { ZipSha256 = new string('0', 64) }, Path.Combine(run, "receipt.json")), "Changed plan/source binding denied before writes");
    await Denied(() => importer.ImportAsync(package, plan with { MediaRoot = storage.TempRoot }, Path.Combine(run, "receipt.json")), "Changed storage root denied before writes");
    await Denied(() => new WeeklyImporter(db, storage, new MediaUploadOptions(), "weekly-test-writer").ImportAsync(package, plan, Path.Combine(run, "scanner-receipt.json")), "Missing scanner configuration cannot start imports when no-AV is off");
    await Denied(() => importer.ImportAsync(package, plan, Path.Combine(run, "receipt.json"), (stt, stage) => stt == 2 && stage == "Uploaded" ? Task.FromException(new IOException("test interruption")) : Task.CompletedTask), "Controlled interruption after committed PDF upload");
    Check(await db.Contents.CountAsync() == 3 && await db.MediaAssets.CountAsync() == 3, "Interrupted import kept two durable drafts/files plus unchanged existing record");
    var reconciled = await importer.ReconcileAsync(package);
    Check(reconciled.Rows.Count == 3 && reconciled.Rows.Single(r => r.Stt == 2).Outcome == "Resume", "Reconcile reconstructs mapping from CMS commands without receipt trust");
    ImportReceipt receipt;
    await using (var restarted = new PortalDbContext(options))
        receipt = await new WeeklyImporter(restarted, storage, policy, "weekly-test-writer").ImportAsync(package, plan, Path.Combine(run, "receipt.json"));
    Check(receipt.Rows.Count == 43 && await db.Contents.CountAsync() == 43 && await db.MediaAssets.CountAsync() == 43, "Resume imported exactly 43 drafts and 43 PDFs");
    Check(receipt.Rows[42].ContentId == existing.ContentId && (await db.Contents.AsNoTracking().SingleAsync(x => x.Id == existing.ContentId)).RowVersion.SequenceEqual(existing.RowVersion), "Pre-existing matching record reused byte-for-byte without editing");
    Check(await db.Revisions.CountAsync() == 86 && await db.ContentCommands.CountAsync() == 86, "Two workflow revisions/commands per new record; no raw inserts");
    Check(await db.Contents.AllAsync(x => !x.IsPublic && x.State == WorkflowState.Draft && x.PublishedAtUtc == null && x.SourcePublishedDate == null), "All imported records remain drafts; Excel dates never become publication dates");
    Check(await db.WeeklyScheduleReleases.CountAsync() == 0 && await db.PublicationJobs.CountAsync() == 0, "No schedule release, replacement or publication job created");
    Check(await db.MediaAssets.AllAsync(x => x.ScanStatus == "NotScanned" && x.ValidationStatus == MediaUploadOptions.ManagedDocumentNoAvValidation), "Honest NotScanned status with existing technical PDF validation");
    foreach (var row in package.Rows)
    {
        var map = receipt.Rows.Single(x => x.Stt == row.Stt); var c = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == map.ContentId);
        var rev = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == c.CurrentRevisionId); var asset = await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == map.MediaId);
        if (rev.Title != row.Title || c.Kind != ContentKind.StaticPage || rev.PrimaryCategoryId != 76 || asset.OriginalName != row.FileName || asset.Sha256 != row.PdfSha256
            || WeeklyPackage.Hash(await File.ReadAllBytesAsync(storage.ResolveMedia(asset.StorageKey))) != row.PdfSha256) throw new Exception("Exact record/PDF mismatch " + row.Stt);
    }
    Check(true, "43/43 titles, original names, bytes, SHA256, category and attachment mappings exact");
    await importer.ImportAsync(package, plan, Path.Combine(run, "receipt.json"));
    Check(await db.Contents.CountAsync() == 43 && await db.Revisions.CountAsync() == 86 && await db.MediaAssets.CountAsync() == 43, "Repeated import creates no duplicate content/revision/media");
    await Denied(() => importer.ImportAsync(package, plan with { Target = plan.Target with { Database = "wrong" } }, Path.Combine(run, "receipt.json")), "Wrong target database denied");

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = Path.Combine(root, "src", "Web"), WebRootPath = Path.Combine(root, "src", "Web", "wwwroot"), EnvironmentName = "Staging" });
    builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Public:BaseUrl"] = "http://127.0.0.1/" });
    builder.Logging.ClearProviders(); builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning); builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddScoped(_ => new PortalDbContext(options)); builder.Services.AddSingleton(storage); builder.Services.AddSingleton(policy); builder.Services.AddSingleton(new VideoOptions());
    builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()); builder.Services.AddHttpContextAccessor(); builder.Services.AddMemoryCache();
    builder.Services.AddScoped<PublicContentQuery>(); builder.Services.AddScoped<PublicSiteService>(); builder.Services.AddScoped<PublicNavigation>(); builder.Services.AddScoped<SiteConfigurationService>();
    builder.Services.AddScoped<CurrentPermissionService>(); builder.Services.AddScoped<ArticleDiscovery>(); builder.Services.AddScoped<DocumentUsage>(); builder.Services.AddScoped<PublicTraffic>(); builder.Services.AddSingleton<PublicUrlPolicy>();
    builder.Services.AddScoped<InteractionService>(); builder.Services.AddSingleton(new InteractionMailOptions());
    builder.Services.AddAuthentication(); builder.Services.AddAuthorization();
    builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PortalController).Assembly);
    app = builder.Build(); app.Services.GetRequiredService<IMemoryCache>().Set<PublicTrafficCounts?>("public-traffic-counts", null);
    app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
    var publicBase = new Uri(app.Urls.Single()); using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };
    var sync = new LinkSync(db, policy, http, publicBase);
    var links = await sync.VerifyAsync(package, receipt, plan.Target);
    Check(links.All(x => x.Url == null), "Drafts export zero links, no HTTP publication implied");
    var workflow = new WorkflowService(db, new HtmlPolicy(), policy, storage); var rights = new CurrentPermissionService(db);
    foreach (var stt in new[] { 1, 2 })
    {
        db.ChangeTracker.Clear(); var id = receipt.Rows.Single(x => x.Stt == stt).ContentId; var c = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == id);
        var submitted = await workflow.SubmitAsync(id, c.CurrentRevisionId, await rights.GetAsync("weekly-test-writer"), c.RowVersion, Guid.NewGuid());
        var approved = await workflow.ApproveAsync(id, submitted.CurrentRevisionId!.Value, await rights.GetAsync("weekly-test-reviewer"), submitted.RowVersion, Guid.NewGuid());
        await workflow.PublishApprovedAsync(id, approved.CurrentRevisionId!.Value, await rights.GetAsync("weekly-test-publisher"), approved.RowVersion, Guid.NewGuid());
    }
    Check(await db.Contents.CountAsync(x => x.IsPublic) == 2, "Test-only Writer submit, Reviewer approve, Publisher publish two revisions");
    links = await sync.VerifyAsync(package, receipt, plan.Target);
    Check(links.Count(x => x.Url != null) == 2 && links.Take(2).All(x => x.Status == "PublishedVerified"), "Actual article HTTP + public PDF bytes/hash verified before link export");
    var linked = Path.Combine(run, "linked.xlsx"); var verifiedLinks = links.ToDictionary(x => x.Stt, x => x.Url); package.WriteWorkbook(linked, verifiedLinks); CheckWorkbook(package, linked, verifiedLinks);
    var secondLinks = await sync.VerifyAsync(package, receipt, plan.Target); var again = Path.Combine(run, "linked-again.xlsx"); package.WriteWorkbook(again, secondLinks.ToDictionary(x => x.Stt, x => x.Url));
    using (var a = ZipFile.OpenRead(linked)) using (var b = ZipFile.OpenRead(again)) Check(a.Entries.All(e => WeeklyPackage.ReadEntry(e).SequenceEqual(WeeklyPackage.ReadEntry(b.GetEntry(e.FullName)!))), "Repeated Sync Links has identical workbook contents and no SQL changes");
    await Denied(() => sync.VerifyAsync(package, receipt with { Rows = receipt.Rows.Select(x => x.Stt == 1 ? x with { ContentId = receipt.Rows[1].ContentId } : x).ToArray() }, plan.Target), "Corrupted/duplicate mapping cannot export another article URL");
    // Withdraw through the existing CMS workflow, then ensure the new copy removes the old link.
    db.ChangeTracker.Clear(); var first = await db.Contents.SingleAsync(x => x.Id == receipt.Rows[0].ContentId);
    var publisher = await rights.GetAsync("weekly-test-publisher");
    await workflow.UnpublishAsync(first.Id, publisher, first.RowVersion, Guid.NewGuid());
    links = await sync.VerifyAsync(package, receipt, plan.Target);
    Check(links[0].Url == null && links[1].Url != null, "Withdrawn record cannot retain a URL in next Excel output");
    await Denied(() => new WeeklyImporter(db, storage, new MediaUploadOptions(), "weekly-test-writer").ImportAsync(package, plan, Path.Combine(run, "scanner-receipt.json")), "Scanner/no-AV policy disabled is not bypassed");
    db.ChangeTracker.Clear(); var removed = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == receipt.Rows[2].ContentId);
    var removedDraft = await db.ContentDrafts.AsNoTracking().SingleAsync(x => x.ContentId == removed.Id);
    await workflow.SaveAsync(removed.Id, new DraftInput { Kind = ContentKind.StaticPage, PrimaryCategoryId = 76, Title = removedDraft.Title, BodyHtml = removedDraft.BodyHtml }, await rights.GetAsync("weekly-test-writer"), removed.RowVersion, Guid.NewGuid());
    await Denied(() => importer.ImportAsync(package, plan, Path.Combine(run, "receipt.json")), "User-removed attachment is never silently reattached on retry");
    Check(!await db.DraftAssets.AnyAsync(x => x.ContentId == removed.Id), "Editor removal preserved after denied retry");
    Check(await db.Users.CountAsync() == 4 && await db.WeeklyScheduleReleases.CountAsync() == 0 && sourceHash == WeeklyPackage.Hash(await File.ReadAllBytesAsync(zip)), "Source ZIP, test identities and schedule releases preserved");
    await WeeklyImporter.AtomicJson(Path.Combine(run, "result.json"), new { passed = checks.Count, checks, instance, operationalSqlTouched = false });
    Console.WriteLine("RESULT " + checks.Count + " PASS; " + run);
}
catch (Exception e) { await File.WriteAllTextAsync(Path.Combine(run, "failure.txt"), e.ToString()); Console.WriteLine("FAIL " + e.GetType().Name + "; details in isolated run/failure.txt"); Environment.ExitCode = 1; }
finally { if (app != null) { await app.StopAsync(); await app.DisposeAsync(); } try { await LocalDb("stop"); } catch { } }

void CheckWorkbook(WeeklyPackage package, string path, IReadOnlyDictionary<int, string?> links)
{
    using var original = new ZipArchive(new MemoryStream(package.Workbook)); using var output = ZipFile.OpenRead(path);
    var relPath = Path.GetDirectoryName(package.SheetPath)!.Replace('\\', '/') + "/_rels/" + Path.GetFileName(package.SheetPath) + ".rels";
    Check(original.Entries.Where(e => e.FullName != package.SheetPath && e.FullName != relPath).All(e => WeeklyPackage.ReadEntry(e).SequenceEqual(WeeklyPackage.ReadEntry(output.GetEntry(e.FullName)!))), "Other sheets, styles, formulas, drawings and workbook structure byte-identical");
    var before = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(original.GetEntry(package.SheetPath)!)); var after = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(output.GetEntry(package.SheetPath)!)); var s = WeeklyPackage.S;
    var allowed = package.Rows.Where(r => r.Pages != r.ExcelPages).Select(r => "E" + r.ExcelRow).Concat(links.Keys.Select(k => "D" + (k + 6))).ToHashSet();
    Check(before.Descendants(s + "c").Where(c => !allowed.Contains((string)c.Attribute("r")!)).All(c => XNode.DeepEquals(c, after.Descendants(s + "c").Single(x => (string?)x.Attribute("r") == (string?)c.Attribute("r")))), "Every untouched cell including all A/B/C values/types/formats preserved exactly");
    Check(after.Descendants(s + "c").Single(c => (string?)c.Attribute("r") == "E48").Element(s + "v")!.Value == "3", "Output E48 equals actual PDF page count");
    if (links.Values.Any(x => x != null))
    {
        var rels = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(output.GetEntry(relPath)!));
        foreach (var (stt, url) in links.Where(x => x.Value != null))
        {
            var hyperlink = after.Descendants(s + "hyperlink").Single(x => (string?)x.Attribute("ref") == "D" + (stt + 6));
            var relation = rels.Root!.Elements().Single(x => (string?)x.Attribute("Id") == (string?)hyperlink.Attribute(WeeklyPackage.R + "id"));
            if ((string?)relation.Attribute("Target") != url || (string?)relation.Attribute("TargetMode") != "External" || (string?)relation.Attribute("Type") != WeeklyPackage.R.NamespaceName + "/hyperlink") throw new Exception("Hyperlink mismatch");
        }
        Check(true, "Published URLs are clickable relationships on their exact STT rows");
    }
}
