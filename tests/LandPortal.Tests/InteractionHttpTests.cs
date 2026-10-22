using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;
public static class InteractionHttpTests
{
    private sealed class ReplyClock:TimeProvider{public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow.AddMinutes(1);}
    private static void Assert(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    public static async Task RunAsync(string root,IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        var evidence=TestEvidencePaths.At(root,"interactions-http");Directory.CreateDirectory(evidence);await using var sink=new LocalMailSink(Path.Combine(evidence,"mail"));var checks=new List<object>();var started=DateTimeOffset.UtcNow;
        var password=Guid.NewGuid().ToString("N")+"!aZ2";var userId=Guid.NewGuid().ToString();var username="qa2c-"+userId+"@landportal.invalid";var date=DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);var articleId=Guid.Empty;var draftId=Guid.Empty;
        await using(var db=new PortalDbContext(options))
        {
            var user=new PortalUser{Id=userId,UserName=username,NormalizedUserName=username.ToUpperInvariant(),Email=username,NormalizedEmail=username.ToUpperInvariant(),EmailConfirmed=true,SecurityStamp=Guid.NewGuid().ToString(),DisplayName="Kiểm duyệt Test"};user.PasswordHash=new PasswordHasher<PortalUser>().HashPassword(user,password);db.Users.Add(user);
            foreach(var p in Permissions.All)db.UserClaims.Add(new(){UserId=user.Id,ClaimType="permission",ClaimValue=p});
            var setting=await db.SiteSettings.FindAsync("Interactions.CommentsEnabled");if(setting==null)db.SiteSettings.Add(new(){Key="Interactions.CommentsEnabled",Value="true"});else setting.Value="true";await db.SaveChangesAsync();
            var workflow=new WorkflowService(db,new HtmlPolicy(),MediaUploadOptions.FromConfiguration(config,"Development"),StorageOptions.FromConfiguration(config));var actor=Phase2aSqlTests.Actor(userId);
            var article=await workflow.SaveAsync(null,Phase2aSqlTests.Input("Bài kiểm thử bình luận 2C "+Guid.NewGuid().ToString("N")),actor,null,Guid.NewGuid());await workflow.PublishDirectAsync(article.ContentId,article.CurrentRevisionId!.Value,actor,article.RowVersion,Guid.NewGuid());articleId=article.ContentId;var c=await db.Contents.FindAsync(articleId);c!.CommentsEnabled=true;await db.SaveChangesAsync();
            var input=Phase2aSqlTests.Input("Dự thảo kiểm thử 2C "+Guid.NewGuid().ToString("N"));input.Kind=ContentKind.DraftDocument;input.Metadata=new(){ConsultationStartDate=date.AddDays(-1),ConsultationEndDate=date.AddDays(1),ConsultationDateEvidence="Test fixture"};
            var draft=await workflow.SaveAsync(null,input,actor,null,Guid.NewGuid());await workflow.PublishDirectAsync(draft.ContentId,draft.CurrentRevisionId!.Value,actor,draft.RowVersion,Guid.NewGuid());draftId=draft.ContentId;
        }
        const string baseUrl="http://127.0.0.1:5219";
        ProcessStartInfo StartInfo(string project)
        {
            var p=new ProcessStartInfo(Path.Combine(root,".local","dotnet","dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};p.ArgumentList.Add(TestRuntimePaths.Assembly(root,project));
            p.Environment["Captcha__TestScope"]=Guid.NewGuid().ToString("N");p.Environment["Captcha__AttemptsPer10Minutes"]="20";p.Environment["DOTNET_ENVIRONMENT"]="Development";p.Environment["ASPNETCORE_ENVIRONMENT"]="Development";p.Environment["Public__BaseUrl"]=baseUrl;p.Environment["Mail__Transport"]="TestSink";p.Environment["Mail__SinkPort"]=sink.Port.ToString();p.Environment["Media__AllowUnscannedDevelopmentUploads"]="false";
            if(project=="Web"){p.Environment["Media__AllowUnscannedDevelopmentUploads"]="true";p.ArgumentList.Add("--contentRoot");p.ArgumentList.Add(Path.Combine(root,"src","Web"));}return p;
        }
        using var web=Process.Start(StartInfo("Web"))!;var webOutput=web.StandardOutput.ReadToEndAsync();var webError=web.StandardError.ReadToEndAsync();
        HttpClient Client()=>new(new HttpClientHandler{CookieContainer=new(),AllowAutoRedirect=false}){BaseAddress=new(baseUrl),Timeout=TimeSpan.FromSeconds(40)};
        using var guest=Client();using var admin=Client();
        async Task<Dictionary<string,string>> Form(HttpClient client,string url)
        {
            using var response=await client.GetAsync(url);Assert(response.StatusCode==HttpStatusCode.OK,"Form GET failed.");var html=await response.Content.ReadAsStringAsync();var document=await new HtmlParser().ParseDocumentAsync(html);var fields=new Dictionary<string,string>();
            foreach(var e in document.QuerySelectorAll("input[name],textarea[name]")){var name=e.GetAttribute("name")!;if(e.GetAttribute("type")=="checkbox")continue;fields[name]=e.LocalName=="textarea"?e.TextContent:e.GetAttribute("value")??"";}if(document.QuerySelector("#captcha-question")!=null)fields["CaptchaAnswer"]=CaptchaHttp.Answer(document);return fields;
        }
        async Task<Guid> Submission(Guid challenge){await using var db=new PortalDbContext(options);return await db.InteractionSubmissions.Where(x=>x.ChallengeId==challenge).Select(x=>x.Id).SingleAsync();}
        async Task Deliver(Guid id)
        {
            var p=StartInfo("Worker");p.ArgumentList.Add("--mail");p.ArgumentList.Add("--once");p.ArgumentList.Add("--outbox");p.ArgumentList.Add(id.ToString());
            using var worker=Process.Start(p)!;var output=worker.StandardOutput.ReadToEndAsync();var error=worker.StandardError.ReadToEndAsync();await worker.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(evidence,"worker-"+id+".log"),await output+"\n"+await error);Assert(worker.ExitCode==0&&sink.Bodies.ContainsKey(id),"Actual receipt worker/sink failed.");
        }
        var submissions=new Dictionary<InteractionKind,Guid>();
        try
        {
            for(var i=0;i<100;i++){try{using var ready=await guest.GetAsync("/health");if(ready.IsSuccessStatusCode)break;}catch(HttpRequestException){}if(web.HasExited)throw new InvalidOperationException("Web startup failed; inspect private log.");await Task.Delay(100);}
            var login=await Form(admin,"/admin/login");login["UserName"]=username;login["Password"]=password;using(var result=await admin.PostAsync("/admin/login",new FormUrlEncodedContent(login)))Assert(result.StatusCode==HttpStatusCode.Redirect,"Test admin login failed.");
            foreach(var kind in Enum.GetValues<InteractionKind>())
            {
                var target=kind==InteractionKind.Comment?articleId:kind==InteractionKind.Consultation?draftId:(Guid?)null;
                var form=await Form(guest,"/tuong-tac/"+kind+(target==null?"":"/"+target));form["Input.Name"]="Người gửi Test";form["Input.Email"]="http-"+Guid.NewGuid().ToString("N")+"@landportal.invalid";form["Input.Title"]="Hồ sơ Test 2C";form["Input.Text"]="Nội dung riêng của người gửi Test <script>neverexecute</script>";form["Input.PublicConsent"]=kind is InteractionKind.Comment or InteractionKind.Inquiry?"true":"false";
                if(kind==InteractionKind.Inquiry)form["Input.Field"]="Lĩnh vực Test";
                using(var noCsrf=await guest.PostAsync("/tuong-tac/gui",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Kind",kind.ToString()}})))Assert(noCsrf.StatusCode==HttpStatusCode.BadRequest,"Public request accepted without CSRF.");
                using(var obsolete=await guest.PostAsync("/tuong-tac/ma-xac-minh",new FormUrlEncodedContent(form)))Assert(obsolete.StatusCode==HttpStatusCode.Gone,"Old OTP endpoint still accepts submissions.");
                var challenge=Guid.Parse(form["CaptchaId"]);
                using(var sent=await guest.PostAsync("/tuong-tac/gui",new FormUrlEncodedContent(form))){var body=await sent.Content.ReadAsStringAsync();if(!sent.IsSuccessStatusCode)await File.WriteAllTextAsync(Path.Combine(evidence,"request-error.html"),body);Assert(sent.IsSuccessStatusCode,"CAPTCHA submission failed.");Assert(Regex.IsMatch((await new HtmlParser().ParseDocumentAsync(body)).QuerySelector("#receipt-code")?.TextContent??"",@"^[a-f0-9]{32}$"),"No immediate receipt code.");}
                using(var replay=await guest.PostAsync("/tuong-tac/gui",new FormUrlEncodedContent(form)))Assert(replay.StatusCode==HttpStatusCode.BadRequest,"CAPTCHA replay accepted.");var id=await Submission(challenge);submissions[kind]=id;
                Guid receiptMail;await using(var db=new PortalDbContext(options)){Assert(await db.InteractionSubmissions.CountAsync(x=>x.ChallengeId==challenge)==1,"Duplicate HTTP submission.");var sub=await db.InteractionSubmissions.SingleAsync(x=>x.Id==id);Assert(sub.Kind==kind&&sub.PublicSnapshotId==null&&sub.State==InteractionState.Pending,"New submission not pending/private.");if(kind==InteractionKind.Consultation)Assert(sub.PublishedRevisionId==Guid.Parse(form["Input.PublishedRevisionId"])&&sub.DeadlineUtc!=null,"Consultation not bound to published window.");receiptMail=await db.InteractionOutbox.Where(x=>x.SubmissionId==id).Select(x=>x.Id).SingleAsync();Assert(!await db.InteractionChallenges.AnyAsync(x=>x.Id==challenge),"CAPTCHA reused OTP storage.");}await Deliver(receiptMail);
                using(var privateDetail=await guest.GetAsync("/admin/tuong-tac/"+id))Assert(privateDetail.StatusCode==HttpStatusCode.Redirect,"Guest accessed PII inbox.");
                var detail=await Form(admin,"/admin/tuong-tac/"+id);detail["Action"]="process";using(var processed=await admin.PostAsync("/admin/tuong-tac/"+id,new FormUrlEncodedContent(detail)))Assert(processed.StatusCode==HttpStatusCode.Redirect,"Admin processing failed.");
                var stale=new Dictionary<string,string>(detail){["CommandId"]=Guid.NewGuid().ToString(),["Action"]="close"};using(var conflict=await admin.PostAsync("/admin/tuong-tac/"+id,new FormUrlEncodedContent(stale)))Assert(conflict.StatusCode==HttpStatusCode.Conflict,"Stale admin overwrite accepted.");
                checks.Add(new{name=kind+" CAPTCHA public form -> SQL Pending/private -> receipt worker/sink -> privileged inbox",status="PASS"});
            }
            var comment=submissions[InteractionKind.Comment];var publish=await Form(admin,"/admin/tuong-tac/"+comment);publish["Action"]="publish";publish["DisplayName"]="Bạn đọc";publish["Text"]="Bình luận công khai đã duyệt <img onerror=bad>";publish["Answer"]="Phản hồi chính thức của cơ quan";
            using(var approved=await admin.PostAsync("/admin/tuong-tac/"+comment,new FormUrlEncodedContent(publish)))Assert(approved.StatusCode==HttpStatusCode.Redirect,"Comment moderation failed.");
            var article=await guest.GetStringAsync("/bai-viet/"+articleId);var articleDom=await new HtmlParser().ParseDocumentAsync(article);var publicText=articleDom.QuerySelector(".approved-interaction")?.TextContent??"";Assert(publicText.Contains("Bình luận công khai đã duyệt")&&publicText.Contains("Phản hồi của cơ quan")&&articleDom.QuerySelector(".approved-interaction img")==null&&!article.Contains("http-")&&!article.Contains("neverexecute"),"Public comment leaked original/HTML/PII.");
            var inquiry=submissions[InteractionKind.Inquiry];var answer=await Form(admin,"/admin/tuong-tac/"+inquiry);answer["Action"]="answer";answer["Answer"]="Câu trả lời riêng Test";using(var saved=await admin.PostAsync("/admin/tuong-tac/"+inquiry,new FormUrlEncodedContent(answer)))Assert(saved.StatusCode==HttpStatusCode.Redirect,"Inquiry answer failed.");
            var protection=DataProtectionProvider.Create(new DirectoryInfo(StorageOptions.FromConfiguration(config).KeysRoot),b=>b.SetApplicationName("LandPortal"));
            Guid answerMail;await using(var db=new PortalDbContext(options)){answerMail=await db.InteractionOutbox.Where(x=>x.SubmissionId==inquiry&&x.RequestedBy!=null).Select(x=>x.Id).SingleAsync();await new InteractionOutboxService(db,protection,new(){Available=true,SinkPort=1},new(db)).RunOnceAsync(answerMail);}
            await using(var db=new PortalDbContext(options)){var s=await db.InteractionSubmissions.SingleAsync(x=>x.Id==inquiry);Assert(s.State==InteractionState.Answered&&s.ProtectedAnswer!=""&&(await db.InteractionOutbox.SingleAsync(x=>x.Id==answerMail)).State==InteractionMailState.Pending,"SMTP failure lost inquiry answer/business state.");}
            await using(var db=new PortalDbContext(options)){await new InteractionOutboxService(db,protection,new(){Available=true,SinkPort=sink.Port},new(db),new ReplyClock()).RunOnceAsync(answerMail);}
            Assert(sink.Bodies.TryGetValue(answerMail,out var sentAnswer)&&sentAnswer=="Câu trả lời riêng Test","Inquiry answer not delivered to real sink after retry.");checks.Add(new{name="Inquiry reply SQL survives real TCP failure; bounded retry delivers actual sink mail",status="PASS"});
            var q=await Form(admin,"/admin/tuong-tac/"+inquiry);q["Action"]="publish";q["DisplayName"]="Người hỏi";q["Text"]="Câu hỏi đã loại thông tin cá nhân";q["Answer"]="Câu trả lời đã được duyệt";using(var approved=await admin.PostAsync("/admin/tuong-tac/"+inquiry,new FormUrlEncodedContent(q)))Assert(approved.StatusCode==HttpStatusCode.Redirect,"Inquiry publication failed.");
            var publicQuestions=await guest.GetStringAsync("/tuong-tac/hoi-dap-da-duyet");Assert(WebUtility.HtmlDecode(publicQuestions).Contains("Câu hỏi đã loại thông tin cá nhân")&&!publicQuestions.Contains("neverexecute")&&!publicQuestions.Contains("http-"),"Inquiry original exposed.");
            foreach(var kind in new[]{InteractionKind.Contact,InteractionKind.Consultation}){var form=await Form(admin,"/admin/tuong-tac/"+submissions[kind]);form["Action"]="publish";using var forbidden=await admin.PostAsync("/admin/tuong-tac/"+submissions[kind],new FormUrlEncodedContent(form));Assert(forbidden.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden,"Private kind published.");}
            checks.Add(new{name="Reviewed immutable public snapshots exclude original PII; contact/consultation cannot publish",status="PASS"});
            // Revocation acts on an already authenticated cookie, without issuing new rights in Dev.
            var hide=await Form(admin,"/admin/tuong-tac/"+comment);hide["Action"]="hide";hide["Reason"]="Test ẩn";
            await using(var db=new PortalDbContext(options)){await db.UserClaims.Where(x=>x.UserId==userId&&x.ClaimValue==Permissions.CommentModerate).ExecuteDeleteAsync();}
            using(var denied=await admin.PostAsync("/admin/tuong-tac/"+comment,new FormUrlEncodedContent(hide)))Assert(denied.StatusCode==HttpStatusCode.Forbidden,"Revoked moderator retained permission.");
            await using(var db=new PortalDbContext(options)){db.UserClaims.Add(new(){UserId=userId,ClaimType="permission",ClaimValue=Permissions.CommentModerate});await db.SaveChangesAsync();}
            hide=await Form(admin,"/admin/tuong-tac/"+comment);hide["Action"]="hide";hide["Reason"]="Test ẩn";using(var hidden=await admin.PostAsync("/admin/tuong-tac/"+comment,new FormUrlEncodedContent(hide)))Assert(hidden.StatusCode==HttpStatusCode.Redirect,"Hide failed.");Assert(!(await guest.GetStringAsync("/bai-viet/"+articleId)).Contains("Bình luận công khai đã duyệt"),"Hidden comment remained public.");
            checks.Add(new{name="Endpoint live permission revocation, stale rowversion, hide and public output encoding",status="PASS"});
            var fixturePath=Path.Combine(evidence,"browser-fixture.json");await File.WriteAllTextAsync(fixturePath,JsonSerializer.Serialize(new{baseUrl,username,password,articleId,draftId,commentId=comment,inquiryId=inquiry}));
            var node=new ProcessStartInfo("node"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};node.ArgumentList.Add(Path.Combine(root,"tests","browser","interactions-check.mjs"));node.ArgumentList.Add(fixturePath);node.ArgumentList.Add(evidence);using var browser=Process.Start(node)!;var bo=browser.StandardOutput.ReadToEndAsync();var be=browser.StandardError.ReadToEndAsync();await browser.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(evidence,"browser.log"),await bo+"\n"+await be);Assert(browser.ExitCode==0,"Interaction browser failed. Inspect actual screenshots.");
        }
        finally{if(!web.HasExited)web.Kill(entireProcessTree:true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(evidence,"web.log"),await webOutput+"\n"+await webError);await File.WriteAllTextAsync(Path.Combine(evidence,"http-report.json"),JsonSerializer.Serialize(new{started,finished=DateTimeOffset.UtcNow,database="LandPortal_Test",sink="loopback actual SMTP",checks,ownedWebStopped=web.HasExited},new JsonSerializerOptions{WriteIndented=true}));}
    }
}
