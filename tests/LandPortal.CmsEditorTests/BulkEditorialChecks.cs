using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

internal static class BulkEditorialChecks
{
    public static async Task Run(DbContextOptions<PortalDbContext> options, HttpClient admin, HttpClient writer, HttpClient reviewer,
        HttpClient publisher, HttpClient outsider, string adminId, string writerId, Func<HttpClient,Task<string>> token, Action<bool,string> check,string run)
    {
        await using(var db=new PortalDbContext(options))
        {
            if(!db.Database.GetDbConnection().DataSource.StartsWith("(localdb)\\LPCms_") || db.Database.GetDbConnection().Database!="LandPortal_Test")throw new Exception("Isolated SQL only");
            db.UserClaims.Add(new(){UserId=adminId,ClaimType="permission",ClaimValue=Permissions.PublishDirect}); await db.SaveChangesAsync();
        }
        async Task<WorkflowResult> Create(bool pending=false)
        {
            await using var db=new PortalDbContext(options);var a=await new CurrentPermissionService(db).GetAsync(writerId);var flow=new WorkflowService(db,new HtmlPolicy());
            var r=await flow.SaveAsync(null,new DraftInput{Kind=ContentKind.Announcement,Title="BULK TEST "+Guid.NewGuid().ToString("N"),BodyHtml="<p>Isolated bulk workflow fixture.</p>",PrimaryCategoryId=1},a,null,Guid.NewGuid());
            return pending?await flow.SubmitAsync(r.ContentId,r.CurrentRevisionId,a,r.RowVersion,Guid.NewGuid()):r;
        }
        object Item(WorkflowResult r)=>new{id=r.ContentId,revisionId=r.CurrentRevisionId,rowVersion=Convert.ToBase64String(r.RowVersion)};
        async Task<(HttpStatusCode Code,JsonElement Body)> Post(HttpClient c,string path,object data,bool csrf=true)
        {
            using var req=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(data)};if(csrf)req.Headers.Add("X-CSRF-TOKEN",await token(c));
            using var response=await c.SendAsync(req);var b=await response.Content.ReadAsStringAsync();JsonElement json=default;try{json=JsonDocument.Parse(b).RootElement.Clone();}catch(JsonException){}
            return(response.StatusCode,json);
        }
        Task<(HttpStatusCode Code,JsonElement Body)> Execute(HttpClient c,string action,WorkflowResult r,Guid command)=>Post(c,"/admin/bulk/execute",new{action,item=Item(r),requestId=command});
        async Task<WorkflowResult> Current(Guid id){await using var db=new PortalDbContext(options);var c=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==id);return new(c.Id,c.CurrentRevisionId,c.PublishedRevisionId,c.RowVersion,c.State,c.IsPublic,c.IsTrashed,false,c.CreatedUtc);}
        var draft=await Create();var pending=await Create(true);var stale=await Create(true);var broken=await Create(true);var later=await Create(true);
        for(var i=0;i<12;i++)await Create();
        await File.WriteAllTextAsync(Path.Combine(run,"bulk-page1.html"),await admin.GetStringAsync("/admin"));
        await File.WriteAllTextAsync(Path.Combine(run,"bulk-page2.html"),await admin.GetStringAsync("/admin?page=2"));
        var preview=await Post(admin,"/admin/bulk/preview",new{action="ApproveAndPublish",items=new[]{Item(draft),Item(pending)}});
        check(preview.Code==HttpStatusCode.OK && preview.Body.GetProperty("items").EnumerateArray().Count(x=>x.GetProperty("eligible").GetBoolean())==1,"HTTP preview distinguishes pending from draft without mutation");
        check((await Current(pending.ContentId)).State==WorkflowState.PendingReview,"Preview does not approve or publish");
        check((await Post(writer,"/admin/bulk/preview",new{action="PublishDirect",items=new[]{Item(draft)}})).Code==HttpStatusCode.Forbidden,"Writer cannot select direct publishing");
        check((await Execute(outsider,"PublishDirect",draft,Guid.NewGuid())).Code==HttpStatusCode.Forbidden,"Unauthorized item execution rejected");
        check((await Post(admin,"/admin/bulk/execute",new{action="PublishDirect",item=Item(draft),requestId=Guid.NewGuid()},false)).Code==HttpStatusCode.BadRequest,"Bulk CSRF required");
        check((await Post(admin,"/admin/bulk/execute",new{action="Trash",item=Item(draft),requestId=Guid.NewGuid()})).Code==HttpStatusCode.BadRequest,"Bulk delete/unpublish not exposed");
        check((await Execute(writer,"Submit",draft,Guid.NewGuid())).Code==HttpStatusCode.OK,"Writer submits exact selected version");
        var submitted=await Current(draft.ContentId);
        check((await Execute(reviewer,"Approve",submitted,Guid.NewGuid())).Code==HttpStatusCode.OK,"Reviewer approves exact submitted version");
        var approved=await Current(draft.ContentId);
        check((await Execute(publisher,"PublishApproved",approved,Guid.NewGuid())).Code==HttpStatusCode.OK,"Publisher uses approved workflow");
        var command=Guid.NewGuid();var result=await Execute(admin,"ApproveAndPublish",pending,command);
        check(result.Code==HttpStatusCode.OK && (await Current(pending.ContentId)).IsPublic,"Combined approve/publish through existing CMS commands");
        check((await Execute(admin,"ApproveAndPublish",pending,command)).Body.GetProperty("alreadyApplied").GetBoolean(),"Exact combined request replay does not create new publication");
        check((await Execute(admin,"PublishDirect",await Current(pending.ContentId),Guid.NewGuid())).Code==HttpStatusCode.Conflict,"Fresh bulk action cannot overwrite public version");
        await using(var db=new PortalDbContext(options))
        {
            var a=await new CurrentPermissionService(db).GetAsync(adminId);var flow=new WorkflowService(db,new HtmlPolicy());
            await flow.ApproveAsync(stale.ContentId,stale.CurrentRevisionId!.Value,a,stale.RowVersion,Guid.NewGuid());
            // Make the attachment fail after the dialog. The real publisher must
            // reject it, without rolling back an unrelated item in the batch.
            var asset=new MediaAsset{Id=Guid.NewGuid(),OriginalName="missing.pdf",MimeType="application/pdf",StorageKey="missing.pdf",ByteLength=32,Origin="EditorialDocument",ScanStatus="NotScanned"};
            db.MediaAssets.Add(asset);db.ContentAssets.Add(new(){RevisionId=broken.CurrentRevisionId!.Value,MediaAssetId=asset.Id,Usage="Attachment"});await db.SaveChangesAsync();
        }
        check((await Execute(admin,"ApproveAndPublish",stale,Guid.NewGuid())).Code==HttpStatusCode.Conflict,"Stale RowVersion cannot approve a changed article");
        var bad=await Execute(admin,"ApproveAndPublish",broken,Guid.NewGuid());
        check(bad.Code==HttpStatusCode.BadRequest && !(await Current(broken.ContentId)).IsPublic,"Invalid attachment does not publish; partial approval reported");
        check((await Execute(admin,"ApproveAndPublish",later,Guid.NewGuid())).Code==HttpStatusCode.OK,"Failure in one item does not block the next item");
        var scheduled=await Create();
        using(var r=await admin.PostAsync("/admin/editor-schedule/"+scheduled.ContentId,new FormUrlEncodedContent(new Dictionary<string,string>{["ContentId"]=scheduled.ContentId.ToString(),["RevisionId"]=scheduled.CurrentRevisionId!.Value.ToString(),["ContentVersion"]=Convert.ToBase64String(scheduled.RowVersion),["DueVietnam"]=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).AddDays(10).ToString("yyyy-MM-ddTHH:mm:ss"),["Direct"]="true",["EditorMode"]="direct",["__RequestVerificationToken"]=await token(admin)})))check(r.IsSuccessStatusCode,"Fixture active schedule via HTTP CMS");
        var current=await Current(scheduled.ContentId);
        check((await Execute(admin,"PublishDirect",current,Guid.NewGuid())).Code==HttpStatusCode.Conflict,"Current RowVersion cannot bypass existing schedule");
        await using(var db=new PortalDbContext(options))check(await db.PublicationJobs.AnyAsync(x=>x.ContentId==scheduled.ContentId&&x.IsActive)&&!current.IsPublic,"Schedule remains active and unpublished");
        var direct=await Create();check((await Execute(admin,"PublishDirect",direct,Guid.NewGuid())).Code==HttpStatusCode.OK,"Explicit direct action works only with its permission");
        check((await admin.GetAsync("/bai-viet/"+direct.ContentId)).StatusCode==HttpStatusCode.OK,"Published fixture article renders HTTP 200");
        check((await admin.GetStringAsync("/admin")).Contains("bulk-list-fragment"),"Updated Razor list and status counts render after batch");
        var scoped=await Create();
        check((await Post(admin,"/admin/bulk/preview",new{action="PublishDirect",items=new[]{Item(scoped)}})).Body.GetProperty("items")[0].GetProperty("eligible").GetBoolean(),"Scope fixture eligible before rights change");
        await using(var db=new PortalDbContext(options))
        {
            db.UserClaims.Add(new(){UserId=adminId,ClaimType="content.category.scope",ClaimValue="76"});await db.SaveChangesAsync();
        }
        check((await Execute(admin,"PublishDirect",scoped,Guid.NewGuid())).Code==HttpStatusCode.Forbidden && !(await Current(scoped.ContentId)).IsPublic,"Category scope is rechecked after preview before writing");
        var restricted=await Post(admin,"/admin/bulk/preview",new{action="PublishDirect",items=new[]{Item(scoped)}});
        check(!restricted.Body.GetProperty("items")[0].GetProperty("eligible").GetBoolean() && restricted.Body.GetProperty("items")[0].GetProperty("url").ValueKind==JsonValueKind.Null,"Out-of-scope preview hides title/link and skips item");
    }
}
