using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;

static class SourcePublicationChecks
{
    internal static async Task Run(IServiceProvider services,HttpClient client,string token,DbContextOptions<PortalDbContext> options,
        StorageOptions storage,FixtureSource fixture,(CrawlItem Item,SourceSyncCheckpoint Data) initial,string run,string evidence,Action<bool,string> check)
    {
        async Task<T> Scope<T>(Func<IServiceProvider,Task<T>> action){await using var scope=services.CreateAsyncScope();return await action(scope.ServiceProvider);}
        async Task<HttpResponseMessage> Post(string path,Dictionary<string,string> values,bool csrf=true){if(csrf)values["__RequestVerificationToken"]=token;return await client.PostAsync(path,new FormUrlEncodedContent(values));}
        async Task<byte[]> Version(Guid id){await using var db=new PortalDbContext(options);return (await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==id)).RowVersion;}
        async Task<WorkflowResult> Save(DraftInput input)=>await Scope(async s=>await s.GetRequiredService<WorkflowService>().SaveAsync(null,input,await s.GetRequiredService<CurrentPermissionService>().GetAsync("admin"),null,Guid.NewGuid()));
        async Task<SourceSyncCheckpoint> BindEvidence(Guid id,ParsedSource source)
        {
            await using var db=new PortalDbContext(options);
            var cp=new SourceSyncCheckpoint{ContentId=id,Source=source,Title=source.Title,Hash=SourceSyncPolicy.Hash(source)};
            db.SourceRecords.Add(new(){ContentId=id,Source="qldd.mae.gov.vn",SourceId=source.SourceId,Kind=source.Kind,CanonicalUrl=source.CanonicalUrl});
            db.CrawlRuns.Add(new(){Status=SourceSyncPolicy.RunStatus,Checkpoint=SourceSyncPolicy.Serialize(cp)});await db.SaveChangesAsync();return cp;
        }
        async Task<IReadOnlyDictionary<string,string>> Requirements(Guid id)=>await Scope(s=>s.GetRequiredService<WorkflowService>().InspectRequirementsAsync(id));
        var article=new DraftInput{Kind=ContentKind.Article,Title="Source without author or image",Summary="A factual opening from the source.",BodyHtml="<p>Real source content.</p>",PrimaryCategoryId=1,SourceCredit="https://qldd.mae.gov.vn/news-910001.htm"};
        var saved=await Save(article);
        var missing=await Requirements(saved.ContentId);
        check(missing.ContainsKey("Author")&&missing.ContainsKey("CoverAssetId"),"Source URL alone cannot waive required fields");
        var source=new ParsedSource{SourceId="910001",SourceUrl=article.SourceCredit,CanonicalUrl=article.SourceCredit,Kind=ContentKind.Article,Title=article.Title,Summary=article.Summary,BodyHtml=article.BodyHtml,PrimaryCategoryId=75,PublishedDate=new(2026,11,20)};
        await BindEvidence(saved.ContentId,source);
        check((await Requirements(saved.ContentId)).Count==0,"Collected and identity-bound source without author/image is eligible");
        var submitted=await Scope(async s=>await s.GetRequiredService<WorkflowService>().SubmitAsync(saved.ContentId,saved.CurrentRevisionId,await s.GetRequiredService<CurrentPermissionService>().GetAsync("admin"),await Version(saved.ContentId),Guid.NewGuid()));
        check(submitted.ContentId==saved.ContentId,"Optional source fields pass actual submit command");
        await using(var db=new PortalDbContext(options))
        {var c=await db.Contents.SingleAsync(x=>x.Id==saved.ContentId);check(!c.IsPublic&&c.PublishedRevisionId==null,"Submit does not auto-publish source article");}
        var strict=await Save(new(){Kind=ContentKind.Article,Title="Source has fields",Summary="Factual summary",BodyHtml="<p>Actual text</p>",PrimaryCategoryId=1});
        source.SourceId="910002";source.SourceUrl=source.CanonicalUrl="https://qldd.mae.gov.vn/news-910002.htm";source.Author="Official author";source.BodyHtml="<p>Actual text</p><img src='https://media.mae.gov.vn/official.jpg'>";
        await BindEvidence(strict.ContentId,source);missing=await Requirements(strict.ContentId);
        check(missing.ContainsKey("Author")&&missing.ContainsKey("CoverAssetId"),"Official author and official image cannot silently be discarded");
        var noSummary=await Save(new(){Kind=ContentKind.Article,Title="No sapo",BodyHtml="<p>Body</p>",PrimaryCategoryId=1});
        source.SourceId="910003";source.SourceUrl=source.CanonicalUrl="https://qldd.mae.gov.vn/news-910003.htm";source.Author="";source.BodyHtml="<p>Body</p>";
        await BindEvidence(noSummary.ContentId,source);check((await Requirements(noSummary.ContentId)).ContainsKey("Summary"),"Sapo remains required for news");

