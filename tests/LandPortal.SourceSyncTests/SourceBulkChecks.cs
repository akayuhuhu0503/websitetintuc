using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

static class SourceBulkChecks
{
    public static async Task Run(HttpClient client,string token,DbContextOptions<PortalDbContext> options,SourceSyncCheckpoint template,string run,Action<bool,string> check)
    {
        async Task<SourceBulkSelection> Seed(int number,string? title=null,bool missingAuthor=false,bool weekly=false,bool emptySummary=false)
        {
            await using var db=new PortalDbContext(options);
            var data=SourceSyncPolicy.Read<SourceSyncCheckpoint>(SourceSyncPolicy.Serialize(template));
            var source=data.Source!;source.Title=title??"Bulk fixture "+number;source.SourceId=number.ToString();
            source.SourceUrl=source.CanonicalUrl="https://qldd.mae.gov.vn/fixture-"+number+".htm";
            if(missingAuthor)source.Author="";
            if(emptySummary){source.Summary="";source.BodyHtml="";}
            if(weekly)
            {
                source.Kind=ContentKind.StaticPage;source.PrimaryCategoryId=76;source.CategoryIds=[76];
                data.Assets=data.Assets.Where(x=>x.Mime=="application/pdf").ToList();
                source.AssetUrls=data.Assets.Select(x=>x.Url).ToList();source.BodyHtml="<p></p>";
                data.WeeklyYear=2026;data.WeeklyWeek=48;data.WeeklyReviewRequired=false;data.ReconcileContentIds=[];
            }
            for(var i=0;i<data.Assets.Count;i++)
            {
                var asset=data.Assets[i];if(asset.Mime!="application/pdf")continue;
                var pdf=new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
                pdf.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("Bulk document "+number,12,new(20,700),pdf.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica));
                var bytes=pdf.Build();var hash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
                var key="source-sync/resource/"+hash+".pdf";var path=Path.Combine(run,"snapshots",key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,bytes);
                data.Assets[i]=asset with{Key=key,Sha256=hash,Bytes=bytes.Length};
            }
            data.Hash=SourceSyncPolicy.Hash(source);data.Title=source.Title;data.ContentId=null;data.AssetHash=SourceWeeklySchedule.AssetHash(data.Assets);
            var id=Guid.NewGuid();var item=new CrawlItem{Id=id,CrawlRunId=id,SourceId=source.SourceId,SourceUrl=source.CanonicalUrl,Kind=source.Kind,Status="Ready"};
            db.CrawlRuns.Add(new(){Id=id,Status=SourceSyncPolicy.RunStatus,Checkpoint=SourceSyncPolicy.Serialize(data)});db.CrawlItems.Add(item);
            db.Jobs.Add(new(){Id=id,Type=SourceSyncPolicy.EntryType,IdempotencyKey=SourceSyncPolicy.Key(source.SourceId),Status="Ready"});await db.SaveChangesAsync();
            return new(id,SourceSyncBulk.Stamp(item,data));
        }
        async Task<HttpResponseMessage> Post(string path,object value,bool csrf=true)
        {var message=new HttpRequestMessage(HttpMethod.Post,"/admin/source-sync/bulk/"+path){Content=JsonContent.Create(value)};if(csrf)message.Headers.Add("X-CSRF-TOKEN",token);return await client.SendAsync(message);}
        async Task<JsonElement> Execute(SourceBulkSelection item,string action,Guid request)
        {using var r=await Post("execute",new{action,item,requestId=request});var text=await r.Content.ReadAsStringAsync();check(r.IsSuccessStatusCode,"HTTP source bulk execute "+action+" "+r.StatusCode+" "+text);return JsonDocument.Parse(text).RootElement.Clone();}
        var first=await Seed(910001);
        using(var r=await Post("preview",new{action="PublishDirect",items=new[]{first}}))check(r.StatusCode==HttpStatusCode.Forbidden,"Direct publish denied without PublishDirect");
        await using(var db=new PortalDbContext(options)){db.UserClaims.Add(new(){UserId="admin",ClaimType="permission",ClaimValue=Permissions.PublishDirect});await db.SaveChangesAsync();}
        using(var r=await Post("preview",new{action="Draft",items=new[]{first}},false))check(r.StatusCode==HttpStatusCode.BadRequest,"Source bulk CSRF required");
        using(var r=await Post("preview",new{action="Draft",items=Enumerable.Repeat(first,101)}))check(r.StatusCode==HttpStatusCode.BadRequest,"Selection limit 100 enforced");
        using(var r=await Post("preview",new{action="Draft",items=new[]{first,first}}))check(r.StatusCode==HttpStatusCode.BadRequest,"Duplicate selection refused");
        using(var r=await Post("preview",new{action="Draft",items=new[]{first with{Stamp=new string('a',64)}}}))
        {var json=JsonDocument.Parse(await r.Content.ReadAsStringAsync());check(!json.RootElement.GetProperty("items")[0].GetProperty("eligible").GetBoolean(),"Stale source stamp excluded");}
        var request=Guid.NewGuid();var published=await Execute(first,"PublishDirect",request);var cid=published.GetProperty("editUrl").GetString()!.Split('/').Last();
        check(published.GetProperty("outcome").GetString()=="Published","New source draft and exact revision published");
        var repeated=await Execute(first,"PublishDirect",request);check(repeated.GetProperty("outcome").GetString()=="Published","Lost-response replay does not create or publish twice");
        using(var r=await Post("preview",new{action="PublishDirect",items=new[]{first with{RequestId=request}}}))
        {var json=JsonDocument.Parse(await r.Content.ReadAsStringAsync());check(json.RootElement.GetProperty("items")[0].GetProperty("eligible").GetBoolean(),"Lost-response preview permits bound command reconciliation");}
        await using(var db=new PortalDbContext(options))check(await db.SourceRecords.CountAsync(x=>x.SourceId=="910001")==1&&await db.ContentCommands.CountAsync(x=>x.Id==SourceSyncBulk.PublishCommand(request))==1,"One source identity and one publication receipt");
        using(var r=await Post("execute",new{action="PublishDirect",item=first,requestId=Guid.NewGuid()}))check(r.StatusCode==HttpStatusCode.Conflict,"Different request cannot republish existing source");
        await using(var db=new PortalDbContext(options)){var c=await db.Contents.SingleAsync(x=>x.Id==Guid.Parse(cid));c.IsPublic=false;await db.SaveChangesAsync();}
        check((await Execute(first,"PublishDirect",request)).GetProperty("outcome").GetString()=="AlreadyProcessed","Publication receipt never republishes withdrawn content");
        var dup=await Seed(910002,"Bulk fixture 910001");using(var r=await Post("preview",new{action="Draft",items=new[]{dup}}))
        {var json=JsonDocument.Parse(await r.Content.ReadAsStringAsync());check(!json.RootElement.GetProperty("items")[0].GetProperty("eligible").GetBoolean(),"Manual/existing title duplicate held without SourceId match");}
        var partial=await Seed(910003,missingAuthor:true);var partialRequest=Guid.NewGuid();
        await using(var db=new PortalDbContext(options))
        {
            var store=new SourceSyncStore(db);var settings=await store.SettingsAsync();settings.Cycle=new(){Categories=[75],WaitingForCapacity=true};settings.ConsecutiveFailures=0;await store.WriteSettingsAsync(settings);
            var job=await db.Jobs.SingleAsync(x=>x.IdempotencyKey==SourceSyncPolicy.SweepKey);job.Status="Requested";job.AvailableUtc=DateTimeOffset.UtcNow.AddMinutes(5);await db.SaveChangesAsync();
        }
        var held=await Execute(partial,"PublishDirect",partialRequest);
        check(held.GetProperty("outcome").GetString()=="DraftedWithError"&&held.GetProperty("editUrl").GetString()!=null,"Publication failure retains one draft and link");
        await using(var db=new PortalDbContext(options))
        {
            var settings=await new SourceSyncStore(db).SettingsAsync();var job=await db.Jobs.SingleAsync(x=>x.IdempotencyKey==SourceSyncPolicy.SweepKey);
            check(settings.Cycle is {WaitingForCapacity:false}&&job.AvailableUtc<DateTimeOffset.UtcNow.AddSeconds(30),"Committed draft wakes capacity-blocked cycle even when publication is held");
        }
        await Execute(partial,"PublishDirect",partialRequest);
        await using(var db=new PortalDbContext(options))check(await db.SourceRecords.CountAsync(x=>x.SourceId=="910003")==1,"Partial retry never creates duplicate draft");
        var ok=await Seed(910004);check((await Execute(ok,"Draft",Guid.NewGuid())).GetProperty("outcome").GetString()=="Drafted","Independent next item succeeds after failed publication");
        var ignored=await Seed(910005);var ignoreRequest=Guid.NewGuid();await Execute(ignored,"Ignore",ignoreRequest);await Execute(ignored,"Ignore",ignoreRequest);
        await using(var db=new PortalDbContext(options))check(await db.AuditLogs.CountAsync(x=>x.Action=="SourceSync.BulkIgnore"&&x.Detail.StartsWith(ignoreRequest.ToString()))==1,"Ignore replay preserves one audit");
        // Race after draft creation: a scheduled/editing article must remain untouched on retry.
        var heldId=Guid.Parse(held.GetProperty("editUrl").GetString()!.Split('/').Last());
        await using(var db=new PortalDbContext(options)){var c=await db.Contents.SingleAsync(x=>x.Id==heldId);c.EditGeneration++;await db.SaveChangesAsync();}
        var changed=await Execute(partial,"PublishDirect",partialRequest);check(changed.GetProperty("message").GetString()!.Contains("chỉnh sửa"),"Edited working draft protected even without a new revision");
        var scheduled=await Seed(910006,missingAuthor:true);var scheduledRequest=Guid.NewGuid();var sh=await Execute(scheduled,"PublishDirect",scheduledRequest);
        var scheduledId=Guid.Parse(sh.GetProperty("editUrl").GetString()!.Split('/').Last());
        await using(var db=new PortalDbContext(options)){var c=await db.Contents.SingleAsync(x=>x.Id==scheduledId);db.PublicationJobs.Add(new(){ContentId=c.Id,RevisionId=c.CurrentRevisionId!.Value,RevisionHash="fixture",RequestedByUserId="admin",Direct=true,DueUtc=DateTimeOffset.UtcNow.AddDays(1),State=PublicationJobState.Pending});await db.SaveChangesAsync();}
        var scheduleResult=await Execute(scheduled,"PublishDirect",scheduledRequest);
        check(scheduleResult.GetProperty("outcome").GetString()=="DraftedWithError"&&scheduleResult.GetProperty("message").GetString()!.Contains("hẹn"),"Scheduled draft explicitly protected on retry");
        await using(var db=new PortalDbContext(options))check(await db.PublicationJobs.AnyAsync(x=>x.ContentId==scheduledId&&x.IsActive&&x.State==PublicationJobState.Pending)&&!await db.Contents.AnyAsync(x=>x.Id==scheduledId&&x.IsPublic),"Active schedule and unpublished state preserved");
        await using(var db=new PortalDbContext(options))
        {
            var store=new SourceSyncStore(db);var settings=await store.SettingsAsync();settings.Cycle=new(){Categories=[75],WaitingForCapacity=true};await store.WriteSettingsAsync(settings);
            var job=await db.Jobs.SingleAsync(x=>x.IdempotencyKey==SourceSyncPolicy.SweepKey);job.Status="Requested";job.AvailableUtc=DateTimeOffset.UtcNow.AddMinutes(5);await db.SaveChangesAsync();
            await store.ResumeAfterCapacityAsync();var resumed=await db.Jobs.AsNoTracking().SingleAsync(x=>x.IdempotencyKey==SourceSyncPolicy.SweepKey);
            check(resumed.AvailableUtc<DateTimeOffset.UtcNow.AddSeconds(30)&&!(await store.SettingsAsync()).Cycle!.WaitingForCapacity,"Capacity release automatically wakes unfinished cycle");
            settings=await store.SettingsAsync();settings.Cycle!.WaitingForCapacity=true;settings.ConsecutiveFailures=1;await store.WriteSettingsAsync(settings);job=await db.Jobs.SingleAsync(x=>x.Id==resumed.Id);job.Status="Backoff";job.AvailableUtc=DateTimeOffset.UtcNow.AddMinutes(15);await db.SaveChangesAsync();await store.ResumeAfterCapacityAsync();
            check((await store.SettingsAsync()).Cycle!.WaitingForCapacity,"Capacity wake never bypasses network backoff");
        }
        var filters=await client.GetStringAsync("/admin/source-sync?sourceCategory=75&from=2026-11-14&to=2026-11-14");check(filters.Contains("Bulk fixture")&&filters.Contains("source-bulk-page"),"SQL source category/date filters and selectable rows render");
        check(!(await client.GetStringAsync("/admin/source-sync?from=2026-12-01")).Contains("Bulk fixture"),"Date filter excludes older source dates");
        for(var n=0;n<24;n++)await Seed(920000+n);
        check((await client.GetStringAsync("/admin/source-sync?page=2")).Contains("source-bulk-row"),"Second selection page renders");
        using(var r=await Post("preview",new{action="PublishDirect",items=new[]{await Seed(930001,emptySummary:true)}}))
        {var json=JsonDocument.Parse(await r.Content.ReadAsStringAsync());check(!json.RootElement.GetProperty("items")[0].GetProperty("eligible").GetBoolean(),"News article without source sapo remains held");}
        using(var requestMessage=new HttpRequestMessage(HttpMethod.Post,"/admin/source-sync/configure")
            {Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"sourceCategory","76"},{"targetCategory","76"}})})
        {requestMessage.Headers.Add("X-CSRF-TOKEN",token);using var r=await client.SendAsync(requestMessage);check(r.IsSuccessStatusCode,"Weekly category mapping uses existing CMS command");}
        var weekly=await Seed(930002,"Lịch công tác tuần 48 năm 2026 của Lãnh đạo Cục Quản lý đất đai",weekly:true,emptySummary:true);
        using(var r=await Post("preview",new{action="PublishDirect",items=new[]{weekly}}))
        {var json=JsonDocument.Parse(await r.Content.ReadAsStringAsync());check(json.RootElement.GetProperty("items")[0].GetProperty("eligible").GetBoolean(),"Verified PDF weekly notice does not invent a source sapo requirement");}
        var weeklyResult=await Execute(weekly,"PublishDirect",Guid.NewGuid());
        check(weeklyResult.GetProperty("outcome").GetString()=="Published","PDF-only weekly notice publishes through unchanged workflow");
        await using(var db=new PortalDbContext(options))
        {
            var weeklyId=Guid.Parse(weeklyResult.GetProperty("editUrl").GetString()!.Split('/').Last());
            var c=await db.Contents.SingleAsync(x=>x.Id==weeklyId);var revision=await db.Revisions.SingleAsync(x=>x.Id==c.PublishedRevisionId);
            check(revision.Summary==""&&c.Kind==ContentKind.StaticPage&&c.PrimaryCategoryId==76&&
                await db.ContentAssets.CountAsync(x=>x.RevisionId==revision.Id&&x.Usage=="Attachment")==1,"Weekly publication preserves empty source sapo and verified PDF attachment");
        }
        await File.WriteAllTextAsync(Path.Combine(run,"bulk-public.json"),JsonSerializer.Serialize(new{contentId=cid}));
    }
}
