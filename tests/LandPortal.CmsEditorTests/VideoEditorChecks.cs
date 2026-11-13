using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

static class VideoEditorChecks
{
    public static async Task RunAsync(DbContextOptions<PortalDbContext> options,StorageOptions storage,Action<bool,string> check)
    {
        await using(var guard=new PortalDbContext(options))
            if(!guard.Database.GetDbConnection().DataSource.StartsWith("(localdb)\\",StringComparison.OrdinalIgnoreCase)||guard.Database.GetDbConnection().Database!="LandPortal_Test")
                throw new InvalidOperationException("Video editor checks require the isolated LocalDB test database.");
        check(new[]{"Bản tin ngày 13.11.2026.mp4","Hội nghị - bản sửa.v2.MOV","thong.tin.đất.đai.webm"}.Select((name,index)=>VideoUploadService.ValidateFileName(name,new[]{"video/mp4","video/quicktime","video/webm"}[index])).SequenceEqual(new[]{".mp4",".mov",".webm"}),"Video names allow Vietnamese/spaces/multiple dots with final extension");
        foreach(var name in new[]{"../video.mp4","folder\\video.mp4","movie.mp4:stream","movie.mp4.exe","video?.mp4","video\u202Emp4.mp4","video.mp4 ","video.mp4."})
        {
            var denied=false;try{VideoUploadService.ValidateFileName(name,"video/mp4");}catch(MediaUploadException){denied=true;}
            check(denied,"Unsafe video filename rejected: "+name.Replace('\u202E','?'));
        }
        var mismatched=false;try{VideoUploadService.ValidateFileName("video.v2.mp4","image/png");}catch(MediaUploadException){mismatched=true;}
        check(mismatched,"Video filename helper preserves MIME validation");
        check(VideoCmsLabels.Article("Approved",false).Contains("chưa xuất bản")&&VideoCmsLabels.Article("Draft",true).Contains("Bản công khai trước đó")&&VideoCmsLabels.File("Pending")=="Đã tải lên, đang chờ xử lý","Video editorial/file labels distinguish approval, publication and processing without false scan claims");
        var policy=new VideoOptions{Enabled=true,ApprovedManagedVideoWithoutAv=true,MaxFileBytes=1024*1024,MinimumFreeBytes=1024,MaxSessions=5};
        var user="cms-video-"+Guid.NewGuid().ToString("N");var outsider="cms-video-other-"+Guid.NewGuid().ToString("N");
        var actor=new ContentActor(user,new HashSet<string>{Permissions.Create,Permissions.EditOwn,Permissions.ViewOwn,Permissions.Submit,Permissions.ManageVideos,Permissions.ManageMedia});
        const int category=90801,secondary=90802;
        await using(var db=new PortalDbContext(options))
        {
            db.Users.AddRange(new PortalUser{Id=user,UserName=user,NormalizedUserName=user.ToUpperInvariant()},new PortalUser{Id=outsider,UserName=outsider,NormalizedUserName=outsider.ToUpperInvariant()});
            foreach(var account in new[]{user,outsider})foreach(var permission in actor.Permissions)db.UserClaims.Add(new IdentityUserClaim<string>{UserId=account,ClaimType="permission",ClaimValue=permission});
            db.Categories.AddRange(new Category{Id=category,Name="Isolated video category",Slug="isolated-video-"+Guid.NewGuid().ToString("N")},new Category{Id=secondary,Name="Isolated secondary",Slug="isolated-secondary-"+Guid.NewGuid().ToString("N")});
            await db.SaveChangesAsync();
        }
        DraftInput Input(string title,List<AssetSelection>? assets=null)=>new(){Kind=ContentKind.Video,Title=title,Summary="Preserved summary",BodyHtml="<p>Preserved body.</p>",Author="Synthetic author",SourceCredit="Synthetic source",PrimaryCategoryId=category,SecondaryCategoryIds=[secondary],Metadata=new(){Unit="Synthetic video unit",PublicationDate=new DateOnly(2026,11,13)},Assets=assets??[]};
        WorkflowService Workflow(PortalDbContext db)=>new(db,new HtmlPolicy(),storage:storage,videoOptions:policy);
        VideoAdminController Controller(PortalDbContext db,string account)
        {
            var permissions=new CurrentPermissionService(db);var upload=new VideoUploadService(db,storage,policy,permissions);
            return new(db,policy,upload,permissions,Workflow(db),new VideoProcessor(policy,storage),new VideoSubtitleService(db,storage,policy,permissions),storage,new TestEnvironment())
            {ControllerContext=new(){HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,account)],"isolated-fixture"))}}};
        }
        async Task<(int Status,JsonElement Json)> Attach(Guid content,Guid session,string version,string? account=null)
        {
            await using var db=new PortalDbContext(options);var result=await Controller(db,account??user).AttachReady(content,new(session,version),CancellationToken.None);
            return result switch{JsonResult json=>(json.StatusCode??200,JsonSerializer.SerializeToElement(json.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web))),ObjectResult error=>(error.StatusCode??200,default),StatusCodeResult status=>(status.StatusCode,default),_=>throw new Exception("Unexpected controller result")};
        }
        async Task<JsonElement> State(Guid content)
        {
            await using var db=new PortalDbContext(options);var result=await Controller(db,user).EditorState(content,CancellationToken.None);
            return result is JsonResult json?JsonSerializer.SerializeToElement(json.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)):throw new Exception("Editor state failed");
        }
        WorkflowResult initial;
        await using(var db=new PortalDbContext(options))initial=await Workflow(db).SaveAsync(null,Input("Isolated video editor"),actor,null,Guid.NewGuid());
        var contentId=initial.ContentId;
        // These byte/hash fixtures exercise the existing readiness boundary, not FFmpeg.
        // The unchanged native codec/transcode pipeline is covered by its separate suite.
        async Task<(Guid Session,Guid Asset,string Mp4,byte[] Bytes)> Fixture(bool requestAutoAttach,VideoState state)
        {
            await using var db=new PortalDbContext(options);var uploads=new VideoUploadService(db,storage,policy,new CurrentPermissionService(db));
            var session=await uploads.CreateAsync(contentId,user,"Bản tin.v2.mp4","video/mp4",64,new string('a',64),autoAttach:requestAutoAttach);
            var key="cms-editor-fixture/"+session.Id.ToString("N")+"/";
            var original=RandomNumberGenerator.GetBytes(64);var mp4=Encoding.UTF8.GetBytes("ISOLATED OUTPUT "+session.Id);var poster=Encoding.UTF8.GetBytes("ISOLATED POSTER "+session.Id);
            async Task Write(string name,byte[] bytes){var path=MediaService.ResolveMediaPath(storage,key+name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,bytes);}
            await Write("original.mp4",original);await Write("output.mp4",mp4);await Write("poster.jpg",poster);
            string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
            var asset=new MediaAsset{Origin="VideoUpload",UploadedByUserId=user,OriginalName="video.mp4",MimeType="video/mp4",StorageKey=key+"original.mp4",ByteLength=original.Length,Sha256=Hash(original),ScanStatus="NotScanned",ValidationStatus=VideoOptions.NoAvValidated,IsQuarantined=true};
            db.MediaAssets.Add(asset);db.Add(new VideoFile{MediaAssetId=asset.Id,Original=asset,UploadSessionId=session.Id,State=state,ReadyUtc=DateTimeOffset.UtcNow,VideoCodec="h264",AudioCodec="aac",Mp4Key=key+"output.mp4",Mp4Sha256=Hash(mp4),Mp4Bytes=mp4.Length,PosterKey=key+"poster.jpg",PosterSha256=Hash(poster),PosterBytes=poster.Length,Width=320,Height=180,DurationSeconds=1});
            session.State=VideoUploadState.Completed;session.MediaAssetId=asset.Id;session.ReceivedBytes=session.ExpectedBytes;session.Sha256=asset.Sha256;db.Entry(session).State=EntityState.Modified;await db.SaveChangesAsync();
            return(session.Id,asset.Id,MediaService.ResolveMediaPath(storage,key+"output.mp4"),mp4);
        }
        var previous=await Fixture(false,VideoState.Ready);
        var preserved=new List<AssetSelection>{new(){MediaAssetId=previous.Asset,Usage="Video",Caption="Keep caption",SortOrder=4}};
        WorkflowResult current;await using(var db=new PortalDbContext(options))current=await Workflow(db).SaveAsync(contentId,Input("Unsaved fields now saved",preserved),actor,initial.RowVersion,Guid.NewGuid());
        var next=await Fixture(true,VideoState.Pending);
        var pending=await Attach(contentId,next.Session,Convert.ToBase64String(current.RowVersion));
        check(pending.Status==409,"Autoattach refuses unprocessed video without consuming request");
        await using(var db=new PortalDbContext(options))
        {
            check(!await db.ContentCommands.AnyAsync(x=>x.Id==next.Session),"Failed autoattach leaves durable intent pending");
            var video=await db.Set<VideoFile>().SingleAsync(x=>x.MediaAssetId==next.Asset);video.State=VideoState.Ready;await db.SaveChangesAsync();
        }
        var restored=await State(contentId);check(restored.GetProperty("sessions").EnumerateArray().Any(x=>x.GetProperty("id").GetGuid()==next.Session&&x.GetProperty("autoAttach").GetBoolean()),"Reopened editor restores pending autoattach intent from SQL");
        check((await Attach(contentId,next.Session,Convert.ToBase64String(initial.RowVersion))).Status==409,"Autoattach refuses stale rowversion rather than overwriting newer draft");
        check((await Attach(contentId,next.Session,Convert.ToBase64String(current.RowVersion),outsider)).Status==404,"Autoattach refuses another account's upload session");
        await File.AppendAllTextAsync(next.Mp4,"changed");
        check((await Attach(contentId,next.Session,Convert.ToBase64String(current.RowVersion))).Status==409,"Autoattach rechecks actual output hash before revision mutation");
        await File.WriteAllBytesAsync(next.Mp4,next.Bytes);
        int revisions;await using(var db=new PortalDbContext(options))revisions=await db.Revisions.CountAsync(x=>x.ContentId==contentId);
        var attached=await Attach(contentId,next.Session,Convert.ToBase64String(current.RowVersion));
        check(attached.Status==200&&attached.Json.GetProperty("videoAssetId").GetGuid()==next.Asset,"Ready video attached through official workflow and returned to editor");
        await using(var db=new PortalDbContext(options))
        {
            var draft=await db.ContentDrafts.AsNoTracking().Include(x=>x.Assets).Include(x=>x.Categories).SingleAsync(x=>x.ContentId==contentId);
            check(draft.Title=="Unsaved fields now saved"&&draft.Summary=="Preserved summary"&&draft.BodyHtml.Contains("Preserved body")&&draft.Author=="Synthetic author"&&draft.SourceCredit=="Synthetic source"&&draft.PrimaryCategoryId==category&&draft.Categories.Any(x=>x.CategoryId==secondary)&&draft.Metadata.Unit=="Synthetic video unit"&&draft.Metadata.Tags==null&&draft.Metadata.Featured==null&&draft.Metadata.PublicationDate==new DateOnly(2026,11,13),"Autoattach preserves current text/categories/metadata without overwriting editor fields");
            check(draft.Assets.Count==2&&draft.Assets.Single(x=>x.MediaAssetId==previous.Asset).Caption=="Keep caption"&&draft.Assets.Single(x=>x.MediaAssetId==previous.Asset).SortOrder==4,"Autoattach preserves existing asset captions/order and adds one video");
            check(await db.ContentCommands.AnyAsync(x=>x.Id==next.Session&&x.ContentId==contentId&&x.ActorUserId==user)&&await db.Revisions.CountAsync(x=>x.ContentId==contentId)==revisions+1,"Revision and one-shot command receipt committed together");
            check(!await db.Contents.AnyAsync(x=>x.Id==contentId&&x.IsPublic)&&!await db.MediaAssets.AnyAsync(x=>x.Id==next.Asset&&x.ScanStatus=="Clean"),"Autoattach never publishes or invents Clean");
        }
        var replay=await Attach(contentId,next.Session,attached.Json.GetProperty("rowVersion").GetString()!);
        check(replay.Status==200&&replay.Json.GetProperty("alreadyApplied").GetBoolean(),"Lost response retry returns existing workflow outcome");
        await using(var db=new PortalDbContext(options))check(await db.Revisions.CountAsync(x=>x.ContentId==contentId)==revisions+1,"Idempotent autoattach does not create duplicate revisions");
        current=new(contentId,null,null,Convert.FromBase64String(replay.Json.GetProperty("rowVersion").GetString()!),WorkflowState.Draft,false,false,false,DateTimeOffset.UtcNow);
        await using(var db=new PortalDbContext(options))current=await Workflow(db).SaveAsync(contentId,Input("Video deliberately removed",preserved),actor,current.RowVersion,Guid.NewGuid());
        var removed=await Attach(contentId,next.Session,Convert.ToBase64String(current.RowVersion));
        check(removed.Status==200&&removed.Json.GetProperty("videoAssetId").ValueKind==JsonValueKind.Null,"Consumed intent cannot reattach video deliberately removed later");
        restored=await State(contentId);check(restored.GetProperty("sessions").EnumerateArray().Where(x=>x.GetProperty("id").GetGuid()==next.Session).All(x=>!x.GetProperty("autoAttach").GetBoolean()),"Reopened editor does not repeat consumed attachment");
        check((await Attach(contentId,previous.Session,Convert.ToBase64String(current.RowVersion))).Status==409,"Legacy standalone upload does not silently acquire autoattach intent");
        await using(var db=new PortalDbContext(options)){var claim=await db.UserClaims.SingleAsync(x=>x.UserId==user&&x.ClaimType=="permission"&&x.ClaimValue==Permissions.ManageVideos);db.UserClaims.Remove(claim);await db.SaveChangesAsync();}
        check((await Attach(contentId,next.Session,Convert.ToBase64String(current.RowVersion))).Status==403,"Autoattach rechecks current video management permission");
    }
    private sealed class TestEnvironment:IHostEnvironment
    {
        public string EnvironmentName{get;set;}="Development";
        public string ApplicationName{get;set;}="IsolatedCmsVideoTests";
        public string ContentRootPath{get;set;}=Path.GetTempPath();
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
    }
}
