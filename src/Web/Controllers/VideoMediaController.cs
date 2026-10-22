using System.Security.Claims;
using System.Security.Cryptography;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
namespace LandPortal.Web.Controllers;
public sealed class VideoMediaController(PortalDbContext db,StorageOptions storage,VideoOptions options,CurrentPermissionService permissions):Controller
{
    [AcceptVerbs("GET","HEAD"),Route("video-media/{id:guid}/{part}")]
    public Task<IActionResult> Public(Guid id,string part)=>Serve(id,part,null);
    [Microsoft.AspNetCore.Authorization.Authorize,AcceptVerbs("GET","HEAD"),Route("admin/videos/media/{id:guid}/{part}")]
    public Task<IActionResult> Preview(Guid id,string part,Guid contentId)=>Serve(id,part,contentId);
    private async Task<bool> Allowed(Guid id,Guid? contentId)
    {
        if(contentId==null)return await db.ContentAssets.AnyAsync(a=>a.MediaAssetId==id&&a.Usage=="Video"&&db.Contents.Any(c=>c.IsPublic&&!c.IsTrashed&&c.PublishedRevisionId==a.RevisionId));
        var actor=await permissions.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)??"");var c=await db.Contents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==contentId);
        if(c==null||!await permissions.CanReadAsync(actor,c))return false;
        var revisions=permissions.ScopeRevisions(db.Revisions.AsNoTracking(),actor).Where(x=>x.ContentId==c.Id).Select(x=>x.Id);
        return await db.ContentAssets.AnyAsync(a=>a.MediaAssetId==id&&a.Usage=="Video"&&revisions.Contains(a.RevisionId))||await db.DraftAssets.AnyAsync(a=>a.ContentId==c.Id&&a.MediaAssetId==id&&a.Usage=="Video")||actor.Has(LandPortal.Application.Permissions.ManageVideos)&&actor.Has(LandPortal.Application.Permissions.ManageMedia)&&await permissions.CanEditAsync(actor,c)&&await db.Set<VideoUploadSession>().AnyAsync(s=>s.ContentId==c.Id&&s.OwnerUserId==actor.UserId&&s.MediaAssetId==id&&s.State==VideoUploadState.Completed);
    }
    private async Task<IActionResult> Serve(Guid id,string part,Guid? contentId)
    {
        Response.Headers.CacheControl="no-store, private";Response.Headers.Pragma="no-cache";Response.Headers["X-Content-Type-Options"]="nosniff";
        if(!options.Enabled||part is not("play" or "poster")||!await Allowed(id,contentId))return NotFound();
        var video=await db.Set<VideoFile>().Include(x=>x.Original).AsNoTracking().SingleOrDefaultAsync(x=>x.MediaAssetId==id);
        if(video==null||!await VideoReadiness.ReadyAsync(db,storage,video.Original,true,HttpContext.RequestAborted))return contentId==null?NotFound():StatusCode(423);
        var key=part=="play"?video.Mp4Key!:video.PosterKey!;var expected=part=="play"?video.Mp4Sha256!:video.PosterSha256!;var length=part=="play"?video.Mp4Bytes!.Value:video.PosterBytes!.Value;
        FileStream? stream=null;
        try
        {
            stream=new FileStream(MediaService.ResolveMediaPath(storage,key),FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
            if(stream.Length!=length||Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,HttpContext.RequestAborted))!=expected||!await Allowed(id,contentId)||!await db.Set<VideoFile>().AnyAsync(x=>x.MediaAssetId==id&&x.State==VideoState.Ready&&x.RowVersion==video.RowVersion)){await stream.DisposeAsync();return NotFound();}
            stream.Position=0;return File(stream,part=="play"?"video/mp4":"image/jpeg",lastModified:video.ReadyUtc,entityTag:new EntityTagHeaderValue('"'+expected+'"'),enableRangeProcessing:true);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or MediaUploadException){if(stream!=null)await stream.DisposeAsync();return NotFound();}
    }
}
