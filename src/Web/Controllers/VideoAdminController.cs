using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.Controllers;
[Authorize]
public sealed class VideoAdminController(PortalDbContext db,VideoOptions options,VideoUploadService uploads,CurrentPermissionService permissions,WorkflowService workflow,VideoProcessor processor,VideoSubtitleService subtitles):Controller
{
    private string UserId=>User.FindFirstValue(ClaimTypes.NameIdentifier)??"";
    [HttpGet("admin/videos")]
    public async Task<IActionResult> Index(string? q,string? state,int page=1)
    {
        var actor=await permissions.GetAsync(UserId);if(!actor.Has(Permissions.ManageVideos)||!new[]{Permissions.ViewOwn,Permissions.ViewAll,Permissions.EditOwn,Permissions.EditAll,Permissions.Review}.Any(actor.Has))return Forbid();if(q?.Length>200)return BadRequest();
        var contents=permissions.Scope(db.Contents.AsNoTracking(),actor).Where(x=>x.Kind==ContentKind.Video);
        if(!actor.Has(Permissions.ViewAll)&&!actor.Has(Permissions.EditAll))contents=contents.Where(x=>x.OwnerUserId==UserId||actor.Has(Permissions.Review)&&x.SubmittedRevisionId!=null);
        if(!string.IsNullOrEmpty(state)){if(!Enum.TryParse<WorkflowState>(state,out var filter)||!Enum.IsDefined(filter))return BadRequest();contents=contents.Where(x=>x.State==filter);}
        var rows=from c in contents join r in db.Revisions on c.CurrentRevisionId equals (Guid?)r.Id select new{c.Id,r.Title,c.State,c.IsPublic,c.CreatedUtc};
        if(!string.IsNullOrWhiteSpace(q))rows=rows.Where(x=>EF.Functions.Collate(x.Title,"Vietnamese_100_CI_AI").Contains(q));page=Math.Clamp(page,1,10000);var total=await rows.CountAsync();
        var pageRows=await rows.OrderByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).Skip((page-1)*15).Take(15).ToListAsync();var items=pageRows.Select(x=>new VideoListItem(x.Id,x.Title,x.State.ToString(),x.IsPublic,x.CreatedUtc)).ToList();
        if(options.Enabled)
        {
            var ids=items.Select(x=>x.Id).ToArray();
            var latest=await db.Set<VideoUploadSession>().AsNoTracking().Where(s=>ids.Contains(s.ContentId)&&s.Id==db.Set<VideoUploadSession>().Where(t=>t.ContentId==s.ContentId).OrderByDescending(t=>t.CreatedUtc).ThenBy(t=>t.Id).Select(t=>t.Id).First()).Select(s=>new{s.ContentId,s.MediaAssetId,s.State,FileState=db.Set<VideoFile>().Where(v=>v.MediaAssetId==s.MediaAssetId).Select(v=>(VideoState?)v.State).FirstOrDefault(),Uploader=db.Users.Where(u=>u.Id==s.OwnerUserId).Select(u=>u.DisplayName).FirstOrDefault()}).ToListAsync();
            items=items.Select(item=>{var file=latest.SingleOrDefault(x=>x.ContentId==item.Id);return file==null?item:item with{FileState=file.FileState?.ToString()??file.State.ToString(),AssetId=file.MediaAssetId,Uploader=file.Uploader??"Tài khoản không còn hoạt động"};}).ToList();
        }
        return View("~/Views/VideoAdmin/Index.cshtml",new VideoIndexModel{Items=items,Query=q,State=state,Page=page,Total=total,Enabled=options.Enabled,CanCreate=actor.Has(Permissions.Create),Processor=(await processor.HealthAsync()).Code});
    }
    [HttpGet("admin/videos/upload/{contentId:guid}")]
    public async Task<IActionResult> Upload(Guid contentId)
    {
        try{await uploads.AuthorizeAsync(UserId,contentId,false);var c=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==contentId);var title=await db.Revisions.Where(x=>x.Id==c.CurrentRevisionId).Select(x=>x.Title).SingleAsync();return View("~/Views/VideoAdmin/Upload.cshtml",new VideoUploadModel(contentId,title,Convert.ToBase64String(c.RowVersion),options.MaxFileBytes,options.ChunkBytes,(await processor.HealthAsync()).Code));}
        catch(BadHttpRequestException e)when(e.StatusCode==413){return StatusCode(413,new{error="Chunk vượt giới hạn request của web server."});}catch(MediaUploadException e){return StatusCode(e.StatusCode,new{error=e.Message});}
    }
    public sealed record CreateRequest(Guid ContentId,string Name,string Mime,long Bytes,string ChunkManifestSha256);
    [HttpPost("admin/videos/sessions"),RequestSizeLimit(4096)]
    public Task<IActionResult> Create([FromBody] CreateRequest request,CancellationToken ct)=>Guard(async()=>{var s=await uploads.CreateAsync(request.ContentId,UserId,request.Name,request.Mime,request.Bytes,request.ChunkManifestSha256,ct);return Json(new{s.Id,s.ChunkBytes,s.ChunkCount,s.ExpectedBytes,s.ExpiresUtc});});
    [HttpGet("admin/videos/sessions")]
    public Task<IActionResult> Sessions(Guid contentId)=>Guard(async()=>{await uploads.AuthorizeAsync(UserId,contentId,false);return Json(await db.Set<VideoUploadSession>().AsNoTracking().Where(x=>x.ContentId==contentId&&x.OwnerUserId==UserId).OrderByDescending(x=>x.CreatedUtc).Select(x=>new{x.Id,state=x.State.ToString(),x.ExpectedBytes,x.ReceivedBytes,x.ExpiresUtc}).Take(20).ToListAsync());});
    [HttpGet("admin/videos/sessions/{id:guid}")]
    public Task<IActionResult> Status(Guid id)=>Guard(async()=>{var s=await uploads.GetAsync(id,UserId);var chunks=await db.Set<VideoChunk>().AsNoTracking().Where(x=>x.SessionId==id).OrderBy(x=>x.Index).Select(x=>new{x.Index,x.Bytes,x.Sha256}).ToListAsync();var job=s.MediaAssetId.HasValue?await db.Set<VideoFile>().AsNoTracking().Where(x=>x.MediaAssetId==s.MediaAssetId).Select(x=>new{state=x.State.ToString(),x.Error,x.Attempts,x.ScannerProvider,x.ScannedUtc,x.DurationSeconds,x.Width,x.Height}).SingleOrDefaultAsync():null;return Json(new{s.Id,state=s.State.ToString(),s.ExpectedBytes,s.ReceivedBytes,s.ChunkBytes,s.ChunkCount,s.Extension,s.ChunkManifestSha256,s.Sha256,s.MediaAssetId,s.ExpiresUtc,chunks,job});});
    [HttpPut("admin/videos/sessions/{id:guid}/chunks/{index:int}"),RequireUploadAntiforgeryHeader,DisableUploadFormValueModelBinding,RequestSizeLimit(8*1024*1024)]
    public Task<IActionResult> Chunk(Guid id,int index,CancellationToken ct)=>Guard(async()=>{var reused=await uploads.ChunkAsync(id,UserId,index,Request.Headers["X-Chunk-SHA256"].ToString(),Request.Body,ct);return Json(new{confirmed=true,reused});});
    [HttpPost("admin/videos/sessions/{id:guid}/complete"),RequestSizeLimit(1024)]
    public Task<IActionResult> Complete(Guid id,CancellationToken ct)=>Guard(async()=>{var s=await uploads.CompleteAsync(id,UserId,ct);return Json(new{s.Id,s.MediaAssetId,s.Sha256,state=s.State.ToString(),notice="Đủ byte đã xác nhận; tệp đang cách ly, chưa sẵn sàng phát."});});
    [HttpPost("admin/videos/sessions/{id:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid id,CancellationToken ct)=>Guard(async()=>{await uploads.CancelAsync(id,UserId,ct);return Json(new{cancelled=true});});
    [HttpPut("admin/videos/{videoId:guid}/subtitle"),RequireUploadAntiforgeryHeader,DisableUploadFormValueModelBinding,RequestSizeLimit(VideoSubtitleService.MaximumBytes)]
    public Task<IActionResult> Subtitle(Guid videoId,CancellationToken ct)=>Guard(async()=>{var t=await subtitles.UploadAsync(videoId,UserId,Request.Headers["X-Subtitle-Name"].ToString(),Request.Body,ct);return Json(new{t.MediaAssetId,state=t.State.ToString(),notice="Đã nhận phụ đề vào kho cách ly; cần tác vụ nền quét thật trước khi xuất bản."});});
    [HttpGet("admin/videos/{videoId:guid}/subtitles")]
    public Task<IActionResult> Subtitles(Guid videoId)=>Guard(async()=>{var v=await db.Set<VideoFile>().AsNoTracking().SingleOrDefaultAsync(x=>x.MediaAssetId==videoId)??throw new MediaUploadException("Không tìm thấy video.",404);await uploads.GetAsync(v.UploadSessionId,UserId);return Json(await db.Set<VideoSubtitle>().AsNoTracking().Where(x=>x.VideoMediaAssetId==videoId).OrderBy(x=>x.AvailableUtc).Take(10).Select(x=>new{x.MediaAssetId,state=x.State.ToString(),x.Error,x.Attempts}).ToListAsync());});
    public sealed record SelectRequest(Guid AssetId,string RowVersion,Guid CommandId,string Usage="Video");
    [HttpPost("admin/videos/{contentId:guid}/select"),RequestSizeLimit(4096)]
    public Task<IActionResult> Select(Guid contentId,[FromBody] SelectRequest request)=>Guard(async()=>
    {
        var actor=await uploads.AuthorizeAsync(UserId,contentId,true);var content=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==contentId);var draft=await db.ContentDrafts.AsNoTracking().Include(x=>x.Categories).Include(x=>x.Assets).SingleAsync(x=>x.ContentId==contentId);
        if(request.Usage is not("Video" or "Subtitle"))throw new MediaUploadException("Loại liên kết không hợp lệ.");
        var asset=await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==request.AssetId)??throw new MediaUploadException("Không tìm thấy tệp.",404);
        if(request.Usage=="Subtitle"?!await db.Set<VideoSubtitle>().AnyAsync(x=>x.MediaAssetId==asset.Id):!await db.Set<VideoFile>().AnyAsync(x=>x.MediaAssetId==asset.Id))throw new MediaUploadException("Không tìm thấy tệp theo loại đã chọn.",404);
        var isPublic=await db.ContentAssets.AnyAsync(x=>x.MediaAssetId==request.AssetId&&x.Usage==request.Usage&&db.Contents.Any(c=>c.IsPublic&&!c.IsTrashed&&c.PublishedRevisionId==x.RevisionId));
        if(asset.UploadedByUserId!=UserId&&!isPublic)throw new MediaUploadException("Không được gắn tệp riêng tư của người khác.",404);
        var existing=draft.Assets.SingleOrDefault(x=>x.MediaAssetId==request.AssetId);var linked=draft.Assets.Select(x=>new AssetSelection{MediaAssetId=x.MediaAssetId,Usage=x.Usage,Caption=x.Caption,SortOrder=x.SortOrder}).Where(x=>x.MediaAssetId!=request.AssetId).ToList();linked.Add(new(){MediaAssetId=request.AssetId,Usage=request.Usage,Caption=existing?.Caption??"",SortOrder=existing?.SortOrder??linked.Count});
        var result=await workflow.SaveAsync(contentId,new(){Kind=content.Kind,Title=draft.Title,Summary=draft.Summary,BodyHtml=draft.BodyHtml,Author=draft.Author,SourceCredit=draft.SourceCredit,Metadata=draft.Metadata,PrimaryCategoryId=draft.PrimaryCategoryId,SecondaryCategoryIds=draft.Categories.Select(x=>x.CategoryId).Where(x=>x!=draft.PrimaryCategoryId).ToList(),CoverAssetId=draft.CoverAssetId,Assets=linked},actor,Convert.FromBase64String(request.RowVersion),request.CommandId);
        return Json(new{saved=true,rowVersion=Convert.ToBase64String(result.RowVersion),notice="Đã gắn vào phiên bản nháp mới; bản công khai giữ nguyên. Khi xuất bản, hệ thống vẫn kiểm tra tệp đã sẵn sàng."});
    });
    [HttpGet("admin/videos/library")]
    public Task<IActionResult> Library(Guid contentId,int page=1)=>Guard(async()=>
    {
        await uploads.AuthorizeAsync(UserId,contentId,true);page=Math.Clamp(page,1,10000);var rows=db.Set<VideoFile>().AsNoTracking().Where(v=>v.State==VideoState.Ready&&(v.Original.UploadedByUserId==UserId||db.ContentAssets.Any(a=>a.MediaAssetId==v.MediaAssetId&&a.Usage=="Video"&&db.Contents.Any(c=>c.IsPublic&&!c.IsTrashed&&c.PublishedRevisionId==a.RevisionId))));return Json(new{items=await rows.OrderByDescending(x=>x.ReadyUtc).ThenBy(x=>x.MediaAssetId).Skip((page-1)*15).Take(15).Select(x=>new{x.MediaAssetId,x.DurationSeconds,x.Width,x.Height}).ToListAsync(),page,total=await rows.CountAsync()});
    });
    [HttpPost("admin/videos/{id:guid}/retry")]
    public Task<IActionResult> Retry(Guid id)=>Guard(async()=>
    {
        var file=await db.Set<VideoFile>().Include(x=>x.Original).SingleOrDefaultAsync(x=>x.MediaAssetId==id&&x.Original.UploadedByUserId==UserId)??throw new MediaUploadException("Không tìm thấy video.",404);var session=await uploads.GetAsync(file.UploadSessionId,UserId);await uploads.AuthorizeAsync(UserId,session.ContentId,true);
        if(file.State is not(VideoState.Blocked or VideoState.ScanFailed or VideoState.Failed)||file.Attempts>=3)throw new MediaUploadException("Trạng thái hoặc giới hạn ba lần thử không cho retry.",409);file.State=VideoState.Pending;file.AvailableUtc=DateTimeOffset.UtcNow;file.Error="";db.AuditLogs.Add(new(){Actor=UserId,Action="video.processing.retry",ContentId=session.ContentId,Detail=id.ToString()});await db.SaveChangesAsync();return Json(new{queued=true});
    });
    [HttpPost("admin/videos/{id:guid}/cancel-job")]
    public Task<IActionResult> CancelJob(Guid id)=>Guard(async()=>
    {
        var file=await db.Set<VideoFile>().AsNoTracking().SingleOrDefaultAsync(x=>x.MediaAssetId==id)??throw new MediaUploadException("Không tìm thấy video.",404);var session=await uploads.GetAsync(file.UploadSessionId,UserId);await uploads.AuthorizeAsync(UserId,session.ContentId,true);
        if(file.State==VideoState.Ready)throw new MediaUploadException("Không hủy tệp sẵn sàng; gỡ tham chiếu qua quy trình duyệt phiên bản.",409);
        if(file.State==VideoState.Cancelled)return Json(new{cancelled=true});
        await using var tx=await db.Database.BeginTransactionAsync();var changed=await db.Set<VideoFile>().Where(x=>x.MediaAssetId==id&&x.RowVersion==file.RowVersion).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.State,VideoState.Cancelled).SetProperty(x=>x.LeaseToken,(Guid?)null).SetProperty(x=>x.LeaseUntilUtc,(DateTimeOffset?)null));if(changed!=1)throw new DbUpdateConcurrencyException();db.AuditLogs.Add(new(){Actor=UserId,Action="video.processing.cancelled",ContentId=session.ContentId,Detail=id.ToString()});await db.SaveChangesAsync();await tx.CommitAsync();return Json(new{cancelled=true,notice="Đã thu hồi quyền ghi kết quả của tác vụ; không xóa bản gốc hoặc phiên bản. Tiến trình đang chạy chịu giới hạn thời gian riêng."});
    });
    private async Task<IActionResult> Guard(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl="no-store, private";
        try{return await action();}catch(BadHttpRequestException e)when(e.StatusCode==413){return StatusCode(413,new{error="Chunk vượt giới hạn request của web server."});}catch(MediaUploadException e){return StatusCode(e.StatusCode,new{error=e.Message});}catch(DbUpdateConcurrencyException){return Conflict(new{error="Phiên bản đã đổi. Tải lại trạng thái để đối chiếu."});}catch(FormatException){return BadRequest(new{error="Mã phiên bản không hợp lệ."});}catch(UnauthorizedAccessException){return StatusCode(403);}catch(InvalidOperationException){return Conflict(new{error="Nháp hoặc trạng thái đã thay đổi; chưa lưu thao tác."});}catch(IOException){return StatusCode(507,new{error="Kho tệp chưa xác nhận ghi thành công; kiểm tra trạng thái rồi thử lại."});}
    }
}