        var image=await Scope(async s=>(await s.GetRequiredService<MediaService>().UploadAsync(new MemoryStream(fixture.Png),"scan.png","image/png","admin",false,default)).Asset);
        var notice=await Save(new(){Kind=ContentKind.Announcement,Title="Official notice scan",PrimaryCategoryId=1,BodyHtml=$"<img src='/media/{image.Id}'>",Assets=[new(){MediaAssetId=image.Id,Usage="Inline"}]});
        check((await Requirements(notice.ContentId)).Count==0&&image.ScanStatus=="NotScanned","Image-only announcement uses byte-verified image, without fake text or Clean");
        await using(var db=new PortalDbContext(options)){var m=await db.MediaAssets.SingleAsync(x=>x.Id==image.Id);m.IsQuarantined=true;await db.SaveChangesAsync();}
        check((await Requirements(notice.ContentId)).ContainsKey("BodyHtml"),"Quarantined image does not satisfy announcement requirement");
        await using(var db=new PortalDbContext(options)){var m=await db.MediaAssets.SingleAsync(x=>x.Id==image.Id);m.IsQuarantined=false;await db.SaveChangesAsync();}
        await File.AppendAllTextAsync(storage.ResolveMedia(image.StorageKey),"tamper");
        check((await Requirements(notice.ContentId)).ContainsKey("BodyHtml"),"Changed image bytes fail announcement requirement");

        var title="Lịch công tác của Lãnh đạo Cục Quản lý đất đai Tuần 48 năm 2026";
        var calendar=await Save(new(){Kind=ContentKind.StaticPage,Title=title,PrimaryCategoryId=76,BodyHtml="<p>Existing editorial text</p>",Metadata=new(){PublicationDate=new(2026,11,23)}});
        var weekly=new ParsedSource{SourceId="910076",SourceUrl="https://qldd.mae.gov.vn/lich-910076.htm",CanonicalUrl="https://qldd.mae.gov.vn/lich-910076.htm",Kind=ContentKind.StaticPage,PrimaryCategoryId=76,Title=title,BodyHtml="<p>Source calendar</p>",AssetUrls=[initial.Data.Assets.Single(x=>x.Mime=="application/pdf").Url]};
        var data=new SourceSyncCheckpoint{Source=weekly,Title=title,Hash=SourceSyncPolicy.Hash(weekly),Assets=[initial.Data.Assets.Single(x=>x.Mime=="application/pdf")],ReconcileContentIds=[calendar.ContentId],Warnings=["Có lịch CMS cùng tiêu đề/tuần đã nhập trước đây nhưng chưa gắn định danh nguồn. Đối soát bài hiện có; chưa tạo bài trùng."]};
        data.AssetHash=SourceWeeklySchedule.AssetHash(data.Assets);
        var entry=Guid.NewGuid();
        await using(var db=new PortalDbContext(options))
        {db.CrawlRuns.Add(new(){Id=entry,Status=SourceSyncPolicy.RunStatus,Checkpoint=SourceSyncPolicy.Serialize(data)});db.CrawlItems.Add(new(){Id=entry,CrawlRunId=entry,SourceId=weekly.SourceId,SourceUrl=weekly.SourceUrl,Kind=ContentKind.StaticPage,Status="Review"});db.Jobs.Add(new(){Id=entry,Type=SourceSyncPolicy.EntryType,IdempotencyKey=SourceSyncPolicy.Key(weekly.SourceId),Status="Review"});await db.SaveChangesAsync();await new SourceSyncStore(db).ConfigureAsync("admin",null,76,76);}
        var version=await Version(calendar.ContentId);var command=Guid.NewGuid();
        Dictionary<string,string> Form(string? hash=null)=>new(){["contentId"]=calendar.ContentId.ToString(),["hash"]=hash??data.Hash,["assetHash"]=data.AssetHash,["version"]=Convert.ToBase64String(version),["commandId"]=command.ToString()};
        using(var r=await Post($"/admin/source-sync/{entry}/bind-weekly",Form(),false))check(r.StatusCode==HttpStatusCode.BadRequest,"Weekly binding requires CSRF");
        using(var r=await Post($"/admin/source-sync/{entry}/bind-weekly",Form(new string('0',64))))check(r.StatusCode==HttpStatusCode.Conflict,"Stale source hash denied before identity write");
        var stale=Form();stale["version"]=Convert.ToBase64String(new byte[8]);
        using(var r=await Post($"/admin/source-sync/{entry}/bind-weekly",stale))check(r.StatusCode==HttpStatusCode.Conflict,"Stale CMS revision cannot be bound");
        client.DefaultRequestHeaders.Remove("X-Fixture-Actor");client.DefaultRequestHeaders.Add("X-Fixture-Actor","denied");
        using(var r=await Post($"/admin/source-sync/{entry}/bind-weekly",Form()))check(r.StatusCode==HttpStatusCode.Forbidden,"Weekly binding rechecks permissions");
        client.DefaultRequestHeaders.Remove("X-Fixture-Actor");client.DefaultRequestHeaders.Add("X-Fixture-Actor","admin");
        using(var r=await Post($"/admin/source-sync/{entry}/bind-weekly",Form())){if(!r.IsSuccessStatusCode)throw new Exception("Bind HTTP "+r.StatusCode+": "+await r.Content.ReadAsStringAsync());check(true,"Verified year/week binds existing ContentId through HTTP");}
        using(var r=await Post($"/admin/source-sync/{entry}/bind-weekly",Form()))check(r.IsSuccessStatusCode,"Same binding command safely replays");
        await using(var db=new PortalDbContext(options))
        {
            var c=await db.Contents.SingleAsync(x=>x.Id==calendar.ContentId);var cp=await new SourceSyncStore(db).ReadAsync(entry);
            check(c.CurrentRevisionId==calendar.CurrentRevisionId&&!c.IsPublic&&c.State==WorkflowState.Draft&&c.RowVersion.SequenceEqual(version),"Binding preserves content revision, state and rowversion");
            check(cp.Item.Status=="Changed"&&cp.Data.ContentId==c.Id&&await db.SourceRecords.CountAsync(x=>x.ContentId==c.Id)==1,"Missing PDF remains a draft update, source identity is unique");
            check(await db.AuditLogs.CountAsync(x=>x.Action=="SourceSyncWeeklyBind"&&x.ContentId==c.Id)==1,"One binding audit and idempotent command receipt");
        }

