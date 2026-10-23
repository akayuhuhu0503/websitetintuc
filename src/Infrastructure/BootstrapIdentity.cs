using System.Security.Claims;
using LandPortal.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace LandPortal.Infrastructure;
public static class BootstrapIdentity
{
    public static async Task InitializeAsync(IServiceProvider provider, IConfiguration config)
    {
        var email = config["Bootstrap:AdminEmail"];
        var password = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) throw new InvalidOperationException("Bootstrap cần email và mật khẩu riêng trong cấu hình private; chưa tạo tài khoản hoặc nhóm quyền.");
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        // Validate before creating any role/claim. A rejected first-use password
        // must not leave a partially provisioned role set behind.
        foreach(var validator in users.PasswordValidators)Check(await validator.ValidateAsync(users,new PortalUser{UserName=email,Email=email},password));
        foreach (var (role, permissions) in new[] { ("Administrator", Permissions.All), ("Editor", new[] { Permissions.EditOwn, Permissions.Submit }), ("Publisher", new[] { Permissions.EditOwn, Permissions.EditAll, Permissions.Submit, Permissions.Publish, Permissions.Review }) })
        {
            if (await roles.FindByNameAsync(role) == null)
            {
                var entity = new IdentityRole(role);
                Check(await roles.CreateAsync(entity));
                foreach (var permission in permissions) Check(await roles.AddClaimAsync(entity, new Claim("permission", permission)));
            }
        }
        // Creation is once only. Existing user credentials/roles are never changed on startup.
        if (await users.FindByEmailAsync(email) != null) return;
        var user = new PortalUser { UserName = email, Email = email, EmailConfirmed = true };
        Check(await users.CreateAsync(user, password));
        Check(await users.AddToRoleAsync(user, "Administrator"));
    }
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Không thể bootstrap Identity: " + string.Join(", ", result.Errors.Select(x => x.Code)));
    }
}
