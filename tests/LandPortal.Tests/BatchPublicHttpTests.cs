using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;
public static class BatchPublicHttpTests
{
    static void Check(bool yes,string reason){if(!yes)throw new InvalidOperationException(reason);}
    public static async Task RunAsync(string root,IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        var folder=TestEvidencePaths.At(root,"batch-public-http");Directory.CreateDirectory(folder);const string url="http://127.0.0.1:5299";
        var p=new ProcessStartInfo(Path.Combine(root,".local","dotnet","dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        p.ArgumentList.Add(TestRuntimePaths.Assembly(root,"Web"));p.ArgumentList.Add("--contentRoot");p.ArgumentList.Add(Path.Combine(root,"src","Web"));p.Environment["DOTNET_ENVIRONMENT"]="Development";p.Environment["ASPNETCORE_ENVIRONMENT"]="Development";p.Environment["Public__BaseUrl"]=url;p.Environment["Mail__Transport"]="Disabled";p.Environment["Media__AllowUnscannedDevelopmentUploads"]="false";
        using var web=Process.Start(p)!;var stdout=web.StandardOutput.ReadToEndAsync();var stderr=web.StandardError.ReadToEndAsync();
        var checks=new List<object>();Guid unsafeContent=Guid.Empty;using var guest=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new(url),Timeout=TimeSpan.FromSeconds(40)};
        var documents=new List<string>();var publicPaths=new List<string>{"/","/van-ban","/du-thao","/chuyen-muc/xay-dung-pho-bien-chinh-sach-phap-luat"};
        try{
            var ready=false;for(var i=0;i<100;i++){try{using var h=await guest.GetAsync("/health");if(h.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(200);}Check(ready,"Owned web did not start.");
            await using var db=new PortalDbContext(options);var ids=new[]{"3666","3670","3673"};
            var records=await db.SourceRecords.AsNoTracking().Where(x=>x.Source=="qldd.mae.gov.vn"&&x.Kind==ContentKind.Article&&ids.Contains(x.SourceId)).ToListAsync();Check(records.Count==3,"Real batch must be committed before HTTP evidence; never synthesize this source sample.");
            foreach(var r in records){
                var c=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==r.ContentId);var rev=await db.Revisions.AsNoTracking().Include(x=>x.Assets).Include(x=>x.Categories).SingleAsync(x=>x.Id==c.PublishedRevisionId);Check(c.IsPublic&&rev.IsSourceRevision,"Public revision incorrect.");
                var path="/bai-viet/"+c.Id;publicPaths.Add(path);using var response=await guest.GetAsync(path);var html=await response.Content.ReadAsStringAsync();var doc=new HtmlParser().ParseDocument(html);
                Check(response.IsSuccessStatusCode&&doc.QuerySelector("h1")?.TextContent.Trim()==rev.Title&&WebUtility.HtmlDecode(html).Contains(rev.Author)&&WebUtility.HtmlDecode(html).Contains(c.SourcePublishedDate!.Value.ToString("dd/MM/yyyy")),"Title/author/date/public HTML mismatch.");
                var srcs=doc.QuerySelectorAll("img[src]").Select(x=>x.GetAttribute("src")!).Where(x=>x.StartsWith("/media/")).Distinct().ToList();Check(srcs.Count>=rev.Assets.Count,"Missing real inline images.");
                foreach(var image in rev.Assets){
                    var asset=await db.MediaAssets.AsNoTracking().SingleAsync(x=>x.Id==image.MediaAssetId);using var get=await guest.GetAsync("/media/"+asset.Id);Check(get.IsSuccessStatusCode&&Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await get.Content.ReadAsByteArrayAsync()))==asset.Sha256,"Media bytes/ACL mismatch.");
                    using var head=new HttpRequestMessage(HttpMethod.Head,"/media/"+asset.Id);using var hr=await guest.SendAsync(head);Check(hr.IsSuccessStatusCode,"HEAD failed.");
                }
                foreach(var query in new[]{rev.Title,TextNormalizer.Search(rev.Title)}){using var result=await guest.GetAsync("/tim-kiem?q="+Uri.EscapeDataString(query));Check(result.IsSuccessStatusCode&&(await result.Content.ReadAsStringAsync()).Contains(c.Id.ToString()),"Vietnamese search failed.");}
                var aliases=await db.SourceUrlAliases.AsNoTracking().Where(x=>x.ContentId==c.Id).ToListAsync();
                foreach(var a in aliases){var uri=new Uri(a.Url);using var redirect=await guest.GetAsync(uri.PathAndQuery);Check(redirect.StatusCode==HttpStatusCode.MovedPermanently&&redirect.Headers.Location?.OriginalString.StartsWith(path)==true,"Alias does not redirect directly to exact content.");using var target=await guest.GetAsync(redirect.Headers.Location);Check(target.IsSuccessStatusCode,"Redirect chain/loop/target failure.");}
                checks.Add(new{name="Real source "+r.SourceId+" public revision/date/author/local exact images/search/aliases",status="PASS",images=rev.Assets.Count,aliases=aliases.Count});
            }
            foreach(var path in publicPaths.Distinct()){using var response=await guest.GetAsync(path);Check(response.IsSuccessStatusCode,"Public route failed: "+path);}
            // Negative fixture never receives a Clean conclusion. It cannot borrow the Development source exception.
            var invalid=new MediaAsset{Sha256=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N"),StorageKey="phase3b/not-scanned-test.bin",ByteLength=3,MimeType="image/jpeg",ScanStatus="NotScanned",ValidationStatus="Phase3BValidated",Origin="Source"};db.MediaAssets.Add(invalid);await db.SaveChangesAsync();
            var technical=new Content{Kind=ContentKind.Article,PrimaryCategoryId=75,SourcePublishedDate=DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),IsPublic=false};db.Contents.Add(technical);await db.SaveChangesAsync();unsafeContent=technical.Id;
            var technicalRevision=new ContentRevision{ContentId=technical.Id,Title="TECHNICAL TEST unavailable cover "+Guid.NewGuid(),Summary="Fixture riêng, không phải nội dung nguồn",BodyHtml="<p>Negative media policy fixture</p>",ContentHash=Guid.NewGuid().ToString("N"),PrimaryCategoryId=75,CoverAssetId=invalid.Id,Assets=[new(){MediaAssetId=invalid.Id,Usage="Cover"}],Categories=[new(){CategoryId=75}]};db.Revisions.Add(technicalRevision);await db.SaveChangesAsync();technical.CurrentRevisionId=technicalRevision.Id;technical.PublishedRevisionId=technicalRevision.Id;technical.IsPublic=true;technical.State=WorkflowState.Published;await db.SaveChangesAsync();
            using(var list=await guest.GetAsync("/chuyen-muc/xay-dung-pho-bien-chinh-sach-phap-luat")){var html=await list.Content.ReadAsStringAsync();Check(list.IsSuccessStatusCode&&html.Contains(technical.Id.ToString())&&!html.Contains("/media/"+invalid.Id),"Public card exposed an unavailable cover.");}
            using(var denied=await guest.GetAsync("/media/"+invalid.Id))Check(denied.StatusCode==HttpStatusCode.NotFound,"Private NotScanned media exposed.");
            using(var denied=await guest.GetAsync("/admin/media/"+invalid.Id+"/preview"))Check(denied.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized,"Guest preview bypass.");
            var fixture=Path.Combine(folder,"browser-fixture.json");await File.WriteAllTextAsync(fixture,JsonSerializer.Serialize(new{baseUrl=url,paths=publicPaths}));
            var browser=new ProcessStartInfo("node"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};browser.ArgumentList.Add(Path.Combine(root,"tests/browser/batch-check.mjs"));browser.ArgumentList.Add(fixture);browser.ArgumentList.Add(folder);using var child=Process.Start(browser)!;var bo=child.StandardOutput.ReadToEndAsync();var be=child.StandardError.ReadToEndAsync();await child.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(folder,"browser.log"),await bo+"\n"+await be);Check(child.ExitCode==0,"Batch responsive browser failed.");
            checks.Add(new{name="Real public routes desktop/mobile, no off-origin dependency, guest private media denied",status="PASS"});
        }finally{
            if(unsafeContent!=Guid.Empty){await using var cleanup=new PortalDbContext(options);var fixtureRow=await cleanup.Contents.SingleAsync(x=>x.Id==unsafeContent);fixtureRow.IsPublic=false;fixtureRow.PublishedRevisionId=null;fixtureRow.State=WorkflowState.Unpublished;await cleanup.SaveChangesAsync();}
            if(!web.HasExited)web.Kill(entireProcessTree:true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(folder,"web.log"),await stdout+"\n"+await stderr);await File.WriteAllTextAsync(Path.Combine(folder,"http-report.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",utc=DateTimeOffset.UtcNow,checks,ownedWebStopped=true,pdfNewImport="BLOCKED: cached PDFs remain NotScanned; existing document regression separate"},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
