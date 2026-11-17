using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using LandPortal.Web.Services;
using LandPortal.WeeklyImport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

if (args.Length != 2) throw new ArgumentException("source ZIP and new test output required");
var root = Directory.GetCurrentDirectory(); var run = Path.GetFullPath(args[1]);
if (!run.StartsWith(Path.Combine(root, ".local", "tests", "weekly-web-"), StringComparison.OrdinalIgnoreCase) || Directory.Exists(run)) throw new ArgumentException("New isolated test folder required");
Directory.CreateDirectory(run);
var instance = "LPWeeklyWeb_" + Guid.NewGuid().ToString("N")[..12];
var checks = new List<string>(); WebApplication? app = null;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); Console.WriteLine("PASS " + name); }
async Task Denied(Func<Task> action, string name, string? expected = null) { try { await action(); } catch (Exception e) when (e is InvalidDataException or UnauthorizedAccessException or ArgumentException) { if(expected != null && !e.Message.Contains(expected,StringComparison.Ordinal)) throw new Exception("Unexpected rejection: "+name+": "+e.Message,e); Check(true, name); return; } throw new Exception("NOT DENIED " + name); }
async Task LocalDb(string operation)
{
    var psi = new ProcessStartInfo("SqlLocalDB.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    psi.ArgumentList.Add(operation); psi.ArgumentList.Add(instance); using var p = Process.Start(psi)!;
    var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync(); await output; await error;
    if (p.ExitCode != 0) throw new Exception("LocalDB " + operation + " failed");
}
try
{
    var package = WeeklyPackage.Read(args[0]); var first = package.Rows[0];
    Check(package.Rows.Count == 43 && package.Rows.Sum(x => x.Pages) == 120 && package.Rows.Single(x => x.Stt == 42).Pages == 3, "43 source PDFs / 120 pages / STT42 3 pages");
    await LocalDb("create"); await LocalDb("start");
    var cs = new SqlConnectionStringBuilder { DataSource = @"(localdb)\" + instance, InitialCatalog = "LandPortal_Test", IntegratedSecurity = true, Encrypt = true, TrustServerCertificate = true, Pooling = false };
    await using (var sql = new SqlConnection(new SqlConnectionStringBuilder(cs.ConnectionString) { InitialCatalog = "master" }.ConnectionString))
    {
        await sql.OpenAsync(); using var cmd = sql.CreateCommand(); cmd.CommandText = "SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB')),CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),DB_ID('LandPortal_Test')";
        using (var r = await cmd.ExecuteReaderAsync()) { await r.ReadAsync(); Check(r.GetInt32(0) == 1 && r.GetString(1).Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) && r.IsDBNull(2), "New local-only SQL instance, operational databases not accessed"); }
        cmd.CommandText = "CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'" + Path.Combine(run, "test.mdf").Replace("'", "''") + "') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'" + Path.Combine(run, "test.ldf").Replace("'", "''") + "')"; await cmd.ExecuteNonQueryAsync();
    }
    var setupOptions = new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString).Options;
    var roleSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    await using (var setup = new PortalDbContext(setupOptions))
    {
        await setup.Database.MigrateAsync(); Check((await setup.Database.GetAppliedMigrationsAsync()).Count() == 18, "Test schema18");
        setup.Categories.Add(new() { Id = 76, Name = "Lịch tuần", Slug = "lich-tuan" }); await setup.SaveChangesAsync();
        await RuntimeRoleInterceptor.ProvisionAsync(setup, roleSecret);
    }
    var storage = new StorageOptions { MediaRoot = Path.Combine(run,"media"), TempRoot = Path.Combine(run,"temp"), KeysRoot = Path.Combine(run,"keys"), SnapshotRoot = Path.Combine(run,"snapshots") };
    foreach (var dir in new[] { storage.MediaRoot, storage.TempRoot, storage.KeysRoot, storage.SnapshotRoot }) Directory.CreateDirectory(dir);
    var config = new Dictionary<string,string?> { ["ConnectionStrings:Portal"] = cs.ConnectionString, ["Sql:ExpectedDatabase"] = "LandPortal_Test", ["Runtime:RolePassword"] = roleSecret,
        ["Storage:MediaRoot"] = storage.MediaRoot, ["Storage:TempRoot"] = storage.TempRoot, ["Storage:KeysRoot"] = storage.KeysRoot, ["Storage:SnapshotRoot"] = storage.SnapshotRoot,
        ["Mail:Transport"] = "Disabled", ["WorkspaceRoot"] = root, ["Public:BaseUrl"] = "http://127.0.0.1/", ["DOTNET_ENVIRONMENT"]="Development", ["ASPNETCORE_ENVIRONMENT"]="Development" };
    var runtimeConfig = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
    var options = new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString).AddInterceptors(new RuntimeRoleInterceptor(runtimeConfig)).Options;
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(AdminController).Assembly.FullName, ContentRootPath = Path.Combine(root,"src","Web"), EnvironmentName = "Development" });
    builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(config); builder.Logging.ClearProviders(); builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning); builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddScoped(_ => new PortalDbContext(options)); builder.Services.AddSingleton(storage);
    var policy = new MediaUploadOptions { ApprovedManagedDocumentsWithoutAv = true }; builder.Services.AddSingleton(policy); // Test DI only; no operational guard changed.
    builder.Services.AddSingleton(new VideoOptions()); builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
    builder.Services.AddIdentity<PortalUser,IdentityRole>(PortalIdentityPolicy.Configure).AddEntityFrameworkStores<PortalDbContext>().AddDefaultTokenProviders();
    builder.Services.ConfigureApplicationCookie(o => { o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; }; o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; }; });
    builder.Services.AddSingleton(new PublicCaptchaOptions { AddressKey = RandomNumberGenerator.GetBytes(32), AttemptsPerWindow = 20 }); builder.Services.AddScoped<PublicCaptchaService>();
    builder.Services.AddScoped<CurrentPermissionService>(); builder.Services.AddScoped<WorkflowService>(); builder.Services.AddSingleton<HtmlPolicy>(); builder.Services.AddScoped<MediaService>(); builder.Services.AddScoped<EditorialDocumentService>();
    builder.Services.AddScoped<IAuthorizationHandler,FreshPermissionHandler>(); builder.Services.AddAuthorization(o => o.AddPolicy("EditorAccess", p => p.RequireAuthenticatedUser().AddRequirements(new FreshPermissionRequirement(null))));
    builder.Services.AddAntiforgery(); builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("login", l => { l.PermitLimit = 20; l.Window = TimeSpan.FromMinutes(1); }));
    builder.Services.AddHttpContextAccessor(); builder.Services.AddMemoryCache();
    builder.Services.AddScoped<PublicContentQuery>(); builder.Services.AddScoped<PublicSiteService>(); builder.Services.AddScoped<PublicNavigation>(); builder.Services.AddScoped<SiteConfigurationService>();
    builder.Services.AddScoped<ArticleDiscovery>(); builder.Services.AddScoped<DocumentUsage>(); builder.Services.AddScoped<PublicTraffic>(); builder.Services.AddSingleton<PublicUrlPolicy>(); builder.Services.AddScoped<InteractionService>(); builder.Services.AddSingleton(new InteractionMailOptions());
    builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddApplicationPart(typeof(AdminController).Assembly);
    app = builder.Build(); app.Services.GetRequiredService<IMemoryCache>().Set<PublicTrafficCounts?>("public-traffic-counts", null);
    var accounts = new Dictionary<string,(string Id,string Password)>();
    using (var scope = app.Services.CreateScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        foreach (var (name, rights) in new[] { ("writer",new[] { Permissions.Create, Permissions.EditOwn, Permissions.ViewAll, Permissions.ManageMedia, Permissions.Submit, Permissions.Trash }), ("reviewer",new[] {Permissions.Review}), ("publisher",new[] {Permissions.PublishApproved,Permissions.Unpublish}), ("outsider",new[] {Permissions.ViewOwn}) })
        {
            var user = new PortalUser { UserName = "web-weekly-"+name, Email = name+"@example.invalid", EmailConfirmed = true, LockoutEnabled = true }; var password = PortalIdentityPolicy.NewTemporaryPassword();
            Check((await users.CreateAsync(user,password)).Succeeded && (await users.AddClaimsAsync(user,rights.Select(p=>new Claim("permission",p)))).Succeeded, "Synthetic Identity "+name); accounts[name]=(user.Id,password);
        }
    }
    string? loseResponse = null; bool corruptDownload = false;
    app.Use(async (context,next) =>
    {
        var lose = context.Request.Method == "POST" && (loseResponse == "save" && context.Request.Path == "/admin/save" || loseResponse == "upload" && Regex.IsMatch(context.Request.Path,@"^/admin/documents/[a-f0-9-]+$"));
        if (!lose && !(corruptDownload && context.Request.Path.StartsWithSegments("/admin/documents/file"))) { await next(); return; }
        var original = context.Response.Body; using var captured = new MemoryStream(); context.Response.Body = captured; await next(); context.Response.Body = original;
        if (lose && context.Response.StatusCode != 200) { await File.WriteAllBytesAsync(Path.Combine(run,"unexpected-response.json"),captured.ToArray()); captured.Position=0; await captured.CopyToAsync(original); }
        else if (lose) { loseResponse = null; context.Response.Clear(); context.Response.StatusCode = 503; await context.Response.WriteAsync("Simulated lost response after commit"); }
        else { context.Response.ContentLength = 4; await context.Response.Body.WriteAsync(new byte[]{1,2,3,4}); }
    });
    app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
    using var cms = new WebCmsClient(new Uri(app.Urls.Single()), true); using var outsider = new WebCmsClient(cms.Origin,true);
    Task<(string,string,string)> Answer(string who,string question) { var n=Regex.Matches(question,@"\d+").Select(x=>int.Parse(x.Value)).ToArray(); if(n.Length!=2||!question.Contains("cộng")&&!question.Contains("trừ"))throw new Exception("Unknown test CAPTCHA"); return Task.FromResult(("web-weekly-"+who,accounts[who].Password,(question.Contains("cộng")?n[0]+n[1]:n[0]-n[1]).ToString())); }
    await cms.LoginAsync(q=>Answer("writer",q)); Check(cms.UserId == accounts["writer"].Id,"Real HTTP Identity/CAPTCHA/CSRF login; no stored password/cookie");
    var importer = new WebWeeklyImporter(cms); await importer.PreflightAsync(); Check(true,"Actual Admin create form, category76 and PDF capability");
    await outsider.LoginAsync(q=>Answer("outsider",q)); await Denied(()=>new WebWeeklyImporter(outsider).PreflightAsync(),"Insufficient CMS permission rejected before create/upload");
    var plan = await importer.PlanAsync(package); Check(plan.Rows.Count(x=>x.Action=="Create")==43,"Read-only HTTP dry-run plans 43 drafts");
    var journal = new WebJournal(plan.Origin,plan.UserId,plan.ZipSha256,plan.WorkbookSha256,[]); var journalPath=Path.Combine(run,"web-journal.json");
    await Denied(()=>importer.ImportAsync(package,plan,journal,journalPath,true),"Batch43 refused before a verified pilot");
    loseResponse="save"; await Denied(()=>importer.ImportAsync(package,plan,journal,journalPath,false),"Lost draft response: stops with Creating journal","CMS HTTP 503");
    await using var db = new PortalDbContext(options);
    Check(await db.Contents.CountAsync()==1 && !await db.MediaAssets.AnyAsync(),"Only one draft committed, no PDF yet");
    journal=JsonSerializer.Deserialize<WebJournal>(await File.ReadAllBytesAsync(journalPath))!;
    loseResponse="upload"; await Denied(()=>importer.ImportAsync(package,plan,journal,journalPath,false),"Resume draft command then lost PDF response","CMS HTTP 503");
    Check(await db.Contents.CountAsync()==1 && await db.Revisions.CountAsync()==1 && await db.MediaAssets.CountAsync()==1,"No duplicate draft; one actual PDF stored by CMS");
    journal=JsonSerializer.Deserialize<WebJournal>(await File.ReadAllBytesAsync(journalPath))!;
    loseResponse="save"; await Denied(()=>importer.ImportAsync(package,plan,journal,journalPath,false),"Find upload by hash then lost attach response","CMS HTTP 503");
    Check(await db.MediaAssets.CountAsync()==1 && await db.DraftAssets.CountAsync()==1,"PDF receipt reused, no duplicate upload");
    journal=JsonSerializer.Deserialize<WebJournal>(await File.ReadAllBytesAsync(journalPath))!;
    await importer.ImportAsync(package,plan,journal,journalPath,false); await importer.ReconcileAsync(package,plan,journal);
    Check(journal.Rows.Single().Stage=="Verified" && await db.Revisions.CountAsync()==2,"Restarted client replays exact attach command, one verified draft");
    await importer.ImportAsync(package,plan,journal,journalPath,false);
    Check(await db.Contents.CountAsync()==1 && await db.MediaAssets.CountAsync()==1 && await db.Revisions.CountAsync()==2,"Repeated pilot has zero content/media/revision delta");
    var media=await db.MediaAssets.SingleAsync(); Check(media.ScanStatus=="NotScanned" && media.Sha256==first.PdfSha256 && media.OriginalName==first.FileName,"Original PDF bytes/name/hash; truthful NotScanned");
    Check((await importer.PlanAsync(package)).Rows[0].Action=="Reuse","Existing exact title/PDF reused via Admin inventory");
    var links=await importer.SyncAsync(package,plan,journal); Check(links.All(x=>x.Url==null),"No link written for draft or remaining42");
    package.WriteWorkbook(Path.Combine(run,"draft-links.xlsx"),links.ToDictionary(x=>x.Stt,x=>x.Url));
    corruptDownload=true; await Denied(()=>importer.ReconcileAsync(package,plan,journal),"HTTP PDF SHA mismatch rejected"); corruptDownload=false;
    var rightsService=new CurrentPermissionService(db); var workflow=new WorkflowService(db,new HtmlPolicy(),policy,storage);
    var writer=await rightsService.GetAsync(accounts["writer"].Id);var reviewer=await rightsService.GetAsync(accounts["reviewer"].Id);var publisher=await rightsService.GetAsync(accounts["publisher"].Id);
    var content=await db.Contents.AsNoTracking().SingleAsync();
    var submitted=await workflow.SubmitAsync(content.Id,content.CurrentRevisionId,writer,content.RowVersion,Guid.NewGuid());
    var approved=await workflow.ApproveAsync(content.Id,submitted.CurrentRevisionId!.Value,reviewer,submitted.RowVersion,Guid.NewGuid());
    await workflow.PublishApprovedAsync(content.Id,approved.CurrentRevisionId!.Value,publisher,approved.RowVersion,Guid.NewGuid());
    links=await importer.SyncAsync(package,plan,journal); Check(links[0].Url!=null && links.Count(x=>x.Url!=null)==1,"Synthetic review/publication: anonymous page+PDF200 and published revision verified");
    package.WriteWorkbook(Path.Combine(run,"published-links.xlsx"),links.ToDictionary(x=>x.Stt,x=>x.Url));
    db.ChangeTracker.Clear();content=await db.Contents.AsNoTracking().SingleAsync();await workflow.UnpublishAsync(content.Id,publisher,content.RowVersion,Guid.NewGuid());
    links=await importer.SyncAsync(package,plan,journal); Check(links.All(x=>x.Url==null),"Withdrawn published link cleared on next Sync");
    db.ChangeTracker.Clear();content=await db.Contents.AsNoTracking().SingleAsync();
    var editing=await workflow.BeginEditAsync(content.Id,writer,content.RowVersion,Guid.NewGuid());
    await workflow.SaveAsync(content.Id,new DraftInput{Kind=ContentKind.StaticPage,Title=first.Title,PrimaryCategoryId=76,BodyHtml="<p>Human edit: PDF intentionally removed.</p>"},writer,editing.RowVersion,Guid.NewGuid());
    await Denied(()=>importer.ImportAsync(package,plan,journal,journalPath,false),"Human-removed PDF is not reattached on resume");
    Check(!await db.DraftAssets.AnyAsync() && !await db.WeeklyScheduleReleases.AnyAsync(),"No overwrite of removal, no WeeklyScheduleRelease created");
    db.ChangeTracker.Clear(); content=await db.Contents.AsNoTracking().SingleAsync(); await workflow.TrashAsync(content.Id,writer,content.RowVersion,Guid.NewGuid());
    await Denied(()=>importer.PlanAsync(package),"Trash title collision blocks recreation/reuse","thùng rác");
    await Denied(()=>Task.Run(()=>new WebCmsClient(new Uri("http://gdla.gov.vn/"))),"Production client requires HTTPS");
    await Denied(()=>Task.Run(()=>cms.Url("https://example.invalid/admin/save")),"Cross-origin requests refused");
    Check(!File.ReadAllText(journalPath).Contains(accounts["writer"].Password,StringComparison.Ordinal)&&!File.ReadAllText(journalPath).Contains("__RequestVerificationToken",StringComparison.Ordinal),"Journal excludes password and CSRF");
    await WeeklyImporter.AtomicJson(Path.Combine(run,"result.json"),new{passed=checks.Count,checks,instance,operationalSqlTouched=false}); Console.WriteLine("RESULT "+checks.Count+" PASS; "+run);
}
catch(Exception e) { await File.WriteAllTextAsync(Path.Combine(run,"failure.txt"),e.ToString());Console.WriteLine("FAIL "+e.GetType().Name+"; isolated failure.txt");Environment.ExitCode=1; }
finally { if(app!=null){await app.StopAsync();await app.DisposeAsync();}try{await LocalDb("stop");}catch{} }