        async Task Flow(string who,Func<WorkflowService,Content,ContentActor,Task<WorkflowResult>> act)=>await Scope(async s=>
        {var c=await s.GetRequiredService<PortalDbContext>().Contents.SingleAsync(x=>x.Id==calendar.ContentId);await act(s.GetRequiredService<WorkflowService>(),c,await s.GetRequiredService<CurrentPermissionService>().GetAsync(who));return true;});
        await Flow("admin",(w,c,a)=>w.SubmitAsync(c.Id,c.CurrentRevisionId,a,c.RowVersion,Guid.NewGuid()));
        await Flow("reviewer",(w,c,a)=>w.ApproveAsync(c.Id,c.SubmittedRevisionId!.Value,a,c.RowVersion,Guid.NewGuid()));
        await Flow("publisher",(w,c,a)=>w.PublishApprovedAsync(c.Id,c.ApprovedRevisionId!.Value,a,c.RowVersion,Guid.NewGuid()));
        var update=new Dictionary<string,string>{["contentId"]=calendar.ContentId.ToString(),["hash"]=data.Hash,["assetHash"]=data.AssetHash,["version"]=Convert.ToBase64String(await Version(calendar.ContentId)),["commandId"]=Guid.NewGuid().ToString()};
        using(var r=await Post($"/admin/source-sync/{entry}/update",update))check(r.IsSuccessStatusCode,"Bound existing public calendar accepts missing PDF into an update draft");
        await using(var db=new PortalDbContext(options))
        {
            var c=await db.Contents.SingleAsync(x=>x.Id==calendar.ContentId);var r=await db.Revisions.SingleAsync(x=>x.Id==c.CurrentRevisionId);
            check(c.IsPublic&&c.State==WorkflowState.Draft&&c.PublishedRevisionId==calendar.CurrentRevisionId&&c.CurrentRevisionId!=calendar.CurrentRevisionId,
                "Weekly PDF addition leaves original public revision unchanged");
            check(r.BodyHtml=="<p>Existing editorial text</p>"&&r.Metadata.PublicationDate==new DateOnly(2026,11,23)&&await db.ContentAssets.CountAsync(x=>x.RevisionId==r.Id&&x.Usage=="Attachment")==1,
                "Weekly update preserves editorial body/date and attaches one verified PDF");
        }
        using(var r=await Post($"/admin/source-sync/{entry}/update",update))check(r.IsSuccessStatusCode,"Repeated weekly update command does not attach another PDF");

