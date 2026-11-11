using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public static class VideoReadiness
{
    public static bool HasProof(VideoFile v,bool development,VideoOptions? policy=null)
    {
        var scanned=v.Original.ScanStatus=="Clean" && v.Original.ValidationStatus=="VideoValidated"
            && v.ScannedSha256==v.Original.Sha256 && v.ScannedUtc!=null && !string.IsNullOrWhiteSpace(v.ScannerVersion)
            && (development || v.ScannerProvider is not ("MicrosoftDefender" or LanMediaSamples.Provider or LanStatisticsSamples.Provider));
        var managed=policy is {Enabled:true,ApprovedManagedVideoWithoutAv:true}
            && v.Original.ScanStatus=="NotScanned" && v.Original.ValidationStatus==VideoOptions.NoAvValidated
            && v.ScannedSha256==null && v.ScannedUtc==null && v.ScannerVersion==null && v.ScannerProvider==null
            && v.SignatureVersion==null && v.SignatureUpdatedUtc==null;
        return v.State==VideoState.Ready && v.Original.Origin=="VideoUpload" && (scanned||managed)
            && v.ReadyUtc!=null && v.VideoCodec=="h264" && v.AudioCodec is null or "aac"
            && v.Mp4Bytes>0 && v.PosterBytes>0 && VideoUploadService.IsHash(v.Mp4Sha256) && VideoUploadService.IsHash(v.PosterSha256);
    }
    public static async Task<bool> ReadyAsync(PortalDbContext db,StorageOptions storage,MediaAsset original,bool development,CancellationToken ct=default,VideoOptions? policy=null)
    {
        var v=await db.Set<VideoFile>().AsNoTracking().Include(x=>x.Original).SingleOrDefaultAsync(x=>x.MediaAssetId==original.Id,ct);
        if(v==null||!HasProof(v,development,policy))return false;
        try
        {
            foreach(var item in new[]{(original.StorageKey,original.Sha256,original.ByteLength),(v.Mp4Key!,v.Mp4Sha256!,v.Mp4Bytes!.Value),(v.PosterKey!,v.PosterSha256!,v.PosterBytes!.Value)})
            {var path=MediaService.ResolveMediaPath(storage,item.Item1);if(!File.Exists(path)||new FileInfo(path).Length!=item.Item3||await VideoUploadService.HashFileAsync(path,ct)!=item.Item2)return false;}
            return true;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException or MediaUploadException){return false;}
    }
}
