using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LandPortal.Tests;

// Real Identity validators/hash/token APIs with an isolated in-memory user store.
// This does not claim SQL/HTTP deployment acceptance or exercise the SQL service.
public static class BaPasswordOfflineTests
{
    public static async Task RunAsync(string root)
    {
        var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        var store=new MemoryUsers();services.AddSingleton<IUserStore<PortalUser>>(store);
        services.AddIdentityCore<PortalUser>(PortalIdentityPolicy.Configure).AddDefaultTokenProviders();
        using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();
        var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        Check(users.Options.Password.RequiredLength==8&&users.Options.Password.RequireUppercase&&users.Options.Password.RequireLowercase&&users.Options.Password.RequireDigit&&users.Options.Password.RequireNonAlphanumeric,"BA policy uses 8 and all four groups");
        Check(users.Options.Lockout.MaxFailedAccessAttempts==5&&users.Options.Lockout.DefaultLockoutTimeSpan==TimeSpan.FromMinutes(15),"Lockout remains five failures / fifteen minutes");
        foreach(var (password,code) in new[]{("Ab1!xyz","PasswordTooShort"),("abcdef1!","PasswordRequiresUpper"),("ABCDEF1!","PasswordRequiresLower"),("Abcdefg!","PasswordRequiresDigit"),("Abcdef12","PasswordRequiresNonAlphanumeric")})
        {
            var results=await Task.WhenAll(users.PasswordValidators.Select(v=>v.ValidateAsync(users,new PortalUser(),password)));
            Check(results.SelectMany(x=>x.Errors).Any(x=>x.Code==code),"Identity rejects "+code);
            var model=new ChangeOwnPasswordModel{CurrentPassword="BeforeA1!",NewPassword=password,ConfirmPassword=password};var validation=new List<ValidationResult>();
            Validator.TryValidateObject(model,new ValidationContext(model,scope.ServiceProvider,null),validation,true);
            Check(validation.Any(x=>x.ErrorMessage!.Contains(PortalIdentityPolicy.PasswordError(code),StringComparison.Ordinal)),"Visible Vietnamese message matches "+code);
        }
        var passwords=Enumerable.Range(0,32).Select(_=>PortalIdentityPolicy.NewTemporaryPassword()).ToArray();
        Check(passwords.Distinct().Count()==passwords.Length&&passwords.All(x=>x.Length==24),"Temporary passwords are independently random and 24 characters");
        foreach(var password in passwords)foreach(var validator in users.PasswordValidators)if(!(await validator.ValidateAsync(users,new(),password)).Succeeded)throw new InvalidOperationException("Generated password violates Identity policy");
        Check(true,"Generated passwords satisfy every configured Identity validator");
        var user=new PortalUser{UserName="offline-unit",Email="offline-unit@example.invalid",SecurityStamp=Guid.NewGuid().ToString(),MustChangePassword=true};
        Check((await users.CreateAsync(user,"StartA1!")).Succeeded,"Identity accepts exactly eight compliant characters");
        var oldStamp=user.SecurityStamp;
        Check(!(await users.ChangePasswordAsync(user,"incorrect","NextAb1!")).Succeeded&&user.MustChangePassword,"Wrong current password cannot clear first-login state");
        Check((await users.ChangePasswordAsync(user,"StartA1!","NextAb1!")).Succeeded&&user.SecurityStamp!=oldStamp,"Identity password change rotates security stamp");
        var token=await users.GeneratePasswordResetTokenAsync(user);oldStamp=user.SecurityStamp;
        Check(!(await users.ResetPasswordAsync(user,token,"weak")).Succeeded,"Identity reset enforces BA complexity");
        Check((await users.ResetPasswordAsync(user,token,PortalIdentityPolicy.NewTemporaryPassword())).Succeeded&&user.SecurityStamp!=oldStamp,"Identity reset token API rotates security stamp");
        var legacy=new PortalUser{Id="legacy-unit",UserName="legacy-unit",Email="legacy-unit@example.invalid",MustChangePassword=false};legacy.PasswordHash=users.PasswordHasher.HashPassword(legacy,"1234567");store.Items[legacy.Id]=legacy;
        var hash=legacy.PasswordHash;
        Check(await users.CheckPasswordAsync(legacy,"1234567")&&legacy.PasswordHash==hash&&!legacy.MustChangePassword,"Existing weak-format password still signs in without re-flagging or resetting");
        foreach(var path in new[]{"/admin","/admin/users","/admin/users/fixture/reset-password","/admin/create","/admin/account/change-password/extra"})
        {
            var context=Context(path);Check(FirstLoginPasswordGate.Restricts(context),"Server gate covers "+path);
        }
        foreach(var path in new[]{"/admin/account/change-password","/admin/account/logout","/tin-tuc"})Check(!FirstLoginPasswordGate.Restricts(Context(path)),"Exact allowed/public path "+path);
        var outside=Context("/protected-download");outside.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(new AuthorizeAttribute()),"unit"));
        Check(FirstLoginPasswordGate.Restricts(outside),"Protected endpoint outside admin also gated");
        var redirect=Context("/admin/users");await FirstLoginPasswordGate.RejectAsync(redirect);Check(redirect.Response.StatusCode==302&&redirect.Response.Headers.Location=="/admin/account/change-password","Browser request is redirected to required change");
        var ajax=Context("/admin/create");ajax.Request.Headers["X-Editor-Request"]="true";await FirstLoginPasswordGate.RejectAsync(ajax);ajax.Response.Body.Position=0;
        Check(ajax.Response.StatusCode==403&&(await new StreamReader(ajax.Response.Body).ReadToEndAsync()).Contains("password_change_required"),"Direct editor request fails closed with 403");
        var privateRoot=TestEvidencePaths.At(root,"ba-password-private");var file=new TemporaryPasswordDelivery(privateRoot).Write("unit-id","unit-only",passwords[0]);
        try{
            if(OperatingSystem.IsWindows()){
                var acl=new FileInfo(file).GetAccessControl();var rules=acl.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();var sid=WindowsIdentity.GetCurrent().User!;
                var allowed=true;foreach(var rule in rules)if(rule.AccessControlType!=AccessControlType.Allow||!rule.IdentityReference.Equals(sid)&&rule.IdentityReference.Value!="S-1-5-18")allowed=false;
                Check(acl.AreAccessRulesProtected&&rules.Length==2&&allowed,"Credential file ACL permits only process identity and SYSTEM");
            }
            var receipt=new TemporaryPasswordReceipt("unit-id",file);Check(!JsonSerializer.Serialize(receipt).Contains(passwords[0])&&!receipt.ToString().Contains(passwords[0]),"HTTP/audit receipt contains no plaintext password");
        }finally{File.Delete(file);}
        var denied=false;try{new TemporaryPasswordDelivery(Path.Combine(root,"src","Web","wwwroot"),Path.Combine(root,"src","Web","wwwroot")).Write("unit","unit",passwords[0]);}catch(InvalidOperationException){denied=true;}
        Check(denied,"Credential file cannot be created in webroot");
        var result=TestEvidencePaths.At(root,"ba-password-offline.json");Directory.CreateDirectory(Path.GetDirectoryName(result)!);
        if(File.Exists(result))throw new InvalidOperationException("Unique evidence run required");
        await File.WriteAllTextAsync(result,JsonSerializer.Serialize(new{status="PASS_OFFLINE",checks,passed=checks.Count,sql="NOT RUN",fullRegression="NOT RUN",store="ISOLATED IN-MEMORY; not SQL E2E"},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(checks.Count+" focused BA password/offline checks PASS; SQL/HTTP integration NOT RUN.");
    }
    private static DefaultHttpContext Context(string path){var c=new DefaultHttpContext();c.Request.Path=path;c.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"unit")],"unit"));c.Response.Body=new MemoryStream();return c;}
    private sealed class MemoryUsers : IUserPasswordStore<PortalUser>,IUserSecurityStampStore<PortalUser>,IUserEmailStore<PortalUser>
    {
        public readonly Dictionary<string,PortalUser> Items=[];
        public void Dispose(){}
        public Task<string> GetUserIdAsync(PortalUser u,CancellationToken c)=>Task.FromResult(u.Id);
        public Task<string?> GetUserNameAsync(PortalUser u,CancellationToken c)=>Task.FromResult(u.UserName);
        public Task SetUserNameAsync(PortalUser u,string? n,CancellationToken c){u.UserName=n;return Task.CompletedTask;}
        public Task<string?> GetNormalizedUserNameAsync(PortalUser u,CancellationToken c)=>Task.FromResult(u.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(PortalUser u,string? n,CancellationToken c){u.NormalizedUserName=n;return Task.CompletedTask;}
        public Task<IdentityResult> CreateAsync(PortalUser u,CancellationToken c){Items[u.Id]=u;return Task.FromResult(IdentityResult.Success);}
        public Task<IdentityResult> UpdateAsync(PortalUser u,CancellationToken c)=>CreateAsync(u,c);
        public Task<IdentityResult> DeleteAsync(PortalUser u,CancellationToken c){Items.Remove(u.Id);return Task.FromResult(IdentityResult.Success);}
        public Task<PortalUser?> FindByIdAsync(string id,CancellationToken c)=>Task.FromResult(Items.GetValueOrDefault(id));
        public Task<PortalUser?> FindByNameAsync(string n,CancellationToken c)=>Task.FromResult(Items.Values.FirstOrDefault(u=>u.NormalizedUserName==n));
        public Task SetPasswordHashAsync(PortalUser u,string? h,CancellationToken c){u.PasswordHash=h;return Task.CompletedTask;}
        public Task<string?> GetPasswordHashAsync(PortalUser u,CancellationToken c)=>Task.FromResult(u.PasswordHash);
        public Task<bool> HasPasswordAsync(PortalUser u,CancellationToken c)=>Task.FromResult(u.PasswordHash!=null);
        public Task SetSecurityStampAsync(PortalUser u,string s,CancellationToken c){u.SecurityStamp=s;return Task.CompletedTask;}
        public Task<string?> GetSecurityStampAsync(PortalUser u,CancellationToken c)=>Task.FromResult(u.SecurityStamp);
        public Task SetEmailAsync(PortalUser u,string? e,CancellationToken c){u.Email=e;return Task.CompletedTask;}
        public Task<string?> GetEmailAsync(PortalUser u,CancellationToken c)=>Task.FromResult(u.Email);
        public Task<bool> GetEmailConfirmedAsync(PortalUser u,CancellationToken c)=>Task.FromResult(u.EmailConfirmed);
        public Task SetEmailConfirmedAsync(PortalUser u,bool v,CancellationToken c){u.EmailConfirmed=v;return Task.CompletedTask;}
        public Task<PortalUser?> FindByEmailAsync(string e,CancellationToken c)=>Task.FromResult(Items.Values.FirstOrDefault(u=>u.NormalizedEmail==e));
        public Task<string?> GetNormalizedEmailAsync(PortalUser u,CancellationToken c)=>Task.FromResult(u.NormalizedEmail);
        public Task SetNormalizedEmailAsync(PortalUser u,string? e,CancellationToken c){u.NormalizedEmail=e;return Task.CompletedTask;}
    }
}
