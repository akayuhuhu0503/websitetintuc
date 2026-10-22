using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

namespace LandPortal.Tests;

public static class Phase2bSiteHttpTests
{
    public static async Task RunAsync(string root, IConfiguration config, DbContextOptions<PortalDbContext> options)
    {
        var storage = StorageOptions.FromConfiguration(config);
        if (new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(DatabaseSettings.Connection(config)).InitialCatalog != "LandPortal_Test" || !storage.MediaRoot.StartsWith(Path.Combine(root, ".local", "tests") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Site HTTP tests require verified SQL Test/private Test storage.");
        var runId = Guid.NewGuid(); var startedUtc = DateTimeOffset.UtcNow; var checks = new List<object>();
        var evidence = TestEvidencePaths.At(root, "phase2b", "site"); Directory.CreateDirectory(evidence);
        var webPath = TestRuntimePaths.Assembly(root,"Web"); var webAssemblyHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(webPath)));
        var password = Guid.NewGuid().ToString("N") + "Aa!7";
        PortalUser User(string kind) { var name = "QA-2B-Site-HTTP-" + kind + "-" + Guid.NewGuid().ToString("N"); var user = new PortalUser { UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.invalid", NormalizedEmail = (name + "@example.invalid").ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true }; user.PasswordHash = new PasswordHasher<PortalUser>().HashPassword(user, password); return user; }
        var admin = User("administrator"); var manager = User("draft-manager"); var publisher = User("site-publisher"); var writer = User("writer"); Guid originalId;
        await using (var seed = new PortalDbContext(options))
        {
            foreach (var (user, rights) in new[] { (admin, Permissions.All), (manager, new[] { Permissions.ManageSite, Permissions.ViewSite, Permissions.ManageMedia }), (publisher, new[] { Permissions.PublishSite, Permissions.ViewSite }), (writer, new[] { Permissions.EditOwn, Permissions.Create }) })
            { seed.Users.Add(user); foreach (var right in rights) seed.UserClaims.Add(new() { UserId = user.Id, ClaimType = "permission", ClaimValue = right }); }
            await seed.SaveChangesAsync(); var service = new SiteConfigurationService(seed, storage, MediaUploadOptions.FromConfiguration(config, "Development")); await service.InitializeAsync("QA-site-http-explicit"); originalId = (await service.ReadAsync(true))!.Revision.Id;
        }
        const string baseUrl = "http://127.0.0.1:5196";
        var info = new ProcessStartInfo(Path.Combine(root, ".local", "dotnet", "dotnet.exe")) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(webPath); info.Environment["Captcha__TestScope"]=Guid.NewGuid().ToString("N");info.Environment["Captcha__AttemptsPer10Minutes"]="20";info.Environment["Public__BaseUrl"] = baseUrl; info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"; info.Environment["ConnectionStrings__Portal"] = DatabaseSettings.Connection(config); info.Environment["Sql__ExpectedDatabase"] = "LandPortal_Test"; info.Environment["Media__AllowUnscannedDevelopmentUploads"] = "true";
        foreach (var key in new[] { "MediaRoot", "SnapshotRoot", "TempRoot", "KeysRoot" }) { var value = config["Storage:" + key] ?? throw new InvalidOperationException("Missing Test storage."); if (!Path.GetFullPath(value, root).StartsWith(Path.Combine(root, ".local", "tests") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Site HTTP storage escaped Test."); info.Environment["Storage__" + key] = value; }
        using var web = Process.Start(info) ?? throw new InvalidOperationException("Site Web could not start."); var output = web.StandardOutput.ReadToEndAsync(); var error = web.StandardError.ReadToEndAsync();
        HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        using var guest = Client(); using var administrator = Client(); using var editor = Client(); using var applier = Client(); using var contentWriter = Client();
        async Task<Dictionary<string, string>> Form(HttpClient client, string path)
        {
            var doc = new HtmlParser().ParseDocument(await client.GetStringAsync(path)); var token = doc.QuerySelector("input[name=__RequestVerificationToken]")?.GetAttribute("value") ?? throw new InvalidOperationException("Missing CSRF.");
            var values = new Dictionary<string,string> { ["__RequestVerificationToken"] = token };
            foreach (var input in doc.QuerySelectorAll("#site-edit-form input[name]")) { var name = input.GetAttribute("name")!; if (input.GetAttribute("type") == "checkbox" && !input.HasAttribute("checked")) continue; if (input.GetAttribute("type") == "hidden" && values.ContainsKey(name)) continue; values[name] = input.GetAttribute("value") ?? ""; }
            foreach (var textarea in doc.QuerySelectorAll("#site-edit-form textarea[name]")) values[textarea.GetAttribute("name")!] = textarea.TextContent;
            foreach (var select in doc.QuerySelectorAll("#site-edit-form select[name]")) values[select.GetAttribute("name")!] = (select.QuerySelector("option[selected]") ?? select.QuerySelector("option"))?.GetAttribute("value") ?? "";
            CaptchaHttp.Add(doc, values); return values;
        }
        async Task Login(HttpClient client, PortalUser user) { var form = await Form(client, "/admin/login"); form["UserName"] = user.UserName!; form["Password"] = password; using var result = await client.PostAsync("/admin/login", new FormUrlEncodedContent(form)); Assert(result.StatusCode == HttpStatusCode.Redirect, "Site HTTP login failed."); }
        async Task<Dictionary<string, string>> Command(HttpClient client) { var form = await Form(client, "/admin/site"); await using var db = new PortalDbContext(options); var site = await db.Set<SiteConfiguration>().AsNoTracking().SingleAsync(); form["rowVersion"] = Convert.ToBase64String(site.RowVersion); form["revisionId"] = site.DraftRevisionId!.Value.ToString(); return form; }
        async Task Check(string name, Func<Task> action) { await action(); checks.Add(new { name, status = "PASS" }); Console.WriteLine("HTTP 2B site PASS " + name); }
        Guid uploadId = Guid.Empty;
        try
        {
            var ready = false; for (var i = 0; i < 150; i++) { if (web.HasExited) throw new InvalidOperationException("Site Web exited."); try { using var response = await guest.GetAsync("/ready"); if (response.IsSuccessStatusCode) { ready = true; break; } } catch (HttpRequestException) { } await Task.Delay(100); } Assert(ready, "Site Web not ready.");
            await Login(administrator, admin); await Login(editor, manager); await Login(applier, publisher); await Login(contentWriter, writer);
            await Check("Endpoint permission boundaries and CSRF protect config/preview/publish", async () =>
            {
                foreach (var client in new[] { guest, contentWriter }) foreach (var path in new[] { "/admin/site", "/admin/site/preview", "/admin/site/edit?section=contact" }) { using var denied = await client.GetAsync(path); Assert(Denied(denied), "Unprivileged caller accessed site configuration."); }
                using var saveDenied = await applier.PostAsync("/admin/site/save", new FormUrlEncodedContent(new Dictionary<string,string>())); Assert(Denied(saveDenied), "PublishSite implied ManageSite.");
                using var publishDenied = await editor.PostAsync("/admin/site/publish", new FormUrlEncodedContent(await Command(editor))); Assert(Denied(publishDenied), "ManageSite implied PublishSite.");
                using var csrf = await administrator.PostAsync("/admin/site/publish", new FormUrlEncodedContent(new Dictionary<string,string>())); Assert(csrf.StatusCode == HttpStatusCode.BadRequest, "Site publish accepted missing CSRF.");
            });
            await Check("Real contact form saves draft; public excludes it; exact publish and stale save", async () =>
            {
                var draftText = "QA private HTTP footer " + Guid.NewGuid(); var form = await Form(editor, "/admin/site/edit?section=contact"); form["Contact.FooterText"] = draftText;
                using var saved = await editor.PostAsync("/admin/site/save", new FormUrlEncodedContent(form)); Assert(saved.StatusCode == HttpStatusCode.Redirect, "Contact draft form did not persist.");
                Assert(!(await guest.GetStringAsync("/")).Contains(draftText, StringComparison.Ordinal), "A draft contact/footer leaked to public.");
                Assert((await editor.GetStringAsync("/admin/site/preview")).Contains(draftText, StringComparison.Ordinal), "Private preview did not use the draft public layout.");
                form["Contact.FooterText"] = "QA stale HTTP"; using var stale = await editor.PostAsync("/admin/site/save", new FormUrlEncodedContent(form)); Assert(stale.StatusCode == HttpStatusCode.Conflict, "Concurrent stale contact save was accepted.");
                var command = await Command(applier); command["revisionId"] = originalId.ToString(); using var wrong = await applier.PostAsync("/admin/site/publish", new FormUrlEncodedContent(command)); Assert(wrong.StatusCode == HttpStatusCode.Conflict, "Publish reused a replaced revision.");
                using var published = await applier.PostAsync("/admin/site/publish", new FormUrlEncodedContent(await Command(applier))); Assert(published.StatusCode == HttpStatusCode.Redirect && (await guest.GetStringAsync("/")).Contains(draftText, StringComparison.Ordinal), "Exact publish did not switch public footer.");
            });
            await Check("Real PNG upload and banner form keep draft asset private then revoke last live reference", async () =>
            {
                using var bitmap = new SKBitmap(2, 2); bitmap.Erase(new SKColor((uint)Random.Shared.NextInt64(0, uint.MaxValue) | 0xff000000)); using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                var csrf = (await Form(editor, "/admin/site/edit?section=banners"))["__RequestVerificationToken"]; using var body = new MultipartFormDataContent(); var bytes = new ByteArrayContent(encoded.ToArray()); bytes.Headers.ContentType = new("image/png"); body.Add(bytes, "file", "QA-site-http-banner.png"); using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/media/upload?site=true") { Content = body }; request.Headers.Add("X-CSRF-TOKEN", csrf);
                using var upload = await editor.SendAsync(request); Assert(upload.IsSuccessStatusCode, "Site image upload did not use the existing pipeline."); var uploaded = JsonDocument.Parse(await upload.Content.ReadAsStringAsync()); uploadId = uploaded.RootElement.GetProperty("id").GetGuid(); Assert(uploaded.RootElement.GetProperty("scanStatus").GetString() == "NotScanned", "Upload falsely reported scanned.");
                using (var privateAsset = await guest.GetAsync("/media/" + uploadId)) Assert(privateAsset.StatusCode == HttpStatusCode.NotFound, "Ungrafted uploaded image leaked.");
                var form = await Form(editor, "/admin/site/edit?section=banners"); form["Banner.Label"] = "QA HTTP banner"; form["Banner.Alt"] = "QA HTTP banner alt"; form["Banner.MediaAssetId"] = uploadId.ToString(); form["Banner.RouteKey"] = "Home";
                using var draft = await editor.PostAsync("/admin/site/save", new FormUrlEncodedContent(form)); Assert(draft.StatusCode == HttpStatusCode.Redirect, "Banner typed form did not save.");
                using (var privateAsset = await guest.GetAsync("/media/" + uploadId)) Assert(privateAsset.StatusCode == HttpStatusCode.NotFound, "Draft banner leaked its original.");
                using (var preview = await editor.GetAsync("/admin/site/media/" + uploadId)) Assert(preview.IsSuccessStatusCode && preview.Headers.CacheControl?.NoStore == true, "Private site preview lost its media or privacy header.");
                using (var live = await applier.PostAsync("/admin/site/publish", new FormUrlEncodedContent(await Command(applier)))) Assert(live.StatusCode == HttpStatusCode.Redirect, "Banner apply failed.");
                using (var publicAsset = await guest.GetAsync("/media/" + uploadId)) Assert(publicAsset.IsSuccessStatusCode, "Live banner asset not served.");
                await using var db = new PortalDbContext(options); var liveId = (await db.Set<SiteConfiguration>().AsNoTracking().SingleAsync()).LiveRevisionId; var banner = await db.Set<SiteBanner>().AsNoTracking().SingleAsync(x => x.RevisionId == liveId && x.MediaAssetId == uploadId);
                var remove = await Command(editor); remove["section"] = "banners"; remove["id"] = banner.Id.ToString(); using (var removed = await editor.PostAsync("/admin/site/remove", new FormUrlEncodedContent(remove))) Assert(removed.StatusCode == HttpStatusCode.Redirect, "Removing a banner draft failed.");
                using (var stillLive = await guest.GetAsync("/media/" + uploadId)) Assert(stillLive.IsSuccessStatusCode, "Removing draft reference changed live grant.");
                using (var apply = await applier.PostAsync("/admin/site/publish", new FormUrlEncodedContent(await Command(applier)))) Assert(apply.StatusCode == HttpStatusCode.Redirect, "Removal apply failed.");
                using (var withdrawn = await guest.GetAsync("/media/" + uploadId)) Assert(withdrawn.StatusCode == HttpStatusCode.NotFound, "Last live banner removal left a public grant.");
                using (var history = await editor.GetAsync("/admin/site/media/" + uploadId)) Assert(history.IsSuccessStatusCode, "Removing live media destroyed its private history.");
            });
            await Check("Source menu protected against posted fields; browser config lifecycle and responsive views", async () =>
            {
                await using var db = new PortalDbContext(options); var state = (await new SiteConfigurationService(db, storage, MediaUploadOptions.FromConfiguration(config, "Development")).ReadAsync(false))!; var source = state.Revision.Categories.First(x => x.CategoryId == 75);
                var form = await Form(editor, "/admin/site/edit?section=categories&categoryId=75"); form["Category.DisplayName"] = "QA forbidden source rename"; form["SystemProtected"] = "false"; using var rejected = await editor.PostAsync("/admin/site/save", new FormUrlEncodedContent(form)); Assert(rejected.StatusCode == HttpStatusCode.BadRequest, "Mass assignment changed a protected source category.");
                var fixture = Path.Combine(root, ".local", "tests", "phase2b", "site-ui.json"); Directory.CreateDirectory(Path.GetDirectoryName(fixture)!); await File.WriteAllTextAsync(fixture, JsonSerializer.Serialize(new { database = "LandPortal_Test", baseUrl, user = admin.UserName, password, uploadId, categories = state.Revision.Categories.Where(x => x.CategoryId is 75 or 83 or 82 or 81 or 55 or 49 or 48 or 53 or 51 or 47 or 50 or 57 or 74 or 56).Select(x => new { x.CategoryId, x.Slug }) }));
                var node = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }; node.ArgumentList.Add(Path.Combine(root, "tests", "browser", "site-check.mjs")); using var browser = Process.Start(node) ?? throw new InvalidOperationException("Site browser could not start."); var browserOutput = browser.StandardOutput.ReadToEndAsync(); var browserError = browser.StandardError.ReadToEndAsync(); await browser.WaitForExitAsync(); await File.WriteAllTextAsync(Path.Combine(evidence, "browser.log"), await browserOutput + "\n" + await browserError); Assert(browser.ExitCode == 0, "Site browser verification failed; inspect ignored evidence.");
            });
        }
        catch (Exception exception) { checks.Add(new { name = "Site HTTP/UI stopped", status = "FAIL", detail = exception.Message }); throw; }
        finally
        {
            if (!web.HasExited) web.Kill(entireProcessTree: true); await web.WaitForExitAsync();
            await using var db = new PortalDbContext(options); var service = new SiteConfigurationService(db, storage, MediaUploadOptions.FromConfiguration(config, "Development")); var actor = new ContentActor(admin.Id, Permissions.All.ToHashSet(StringComparer.Ordinal)); var state = (await service.ReadAsync(false))!;
            var restored = await service.RestoreAsync(actor, state.Header.RowVersion, originalId); await service.PublishAsync(actor, restored.Header.RowVersion, restored.Revision.Id);
            await File.WriteAllTextAsync(Path.Combine(evidence, "web.log"), await output + "\n" + await error);
            await File.WriteAllTextAsync(Path.Combine(evidence, "http-report.json"), JsonSerializer.Serialize(new { runId, database = "LandPortal_Test", startedUtc, finishedUtc = DateTimeOffset.UtcNow, webAssemblyHash, ownedWebStopped = web.HasExited, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private static bool Denied(HttpResponseMessage response) => response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Redirect && (response.Headers.Location?.OriginalString.Contains("/admin/login", StringComparison.Ordinal) == true || response.Headers.Location?.OriginalString.Contains("/admin/access-denied", StringComparison.Ordinal) == true);
    private static void Assert(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
