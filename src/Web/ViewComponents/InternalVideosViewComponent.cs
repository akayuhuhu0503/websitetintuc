using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.ViewComponents;
public sealed class InternalVideosViewComponent(PortalDbContext db,StorageOptions storage,VideoOptions options):ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Guid id,bool showMissing=false)
    {
        ViewData["ShowMissing"]=showMissing;
        var ids=new List<VideoEmbedModel>();if(!options.Enabled)return View(ids);
        var assets=await db.ContentAssets.AsNoTracking().Include(x=>x.MediaAsset).Where(a=>a.Usage=="Video"&&db.Contents.Any(c=>c.Id==id&&c.IsPublic&&!c.IsTrashed&&c.PublishedRevisionId==a.RevisionId)).OrderBy(x=>x.SortOrder).ToListAsync();
        foreach(var asset in assets)if(await VideoReadiness.ReadyAsync(db,storage,asset.MediaAsset,true))
        {
            var tracks=await db.ContentAssets.AsNoTracking().Where(a=>a.RevisionId==asset.RevisionId&&a.Usage=="Subtitle"&&db.Set<VideoSubtitle>().Any(t=>t.MediaAssetId==a.MediaAssetId&&t.VideoMediaAssetId==asset.MediaAssetId)).Select(x=>x.MediaAssetId).ToListAsync();var ready=new List<Guid>();foreach(var track in tracks)if(await VideoSubtitleService.ReadyAsync(db,storage,track,true))ready.Add(track);
            ids.Add(new(asset.MediaAssetId,asset.Caption,ready));
        }
        return View(ids);
    }
}
