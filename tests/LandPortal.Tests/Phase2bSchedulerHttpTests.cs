using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;

public static class Phase2bSchedulerHttpTests
{
    public static async Task RunAsync(string root, IConfiguration config, DbContextOptions<PortalDbContext> options)
    {
        if (new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(DatabaseSettings.Connection(config)).InitialCatalog != "LandPortal_Test") throw new InvalidOperationException("Scheduler HTTP requires SQL Test.");
        var storage = StorageOptions.FromConfiguration(config); if (!storage.MediaRoot.StartsWith(Path.Combine(root, ".local", "tests"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Scheduler HTTP requires isolated media storage.");
        var password = Guid.NewGuid().ToString("N") + "Aa!7";
        PortalUser User() { var name = "QA-scheduler-HTTP-" + Guid.NewGuid().ToString("N"); var user = new PortalUser { UserName = name, NormalizedUserName = name.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true }; user.PasswordHash = new PasswordHasher<PortalUser>().HashPassword(user, password); return user; }
        var admin = User(); var writer = User(); Guid contentId; Guid revisionId; string title;
        await using (var seed = new PortalDbContext(options))
        {
            foreach (var (user, rights) in new[] { (admin, Permissions.All), (writer, new[] { Permissions.Create, Permissions.EditOwn, Permissions.ViewOwn }) })
            { seed.Users.Add(user); foreach (var value in rights) seed.UserClaims.Add(new() { UserId = user.Id, ClaimType = "permission", ClaimValue = value }); }
            await seed.SaveChangesAsync(); var actor = await new CurrentPermissionService(seed).GetAsync(admin.Id);
            title = "QA scheduler UI " + Guid.NewGuid().ToString("N"); var content = await new WorkflowService(seed, new HtmlPolicy(), storage: storage).SaveAsync(null, Phase2aSqlTests.Input(title), actor, null, Guid.NewGuid()); contentId = content.ContentId; revisionId = content.CurrentRevisionId!.Value;
        }
        const string baseUrl = "http://127.0.0.1:5195"; var checks = new List<object>(); var evidence = TestEvidencePaths.At(root, "phase2b", "scheduler"); Directory.CreateDirectory(evidence);
        ProcessStartInfo Dotnet(string dll) { var info = new ProcessStartInfo(Path.Combine(root, ".local", "dotnet", "dotnet.exe")) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }; info.ArgumentList.Add(dll); info.Environment["Captcha__TestScope"]=Guid.NewGuid().ToString("N");info.Environment["Captcha__AttemptsPer10Minutes"]="20";info.Environment["Public__BaseUrl"] = baseUrl; info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"; info.Environment["DOTNET_ENVIRONMENT"] = "Development"; info.Environment["ConnectionStrings__Portal"] = DatabaseSettings.Connection(config); info.Environment["Sql__ExpectedDatabase"] = "LandPortal_Test"; info.Environment["Media__AllowUnscannedDevelopmentUploads"] = "true"; info.Environment["Storage__MediaRoot"] = storage.MediaRoot; info.Environment["Storage__SnapshotRoot"] = storage.SnapshotRoot; info.Environment["Storage__TempRoot"] = storage.TempRoot; info.Environment["Storage__KeysRoot"] = storage.KeysRoot; return info; }
        using var web = Process.Start(Dotnet(TestRuntimePaths.Assembly(root,"Web"))) ?? throw new InvalidOperationException("Scheduler web could not start."); var output = web.StandardOutput.ReadToEndAsync(); var error = web.StandardError.ReadToEndAsync();
        HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        using var guest = Client(); using var administrator = Client(); using var editor = Client();
        async Task<Dictionary<string, string>> Form(HttpClient client, string path) { var doc = new HtmlParser().ParseDocument(await client.GetStringAsync(path)); var token = doc.QuerySelector("input[name=__RequestVerificationToken]")?.GetAttribute("value") ?? throw new InvalidOperationException("CSRF missing."); var form = new Dictionary<string,string> { ["__RequestVerificationToken"] = token }; foreach (var input in doc.QuerySelectorAll(".scheduler-form input[name]")) form[input.GetAttribute("name")!] = input.GetAttribute("value") ?? ""; CaptchaHttp.Add(doc, form); return form; }
        async Task Login(HttpClient client, PortalUser user) { var values = await Form(client, "/admin/login"); values["UserName"] = user.UserName!; values["Password"] = password; using var response = await client.PostAsync("/admin/login", new FormUrlEncodedContent(values)); Assert(response.StatusCode == HttpStatusCode.Redirect, "Scheduler Identity login failed."); }
        async Task Check(string name, Func<Task> run) { await run(); checks.Add(new { name, status = "PASS" }); Console.WriteLine("HTTP 2B scheduler PASS " + name); }
        try
        {
            var ready = false; for (var i = 0; i < 150; i++) { if (web.HasExited) throw new InvalidOperationException("Scheduler web exited early."); try { using var response = await guest.GetAsync("/ready"); if (response.IsSuccessStatusCode) { ready = true; break; } } catch (HttpRequestException) { } await Task.Delay(100); } Assert(ready, "Scheduler web not ready.");
            await Login(administrator, admin); await Login(editor, writer);
            await Check("Guest and editor cannot read/write schedules; sensitive mutation requires CSRF", async () =>
            {
                foreach (var client in new[] { guest, editor }) { using var denied = await client.GetAsync("/admin/schedules"); Assert(Denied(denied), "Unprivileged principal listed schedules."); var token = await Form(client, client == guest ? "/admin/login" : "/admin/create"); token["ContentId"] = contentId.ToString(); using var blocked = await client.PostAsync("/admin/schedules/save", new FormUrlEncodedContent(token)); Assert(Denied(blocked), "Unprivileged schedule mutation was accepted."); }
                using var csrf = await administrator.PostAsync("/admin/schedules/save", new FormUrlEncodedContent(new Dictionary<string,string> { ["ContentId"] = contentId.ToString() })); Assert(csrf.StatusCode == HttpStatusCode.BadRequest, "Schedule accepted missing CSRF.");
            });
            await Check("Real keyboard/mobile/desktop UI creates and cancels SQL schedules", async () =>
            {
                var fixture = Path.Combine(root, ".local", "tests", "phase2b", "scheduler-ui.json"); Directory.CreateDirectory(Path.GetDirectoryName(fixture)!); await File.WriteAllTextAsync(fixture, JsonSerializer.Serialize(new { database = "LandPortal_Test", baseUrl, user = admin.UserName, password, contentId, title }));
                var info = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }; info.ArgumentList.Add(Path.Combine(root, "tests", "browser", "scheduler-check.mjs")); using var browser = Process.Start(info) ?? throw new InvalidOperationException("Scheduler browser failed to start."); var browserOutput = browser.StandardOutput.ReadToEndAsync(); var browserError = browser.StandardError.ReadToEndAsync(); await browser.WaitForExitAsync(); await File.WriteAllTextAsync(Path.Combine(evidence, "browser.log"), await browserOutput + "\n" + await browserError); Assert(browser.ExitCode == 0, "Scheduler UI failed; inspect private evidence.");
                await using var db = new PortalDbContext(options); Assert(await db.PublicationJobs.CountAsync(x => x.ContentId == contentId) == 2 && await db.PublicationJobs.AnyAsync(x => x.ContentId == contentId && x.State == PublicationJobState.Canceled) && await db.PublicationJobs.AnyAsync(x => x.ContentId == contentId && x.IsActive), "Real UI did not preserve cancellation and distinct replacement job.");
            });
            await Check("Independent worker updates a web process with previously requested public state", async () =>
            {
                using (var hidden = await guest.GetAsync("/bai-viet/" + contentId)) Assert(hidden.StatusCode == HttpStatusCode.NotFound, "Schedule published before worker execution.");
                var search = await guest.GetStringAsync("/tim-kiem?q=" + Uri.EscapeDataString(title)); Assert(!search.Contains(contentId.ToString(), StringComparison.Ordinal), "Pending scheduled draft leaked to search.");
                await using var db = new PortalDbContext(options); var job = await db.PublicationJobs.AsNoTracking().SingleAsync(x => x.ContentId == contentId && x.IsActive);
                var wait = job.DueUtc - DateTimeOffset.UtcNow; if (wait > TimeSpan.Zero) await Task.Delay(wait + TimeSpan.FromMilliseconds(100));
                ProcessStartInfo WorkerInfo() { var info = Dotnet(TestRuntimePaths.Assembly(root,"Worker")); info.ArgumentList.Add("--once"); info.ArgumentList.Add("--job"); info.ArgumentList.Add(job.Id.ToString()); return info; }
                using var worker = Process.Start(WorkerInfo()) ?? throw new InvalidOperationException("Independent scheduler worker failed to start."); using var competitor = Process.Start(WorkerInfo()) ?? throw new InvalidOperationException("Second scheduler worker failed to start.");
                var workerOutput = worker.StandardOutput.ReadToEndAsync(); var workerError = worker.StandardError.ReadToEndAsync(); var competitorOutput = competitor.StandardOutput.ReadToEndAsync(); var competitorError = competitor.StandardError.ReadToEndAsync();
                await Task.WhenAll(worker.WaitForExitAsync(), competitor.WaitForExitAsync()); await File.WriteAllTextAsync(Path.Combine(evidence, "worker.log"), "Worker A:\n" + await workerOutput + "\n" + await workerError + "\nWorker B:\n" + await competitorOutput + "\n" + await competitorError); Assert(worker.ExitCode == 0 && competitor.ExitCode == 0, "Independent competing scheduler worker failed.");
                using var detail = await guest.GetAsync("/bai-viet/" + contentId); Assert(detail.IsSuccessStatusCode && (await detail.Content.ReadAsStringAsync()).Contains(title, StringComparison.Ordinal), "Existing web process did not see worker publication."); Assert(detail.Headers.CacheControl?.NoStore == true, "Public detail was cacheable after worker publication.");
                Assert((await guest.GetStringAsync("/tim-kiem?q=" + Uri.EscapeDataString(title))).Contains(contentId.ToString(), StringComparison.Ordinal), "Public search did not see worker publication.");
                Assert(await db.Contents.AnyAsync(x => x.Id == contentId && x.IsPublic && x.PublishedRevisionId == revisionId), "Worker published a different revision.");
                Assert(await db.AuditLogs.CountAsync(x => x.ContentId == contentId && x.Action == "PublishDirect") == 1 && await db.PublicationJobAttempts.CountAsync(x => x.JobId == job.Id) == 1, "Competing worker processes duplicated publication or the live claim.");
                var completed = await db.PublicationJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id); var token = await Form(administrator, "/admin/schedules"); token["version"] = Convert.ToBase64String(completed.RowVersion); using var cancel = await administrator.PostAsync("/admin/schedules/" + job.Id + "/cancel", new FormUrlEncodedContent(token)); Assert(cancel.StatusCode == HttpStatusCode.Redirect && await db.Contents.AnyAsync(x => x.Id == contentId && x.IsPublic), "Completed cancel withdrew or pretended to cancel publication.");
            });
        }
        catch (Exception exception) { checks.Add(new { name = "Scheduler HTTP/UI stopped", status = "FAIL", detail = exception.Message }); throw; }
        finally { if (!web.HasExited) web.Kill(entireProcessTree: true); await web.WaitForExitAsync(); await File.WriteAllTextAsync(Path.Combine(evidence, "web.log"), await output + "\n" + await error); await File.WriteAllTextAsync(Path.Combine(evidence, "http-report.json"), JsonSerializer.Serialize(new { database = "LandPortal_Test", utc = DateTimeOffset.UtcNow, ownedWebStopped = web.HasExited, checks }, new JsonSerializerOptions { WriteIndented = true })); }
    }
    private static bool Denied(HttpResponseMessage response) => response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Redirect && (response.Headers.Location?.OriginalString.Contains("/admin/login", StringComparison.Ordinal) == true || response.Headers.Location?.OriginalString.Contains("/admin/access-denied", StringComparison.Ordinal) == true);
    private static void Assert(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
