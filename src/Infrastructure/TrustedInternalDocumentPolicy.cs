using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

// Per-upload administrator attestation. Never a scanner result or a public upload policy.
public static class TrustedInternalDocumentPolicy
{
    public const string Validation = "TrustedInternalValidatedV1";
    public const string GrantAction = "document.trusted.accepted.v1";
    public const string ReuseAction = "document.trusted.reused.v1";
    public static bool Enabled(MediaUploadOptions? options) => options?.NativeUatUploads == true;
    public static bool AllowedActor(ContentActor actor) => actor.Has(Permissions.ManageSettings) && actor.Has(Permissions.ManageMedia);
    public static bool AllowedMime(string mime) => mime is "application/pdf" or EditorialZipPolicy.Mime;
    public static bool Candidate(MediaAsset asset, MediaUploadOptions? options) => Enabled(options)
        && asset.Origin == "EditorialDocument" && !asset.IsQuarantined && asset.ScanStatus == "NotScanned"
        && asset.ValidationStatus == Validation && AllowedMime(asset.MimeType);
    public static string Binding(MediaAsset asset) => asset.Sha256 + "|" + asset.StorageKey + "|" + asset.MimeType + "|" + asset.Id;
    public static Task<bool> HasGrantAsync(PortalDbContext db, MediaAsset asset, EditorialDocument proof, CancellationToken ct = default)
    {
        var binding = Binding(asset);
        return db.AuditLogs.AsNoTracking().AnyAsync(a => a.Action == GrantAction && a.Actor == proof.OwnerId
            && a.Actor == asset.UploadedByUserId && a.ContentId == proof.ContentId && a.Detail == binding, ct);
    }
    public static Task<bool> HasReuseGrantAsync(PortalDbContext db, MediaAsset asset, Guid contentId, MediaUploadOptions? options, CancellationToken ct = default)
    {
        if (asset.MimeType != "application/pdf" || !Candidate(asset, options)) return Task.FromResult(false);
        var binding = Binding(asset);
        return db.AuditLogs.AsNoTracking().AnyAsync(a => a.Action == ReuseAction && a.ContentId == contentId && a.Detail == binding, ct);
    }
}
