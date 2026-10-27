using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AngleSharp.Html.Parser;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LandPortal.Tests;

// Real Identity/SQL/HTTP, one temporary SQL Test account; no LAN credentials.
public static class OwnPasswordHttpTests
{
    public static async Task RunAsync(string root,IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        if(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(DatabaseSettings.Connection(config)).InitialCatalog!="LandPortal_Test")throw new InvalidOperationException("SQL Test only.");
        var folder=TestEvidencePaths.At(root,"own-password");Directory.CreateDirectory(folder);
        var services=new ServiceCollection();services.AddLogging();services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(StorageOptions.FromConfiguration(config).KeysRoot));services.AddScoped(_=>new PortalDbContext(options));
        services.AddIdentity<PortalUser,IdentityRole>(PortalIdentityPolicy.Configure).AddEntityFrameworkStores<PortalDbContext>().AddDefaultTokenProviders();
        using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var name="QA-Password-"+Guid.NewGuid().ToString("N");var user=new PortalUser{UserName=name,Email=name+"@example.invalid",EmailConfirmed=true,MustChangePassword=true,LockoutEnabled=true,TwoFactorEnabled=true};
        var checks=new List<string>();void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks.Add(label);}
        Check(users.Options.Password.RequiredLength==7&&!users.Options.Password.RequireUppercase&&!users.Options.Password.RequireLowercase&&!users.Options.Password.RequireDigit&&!users.Options.Password.RequireNonAlphanumeric&&users.Options.Password.RequiredUniqueChars==0,"Shared Identity policy is length-only seven");
        Check(users.Options.Lockout.MaxFailedAccessAttempts==5&&users.Options.Lockout.DefaultLockoutTimeSpan==TimeSpan.FromMinutes(15),"Five failures and fifteen-minute lockout preserved");
        Check(!(await users.CreateAsync(user,"abcdef")).Succeeded,"Identity creation refuses six characters");
        if(!(await users.CreateAsync(user,"abcdefg")).Succeeded)throw new InvalidOperationException("Seven-letter fixture creation failed.");checks.Add("Identity creation accepts seven letters");
        var seed=scope.ServiceProvider.GetRequiredService<PortalDbContext>();seed.UserClaims.Add(new(){UserId=user.Id,ClaimType="permission",ClaimValue=LandPortal.Application.Permissions.ViewOwn});await seed.SaveChangesAsync();
        const string url="http://127.0.0.1:5497";
        var start=new ProcessStartInfo(Path.Combine(root,".local","dotnet","dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(TestRuntimePaths.Assembly(root,"Web"));start.ArgumentList.Add("--contentRoot");start.ArgumentList.Add(Path.Combine(root,"src","Web"));
        start.Environment["Public__BaseUrl"]=url;start.Environment["DOTNET_ENVIRONMENT"]="Development";start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";start.Environment["Mail__Transport"]="Disabled";start.Environment["Media__AllowUnscannedDevelopmentUploads"]="false";start.Environment["Captcha__TestScope"]=Guid.NewGuid().ToString("N");
        using var web=Process.Start(start)!;var stdout=web.StandardOutput.ReadToEndAsync();var stderr=web.StandardError.ReadToEndAsync();
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new()}){BaseAddress=new(url),Timeout=TimeSpan.FromSeconds(30)};
        async Task<AngleSharp.Dom.IDocument> Page(string path){using var response=await client.GetAsync(path);if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Form HTTP "+(int)response.StatusCode);return new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());}
        async Task<HttpResponseMessage> Login(string password){var form=await Page("/admin/login");var fields=CaptchaHttp.Fields(form);fields["UserName"]=name;fields["Password"]=password;fields["ReturnUrl"]="/admin";return await client.PostAsync("/admin/login",new FormUrlEncodedContent(fields));}
        async Task<HttpResponseMessage> Change(string next,string confirm){var page=await Page("/admin/account/change-password");return await client.PostAsync("/admin/account/change-password",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=page.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!,["CurrentPassword"]="abcdefg",["NewPassword"]=next,["ConfirmPassword"]=confirm}));}
        try
        {
            var ready=false;for(var i=0;i<100;i++){if(web.HasExited)break;try{using var response=await client.GetAsync("/health");if(response.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(150);}Check(ready,"Focused Test Web ready");
            using(var response=await Login("wrong-password")){var dom=new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());Check(response.StatusCode!=HttpStatusCode.Redirect&&dom.QuerySelector(".validation-summary")?.TextContent.Contains("không hợp lệ")==true,"Wrong password rejected with real CAPTCHA");}
            using(var response=await client.GetAsync("/admin"))Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location!.OriginalString.Contains("/admin/login"),"Failed login has no authenticated session");
            using(var response=await Login("abcdefg"))Check(response.StatusCode==HttpStatusCode.Redirect,"Password login succeeds despite inert legacy two-factor flag");
            using(var response=await client.GetAsync("/admin"))Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location?.OriginalString=="/admin/account/change-password","First change still required for temporary password");
            var page=await Page("/admin/account/change-password");Check(page.QuerySelector("#password-policy")?.TextContent.Trim()=="Mật khẩu phải có ít nhất 7 ký tự.","Length-only Vietnamese guidance agrees with backend");
            using(var response=await Change("abcdef","abcdef")){var dom=new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());Check(dom.QuerySelector(".validation-summary")!.TextContent.Contains("Mật khẩu phải có tối thiểu 7 ký tự."),"Six-character change rejected with visible message");}
            using(var response=await Change("1234567","1234568")){var dom=new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());Check(dom.QuerySelector(".validation-summary")!.TextContent.Contains("Mật khẩu nhập lại không khớp."),"Confirmation mismatch visible");}
            using(var response=await Change("1234567","1234567"))Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location?.OriginalString=="/admin","Seven-digit change goes directly to admin");
            await using(var db=new PortalDbContext(options)){var changed=await db.Users.AsNoTracking().SingleAsync(x=>x.Id==user.Id);Check(!changed.MustChangePassword&&changed.TwoFactorEnabled&&new PasswordHasher<PortalUser>().VerifyHashedPassword(changed,changed.PasswordHash!,"1234567")!=PasswordVerificationResult.Failed,"SQL confirms password/flag while legacy Identity data remains intact");}
            var dashboard=await Page("/admin");Check(dashboard.QuerySelector("a[href='/admin/account/mfa']")==null,"Dashboard accessible without enrollment UI");
            foreach(var path in new[]{"/admin/mfa","/admin/account/mfa"}){using var response=await client.GetAsync(path);Check(response.StatusCode==HttpStatusCode.NotFound,"Retired endpoint absent: "+path);}
            using(var response=await client.GetAsync("/admin/users"))Check(response.StatusCode==HttpStatusCode.Forbidden||response.Headers.Location?.OriginalString.Contains("access-denied")==true,"Server permissions still deny user administration");
            seed.ChangeTracker.Clear();var fixture=(await users.FindByIdAsync(user.Id))!;var token=await users.GeneratePasswordResetTokenAsync(fixture);Check((await users.ResetPasswordAsync(fixture,token,"abcdefg")).Succeeded,"Identity reset accepts seven letters on isolated fixture");
            using(var response=await Login("abcdefg"))Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location?.OriginalString=="/admin","Reset fixture signs in directly without another forced change");
        }
        finally
        {
            if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(folder,"web.log"),await stdout+"\n"+await stderr);
            seed.ChangeTracker.Clear();var fixture=await users.FindByIdAsync(user.Id);if(fixture?.UserName!=name||!(await users.DeleteAsync(fixture)).Succeeded)throw new InvalidOperationException("Exact SQL Test fixture cleanup failed.");
            await File.WriteAllTextAsync(Path.Combine(folder,"checks.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,checks,fixtureRemoved=true,database="LandPortal_Test",fullRegression="NOT RUN"},new JsonSerializerOptions{WriteIndented=true}));
        }
        Console.WriteLine("Focused authentication/password checks: "+checks.Count+" PASS; SQL Test fixture removed.");
    }
}
