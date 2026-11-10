using System.Data;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Explicit owner-approved LAN-only Windows operator command, not a CMS endpoint.
// Uses the same authenticated Windows ADMIN/local integrated SQL boundary as the
// existing LAN operator tool. It cannot grant arbitrary permissions or roles.
[SupportedOSPlatform("windows")]
public static class LanReviewerPermissions
{
    public static string Stage { get; private set; } = "target-operator-guard";
    private static readonly string[] Approved = [Permissions.CommentRead, Permissions.CommentModerate,
        Permissions.InquiryList, Permissions.InquiryRead, Permissions.InquiryRespond];
    private static readonly string[] Existing = [Permissions.EditAll, Permissions.PublishApproved,
        Permissions.Review, Permissions.Unpublish, Permissions.ViewAll, Permissions.ManageMedia];

    public static async Task RunAsync(IConfiguration config, PortalDbContext db, string[] args)
    {
        if(args.Length != 1 && !(args.Length == 2 && args[1] == "--apply-approved-five"))
            throw new InvalidOperationException("Only inspect or exact approved-five operation is supported.");
        if(!LanDemoDeployment.Validate(config,"Development"))throw new InvalidOperationException("LAN profile required.");
        using var who=WindowsIdentity.GetCurrent();
        var sid=who.User??throw new UnauthorizedAccessException("Windows identity missing.");
        if(!who.IsAuthenticated || !who.Name.EndsWith("\\ADMIN",StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Authenticated Windows ADMIN required.");
        if(Approved.Any(p=>!Permissions.All.Contains(p,StringComparer.Ordinal)))throw new InvalidOperationException("Catalog mismatch.");
        await db.Database.OpenConnectionAsync();
        using(var command=db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText="SELECT DB_NAME(),CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)),ORIGINAL_LOGIN()";
            using var r=await command.ExecuteReaderAsync();
            if(!await r.ReadAsync() || r.GetString(0)!=LanDemoDeployment.Database ||
                !r.GetString(1).Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase) ||
                !r.GetString(2).Equals(who.Name,StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Live local database/operator mismatch.");
        }
        var services=new ServiceCollection();services.AddLogging();services.AddScoped(_=>db);
        services.AddIdentity<PortalUser,IdentityRole>(PortalIdentityPolicy.Configure).AddUserValidator<OptionalEmailUserValidator>().AddEntityFrameworkStores<PortalDbContext>();
        using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();
        var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'LandPortal.IdentityAdministration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000; IF @result<0 THROW 51001, 'Identity administration is busy.', 1;");
        Stage="scope-guard";
        var role=await roles.FindByNameAsync("LanDemo.reviewer")??throw new InvalidOperationException("Existing role required.");
        var members=await users.GetUsersInRoleAsync(role.Name!);
        if(role.Name!="LanDemo.reviewer" || members.Count!=1 || members[0].UserName!="lan-ba-reviewer")throw new InvalidOperationException("Exact single reviewer membership required.");
        var user=members[0];
        if(user.IsDisabled || user.MustChangePassword || await users.IsLockedOutAsync(user) ||
            !(await users.GetRolesAsync(user)).SequenceEqual(["LanDemo.reviewer"]))throw new InvalidOperationException("Reviewer state mismatch.");
        var beforeClaims=await roles.GetClaimsAsync(role);
        var beforePermissions=beforeClaims.Where(x=>x.Type=="permission").Select(x=>x.Value).Order().ToArray();
        if(!Existing.All(beforePermissions.Contains) || beforePermissions.Except(Existing.Concat(Approved)).Any() ||
            beforePermissions.Distinct().Count()!=beforePermissions.Length)throw new InvalidOperationException("Unexpected existing permission scope.");
        var missing=Approved.Except(beforePermissions).Order().ToArray();
        async Task<string> Preserved()
        {
            var allUsers=await db.Users.AsNoTracking().OrderBy(x=>x.Id).ToListAsync();
            var reviewer=allUsers.Single(x=>x.Id==user.Id);reviewer.SecurityStamp=null;reviewer.ConcurrencyStamp=null;
            var allRoles=await db.Roles.AsNoTracking().OrderBy(x=>x.Id).ToListAsync();allRoles.Single(x=>x.Id==role.Id).ConcurrencyStamp=null;
            var memberships=await db.UserRoles.AsNoTracking().OrderBy(x=>x.UserId).ThenBy(x=>x.RoleId).ToListAsync();
            var ownClaims=await db.UserClaims.AsNoTracking().OrderBy(x=>x.Id).ToListAsync();
            var roleClaims=await db.RoleClaims.AsNoTracking().Where(x=>x.RoleId!=role.Id || x.ClaimType!="permission" || !Approved.Contains(x.ClaimValue!)).OrderBy(x=>x.Id).ToListAsync();
            return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{allUsers,allRoles,memberships,ownClaims,roleClaims})));
        }
        var before=await Preserved();var stamp=user.SecurityStamp;
        if(args.Length==1 || missing.Length==0)
        {
            await tx.RollbackAsync();
            Console.WriteLine(JsonSerializer.Serialize(new{status=missing.Length==0?"ALREADY_GRANTED":"INSPECT_READY",database=LanDemoDeployment.Database,role=role.Name,missing,current=beforePermissions,writes=0}));return;
        }
        Stage="identity-grant";
        static void Check(IdentityResult result){if(!result.Succeeded)throw new InvalidOperationException("Identity operation rejected.");}
        foreach(var permission in missing)Check(await roles.AddClaimAsync(role,new Claim("permission",permission)));
        Check(await users.UpdateSecurityStampAsync(user));
        var actual=(await roles.GetClaimsAsync(role)).Where(x=>x.Type=="permission").Select(x=>x.Value).Order().ToArray();
        if(!actual.SequenceEqual(Existing.Concat(Approved).Order()) || stamp==user.SecurityStamp || before!=await Preserved())
            throw new InvalidOperationException("Readback/preservation mismatch; rollback.");
        db.AuditLogs.Add(new AuditLog{Actor=sid.Value,Action="LanReviewerPermissionsGranted",Detail=JsonSerializer.Serialize(new{database=LanDemoDeployment.Database,roleId=role.Id,role=role.Name,userId=user.Id,added=missing,before=beforePermissions,after=actual,authority="OwnerApprovedBAFivePermissions20261030",operatorAuthentication="WindowsIntegrated",sessionsRevoked=true,passwordAndOtherAccountsPreserved=true})});
        await db.SaveChangesAsync();await tx.CommitAsync();
        Stage="committed";
        Console.WriteLine(JsonSerializer.Serialize(new{status="APPLIED_VERIFIED",database=LanDemoDeployment.Database,role=role.Name,added=missing,permissions=actual,sessionsRevoked=true,preserved=true}));
    }
}
