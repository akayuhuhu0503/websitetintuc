using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace LandPortal.MigrationCli;
public sealed partial class SqlImporter(PortalDbContext db, StorageOptions storage)
{
    public async Task<object> ImportAsync(SampleManifest manifest, CancellationToken cancellation = default)
    {
        var beforeCounts = await CountsAsync(cancellation);
        await PersistCrawlAsync(manifest, cancellation);
        var run = new ImportRun { CrawlRunId = manifest.CrawlRunId };
        db.ImportRuns.Add(run); await db.SaveChangesAsync(cancellation);
        foreach (var staged in manifest.Contents)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
            try
            {
                var source = staged.Source;
                var record = await db.SourceRecords.Include(x => x.Content).SingleOrDefaultAsync(x => x.Source == "qldd.mae.gov.vn" && x.Kind == source.Kind && x.SourceId == source.SourceId, cancellation);
                if (record is not null && record.LastContentHash == staged.ContentHash && await SourceMetadataMatchesAsync(record.ContentId, staged, manifest, cancellation))
                {
                    record.LastSeenUtc = DateTimeOffset.UtcNow;
                    await AliasesAsync(record.ContentId, staged.Aliases, cancellation);
                    // An unchanged source import must not mutate a published snapshot
                    // or an editor's category selections.
                    await SetItemStateAsync(manifest.CrawlRunId, source, "Imported", cancellation);
                    run.Unchanged++; await db.SaveChangesAsync(cancellation); await transaction.CommitAsync(cancellation); continue;
                }
                var content = record?.Content ?? new Content { Kind = source.Kind, State = WorkflowState.Published, IsPublic = true,
                    PublishedAtUtc = source.PublishedUtc, SourcePublishedDate = source.PublishedDate,
                    OriginalDateText = source.OriginalDateText, DatePrecision = source.DatePrecision, CommentsEnabled = false,
                    PrimaryCategoryId = source.PrimaryCategoryId is int cat && await db.Categories.AnyAsync(x => x.Id == cat, cancellation) ? cat : null };
                if (record is null) { db.Contents.Add(content); await db.SaveChangesAsync(cancellation); }
                // Resolve real assets before building the immutable revision. Do not
                // save a snapshot and then mutate its metadata or relation rows.
                var resolvedAssets = new List<(Guid Id, AssetEntry Entry, int Order)>();
                var bodyHtml = source.BodyHtml;
                foreach (var id in staged.AssetIds)
                {
                    var entry = manifest.Assets.FirstOrDefault(x => x.Id == id) ?? throw new InvalidDataException("Staged asset reference missing from manifest.");
                    await ValidateAssetAsync(entry, cancellation);
                    var asset = await db.MediaAssets.SingleOrDefaultAsync(x => x.Sha256 == entry.Sha256, cancellation);
                    if (asset is null)
                    {
                        asset = new MediaAsset { Id = entry.Id, Sha256 = entry.Sha256, StorageKey = entry.StorageKey, ByteLength = entry.Bytes,
                            MimeType = entry.MimeType, SourceUrl = entry.SourceUrl, OriginalName = entry.OriginalName,
                            // This is local phase-1 evidence only; no malware scanner is configured or claimed.
                            ScanStatus = "NotScanned", IsQuarantined = false };
                        db.MediaAssets.Add(asset); await db.SaveChangesAsync(cancellation);
                    }
                    if (asset.Id != id) bodyHtml = bodyHtml.Replace("/media/" + id, "/media/" + asset.Id, StringComparison.OrdinalIgnoreCase);
                    if (resolvedAssets.All(x => x.Id != asset.Id)) resolvedAssets.Add((asset.Id, entry, staged.AssetIds.IndexOf(id)));
                }
                var primaryCategory = source.PrimaryCategoryId is int primary && await db.Categories.AnyAsync(x => x.Id == primary, cancellation) ? (int?)primary : null;
                var categoryIds = source.CategoryIds.Append(primaryCategory ?? -1).Distinct().ToArray();
                categoryIds = await db.Categories.Where(x => categoryIds.Contains(x.Id)).Select(x => x.Id).ToArrayAsync(cancellation);
                var revision = new ContentRevision { Metadata = SourceMetadata(source), ContentId = content.Id, Title = source.Title, Summary = source.Summary,
                    BodyHtml = bodyHtml, Author = source.Author, SourceCredit = "qldd.mae.gov.vn", IsSourceRevision = true,
                    PrimaryCategoryId = primaryCategory, CoverAssetId = resolvedAssets.Where(x => x.Entry.MimeType.StartsWith("image/")).Select(x => (Guid?)x.Id).FirstOrDefault(),
                    ContentHash = staged.ContentHash, SearchText = TextNormalizer.Search(source.Title + " " + source.Summary + " " + new HtmlPolicy().PlainText(bodyHtml)),
                    Categories = categoryIds.Select(x => new RevisionCategory { CategoryId = x }).ToList(),
                    Assets = resolvedAssets.Select(x => new ContentAsset { MediaAssetId = x.Id, Usage = x.Entry.MimeType.StartsWith("image/") ? "Inline" : "Attachment", SortOrder = x.Order }).ToList() };
                db.Revisions.Add(revision); db.Add(SearchProjectionService.Create(revision)); await db.SaveChangesAsync(cancellation);
                if (!content.HasEditorialChanges && !content.IsTrashed && content.State is not (WorkflowState.Unpublished or WorkflowState.Archived))
                {
                    content.CurrentRevisionId = revision.Id; content.PublishedRevisionId = revision.Id;
                    content.State = WorkflowState.Published; content.IsPublic = true; content.PrimaryCategoryId = primaryCategory;
                    content.PublishedAtUtc = source.PublishedUtc; content.SourcePublishedDate = source.PublishedDate;
                    content.OriginalDateText = source.OriginalDateText; content.DatePrecision = source.DatePrecision;
                    await AddCategoriesAsync(content.Id, categoryIds, cancellation);
                }
                else db.ImportIssues.Add(new() { CrawlRunId = manifest.CrawlRunId, SourceUrl = source.SourceUrl, Stage = "Import", Code = "SOURCE_CHANGE_AWAITING_COMPARISON", Message = "Imported source revision preserved; editorial/published revisions were not overwritten." });
                if (record is null)
                {
                    record = new SourceRecord { ContentId = content.Id, Kind = source.Kind, SourceId = source.SourceId, CanonicalUrl = source.CanonicalUrl };
                    db.SourceRecords.Add(record);
                }
                record.LastContentHash = staged.ContentHash; record.LastSeenUtc = DateTimeOffset.UtcNow;
                await AliasesAsync(content.Id, staged.Aliases, cancellation);
                if (source.Kind == ContentKind.LegalDocument && !await db.LegalDocuments.AnyAsync(x => x.ContentId == content.Id, cancellation))
                    db.LegalDocuments.Add(new() { ContentId = content.Id, Number = source.Fields.GetValueOrDefault("Số ký hiệu", ""), DocumentType = source.Fields.GetValueOrDefault("Loại văn bản", ""),
                        IssuingBody = source.Fields.GetValueOrDefault("Cơ quan ban hành", ""), Signer = source.Fields.GetValueOrDefault("Người ký", ""), Field = source.Fields.GetValueOrDefault("Lĩnh vực", ""), IssuedDate = ParseDate(source.Fields.GetValueOrDefault("Ngày ban hành", "")) });
                if (source.Kind == ContentKind.DraftDocument && !await db.DraftDocuments.AnyAsync(x => x.ContentId == content.Id, cancellation))
                    db.DraftDocuments.Add(new() { ContentId = content.Id, ConsultationDateEvidence = source.Fields.GetValueOrDefault("Ngày bắt đầu", "") + " – " + source.Fields.GetValueOrDefault("Ngày hết hạn", ""),
                        ConsultationStartUtc = ConsultationDate(source.Fields.GetValueOrDefault("Ngày bắt đầu", ""), false), ConsultationEndUtc = ConsultationDate(source.Fields.GetValueOrDefault("Ngày hết hạn", ""), true) });
                db.AuditLogs.Add(new() { Actor = "migration-cli", Action = "SourceImport", ContentId = content.Id, RevisionId = revision.Id, Detail = "CrawlRun=" + manifest.CrawlRunId + "; source=" + source.Kind + ":" + source.SourceId });
                await SetItemStateAsync(manifest.CrawlRunId, source, "Imported", cancellation);
                run.Imported++; await db.SaveChangesAsync(cancellation); await transaction.CommitAsync(cancellation);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await transaction.RollbackAsync(cancellation); db.ChangeTracker.Clear();
                run = await db.ImportRuns.SingleAsync(x => x.Id == run.Id, cancellation); run.Failed++;
                db.ImportIssues.Add(new() { CrawlRunId = manifest.CrawlRunId, SourceUrl = staged.Source.SourceUrl, Stage = "Import", Code = "SQL_IMPORT_ERROR", Message = exception.GetType().Name + ": " + exception.Message });
                await db.SaveChangesAsync(cancellation);
                Console.Error.WriteLine("SQL import failed source=" + staged.Source.SourceId + ": " + exception.Message);
            }
        }
        run.Status = run.Failed == 0 ? "SampleImported" : "CompletedWithErrors"; await db.SaveChangesAsync(cancellation);
        var afterCounts = await CountsAsync(cancellation);
        return new { run.Imported, run.Unchanged, run.Failed, run.Status, SourceTotal = "Unknown; bounded sample only", DatabaseBefore = beforeCounts, DatabaseAfter = afterCounts };
    }
    private static ContentMetadata SourceMetadata(ParsedSource source)
    {
        var m = new ContentMetadata { MetadataOrigin="LegacyContentMetadata" };
        if(source.Kind == ContentKind.LegalDocument) {
            m.Number=source.Fields.GetValueOrDefault("Số ký hiệu",""); m.DocumentType=source.Fields.GetValueOrDefault("Loại văn bản","");
            m.IssuingBody=source.Fields.GetValueOrDefault("Cơ quan ban hành",""); m.Signer=source.Fields.GetValueOrDefault("Người ký","");
            m.Field=source.Fields.GetValueOrDefault("Lĩnh vực",""); m.IssuedDate=ParseDate(source.Fields.GetValueOrDefault("Ngày ban hành",""));
        }
        if(source.Kind == ContentKind.DraftDocument) {
            var first=source.Fields.GetValueOrDefault("Ngày bắt đầu",""); var last=source.Fields.GetValueOrDefault("Ngày hết hạn","");
            m.ConsultationStartDate=ParseDate(first); m.ConsultationEndDate=ParseDate(last);
            m.ConsultationStartUtc=ConsultationDate(first,false); m.ConsultationEndUtc=ConsultationDate(last,true);
            m.ConsultationDateEvidence=first+" – "+last;
        }
        return m;
    }
    public async Task<object> CountsAsync(CancellationToken cancellation = default) => new
    {
        Contents = await db.Contents.CountAsync(cancellation), SourceRecords = await db.SourceRecords.CountAsync(cancellation),
        Revisions = await db.Revisions.CountAsync(cancellation), Aliases = await db.SourceUrlAliases.CountAsync(cancellation),
        MediaAssets = await db.MediaAssets.CountAsync(cancellation), ContentAssetRelations = await db.ContentAssets.CountAsync(cancellation),
        LegalDocuments = await db.LegalDocuments.CountAsync(cancellation), DraftDocuments = await db.DraftDocuments.CountAsync(cancellation)
    };
    public async Task PersistCrawlAsync(SampleManifest manifest, CancellationToken cancellation)
    {
        var categoryIds = (await db.Categories.Select(x => x.Id).ToListAsync(cancellation)).ToHashSet();
        foreach (var (id, name, slug, path) in SourceCatalog.Categories)
            if (!categoryIds.Contains(id)) db.Categories.Add(new() { Id = id, Name = name, Slug = slug, SourceUrl = SourceCatalog.BaseUrl + path, SortOrder = Array.FindIndex(SourceCatalog.Categories, x => x.Id == id) });
        var run = await db.CrawlRuns.FindAsync([manifest.CrawlRunId], cancellation);
        if (run is null) { run = new() { Id = manifest.CrawlRunId, StartedUtc = manifest.StartedUtc }; db.CrawlRuns.Add(run); }
        run.Status = manifest.Status; run.RequestBudget = manifest.RequestBudget; run.ContentBudget = manifest.ContentBudget;
        run.RequestsUsed = manifest.RequestsUsed; run.EndedUtc = manifest.EndedUtc; run.Checkpoint = $"sample-manifest.json; staged={manifest.Contents.Count}; issues={manifest.Issues.Count}; sourceTotal=Unknown";
        var snapshotKeys = (await db.SourceSnapshots.Where(x => x.CrawlRunId == manifest.CrawlRunId).Select(x => new { x.SourceUrl, x.Sha256 }).ToListAsync(cancellation)).Select(x => x.SourceUrl + "|" + x.Sha256).ToHashSet();
        foreach (var entry in manifest.Snapshots)
            if (snapshotKeys.Add(entry.Url + "|" + entry.Sha256))
                db.SourceSnapshots.Add(new() { CrawlRunId = manifest.CrawlRunId, SourceUrl = entry.Url, StorageKey = entry.StorageKey, Sha256 = entry.Sha256, HttpStatus = entry.Status, ContentType = entry.ContentType, FetchedUtc = entry.FetchedUtc });
        var items = (await db.CrawlItems.Where(x => x.CrawlRunId == manifest.CrawlRunId).ToListAsync(cancellation)).ToDictionary(x => x.SourceUrl);
        foreach (var link in manifest.Discovered)
        {
            var item = items.GetValueOrDefault(link.Url);
            if (item is null) { item = new() { CrawlRunId = manifest.CrawlRunId, SourceUrl = link.Url, SourceId = link.SourceId, Kind = link.Kind }; db.CrawlItems.Add(item); }
            if (item.Status is not ("Imported" or "Verified"))
            {
                item.Status = manifest.Contents.Any(x => x.Source.Kind == link.Kind && x.Source.SourceId == link.SourceId) ? "Staged" : "DiscoveredOutsideSample";
                item.Attempts = item.Status == "Staged" ? 1 : 0;
                if (manifest.ItemStages.TryGetValue(link.Url, out var step))
                { item.Status = step.Stage; item.Attempts = step.Attempts; item.Error = step.Error; item.UpdatedUtc = step.UpdatedUtc; }
            }
        }
        var existingIssues = (await db.ImportIssues.Where(x => x.CrawlRunId == manifest.CrawlRunId).ToListAsync(cancellation)).GroupBy(x => (x.SourceUrl, x.Stage, x.Code)).ToDictionary(x => x.Key, x => x.OrderByDescending(issue => issue.CreatedUtc).ThenByDescending(issue => issue.Id).First());
        foreach (var group in manifest.Issues.GroupBy(x => new { x.SourceUrl, x.Stage, x.Code }))
        {
            var issue = group.Last();
            var existing = existingIssues.GetValueOrDefault((issue.SourceUrl, issue.Stage, issue.Code));
            var message = string.Join(" | ", group.Select(x => (x.Resolved ? "[Resolved in manifest] " : "") + x.Message));
            if (existing is null) db.ImportIssues.Add(new() { CrawlRunId = manifest.CrawlRunId, SourceUrl = issue.SourceUrl, Stage = issue.Stage, Code = issue.Code, Message = message });
            else existing.Message = message;
        }
        await db.SaveChangesAsync(cancellation);
    }
    private async Task SetItemStateAsync(Guid runId, ParsedSource source, string status, CancellationToken cancellation)
    {
        foreach (var item in await db.CrawlItems.Where(x => x.CrawlRunId == runId && x.Kind == source.Kind && x.SourceId == source.SourceId).ToListAsync(cancellation))
        { item.Status = status; item.UpdatedUtc = DateTimeOffset.UtcNow; item.Error = null; }
    }
    private async Task AliasesAsync(Guid id, IEnumerable<string> aliases, CancellationToken cancellation)
    {
        foreach (var url in aliases.Distinct())
        {
            var hash = TextNormalizer.Hash(url);
            var existing = await db.SourceUrlAliases.SingleOrDefaultAsync(x => x.UrlHash == hash, cancellation);
            if (existing is not null && (existing.Url != url || existing.ContentId != id)) throw new InvalidDataException("Source alias collision detected; no overwrite performed.");
            if (existing is null && !db.SourceUrlAliases.Local.Any(x => x.UrlHash == hash)) db.SourceUrlAliases.Add(new() { ContentId = id, Url = url, UrlHash = hash });
        }
    }
    private async Task AddCategoriesAsync(Guid id, IEnumerable<int> categories, CancellationToken cancellation)
    {
        foreach (var category in categories.Distinct())
            if (await db.Categories.AnyAsync(x => x.Id == category, cancellation) && !await db.ContentCategories.AnyAsync(x => x.ContentId == id && x.CategoryId == category, cancellation) && !db.ContentCategories.Local.Any(x => x.ContentId == id && x.CategoryId == category))
                db.ContentCategories.Add(new() { ContentId = id, CategoryId = category });
    }
    private async Task<bool> SourceMetadataMatchesAsync(Guid contentId, StagedContent staged, SampleManifest manifest, CancellationToken cancellation)
    {
        // The historical crawl hash does not include taxonomy or asset selections.
        // Keep that provenance hash, but compare the complete source snapshot too.
        var revision = await db.Revisions.AsNoTracking().Include(x => x.Categories).Include(x => x.Assets)
            .Where(x => x.ContentId == contentId && x.IsSourceRevision).OrderByDescending(x => x.CreatedUtc).ThenByDescending(x => x.Id).FirstOrDefaultAsync(cancellation);
        if (revision is null || revision.ContentHash != staged.ContentHash) return false;
        var actual = revision.Metadata; var expectedMeta = SourceMetadata(staged.Source);
        if (actual.Number != expectedMeta.Number || actual.DocumentType != expectedMeta.DocumentType || actual.IssuingBody != expectedMeta.IssuingBody || actual.Signer != expectedMeta.Signer || actual.Field != expectedMeta.Field || actual.IssuedDate != expectedMeta.IssuedDate || actual.ConsultationStartDate != expectedMeta.ConsultationStartDate || actual.ConsultationEndDate != expectedMeta.ConsultationEndDate || actual.ConsultationStartUtc != expectedMeta.ConsultationStartUtc || actual.ConsultationEndUtc != expectedMeta.ConsultationEndUtc || actual.ConsultationDateEvidence != expectedMeta.ConsultationDateEvidence) return false;
        var categoryIds = staged.Source.CategoryIds.Append(staged.Source.PrimaryCategoryId ?? -1).Distinct().ToArray();
        categoryIds = await db.Categories.Where(x => categoryIds.Contains(x.Id)).Select(x => x.Id).ToArrayAsync(cancellation);
        var primary = staged.Source.PrimaryCategoryId is int cat && categoryIds.Contains(cat) ? (int?)cat : null;
        if (revision.PrimaryCategoryId != primary || !revision.Categories.Select(x => x.CategoryId).Order().SequenceEqual(categoryIds.Order())) return false;
        var expected = new List<(Guid Id, string Usage, int Order)>();
        Guid? cover = null;
        foreach (var id in staged.AssetIds)
        {
            var entry = manifest.Assets.FirstOrDefault(x => x.Id == id);
            if (entry is null) return false;
            var storedId = await db.MediaAssets.Where(x => x.Sha256 == entry.Sha256).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellation);
            if (storedId is null) return false;
            if (entry.MimeType.StartsWith("image/") && cover is null) cover = storedId;
            if (expected.All(x => x.Id != storedId)) expected.Add((storedId.Value, entry.MimeType.StartsWith("image/") ? "Inline" : "Attachment", staged.AssetIds.IndexOf(id)));
        }
        return revision.CoverAssetId == cover && revision.Assets.Count == expected.Count &&
            expected.All(x => revision.Assets.Any(a => a.MediaAssetId == x.Id && a.Usage == x.Usage && a.SortOrder == x.Order && a.Caption == ""));
    }
    private async Task ValidateAssetAsync(AssetEntry entry, CancellationToken cancellation)
    {
        var path = storage.ResolveMedia(entry.StorageKey);
        if (!File.Exists(path) || new FileInfo(path).Length != entry.Bytes) throw new InvalidDataException("Stored asset missing or size mismatch.");
        await using var stream = File.OpenRead(path);
        if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellation)) != entry.Sha256) throw new InvalidDataException("Stored asset hash mismatch.");
    }
    public async Task<object> VerifyAsync(SampleManifest manifest, CancellationToken cancellation = default)
    {
        var errors = new List<string>(); var verified = 0;
        foreach (var staged in manifest.Contents)
        {
            var contentErrorsBefore = errors.Count;
            var record = await db.SourceRecords.SingleOrDefaultAsync(x => x.Source == "qldd.mae.gov.vn" && x.Kind == staged.Source.Kind && x.SourceId == staged.Source.SourceId, cancellation);
            if (record is null) { errors.Add("Content missing: " + staged.Source.Kind + ":" + staged.Source.SourceId); continue; }
            var revision = await db.Revisions.Where(x => x.ContentId == record.ContentId && x.IsSourceRevision && x.ContentHash == staged.ContentHash).OrderByDescending(x => x.CreatedUtc).ThenByDescending(x => x.Id).FirstOrDefaultAsync(cancellation);
            if (revision is null) { errors.Add("Source revision missing: " + staged.Source.SourceId); continue; }
            var rawSnapshot = manifest.Snapshots.LastOrDefault(x => x.Url == staged.Source.SourceUrl && x.Status == 200);
            if (rawSnapshot is null) errors.Add("Raw content snapshot missing: " + staged.Source.SourceId);
            else
            {
                var rawSource = SourceParser.Parse(await File.ReadAllTextAsync(storage.ResolveSnapshot(rawSnapshot.StorageKey), cancellation), staged.Source.SourceUrl, staged.Source.Kind, staged.Source.SourceId);
                string Comparable(string text) => System.Text.RegularExpressions.Regex.Replace(text.Normalize(System.Text.NormalizationForm.FormC), @"\s+", " ").Trim();
                var policy = new HtmlPolicy();
                if (Comparable(policy.PlainText(rawSource.BodyHtml)) != Comparable(policy.PlainText(revision.BodyHtml))) errors.Add("Meaningful source text changed during sanitization: " + staged.Source.SourceId);
            }
            if (RegexHasSourceImage(revision.BodyHtml)) errors.Add("Hotlinked source asset: " + staged.Source.SourceId);
            foreach (var id in staged.AssetIds)
            {
                if (!await db.ContentAssets.AnyAsync(x => x.RevisionId == revision.Id && x.MediaAssetId == id, cancellation)) errors.Add("Content asset relation missing: " + staged.Source.SourceId);
                try { await ValidateAssetAsync(manifest.Assets.First(x => x.Id == id), cancellation); }
                catch (Exception exception) { errors.Add(id + ": " + exception.Message); }
            }
            if (errors.Count == contentErrorsBefore)
            { await SetItemStateAsync(manifest.CrawlRunId, staged.Source, "Verified", cancellation); verified++; }
        }
        foreach (var asset in manifest.Assets.DistinctBy(x => x.Id))
            try { await ValidateAssetAsync(asset, cancellation); }
            catch (Exception exception) { errors.Add(asset.Id + ": " + exception.Message); }
        foreach (var snapshot in manifest.Snapshots)
        {
            var file = storage.ResolveSnapshot(snapshot.StorageKey);
            if (!File.Exists(file)) { errors.Add("Snapshot missing: " + snapshot.StorageKey); continue; }
            await using var stream = File.OpenRead(file);
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellation)) != snapshot.Sha256) errors.Add("Snapshot hash mismatch: " + snapshot.StorageKey);
        }
        await db.SaveChangesAsync(cancellation);
        return new { Status = errors.Count == 0 ? "SampleVerified" : "VerificationFailed", VerifiedContents = verified, UniqueAssets = manifest.Assets.Select(x => x.Id).Distinct().Count(), Snapshots = manifest.Snapshots.Count, Errors = errors, SourceCoverage = "Unknown total; bounded sample", ManifestIssues = manifest.Issues.Count };
    }
    private static bool RegexHasSourceImage(string html) => System.Text.RegularExpressions.Regex.IsMatch(html, "(?:src|href)=\"https?://(?:media-)?qldd\\.mae\\.gov\\.vn/[^\"]*\\.(?:jpg|png|jpeg|pdf|mp4)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static DateOnly? ParseDate(string date) => DateOnly.TryParseExact(date.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    private static DateTimeOffset? ConsultationDate(string text, bool end)
    {
        var date = ParseDate(text); if (date is null) return null;
        return new DateTimeOffset(date.Value.ToDateTime(end ? new TimeOnly(23,59,59) : TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
    }
}
