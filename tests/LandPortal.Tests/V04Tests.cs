using System.Security.Claims;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LandPortal.Tests;
public static class V04Tests
{
    private static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static async Task Denied<T>(Func<Task> action) where T:Exception
    {try{await action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(DbContextOptions<PortalDbContext> options,IConfiguration config)
    {
        PortalDbContext Db()=>new(options);
        WorkflowService Flow(PortalDbContext db)=>new(db,new HtmlPolicy(),MediaUploadOptions.FromConfiguration(config,"Development"),StorageOptions.FromConfiguration(config));
        ContentActor Actor()=>new("qa-v04-"+Guid.NewGuid(),Permissions.All.ToHashSet());
        DraftInput Input(int primary=75,int[]? secondary=null)=>new(){Title="QA V04 "+Guid.NewGuid(),BodyHtml="<p>Kiểm thử riêng SQL Test.</p>",PrimaryCategoryId=primary,SecondaryCategoryIds=secondary?.ToList()??[]};
        yield return("V04 category primary AND all secondary scope, read/edit/clone/replay denied after scope change",async()=>
        {
            await using var db=Db();var global=Actor();var scoped=global with{CategoryIds=new HashSet<int>{75}};var f=Flow(db);
            await Denied<UnauthorizedAccessException>(()=>f.SaveAsync(null,Input(75,[82]),scoped,null,Guid.NewGuid()));
            await Denied<UnauthorizedAccessException>(()=>f.SaveAsync(null,Input(82),scoped,null,Guid.NewGuid()));
            var input=Input(75,[82]);var command=Guid.NewGuid();var saved=await f.SaveAsync(null,input,global,null,command);
            var permissions=new CurrentPermissionService(db);var content=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==saved.ContentId);
            Assert(!await permissions.CanReadAsync(scoped,content)&&!await permissions.CanEditAsync(scoped,content),"Secondary category escaped server scope.");
            await Denied<UnauthorizedAccessException>(()=>f.SaveAsync(saved.ContentId,Input(),scoped,saved.RowVersion,Guid.NewGuid()));
            await Denied<UnauthorizedAccessException>(()=>f.CloneAsync(saved.ContentId,saved.CurrentRevisionId,scoped,Guid.NewGuid()));
            await Denied<UnauthorizedAccessException>(()=>f.SaveAsync(null,input,scoped,null,command));
            Assert(await permissions.CanReadAsync(global with{CategoryIds=new HashSet<int>{75,82}},content),"Complete assigned scope was denied.");
        });
        yield return("V04 scope assignment uses SQL Identity concurrency, revokes session, rejects self and expansion",async()=>
        {
            using var provider=Phase2bAccountTests.Provider(options);var admin=await Phase2bAccountTests.Fixture(provider,Permissions.All);var writer=await Phase2bAccountTests.Fixture(provider,[Permissions.EditAll]);
            string stamp,security,password;using(var scope=provider.CreateScope()){var u=await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Users.AsNoTracking().SingleAsync(x=>x.Id==writer.UserId);stamp=u.ConcurrencyStamp!;security=u.SecurityStamp!;password=u.PasswordHash!;}
            using(var scope=provider.CreateScope())await scope.ServiceProvider.GetRequiredService<AccountAdministrationService>().SetCategoryScopeAsync(admin.UserId,writer.UserId,stamp,false,[75]);
            using(var scope=provider.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<PortalDbContext>();var service=scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
                var actual=await scope.ServiceProvider.GetRequiredService<CurrentPermissionService>().GetAsync(writer.UserId);var user=await db.Users.AsNoTracking().SingleAsync(x=>x.Id==writer.UserId);
                Assert(actual.CategoryIds!.SetEquals([75])&&user.SecurityStamp!=security&&user.PasswordHash==password,"Scope/stamp/password invariant failed.");
                await Denied<DbUpdateConcurrencyException>(()=>service.SetCategoryScopeAsync(admin.UserId,writer.UserId,stamp,false,[82]));
                var own=await db.Users.AsNoTracking().SingleAsync(x=>x.Id==admin.UserId);
                await Denied<UnauthorizedAccessException>(()=>service.SetCategoryScopeAsync(admin.UserId,admin.UserId,own.ConcurrencyStamp!,false,[75]));
            }
            using(var scope=provider.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<PortalDbContext>();db.UserClaims.Add(new(){UserId=admin.UserId,ClaimType="content.category.scope",ClaimValue="restricted"});db.UserClaims.Add(new(){UserId=admin.UserId,ClaimType="content.category",ClaimValue="75"});await db.SaveChangesAsync();
                var target=await db.Users.AsNoTracking().SingleAsync(x=>x.Id==writer.UserId);var service=scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
                await Denied<UnauthorizedAccessException>(()=>service.SetCategoryScopeAsync(admin.UserId,writer.UserId,target.ConcurrencyStamp!,false,[75,82]));
                await Denied<UnauthorizedAccessException>(()=>service.SetCategoryScopeAsync(admin.UserId,writer.UserId,target.ConcurrencyStamp!,true,[]));
            }
        });
        yield return("V04 historical revisions and private media cannot bypass finite scope with EditAll",async()=>
        {
            await using var db=Db();var actor=Actor();var f=Flow(db);var saved=await f.SaveAsync(null,Input(82),actor,null,Guid.NewGuid());
            var newDraft=await f.SaveAsync(saved.ContentId,Input(75),actor,saved.RowVersion,Guid.NewGuid());var limited=actor with{CategoryIds=new HashSet<int>{75}};
            Assert(!await new CurrentPermissionService(db).ScopeRevisions(db.Revisions.AsNoTracking(),limited).AnyAsync(x=>x.Id==saved.CurrentRevisionId),"History revealed old out-of-scope revision.");
            await Denied<UnauthorizedAccessException>(()=>f.RestoreRevisionAsync(saved.ContentId,saved.CurrentRevisionId!.Value,limited,newDraft.RowVersion,Guid.NewGuid()));
            var sourcePdf=await db.MediaAssets.AsNoTracking().FirstAsync(x=>x.SourceUrl!=""&&x.MimeType=="application/pdf");var storage=StorageOptions.FromConfiguration(config);var bytes=(await File.ReadAllBytesAsync(storage.ResolveMedia(sourcePdf.StorageKey))).Concat(System.Text.Encoding.UTF8.GetBytes("\n% QA V04 "+Guid.NewGuid())).ToArray();
            var key="qa-v04/"+Guid.NewGuid()+".pdf";var path=storage.ResolveMedia(key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,bytes);
            var privateAsset=new MediaAsset{OriginalName="private-other-scope.pdf",Sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),StorageKey=key,Origin="Uploaded",UploadedByUserId="qa-other",MimeType="application/pdf",ValidationStatus="Validated",ScanStatus="NotScanned",ByteLength=bytes.Length};db.MediaAssets.Add(privateAsset);await db.SaveChangesAsync();
            var current=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==saved.ContentId);var bad=Input();bad.Assets=[new(){MediaAssetId=privateAsset.Id,Usage="Attachment"}];
            await Denied<UnauthorizedAccessException>(()=>f.SaveAsync(current.Id,bad,limited,current.RowVersion,Guid.NewGuid()));
        });
        yield return("V04 scheduled requester AND reviewer scopes rechecked at execution",async()=>
        {
            foreach(var restrictReviewer in new[]{false,true})
            {
                using var provider=Phase2bAccountTests.Provider(options);var requester=await Phase2bAccountTests.Fixture(provider,Permissions.All);var reviewer=await Phase2bAccountTests.Fixture(provider,Permissions.All);
                await using var db=Db();var rights=new CurrentPermissionService(db);var actor=await rights.GetAsync(requester.UserId);var approver=await rights.GetAsync(reviewer.UserId);var f=Flow(db);
                var saved=await f.SaveAsync(null,Input(75,[82]),actor,null,Guid.NewGuid());var submitted=await f.SubmitAsync(saved.ContentId,saved.CurrentRevisionId,actor,saved.RowVersion,Guid.NewGuid());var approved=await f.ApproveAsync(saved.ContentId,submitted.CurrentRevisionId!.Value,approver,submitted.RowVersion,Guid.NewGuid());
                var scheduler=new SchedulerService(db,f,rights);var job=await scheduler.ScheduleAsync(saved.ContentId,approved.CurrentRevisionId!.Value,DateTimeOffset.UtcNow.AddSeconds(1),false,actor,approved.RowVersion);
                var target=restrictReviewer?reviewer.UserId:requester.UserId;db.UserClaims.Add(new(){UserId=target,ClaimType="content.category.scope",ClaimValue="restricted"});db.UserClaims.Add(new(){UserId=target,ClaimType="content.category",ClaimValue="75"});await db.SaveChangesAsync();await Task.Delay(1100);
                var lease=await scheduler.ClaimAsync("qa-v04-scope",onlyJobId:job.Id);Assert(lease is not null,"Scoped fixture lease missing.");var result=await scheduler.ExecuteAsync(lease!);Assert(result.State=="NeedsReview","Revoked secondary scope still published.");
                Assert(!await db.Contents.AsNoTracking().AnyAsync(x=>x.Id==saved.ContentId&&x.IsPublic),"Denied job changed public state.");
            }
        });
        yield return("V04 Vietnam legal expiry requires confirmed evidence AND explicit boundary, Unknown stays Unknown",()=>
        {
            var metadata=new ContentMetadata{EffectivenessStatus="ConfirmedEffective",EffectiveUntilDate=new(2026,10,14),EffectivenessEvidence="QA confirmed source",EffectivenessBoundary="EndOfVietnamDay"};
            Assert(metadata.EffectiveStatusAt(new(2026,10,14,16,59,59,TimeSpan.Zero))=="ConfirmedEffective","End-day expired early.");Assert(metadata.EffectiveStatusAt(new(2026,10,14,17,0,0,TimeSpan.Zero))=="ConfirmedExpired","Vietnam end-day boundary wrong.");
            metadata.EffectivenessBoundary="StartOfVietnamDay";Assert(metadata.EffectiveStatusAt(new(2026,10,13,17,0,0,TimeSpan.Zero))=="ConfirmedExpired","Vietnam start-day boundary wrong.");metadata.EffectivenessBoundary=null;Assert(metadata.EffectiveStatusAt(DateTimeOffset.MaxValue.ToOffset(TimeSpan.Zero).AddDays(-1))=="ConfirmedEffective","Unconfirmed boundary guessed.");
            metadata.EffectivenessBoundary="StartOfVietnamDay";metadata.EffectivenessStatus="Unknown";Assert(metadata.EffectiveStatusAt(new(2027,1,1,0,0,0,TimeSpan.Zero))=="Unknown","Unknown status guessed.");
            Assert(!JsonSerializer.Serialize(new ContentMetadata()).Contains("EffectiveUntilDate")&&!JsonSerializer.Serialize(new ContentMetadata()).Contains("EffectivenessBoundary"),"Nullable additions changed old revision hash payload.");return Task.CompletedTask;
        });
        yield return("V04 legal expiry is immutable revision metadata and draft cannot change public filter or approval",async()=>
        {
            await using var db=Db();var f=Flow(db);var actor=Actor();var input=Input();input.Kind=ContentKind.LegalDocument;input.Metadata=new(){Number="QA/V04/"+Guid.NewGuid(),EffectivenessStatus="ConfirmedEffective",EffectivenessEvidence="QA documented rule",EffectiveUntilDate=new(2020,1,1),EffectivenessBoundary="EndOfVietnamDay"};
            var saved=await f.SaveAsync(null,input,actor,null,Guid.NewGuid());var live=await f.PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());
            var publicQuery=new PublicContentQuery(db);Assert(await publicQuery.Published().AnyAsync(x=>x.Id==saved.ContentId&&x.EffectivenessStatus=="ConfirmedExpired"),"SQL did not use published confirmed expiry.");
            var edit=await f.BeginEditAsync(saved.ContentId,actor,live.RowVersion,Guid.NewGuid());input.Metadata.EffectiveUntilDate=new(2099,1,1);var changed=await f.SaveAsync(saved.ContentId,input,actor,edit.RowVersion,Guid.NewGuid());
            Assert(changed.CurrentRevisionId!=live.PublishedRevisionId&&await publicQuery.Published().AnyAsync(x=>x.Id==saved.ContentId&&x.EffectivenessStatus=="ConfirmedExpired"),"Draft expiry leaked or hash reused.");
            var sub=await f.SubmitAsync(saved.ContentId,changed.CurrentRevisionId,actor,changed.RowVersion,Guid.NewGuid());var app=await f.ApproveAsync(saved.ContentId,sub.CurrentRevisionId!.Value,actor,sub.RowVersion,Guid.NewGuid());var opened=await f.BeginEditAsync(saved.ContentId,actor,app.RowVersion,Guid.NewGuid());input.Metadata.EffectivenessBoundary="StartOfVietnamDay";var newer=await f.SaveAsync(saved.ContentId,input,actor,opened.RowVersion,Guid.NewGuid());await Denied<DbUpdateConcurrencyException>(()=>f.PublishApprovedAsync(saved.ContentId,app.CurrentRevisionId!.Value,actor,newer.RowVersion,Guid.NewGuid()));
            await f.UnpublishAsync(saved.ContentId,actor,newer.RowVersion,Guid.NewGuid());
            foreach(var bad in new[]{new ContentMetadata{EffectiveUntilDate=new(2027,1,1)},new ContentMetadata{EffectivenessBoundary="Guessed"},new ContentMetadata{EffectiveDate=new(2028,1,1),EffectiveUntilDate=new(2027,1,1),EffectivenessEvidence="QA"}})
            {input.Metadata=bad;await Denied<InvalidOperationException>(()=>f.SaveAsync(null,input,actor,null,Guid.NewGuid()));}
        });
        yield return("V04 public exact slash identifier outranks newer title match, stable SQL paging excludes draft",async()=>
        {
            var marker="qa-v04-"+Guid.NewGuid().ToString("N")+"/2026";await using var db=Db();var f=Flow(db);var actor=Actor();var exactInput=Input();exactInput.Kind=ContentKind.LegalDocument;exactInput.Metadata.Number=marker;
            var exact=await f.SaveAsync(null,exactInput,actor,null,Guid.NewGuid());var a=await f.PublishDirectAsync(exact.ContentId,exact.CurrentRevisionId!.Value,actor,exact.RowVersion,Guid.NewGuid());var other=Input();other.Title=marker;other.Metadata.PublicationDate=new(2090,1,1);var title=await f.SaveAsync(null,other,actor,null,Guid.NewGuid());var b=await f.PublishDirectAsync(title.ContentId,title.CurrentRevisionId!.Value,actor,title.RowVersion,Guid.NewGuid());var draft=await f.SaveAsync(null,other,actor,null,Guid.NewGuid());
            var q=new PublicContentQuery(db);var ordered=PublicContentQuery.Relevant(q.Filter(q.Published(),marker),marker);var first=await ordered.Take(1).Select(x=>x.Id).SingleAsync();var next=await ordered.Skip(1).Take(1).Select(x=>x.Id).SingleAsync();
            Assert(first==exact.ContentId&&next==title.ContentId&&await ordered.CountAsync()==2,"Exact-number ranking/draft guard failed.");Assert(ordered.Skip(1).Take(1).ToQueryString().Contains("OFFSET"),"Relevance paging left SQL.");
            Assert(PublicContentQuery.Card(await q.Published().SingleAsync(x=>x.Id==exact.ContentId)).Url=="/bai-viet/"+exact.ContentId,"Public URL changed with title.");
            await f.UnpublishAsync(exact.ContentId,actor,a.RowVersion,Guid.NewGuid());await f.UnpublishAsync(title.ContentId,actor,b.RowVersion,Guid.NewGuid());
        });
        yield return("V04 PDF-only announcement requires neither body image nor cover",async()=>
        {
            await using var db=Db();var pdf=await db.MediaAssets.AsNoTracking().FirstAsync(x=>x.SourceUrl!=""&&x.MimeType=="application/pdf");var actor=Actor();var f=Flow(db);var input=Input();input.Kind=ContentKind.Announcement;input.BodyHtml="";input.Assets=[new(){MediaAssetId=pdf.Id,Usage="Attachment",Caption="PDF nguồn thật dùng trong Test"}];var saved=await f.SaveAsync(null,input,actor,null,Guid.NewGuid());var live=await f.PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());Assert(await db.Revisions.AnyAsync(x=>x.Id==live.PublishedRevisionId&&x.BodyHtml==""&&x.CoverAssetId==null),"PDF-only announcement gained fake content.");await f.UnpublishAsync(saved.ContentId,actor,live.RowVersion,Guid.NewGuid());
        });
        yield return("V04 idle30min expires at boundary and editor GET polling never renews",()=>
        {
            var now=DateTimeOffset.UtcNow;var properties=new AuthenticationProperties();AdminSessionPolicy.Touch(properties,now,TimeSpan.FromMinutes(30));Assert(!AdminSessionPolicy.Expired(properties,now.AddMinutes(29),TimeSpan.FromMinutes(30))&&AdminSessionPolicy.Expired(properties,now.AddMinutes(30),TimeSpan.FromMinutes(30)),"Idle boundary incorrect.");var http=new DefaultHttpContext();http.Request.Method="GET";http.Request.Path="/admin/session";Assert(!AdminSessionPolicy.IsUserActivity(http.Request),"Session poll extends idle.");http.Request.Path="/admin/media";Assert(!AdminSessionPolicy.IsUserActivity(http.Request),"Media poll extends idle.");http.Request.Method="POST";http.Request.Path="/admin/save";Assert(AdminSessionPolicy.IsUserActivity(http.Request),"Actual save not activity.");return Task.CompletedTask;
        });
    }
}
