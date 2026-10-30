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
        if (string.IsNullOrWhiteSpace(email)) throw new InvalidOperationException("Bootstrap cần email trong cấu hình private; chưa tạo tài khoản hoặc nhóm quyền.");
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        // Reruns never reset/re-flag existing users or change their roles.
        if(await users.FindByEmailAsync(email)!=null)return;
        var password=PortalIdentityPolicy.NewTemporaryPassword();
        var user = new PortalUser { UserName = email, Email = email, EmailConfirmed = true,LockoutEnabled=true,MustChangePassword=true };
        var file=new TemporaryPasswordDelivery(StorageOptions.FromConfiguration(config).TempRoot).Write(user.Id,email,password);
        var db=scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        await using var transaction=await db.Database.BeginTransactionAsync();
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
        Check(await users.CreateAsync(user, password));
        Check(await users.AddToRoleAsync(user, "Administrator"));
        db.AuditLogs.Add(new(){Actor=user.Id,Action="AccountBootstrap",Detail="New administrator; random private temporary credential; first-login password change required; no secret recorded"});
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        Console.WriteLine("Bootstrap account requires first-login password change. Temporary credential private file: "+file);
    }
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Không thể bootstrap Identity: " + string.Join(", ", result.Errors.Select(x => x.Code)));
    }
}
