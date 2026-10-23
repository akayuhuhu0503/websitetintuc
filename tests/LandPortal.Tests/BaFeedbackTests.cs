using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using System.IO.Compression;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Tests;

public static class BaFeedbackTests
{
    static void Check(bool value,string why)=>V05FinishTests.Check(value,why);
    public static Task DatesAndLabels()
    {
        Check(VietnameseFilterDateBinder.TryParse("04/11/2026",out var d)&&d==new DateOnly(2026,11,4),"Vietnamese date inverted");
        Check(VietnameseFilterDateBinder.TryParse("2026-11-04",out var iso)&&d==iso,"Old ISO bookmarks broken");
        foreach(var invalid in new[]{"11/31/2026","31/11/2026","29/02/2026","4/11/2026","2026/11/04"})Check(!VietnameseFilterDateBinder.TryParse(invalid,out _),"Invalid/ambiguous date accepted");
        Check(VietnameseFilterDateBinder.TryParse("29/02/2028",out _),"Leap day rejected");
        Check(Permissions.All.All(AdminLabels.PermissionNames.ContainsKey),"Missing permission display name");
        foreach(var code in Enum.GetNames<WorkflowState>().Concat(Enum.GetNames<InquiryScanState>()).Concat(Enum.GetNames<VideoState>()).Concat(Enum.GetNames<VideoUploadState>()))Check(AdminLabels.States.ContainsKey(code),"Untranslated enum: "+code);
        Check(AdminLabels.State("Ready")=="Sẵn sàng"&&AdminLabels.State("NotScanned")=="Chưa quét an toàn","Unsafe scan label");
        var check=typeof(AccountAdministrationService).GetMethod("Check",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        foreach(var code in new[]{"PasswordTooShort","DuplicateUserName","ConcurrencyFailure"})
        {
            try{check.Invoke(null,[IdentityResult.Failed(new IdentityError{Code=code,Description="Raw framework text"})]);throw new Exception("Identity rejection changed");}
            catch(System.Reflection.TargetInvocationException e)when(e.InnerException is InvalidOperationException){Check(!e.InnerException.Message.Contains(code)&&!e.InnerException.Message.Contains("Identity")&&!e.InnerException.Message.Contains("Raw framework"),"Technical Identity error exposed");}
        }
        return Task.CompletedTask;
    }
    public static async Task RealWord(string root)
    {
        var file=Directory.GetFiles(Path.Combine(root,"docs","ba-input"),"*.docx").Single(x=>!Path.GetFileName(x).StartsWith("SRS_"));
        var bytes=await File.ReadAllBytesAsync(file);var hash=ScannerTests.Hash(bytes);
        Check(hash=="f982f986d15643e97f225f295f995a949058a221fbe6e973406885f451a2552e","Real BA Word changed");
        var preview=WeeklyDocxReader.Parse(new MemoryStream(bytes),Path.GetFileName(file),2026,43);
        XNamespace w="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var zip=new ZipArchive(new MemoryStream(bytes));using var stream=zip.GetEntry("word/document.xml")!.Open();var xml=XDocument.Load(stream);
        string Text(XElement c)=>string.Join("\n",c.Elements(w+"p").Select(p=>string.Concat(p.Descendants().Where(x=>x.Name==w+"t"||x.Name==w+"br"||x.Name==w+"cr"||x.Name==w+"tab").Select(x=>x.Name==w+"t"?x.Value:x.Name==w+"tab"?"\t":"\n")))).Trim();
        var table=xml.Descendants(w+"tbl").Single(t=>t.Elements(w+"tr").First().Elements(w+"tc").Count()==5);
        var rows=table.Elements(w+"tr").Skip(1).ToArray();Check(rows.Length==10&&preview.Rows.Count==10,"Real rows missing");
        var day=new DateOnly(2026,10,19);var coordinates=new List<object>();
        for(var i=0;i<rows.Length;i++)
        {
            var cells=rows[i].Elements(w+"tc").Select(Text).ToArray();if(cells[0].Contains("20/10"))day=new(2026,10,20);if(cells[0].Contains("21/10"))day=new(2026,10,21);if(cells[0].Contains("22/10"))day=new(2026,10,22);if(cells[0].Contains("23/10"))day=new(2026,10,23);
            var parsed=preview.Rows[i];Check(parsed.Date==day&&parsed.Chair==cells[1]&&parsed.Time==cells[2]&&parsed.Content==cells[3]&&parsed.Location==cells[4],"Real Word cell changed at row "+(i+2));
            Check(parsed.Period=="Chưa xác định","Invented period");Check(parsed.Attendees==string.Join("\n",cells[3].Split('\n').Select(x=>x.Trim()).Where(x=>x.StartsWith("TP:")).Select(x=>x[3..].Trim())),"Participants differ at Word row "+(i+2));
            coordinates.Add(new{wordRow=i+2,date=parsed.Date,time=parsed.Time,allFiveCellsExact=true});
        }
        Check(preview.Warnings.Any(x=>x.Contains("Phát hành lúc"))&&preview.Warnings.Any(x=>x.Contains("cập nhật lần 01"))&&!preview.BusinessTemplateApproved,"Lost issue/update provenance or false UAT approval");
        await V05FinishTests.Denied<InvalidOperationException>(()=>{WeeklyDocxReader.Parse(new MemoryStream(bytes),Path.GetFileName(file),2026,42);return Task.CompletedTask;});
        var folder=TestEvidencePaths.At(root,"real-word");Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder,"preview-private.json"),JsonSerializer.Serialize(new{sourceSha256=hash,preview.Template,preview.Rows,preview.Warnings,coordinates,parser="PASS",scanner="NOT RUN: real staff Word not submitted to anonymous Test scanner",published=false,AC16_1="BLOCKED: only one real week supplied"},new JsonSerializerOptions{WriteIndented=true}));
        Check(ScannerTests.Hash(await File.ReadAllBytesAsync(file))==hash,"Original Word modified");
        await V05WordTests.RunAsync(root);
    }
    public static async Task Http(string root,IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        const string url="http://127.0.0.1:5291";var folder=TestEvidencePaths.At(root,"http");Directory.CreateDirectory(folder);
        var password=Guid.NewGuid()+"!Aa2";string username;string userId;var marker="BA"+Guid.NewGuid().ToString("N");var documentIds=new List<Guid>();Guid realWeekId;Guid videoDraftId;Guid? videoPublicId=null;Guid? videoAssetId=null;
        await using(var db=new PortalDbContext(options))
        {
            userId=await InquiryFinishTests.User(db,Permissions.All,"BA technical QA");var user=(await db.Users.FindAsync(userId))!;username=user.UserName!;user.PasswordHash=new PasswordHasher<PortalUser>().HashPassword(user,password);await db.SaveChangesAsync();
            var actor=await new CurrentPermissionService(db).GetAsync(userId);var wf=new WorkflowService(db,new(),MediaUploadOptions.FromConfiguration(config,"Development"),StorageOptions.FromConfiguration(config));
            foreach(var date in new[]{new DateOnly(2026,4,11),new DateOnly(2026,11,4)})
            {
                var input=Phase2aSqlTests.Input(marker+" "+date.ToString("yyyy-MM-dd"));input.Kind=ContentKind.LegalDocument;input.Metadata=new(){Number=marker+"-"+date.ToString("yyyyMMdd"),DocumentType="Quyết định",IssuedDate=date,IssuingBody="Anonymous BA technical Test"};
                var saved=await wf.SaveAsync(null,input,actor,null,Guid.NewGuid());await wf.PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());documentIds.Add(saved.ContentId);
            }
            var wordPath=Directory.GetFiles(Path.Combine(root,"docs","ba-input"),"*.docx").Single(x=>!Path.GetFileName(x).StartsWith("SRS_"));using var word=File.OpenRead(wordPath);var parsed=WeeklyDocxReader.Parse(word,Path.GetFileName(wordPath),2026,43);
            var weekly=Phase2aSqlTests.Input("KIỂM THỬ RIÊNG TƯ — Lịch Lãnh đạo Trung tâm tuần43 "+marker,76);weekly.Kind=ContentKind.StaticPage;weekly.Metadata=new(){WeeklyYear=2026,WeeklyWeek=43,WeeklyRowsJson=JsonSerializer.Serialize(parsed.Rows),WeeklyChangeNote="Đối chiếu parser Word BA, không phải lịch chính thức của Cục; file gốc chỉ ở kho tài liệu riêng, chưa quét/phát hành"};
            realWeekId=(await wf.SaveAsync(null,weekly,actor,null,Guid.NewGuid())).ContentId;
            var emptyVideo=Phase2aSqlTests.Input("Video Test chưa có tệp "+marker);emptyVideo.Kind=ContentKind.Video;var draft=await wf.SaveAsync(null,emptyVideo,actor,null,Guid.NewGuid());videoDraftId=draft.ContentId;
            await V05FinishTests.Denied<InvalidOperationException>(()=>wf.PublishDirectAsync(draft.ContentId,draft.CurrentRevisionId!.Value,actor,draft.RowVersion,Guid.NewGuid()));
            // Reuse one genuine previously processed anonymous Test clip; no native
            // scanner/FFmpeg rerun or source-media changes for a label-only delta.
            var ready=await db.Set<VideoFile>().AsNoTracking().Where(v=>v.State==VideoState.Ready&&v.Original.ScanStatus=="Clean").OrderByDescending(v=>v.ReadyUtc).FirstOrDefaultAsync();
            Check(ready!=null,"No genuine Ready Test clip available for required player smoke");
            var video=Phase2aSqlTests.Input("Video phát thử vô danh "+marker);video.Kind=ContentKind.Video;video.Assets=[new(){MediaAssetId=ready!.MediaAssetId,Usage="Video"}];var playable=await wf.SaveAsync(null,video,actor,null,Guid.NewGuid());
            await wf.PublishDirectAsync(playable.ContentId,playable.CurrentRevisionId!.Value,actor,playable.RowVersion,Guid.NewGuid());videoPublicId=playable.ContentId;videoAssetId=ready.MediaAssetId;
        }
        var start=new ProcessStartInfo(Path.Combine(root,".local","dotnet","dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(TestRuntimePaths.Assembly(root,"Web"));start.ArgumentList.Add("--contentRoot");start.ArgumentList.Add(Path.Combine(root,"src","Web"));
        start.Environment["Public__BaseUrl"]=url;start.Environment["DOTNET_ENVIRONMENT"]="Development";start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";start.Environment["Mail__Transport"]="Disabled";start.Environment["Media__AllowUnscannedDevelopmentUploads"]="false";start.Environment["Captcha__TestScope"]=Guid.NewGuid().ToString("N");start.Environment["Captcha__AttemptsPer10Minutes"]="20";start.Environment["Video__Enabled"]="true";
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        HttpClient Client()=>new(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new()}){BaseAddress=new(url),Timeout=TimeSpan.FromSeconds(40)};
        using var guest=Client();using var admin=Client();var checks=new List<string>();
        async Task<Dictionary<string,string>> Form(HttpClient client,string path)
        {
            using var r=await client.GetAsync(path);Check(r.IsSuccessStatusCode,"Form HTTP: "+path+" "+r.StatusCode);var dom=new HtmlParser().ParseDocument(await r.Content.ReadAsStringAsync());var fields=new Dictionary<string,string>();
            foreach(var e in dom.QuerySelectorAll("input[name]"))if(e.GetAttribute("type") is not ("checkbox" or "radio"))fields[e.GetAttribute("name")!]=e.GetAttribute("value")??"";
            CaptchaHttp.Add(dom,fields);return fields;
        }
        try
        {
            var ready=false;for(var i=0;i<150;i++){try{using var h=await guest.GetAsync("/health");if(h.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}if(process.HasExited)break;await Task.Delay(100);}Check(ready,"Test host not ready");
            foreach(var value in new[]{"04/11/2026","2026-11-04"})
            {using var r=await guest.GetAsync("/van-ban?q="+marker+"&issuedFrom="+Uri.EscapeDataString(value)+"&issuedTo="+Uri.EscapeDataString(value));var html=await r.Content.ReadAsStringAsync();Check(r.IsSuccessStatusCode&&html.Contains(documentIds[1].ToString())&&!html.Contains(documentIds[0].ToString()),"HTTP date binding reversed or SQL filter ignored");}
            foreach(var query in new[]{"issuedFrom=31/11/2026","issuedFrom=04/11/2026&issuedTo=03/11/2026","issuedFrom=04/11/2026&issuedFrom=11/04/2026"}){using var r=await guest.GetAsync("/van-ban?"+query);Check(r.StatusCode==HttpStatusCode.BadRequest,"Invalid range/date/duplicate accepted");}
            checks.Add("VN/ISO HTTP dates and SQL result isolation; invalid/range/duplicate rejected");
            foreach(var kind in new[]{"Inquiry","Contact"})
            {
                var form=await Form(guest,"/tuong-tac/"+kind);form["Input.Name"]="Anonymous BA QA";form["Input.Email"]="ba-private@landportal.invalid";form["Input.Title"]=marker+kind;form["Input.Text"]="PRIVATE BA QA "+marker;if(kind=="Inquiry")form["Input.Field"]="Đất đai";form["Input.PublicConsent"]="false";
                using(var denied=await guest.PostAsync("/tuong-tac/gui",new FormUrlEncodedContent(form.Where(x=>x.Key!="__RequestVerificationToken"))))Check(denied.StatusCode==HttpStatusCode.BadRequest,"Public CSRF guard bypassed");
                using var sent=await guest.PostAsync("/tuong-tac/gui",new FormUrlEncodedContent(form));var sentDom=new HtmlParser().ParseDocument(await sent.Content.ReadAsStringAsync());Check(sent.IsSuccessStatusCode&&sentDom.QuerySelector("h1")?.TextContent.Contains("tiếp nhận")==true,"Public form submission failed "+kind+" "+(int)sent.StatusCode+": "+string.Join("; ",sentDom.QuerySelectorAll(".validation-summary li").Select(x=>x.TextContent)));
                await using var db=new PortalDbContext(options);Check(await db.InteractionSubmissions.AnyAsync(x=>x.Kind==Enum.Parse<InteractionKind>(kind)&&x.State==InteractionState.Pending&&x.EmailHash==TextNormalizer.Hash("ba-private@landportal.invalid")),"Private pending SQL inbox absent");
            }
            using(var r=await guest.GetAsync("/tim-kiem?q="+marker)){var html=await r.Content.ReadAsStringAsync();Check(!html.Contains("PRIVATE BA QA")&&!html.Contains("ba-private@"),"Private submission leaked");}checks.Add("Inquiry/Contact real CAPTCHA POST -> private pending SQL; CSRF denied; no public PII");
            var login=await Form(admin,"/admin/login");login["UserName"]=username;login["Password"]=password;using(var r=await admin.PostAsync("/admin/login",new FormUrlEncodedContent(login)))Check(r.StatusCode==HttpStatusCode.Redirect,"Identity login failed");
            foreach(var path in new[]{"/admin","/admin/roles","/admin/site","/admin/videos","/admin/schedules"}){using var r=await admin.GetAsync(path);Check(r.IsSuccessStatusCode,"Admin localized page failed: "+path);var dom=new HtmlParser().ParseDocument(await r.Content.ReadAsStringAsync());Check(!dom.Body!.TextContent.Contains("site.manage")&&!dom.Body.TextContent.Contains("content.publish"),"Raw permission exposed in visible text");}
            checks.Add("Actual Identity login and localized admin pages");
            using(var r=await guest.GetAsync("/bai-viet/"+realWeekId))Check(r.StatusCode==HttpStatusCode.NotFound,"Real staff schedule draft exposed");
            using(var r=await guest.GetAsync("/bai-viet/"+videoDraftId))Check(r.StatusCode==HttpStatusCode.NotFound,"Empty video draft exposed");
            using(var r=await guest.GetAsync("/bai-viet/"+videoPublicId)){var dom=new HtmlParser().ParseDocument(await r.Content.ReadAsStringAsync());Check(r.IsSuccessStatusCode&&dom.QuerySelector("video source")?.GetAttribute("src")=="/video-media/"+videoAssetId+"/play"&&dom.QuerySelector("iframe")==null,"Internal video player missing or external player rendered");}
            using(var req=new HttpRequestMessage(HttpMethod.Get,"/video-media/"+videoAssetId+"/play")){req.Headers.Range=new(0,7);using var r=await guest.SendAsync(req);Check(r.StatusCode==HttpStatusCode.PartialContent&&(await r.Content.ReadAsByteArrayAsync()).Length==8,"Existing native MP4 Range smoke failed");}
            checks.Add("Real week43 stored only as private Test draft; video without file cannot publish; genuine existing MP4 internal player and Range206");
            using(var r=await guest.GetAsync("/cong-khai-thong-tin/nguoi-nuoc-ngoai-dinh-cu-o-viet-nam"))Check(r.IsSuccessStatusCode,"FR17 empty category not navigable");
            using(var r=await guest.GetAsync("/tra-cuu-ho-so"))Check(new HtmlParser().ParseDocument(await r.Content.ReadAsStringAsync()).Body!.TextContent.Contains("tạm khóa"),"Private lookup fence missing");
            var privatePath=Path.Combine(folder,"browser-input-private.json");await File.WriteAllTextAsync(privatePath,JsonSerializer.Serialize(new{baseUrl=url,username,password,marker,realWeekId,videoDraftId,videoPublicId}));
            try
            {
                var node=new ProcessStartInfo("node"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};node.ArgumentList.Add("tests/browser/ba-feedback-check.mjs");node.ArgumentList.Add(privatePath);node.ArgumentList.Add(folder);
                using var browser=Process.Start(node)!;var output=browser.StandardOutput.ReadToEndAsync();var error=browser.StandardError.ReadToEndAsync();await browser.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(folder,"browser.log"),await output+"\n"+await error);Check(browser.ExitCode==0,"Focused browser review failed; see unique browser.log");
            }finally{File.Delete(privatePath);}
            await File.WriteAllTextAsync(Path.Combine(folder,"http-report.json"),JsonSerializer.Serialize(new{checks,database="LandPortal_Test",devWrites=0,mail="Disabled",nativeScannerRerun="NOT RUN: scanner algorithm unchanged",fullRegression="NOT RUN"},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(folder,"web.log"),await stdout+"\n"+await stderr);
            if(videoPublicId!=null){await using var db=new PortalDbContext(options);var row=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==videoPublicId);var actor=await new CurrentPermissionService(db).GetAsync(userId);await new WorkflowService(db,new(),MediaUploadOptions.FromConfiguration(config,"Development"),StorageOptions.FromConfiguration(config)).UnpublishAsync(row.Id,actor,row.RowVersion,Guid.NewGuid());}
        }
    }
}
