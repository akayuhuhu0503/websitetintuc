using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.ViewComponents;
public sealed class HomeVideosViewComponent(PortalDbContext db, PublicContentQuery query, StorageOptions storage, VideoOptions options, IHostEnvironment environment) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(SitePublicConfiguration? site = null)
    {
        var cards=new List<HomeVideoCard>();
        var blocks=site?.Revision.Blocks.Where(x=>x.Type=="Videos").OrderBy(x=>x.SortOrder).ThenBy(x=>x.Id).ToArray()??[];
        if(!options.Enabled || blocks.Length>0 && blocks.All(x=>!x.Enabled))return View(cards);
        var enabled=blocks.Where(x=>x.Enabled).ToArray();
        var limit=enabled.Length==0?3:Math.Min(3,enabled.Sum(x=>x.Limit));
        if(limit<=0)return View(cards);
        var pins=enabled.SelectMany(b=>site!.Revision.Pins.Where(x=>x.BlockId==b.Id).OrderBy(x=>x.SortOrder).ThenBy(x=>x.ContentId)).Select(x=>x.ContentId).Distinct().Take(12).ToArray();
        var rows=query.Published().Where(x=>x.Kind==ContentKind.Video && db.ContentAssets.Any(a=>a.RevisionId==x.RevisionId&&a.Usage=="Video"&&db.Set<VideoFile>().Any(v=>v.MediaAssetId==a.MediaAssetId&&v.State==VideoState.Ready&&v.Original.ScanStatus=="Clean")));
        var pinned=pins.Length==0?[]:await PublicContentQuery.ForDisplay(rows.Where(x=>pins.Contains(x.Id))).ToListAsync();
        var latest=await query.CardsPageAsync(PublicContentQuery.Recent(rows.Where(x=>!pins.Contains(x.Id))),0,12);
        var candidates=pins.Select(id=>pinned.SingleOrDefault(x=>x.Id==id)).Where(x=>x!=null).Select(x=>x!).Concat(latest).Take(24).ToArray();
        var revisions=candidates.Select(x=>x.RevisionId).ToArray();
        var assets=await db.ContentAssets.AsNoTracking().Where(x=>revisions.Contains(x.RevisionId)&&x.Usage=="Video").OrderBy(x=>x.SortOrder).ThenBy(x=>x.MediaAssetId).Select(x=>new{x.RevisionId,x.MediaAssetId}).ToArrayAsync();
        var ids=assets.Select(x=>x.MediaAssetId).Distinct().ToArray();
        var videos=await db.Set<VideoFile>().AsNoTracking().Include(x=>x.Original).Where(x=>ids.Contains(x.MediaAssetId)).ToDictionaryAsync(x=>x.MediaAssetId);
        foreach(var row in candidates)
        {
            // Metadata/size precheck only on the homepage; the existing media
            // endpoint revalidates full SHA and publication rights before streaming.
            var video=assets.Where(x=>x.RevisionId==row.RevisionId).Select(x=>videos.GetValueOrDefault(x.MediaAssetId)).FirstOrDefault(v=>v!=null&&Present(v));
            if(video==null)continue;
            cards.Add(new(PublicContentQuery.Card(row),video.MediaAssetId,video.DurationSeconds));
            if(cards.Count==limit)break;
        }
        return View(cards);
    }
    private bool Present(VideoFile v)
    {
        if(!VideoReadiness.HasProof(v,environment.IsDevelopment()))return false;
        try
        {
            return new[]{(v.Original.StorageKey,(long?)v.Original.ByteLength),(v.Mp4Key,v.Mp4Bytes),(v.PosterKey,v.PosterBytes)}
                .All(x=>x.Item1!=null&&x.Item2>0&&new FileInfo(MediaService.ResolveMediaPath(storage,x.Item1)) is {Exists:true} file&&file.Length==x.Item2);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or MediaUploadException or InvalidOperationException){return false;}
    }
}
