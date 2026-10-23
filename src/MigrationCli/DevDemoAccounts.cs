using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LandPortal.MigrationCli;

// Explicit owner-authorized local tooling, never invoked by Web startup or UAT setup.
public static class DevDemoAccounts
{
    public const string Marker="SRS1.2.DevDemo.v1";
    public static readonly string[] Writer=[Permissions.Create,Permissions.ViewOwn,Permissions.EditOwn,Permissions.Submit,Permissions.ManageMedia];
    public static readonly string[] Reviewer=[Permissions.ViewAll,Permissions.EditAll,Permissions.Review,Permissions.PublishApproved,Permissions.Unpublish,Permissions.ManageMedia,Permissions.CommentRead,Permissions.CommentModerate,Permissions.CommentReply,Permissions.InquiryList,Permissions.InquiryRead,Permissions.InquiryRespond];
    public static async Task ExecuteAsync(string[] args,IConfiguration config,string workspace)
    {
        string Arg(string name){var i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:throw new InvalidDataException(name+" required");}
        var database=config["Sql:ExpectedDatabase"];
        if(!OperatingSystem.IsWindows()||Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")!="Development"||database is not ("LandPortal_Test" or "LandPortal_Dev")||UatDeployment.Enabled(config))throw new InvalidDataException("Demo accounts are restricted to Windows local Development Dev/Test.");
        var services=new ServiceCollection();services.AddLogging();services.AddDataProtection();
        // Explicit operator command: retain real Windows SQL authority for target/backup guards.
        // The Web application role is deliberately not used to bootstrap identities.
        services.AddScoped<PortalDbContext>(_=>new PortalDbContext(DatabaseSettings.Options(config,forMigration:true)));
        services.AddIdentity<PortalUser,IdentityRole>(PortalIdentityPolicy.Configure).AddEntityFrameworkStores<PortalDbContext>().AddDefaultTokenProviders();
        using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        await SchemaManager.VerifyLocalProjectAsync(db,database);
        await db.Database.OpenConnectionAsync();
        using(var cmd=db.Database.GetDbConnection().CreateCommand())
        {
            cmd.CommandText="SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(200))";
            var actual=Convert.ToString(await cmd.ExecuteScalarAsync());
            if(Arg("--confirm-target")!=actual+"/"+database)throw new InvalidDataException("Exact physical SQL target confirmation differs.");
        }
        await ApprovedBatchExecution.VerifyBackupAsync(db,workspace,database,Arg("--backup-receipt"));
        var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var specs=new[]{("writer","Người viết",Writer),("reviewer","Người duyệt",Reviewer),("admin","Quản trị",Permissions.All)};
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource=N'dev-demo-accounts-v12',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;IF @r<0 THROW 51000,'Demo bootstrap busy',1;");
        var disable=args.Contains("--disable");var category=disable?0:int.Parse(Arg("--category"));
        if(!disable&&!await db.Categories.AnyAsync(x=>x.Id==category))throw new InvalidDataException("Assigned category does not exist.");
        var secrets=new List<object>();var expires=DateTimeOffset.UtcNow.AddDays(14);
        foreach(var (suffix,label,permissions) in specs)
        {
            var name="dev-srs12-"+suffix;var roleName="DevDemo.SRS12."+suffix;
            var user=await users.FindByNameAsync(name);
            if(user!=null)
            {
                var claims=await users.GetClaimsAsync(user);
                if(!claims.Any(c=>c.Type=="dev.demo"&&c.Value==Marker))throw new InvalidDataException("Existing account collision; no credentials or grants changed.");
                if(disable){user.IsDisabled=true;Check(await users.UpdateSecurityStampAsync(user));}
                // Never reset password, unlock, extend expiry or add rights on rerun.
                continue;
            }
            if(disable)continue;
            var role=await roles.FindByNameAsync(roleName);
            if(role==null){role=new(roleName);Check(await roles.CreateAsync(role));foreach(var permission in permissions)Check(await roles.AddClaimAsync(role,new("permission",permission)));}
            else if(!(await roles.GetClaimsAsync(role)).Where(c=>c.Type=="permission").Select(c=>c.Value).Order().SequenceEqual(permissions.Order()))throw new InvalidDataException("Demo role drift; stop without modifying existing roles.");
            var password="A!a7"+Convert.ToBase64String(RandomNumberGenerator.GetBytes(27));
            user=new(){UserName=name,Email=name+"@example.invalid",EmailConfirmed=true,DisplayName=label+" (giả lập Dev)",LockoutEnabled=true,MustChangePassword=true};
            Check(await users.CreateAsync(user,password));Check(await users.AddToRoleAsync(user,roleName));
            Check(await users.AddClaimsAsync(user,[new("dev.demo",Marker),new("dev.demo.expires",expires.ToString("O"))]));
            if(suffix!="admin")Check(await users.AddClaimsAsync(user,[new("content.category.scope","assigned"),new("content.category",category.ToString())]));
            secrets.Add(new{userName=name,password,firstLoginMustChangePassword=true,expiresUtc=expires});
        }
        string? path=null;
        if(secrets.Count>0)
        {
            path=Path.GetFullPath(Arg("--credentials"),workspace);
            var allowed=Path.GetFullPath(Path.Combine(workspace,".local/credentials"))+Path.DirectorySeparatorChar;
            if(!path.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!Directory.Exists(Path.GetDirectoryName(path))||File.Exists(path))throw new InvalidDataException("New presecured .local/credentials artifact required.");
            var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{database,accounts=secrets}));
            try{var encrypted=ProtectedData.Protect(bytes,Encoding.UTF8.GetBytes(Marker),DataProtectionScope.CurrentUser);using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);file.Write(encrypted);file.Flush(true);}finally{CryptographicOperations.ZeroMemory(bytes);}
        }
        db.AuditLogs.Add(new(){Actor="owner-authorized-dev-bootstrap",Action=disable?"DevDemoAccountsDisabled":"DevDemoAccountsCreated",Detail="SRS1.2 explicit Development scope; existing identities preserved; no credentials logged"});
        await db.SaveChangesAsync();await tx.CommitAsync();
        Console.WriteLine(disable?"Demo accounts disabled; existing non-demo accounts untouched.":$"Demo account bootstrap committed: {secrets.Count} new; existing accounts unchanged. Credentials encrypted for current Windows user; first login requires password change.");
    }
    static void Check(IdentityResult r){if(!r.Succeeded)throw new InvalidDataException("Demo Identity operation rejected: "+string.Join(",",r.Errors.Select(x=>x.Code)));}
}
