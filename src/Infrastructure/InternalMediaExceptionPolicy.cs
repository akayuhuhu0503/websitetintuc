using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

// Release exception is granted only by the scoped owner+DBA import, never upload.
public static class InternalMediaExceptionPolicy
{
    public const string Status="ScannerSkipped";
    public const string Prefix="Internal252:";
    public const string GrantAction="InternalMediaExceptionRelease";
    public const string StoragePrefix="uat-data/dee778f3401047f5898169b2fdabdaa3/";
    public static bool Candidate(MediaAsset asset)=>asset.Origin=="Source"&&!asset.IsQuarantined&&asset.ScanStatus==Status&&
        asset.ValidationStatus.StartsWith(Prefix,StringComparison.Ordinal)&&asset.ValidationStatus.Length==Prefix.Length+64&&
        asset.ValidationStatus[Prefix.Length..].All(Uri.IsHexDigit)&&asset.StorageKey.StartsWith(StoragePrefix,StringComparison.Ordinal)&&
        asset.Sha256.Length==64&&Path.GetFileNameWithoutExtension(asset.StorageKey)==asset.Sha256&&
        asset.MimeType is "image/jpeg" or "image/png" or "image/gif" or "image/webp" or "application/pdf";
    public static string Binding(MediaAsset asset)=>asset.ValidationStatus+"|"+asset.Sha256+"|"+asset.StorageKey;
    public static Task<bool> HasGrantAsync(PortalDbContext db,MediaAsset asset)=>Candidate(asset)
        ?db.AuditLogs.AsNoTracking().AnyAsync(a=>a.Action==GrantAction&&a.Actor==asset.Id.ToString()&&a.Detail==Binding(asset))
        :Task.FromResult(false);
}
