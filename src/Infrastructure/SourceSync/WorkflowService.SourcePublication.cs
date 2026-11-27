using System.Security.Cryptography;
using AngleSharp.Html.Parser;
using LandPortal.Domain;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

public sealed partial class WorkflowService
{
    // Evidence comes from the collected checkpoint bound to a real SourceRecord,
    // never from SourceCredit or a checkbox submitted by the editor.
    private async Task<ParsedSource?> PublicationSourceAsync(Guid contentId)
    {
        var records = await db.SourceRecords.AsNoTracking().Where(x => x.ContentId == contentId && x.Source == "qldd.mae.gov.vn").ToListAsync();
        var identity = contentId.ToString();
        var checkpoints = await db.CrawlRuns.AsNoTracking().Where(x => x.Status == SourceSyncPolicy.RunStatus &&
            PortalDbContext.JsonValue(x.Checkpoint, "$.ContentId") == identity).Select(x => x.Checkpoint).ToListAsync();
        foreach (var json in checkpoints)
        {
            var data = SourceSyncPolicy.Read<SourceSyncCheckpoint>(json);
            if (data.Source is { } source && data.ContentId == contentId && data.Hash == SourceSyncPolicy.Hash(source) &&
                SourceSyncPolicy.Id(source.CanonicalUrl) == source.SourceId && records.Any(x => x.SourceId == source.SourceId)) return source;
        }
        return null;
    }

    private async Task<bool> HasVerifiedAnnouncementImagesAsync(string body)
    {
        if (storage == null) return false;
        var images = new HtmlParser().ParseDocument(body).QuerySelectorAll("img[src]");
        if (images.Length == 0) return false;
        var ids = new HashSet<Guid>();
        foreach (var image in images)
        {
            var src = image.GetAttribute("src") ?? "";
            if (!src.StartsWith("/media/", StringComparison.Ordinal) || !Guid.TryParseExact(src[7..], "D", out var id)) return false;
            ids.Add(id);
        }
        var options = mediaOptions ?? new MediaUploadOptions();
        var candidates = await db.MediaAssets.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync();
        if (candidates.Count != ids.Count) return false;
        var validator = new MediaService(db, storage, options);
        foreach (var asset in candidates)
        {
            if (!asset.MimeType.StartsWith("image/", StringComparison.Ordinal) || asset.IsQuarantined || !options.CanServe(asset)) return false;
            try
            {
                var path = MediaService.ResolveMediaPath(storage, asset.StorageKey);
                await using var file = File.OpenRead(path);
                if (file.Length != asset.ByteLength || Convert.ToHexStringLower(await SHA256.HashDataAsync(file)) != asset.Sha256) return false;
                if (asset.ValidationStatus == BatchMediaProof.Validation && !await BatchMediaProof.ValidAsync(storage, asset, options.VerifiedSourceAllowed)) return false;
                validator.ValidateContent(path, asset.MimeType, default);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or MediaUploadException or InvalidOperationException) { return false; }
        }
        return true;
    }
}
