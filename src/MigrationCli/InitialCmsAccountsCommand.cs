using System.Data;
using System.Security.AccessControl;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LandPortal.MigrationCli;

// Explicit operator bootstrap for the allocated shared website only. Never run
// at Web startup, never reset an existing user or modify an existing role set.
public static class InitialCmsAccountsCommand
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        string Required(string key)
        {
            var i=Array.IndexOf(args,key);
            return i>=0&&i+1<args.Length?args[i+1]:throw new InvalidOperationException("Required option: "+key);
        }
        var mode=Required("--mode");
        if(mode is not ("inspect" or "create"))throw new InvalidOperationException("Mode must be inspect or create.");
        var config=new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(Required("--config")),false)
            .AddInMemoryCollection(new Dictionary<string,string?> { ["DOTNET_ENVIRONMENT"]="Staging", ["ASPNETCORE_ENVIRONMENT"]="Staging" }).Build();
        var sql=new SqlConnectionStringBuilder(DatabaseSettings.Connection(config));
        if(!UatDeployment.Enabled(config)||sql.DataSource!="45.120.229.43,1433"||sql.InitialCatalog!="website_tin_tuc"||sql.UserID!="website_tin_tuc_app"||sql.IntegratedSecurity)
            throw new InvalidOperationException("Exact shared runtime target required; no operator/other database connection permitted.");
        var services=new ServiceCollection();services.AddLogging();
        services.AddScoped(_=>new PortalDbContext(DatabaseSettings.Options(config)));
        services.AddIdentityCore<PortalUser>(PortalIdentityPolicy.Configure).AddRoles<IdentityRole>()
            .AddUserValidator<OptionalEmailUserValidator>().AddEntityFrameworkStores<PortalDbContext>();
        using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        await db.Database.OpenConnectionAsync();
        await using(var command=db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText="SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME(),ORIGINAL_LOGIN(),USER_NAME()";
            await using var reader=await command.ExecuteReaderAsync();
            if(!await reader.ReadAsync()||reader.GetString(0)!="WIN-S1IBJB84OOO"||reader.GetString(1)!="website_tin_tuc"||reader.GetString(2)!="website_tin_tuc_app"||reader.GetString(3)!="LandPortalRuntime")
                throw new InvalidOperationException("Actual SQL server/database/login/application role mismatch.");
        }
        var migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray();
        if(migrations.Length!=17||migrations[^1]!="20261102042625_DraftConsultationEmail")throw new InvalidOperationException("Expected schema 17 required; no migration performed.");
        if(mode=="inspect")
        {
            Console.WriteLine(JsonSerializer.Serialize(new{status="READ_ONLY",database="website_tin_tuc",users=await db.Users.CountAsync(),roles=await db.Roles.CountAsync(),migrations=migrations.Length}));return 0;
        }
        var output=Path.GetFullPath(Required("--credential-root"));
        var workspace=Path.GetFullPath(PortalConfiguration.FindWorkspace()).TrimEnd(Path.DirectorySeparatorChar);
        if(output.Equals(workspace,StringComparison.OrdinalIgnoreCase)||output.StartsWith(workspace+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(output)||File.Exists(output))
            throw new InvalidOperationException("New private credential directory outside repository required.");
        if(!OperatingSystem.IsWindows())throw new InvalidOperationException("Windows operator ACL required.");
        for(string? node=output;node!=null;node=Path.GetDirectoryName(node))
            if((Directory.Exists(node)||File.Exists(node))&&(File.GetAttributes(node)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Reparse credential path refused.");

        var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'LandPortal.IdentityAdministration',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=0; IF @r<0 THROW 51000,'Identity administration busy',1;");
        if(await db.Users.AnyAsync()||await db.Roles.AnyAsync()||await db.UserClaims.AnyAsync()||await db.RoleClaims.AnyAsync()||await db.UserRoles.AnyAsync())
            throw new InvalidOperationException("Initial bootstrap requires empty Identity; existing accounts/roles preserved. Reconcile before retry.");
        var before=await ContentCounts(db);
        var directory=Directory.CreateDirectory(output);
        var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
        foreach(var sid in new[]{WindowsIdentity.GetCurrent().User!,new SecurityIdentifier("S-1-5-18")})
            acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        directory.SetAccessControl(acl);
        var allowedSids=new[]{WindowsIdentity.GetCurrent().User!.Value,"S-1-5-18"};
        var actual=directory.GetAccessControl();
        if(!actual.AreAccessRulesProtected)throw new InvalidOperationException("Private credential ACL inheritance must be disabled.");
        foreach(FileSystemAccessRule rule in actual.GetAccessRules(true,true,typeof(SecurityIdentifier)))
            if(rule.AccessControlType==AccessControlType.Allow&&!allowedSids.Contains(rule.IdentityReference.Value))
                throw new InvalidOperationException("Private credential ACL validation failed.");
        var delivery=new TemporaryPasswordDelivery(output);
        var specs=new[]{
            ("admin","Administrator","Quản trị",Permissions.All),
            ("review","Reviewer","Người duyệt",new[]{Permissions.ViewAll,Permissions.EditAll,Permissions.Review,Permissions.PublishApproved,Permissions.Unpublish,Permissions.ManageMedia,Permissions.CommentRead,Permissions.CommentModerate,Permissions.InquiryList,Permissions.InquiryRead,Permissions.InquiryRespond}),
            ("writer","Writer","Người viết",new[]{Permissions.Create,Permissions.ViewOwn,Permissions.EditOwn,Permissions.Submit,Permissions.ManageMedia})
        };
        var credentials=new List<object>();
        var secrets=new List<(PortalUser User,string Password,string Role,string[] Permissions)>();
        var actor="owner-approved-initial-cms:"+WindowsIdentity.GetCurrent().User!.Value;
        foreach(var (name,roleName,label,permissions) in specs)
        {
            var role=new IdentityRole(roleName);Check(await roles.CreateAsync(role));
            foreach(var permission in permissions)Check(await roles.AddClaimAsync(role,new Claim("permission",permission)));
            var password=PortalIdentityPolicy.NewTemporaryPassword();
            var user=new PortalUser{UserName=name,DisplayName=label,Email=null,EmailConfirmed=false,LockoutEnabled=true,MustChangePassword=true};
            var file=delivery.Write(user.Id,name,password);
            Check(await users.CreateAsync(user,password));Check(await users.AddToRoleAsync(user,roleName));
            db.AuditLogs.Add(new(){Actor=actor,Action="AccountBootstrap",Detail=JsonSerializer.Serialize(new{user.Id,user.UserName,role=roleName,permissions,emailOmitted=true,mustChangePassword=true,method="Explicit owner-authorized initial Identity bootstrap"})});
            credentials.Add(new{userName=name,role=roleName,credentialFile=file,mustChangePassword=true,loginUrl="https://gdla.gov.vn/admin/login"});
            secrets.Add((user,password,roleName,permissions));
        }
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        foreach(var (original,password,role,permissions) in secrets)
        {
            var user=await users.FindByIdAsync(original.Id)??throw new InvalidOperationException("Missing newly created user");
            if(!await users.CheckPasswordAsync(user,password)||user.Email is not null||!user.MustChangePassword||user.IsDisabled||!user.LockoutEnabled||await users.IsLockedOutAsync(user)||!(await users.GetRolesAsync(user)).SequenceEqual(new[]{role}))
                throw new InvalidOperationException("Identity readback mismatch; transaction rolled back.");
            var storedRole=(await roles.FindByNameAsync(role))!;
            if(!(await roles.GetClaimsAsync(storedRole)).Where(x=>x.Type=="permission").Select(x=>x.Value).Order().SequenceEqual(permissions.Order()))
                throw new InvalidOperationException("Role permissions readback mismatch.");
            if((await new CurrentPermissionService(db).GetAsync(user.Id)).Permissions.Count!=0)
                throw new InvalidOperationException("First-login authorization gate must remain closed.");
        }
        if(await db.Users.CountAsync()!=3||await db.Roles.CountAsync()!=3||await ContentCounts(db)!=before)
            throw new InvalidOperationException("Unexpected count delta; transaction rolled back.");
        await transaction.CommitAsync();
        var report=new{status="CREATED_VERIFIED",server="WIN-S1IBJB84OOO",database="website_tin_tuc",accounts=credentials,passwordVerification="MATCH",contentCountsUnchanged=before,sqlSchemaChanged=false,requiresIisOptionalEmailBinary=true};
        await File.WriteAllTextAsync(Path.Combine(output,"handoff.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(report));return 0;
    }
    private static void Check(IdentityResult result)
    {
        if(!result.Succeeded)throw new InvalidOperationException("Identity rejected: "+string.Join(',',result.Errors.Select(e=>e.Code)));
    }
    private static async Task<string> ContentCounts(PortalDbContext db)=>string.Join('/',await db.Contents.CountAsync(),await db.Revisions.CountAsync(),await db.MediaAssets.CountAsync(),await db.SourceUrlAliases.CountAsync(),await db.Categories.CountAsync());
}
