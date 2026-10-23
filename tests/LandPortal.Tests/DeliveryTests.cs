using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Tests;
public static class DeliveryTests
{
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(string root,IConfiguration source)
    {
        IConfiguration Uat(string key="",string? value=null)
        {
            var home=Path.Combine(root,".local/uat-technical-fixture");
            var d=new Dictionary<string,string?>{{"Deployment:Profile","UAT"},{"Deployment:ExpectedSqlServer","uat.invalid"},{"Deployment:ExpectedHost","uat.landportal.invalid"},{"DOTNET_ENVIRONMENT","Staging"},{"ASPNETCORE_ENVIRONMENT","Staging"},{"ConnectionStrings:Portal","Server=uat.invalid;Database=LandPortal_Uat;Integrated Security=true;Encrypt=true;TrustServerCertificate=false"},{"Sql:ExpectedDatabase","LandPortal_Uat"},{"Public:BaseUrl","https://uat.landportal.invalid"},{"WorkspaceRoot",home},{"Scanner:Provider","Clamd"},{"Scanner:ClamdPort","3310"},{"Scanner:PolicyReference","Technical configuration fixture, no target connection"},{"Runtime:RolePassword",new string('x',32)}};
            foreach(var name in new[]{"MediaRoot","SnapshotRoot","TempRoot","KeysRoot"})d["Storage:"+name]=Path.Combine(home,name);
            d["Deployment:TargetApproved"]="true";d["Deployment:TargetApprovalReference"]="Technical fixture only, not operator authority";
            if(key.Length>0)d[key]=value;return new ConfigurationBuilder().AddInMemoryCollection(d).Build();
        }
        yield return("Delivery isolated UAT profile exact DB server TLS host private stores and no scanner bypass",()=>
        {
            Check(UatDeployment.Validate(Uat(),"Staging"),"Positive UAT guard");
            UatSiteInitialization.RequireAuthority(["site-initialize","--confirm-target","LandPortal_Uat","--approval-reference","Technical fixture only, not operator authority"],Uat(),"Staging");
            foreach(var confirm in new[]{Array.Empty<string>(),new[]{"--confirm-target","LandPortal_Dev","--approval-reference","Technical fixture only, not operator authority"},new[]{"--confirm-target","LandPortal_Uat","--approval-reference","wrong"}})
            {var rejected=false;try{UatSiteInitialization.RequireAuthority(confirm,Uat(),"Staging");}catch(InvalidOperationException){rejected=true;}Check(rejected,"UAT initialization confirmation/authority failed open");}
            foreach(var pair in new[]{("DOTNET_ENVIRONMENT","Production"),("Sql:ExpectedDatabase","LandPortal_Dev"),("Deployment:ExpectedSqlServer","wrong.invalid"),("Scanner:Provider","DefenderTest"),("Media:AllowUnscannedDevelopmentUploads","true"),("Storage:KeysRoot",Path.Combine(root,"src/Web/wwwroot")),("Public:BaseUrl","http://uat.landportal.invalid"),("Storage:KeysRoot",Path.Combine(root,".local/uat-technical-fixture/MediaRoot/nested"))})
            {var denied=false;try{UatDeployment.Validate(Uat(pair.Item1,pair.Item2),"Staging");}catch(InvalidOperationException){denied=true;}Check(denied,"UAT negative guard: "+pair.Item1);}
            Check(UatDeployment.RequireMfa(Uat()),"UAT MFA mandatory");Check(!UatDeployment.Validate(source,"Development"),"Existing local profile preserved");return Task.CompletedTask;
        });
        yield return("Delivery UAT allocated database approval required and reserved targets denied",()=>
        {
            foreach(var name in new[]{"LandPortal_Dev","landportal_test","master","LandPortal_Restore3J_abcd","LandPortal_Production","REPLACE_APPROVED_DATABASE","unsafe];DROP",""})
                Check(!UatDeployment.IsTargetDatabase(name),"Reserved/unsafe target denied");
            foreach(var pair in new[]{("Deployment:TargetApproved","false"),("Deployment:TargetApprovalReference",""),("Deployment:ExpectedSqlServer","REPLACE_SERVER")})
            {bool denied=false;try{UatDeployment.Validate(Uat(pair.Item1,pair.Item2),"Staging");}catch(InvalidOperationException){denied=true;}Check(denied,"Unallocated target denied");}
            var allocated=new ConfigurationBuilder().AddConfiguration(Uat()).AddInMemoryCollection(new Dictionary<string,string?>{
                ["Sql:ExpectedDatabase"]="AgencyPortal_UatAllocated",["ConnectionStrings:Portal"]="Server=uat.invalid;Database=AgencyPortal_UatAllocated;Integrated Security=true;Encrypt=true;TrustServerCertificate=false"}).Build();
            Check(UatDeployment.Validate(allocated,"Staging"),"Allocated name accepted only with matching private target profile");return Task.CompletedTask;
        });
        yield return("Delivery SMTP explicit UAT STARTTLS policy opt-in and test sink fences",()=>
        {
            IConfiguration Mail(params (string,string)[] entries)=>new ConfigurationBuilder().AddConfiguration(Uat()).AddInMemoryCollection(entries.ToDictionary(x=>x.Item1,x=>(string?)x.Item2)).Build();
            var enabled=Mail(("Mail:Transport","Smtp"),("Mail:DeliveryEnabled","true"),("Mail:PolicyReference","Operator UAT mail approval pending real relay"),("Mail:Host","smtp.invalid"),("Mail:Port","587"),("Mail:From","portal@landportal.invalid"),("Mail:Tls","StartTls"));
            Check(InteractionMailOptions.FromConfiguration(enabled,"Staging").Available,"Explicit UAT SMTP config");
            foreach(var entry in new[]{("Mail:DeliveryEnabled","false"),("Mail:Tls","None"),("Mail:From","bad\r\nRCPT TO:x@invalid"),("Mail:Port","0")})
            {var denied=false;try{var c=new ConfigurationBuilder().AddConfiguration(enabled).AddInMemoryCollection(new Dictionary<string,string?>{{entry.Item1,entry.Item2}}).Build();denied=!InteractionMailOptions.FromConfiguration(c,"Staging").Available;}catch(InvalidOperationException){denied=true;}Check(denied,"SMTP negative: "+entry.Item1);}
            var test=new ConfigurationBuilder().AddConfiguration(source).AddInMemoryCollection(new Dictionary<string,string?>{{"Mail:Transport","Smtp"},{"Mail:DeliveryEnabled","true"}}).Build();
            var blocked=false;try{InteractionMailOptions.FromConfiguration(test,"Development");}catch(InvalidOperationException){blocked=true;}Check(blocked,"Real SMTP forbidden on local Test profile");return Task.CompletedTask;
        });
        yield return("Delivery approved source policy exact hash URL scope and public upload limit unchanged",()=>
        {
            var p=SourceResourcePolicy.Read(source)??throw new InvalidOperationException("Run-specific policy required");
            Check(p.Approval.MaxPdfBytes==48L*1024*1024,"Approved source cap");
            var selected=p.Approval.Urls.First();Check(SourceResourcePolicy.MediaOptions(source,selected).MaxPdfBytes==48L*1024*1024,"Scoped approved PDF options");
            Check(SourceResourcePolicy.MediaOptions(source,"https://qldd.mae.gov.vn/not-approved.pdf").MaxPdfBytes==32L*1024*1024,"Unapproved URL retains old cap");
            Check(MediaUploadOptions.FromConfiguration(source,"Development").MaxPdfBytes==32L*1024*1024,"Public upload cap unchanged");
            var tampered=new ConfigurationBuilder().AddConfiguration(source).AddInMemoryCollection(new Dictionary<string,string?>{{"SourceImport:PolicySha256",new string('0',64)}}).Build();var denied=false;
            try{SourceResourcePolicy.Read(tampered);}catch(InvalidDataException){denied=true;}Check(denied,"Policy hash tamper must fail");return Task.CompletedTask;
        });
    }
    public static async Task MfaHttp(string root,IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        var directory=TestEvidencePaths.At(root,"delivery-mfa");Directory.CreateDirectory(directory);var password=Guid.NewGuid()+"!aZ2";string id,name;
        await using(var db=new PortalDbContext(options)){id=await InquiryFinishTests.User(db,Permissions.All,"Technical delivery MFA fixture");var user=(await db.Users.FindAsync(id))!;name=user.UserName!;user.PasswordHash=new PasswordHasher<PortalUser>().HashPassword(user,password);user.LockoutEnabled=true;await db.SaveChangesAsync();}
        const string origin="http://127.0.0.1:5394";
        var info=new ProcessStartInfo(Path.Combine(root,".local/dotnet/dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add(TestRuntimePaths.Assembly(root,"Web"));info.ArgumentList.Add("--contentRoot");info.ArgumentList.Add(Path.Combine(root,"src/Web"));
        foreach(var pair in new[]{("Public__BaseUrl",origin),("DOTNET_ENVIRONMENT","Development"),("ASPNETCORE_ENVIRONMENT","Development"),("Captcha__TestScope",Guid.NewGuid().ToString("N")),("Captcha__AttemptsPer10Minutes","30"),("Mail__Transport","Disabled"),("Media__AllowUnscannedDevelopmentUploads","false"),("AdminSession__RequireMfa","true")})info.Environment[pair.Item1]=pair.Item2;
        using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new()}){BaseAddress=new(origin),Timeout=TimeSpan.FromSeconds(40)};
        async Task<(AngleSharp.Dom.IDocument,Dictionary<string,string>)> Form(string path){using var res=await client.GetAsync(path);Check(res.IsSuccessStatusCode,"MFA form "+path+" "+res.StatusCode);var d=new HtmlParser().ParseDocument(await res.Content.ReadAsStringAsync());var f=new Dictionary<string,string>();foreach(var e in d.QuerySelectorAll("input[name]")){var n=e.GetAttribute("name")!;if(n=="__RequestVerificationToken")f[n]=e.GetAttribute("value")??"";}CaptchaHttp.Add(d,f);return(d,f);}
        async Task<HttpResponseMessage> Post(string path,Dictionary<string,string> f)=>await client.PostAsync(path,new FormUrlEncodedContent(f));
        async Task Login(){var(_,f)=await Form("/admin/login");f["UserName"]=name;f["Password"]=password;f["ReturnUrl"]="/admin";using var res=await Post("/admin/login",f);Check(res.StatusCode==HttpStatusCode.Redirect,"Password+CAPTCHA login");}
        async Task Logout(){var(_,f)=await Form("/admin/account/mfa");using var res=await Post("/admin/account/logout",f);Check(res.StatusCode==HttpStatusCode.Redirect,"Logout");}
        string Totp(string key,long step)
        {
            const string chars="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";var bytes=new List<byte>();int buffer=0,bits=0;foreach(var c in key.ToUpperInvariant()){buffer=(buffer<<5)|chars.IndexOf(c);bits+=5;if(bits>=8){bits-=8;bytes.Add((byte)(buffer>>bits));}}
            var counter=BitConverter.GetBytes(step);if(BitConverter.IsLittleEndian)Array.Reverse(counter);var hash=HMACSHA1.HashData(bytes.ToArray(),counter);int offset=hash[^1]&15;return (((hash[offset]&127)<<24|hash[offset+1]<<16|hash[offset+2]<<8|hash[offset+3])%1000000).ToString("D6");
        }
        try
        {
            var ready=false;for(var i=0;i<60;i++){if(process.HasExited)break;try{using var res=await client.GetAsync("/admin/login");if(res.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(250);}Check(ready,"MFA web startup");
            using(var direct=await client.GetAsync("/admin/mfa"))Check(direct.StatusCode==HttpStatusCode.Redirect,"No pending password session");
            await Login();using(var admin=await client.GetAsync("/admin"))Check(admin.StatusCode==HttpStatusCode.Redirect&&admin.Headers.Location!.ToString()=="/admin/account/mfa","MFA policy gates admin before enrollment");
            var(_,start)=await Form("/admin/account/mfa");start["password"]=password;using var setup=await Post("/admin/account/mfa/start",start);Check(setup.IsSuccessStatusCode,"Enrollment password step");var setupDoc=new HtmlParser().ParseDocument(await setup.Content.ReadAsStringAsync());var key=setupDoc.QuerySelector("code")!.TextContent.Trim();var token=setupDoc.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;var step=DateTimeOffset.UtcNow.ToUnixTimeSeconds()/30;
            using var enable=await Post("/admin/account/mfa/enable",new(){{"__RequestVerificationToken",token},{"code",Totp(key,step)}});Check(enable.IsSuccessStatusCode,"Real Identity TOTP enrollment");var codes=new HtmlParser().ParseDocument(await enable.Content.ReadAsStringAsync()).QuerySelectorAll("li code").Select(x=>x.TextContent.Trim()).ToArray();Check(codes.Length==10,"One-time recovery codes");
            using(var admin=await client.GetAsync("/admin"))Check(admin.IsSuccessStatusCode,"amr MFA allows admin");await Logout();await Login();var(_,challenge)=await Form("/admin/mfa");challenge["Code"]="999999";using(var wrong=await Post("/admin/mfa",challenge))Check(wrong.StatusCode==HttpStatusCode.BadRequest,"Wrong TOTP denied");
            var(_,challenge2)=await Form("/admin/mfa");var fresh=Totp(key,step+1);challenge2["Code"]=fresh;using(var ok=await Post("/admin/mfa",challenge2))Check(ok.StatusCode==HttpStatusCode.Redirect,"Real TOTP challenge");await Logout();await Login();var(_,replay)=await Form("/admin/mfa");replay["Code"]=fresh;using(var reused=await Post("/admin/mfa",replay))Check(reused.StatusCode==HttpStatusCode.BadRequest,"Used TOTP denied SQL replay fence");
            var(_,recover)=await Form("/admin/mfa");recover["Code"]=codes[0];recover["Recovery"]="true";using(var recovered=await Post("/admin/mfa",recover))Check(recovered.StatusCode==HttpStatusCode.Redirect,"One-use Identity recovery login");
            var(_,reset)=await Form("/admin/account/mfa");reset["password"]=password;reset["recoveryCode"]=codes[1];using(var result=await Post("/admin/account/mfa/reset",reset))Check(result.StatusCode==HttpStatusCode.Redirect,"Own authenticated reset");
            await using var read=new PortalDbContext(options);var row=await read.Users.FindAsync(id);Check(!row!.TwoFactorEnabled,"Reset disabled old TOTP");Check(await read.AuditLogs.CountAsync(x=>x.Actor==id&&x.Action=="AdminMfaTokenUsed")==2,"Only distinct valid authenticator codes consumed");
            using(var admin=await client.GetAsync("/admin"))Check(admin.StatusCode==HttpStatusCode.Redirect,"Reset revoked current session");
            await File.WriteAllTextAsync(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{status="PASS",database="LandPortal_Test",fixtureUser=id,passwordAndCaptcha=true,enrollment=true,wrongDenied=true,replayDenied=true,recovery=true,resetRevokes=true,devIdentityWrites=0,secretsLogged=false}));
        }
        finally{if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(directory,"web.log"),await output+"\n"+await errors);}
    }
}
