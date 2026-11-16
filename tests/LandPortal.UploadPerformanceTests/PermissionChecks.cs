using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

static class PermissionChecks
{
    public static async Task Run(DbContextOptions<PortalDbContext> options, Action<bool,string> check)
    {
        await using var db = new PortalDbContext(options);
        var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(db.Database.GetConnectionString());
        if (!System.Text.RegularExpressions.Regex.IsMatch(connection.DataSource, @"^\(localdb\)\\LPPerf_[a-f0-9]{12}$") || connection.InitialCatalog != "LandPortal_Test")
            throw new InvalidOperationException("Isolated permission fixture only");
        var user = new PortalUser { UserName = "permission-fixture", NormalizedUserName = "PERMISSION-FIXTURE", DisplayName = "Synthetic permission check" };
        var role = new IdentityRole("permission-fixture");
        db.Users.Add(user); db.Roles.Add(role);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
        db.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = role.Id, ClaimType = "permission", ClaimValue = Permissions.PublishDirect });
        var edit = new IdentityUserClaim<string> { UserId = user.Id, ClaimType = "permission", ClaimValue = Permissions.EditOwn };
        db.UserClaims.Add(edit); await db.SaveChangesAsync();
        var service = new CurrentPermissionService(db);
        var actor = await service.GetAsync(user.Id);
        check(actor.Has(Permissions.EditOwn) && actor.Has(Permissions.Create) && actor.Has(Permissions.ViewOwn) && actor.Has(Permissions.PublishApproved) && actor.CategoryIds == null, "Permission projection retains role/own implications without cache");
        var scope = new IdentityUserClaim<string> { UserId = user.Id, ClaimType = "content.category.scope", ClaimValue = "restricted" };
        var category = new IdentityUserClaim<string> { UserId = user.Id, ClaimType = "content.category", ClaimValue = "1" };
        db.UserClaims.AddRange(scope, category); await db.SaveChangesAsync();
        check((await service.GetAsync(user.Id)).CategoryIds?.SetEquals(new[] { 1 }) == true, "Fresh category scope retained");
        category.ClaimValue = "invalid"; await db.SaveChangesAsync();
        check((await service.GetAsync(user.Id)).CategoryIds?.Count == 0, "Invalid category is empty scope, never unrestricted");
        db.UserClaims.Remove(edit); await db.SaveChangesAsync();
        check(!(await service.GetAsync(user.Id)).Has(Permissions.EditOwn), "Revocation visible in same service instance");
        user.IsDisabled = true; await db.SaveChangesAsync();
        check(!(await service.GetAsync(user.Id)).Has(Permissions.PublishApproved), "Disabled user denied");
        user.IsDisabled = false; user.MustChangePassword = true; await db.SaveChangesAsync();
        check(!(await service.GetAsync(user.Id)).Has(Permissions.PublishApproved), "First password change requirement retained");
        user.MustChangePassword = false; user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(2); await db.SaveChangesAsync();
        check(!(await service.GetAsync(user.Id)).Has(Permissions.PublishApproved), "Locked user denied");
        user.LockoutEnd = null;
        db.UserClaims.Add(new() { UserId = user.Id, ClaimType = "dev.demo", ClaimValue = "SRS1.2.DevDemo.v1" });
        db.UserClaims.Add(new() { UserId = user.Id, ClaimType = "dev.demo.expires", ClaimValue = DateTimeOffset.UtcNow.AddDays(-1).ToString("O") });
        await db.SaveChangesAsync();
        check(!(await service.GetAsync(user.Id)).Has(Permissions.PublishApproved), "Expired demo claim denied");
    }
}
