using System.Security.Cryptography;
using LandPortal.Domain;
namespace LandPortal.Infrastructure;
// Phase3B proofs are Test-only. They never authorize Dev/Production scan promotion.
public static class BatchMediaProof
{
    public const string Validation="Phase3BValidated";
    public static async Task<bool> ValidAsync(StorageOptions storage,MediaAsset asset,bool development)
    {
        if(!development||asset.Origin!="Source"||asset.ValidationStatus!=Validation||asset.IsQuarantined||asset.ScanStatus!="Clean")return false;
        try{
            var path=MediaService.ResolveMediaPath(storage,asset.StorageKey);
            await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            return file.Length==asset.ByteLength&&Convert.ToHexStringLower(await SHA256.HashDataAsync(file))==asset.Sha256;
        }catch(Exception e)when(e is IOException or InvalidOperationException or MediaUploadException){return false;}
    }
}
