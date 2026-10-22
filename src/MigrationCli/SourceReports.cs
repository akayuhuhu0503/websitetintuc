using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace LandPortal.MigrationCli;
public static class SourceReports
{
    public static async Task WriteAsync(SampleManifest manifest, StorageOptions storage, PortalDbContext db, string folder, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(folder);
        var records = await db.SourceRecords.AsNoTracking().Where(x => x.Source == "qldd.mae.gov.vn").Select(x => new { x.Kind, x.SourceId, x.ContentId }).ToListAsync(cancellation);
        var imported = records.Select(x => (x.Kind, x.SourceId)).ToHashSet();
        var diffs = new List<object>();
        foreach (var content in manifest.Contents)
        {
            var source = content.Source;
            var snapshot = manifest.Snapshots.LastOrDefault(x => x.Url == source.SourceUrl && x.Status == 200);
            if (snapshot is null) { diffs.Add(new { source.Kind, source.SourceId, Status = "RAW_SNAPSHOT_MISSING" }); continue; }
            try
            {
                var raw = SourceParser.Parse(await File.ReadAllTextAsync(storage.ResolveSnapshot(snapshot.StorageKey), cancellation), source.SourceUrl, source.Kind, source.SourceId);
                var before = SourceParser.Document(raw.BodyHtml); var after = SourceParser.Document(source.BodyHtml);
                var sourceText = ComparableText(before.Body?.TextContent); var importedText = ComparableText(after.Body?.TextContent);
                var missing = raw.AssetUrls.Where(url => !manifest.Assets.Any(a => a.SourceUrl == url && content.AssetIds.Contains(a.Id))).ToArray();
                diffs.Add(new { Kind = source.Kind.ToString(), source.SourceId, source.SourceUrl, RawSnapshot = snapshot.StorageKey, TextBeforeCharacters = sourceText.Length,
                    TextAfterCharacters = importedText.Length, MeaningfulTextEqualAfterWhitespaceNormalization = sourceText == importedText,
                    ImagesBefore = before.QuerySelectorAll("img").Length, ImagesAfter = after.QuerySelectorAll("img").Length,
                    TablesBefore = before.QuerySelectorAll("table").Length, TablesAfter = after.QuerySelectorAll("table").Length,
                    AttachmentsBefore = raw.AssetUrls.Count(x => new Uri(x).AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)),
                    ExpectedAssetUrls = raw.AssetUrls.Count, MissingAssetUrls = missing,
                    ActiveElementsAfter = after.QuerySelectorAll("script,style,iframe,object,embed,form").Length,
                    RemoteImagesAfter = after.QuerySelectorAll("img[src]").Count(x => !(x.GetAttribute("src") ?? "").StartsWith("/media/")),
                    Explanation = "Sanitization strips active/unsupported attributes and source formatting; local asset references replace downloaded URLs. Text comparison normalizes whitespace and Unicode composition only, never display." });
            }
            catch (Exception exception) { diffs.Add(new { Kind = source.Kind.ToString(), source.SourceId, Status = "DIFF_ERROR", Error = exception.Message }); }
        }
        var assetUrls = manifest.Contents.SelectMany(x => x.Source.AssetUrls).Distinct().ToArray();
        var unresolvedAssets = assetUrls.Where(url => !manifest.Assets.Any(x => x.SourceUrl == url)).ToArray();
        var history = manifest.Issues.Select(x => new { x.SourceUrl, x.Stage, x.Code, x.Message, Resolved = x.Resolved || x.Stage == "DownloadAssets" && manifest.Assets.Any(a => a.SourceUrl == x.SourceUrl) }).ToArray();
        var categories = SourceCatalog.Categories.Select(cat => new
        {
            SourceCategoryId = cat.Id, cat.Name, SourceUrl = SourceCatalog.BaseUrl + cat.Path,
            SurveyedListPages = manifest.Lists.Count(x => x.CategoryId == cat.Id),
            DistinctSourceIdsInSurveyedLists = manifest.Lists.Where(x => x.CategoryId == cat.Id).SelectMany(x => x.SourceIds).Distinct().Count(),
            DiscoveredUrls = manifest.Discovered.Count(x => x.CategoryId == cat.Id),
            Staged = manifest.Contents.Count(x => x.Source.CategoryIds.Contains(cat.Id)),
            Imported = manifest.Contents.Count(x => x.Source.CategoryIds.Contains(cat.Id) && imported.Contains((x.Source.Kind, x.Source.SourceId))),
            SourceCategoryTotal = (int?)null
        }).ToArray();
        var kinds = Enum.GetValues<ContentKind>().Select(kind => new
        {
            Kind = kind.ToString(), DiscoveredUrls = manifest.Discovered.Count(x => x.Kind == kind),
            UniqueDiscoveredIdentities = manifest.Discovered.Where(x => x.Kind == kind).Select(x => x.SourceId).Distinct().Count(),
            Staged = manifest.Contents.Count(x => x.Source.Kind == kind),
            Imported = manifest.Contents.Count(x => x.Source.Kind == kind && imported.Contains((kind, x.Source.SourceId))),
            WholeSourceTotal = (int?)null
        }).ToArray();
        var sqlAssets = await db.MediaAssets.AsNoTracking().CountAsync(cancellation);
        var sourceAssetIds = manifest.Contents.SelectMany(x => x.AssetIds).Distinct().ToHashSet();
        var report = new
        {
            SchemaVersion = 1, GeneratedUtc = DateTimeOffset.UtcNow, Scope = "Phase 0–1 bounded real sample; not full migration", manifest.CrawlRunId, manifest.Status,
            Discovery = new { DiscoveredUrls = manifest.Discovered.Select(x => x.Url).Distinct().Count(), UniqueIdentities = manifest.Discovered.Select(x => (x.Kind, x.SourceId)).Distinct().Count(), SurveyedLists = manifest.Lists.Count, WholeSourceTotal = (int?)null, WholeSourceCoverage = "Unknown" },
            Processing = new { Staged = manifest.Contents.Count, Imported = manifest.Contents.Count(x => imported.Contains((x.Source.Kind, x.Source.SourceId))), ContentErrors = manifest.Issues.Count(x => x.Code == "CONTENT_ERROR" && !x.Resolved), ByKind = kinds, ByCategory = categories },
            Resources = new { ExpectedDistinctAssetUrlsInStagedContent = assetUrls.Length, DownloadedAssetUrlAliases = manifest.Assets.Count,
                UniqueDownloadedFiles = manifest.Assets.Select(x => x.Id).Distinct().Count(), AssociatedContentFiles = sourceAssetIds.Count,
                BrandingOrOtherUnassociatedFiles = manifest.Assets.Where(x => !sourceAssetIds.Contains(x.Id)).Select(x => x.Id).Distinct().Count(), SqlMediaAssets = sqlAssets,
                BytesDownloadedUniqueFiles = manifest.Assets.DistinctBy(x => x.Id).Sum(x => x.Bytes), OutstandingAssetUrls = unresolvedAssets,
                SourceVideosWithoutTransferredFile = manifest.Contents.Where(x => x.Source.Kind == ContentKind.Video && x.Source.Warnings.Any(w => w.StartsWith("VIDEO_FILE_UNAVAILABLE"))).Select(x => x.Source.SourceId).ToArray(),
                MalwareScan = "Not configured; all source sample files retain NotScanned. Production security acceptance blocked." },
            Budget = new { manifest.RequestBudget, manifest.RequestsUsed, manifest.ContentBudget, Reason = manifest.Status == "PausedByBudget" ? "Bounded sample/checkpoint; not source completion" : manifest.Status },
            Issues = new { HistoricalEntries = history.Length, ResolvedEntries = history.Count(x => x.Resolved), OutstandingEntries = history.Count(x => !x.Resolved), Details = history },
            Sanitization = diffs,
            PaginationEvidence = manifest.Lists
        };
        await File.WriteAllTextAsync(Path.Combine(folder, "source-report.json"), JsonSerializer.Serialize(report, SampleCollector.JsonOptions), new UTF8Encoding(false), cancellation);
        var csv = new StringBuilder("kind,source_id,source_url,title,category_ids,staged_assets,expected_asset_urls,sql_imported,warnings\n");
        foreach (var item in manifest.Contents)
            csv.AppendLine(string.Join(",", new[] { item.Source.Kind.ToString(), item.Source.SourceId, item.Source.SourceUrl, item.Source.Title, string.Join(";", item.Source.CategoryIds), item.AssetIds.Count.ToString(), item.Source.AssetUrls.Count.ToString(), imported.Contains((item.Source.Kind, item.Source.SourceId)).ToString(), string.Join(";", item.Source.Warnings) }.Select(Csv)));
        await File.WriteAllTextAsync(Path.Combine(folder, "source-content-report.csv"), csv.ToString(), new UTF8Encoding(true), cancellation);
        await File.WriteAllTextAsync(Path.Combine(folder, "source-sanitization-report.json"), JsonSerializer.Serialize(diffs, SampleCollector.JsonOptions), new UTF8Encoding(false), cancellation);
    }
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string ComparableText(string? text) => System.Text.RegularExpressions.Regex.Replace((text ?? "").Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
}
