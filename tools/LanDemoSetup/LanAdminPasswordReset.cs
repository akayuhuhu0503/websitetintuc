using System.Globalization;
using System.Net;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[SupportedOSPlatform("windows")]
public static class LanAdminPasswordReset
{
    public static string Stage { get; private set; }="target-guard";
    public static async Task RunAsync(IConfiguration config,PortalDbContext db,string[] args)
    {
        if(!LanDemoDeployment.Validate(config,"Development"))throw new InvalidOperationException("Exact password-only LAN target required.");
        var who=WindowsIdentity.GetCurrent();var sid=who.User??throw new InvalidOperationException("Windows identity missing.");
        if(!who.Name.EndsWith("\\ADMIN",StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Windows ADMIN required.");
        var at=Array.IndexOf(args,"--credentials");if(at<0||at+1>=args.Length)throw new InvalidOperationException("Protected output file required.");
        var path=Path.GetFullPath(args[at+1]);var parent=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"LandPortal-LanDemo-Credentials")+Path.DirectorySeparatorChar;
        if(!path.StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Credentials must stay outside Git in the ADMIN profile.");
        for(string? node=path;node!=null;node=Path.GetDirectoryName(node))if((File.Exists(node)||Directory.Exists(node))&&(File.GetAttributes(node)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Reparse credential path refused.");
        var verifyExisting=args.Contains("--verify-existing");
        var info=new FileInfo(path);if(!info.Exists||!verifyExisting&&info.Length!=0||verifyExisting&&info.Length==0)throw new InvalidOperationException("Protected credential file state mismatch; no overwrite.");
        var acl=info.GetAccessControl();var rules=acl.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        if(!acl.AreAccessRulesProtected||!sid.Equals(acl.GetOwner(typeof(SecurityIdentifier)))||rules.Length!=1||!sid.Equals(rules[0].IdentityReference)||rules[0].AccessControlType!=AccessControlType.Allow||rules[0].FileSystemRights!=FileSystemRights.FullControl)throw new UnauthorizedAccessException("Credential ACL must allow only the current ADMIN SID.");
        await db.Database.OpenConnectionAsync();using(var command=db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText="SELECT DB_NAME(),CAST(SERVERPROPERTY('MachineName') AS nvarchar(128))";using var reader=await command.ExecuteReaderAsync();
            if(!await reader.ReadAsync()||reader.GetString(0)!=LanDemoDeployment.Database||!reader.GetString(1).Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Live database identity mismatch.");
        }
        var services=new ServiceCollection();services.AddLogging();services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(StorageOptions.FromConfiguration(config).KeysRoot)).SetApplicationName("LandPortal");services.AddScoped(_=>db);
        services.AddIdentity<PortalUser,IdentityRole>(PortalIdentityPolicy.Configure).AddEntityFrameworkStores<PortalDbContext>().AddDefaultTokenProviders();
        using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();var manager=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var user=await manager.FindByNameAsync("lan-ba-admin")??throw new InvalidOperationException("Existing admin missing.");
        if(user.UserName!="lan-ba-admin"||user.IsDisabled||await manager.IsLockedOutAsync(user))throw new InvalidOperationException("Account disabled or locked; no password change.");
        async Task<string> PreservedDigest()
        {
            var others=await db.Users.AsNoTracking().Where(x=>x.Id!=user.Id).OrderBy(x=>x.Id).ToListAsync();
            var roles=await db.UserRoles.AsNoTracking().OrderBy(x=>x.UserId).ThenBy(x=>x.RoleId).ToListAsync();
            var claims=await db.UserClaims.AsNoTracking().OrderBy(x=>x.Id).ToListAsync();var roleClaims=await db.RoleClaims.AsNoTracking().OrderBy(x=>x.Id).ToListAsync();
            return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{others,roles,claims,roleClaims})));
        }
        static void Ok(IdentityResult result){if(!result.Succeeded)throw new InvalidOperationException("Identity password reset rejected: "+string.Join(',',result.Errors.Select(x=>x.Code)));}
        var before=await PreservedDigest();string password;
        if(verifyExisting)
        {
            using var credentials=JsonDocument.Parse(await File.ReadAllTextAsync(path));
            if(credentials.RootElement.GetProperty("userName").GetString()!="lan-ba-admin"||credentials.RootElement.GetProperty("database").GetString()!=LanDemoDeployment.Database)throw new InvalidOperationException("Credential scope mismatch.");
            password=credentials.RootElement.GetProperty("password").GetString()!;
        }
        else password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+','-').Replace('/','_');
        if(!verifyExisting){
        Stage="identity-reset";
        // Open the ACL-checked file before committing; secrets never enter CLI arguments or logs.
        await using(var output=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.None))
        await using(var transaction=await db.Database.BeginTransactionAsync())
        {
            var token=await manager.GeneratePasswordResetTokenAsync(user);Ok(await manager.ResetPasswordAsync(user,token,password));
            user.MustChangePassword=false;Ok(await manager.UpdateAsync(user));
            db.AuditLogs.Add(new AuditLog{Actor=sid.Value,Action="LanAdminPasswordReset",Detail=JsonSerializer.Serialize(new{user.Id,user.UserName,database=LanDemoDeployment.Database,mustChangePassword=false,ownerRequested=true,noSecretRecorded=true})});
            await db.SaveChangesAsync();if(before!=await PreservedDigest())throw new InvalidOperationException("Other accounts or permissions changed; rollback.");
            await JsonSerializer.SerializeAsync(output,new{userName=user.UserName,password,database=LanDemoDeployment.Database,loginUrl=config["Public:BaseUrl"]!.TrimEnd('/')+"/admin/login",mustChangePassword=false,createdUtc=DateTimeOffset.UtcNow},new JsonSerializerOptions{WriteIndented=true});
            await output.FlushAsync();output.Flush(true);await transaction.CommitAsync();
        }
        }
        Stage="identity-password-readback";
        if(!await manager.CheckPasswordAsync(user,password)||user.MustChangePassword)throw new InvalidOperationException("Reset readback failed.");
        using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new()}){BaseAddress=new(config["Public:BaseUrl"]!),Timeout=TimeSpan.FromSeconds(30)};
        Stage="login-form";
        using var login=await http.GetAsync("/admin/login");Console.WriteLine("Login form HTTP "+(int)login.StatusCode);if(!login.IsSuccessStatusCode)throw new InvalidOperationException("Login form unavailable after reset; credential file retained.");
        var parser=new HtmlParser();var doc=parser.ParseDocument(await login.Content.ReadAsStringAsync());
        Stage="captcha-form";
        var question=doc.QuerySelector("#captcha-question")?.TextContent??throw new InvalidOperationException("CAPTCHA missing.");var numbers=Regex.Matches(question,@"\d+").Select(x=>int.Parse(x.Value,CultureInfo.InvariantCulture)).ToArray();
        if(numbers.Length!=2||!question.Contains("cộng")&&!question.Contains("trừ"))throw new InvalidOperationException("Unknown CAPTCHA format.");
        var fields=new Dictionary<string,string>{["__RequestVerificationToken"]=doc.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!,["CaptchaId"]=doc.QuerySelector("input[name=CaptchaId]")!.GetAttribute("value")!,["CaptchaAnswer"]=(question.Contains("cộng")?numbers[0]+numbers[1]:numbers[0]-numbers[1]).ToString(CultureInfo.InvariantCulture),["UserName"]="lan-ba-admin",["Password"]=password,["ReturnUrl"]="/admin"};
        Stage="login-submit";
        using var signed=await http.PostAsync("/admin/login",new FormUrlEncodedContent(fields));Console.WriteLine("Login POST HTTP "+(int)signed.StatusCode);
        if(signed.StatusCode!=HttpStatusCode.Redirect||signed.Headers.Location?.OriginalString!="/admin")throw new InvalidOperationException("Reset committed but HTTP login did not reach admin; credential file retained.");
        Stage="dashboard";
        using var dashboard=await http.GetAsync("/admin");if(!dashboard.IsSuccessStatusCode)throw new InvalidOperationException("Dashboard unavailable after successful login.");
        var page=parser.ParseDocument(await dashboard.Content.ReadAsStringAsync());var csrf=page.QuerySelector("form[action='/admin/account/logout'] input[name=__RequestVerificationToken]")?.GetAttribute("value");
        if(csrf!=null){using var logout=await http.PostAsync("/admin/account/logout",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",csrf}}));}
        if(before!=await PreservedDigest())throw new InvalidOperationException("Other accounts/permissions changed during smoke.");
        Console.WriteLine("RESET PASS: lan-ba-admin / LandPortal_LanDemo; Identity reset, protected credential file, login/dashboard smoke PASS; no forced change; other accounts/roles preserved.");
    }
}
