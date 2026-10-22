using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Security.Cryptography;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Tests;

public static class Phase2bAccountHttpTests
{
    public static async Task RunAsync(string root, IConfiguration config, DbContextOptions<PortalDbContext> options)
    {
        if (new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(DatabaseSettings.Connection(config)).InitialCatalog != "LandPortal_Test") throw new InvalidOperationException("Account HTTP tests require SQL Test.");
        var runId = Guid.NewGuid(); var startedUtc = DateTimeOffset.UtcNow;
        var webAssemblyHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(TestRuntimePaths.Assembly(root,"Web"))));
        var testAssemblyHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(Phase2bAccountHttpTests).Assembly.Location)));
        var checks = new List<object>(); var evidence = TestEvidencePaths.At(root, "phase2b", "accounts"); Directory.CreateDirectory(evidence);
        var password = Guid.NewGuid().ToString("N") + "Aa!7"; var newPassword = Guid.NewGuid().ToString("N") + "Bb!9";
        PortalUser User(string kind) { var name = "QA-2B-HTTP-" + kind + "-" + Guid.NewGuid().ToString("N"); var user = new PortalUser { UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.invalid", NormalizedEmail = (name + "@example.invalid").ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true }; user.PasswordHash = new PasswordHasher<PortalUser>().HashPassword(user, password); return user; }
        var admin = User("administrator"); var manager = User("profiles"); var editor = User("editor");
        await using (var seed = new PortalDbContext(options))
        {
            foreach (var (user, rights) in new[] { (admin, Permissions.All), (manager, new[] { Permissions.ManageUsers }), (editor, new[] { Permissions.EditOwn, Permissions.Create }) })
            {
                seed.Users.Add(user); var role = new IdentityRole("QA-2B HTTP " + Guid.NewGuid().ToString("N")); role.NormalizedName = role.Name!.ToUpperInvariant(); seed.Roles.Add(role); seed.UserRoles.Add(new() { UserId = user.Id, RoleId = role.Id });
                foreach (var permission in rights) seed.RoleClaims.Add(new() { RoleId = role.Id, ClaimType = "permission", ClaimValue = permission });
            }
            await seed.SaveChangesAsync();
        }
        const string baseUrl = "http://127.0.0.1:5194";
        var info = new ProcessStartInfo(Path.Combine(root, ".local", "dotnet", "dotnet.exe")) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(TestRuntimePaths.Assembly(root,"Web")); info.Environment["Captcha__TestScope"]=Guid.NewGuid().ToString("N");info.Environment["Captcha__AttemptsPer10Minutes"]="20";info.Environment["Public__BaseUrl"] = baseUrl; info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"; info.Environment["ConnectionStrings__Portal"] = DatabaseSettings.Connection(config); info.Environment["Sql__ExpectedDatabase"] = "LandPortal_Test"; info.Environment["Media__AllowUnscannedDevelopmentUploads"] = "true";
        foreach (var key in new[] { "MediaRoot", "SnapshotRoot", "TempRoot", "KeysRoot" }) { var value = config["Storage:" + key] ?? throw new InvalidOperationException("Test storage missing."); if (!Path.GetFullPath(value, root).StartsWith(Path.Combine(root, ".local", "tests"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Account HTTP storage must be isolated."); info.Environment["Storage__" + key] = value; }
        using var web = Process.Start(info) ?? throw new InvalidOperationException("Account test web could not start."); var output = web.StandardOutput.ReadToEndAsync(); var error = web.StandardError.ReadToEndAsync();
        HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        using var guest = Client(); using var administrator = Client(); using var profiles = Client(); using var writer = Client(); using var temporary = Client();
        async Task<Dictionary<string, string>> Form(HttpClient client, string path) { var html = new HtmlParser().ParseDocument(await client.GetStringAsync(path)); var token = html.QuerySelector("input[name=__RequestVerificationToken]")?.GetAttribute("value") ?? throw new InvalidOperationException("CSRF field missing."); return CaptchaHttp.Fields(html); }
        async Task Login(HttpClient client, string name, string pass) { var values = await Form(client, "/admin/login"); values["UserName"] = name; values["Password"] = pass; values["ReturnUrl"] = "/admin"; using var response = await client.PostAsync("/admin/login", new FormUrlEncodedContent(values)); Assert(response.StatusCode == HttpStatusCode.Redirect, "HTTP Identity login failed."); }
        async Task<string> Stamp(string id) { await using var db = new PortalDbContext(options); return (await db.Users.AsNoTracking().SingleAsync(x => x.Id == id)).ConcurrencyStamp!; }
        async Task Check(string name, Func<Task> action) { await action(); checks.Add(new { name, status = "PASS" }); Console.WriteLine("HTTP 2B account PASS " + name); }
        string? createdRole = null; string? tempId = null; string? tempName = null;
        try
        {
            var ready = false; for (var i = 0; i < 150; i++) { if (web.HasExited) throw new InvalidOperationException("Account web exited early."); try { using var response = await guest.GetAsync("/ready"); if (response.IsSuccessStatusCode) { ready = true; break; } } catch (HttpRequestException) { } await Task.Delay(100); } Assert(ready, "Account web not ready.");
            await Login(administrator, admin.UserName!, password); await Login(profiles, manager.UserName!, password); await Login(writer, editor.UserName!, password);
            await Check("Private account and role endpoints enforce guest/editor/profile-manager boundaries", async () =>
            {
                foreach (var path in new[] { "/admin/users", "/admin/roles" }) { using var anonymous = await guest.GetAsync(path); Assert(Denied(anonymous), "Guest accessed identity administration."); using var denied = await writer.GetAsync(path); Assert(Denied(denied), "A content editor accessed identity administration."); }
                using var allowed = await profiles.GetAsync("/admin/users"); Assert(allowed.IsSuccessStatusCode, "Profile manager could not list accounts."); using var deniedRoles = await profiles.GetAsync("/admin/roles"); Assert(Denied(deniedRoles), "ManageUsers implied ManageRoles.");
                var forged = await Form(profiles, "/admin/users/" + editor.Id); forged["ConcurrencyStamp"] = await Stamp(editor.Id); using var deniedGrant = await profiles.PostAsync("/admin/users/" + editor.Id + "/roles", new FormUrlEncodedContent(forged)); Assert(Denied(deniedGrant), "ManageUsers implied role grants.");
                using var csrf = await administrator.PostAsync("/admin/roles/save", new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "QA forged" })); Assert(csrf.StatusCode == HttpStatusCode.BadRequest, "Identity mutation accepted missing CSRF.");
            });
            await Check("Role UI posts catalog-bound permissions and blocks injected/self-role escalation", async () =>
            {
                var form = await Form(administrator, "/admin/roles/create"); form["Name"] = "QA-2B HTTP role " + Guid.NewGuid(); form["Permissions"] = Permissions.PublishDirect;
                using var saved = await administrator.PostAsync("/admin/roles/save", new FormUrlEncodedContent(form)); Assert(saved.StatusCode == HttpStatusCode.Redirect, "Role form did not save."); createdRole = saved.Headers.Location!.OriginalString.Split('/').Last();
                form = await Form(administrator, "/admin/roles/create"); form["Name"] = "QA-2B injection " + Guid.NewGuid(); form["Permissions"] = "untrusted.admin"; using var injection = await administrator.PostAsync("/admin/roles/save", new FormUrlEncodedContent(form)); Assert(injection.StatusCode == HttpStatusCode.BadRequest, "Unknown permission was accepted.");
                await using var db = new PortalDbContext(options); var ownRole = await db.UserRoles.Where(x => x.UserId == admin.Id).Select(x => x.RoleId).SingleAsync(); var role = await db.Roles.AsNoTracking().SingleAsync(x => x.Id == ownRole);
                form = await Form(administrator, "/admin/roles/" + ownRole); form["Id"] = ownRole; form["ConcurrencyStamp"] = role.ConcurrencyStamp!; form["Name"] = role.Name!; form["Permissions"] = Permissions.EditOwn; using var own = await administrator.PostAsync("/admin/roles/save", new FormUrlEncodedContent(form)); Assert(Denied(own), "Editing one's own role permission set was accepted.");
            });
            await Check("Shared-role revocation expires existing cookies and removes current publication authority", async () =>
            {
                var assign = await Form(administrator, "/admin/users/" + editor.Id); assign["ConcurrencyStamp"] = await Stamp(editor.Id); assign["RoleIds"] = createdRole!;
                using var grant = await administrator.PostAsync("/admin/users/" + editor.Id + "/roles", new FormUrlEncodedContent(assign)); Assert(grant.StatusCode == HttpStatusCode.Redirect, "Authorized role assignment failed.");
                using var revokedOldCookie = await writer.GetAsync("/admin/create"); Assert(Denied(revokedOldCookie), "Assigning groups retained the old cookie."); await Login(writer, editor.UserName!, password);
                await using var db = new PortalDbContext(options); var role = await db.Roles.AsNoTracking().SingleAsync(x => x.Id == createdRole);
                var remove = await Form(administrator, "/admin/roles/" + createdRole); remove["Id"] = createdRole!; remove["Name"] = role.Name!; remove["ConcurrencyStamp"] = role.ConcurrencyStamp!;
                using var reduced = await administrator.PostAsync("/admin/roles/save", new FormUrlEncodedContent(remove)); Assert(reduced.StatusCode == HttpStatusCode.Redirect, "Shared-role reduction failed.");
                using var oldCookie = await writer.GetAsync("/admin/create"); Assert(Denied(oldCookie), "Role changes retained member's old cookie.");
                Assert(!(await new CurrentPermissionService(db).GetAsync(editor.Id)).Has(Permissions.PublishDirect), "SQL effective permissions retained removed publication.");
            });
            await Check("Temporary account must change its own password before any sensitive endpoint", async () =>
            {
                tempName = "QA-2B-HTTP-Temp-" + Guid.NewGuid().ToString("N"); var form = await Form(administrator, "/admin/users/create"); form["UserName"] = tempName; form["Email"] = tempName + "@example.invalid"; form["DisplayName"] = "QA temporary editor"; form["TemporaryPassword"] = password;
                using var created = await administrator.PostAsync("/admin/users/create", new FormUrlEncodedContent(form)); Assert(created.StatusCode == HttpStatusCode.Redirect, "Temporary account creation failed."); tempId = created.Headers.Location!.OriginalString.Split('/').Last();
                await Login(temporary, tempName, password); using var forced = await temporary.GetAsync("/admin/users"); Assert(forced.StatusCode == HttpStatusCode.Redirect && forced.Headers.Location?.OriginalString == "/admin/account/change-password", "Temporary account bypassed password change.");
                form = await Form(temporary, "/admin/account/change-password"); form["CurrentPassword"] = password; form["NewPassword"] = newPassword; form["ConfirmPassword"] = newPassword;
                using var changed = await temporary.PostAsync("/admin/account/change-password", new FormUrlEncodedContent(form)); Assert(changed.StatusCode == HttpStatusCode.Redirect, "Own password change failed.");
                await using var db = new PortalDbContext(options); var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == tempId); Assert(!user.MustChangePassword && new PasswordHasher<PortalUser>().VerifyHashedPassword(user, user.PasswordHash!, newPassword) != PasswordVerificationResult.Failed, "Password change did not persist in SQL."); Assert(!await db.UserRoles.AnyAsync(x => x.UserId == tempId), "Temporary account gained permissions implicitly.");
            });
            await Check("Profile concurrency and lock operations reject stale requests and preserve password", async () =>
            {
                var stamp = await Stamp(manager.Id); var form = await Form(administrator, "/admin/users/" + manager.Id); form["ConcurrencyStamp"] = stamp; form["Email"] = manager.Email!; form["DisplayName"] = "QA profile first";
                using var first = await administrator.PostAsync("/admin/users/" + manager.Id + "/update", new FormUrlEncodedContent(form)); Assert(first.StatusCode == HttpStatusCode.Redirect, "Authorized profile edit failed."); form["DisplayName"] = "QA stale overwrite"; using var stale = await administrator.PostAsync("/admin/users/" + manager.Id + "/update", new FormUrlEncodedContent(form)); Assert(stale.StatusCode == HttpStatusCode.Conflict, "Stale profile update was accepted.");
                form = await Form(administrator, "/admin/users/" + manager.Id); form["ConcurrencyStamp"] = await Stamp(manager.Id); form["disabled"] = "true";
                using var disabled = await administrator.PostAsync("/admin/users/" + manager.Id + "/status", new FormUrlEncodedContent(form)); Assert(disabled.StatusCode == HttpStatusCode.Redirect, "Authorized disable failed."); using var cookie = await profiles.GetAsync("/admin/users"); Assert(Denied(cookie), "Disabled profile manager retained its cookie.");
                await using var db = new PortalDbContext(options); var preserved = await db.Users.AsNoTracking().SingleAsync(x => x.Id == manager.Id); Assert(preserved.IsDisabled && preserved.PasswordHash == manager.PasswordHash, "Lock deleted/reset user credentials.");
            });
            var fixturePath = Path.Combine(root, ".local", "tests", "phase2b", "accounts-ui.json"); Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
            await File.WriteAllTextAsync(fixturePath, JsonSerializer.Serialize(new { database = "LandPortal_Test", baseUrl, admin = admin.UserName, password, tempId, tempName, newPassword, roleId = createdRole }));
            var node = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }; node.ArgumentList.Add(Path.Combine(root, "tests", "browser", "accounts-check.mjs"));
            using var browser = Process.Start(node) ?? throw new InvalidOperationException("Account browser test could not start."); var browserOut = browser.StandardOutput.ReadToEndAsync(); var browserErr = browser.StandardError.ReadToEndAsync(); await browser.WaitForExitAsync();
            await File.WriteAllTextAsync(Path.Combine(evidence, "browser-run.log"), await browserOut + "\n" + await browserErr); Assert(browser.ExitCode == 0, "Account browser tests failed; inspect ignored account evidence.");
        }
        catch (Exception exception) { checks.Add(new { name = "Account HTTP/UI suite stopped", status = "FAIL", detail = exception.Message }); throw; }
        finally { if (!web.HasExited) web.Kill(entireProcessTree: true); await web.WaitForExitAsync(); await File.WriteAllTextAsync(Path.Combine(evidence, "web.log"), await output + "\n" + await error); await File.WriteAllTextAsync(Path.Combine(evidence, "http-report.json"), JsonSerializer.Serialize(new { runId, database = "LandPortal_Test", startedUtc, finishedUtc = DateTimeOffset.UtcNow, webAssemblyHash, testAssemblyHash, ownedWebStopped = web.HasExited, checks }, new JsonSerializerOptions { WriteIndented = true })); }
    }
    private static bool Denied(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        var path = location is null ? "" : location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0];
        return response.StatusCode == HttpStatusCode.Forbidden || (response.StatusCode == HttpStatusCode.Redirect && path is "/admin/login" or "/admin/access-denied");
    }
    private static void Assert(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
