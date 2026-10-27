using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// Owner-authorized, additive LAN-only release of the five BA-reviewed public
// source pages. No account/configuration reseed; never an alternative Dev importer.
internal static class IntroductionSync
{
    internal static async Task RunAsync(string root, IConfiguration configuration, PortalDbContext dest,bool recoveredSource=false)
    {
        string[] sourceIds = recoveredSource?["1774","3194","3226","3458","3467","3328"]:["3060", "3061", "3062", "3063", "3064"];
        var expected=sourceIds.Length;
        LanDemoDeployment.Validate(configuration, "Development");
        if (dest.Database.GetDbConnection().Database != LanDemoDeployment.Database)
            throw new InvalidOperationException("Exact LAN destination required");
        var original = PortalConfiguration.Load(root);
        var connection = new SqlConnectionStringBuilder(DatabaseSettings.Connection(original));
        if (connection.DataSource != @".\SQLEXPRESS" || connection.InitialCatalog != "LandPortal_Dev")
            throw new InvalidOperationException("Expected local project connection");
        connection.InitialCatalog = "LandPortal_Test";
        var sourceConfig = new ConfigurationBuilder().AddConfiguration(original).AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:Portal"] = connection.ConnectionString, ["Sql:ExpectedDatabase"] = "LandPortal_Test"
        }).Build();
        await using var source = new PortalDbContext(DatabaseSettings.Options(sourceConfig, forMigration: true));
        var records = await source.SourceRecords.AsNoTracking().Where(x => x.Source == "qldd.mae.gov.vn" && (recoveredSource ? x.Kind==ContentKind.Article||x.Kind==ContentKind.Announcement : x.Kind == ContentKind.StaticPage) && sourceIds.Contains(x.SourceId)).ToListAsync();
        if (records.Count != expected || records.Select(x => x.SourceId).Distinct().Count() != expected)
            throw new InvalidOperationException("Exact five real source identities required");
        var existing = await dest.SourceRecords.AsNoTracking().Where(x => x.Source == "qldd.mae.gov.vn" && (recoveredSource ? x.Kind==ContentKind.Article||x.Kind==ContentKind.Announcement : x.Kind == ContentKind.StaticPage) && sourceIds.Contains(x.SourceId)).ToListAsync();
        if (existing.Count != 0) {
            if (existing.Count != expected) throw new InvalidOperationException("Partial prior release: reconcile without overwrite");
            Console.WriteLine($"Already present: {expected} source contents; no updates, including BA edits."); return;
        }
        var ids = records.Select(x => x.ContentId).ToArray();
        var contents = await source.Contents.AsNoTracking().Where(x => ids.Contains(x.Id) && x.IsPublic && !x.IsTrashed && !x.HasEditorialChanges && x.PublishedRevisionId != null).ToListAsync();
        if (contents.Count != expected || await dest.Contents.AnyAsync(x => ids.Contains(x.Id))) throw new InvalidOperationException("Public source set missing or destination identity collision");
        var revisionIds = contents.Select(x => x.PublishedRevisionId!.Value).ToArray();
        var revisions = await source.Revisions.AsNoTracking().Where(x => revisionIds.Contains(x.Id) && x.IsSourceRevision).ToListAsync();
        if (revisions.Count != expected || (!recoveredSource && revisions.Any(x => x.PrimaryCategoryId != 60 || !x.Title.Contains("chức năng", StringComparison.OrdinalIgnoreCase))) || (recoveredSource && revisions.Any(x=>!x.BodyHtml.Contains("/tep-nguon/",StringComparison.Ordinal))))
            throw new InvalidOperationException("BA function/category60 mapping not verified on Test");
        var links = await source.ContentAssets.AsNoTracking().Where(x => revisionIds.Contains(x.RevisionId)).ToListAsync();
        var mids = links.Select(x => x.MediaAssetId).Distinct().ToArray();
        var assets = await source.MediaAssets.AsNoTracking().Where(x => mids.Contains(x.Id) && x.ScanStatus == "Clean" && !x.IsQuarantined && x.Origin == "Source").ToListAsync();
        if (assets.Count != mids.Length) throw new InvalidOperationException("Real Clean source media required; no NotScanned/quarantine release");
        var mediaRoot = Path.Combine(root, ".local/tests/phase2a/media");
        foreach (var m in assets) {
            var path = StorageOptions.ResolveWithin(mediaRoot, m.StorageKey);
            var target = StorageOptions.ResolveWithin(configuration["Storage:MediaRoot"]!, m.StorageKey);
            foreach (var file in new[] { path, target })
                for (string? n = file; n != null; n = Path.GetDirectoryName(n))
                    if ((File.Exists(n) || Directory.Exists(n)) && (File.GetAttributes(n) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Reparse media refused");
            var bytes = await File.ReadAllBytesAsync(path);
            if (bytes.LongLength != m.ByteLength || Convert.ToHexStringLower(SHA256.HashData(bytes)) != m.Sha256) throw new InvalidOperationException("Test source media hash mismatch");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) { await using var f = new FileStream(target, FileMode.CreateNew); await f.WriteAsync(bytes); }
            if (Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(target))) != m.Sha256) throw new InvalidOperationException("LAN media hash mismatch; existing file never overwritten");
        }
        await using var transaction = await dest.Database.BeginTransactionAsync();
        var revisionCategories=await source.RevisionCategories.AsNoTracking().Where(x=>revisionIds.Contains(x.RevisionId)).ToListAsync();
        var contentCategories=await source.ContentCategories.AsNoTracking().Where(x=>ids.Contains(x.ContentId)).ToListAsync();
        var categoryIds=revisionCategories.Select(x=>x.CategoryId).Concat(contentCategories.Select(x=>x.CategoryId)).Concat(revisions.Where(x=>x.PrimaryCategoryId!=null).Select(x=>x.PrimaryCategoryId!.Value)).Distinct().ToArray();
        var knownCategories=await dest.Categories.Select(x=>x.Id).ToListAsync();
        dest.Categories.AddRange(await source.Categories.AsNoTracking().Where(x=>categoryIds.Contains(x.Id)&&!knownCategories.Contains(x.Id)).ToListAsync());
        var existingMedia = await dest.MediaAssets.Where(x => mids.Contains(x.Id)).ToListAsync();
        foreach (var m in assets) {
            var old = existingMedia.SingleOrDefault(x => x.Id == m.Id);
            if (old != null) { if (old.Sha256 != m.Sha256 || old.ScanStatus != "Clean" || old.IsQuarantined) throw new InvalidOperationException("Existing media collision"); }
            else { m.UploadedByUserId = null; dest.MediaAssets.Add(m); }
        }
        await dest.SaveChangesAsync();
        var admin = await dest.Users.Where(x => x.UserName == "lan-ba-admin").Select(x => x.Id).SingleAsync();
        foreach (var content in contents) {
            content.CurrentRevisionId = content.PublishedRevisionId = content.SubmittedRevisionId = content.ApprovedRevisionId = null;
            content.ApprovedByUserId = null; content.ApprovalHash = null; content.OwnerUserId = admin; content.RowVersion = [];
            content.Revisions = []; content.Categories = [];
        }
        dest.Contents.AddRange(contents); await dest.SaveChangesAsync();
        foreach (var revision in revisions) { revision.EditedByUserId = null; revision.Content = null!; revision.Assets = []; revision.Categories = []; }
        dest.Revisions.AddRange(revisions); await dest.SaveChangesAsync();
        dest.ContentAssets.AddRange(links.Select(x => new ContentAsset { RevisionId = x.RevisionId, MediaAssetId = x.MediaAssetId, Usage = x.Usage, Caption = x.Caption, SortOrder = x.SortOrder }));
        dest.RevisionCategories.AddRange(revisionCategories.Select(x => new RevisionCategory { RevisionId = x.RevisionId, CategoryId = x.CategoryId }));
        dest.ContentCategories.AddRange(contentCategories.Select(x => new ContentCategory { ContentId = x.ContentId, CategoryId = x.CategoryId }));
        foreach (var content in contents) { var revision = revisions.Single(x => x.ContentId == content.Id); content.CurrentRevisionId = content.PublishedRevisionId = revision.Id; }
        dest.SourceRecords.AddRange(records.Select(x => new SourceRecord { Source = x.Source, SourceId = x.SourceId, Kind = x.Kind, ContentId = x.ContentId, CanonicalUrl = x.CanonicalUrl, LastContentHash = x.LastContentHash, LastSeenUtc = x.LastSeenUtc }));
        var aliases = await source.SourceUrlAliases.AsNoTracking().Where(x => ids.Contains(x.ContentId)).ToListAsync();
        var aliasHashes = aliases.Select(x => x.UrlHash).ToArray();
        if (await dest.SourceUrlAliases.AnyAsync(x => aliasHashes.Contains(x.UrlHash))) throw new InvalidOperationException("Alias collision requires review");
        dest.SourceUrlAliases.AddRange(aliases.Select(x => new SourceUrlAlias { ContentId = x.ContentId, Url = x.Url, UrlHash = x.UrlHash }));
        dest.AddRange(revisions.Select(SearchProjectionService.Create));
        dest.AuditLogs.Add(new() { Actor = "owner-authorized-ba-source-release", Action = recoveredSource?"LanSourceRecoveryRelease":"LanIntroductionRelease", Detail = $"Additive {expected} Test public source release; unavailable attachments retain local404/source audit; Clean files/hash verified; no existing content/account/site updates" });
        await dest.SaveChangesAsync(); await transaction.CommitAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { addedContents = expected, addedRevisions = expected, cleanAssets = assets.Count, aliases = aliases.Count, devWrites = 0 }));
    }
}
