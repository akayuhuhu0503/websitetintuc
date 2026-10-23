using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Tests;

public static class Srs12Tests
{
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static async Task Http(string root,IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        var folder=TestEvidencePaths.At(root);Directory.CreateDirectory(folder);var checks=new List<string>();
        Check(DevDemoPolicy.Allowed([new("dev.demo",DevDemoAccounts.Marker),new("dev.demo.expires",DateTimeOffset.UtcNow.AddDays(1).ToString("O"))],"Development","LandPortal_Test"),"Local demo guard rejected");
        foreach(var environment in new[]{"Staging","Production"})Check(!DevDemoPolicy.Allowed([new("dev.demo",DevDemoAccounts.Marker)],environment,"LandPortal_Test"),"Demo escaped environment");
        foreach(var bad in new[]{"javascript:alert(1)","data:text/html,x","//evil.invalid","/\\evil.invalid","/admin","/%61dmin/users","/%252f%252fevil.invalid","/x/../admin","/x%0a"})Check(!SiteConfigurationService.InternalUrlValid(bad),"Internal URL accepted: "+bad);
        foreach(var bad in new[]{"javascript:alert(1)","data:text/html,x","https://u:p@evil.invalid/","https://site.invalid/admin"})Check(!SiteConfigurationService.ExternalUrlValid(bad),"External URL accepted");
        Check(SiteConfigurationService.InternalUrlValid("/hoi-dap?q=dat%20dai")&&SitePresentation.EnglishName(null)=="Department of Land Administration","Official English/internal URL wrong");
        checks.Add("Environment/expiry and internal/external URL fail-closed boundaries");
        const string origin="http://127.0.0.1:5297";
        var info=new ProcessStartInfo(Path.Combine(root,".local/dotnet/dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add(TestRuntimePaths.Assembly(root,"Web"));info.ArgumentList.Add("--contentRoot");info.ArgumentList.Add(Path.Combine(root,"src/Web"));
        foreach(var p in new[]{("Public__BaseUrl",origin),("ASPNETCORE_URLS",origin),("DOTNET_ENVIRONMENT","Development"),("ASPNETCORE_ENVIRONMENT","Development"),("Mail__Transport","Disabled"),("Media__AllowUnscannedDevelopmentUploads","false"),("Captcha__TestScope",Guid.NewGuid().ToString("N"))})info.Environment[p.Item1]=p.Item2;
        using var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        HttpClient Client()=>new(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new()}){BaseAddress=new(origin),Timeout=TimeSpan.FromSeconds(40)};
        using var guest=Client();using var writer=Client();using var reviewer=Client();using var admin=Client();
        var oldLive=Guid.Empty;ContentActor? actor=null;
        SiteConfigurationService Site(PortalDbContext db)=>new(db,StorageOptions.FromConfiguration(config),MediaUploadOptions.FromConfiguration(config,"Development"));
        async Task<Dictionary<string,string>> Form(HttpClient client,string path)
        {
            using var r=await client.GetAsync(path);Check(r.IsSuccessStatusCode,"Form failed "+path+" "+r.StatusCode);
            var dom=new HtmlParser().ParseDocument(await r.Content.ReadAsStringAsync());var fields=new Dictionary<string,string>();
            foreach(var input in dom.QuerySelectorAll("input[name]"))if(input.GetAttribute("type") is not ("checkbox" or "radio"))fields[input.GetAttribute("name")!]=input.GetAttribute("value")??"";
            CaptchaHttp.Add(dom,fields);
            if(path=="/tra-cuu-ho-so")fields["CaptchaAnswer"]=CaptchaHttp.AnswerText(dom.QuerySelector("label[for=CaptchaAnswer]")!.TextContent);
            return fields;
        }
        async Task<string> Post(HttpClient client,string path,Dictionary<string,string> fields,HttpStatusCode expected=HttpStatusCode.OK)
        {using var r=await client.PostAsync(path,new FormUrlEncodedContent(fields));var body=await r.Content.ReadAsStringAsync();Check(r.StatusCode==expected,"POST failed "+path+" "+r.StatusCode);return body;}
        try
        {
            var ready=false;for(var i=0;i<120;i++){try{using var response=await guest.GetAsync("/health");if(response.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(100);}Check(ready,"Test host unavailable");
            var credentialPath=Environment.GetEnvironmentVariable("Srs12__TestCredentials")??throw new InvalidOperationException("Private Test credential file required");
            if(!OperatingSystem.IsWindows())throw new InvalidOperationException("DPAPI Windows Test required");
            var plain=ProtectedData.Unprotect(await File.ReadAllBytesAsync(credentialPath),Encoding.UTF8.GetBytes(DevDemoAccounts.Marker),DataProtectionScope.CurrentUser);
            using var secrets=JsonDocument.Parse(plain);Check(secrets.RootElement.GetProperty("database").GetString()=="LandPortal_Test","Credential target differs");
            var rotated=new List<object>();
            foreach(var item in secrets.RootElement.GetProperty("accounts").EnumerateArray())
            {
                var name=item.GetProperty("userName").GetString()!;var password=item.GetProperty("password").GetString()!;var client=name.EndsWith("writer")?writer:name.EndsWith("reviewer")?reviewer:admin;
                var form=await Form(client,"/admin/login");form["UserName"]=name;form["Password"]=password;
                await Post(client,"/admin/login",form,HttpStatusCode.Redirect);
                await using(var read=new PortalDbContext(options)){var user=await read.Users.AsNoTracking().SingleAsync(x=>x.UserName==name);if(user.MustChangePassword){var change=await Form(client,"/admin/account/change-password");var next="Z!a9"+Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));change["CurrentPassword"]=password;change["NewPassword"]=next;change["ConfirmPassword"]=next;await Post(client,"/admin/account/change-password",change,HttpStatusCode.Redirect);password=next;}}
                rotated.Add(new{userName=name,password});
            }
            var encrypted=ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(new{database="LandPortal_Test",accounts=rotated}),Encoding.UTF8.GetBytes(DevDemoAccounts.Marker),DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(credentialPath+".active",encrypted);CryptographicOperations.ZeroMemory(plain);
            await using(var db=new PortalDbContext(options))
            {
                var current=new CurrentPermissionService(db);var accounts=await db.Users.Where(x=>x.UserName!.StartsWith("dev-srs12-")).ToListAsync();Check(accounts.Count==3,"Three demo accounts absent");
                var wa=await current.GetAsync(accounts.Single(x=>x.UserName!.EndsWith("writer")).Id);var ra=await current.GetAsync(accounts.Single(x=>x.UserName!.EndsWith("reviewer")).Id);actor=await current.GetAsync(accounts.Single(x=>x.UserName!.EndsWith("admin")).Id);
                Check(wa.Has(Permissions.Submit)&&!wa.Has(Permissions.PublishDirect)&&!wa.Has(Permissions.PublishApproved)&&wa.CategoryIds!.SetEquals(new[]{75}),"Writer privilege/scope escaped");
                Check(ra.Has(Permissions.Review)&&ra.Has(Permissions.PublishApproved)&&ra.Has(Permissions.Unpublish)&&!ra.Has(Permissions.ManageUsers)&&!ra.Has(Permissions.ManageSite)&&ra.CategoryIds!.SetEquals(new[]{75}),"Reviewer privileges escaped");
                Check(actor.Has(Permissions.ManageSite)&&actor.Has(Permissions.PublishSite)&&actor.Has(Permissions.ManageUsers)&&actor.CategoryIds==null,"Admin permissions absent");
            }
            foreach(var path in new[]{"/admin/site","/admin/users","/admin/roles"}){using var r=await reviewer.GetAsync(path);Check(r.StatusCode==HttpStatusCode.Redirect&&r.Headers.Location!.ToString().Contains("access-denied"),"Reviewer reached admin endpoint");}
            var denial=await Form(writer,"/admin/create");
            await Post(writer,"/admin/save?format=json&intent=Publish",denial,HttpStatusCode.Forbidden);
            Guid cover;await using(var db=new PortalDbContext(options))cover=await db.MediaAssets.Where(x=>x.ScanStatus=="Clean"&&!x.IsQuarantined&&x.MimeType.StartsWith("image/")&&db.ContentAssets.Any(a=>a.MediaAssetId==x.Id&&db.Contents.Any(c=>c.PublishedRevisionId==a.RevisionId&&c.IsPublic))).Select(x=>x.Id).FirstAsync();
            denial["Title"]="SRS12 technical workflow "+Guid.NewGuid();denial["CategoryId"]="75";denial["Summary"]="Technical Test summary";denial["Author"]="Anonymous QA";denial["BodyHtml"]="<p>Technical Test workflow, not source migration.</p>";denial["CoverAssetId"]=cover.ToString();
            using var saved=JsonDocument.Parse(await Post(writer,"/admin/save?format=json",denial));var contentId=saved.RootElement.GetProperty("contentId").GetGuid();
            var action=await Form(writer,"/admin/edit/"+contentId);action["RequestId"]=Guid.NewGuid().ToString();action["Action"]="Submit";action["RevisionId"]=saved.RootElement.GetProperty("currentRevisionId").GetString()??"";
            using var submitted=JsonDocument.Parse(await Post(writer,"/admin/workflow/"+contentId,action));action["RowVersion"]=submitted.RootElement.GetProperty("rowVersion").GetString()!;
            action["RequestId"]=Guid.NewGuid().ToString();action["Action"]="PublishDirect";await Post(writer,"/admin/workflow/"+contentId,action,HttpStatusCode.Forbidden);
            var reviewerToken=await Form(reviewer,"/admin");action["__RequestVerificationToken"]=reviewerToken["__RequestVerificationToken"];action["RequestId"]=Guid.NewGuid().ToString();action["Action"]="Approve";
            using var approved=JsonDocument.Parse(await Post(reviewer,"/admin/workflow/"+contentId,action));action["RowVersion"]=approved.RootElement.GetProperty("rowVersion").GetString()!;action["RequestId"]=Guid.NewGuid().ToString();action["Action"]="PublishApproved";
            using var published=JsonDocument.Parse(await Post(reviewer,"/admin/workflow/"+contentId,action));action["RowVersion"]=published.RootElement.GetProperty("rowVersion").GetString()!;action["RequestId"]=Guid.NewGuid().ToString();action["Action"]="Unpublish";await Post(reviewer,"/admin/workflow/"+contentId,action);
            checks.Add("Three real Identity roles login/password change; assigned category and direct endpoint privilege denials");
            await using(var db=new PortalDbContext(options)){var site=Site(db);oldLive=(await site.ReadAsync(true))!.Revision.Id;var state=(await site.ReadAsync(false))!;
                state=await site.MutateAsync(actor!,state.Header.RowVersion,r=>{foreach(var c in db.Categories.AsNoTracking().ToList().Where(c=>r.Categories.All(x=>x.CategoryId!=c.Id)))r.Categories.Add(new(){CategoryId=c.Id,DisplayName=c.Name,Slug=c.Slug,ParentCategoryId=c.ParentId,SortOrder=c.SortOrder});},"Technical Test retain full reference categories");}
            var configForm=await Form(admin,"/admin/site/edit?section=menus");configForm["Section"]="menus";configForm["Menu.Label"]="SRS12 technical menu";configForm["Menu.Position"]="main";configForm["Menu.TargetKind"]="Internal";configForm["Menu.ExternalUrl"]="/hoi-dap";configForm["Menu.Enabled"]="true";configForm["Menu.SortOrder"]="99";
            using(var denied=await reviewer.PostAsync("/admin/site/save?format=json",new FormUrlEncodedContent(configForm)))Check(denied.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden,"Reviewer config POST permitted");
            using(var missingCsrf=await admin.PostAsync("/admin/site/save?format=json",new FormUrlEncodedContent(configForm.Where(x=>x.Key!="__RequestVerificationToken"))))Check(missingCsrf.StatusCode==HttpStatusCode.BadRequest,"Config CSRF bypass");
            await Post(admin,"/admin/site/save?format=json",configForm);
            await using(var db=new PortalDbContext(options)){var site=Site(db);var state=(await site.ReadAsync(false))!;Check((await site.ReadAsync(true))!.Revision.Id==oldLive,"Draft changed live");
                var item=state.Revision.Menus.Single(x=>x.Label=="SRS12 technical menu");var form=await Form(admin,"/admin/site/edit?section=menus&id="+item.Id);form["Section"]="menus";form["Menu.Label"]="SRS12 technical edited";form["Menu.TargetKind"]="Internal";form["Menu.ExternalUrl"]="/van-ban";form["Menu.Enabled"]="false";form["Menu.SortOrder"]="99";await Post(admin,"/admin/site/save?format=json",form);}
            await using(var db=new PortalDbContext(options))
            {var site=Site(db);var state=(await site.ReadAsync(false))!;Check(!state.Revision.Menus.Single(x=>x.Label=="SRS12 technical edited").Enabled,"Disabled menu not saved");
                state=await site.MutateAsync(actor!,state.Header.RowVersion,r=>{r.EnglishName=SitePresentation.OfficialEnglishName;SiteConfigurationService.AddDisclosureMenu(r);r.Menus.Single(x=>x.Label=="SRS12 technical edited").Enabled=true;},"Technical Test exact names/menu");
                var publish=await Form(admin,"/admin/site?section=menus");publish["rowVersion"]=Convert.ToBase64String(state.Header.RowVersion);publish["revisionId"]=state.Revision.Id.ToString();await Post(admin,"/admin/site/publish",publish,HttpStatusCode.Redirect);}
            var home=await guest.GetStringAsync("/");Check(home.Contains("Department of Land Administration")&&home.Contains("SRS12 technical edited"),"Published menu/English absent");
            foreach(var pair in V05ContentPolicy.Groups){using var r=await guest.GetAsync("/cong-khai-thong-tin/"+pair.Key);Check(r.IsSuccessStatusCode,"Disclosure page unavailable");}
            await using(var db=new PortalDbContext(options)){var site=Site(db);var state=(await site.ReadAsync(false))!;var item=state.Revision.Menus.Single(x=>x.Label=="SRS12 technical edited");var remove=await Form(admin,"/admin/site?section=menus");remove["section"]="menus";remove["id"]=item.Id.ToString();remove["rowVersion"]=Convert.ToBase64String(state.Header.RowVersion);await Post(admin,"/admin/site/remove",remove,HttpStatusCode.Redirect);}
            checks.Add("Real admin menu create/edit/disable/preview/publish/delete; draft isolation, three disclosure routes, English and CSRF");
            var formInquiry=await Form(guest,"/tuong-tac/Inquiry");var marker="SRS12_PRIVATE_"+Guid.NewGuid().ToString("N");var email="srs12-private@example.invalid";
            formInquiry["Input.Name"]="Private technical person";formInquiry["Input.Email"]=email;formInquiry["Input.Phone"]="0901234567";formInquiry["Input.Field"]="Đất đai";formInquiry["Input.Title"]=marker;formInquiry["Input.Text"]="Private technical submission";formInquiry["Input.PublicConsent"]="false";
            var received=await Post(guest,"/tuong-tac/gui",formInquiry);var receipt=new HtmlParser().ParseDocument(received).QuerySelector("#receipt-code")?.TextContent.Trim();Check(receipt?.Length==32,"Receipt missing");
            const string expectedAnswer="SRS12 technical answer <script>text only</script>";
            await using(var db=new PortalDbContext(options))
            {
                var row=await db.InteractionSubmissions.SingleAsync(x=>x.ReceiptHash==TextNormalizer.Hash(receipt!));
                var protection=DataProtectionProvider.Create(new DirectoryInfo(StorageOptions.FromConfiguration(config).KeysRoot),b=>b.SetApplicationName("LandPortal"));
                var permissions=new CurrentPermissionService(db);var service=new InteractionService(db,protection,InteractionMailOptions.FromConfiguration(config,"Development"),permissions);
                var responder=await db.Users.SingleAsync(x=>x.UserName=="dev-srs12-reviewer");
                await new InquiryAssignmentService(db,service,permissions).AssignAsync(actor!,row.Id,responder.Id,row.RowVersion,Guid.NewGuid(),"Technical Test assignment");
                await service.ModerateAsync(await permissions.GetAsync(responder.Id),row.Id,row.RowVersion,Guid.NewGuid(),"answer","","",expectedAnswer,"");
            }
            var before=await guest.GetStringAsync("/tra-cuu-ho-so");Check(!before.Contains(marker)&&!before.Contains(email)&&!before.Contains(receipt!),"Pre-verification leakage");
            var success=await Form(guest,"/tra-cuu-ho-so");success["Receipt"]=receipt!;success["Email"]=email;var result=await Post(guest,"/tra-cuu-ho-so",success);Check(result.Contains("Kết quả tra cứu")&&!result.Contains(email)&&!result.Contains("0901234567")&&!result.Contains(marker)&&!result.Contains("<script>text only</script>")&&new HtmlParser().ParseDocument(result).QuerySelector(".lookup-answer")?.TextContent==expectedAnswer,"Lookup leaked original, unescaped answer or failed");
            var wrong=await Form(guest,"/tra-cuu-ho-so");wrong["Receipt"]=receipt!;wrong["Email"]="wrong@example.invalid";var badEmail=await Post(guest,"/tra-cuu-ho-so",wrong);
            wrong=await Form(guest,"/tra-cuu-ho-so");wrong["Receipt"]=Guid.NewGuid().ToString("N");wrong["Email"]=email;var badCode=await Post(guest,"/tra-cuu-ho-so",wrong);
            var parser=new HtmlParser();Check(parser.ParseDocument(badEmail).QuerySelector(".form-message")!.TextContent==parser.ParseDocument(badCode).QuerySelector(".form-message")!.TextContent,"Existence oracle in wrong code/email response");
            using(var replay=await guest.PostAsync("/tra-cuu-ho-so",new FormUrlEncodedContent(success)))Check(replay.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.TooManyRequests,"CAPTCHA replay permitted");
            using(var r=await guest.GetAsync("/tra-cuu-ho-so"))Check(r.Headers.CacheControl!.NoStore,"Lookup publicly cacheable");
            using(var r=await guest.GetAsync("/tra-cuu-ho-so/ket-qua"))Check(r.StatusCode==HttpStatusCode.Gone,"Old token grant accepted");
            checks.Add("Real public submission -> SQL -> code/email/one-use CAPTCHA lookup; generic misses, no PII, old grants denied, no-store");
        }
        finally
        {
            if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(folder,"web.log"),await stdout+"\n"+await stderr);
            if(oldLive!=Guid.Empty&&actor!=null){await using var db=new PortalDbContext(options);var site=Site(db);var state=(await site.ReadAsync(false))!;var restored=await site.RestoreAsync(actor,state.Header.RowVersion,oldLive);await site.PublishAsync(actor,restored.Header.RowVersion,restored.Revision.Id);}
        }
        await LookupGuards(config,options);checks.Add("Expired/wrong-purpose CAPTCHA fail closed; persisted SQL receipt budget across six connections");
        await File.WriteAllTextAsync(Path.Combine(folder,"http-readback.json"),JsonSerializer.Serialize(new{checks,fullRegression="NOT RUN",screenshots=0,devWrites=0}));
    }
    sealed class LookupClock:TimeProvider{public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
    static async Task LookupGuards(IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        var protection=new EphemeralDataProtectionProvider();var optionsCaptcha=new PublicCaptchaOptions{AddressKey=RandomNumberGenerator.GetBytes(32)};var clock=new LookupClock();
        var receipt=Guid.NewGuid().ToString("N");
        await using(var db=new PortalDbContext(options))
        {
            var captcha=new PublicCaptchaService(db,protection,optionsCaptcha,clock);var question=await captcha.IssueAsync("expiry-test","expiry-test",InquiryCaptchaLookup.Purpose);clock.Now=clock.Now.AddMinutes(6);
            await V05FinishTests.Denied<PublicCaptchaException>(()=>captcha.ConsumeAsync(question.Id,CaptchaHttp.AnswerText(question.Question),"expiry-test","expiry-test",InquiryCaptchaLookup.Purpose));
            clock.Now=DateTimeOffset.UtcNow;question=await captcha.IssueAsync("purpose-test","purpose-test","Inquiry");
            await V05FinishTests.Denied<PublicCaptchaException>(()=>captcha.ConsumeAsync(question.Id,CaptchaHttp.AnswerText(question.Question),"purpose-test","purpose-test",InquiryCaptchaLookup.Purpose));
        }
        for(var i=0;i<6;i++)
        {
            await using var db=new PortalDbContext(options);var session="lookup-budget-"+Guid.NewGuid();var captcha=new PublicCaptchaService(db,protection,optionsCaptcha,clock);
            var challenge=await captcha.IssueAsync(session,session,InquiryCaptchaLookup.Purpose);
            var service=new InquiryCaptchaLookup(db,captcha,new(db,protection,InteractionMailOptions.FromConfiguration(config,"Development"),new(db)),clock);
            if(i==5)await V05FinishTests.Denied<PublicCaptchaException>(()=>service.ReadAsync(receipt,"absent@example.invalid",challenge.Id,CaptchaHttp.AnswerText(challenge.Question),session,session));
            else Check(await service.ReadAsync(receipt,"absent@example.invalid",challenge.Id,CaptchaHttp.AnswerText(challenge.Question),session,session)==null,"Absent lookup disclosed data");
        }
    }
}
