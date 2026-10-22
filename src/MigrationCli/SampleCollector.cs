using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LandPortal.MigrationCli;
public sealed class SampleCollector(StorageOptions storage, string manifestPath, SampleManifest manifest, bool refresh = false, Func<SampleManifest, Task>? checkpoint = null)
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private SafeSourceClient? client;
    private int requestBaseline;
    private bool offlineMode;
    public async Task RunAsync(int limit, int requestBudget, CancellationToken cancellation)
    {
        manifest.ContentBudget = limit; manifest.RequestBudget = requestBudget; manifest.Status = "Running";
        requestBaseline = manifest.RequestsUsed;
        using var sourceClient = new SafeSourceClient(Math.Max(0, requestBudget - requestBaseline)); client = sourceClient;
        try
        {
            var robots = await SnapshotAsync(SourceCatalog.BaseUrl + "/robots.txt", cancellation);
            if (robots.Status == 200 && Regex.IsMatch(Encoding.UTF8.GetString(robots.Bytes), @"Disallow:\s*/\s*(?:\r?\n|$)", RegexOptions.IgnoreCase))
                throw new InvalidDataException("Source robots disallows all paths; collection stopped.");
            if (robots.Status != 200) AddIssue(robots.Url, "Discover", "ROBOTS_UNAVAILABLE", "robots.txt returned HTTP " + robots.Status + "; no affirmative rules available.");
            var home = await SnapshotAsync(SourceCatalog.BaseUrl + "/", cancellation);
            if (home.Status != 200) throw new HttpRequestException("Source homepage unavailable: HTTP " + home.Status);
            foreach (var category in SourceCatalog.Categories)
            {
                var url = SourceCatalog.BaseUrl + category.Path;
                try
                {
                    var page = await SnapshotAsync(url, cancellation);
                    if (page.Status != 200) { AddIssue(url, "Discover", "LIST_HTTP_ERROR", "HTTP " + page.Status); continue; }
                    var html = Encoding.UTF8.GetString(page.Bytes);
                    var links = SourceParser.DiscoverNewsLinks(html, url, category.Id).ToList();
                    if (links.Count == 0) AddIssue(url, "Discover", "NO_ACCESSIBLE_LIST_CONTENT", "No article links exposed by surveyed source list; whole-category total is unknown. Browser inspection is recorded separately.");
                    manifest.Lists.RemoveAll(x => x.Url == url);
                    manifest.Lists.Add(new(url, category.Id, SourceParser.ReadPagination(html), links.Select(x => x.SourceId).ToList()));
                    foreach (var link in links) if (!manifest.Discovered.Any(x => x.Url == link.Url)) manifest.Discovered.Add(link);
                    // Stratified first-run sample, never a declaration that all category pages were traversed.
                    foreach (var link in links.Take(category.Id == 53 ? 5 : category.Id == 52 ? 2 : category.Id == 79 ? 1 : 2))
                        if (manifest.Contents.Count < limit) await StageAsync(link, cancellation);
                }
                catch (RequestBudgetException) { throw; }
                catch (Exception exception) when (exception is not OperationCanceledException) { AddIssue(url, "Discover", "LIST_ERROR", exception.Message); }
                await SaveAsync();
            }
            var homeHtml = Encoding.UTF8.GetString(home.Bytes);
            var homeDoc = SourceParser.Document(homeHtml);
            foreach (var section in new[] { ("/danh-sach-van-ban.htm", ContentKind.LegalDocument, @"/van-ban-(\d+)\.htm", 3), ("/danh-sach-van-ban-du-thao.htm", ContentKind.DraftDocument, @"/chi-tiet-van-ban-du-thao-(\d+)\.htm", 2), ("/video.htm", ContentKind.Video, @"/video-(\d+)\.htm", 2) })
            {
                var url = SourceCatalog.BaseUrl + section.Item1;
                var page = await SnapshotAsync(url, cancellation);
                if (page.Status != 200) { AddIssue(url, "Discover", "LIST_HTTP_ERROR", "HTTP " + page.Status); continue; }
                var html = Encoding.UTF8.GetString(page.Bytes);
                var ids = Regex.Matches(html, section.Item3).Select(m => m.Groups[1].Value).Distinct().ToList();
                if (section.Item2 is ContentKind.LegalDocument or ContentKind.DraftDocument) ids = SourceParser.EmbeddedDocumentIds(html);
                if (section.Item2 == ContentKind.Video)
                    ids = Regex.Matches(html, "class=\"i-img\" id=\"(\\d+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
                manifest.Lists.RemoveAll(x => x.Url == url);
                manifest.Lists.Add(new(url, null, SourceParser.ReadPagination(html), ids));
                foreach (var id in ids)
                {
                    var path = section.Item2 == ContentKind.LegalDocument ? "/van-ban-" : section.Item2 == ContentKind.DraftDocument ? "/chi-tiet-van-ban-du-thao-" : "/video-";
                    var link = new DiscoveredLink(SourceCatalog.BaseUrl + path + id + ".htm", id, section.Item2);
                    if (!manifest.Discovered.Any(x => x.Url == link.Url)) manifest.Discovered.Add(link);
                }
                foreach (var link in manifest.Discovered.Where(x => x.Kind == section.Item2).Take(section.Item4))
                    if (manifest.Contents.Count < limit) await StageAsync(link, cancellation);
            }
            foreach (var path in new[] { ("/lanh-dao-cuc-1780.htm", "1780"), ("/chuc-nang-nhiem-vu-3049.htm", "3049") })
                if (manifest.Contents.Count < limit) await StageAsync(new(SourceCatalog.BaseUrl + path.Item1, path.Item2, ContentKind.StaticPage), cancellation);
            // Source category pagination boundaries verified without guessing a next-page scheme.
            var list75 = manifest.Lists.FirstOrDefault(x => x.CategoryId == 75);
            if (list75 is not null && list75.Pagination.TotalPages > 1 && list75.Pagination.NextTemplate.Length > 0)
                foreach (var pageNumber in new[] { 2, list75.Pagination.TotalPages }.Distinct())
                {
                    var url = SourceCatalog.BaseUrl + list75.Pagination.NextTemplate.Replace("{page}", pageNumber.ToString());
                    var response = await SnapshotAsync(url, cancellation);
                    var html = Encoding.UTF8.GetString(response.Bytes);
                    var links = SourceParser.DiscoverNewsLinks(html, url, 75).ToList();
                    manifest.Lists.RemoveAll(x => x.Url == url);
                    manifest.Lists.Add(new(url, 75, SourceParser.ReadPagination(html), links.Select(x => x.SourceId).ToList()));
                    if (!links.Select(x => x.SourceId).Except(list75.SourceIds).Any()) AddIssue(url, "Discover", "PAGINATION_REPEATS_FIRST_PAGE", "Next-page source IDs were not distinct from first page.");
                }
            var legalPage2 = SourceCatalog.BaseUrl + "/danh-sach-van-ban.htm?Page=2";
            var legalResponse = await SnapshotAsync(legalPage2, cancellation);
            var legalIds = SourceParser.EmbeddedDocumentIds(Encoding.UTF8.GetString(legalResponse.Bytes));
            manifest.Lists.RemoveAll(x => x.Url == legalPage2);
            manifest.Lists.Add(new(legalPage2, null, SourceParser.ReadPagination(Encoding.UTF8.GetString(legalResponse.Bytes)), legalIds));
            // A content budget is intentionally a paused sample, not completion of source migration.
            manifest.Status = "PausedByBudget";
        }
        catch (RequestBudgetException) { manifest.Status = "PausedByBudget"; }
        catch (OperationCanceledException) { manifest.Status = "Cancelled"; }
        catch (Exception exception) { manifest.Status = "Failed"; AddIssue(SourceCatalog.BaseUrl, "Fetch", "RUN_ERROR", exception.Message); }
        finally { manifest.RequestsUsed = requestBaseline + sourceClient.RequestsUsed; manifest.EndedUtc = DateTimeOffset.UtcNow; await SaveAsync(); }
    }
    public async Task RepairAssetsAsync(int requestBudget, CancellationToken cancellation)
    {
        manifest.RequestBudget = requestBudget; requestBaseline = manifest.RequestsUsed;
        using var sourceClient = new SafeSourceClient(Math.Max(0, requestBudget - requestBaseline)); client = sourceClient;
        manifest.Status = "Running";
        try
        {
            foreach (var staged in manifest.Contents.ToArray()) await StageAsync(new(staged.Source.SourceUrl, staged.Source.SourceId, staged.Source.Kind), cancellation, true);
            foreach (var category in SourceCatalog.Categories.Where(x => x.Id is 77 or 76))
            {
                var url = SourceCatalog.BaseUrl + category.Path; var response = await SnapshotAsync(url, cancellation);
                var html = Encoding.UTF8.GetString(response.Bytes); var links = SourceParser.DiscoverNewsLinks(html, url, category.Id).ToList();
                manifest.Lists.RemoveAll(x => x.Url == url); manifest.Lists.Add(new(url, category.Id, SourceParser.ReadPagination(html), links.Select(x => x.SourceId).ToList()));
                foreach (var link in links) if (!manifest.Discovered.Any(x => x.Url == link.Url)) manifest.Discovered.Add(link);
            }
            foreach (var section in new[] { ("/danh-sach-van-ban.htm", ContentKind.LegalDocument, 3), ("/danh-sach-van-ban-du-thao.htm", ContentKind.DraftDocument, 2) })
            {
                var url = SourceCatalog.BaseUrl + section.Item1; var response = await SnapshotAsync(url, cancellation);
                var html = Encoding.UTF8.GetString(response.Bytes); var ids = SourceParser.EmbeddedDocumentIds(html);
                manifest.Lists.RemoveAll(x => x.Url == url); manifest.Lists.Add(new(url, null, SourceParser.ReadPagination(html), ids));
                foreach (var id in ids)
                {
                    var link = new DiscoveredLink(SourceCatalog.BaseUrl + (section.Item2 == ContentKind.LegalDocument ? "/van-ban-" : "/chi-tiet-van-ban-du-thao-") + id + ".htm", id, section.Item2);
                    if (!manifest.Discovered.Any(x => x.Url == link.Url)) manifest.Discovered.Add(link);
                    if (manifest.Contents.Count(x => x.Source.Kind == section.Item2) < section.Item3 && manifest.Contents.Count < 50) await StageAsync(link, cancellation);
                }
            }
            var legalPage2Url = SourceCatalog.BaseUrl + "/danh-sach-van-ban.htm?Page=2";
            var legalPage2 = await SnapshotAsync(legalPage2Url, cancellation);
            var page2Html = Encoding.UTF8.GetString(legalPage2.Bytes);
            manifest.Lists.RemoveAll(x => x.Url == legalPage2Url); manifest.Lists.Add(new(legalPage2Url, null, SourceParser.ReadPagination(page2Html), SourceParser.EmbeddedDocumentIds(page2Html)));
            var directiveUrl = SourceCatalog.BaseUrl + "/van-ban-chi-dao-dieu-hanh.htm";
            var directivePage = await SnapshotAsync(directiveUrl, cancellation);
            if (directivePage.Status != 200) AddIssue(directiveUrl, "Discover", "DIRECTIVE_LIST_HTTP_ERROR", "HTTP " + directivePage.Status + "; adapter not implemented.");
            // Preserve official source branding as a downloaded local asset, distinct from article migration counts.
            foreach (var brandUrl in new[] { SourceCatalog.BaseUrl + "/noidung/Lists/Banner/Attachments/12/Banner_header_cuc_QLdatdai.png", "https://media-qldd.mae.gov.vn/Image/15de68f5-850e-d5ab-10c4-cc7e86915f81/2025/5/22/Banner_header_cuc_QLdatdai_f37c6f2624.png", SourceCatalog.BaseUrl + "/Content/publishing_home/img_moj/logo.png" })
            {
                try { await DownloadAsync(brandUrl, cancellation); }
                catch (RequestBudgetException) { throw; }
                catch (Exception exception) when (exception is not OperationCanceledException) { AddIssue(brandUrl, "DownloadAssets", "BRANDING_ASSET_ERROR", exception.Message); }
            }
            manifest.Status = "PausedByBudget";
        }
        catch (RequestBudgetException) { manifest.Status = "PausedByBudget"; }
        catch (OperationCanceledException) { manifest.Status = "Cancelled"; }
        catch (Exception exception) { manifest.Status = "Failed"; AddIssue(SourceCatalog.BaseUrl, "RepairAssets", "RUN_ERROR", exception.Message); }
        finally { manifest.RequestsUsed = requestBaseline + sourceClient.RequestsUsed; manifest.EndedUtc = DateTimeOffset.UtcNow; await SaveAsync(); }
    }
    public async Task NormalizeOfflineAsync(CancellationToken cancellation)
    {
        offlineMode = true; manifest.Status = "Running";
        try
        {
            await CompleteDiscoveryFromSavedListsAsync(cancellation);
            foreach (var staged in manifest.Contents.ToArray())
                await StageAsync(new(staged.Source.SourceUrl, staged.Source.SourceId, staged.Source.Kind), cancellation, true);
            manifest.Status = "PausedByBudget";
        }
        catch (OperationCanceledException) { manifest.Status = "Cancelled"; }
        finally { await SaveAsync(); }
    }
    private async Task CompleteDiscoveryFromSavedListsAsync(CancellationToken cancellation)
    {
        foreach (var list in manifest.Lists)
        {
            var snapshot = manifest.Snapshots.LastOrDefault(x => x.Url == list.Url && x.Status == 200);
            if (snapshot is null) continue;
            var html = await File.ReadAllTextAsync(storage.ResolveSnapshot(snapshot.StorageKey), cancellation);
            IEnumerable<DiscoveredLink> links = list.CategoryId is int category ? SourceParser.DiscoverNewsLinks(html, list.Url, category) :
                list.Url.Contains("/danh-sach-van-ban", StringComparison.Ordinal) ? SourceParser.EmbeddedDocumentIds(html).Select(id => new DiscoveredLink(SourceCatalog.BaseUrl + (list.Url.Contains("du-thao", StringComparison.Ordinal) ? "/chi-tiet-van-ban-du-thao-" : "/van-ban-") + id + ".htm", id, list.Url.Contains("du-thao", StringComparison.Ordinal) ? ContentKind.DraftDocument : ContentKind.LegalDocument)) : [];
            foreach (var link in links) if (!manifest.Discovered.Any(x => x.Url == link.Url)) manifest.Discovered.Add(link);
        }
    }
    private async Task StageAsync(DiscoveredLink discovered, CancellationToken cancellation, bool force = false)
    {
        if (!manifest.Discovered.Any(x => x.Url == discovered.Url)) manifest.Discovered.Add(discovered);
        if (!force && manifest.Contents.Any(x => x.Source.Kind == discovered.Kind && x.Source.SourceId == discovered.SourceId && !refresh)) return;
        try
        {
            var attempts = manifest.ItemStages.GetValueOrDefault(discovered.Url)?.Attempts ?? 0;
            manifest.ItemStages[discovered.Url] = new("Fetching", attempts + 1, DateTimeOffset.UtcNow);
            await SaveAsync();
            var response = await SnapshotAsync(discovered.Url, cancellation);
            if (response.Status != 200) throw new HttpRequestException("Content HTTP " + response.Status);
            manifest.ItemStages[discovered.Url] = new("Snapshot", attempts + 1, DateTimeOffset.UtcNow);
            await SaveAsync();
            var parsed = SourceParser.Parse(Encoding.UTF8.GetString(response.Bytes), discovered.Url, discovered.Kind, discovered.SourceId);
            if (discovered.CategoryId is int category && !parsed.CategoryIds.Contains(category)) parsed.CategoryIds.Add(category);
            // Primary category comes from the article back-link, not the sidebar or guessed URL slug.
            var prior = manifest.Contents.FirstOrDefault(x => x.Source.Kind == parsed.Kind && x.Source.SourceId == parsed.SourceId);
            if (prior is not null && !refresh && !force)
            {
                if (!prior.Aliases.Contains(discovered.Url)) prior.Aliases.Add(discovered.Url);
                foreach (var cat in parsed.CategoryIds) if (!prior.Source.CategoryIds.Contains(cat)) prior.Source.CategoryIds.Add(cat);
                return;
            }
            var staged = new StagedContent { Source = parsed, Aliases = [discovered.Url, parsed.CanonicalUrl] };
            if (prior is not null)
            {
                staged.Aliases = staged.Aliases.Concat(prior.Aliases).Distinct().ToList();
                foreach (var cat in prior.Source.CategoryIds) if (!parsed.CategoryIds.Contains(cat)) parsed.CategoryIds.Add(cat);
            }
            for (var i = 0; i < manifest.Issues.Count; i++)
                if (manifest.Issues[i].SourceUrl == discovered.Url && manifest.Issues[i].Code == "EMPTY_BODY" && !parsed.Warnings.Contains("EMPTY_BODY")) manifest.Issues[i] = manifest.Issues[i] with { Resolved = true };
            manifest.ItemStages[discovered.Url] = new("Extracted", attempts + 1, DateTimeOffset.UtcNow);
            await SaveAsync();
            foreach (var warning in parsed.Warnings) AddIssue(discovered.Url, "Extract", warning.Split(':')[0], warning);
            foreach (var assetUrl in parsed.AssetUrls)
            {
                if (offlineMode && !manifest.Assets.Any(x => x.SourceUrl == assetUrl && File.Exists(storage.ResolveMedia(x.StorageKey)))) continue;
                try
                {
                    manifest.ItemStages[discovered.Url] = new("DownloadAssets", attempts + 1, DateTimeOffset.UtcNow);
                    var asset = await DownloadAsync(assetUrl, cancellation);
                    if (!staged.AssetIds.Contains(asset.Id)) staged.AssetIds.Add(asset.Id);
                }
                catch (RequestBudgetException) { throw; }
                catch (Exception exception) when (exception is not OperationCanceledException) { AddIssue(assetUrl, "DownloadAssets", "ASSET_ERROR", exception.Message); }
            }
            var body = SourceParser.Document(parsed.BodyHtml);
            foreach (var element in body.QuerySelectorAll("[src],[href],[data-src],[srcset]"))
            {
                foreach (var attribute in new[] { "src", "href", "data-src" })
                {
                    var raw = element.GetAttribute(attribute);
                    if (raw is null || !Uri.TryCreate(new Uri(discovered.Url), raw, out var target)) continue;
                    var asset = manifest.Assets.FirstOrDefault(x => x.SourceUrl == target.AbsoluteUri);
                    if (asset is not null) element.SetAttribute(attribute == "data-src" ? "src" : attribute, "/media/" + asset.Id);
                    else if (attribute is "src" or "data-src") { element.RemoveAttribute(attribute); element.SetAttribute("alt", element.GetAttribute("alt") ?? "Tài nguyên nguồn chưa chuyển được"); }
                    else if (SafeSourceClient.AllowedHosts.Contains(target.Host) && Regex.IsMatch(target.AbsolutePath, @"\.(pdf|docx?|xlsx?|mp4|zip)$", RegexOptions.IgnoreCase)) element.RemoveAttribute(attribute);
                }
                element.RemoveAttribute("srcset"); element.RemoveAttribute("data-src");
            }
            var policy = new HtmlPolicy();
            parsed.BodyHtml = policy.Clean(body.Body?.InnerHtml ?? "");
            manifest.ItemStages[discovered.Url] = new("Normalized", attempts + 1, DateTimeOffset.UtcNow);
            await SaveAsync();
            staged.ContentHash = TextNormalizer.Hash(string.Join("\n", parsed.Title, parsed.Summary, parsed.BodyHtml, parsed.Author, parsed.OriginalDateText));
            manifest.ItemStages[discovered.Url] = new("Validated", attempts + 1, DateTimeOffset.UtcNow);
            await SaveAsync();
            if (prior is not null) manifest.Contents.Remove(prior);
            manifest.Contents.Add(staged);
            manifest.ItemStages[discovered.Url] = new("Staged", attempts + 1, DateTimeOffset.UtcNow);
            await SaveAsync();
            Console.WriteLine($"Staged {parsed.Kind} source={parsed.SourceId}; contents={manifest.Contents.Count}; assets={staged.AssetIds.Count}");
        }
        catch (RequestBudgetException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AddIssue(discovered.Url, "Extract", "CONTENT_ERROR", exception.Message);
            manifest.ItemStages[discovered.Url] = new("Failed", manifest.ItemStages.GetValueOrDefault(discovered.Url)?.Attempts ?? 1, DateTimeOffset.UtcNow, exception.Message);
            await SaveAsync();
        }
    }
    private async Task<SourceResponse> SnapshotAsync(string url, CancellationToken cancellation)
    {
        if (new Uri(url).Host != "qldd.mae.gov.vn") throw new InvalidDataException("Page crawl is restricted to qldd.mae.gov.vn; media allowlist does not authorize external pages.");
        var cached = manifest.Snapshots.LastOrDefault(x => x.Url == url);
        if (!refresh && cached is not null && File.Exists(storage.ResolveSnapshot(cached.StorageKey)))
            return new(cached.FinalUrl, cached.Status, cached.ContentType, await File.ReadAllBytesAsync(storage.ResolveSnapshot(cached.StorageKey), cancellation), cached.FetchedUtc);
        // Reuse the separately captured initial survey to avoid unnecessary repeated source requests.
        var surveyFile = storage.ResolveSnapshot("survey/survey-manifest.json");
        if (!refresh && File.Exists(surveyFile))
        {
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(surveyFile, cancellation));
            foreach (var entry in json.RootElement.EnumerateArray()) if (entry.GetProperty("url").GetString() == url && entry.TryGetProperty("path", out var path))
            {
                var name = Path.GetFileName(path.GetString());
                var key = "survey/" + name;
                var bytes = await File.ReadAllBytesAsync(storage.ResolveSnapshot(key), cancellation);
                var response = new SourceResponse(entry.GetProperty("finalUrl").GetString() ?? url, entry.GetProperty("status").GetInt32(), "text/html", bytes, File.GetLastWriteTimeUtc(storage.ResolveSnapshot(key)));
                manifest.Snapshots.Add(new(url, response.Url, response.Status, response.ContentType, key, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length, response.FetchedUtc));
                return response;
            }
        }
        if (!refresh && url == SourceCatalog.BaseUrl + "/" && File.Exists(storage.ResolveSnapshot("survey/home.html")))
        {
            var bytes = await File.ReadAllBytesAsync(storage.ResolveSnapshot("survey/home.html"), cancellation);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            manifest.Snapshots.Add(new(url, url, 200, "text/html", "survey/home.html", hash, bytes.Length, File.GetLastWriteTimeUtc(storage.ResolveSnapshot("survey/home.html"))));
            return new(url, 200, "text/html", bytes, File.GetLastWriteTimeUtc(storage.ResolveSnapshot("survey/home.html")));
        }
        if (offlineMode) throw new InvalidDataException("Offline normalization requires an existing raw source snapshot; no HTTP fallback.");
        var fetched = await client!.FetchAsync(url, cancellation: cancellation, pagesOnly: true);
        var sha = Convert.ToHexStringLower(SHA256.HashData(fetched.Bytes));
        var storageKey = "html/" + sha + ".html";
        var file = storage.ResolveSnapshot(storageKey); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (!File.Exists(file)) await File.WriteAllBytesAsync(file, fetched.Bytes, cancellation);
        manifest.Snapshots.Add(new(url, fetched.Url, fetched.Status, fetched.ContentType, storageKey, sha, fetched.Bytes.Length, fetched.FetchedUtc));
        return fetched;
    }
    private async Task<AssetEntry> DownloadAsync(string url, CancellationToken cancellation)
    {
        var known = manifest.Assets.FirstOrDefault(x => x.SourceUrl == url);
        if (known is not null && File.Exists(storage.ResolveMedia(known.StorageKey))) return known;
        var response = await client!.FetchAsync(url, 32 * 1024 * 1024, cancellation);
        if (response.Status != 200) throw new HttpRequestException("Asset HTTP " + response.Status);
        var type = DetectType(response.Bytes);
        if (type is null)
        {
            var rejectedSha = Convert.ToHexStringLower(SHA256.HashData(response.Bytes));
            var rejectedKey = "rejected/" + rejectedSha + ".bin"; var rejectedFile = storage.ResolveSnapshot(rejectedKey);
            Directory.CreateDirectory(Path.GetDirectoryName(rejectedFile)!);
            if (!File.Exists(rejectedFile)) await File.WriteAllBytesAsync(rejectedFile, response.Bytes, cancellation);
            manifest.Snapshots.Add(new(url, response.Url, response.Status, response.ContentType, rejectedKey, rejectedSha, response.Bytes.Length, response.FetchedUtc));
            throw new InvalidDataException("Asset signature unsupported or unsafe (HTML/SVG/error response refused); private response snapshot retained.");
        }
        var sha = Convert.ToHexStringLower(SHA256.HashData(response.Bytes));
        var id = new Guid(SHA256.HashData(response.Bytes).AsSpan(0, 16));
        var storageKey = sha[..2] + "/" + sha + type.Value.Extension;
        var file = storage.ResolveMedia(storageKey); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (!File.Exists(file)) { var temp = file + ".partial"; await File.WriteAllBytesAsync(temp, response.Bytes, cancellation); File.Move(temp, file); }
        var asset = new AssetEntry(id, url, storageKey, sha, response.Bytes.Length, type.Value.Mime, Path.GetFileName(Uri.UnescapeDataString(new Uri(url).AbsolutePath)));
        for (var i = 0; i < manifest.Issues.Count; i++) if (manifest.Issues[i].Stage == "DownloadAssets" && manifest.Issues[i].SourceUrl == url) manifest.Issues[i] = manifest.Issues[i] with { Resolved = true };
        manifest.Assets.Add(asset); await SaveAsync(); return asset;
    }
    public static (string Mime, string Extension)? DetectType(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith("%PDF-"u8)) return ("application/pdf", ".pdf");
        if (bytes.Length > 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 })) return ("image/png", ".png");
        if (bytes.Length > 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return ("image/jpeg", ".jpg");
        if (bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8)) return ("image/gif", ".gif");
        if (bytes.Length > 12 && bytes.AsSpan(0,4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8,4).SequenceEqual("WEBP"u8)) return ("image/webp", ".webp");
        if (bytes.Length > 12 && bytes.AsSpan(4,4).SequenceEqual("ftyp"u8))
        {
            var brand = Encoding.ASCII.GetString(bytes, 8, 4);
            if (brand is "avif" or "avis") return ("image/avif", ".avif");
            if (brand is "isom" or "iso2" or "mp41" or "mp42" or "M4V " or "avc1") return ("video/mp4", ".mp4");
        }
        return null;
    }
    private void AddIssue(string url, string stage, string code, string message)
    { if (!manifest.Issues.Any(x => x.SourceUrl == url && x.Stage == stage && x.Code == code && x.Message == message)) manifest.Issues.Add(new(url, stage, code, message)); }
    public async Task SaveAsync()
    {
        if (client is not null) manifest.RequestsUsed = requestBaseline + client.RequestsUsed;
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var temp = manifestPath + ".partial";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
        File.Move(temp, manifestPath, true);
        if (checkpoint is not null) await checkpoint(manifest);
    }
}
