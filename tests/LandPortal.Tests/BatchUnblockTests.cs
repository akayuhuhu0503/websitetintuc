using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;

public static class BatchUnblockTests
{
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static async Task<(InventoryState State,InventoryItem Item,OptionalImageReview Review)> MissingPhoto(string root)
    {
        var state=new InventoryState();var id="8"+Random.Shared.NextInt64(100000000,999999999);
        var url=SourceCatalog.BaseUrl+"/unblock-technical-test-"+id+".htm";var photo=SourceCatalog.BaseUrl+"/Image/missing-"+id+".jpg";
        var text=string.Join(" ",Enumerable.Repeat("Đây là văn bản kiểm thử kỹ thuật có đầy đủ thông tin bằng chữ, không phải dữ liệu nguồn hoặc bằng chứng nghiệp vụ.",5));
        var raw=$"<html><head><link rel='canonical' href='{SourceCatalog.BaseUrl}/Pages/chi-tiet-tin-tuc.aspx?ItemID={id}'></head><body><h1 class='tieu-de-tin-tuc'>TECHNICAL TEST missing optional photo {id}</h1><article class='noi-dung-chi-tiet-tin'><p>{text}</p><figure><img src='{photo}'><figcaption>Chú thích phải giữ nguyên</figcaption></figure></article></body></html>";
        var bytes=Encoding.UTF8.GetBytes(raw);var sha=SourceInventory.Hash(bytes);var key="responses/"+sha+".bin";var dir=Path.Combine(root,".local/phase3a",state.Id.ToString("N"),"responses");Directory.CreateDirectory(dir);await File.WriteAllBytesAsync(Path.Combine(dir,sha+".bin"),bytes);
        var parsed=SourceParser.Parse(raw,url,ContentKind.Article,id);parsed.CategoryIds=[75];parsed.PrimaryCategoryId=75;
        var item=new InventoryItem{Source=parsed,RawSha256=sha,Aliases=[url,parsed.CanonicalUrl]};state.Items.Add(item);state.Urls.Add(new(){Url=url,Role="Detail"});state.Captures.Add(new(url,url,200,"text/html",key,sha,bytes.Length,DateTimeOffset.UtcNow,"TechnicalFixture"));
        return(state,item,new(photo,"TECHNICAL TEST: ảnh minh họa không chứa dữ kiện riêng.","TECHNICAL FIXTURE source raw contains full text and retained caption; not approval for any real source photo."));
    }
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(string root,DbContextOptions<PortalDbContext> options,IConfiguration config)
    {
        var cfg=new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{{"DOTNET_ENVIRONMENT","Development"}}).Build();var storage=StorageOptions.FromConfiguration(cfg);
        yield return("Unblock missing reviewed optional photo imports without hotlink; SQL warning revision idempotence and rollback",async()=>{
            var f=await MissingPhoto(root);await using var db=new PortalDbContext(options);var hash=SourceInventory.HashText(JsonSerializer.Serialize(f.State));var svc=new SourceBatch(db,storage,cfg,root,f.State,hash);
            var blocked=await svc.PreviewAsync();Check(blocked.Rows.Single().Action=="Blocked","Unreviewed missing photo accepted.");
            // An empty HTTP 200 is an absent image, not a valid JPEG or a Clean file.
            var emptyHash=SourceInventory.Hash([]);var emptyKey="responses/"+emptyHash+".bin";await File.WriteAllBytesAsync(Path.Combine(root,".local/phase3a",f.State.Id.ToString("N"),emptyKey),[]);
            f.State.Urls.Add(new(){Url=f.Review.Url,Role="File",CaptureSha=emptyHash,ScanStatus="NotScanned"});
            f.State.Captures.Add(new(f.Review.Url,f.Review.Url,200,"image/jpeg",emptyKey,emptyHash,0,DateTimeOffset.UtcNow,"TechnicalFixture"));
            var review=new Dictionary<string,List<OptionalImageReview>>{{"Article:"+f.Item.Source.SourceId,[f.Review]}};
            var plan=await svc.PreviewAsync(review);Check(plan.Rows.Single().Action=="CreatePublishedTest"&&plan.Rows.Single().Findings.Any(x=>x.Code=="OPTIONAL_IMAGE_OMITTED"&&x.Severity=="NON-BLOCKING"),"Reviewed absence not nonblocking.");
            var sha=SourceInventory.HashText(JsonSerializer.Serialize(plan));var outcome=(await svc.CommitAsync(plan,sha)).Single();Check(outcome.Status=="CreatePublishedTest","Optional missing import failed.");
            var rev=await db.Revisions.Include(x=>x.Assets).SingleAsync(x=>x.Id==outcome.RevisionId);Check(!rev.BodyHtml.Contains(f.Review.Url)&&!rev.BodyHtml.Contains("<img")&&rev.BodyHtml.Contains("Chú thích phải giữ nguyên")&&rev.Assets.Count==0&&rev.CoverAssetId==null,"Missing photo replaced, hotlinked, or caption lost.");
            Check(await db.AuditLogs.AnyAsync(x=>x.ContentId==outcome.ContentId&&x.RevisionId==rev.Id&&x.Action=="Phase3B.ImportWarning"&&x.Detail.Contains("OPTIONAL_IMAGE_OMITTED")),"Admin warning missing.");
            var before=await db.Revisions.CountAsync();var audits=await db.AuditLogs.CountAsync();Check((await svc.CommitAsync(plan,sha)).Single().Status=="AlreadyCommitted"&&await db.Revisions.CountAsync()==before&&await db.AuditLogs.CountAsync()==audits,"Optional import not idempotent.");Check((await svc.VerifyAsync(plan,sha)).Count==0,"Optional verify mismatch.");
            Check(await svc.RollbackPublicationAsync(plan,sha)==1&&await svc.RollbackPublicationAsync(plan,sha)==0,"Optional rollback not safe.");
            var folder=TestEvidencePaths.At(root,"unblock-optional");Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,"fixture-result.json"),JsonSerializer.Serialize(new{technicalFixture=true,outcome,plan,revisionBody=rev.BodyHtml}));
        });
        yield return("Unblock optional review cannot omit captured NotScanned Infected PDF or image-only essential content",async()=>{
            const string hash="d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3";
            var state=await SourceBatch.ReadCheckpointAsync(root,".local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/"+hash+".bin",hash);await using var db=new PortalDbContext(options);var svc=new SourceBatch(db,storage,cfg,root,state,hash);
            var real=state.Items.Single(x=>x.Source.Kind==ContentKind.Article&&x.Source.SourceId=="3349");var node=state.Urls.Single(x=>x.Url==real.Source.AssetUrls[0]);
            var review=new[]{new OptionalImageReview(node.Url,"TECHNICAL TEST negative optional claim","Not an approval")};
            Check((await svc.PrepareAsync(real,review)).Errors.Any(x=>x.StartsWith("SCAN_NOT_CLEAN")),"Optional claim bypassed NotScanned.");node.ScanStatus="Infected";Check((await svc.PrepareAsync(real,review)).Errors.Any(x=>x.StartsWith("SCAN_NOT_CLEAN")),"Optional claim bypassed Infected.");
            node.ScanStatus="NotScanned";node.CaptureSha=null;Check((await svc.PrepareAsync(real,review)).Errors.Any(x=>x.StartsWith("MISSING_FILE")),"Captured unsafe bytes became missing optional photo.");
            var legal=state.Items.Single(x=>x.Source.Kind==ContentKind.LegalDocument&&x.Source.SourceId=="231");Check((await svc.PrepareAsync(legal,[new(legal.Source.AssetUrls[0],"technical negative","technical evidence")])).Errors.Contains("INVALID_OPTIONAL_IMAGE_REVIEW"),"Essential PDF omitted.");
            var leaders=state.Items.Single(x=>x.Source.Kind==ContentKind.StaticPage&&x.Source.SourceId=="1780");Check((await svc.PrepareAsync(leaders,[new(leaders.Source.AssetUrls[0],"technical negative","technical evidence")])).Errors.Contains("INVALID_OPTIONAL_IMAGE_REVIEW"),"Image-only leadership page accepted as optional.");
        });
    }
    public static async Task HttpAsync(string root,IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        const string url="http://127.0.0.1:5301";var folder=TestEvidencePaths.At(root,"unblock-http");Directory.CreateDirectory(folder);var checks=new List<object>();
        var start=new ProcessStartInfo(Path.Combine(root,".local/dotnet/dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(TestRuntimePaths.Assembly(root,"Web"));start.ArgumentList.Add("--contentRoot");start.ArgumentList.Add(Path.Combine(root,"src/Web"));start.Environment["DOTNET_ENVIRONMENT"]="Development";start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";start.Environment["Public__BaseUrl"]=url;start.Environment["Mail__Transport"]="Disabled";start.Environment["Media__AllowUnscannedDevelopmentUploads"]="false";
        using var web=Process.Start(start)!;var stdout=web.StandardOutput.ReadToEndAsync();var stderr=web.StandardError.ReadToEndAsync();using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new(url),Timeout=TimeSpan.FromSeconds(30)};
        try{
            var ready=false;for(var i=0;i<100;i++){try{using var r=await client.GetAsync("/health");if(r.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(200);}Check(ready,"Owned web unavailable.");
            await using var db=new PortalDbContext(options);
            foreach(var sourceId in new[]{"3666","3670","3673"}){
                var record=await db.SourceRecords.SingleAsync(x=>x.Source==SourceBatch.Source&&x.Kind==ContentKind.Article&&x.SourceId==sourceId);var content=await db.Contents.SingleAsync(x=>x.Id==record.ContentId);var rev=await db.Revisions.Include(x=>x.Assets).SingleAsync(x=>x.Id==content.PublishedRevisionId);
                using var page=await client.GetAsync("/bai-viet/"+content.Id);Check(page.IsSuccessStatusCode&&WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync()).Contains(rev.Title),"Real article smoke failed.");
                foreach(var a in rev.Assets){var media=await db.MediaAssets.SingleAsync(x=>x.Id==a.MediaAssetId);using var image=await client.GetAsync("/media/"+media.Id);Check(image.IsSuccessStatusCode&&SourceInventory.Hash(await image.Content.ReadAsByteArrayAsync())==media.Sha256,"Actual real image bytes mismatch.");}
                checks.Add(new{sourceId,content.Id,images=rev.Assets.Count,status="PASS"});
            }
            using(var category=await client.GetAsync("/chuyen-muc/xay-dung-pho-bien-chinh-sach-phap-luat"))Check(category.IsSuccessStatusCode,"Category smoke failed.");
            var privateAsset=await db.MediaAssets.FirstAsync(x=>x.ValidationStatus==SourceBatch.AssetValidation&&x.ScanStatus=="NotScanned");
            using(var denied=await client.GetAsync("/media/"+privateAsset.Id))Check(denied.StatusCode==HttpStatusCode.NotFound,"NotScanned media exposed.");
            using(var denied=await client.GetAsync("/admin/media/"+privateAsset.Id+"/preview"))Check(denied.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized,"Private preview exposed.");
            checks.Add(new{name="category and public/private NotScanned ACL",status="PASS"});
        }finally{if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(folder,"web.log"),await stdout+"\n"+await stderr);await File.WriteAllTextAsync(Path.Combine(folder,"http.json"),JsonSerializer.Serialize(new{checks,ownedWebStopped=true},new JsonSerializerOptions{WriteIndented=true}));}
    }
}
