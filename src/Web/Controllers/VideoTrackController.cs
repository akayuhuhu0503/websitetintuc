using System.Security.Claims;
using System.Security.Cryptography;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
namespace LandPortal.Web.Controllers;
public sealed class VideoTrackController(PortalDbContext db,StorageOptions storage,VideoOptions options,CurrentPermissionService permissions):Controller
{
    [AcceptVerbs("GET","HEAD"),Route("video-track/{id:guid}")]
    public Task<IActionResult> Public(Guid id)=>Serve(id,null);
    [Authorize,AcceptVerbs("GET","HEAD"),Route("admin/videos/track/{id:guid}")]
    public Task<IActionResult> Preview(Guid id,Guid contentId)=>Serve(id,contentId);
    private async Task<bool> Allowed(VideoSubtitle track,Guid? contentId)
    {
        if(contentId==null)return await db.ContentAssets.AnyAsync(a=>a.MediaAssetId==track.MediaAssetId&&a.Usage=="Subtitle"&&db.ContentAssets.Any(v=>v.RevisionId==a.RevisionId&&v.MediaAssetId==track.VideoMediaAssetId&&v.Usage=="Video")&&db.Contents.Any(c=>c.IsPublic&&!c.IsTrashed&&c.PublishedRevisionId==a.RevisionId));
        var actor=await permissions.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)??"");var content=await db.Contents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==contentId);if(content==null||!await permissions.CanReadAsync(actor,content))return false;
        var revisions=permissions.ScopeRevisions(db.Revisions.AsNoTracking(),actor).Where(x=>x.ContentId==content.Id).Select(x=>x.Id);
        return await db.ContentAssets.AnyAsync(a=>a.MediaAssetId==track.MediaAssetId&&a.Usage=="Subtitle"&&revisions.Contains(a.RevisionId))||await db.DraftAssets.AnyAsync(a=>a.ContentId==content.Id&&a.MediaAssetId==track.MediaAssetId&&a.Usage=="Subtitle")||actor.Has(Permissions.ManageVideos)&&actor.Has(Permissions.ManageMedia)&&await permissions.CanEditAsync(actor,content)&&await db.Set<VideoUploadSession>().AnyAsync(s=>s.Id==track.UploadSessionId&&s.ContentId==content.Id&&s.OwnerUserId==actor.UserId);
    }
    private async Task<IActionResult> Serve(Guid id,Guid? contentId)
    {
        Response.Headers.CacheControl="no-store, private";Response.Headers["X-Content-Type-Options"]="nosniff";
        if(!options.Enabled)return NotFound();var track=await db.Set<VideoSubtitle>().AsNoTracking().Include(x=>x.Original).SingleOrDefaultAsync(x=>x.MediaAssetId==id);
        if(track==null||!await Allowed(track,contentId)||!await VideoSubtitleService.ReadyAsync(db,storage,id,true,HttpContext.RequestAborted))return NotFound();
        FileStream? stream=null;try
        {
            stream=new FileStream(MediaService.ResolveMediaPath(storage,track.Original.StorageKey),FileMode.Open,FileAccess.Read,FileShare.Read,16384,FileOptions.Asynchronous);
            if(stream.Length!=track.Original.ByteLength||Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,HttpContext.RequestAborted))!=track.ScannedSha256||!await Allowed(track,contentId)||!await db.Set<VideoSubtitle>().AnyAsync(x=>x.MediaAssetId==id&&x.State==InquiryScanState.Clean&&x.RowVersion==track.RowVersion)){await stream.DisposeAsync();return NotFound();}
            stream.Position=0;return File(stream,"text/vtt; charset=utf-8",lastModified:track.ScannedUtc,entityTag:new EntityTagHeaderValue('"'+track.Original.Sha256+'"'),enableRangeProcessing:false);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or MediaUploadException){if(stream!=null)await stream.DisposeAsync();return NotFound();}
    }
}
