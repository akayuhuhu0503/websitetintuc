using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;
public static class V04HttpTests
{
    public static async Task RunAsync(string root,IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        if(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(DatabaseSettings.Connection(config)).InitialCatalog!="LandPortal_Test")throw new InvalidOperationException("V04 HTTP requires SQL Test.");
        void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var checks=new List<object>();var directory=TestEvidencePaths.At(root,"v04","http");Directory.CreateDirectory(directory);
        var name="qa-v04-http-"+Guid.NewGuid().ToString("N");var password=Guid.NewGuid().ToString("N")+"Aa!7";
        var user=new PortalUser{UserName=name,NormalizedUserName=name.ToUpperInvariant(),Email=name+"@example.invalid",NormalizedEmail=(name+"@example.invalid").ToUpperInvariant(),EmailConfirmed=true,SecurityStamp=Guid.NewGuid().ToString(),ConcurrencyStamp=Guid.NewGuid().ToString(),LockoutEnabled=true};user.PasswordHash=new PasswordHasher<PortalUser>().HashPassword(user,password);
        var actor=new ContentActor(user.Id,Permissions.All.ToHashSet());Guid id;Guid foreign;byte[] version;
        await using(var db=new PortalDbContext(options))
        {
            db.Users.Add(user);foreach(var permission in Permissions.All)db.UserClaims.Add(new(){UserId=user.Id,ClaimType="permission",ClaimValue=permission});await db.SaveChangesAsync();
            var f=new WorkflowService(db,new HtmlPolicy(),MediaUploadOptions.FromConfiguration(config,"Development"),StorageOptions.FromConfiguration(config));
            var draft=await f.SaveAsync(null,new(){Title="QA V04 HTTP public "+name,BodyHtml="<p>Public revision</p>",PrimaryCategoryId=75},actor,null,Guid.NewGuid());id=draft.ContentId;
            var live=await f.PublishDirectAsync(id,draft.CurrentRevisionId!.Value,actor,draft.RowVersion,Guid.NewGuid());var edit=await f.BeginEditAsync(id,actor,live.RowVersion,Guid.NewGuid());var changed=await f.SaveAsync(id,new(){Title="V04 PRIVATE "+name,BodyHtml="<p>Private draft marker</p>",PrimaryCategoryId=75},actor,edit.RowVersion,Guid.NewGuid());version=changed.RowVersion;
            foreign=(await f.SaveAsync(null,new(){Title="V04 FOREIGN "+name,BodyHtml="<p>Foreign scope</p>",PrimaryCategoryId=75,SecondaryCategoryIds=[82]},actor,null,Guid.NewGuid())).ContentId;
            db.UserClaims.Add(new(){UserId=user.Id,ClaimType="content.category.scope",ClaimValue="restricted"});db.UserClaims.Add(new(){UserId=user.Id,ClaimType="content.category",ClaimValue="75"});await db.SaveChangesAsync();
        }
        const string baseUrl="http://127.0.0.1:5195";
        var info=new ProcessStartInfo(Path.Combine(root,".local/dotnet/dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add(TestRuntimePaths.Assembly(root,"Web"));
        info.Environment["ASPNETCORE_URLS"]=baseUrl;info.Environment["Captcha__TestScope"]=Guid.NewGuid().ToString("N");info.Environment["Captcha__AttemptsPer10Minutes"]="20";info.Environment["Public__BaseUrl"]=baseUrl;info.Environment["ASPNETCORE_ENVIRONMENT"]="Development";info.Environment["ConnectionStrings__Portal"]=DatabaseSettings.Connection(config);info.Environment["Sql__ExpectedDatabase"]="LandPortal_Test";
        foreach(var key in new[]{"MediaRoot","SnapshotRoot","TempRoot","KeysRoot"}){var value=config["Storage:"+key]!;if(!Path.GetFullPath(value,root).StartsWith(Path.Combine(root,".local","tests"),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Private Test storage required.");info.Environment["Storage__"+key]=value;}
        using var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        var cookies=new CookieContainer();using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=cookies}){BaseAddress=new(baseUrl),Timeout=TimeSpan.FromSeconds(30)};client.DefaultRequestHeaders.Add("X-Editor-Request","true");
        var storage=StorageOptions.FromConfiguration(config);var protection=DataProtectionProvider.Create(new DirectoryInfo(storage.KeysRoot),b=>b.SetApplicationName("LandPortal"));var tickets=new TicketDataFormat(protection.CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware",IdentityConstants.ApplicationScheme,"v2"));
        void Cookie(bool expired=false)
        {
            var properties=new AuthenticationProperties();AdminSessionPolicy.Touch(properties,DateTimeOffset.UtcNow.AddMinutes(expired?-31:0),TimeSpan.FromMinutes(30));if(expired)properties.ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(10);
            var identity=new ClaimsIdentity([new(ClaimTypes.NameIdentifier,user.Id),new(ClaimTypes.Name,name),new("AspNet.Identity.SecurityStamp",user.SecurityStamp!)],IdentityConstants.ApplicationScheme);
            cookies.Add(new Uri(baseUrl),new Cookie("LandPortal.Admin",tickets.Protect(new AuthenticationTicket(new ClaimsPrincipal(identity),properties,IdentityConstants.ApplicationScheme)),"/"));
        }
        async Task<Dictionary<string,string>> Reauth(string expected)
        {
            using var challenge=JsonDocument.Parse(await client.GetStringAsync("/admin/reauthenticate/challenge"));var c=challenge.RootElement;
            return new(){{"userName",name},{"password",password},{"expectedUserId",expected},{"__RequestVerificationToken",c.GetProperty("csrf").GetString()!},{"captchaId",c.GetProperty("captchaId").GetString()!},{"captchaAnswer",CaptchaHttp.AnswerText(c.GetProperty("question").GetString()!)}};
        }
        try
        {
            var ready=false;for(var n=0;n<100;n++){try{using var response=await client.GetAsync("/ready");if(response.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(100);}Assert(ready,"V04 owned Test Web not ready.");
            Cookie();using(var session=await client.GetAsync("/admin/session")){Assert(session.StatusCode==HttpStatusCode.OK,"Actual signed Identity session rejected.");using var data=JsonDocument.Parse(await session.Content.ReadAsStringAsync());var expiry=data.RootElement.GetProperty("expiresUtc").GetString();using var second=await client.GetAsync("/admin/session");using var secondData=JsonDocument.Parse(await second.Content.ReadAsStringAsync());Assert(secondData.RootElement.GetProperty("expiresUtc").GetString()==expiry&&!second.Headers.Contains("Set-Cookie"),"GET session polling renewed authentication.");}checks.Add(new{name="Actual Identity cookie GET polling preserves expiry",status="PASS"});
            foreach(var path in new[]{"/admin/edit/"+foreign,"/admin/preview/"+foreign,"/admin/history/"+foreign,"/admin/media?contentId="+foreign}){using var denied=await client.GetAsync(path);Assert(denied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,"Direct endpoint escaped secondary scope "+path);}var listing=await client.GetStringAsync("/admin?q="+name);Assert(!listing.Contains("V04 FOREIGN"),"Admin SQL listing revealed foreign scope.");checks.Add(new{name="Endpoint editor/preview/history/media and SQL list enforce all category scopes",status="PASS"});
            Cookie(true);using(var expired=await client.GetAsync("/admin/session")){Assert(expired.StatusCode==HttpStatusCode.Unauthorized&&expired.Content.Headers.ContentType?.MediaType=="application/json","Idle-expired ticket was renewed or redirected to login HTML.");}checks.Add(new{name="Idle31min ticket with future cookie expiry still returns401 JSON",status="PASS"});
            using(var wrong=await client.PostAsync("/admin/reauthenticate",new FormUrlEncodedContent(await Reauth(Guid.NewGuid().ToString())))){Assert(wrong.StatusCode==HttpStatusCode.Unauthorized,"Reauth switched editing principal.");}
            using(var response=await client.PostAsync("/admin/reauthenticate",new FormUrlEncodedContent(await Reauth(user.Id)))){Assert(response.IsSuccessStatusCode,"Actual same-user reauth failed.");}using(var fresh=await client.GetAsync("/admin/session")){Assert(fresh.IsSuccessStatusCode,"Reauth session invalid.");using var json=JsonDocument.Parse(await fresh.Content.ReadAsStringAsync());Assert(json.RootElement.GetProperty("userId").GetString()==user.Id&&json.RootElement.GetProperty("csrf").GetString()!.Length>20,"Reauth did not issue current-user CSRF.");}checks.Add(new{name="Real CAPTCHA/password reauth rejects principal switch and returns fresh session/CSRF",status="PASS"});
            using var guest=new HttpClient{BaseAddress=new(baseUrl)};var page=await guest.GetStringAsync("/bai-viet/"+id);Assert(page.Contains("QA V04 HTTP public")&&!page.Contains("V04 PRIVATE")&&!page.Contains("Private draft marker"),"Draft leaked into body/SEO.");var preview=await client.GetStringAsync("/admin/preview/"+id);Assert(preview.Contains("V04 PRIVATE")&&!preview.Contains("property=\"og:title\""),"Private preview leaked OG or did not render current draft.");checks.Add(new{name="Public body/SEO/canonical from published revision; private preview omits OpenGraph",status="PASS"});
            await using var db=new PortalDbContext(options);var f=new WorkflowService(db,new HtmlPolicy(),MediaUploadOptions.FromConfiguration(config,"Development"),storage);var current=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==id);await f.UnpublishAsync(id,actor,current.RowVersion,Guid.NewGuid());using var removed=await guest.GetAsync("/bai-viet/"+id);Assert(removed.StatusCode==HttpStatusCode.NotFound,"Withdrawal left public URL/index cached.");checks.Add(new{name="Unpublish immediately removes public route across new SQL context",status="PASS"});
        }
        catch(Exception error){checks.Add(new{name="Stopped V04 HTTP",status="FAIL",error=error.GetType().Name,detail=error.Message});throw;}
        finally{if(!process.HasExited)process.Kill(entireProcessTree:true);await process.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(directory,"web.log"),await stdout+"\n"+await stderr);await File.WriteAllTextAsync(Path.Combine(directory,"http-report.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,checks},new JsonSerializerOptions{WriteIndented=true}));}
        Console.WriteLine("V04 HTTP PASS "+checks.Count+" actual endpoint/session/privacy checks; SQL Test only.");
    }
}
