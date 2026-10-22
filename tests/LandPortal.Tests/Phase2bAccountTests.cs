using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LandPortal.Tests;

public static class Phase2bAccountTests
{
    public static IEnumerable<(string Name, Func<Task> Test)> Cases(DbContextOptions<PortalDbContext> options)
    {
        yield return ("2B Identity catalog and role name never imply authority", async () =>
        {
            using var provider = Provider(options); var admin = await Fixture(provider, Permissions.All); var empty = await Fixture(provider, []);
            using var scope = provider.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
            await Expect<InvalidOperationException>(() => service.SaveRoleAsync(admin.UserId, null, null, "QA-2B injected " + Guid.NewGuid(), ["invented.admin.permission"]));
            var actor = await scope.ServiceProvider.GetRequiredService<CurrentPermissionService>().GetAsync(empty.UserId);
            Assert(!actor.Permissions.Any(), "An empty role gained permissions by its name.");
            await Expect<UnauthorizedAccessException>(() => service.SaveRoleAsync(empty.UserId, null, null, "QA-2B unauthorized " + Guid.NewGuid(), []));
        });
        yield return ("2B Identity ManageUsers cannot assign publishing roles or self elevate", async () =>
        {
            using var provider = Provider(options); var manager = await Fixture(provider, [Permissions.ManageUsers]); var writer = await Fixture(provider, [Permissions.EditOwn]); var publisher = await Fixture(provider, [Permissions.PublishDirect]);
            using var scope = provider.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
            var stamp = await Stamp(scope, writer.UserId);
            await Expect<UnauthorizedAccessException>(() => service.AssignRolesAsync(manager.UserId, writer.UserId, stamp, [publisher.RoleId]));
            var ownStamp = await Stamp(scope, manager.UserId);
            await Expect<UnauthorizedAccessException>(() => service.AssignRolesAsync(manager.UserId, manager.UserId, ownStamp, [publisher.RoleId]));
            Assert(!(await scope.ServiceProvider.GetRequiredService<CurrentPermissionService>().GetAsync(manager.UserId)).Has(Permissions.PublishDirect), "Profile management implied publishing.");
        });
        yield return ("2B Identity delegation boundary blocks unheld and privileged permission grants", async () =>
        {
            using var provider = Provider(options); var delegated = await Fixture(provider, [Permissions.ManageRoles, Permissions.GrantPermissions, Permissions.EditOwn]);
            using var scope = provider.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
            await Expect<UnauthorizedAccessException>(() => service.SaveRoleAsync(delegated.UserId, null, null, "QA-2B forbidden publisher " + Guid.NewGuid(), [Permissions.PublishDirect]));
            await Expect<UnauthorizedAccessException>(() => service.SaveRoleAsync(delegated.UserId, null, null, "QA-2B forbidden grant " + Guid.NewGuid(), [Permissions.GrantPermissions]));
            var ordinary = await service.SaveRoleAsync(delegated.UserId, null, null, "QA-2B delegated writer " + Guid.NewGuid(), [Permissions.EditOwn]);
            Assert(await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Roles.AnyAsync(x => x.Id == ordinary), "An authorized ordinary grant failed.");
        });
        yield return ("2B Identity own shared role cannot be edited to elevate privileges", async () =>
        {
            using var provider = Provider(options); var admin = await Fixture(provider, Permissions.All);
            using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>(); var role = await db.Roles.AsNoTracking().SingleAsync(x => x.Id == admin.RoleId);
            var service = scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
            await Expect<UnauthorizedAccessException>(() => service.SaveRoleAsync(admin.UserId, role.Id, role.ConcurrencyStamp, role.Name!, [Permissions.ManageRoles]));
            var ownStamp = await Stamp(scope, admin.UserId);
            await Expect<UnauthorizedAccessException>(() => service.AssignRolesAsync(admin.UserId, admin.UserId, ownStamp, []));
        });
        yield return ("2B Identity shared role revocation immediately changes effective rights and all member stamps", async () =>
        {
            using var provider = Provider(options); var operatorUser = await Fixture(provider, Permissions.All); var member = await Fixture(provider, [Permissions.EditOwn, Permissions.PublishDirect]);
            using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>(); var role = await db.Roles.AsNoTracking().SingleAsync(x => x.Id == member.RoleId);
            var beforeStamp = await db.Users.AsNoTracking().Where(x => x.Id == member.UserId).Select(x => x.SecurityStamp).SingleAsync();
            await scope.ServiceProvider.GetRequiredService<AccountAdministrationService>().SaveRoleAsync(operatorUser.UserId, role.Id, role.ConcurrencyStamp, role.Name!, [Permissions.EditOwn]);
            using var after = provider.CreateScope(); var actor = await after.ServiceProvider.GetRequiredService<CurrentPermissionService>().GetAsync(member.UserId);
            Assert(!actor.Has(Permissions.PublishDirect) && actor.Has(Permissions.EditOwn), "A shared role retained revoked publication.");
            var freshStamp = await after.ServiceProvider.GetRequiredService<PortalDbContext>().Users.AsNoTracking().Where(x => x.Id == member.UserId).Select(x => x.SecurityStamp).SingleAsync();
            Assert(beforeStamp != freshStamp, "The shared role change did not revoke the existing session stamp.");
        });
        yield return ("2B Identity disabled accounts retain history and session revocation never resets password", async () =>
        {
            using var provider = Provider(options); var admin = await Fixture(provider, Permissions.All); var writer = await Fixture(provider, [Permissions.EditOwn]);
            using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>(); var before = await db.Users.AsNoTracking().SingleAsync(x => x.Id == writer.UserId);
            await scope.ServiceProvider.GetRequiredService<AccountAdministrationService>().SetDisabledAsync(admin.UserId, writer.UserId, before.ConcurrencyStamp!, true);
            using var after = provider.CreateScope(); var user = await after.ServiceProvider.GetRequiredService<PortalDbContext>().Users.AsNoTracking().SingleAsync(x => x.Id == writer.UserId);
            Assert(user.IsDisabled && user.PasswordHash == before.PasswordHash && user.SecurityStamp != before.SecurityStamp, "Disable deleted/reset the account or retained its session.");
            Assert(!(await after.ServiceProvider.GetRequiredService<CurrentPermissionService>().GetAsync(writer.UserId)).Permissions.Any(), "A disabled account retained permissions.");
            var audit = await after.ServiceProvider.GetRequiredService<PortalDbContext>().AuditLogs.AsNoTracking().Where(x => x.Action == "AccountDisabled" && x.Actor == admin.UserId).Select(x => x.Detail).ToListAsync();
            Assert(audit.Any(x => x.Contains(writer.UserId)) && audit.All(x => !x.Contains("Password") && !x.Contains("SecurityStamp")), "Disable audit is absent or contains secrets.");
        });
        yield return ("2B Identity temporary-password account is forced to change and begins with no role", async () =>
        {
            using var provider = Provider(options); var admin = await Fixture(provider, Permissions.All);
            using var scope = provider.CreateScope(); var name = "QA-2B-Temp-" + Guid.NewGuid().ToString("N");
            var id = await scope.ServiceProvider.GetRequiredService<AccountAdministrationService>().CreateUserAsync(admin.UserId, name, name + "@example.invalid", "QA temporary account", Guid.NewGuid().ToString("N") + "Aa!7");
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>(); var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert(user.MustChangePassword && !await db.UserRoles.AnyAsync(x => x.UserId == id), "Temporary user did not require change or gained a role.");
            Assert(!(await scope.ServiceProvider.GetRequiredService<CurrentPermissionService>().GetAsync(id)).Permissions.Any(), "Temporary-password user could use sensitive permissions.");
        });
        yield return ("2B Identity optimistic concurrency prevents stale profile and role overwrite", async () =>
        {
            using var provider = Provider(options); var admin = await Fixture(provider, Permissions.All); var writer = await Fixture(provider, [Permissions.EditOwn]);
            string stamp; using (var before = provider.CreateScope()) stamp = await Stamp(before, writer.UserId);
            using (var first = provider.CreateScope()) await first.ServiceProvider.GetRequiredService<AccountAdministrationService>().UpdateProfileAsync(admin.UserId, writer.UserId, stamp, writer.UserName + "@example.invalid", "QA first edit");
            using var stale = provider.CreateScope(); await Expect<DbUpdateConcurrencyException>(() => stale.ServiceProvider.GetRequiredService<AccountAdministrationService>().UpdateProfileAsync(admin.UserId, writer.UserId, stamp, writer.UserName + "@example.invalid", "QA stale edit"));
            Assert(await stale.ServiceProvider.GetRequiredService<PortalDbContext>().Users.AsNoTracking().Where(x => x.Id == writer.UserId).Select(x => x.DisplayName).SingleAsync() == "QA first edit", "A stale profile overwrote a newer edit.");
        });
        yield return ("2B Identity two concurrent reciprocal locks preserve a combined administration path", async () =>
        {
            using var provider = Provider(options); var a = await Fixture(provider, Permissions.All); var b = await Fixture(provider, Permissions.All);
            string aStamp, bStamp; using (var before = provider.CreateScope()) { aStamp = await Stamp(before, a.UserId); bStamp = await Stamp(before, b.UserId); }
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<bool> Lock(string actor, string target, string stamp)
            {
                using var scope = provider.CreateScope(); await start.Task;
                try { await scope.ServiceProvider.GetRequiredService<AccountAdministrationService>().SetDisabledAsync(actor, target, stamp, true); return true; }
                catch (UnauthorizedAccessException) { return false; }
                catch (InvalidOperationException) { return false; }
            }
            var first = Lock(a.UserId, b.UserId, bStamp); var second = Lock(b.UserId, a.UserId, aStamp); start.SetResult(); var results = await Task.WhenAll(first, second);
            using var after = provider.CreateScope(); var db = after.ServiceProvider.GetRequiredService<PortalDbContext>();
            var remaining = await db.Users.AsNoTracking().CountAsync(x => (x.Id == a.UserId || x.Id == b.UserId) && !x.IsDisabled);
            Assert(results.Count(x => x) == 1 && remaining == 1, "Concurrent reciprocal requests removed both administrators.");
        });
        yield return ("2B Identity two concurrent reciprocal role reductions preserve effective administration", async () =>
        {
            using var provider = Provider(options); var a = await Fixture(provider, Permissions.All); var b = await Fixture(provider, Permissions.All);
            string aStamp, bStamp; using (var before = provider.CreateScope()) { var db = before.ServiceProvider.GetRequiredService<PortalDbContext>(); aStamp = (await db.Roles.AsNoTracking().SingleAsync(x => x.Id == a.RoleId)).ConcurrencyStamp!; bStamp = (await db.Roles.AsNoTracking().SingleAsync(x => x.Id == b.RoleId)).ConcurrencyStamp!; }
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<bool> Reduce(string actor, string targetRole, string stamp)
            {
                using var scope = provider.CreateScope(); await start.Task;
                try { await scope.ServiceProvider.GetRequiredService<AccountAdministrationService>().SaveRoleAsync(actor, targetRole, stamp, "QA-2B reduced " + targetRole, [Permissions.EditOwn]); return true; }
                catch (UnauthorizedAccessException) { return false; }
                catch (InvalidOperationException) { return false; }
            }
            var first = Reduce(a.UserId, b.RoleId, bStamp); var second = Reduce(b.UserId, a.RoleId, aStamp); start.SetResult(); var results = await Task.WhenAll(first, second);
            using var after = provider.CreateScope(); var current = after.ServiceProvider.GetRequiredService<CurrentPermissionService>(); var actors = new[] { await current.GetAsync(a.UserId), await current.GetAsync(b.UserId) };
            Assert(results.Count(x => x) == 1 && actors.Count(x => x.Has(Permissions.ManageUsers) && x.Has(Permissions.ManageRoles) && x.Has(Permissions.GrantPermissions)) == 1, "Concurrent shared-role edits removed both administration paths.");
        });
    }
    internal static ServiceProvider Provider(DbContextOptions<PortalDbContext> options)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(options); services.AddScoped<PortalDbContext>();
        services.AddIdentityCore<PortalUser>(options => { options.User.RequireUniqueEmail = true; options.Password.RequiredLength = 14; })
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<PortalDbContext>();
        services.AddScoped<CurrentPermissionService>(); services.AddScoped<AccountAdministrationService>(); return services.BuildServiceProvider();
    }
    internal static async Task<(string UserId, string RoleId, string UserName)> Fixture(ServiceProvider provider, IEnumerable<string> permissions)
    {
        using var scope = provider.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>(); var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var name = "QA-2B-Identity-" + Guid.NewGuid().ToString("N"); var user = new PortalUser { UserName = name, Email = name + "@example.invalid", EmailConfirmed = true, LockoutEnabled = true };
        Check(await users.CreateAsync(user, Guid.NewGuid().ToString("N") + "Aa!7")); var role = new IdentityRole("QA-2B Identity " + Guid.NewGuid().ToString("N")); Check(await roles.CreateAsync(role));
        foreach (var permission in permissions.Distinct()) Check(await roles.AddClaimAsync(role, new Claim("permission", permission)));
        Check(await users.AddToRoleAsync(user, role.Name!)); return (user.Id, role.Id, name);
    }
    private static Task<string> Stamp(IServiceScope scope, string id) => scope.ServiceProvider.GetRequiredService<PortalDbContext>().Users.AsNoTracking().Where(x => x.Id == id).Select(x => x.ConcurrencyStamp!).SingleAsync();
    private static void Check(IdentityResult result) { if (!result.Succeeded) throw new InvalidOperationException("Test fixture Identity failed: " + string.Join(",", result.Errors.Select(x => x.Code))); }
    private static void Assert(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