        // Real captured evidence: no source network and no operational CMS/SQL.
        var proofs=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence,"weekly-years-proof.json")));
        foreach(var p in proofs.RootElement.EnumerateArray())
        {var id=p.GetProperty("sourceId").GetString();var expected=id=="3361"?36:id=="3362"?37:38;check(SourceWeeklyEvidence.HeaderPeriod(p.GetProperty("firstPageText").GetString()!)==(2025,expected),"Verified real PDF header resolves missing year "+id);}
        check(SourceWeeklyEvidence.HeaderPeriod("Cục Quản lý đất đai Lãnh đạo Trung tâm Tuần 36 năm 2025")==null,"Centre calendar not treated as Department calendar");
        check(SourceWeeklySchedule.Period("Tuần 44. Từ ngày 02/11/2026 đến ngày 08/11/2026")==null,"Conflicting week/range is still held for review");
        var pdfBuilder=new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        pdfBuilder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("CUC QUAN LY DAT DAI - LANH DAO CUC - Tuan 36 nam 2025",10,new(15,700),pdfBuilder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica));
        var bytes=pdfBuilder.Build();var sha=Convert.ToHexStringLower(SHA256.HashData(bytes));
        var key="source-sync/resource/"+Guid.NewGuid().ToString("N")+".bin";await File.WriteAllBytesAsync(storage.ResolveSnapshot(key),bytes);
        var pdfUrl="https://qldd.mae.gov.vn/noidung/week36.pdf";
        var missingYear=new ParsedSource{SourceId="910077",SourceUrl="https://qldd.mae.gov.vn/lich-910077.htm",CanonicalUrl="https://qldd.mae.gov.vn/lich-910077.htm",Kind=ContentKind.StaticPage,PrimaryCategoryId=76,Title="Lịch công tác tuần 36 của Lãnh đạo Cục Quản lý đất đai",BodyHtml=$"<a href='{pdfUrl}'>Lịch tuần</a>",AssetUrls=[pdfUrl],PublishedDate=new(2025,9,1)};
        var yearEntry=Guid.NewGuid();var yearData=new SourceSyncCheckpoint{Source=missingYear,Title=missingYear.Title,Hash=SourceSyncPolicy.Hash(missingYear),WeeklyYear=2025,WeeklyWeek=36,Assets=[new(pdfUrl,key,sha,bytes.Length,"application/pdf","week36.pdf")]};
        yearData.AssetHash=SourceWeeklySchedule.AssetHash(yearData.Assets);
        await using(var db=new PortalDbContext(options))
        {db.CrawlRuns.Add(new(){Id=yearEntry,Status=SourceSyncPolicy.RunStatus,Checkpoint=SourceSyncPolicy.Serialize(yearData)});db.CrawlItems.Add(new(){Id=yearEntry,CrawlRunId=yearEntry,SourceId=missingYear.SourceId,SourceUrl=missingYear.SourceUrl,Kind=ContentKind.StaticPage,Status="Ready"});db.Jobs.Add(new(){Id=yearEntry,Type=SourceSyncPolicy.EntryType,IdempotencyKey=SourceSyncPolicy.Key(missingYear.SourceId),Status="Ready"});await db.SaveChangesAsync();}
        using(var r=await Post($"/admin/source-sync/{yearEntry}/draft",new(){["hash"]=yearData.Hash,["assetHash"]=yearData.AssetHash}))check(r.IsSuccessStatusCode,"Missing title year transfers through HTTP using byte-bound PDF header evidence");
        await using(var db=new PortalDbContext(options))
        {
            var cp=await new SourceSyncStore(db).ReadAsync(yearEntry);var c=await db.Contents.SingleAsync(x=>x.Id==cp.Data.ContentId);var r=await db.Revisions.SingleAsync(x=>x.Id==c.CurrentRevisionId);
            check(c.State==WorkflowState.Draft&&!c.IsPublic&&r.Title==missingYear.Title&&r.Metadata.PublicationDate==missingYear.PublishedDate&&await db.ContentAssets.CountAsync(x=>x.RevisionId==r.Id&&x.Usage=="Attachment")==1,
                "Year evidence never rewrites source title/date or publishes the new calendar");
        }
        await SourceQueueChecks.Run(services,options,fixture,check);
    }
}
