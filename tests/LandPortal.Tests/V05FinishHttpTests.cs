using System.Diagnostics;
using System.Globalization;
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

public static class V05FinishHttpTests
{
    private static void Check(bool value, string message) => V05FinishTests.Check(value, message);
    public static async Task RunAsync(string root, IConfiguration config, DbContextOptions<PortalDbContext> options)
    {
        var directory = TestEvidencePaths.At(root, "v05-finish-http"); Directory.CreateDirectory(directory);
        const string url = "http://127.0.0.1:5289"; var reports = new List<object>(); var password = Guid.NewGuid() + "!aZ2"; string userId; string username; Guid contentId; int week; const int year = 2097;
        await using (var db = new PortalDbContext(options))
        {
            userId = await InquiryFinishTests.User(db, Permissions.All, "V05 Finish HTTP QA"); var user = (await db.Users.FindAsync(userId))!; username = user.UserName!;
            user.PasswordHash = new PasswordHasher<PortalUser>().HashPassword(user, password); user.LockoutEnabled = true; await db.SaveChangesAsync();
            week = (await db.WeeklyScheduleReleases.Where(x => x.IsoYear == year).MaxAsync(x => (int?)x.IsoWeek) ?? 0) + 1;
            contentId = (await V05FinishTests.Weekly(db, config, await new CurrentPermissionService(db).GetAsync(userId), year, week)).ContentId;
        }
        var date = DateOnly.FromDateTime(ISOWeek.ToDateTime(year, week, DayOfWeek.Monday));
        var first = V05FinishTests.Word(date, "Word technical V1 " + Guid.NewGuid()); var second = V05FinishTests.Word(date, "Word technical V2 " + Guid.NewGuid());
        var bad = V05FinishTests.Word(date, "Wrong table " + Guid.NewGuid(), wrongHeader: true); var infected = V05FinishTests.Word(date, "Safe EICAR fixture", eicar: true);
        ProcessStartInfo Start(string project)
        {
            var p = new ProcessStartInfo(Path.Combine(root, ".local", "dotnet", "dotnet.exe")) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            p.ArgumentList.Add(TestRuntimePaths.Assembly(root, project)); p.Environment["DOTNET_ENVIRONMENT"] = "Development"; p.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            p.Environment["Public__BaseUrl"] = url; p.Environment["Captcha__TestScope"] = Guid.NewGuid().ToString("N"); p.Environment["Captcha__AttemptsPer10Minutes"] = "20";
            p.Environment["Media__AllowUnscannedDevelopmentUploads"] = "false"; p.Environment["Mail__Transport"] = "Disabled";
            p.Environment["Scanner__Provider"] = "DefenderTest"; p.Environment["Scanner__DefenderAnonymousTestOnly"] = "true"; p.Environment["Scanner__DefenderAllowedTestHashes"] = string.Join(',', new[] { first, second, bad, infected }.Select(ScannerTests.Hash));
            if (project == "Web") { p.ArgumentList.Add("--contentRoot"); p.ArgumentList.Add(Path.Combine(root, "src", "Web")); } return p;
        }
        using var web = Process.Start(Start("Web"))!; var stdout = web.StandardOutput.ReadToEndAsync(); var stderr = web.StandardError.ReadToEndAsync();
        HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new() }) { BaseAddress = new(url), Timeout = TimeSpan.FromSeconds(40) };
        using var admin = Client(); using var guest = Client();
        async Task<Dictionary<string,string>> Form(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path); Check(response.IsSuccessStatusCode, "Form failed: " + path + " " + response.StatusCode);
            var doc = new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync()); var fields = new Dictionary<string,string>(); var attachments = 0;
            foreach (var e in doc.QuerySelectorAll("input[name],textarea[name],select[name]"))
            {
                if (e.GetAttribute("type") is "checkbox" or "radio" && !e.HasAttribute("checked")) continue;
                var name = e.GetAttribute("name")!; if (name == "AttachmentIds") name = "AttachmentIds[" + attachments++ + "]";
                fields[name] = e.LocalName == "textarea" ? e.TextContent : e.LocalName == "select" ? (e.QuerySelector("option[selected]") ?? e.QuerySelector("option"))?.GetAttribute("value") ?? e.QuerySelector("option[selected]")?.TextContent ?? "" : e.GetAttribute("value") ?? "";
            }
            CaptchaHttp.Add(doc, fields); return fields;
        }
        async Task<Content> ContentRow() { await using var db = new PortalDbContext(options); return await db.Contents.AsNoTracking().SingleAsync(x => x.Id == contentId); }
        async Task Command(string action, Guid? revision = null)
        {
            var form = await Form(admin, "/admin/edit/" + contentId); var row = await ContentRow();
            var fields = new Dictionary<string,string> { ["__RequestVerificationToken"] = form["__RequestVerificationToken"], ["RowVersion"] = Convert.ToBase64String(row.RowVersion), ["RevisionId"] = (revision ?? row.CurrentRevisionId).ToString()!, ["RequestId"] = Guid.NewGuid().ToString(), ["Action"] = action };
            using var r = await admin.PostAsync("/admin/workflow/" + contentId, new FormUrlEncodedContent(fields)); Check(r.IsSuccessStatusCode, "Workflow HTTP failed: " + action + ": " + await r.Content.ReadAsStringAsync());
        }
        async Task Publish() { await Command("Submit"); await Command("Approve"); await Command("PublishApproved"); }
        async Task<Guid> Upload(byte[] bytes, string name)
        {
            var form = await Form(admin, "/admin/documents/" + contentId); using var multipart = new MultipartFormDataContent(); multipart.Add(new StringContent(form["__RequestVerificationToken"]), "__RequestVerificationToken");
            var data = new ByteArrayContent(bytes); data.Headers.ContentType = new(OfficeDocumentPolicy.Docx); multipart.Add(data, "file", name);
            using var result = await admin.PostAsync("/admin/documents/" + contentId, multipart); Check(result.StatusCode == HttpStatusCode.Redirect, "Actual Word upload failed: " + await result.Content.ReadAsStringAsync());
            await using var db = new PortalDbContext(options); return await db.MediaAssets.Where(x => x.Sha256 == ScannerTests.Hash(bytes) && x.Origin == "EditorialDocument").Select(x => x.Id).SingleAsync();
        }
        async Task Worker(Guid id, string label)
        {
            var p = Start("Worker"); p.ArgumentList.Add("--documents"); p.ArgumentList.Add("--once"); p.ArgumentList.Add("--asset"); p.ArgumentList.Add(id.ToString());
            using var process = Process.Start(p)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
            await File.WriteAllTextAsync(Path.Combine(directory, "worker-" + label + ".log"), await output + "\n" + await error); Check(process.ExitCode == 0, "Actual standalone document worker failed.");
        }
        async Task SaveImport(Guid id, string note)
        {
            var form = await Form(admin, "/admin/edit/" + contentId + "?documentId=" + id); form["Metadata.WeeklyOriginalReviewEvidence"] = "QA anonymous fixture reviewed including metadata, not official";
            form["Metadata.WeeklyChangeNote"] = note; form["ScheduleRows[0].Location"] = "Preview corrected location"; form["intent"] = "Save";
            using var saved = await admin.PostAsync("/admin/save", new FormUrlEncodedContent(form)); var html = await saved.Content.ReadAsStringAsync();
            if (saved.StatusCode != HttpStatusCode.Redirect) { var dom = new HtmlParser().ParseDocument(html); throw new InvalidOperationException("Word preview edit save failed: " + string.Join("; ", dom.QuerySelectorAll(".validation-summary li").Select(x => x.TextContent))); }
        }
        try
        {
            for (var i = 0; i < 150; i++) { try { using var h = await guest.GetAsync("/health"); if (h.IsSuccessStatusCode) break; } catch (HttpRequestException) { } if (web.HasExited) throw new InvalidOperationException("Finish web stopped."); await Task.Delay(100); }
            using (var direct = await guest.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string,string> { ["UserName"] = username, ["Password"] = password }))) Check(direct.StatusCode == HttpStatusCode.BadRequest, "Direct login without CSRF accepted.");
            var login = await Form(admin, "/admin/login"); login["UserName"] = username; login["Password"] = password;
            using (var bypass = await admin.PostAsync("/admin/reauthenticate", new FormUrlEncodedContent(new Dictionary<string,string>{{"userName",username},{"password",password},{"expectedUserId",userId},{"__RequestVerificationToken",login["__RequestVerificationToken"]}}))) Check(bypass.StatusCode == HttpStatusCode.BadRequest, "Reauthentication bypassed CAPTCHA login.");
            var wrong = new Dictionary<string,string>(login) { ["CaptchaAnswer"] = "999" }; using (var r = await admin.PostAsync("/admin/login", new FormUrlEncodedContent(wrong))) Check(r.StatusCode == HttpStatusCode.BadRequest, "Wrong CAPTCHA accepted.");
            await using (var db = new PortalDbContext(options)) Check((await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId)).AccessFailedCount == 0, "Wrong CAPTCHA invoked password authentication.");
            login = await Form(admin, "/admin/login"); login["UserName"] = username; login["Password"] = password;
            await using (var db = new PortalDbContext(options)) await db.PublicCaptchaChallenges.Where(x => x.Id == Guid.Parse(login["CaptchaId"])).ExecuteUpdateAsync(x => x.SetProperty(c => c.ExpiresUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
            using (var r = await admin.PostAsync("/admin/login", new FormUrlEncodedContent(login))) Check(r.StatusCode == HttpStatusCode.BadRequest, "Expired CAPTCHA accepted.");
            login = await Form(admin, "/admin/login"); login["UserName"] = username; login["Password"] = password; login["CaptchaId"] = Guid.Empty.ToString();
            using (var r = await admin.PostAsync("/admin/login", new FormUrlEncodedContent(login))) Check(r.StatusCode == HttpStatusCode.BadRequest, "Missing CAPTCHA accepted.");
            login = await Form(admin, "/admin/login"); login["UserName"] = username; login["Password"] = password;
            using (var r = await admin.PostAsync("/admin/login", new FormUrlEncodedContent(login))) Check(r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location?.OriginalString == "/admin", "Valid CAPTCHA did not preserve Identity login.");
            var logout = await Form(admin, "/admin"); using (var r = await admin.PostAsync("/admin/logout", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = logout["__RequestVerificationToken"] }))) Check(r.StatusCode == HttpStatusCode.Redirect, "Logout failed.");
            using (var r = await admin.PostAsync("/admin/login", new FormUrlEncodedContent(login))) Check(r.StatusCode == HttpStatusCode.BadRequest, "Consumed login CAPTCHA replayed.");
            login = await Form(admin, "/admin/login"); login["UserName"] = username; login["Password"] = password; using (var r = await admin.PostAsync("/admin/login", new FormUrlEncodedContent(login))) Check(r.StatusCode == HttpStatusCode.Redirect, "Relogin failed.");
            reports.Add(new { name = "Login genuine CAPTCHA wrong/expired/missing/replay/direct-CSRF deny; correct answer still needs Identity", status = "PASS" });
            var originalId = await Upload(first, "weekly-v1.docx");
            using (var r = await guest.GetAsync("/media/" + originalId)) Check(r.StatusCode == HttpStatusCode.NotFound, "Pending Word public.");
            using (var r = await admin.GetAsync("/admin/documents/file/" + originalId)) Check((int)r.StatusCode == 423, "Pending Word privately downloadable.");
            using (var r = await admin.GetAsync("/admin/edit/" + contentId + "?documentId=" + originalId)) Check((int)r.StatusCode == 423, "Pending Word parsed.");
            // Durable restart: abandoned claim is stored in SQL; a new process recovers its expired lease.
            await using (var db = new PortalDbContext(options)) { var d = await db.EditorialDocuments.SingleAsync(x => x.MediaAssetId == originalId); d.ScanState = InquiryScanState.Scanning; d.LeaseToken = Guid.NewGuid(); d.LeaseUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-1); d.Attempts = 1; await db.SaveChangesAsync(); }
            await Worker(originalId, "restart-clean-v1");
            await using (var db = new PortalDbContext(options)) { var d = await db.EditorialDocuments.SingleAsync(x => x.MediaAssetId == originalId); Check(d.ScanState == InquiryScanState.Clean && d.Attempts == 2 && d.ScannedSha256 == ScannerTests.Hash(first), "Restart did not recover genuine scan proof."); }
            await SaveImport(originalId, "Import Word technical v1"); var firstRevision = (await ContentRow()).CurrentRevisionId!.Value;
            using (var r = await guest.GetAsync("/media/" + originalId)) Check(r.StatusCode == HttpStatusCode.NotFound, "Saved Clean draft Word public.");
            await Publish();
            using (var r = await guest.GetAsync("/media/" + originalId + "?download=true")) Check(r.IsSuccessStatusCode && ScannerTests.Hash(await r.Content.ReadAsByteArrayAsync()) == ScannerTests.Hash(first) && r.Headers.CacheControl?.NoStore == true, "Published Word differs from genuine scanned original.");
            using (var request = new HttpRequestMessage(HttpMethod.Get, "/media/" + originalId)) { request.Headers.Range = new(0, 9); using var r = await guest.SendAsync(request); Check(r.StatusCode == HttpStatusCode.PartialContent && (await r.Content.ReadAsByteArrayAsync()).SequenceEqual(first.Take(10)), "Word Range bypassed exact-byte gate."); }
            await Command("BeginEdit"); var secondId = await Upload(second, "weekly-v2.docx"); await Worker(secondId, "clean-v2"); await SaveImport(secondId, "Import Word technical v2");
            var publicV1 = WebUtility.HtmlDecode(await guest.GetStringAsync($"/lich-tuan?year={year}&week={week}")); Check(publicV1.Contains("Word technical V1") && !publicV1.Contains("Word technical V2"), "Draft Word changed live schedule.");
            using (var r = await guest.GetAsync("/media/" + secondId)) Check(r.StatusCode == HttpStatusCode.NotFound, "New draft Word exposed.");
            await Publish();
            using (var r = await guest.GetAsync("/media/" + originalId)) Check(r.StatusCode == HttpStatusCode.NotFound, "Retired original remains public without current grant.");
            using (var r = await admin.GetAsync("/admin/documents/file/" + originalId)) Check(r.IsSuccessStatusCode && ScannerTests.Hash(await r.Content.ReadAsByteArrayAsync()) == ScannerTests.Hash(first), "Historical Word original lost.");
            await Command("RestoreRevision", firstRevision); await Publish();
            var release = WebUtility.HtmlDecode(await guest.GetStringAsync($"/lich-tuan?year={year}&week={week}")); Check(release.Contains("Phiên bản 3") && release.Contains("Word technical V1") && release.Contains("Preview corrected location"), "Restore did not create v3 with corrected rows and original.");
            reports.Add(new { name = "Actual DOCX upload private→real standalone worker/restart→parser merged rows→edit preview→SQL draft→approve exact revision→publish v1/v2→restore v3; original bytes/history/Range", status = "PASS", originalId, secondId, year, week });
            await Command("BeginEdit"); var badId = await Upload(bad, "wrong-table.docx"); await Worker(badId, "clean-wrong-table");
            var before = await ContentRow(); var preview = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/edit/" + contentId + "?documentId=" + badId)); var after = await ContentRow();
            Check(preview.Contains("Sai mẫu") && before.PublishedRevisionId == after.PublishedRevisionId && before.CurrentRevisionId == after.CurrentRevisionId && before.RowVersion.SequenceEqual(after.RowVersion), "Parser error changed saved/live state or lacks location/message.");
            var eicarId = await Upload(infected, "eicar-technical.docx"); await Worker(eicarId, "infected");
            using (var r = await guest.GetAsync("/media/" + eicarId)) Check(r.StatusCode == HttpStatusCode.NotFound, "Infected Word public."); using (var r = await admin.GetAsync("/admin/documents/file/" + eicarId)) Check((int)r.StatusCode == 423, "Infected Word admin download.");
            using (var r = await guest.GetAsync("/admin/documents/file/" + originalId)) Check(r.StatusCode == HttpStatusCode.Redirect, "Guest private document ACL bypassed.");
            reports.Add(new { name = "Wrong Word template shows editable preview diagnostics without SQL changes; genuine EICAR quarantined; guest/private/current publication gates", status = "PASS", badId, eicarId, AC161 = "BLOCKED:NO_REAL_WORD_SAMPLES" });
            var fixture = Path.Combine(directory, "browser-fixture.json"); await File.WriteAllTextAsync(fixture, JsonSerializer.Serialize(new { baseUrl = url, username, password, contentId, originalId, badId, year, week }));
            var node = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }; node.ArgumentList.Add(Path.Combine(root, "tests", "browser", "v05-finish-check.mjs")); node.ArgumentList.Add(fixture); node.ArgumentList.Add(directory);
            using (var browser = Process.Start(node)!) { var output = browser.StandardOutput.ReadToEndAsync(); var error = browser.StandardError.ReadToEndAsync(); await browser.WaitForExitAsync(); await File.WriteAllTextAsync(Path.Combine(directory, "browser.log"), await output + "\n" + await error); Check(browser.ExitCode == 0, "Finish actual browser failed."); }
            await using (var db = new PortalDbContext(options)) { var u = await db.Users.SingleAsync(x => x.Id == userId); u.IsDisabled = true; await db.SaveChangesAsync(); }
            using (var r = await admin.GetAsync("/admin/documents/file/" + originalId)) Check(r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.NotFound or HttpStatusCode.Forbidden, "Disabled principal retained download ACL.");
            reports.Add(new { name = "Existing authenticated document access revoked after account disabled", status = "PASS" });
        }
        finally
        {
            if (!web.HasExited) web.Kill(true); await web.WaitForExitAsync();
            await File.WriteAllTextAsync(Path.Combine(directory, "web.log"), await stdout + "\n" + await stderr);
            await File.WriteAllTextAsync(Path.Combine(directory, "http-report.json"), JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, database = "LandPortal_Test", reports, ownedWebStopped = web.HasExited }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
