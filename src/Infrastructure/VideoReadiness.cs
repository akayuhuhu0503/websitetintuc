using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public static class VideoReadiness
{
    public static bool HasProof(VideoFile v,bool development)=>v.State==VideoState.Ready&&v.Original.Origin=="VideoUpload"&&v.Original.ScanStatus=="Clean"&&v.Original.ValidationStatus=="VideoValidated"&&v.ScannedSha256==v.Original.Sha256&&v.ScannedUtc!=null&&!string.IsNullOrWhiteSpace(v.ScannerVersion)&&v.ReadyUtc!=null&&v.VideoCodec=="h264"&&v.AudioCodec is null or "aac"&&v.Mp4Bytes>0&&v.PosterBytes>0&&VideoUploadService.IsHash(v.Mp4Sha256)&&VideoUploadService.IsHash(v.PosterSha256)&&(v.ScannerProvider!="MicrosoftDefender"||development);
    public static async Task<bool> ReadyAsync(PortalDbContext db,StorageOptions storage,MediaAsset original,bool development,CancellationToken ct=default)
    {
        var v=await db.Set<VideoFile>().AsNoTracking().Include(x=>x.Original).SingleOrDefaultAsync(x=>x.MediaAssetId==original.Id,ct);
        if(v==null||!HasProof(v,development))return false;
        try
        {
            foreach(var item in new[]{(original.StorageKey,original.Sha256,original.ByteLength),(v.Mp4Key!,v.Mp4Sha256!,v.Mp4Bytes!.Value),(v.PosterKey!,v.PosterSha256!,v.PosterBytes!.Value)})
            {var path=MediaService.ResolveMediaPath(storage,item.Item1);if(!File.Exists(path)||new FileInfo(path).Length!=item.Item3||await VideoUploadService.HashFileAsync(path,ct)!=item.Item2)return false;}
            return true;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException or MediaUploadException){return false;}
    }
}
