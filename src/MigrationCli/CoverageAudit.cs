using System.Text;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;

namespace LandPortal.MigrationCli;

public sealed record CandidateCoverage(string SourceIdentity, string Status, string Reason, string[] Urls,
    int[] Categories, string[] CaptureHashes, string[] Errors);
public sealed record CategoryCoverage(int Id, int Candidates, int Verified, int Pending, int[] ObservedPages,
    int DeclaredPages, string[] Warnings);
public sealed record SourceCoverageReport(int Urls, int CandidateIdentities, int VerifiedItems,
    int CandidateVerified, int PendingCandidates, int NewVerified, int RequestsReserved, string Approval,
    CandidateCoverage[] Candidates, CategoryCoverage[] Categories, object[] OfflineExtraction,
    string TotalSource = "UNKNOWN", string OnlineCollection = "NEEDS_APPROVAL");

public static class CoverageAudit
{
    public static async Task<SourceCoverageReport> InspectAsync(string workspace, InventoryState state)
    {
        var verified = state.Items.ToDictionary(x => x.Source.Kind + ":" + x.Source.SourceId);
        var aliases = state.Items.SelectMany(x => x.Aliases.Select(a => (Url: a, Item: x))).GroupBy(x => x.Url).ToDictionary(x => x.Key, x => x.First().Item);
        var rawRoot = Path.Combine(workspace, ".local/phase3a", state.Id.ToString("N"));
        var rows = new List<CandidateCoverage>(); var extraction = new List<object>();
        var newlyVerified = new HashSet<string>();
        foreach (var group in state.Urls.Where(x => x.Role == "Detail" && x.CandidateId.Length > 0).GroupBy(x => x.Kind + ":" + x.CandidateId).OrderBy(x => x.Key))
        {
            var nodes = group.ToArray(); var captures = state.Captures.Where(x => nodes.Any(u => u.Url == x.Url) && x.Status == 200).ToArray();
            var existing = verified.ContainsKey(group.Key) || nodes.Any(x => aliases.ContainsKey(x.Url));
            var reason = existing ? "VERIFIED_ITEM_EXISTS" : captures.Length == 0 ? "NO_DETAIL_SNAPSHOT_BUDGET_OR_NOT_FETCHED" : "EXTRACTION_NEEDS_REVIEW";
            foreach (var capture in captures.GroupBy(x => x.Sha256).Select(x => x.First()))
            {
                var full = StorageOptions.ResolveWithin(rawRoot, capture.Key); SourceInventory.NoReparse(full);
                var bytes = await File.ReadAllBytesAsync(full);
                if (bytes.LongLength != capture.Bytes || SourceInventory.Hash(bytes) != capture.Sha256) throw new InvalidDataException("Coverage audit raw snapshot differs from SHA/bytes.");
                if (existing) continue;
                try
                {
                    var node = nodes.First(x => x.Url == capture.Url);
                    var parsed = SourceParser.Parse(new UTF8Encoding(false, true).GetString(bytes), node.Url, node.Kind, node.CandidateId);
                    var canonical = InventoryDiscovery.Classify(parsed.CanonicalUrl, node.Url);
                    var canonicalIdentity = parsed.Kind + ":" + parsed.SourceId;
                    if (canonical is not { Role: "Detail" } || !parsed.CanonicalUrl.Contains("ItemID=", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Canonical ItemID not established.");
                    reason = verified.ContainsKey(canonicalIdentity) ? "CANONICAL_ALIAS_OF_VERIFIED_ITEM" : "OFFLINE_PARSED_NEEDS_STAGING_MEDIA_REVIEW";
                    if (!verified.ContainsKey(canonicalIdentity)) newlyVerified.Add(canonicalIdentity);
                    extraction.Add(new { Candidate = group.Key, CanonicalIdentity = canonicalIdentity, capture.Sha256, Status = reason, parsed.Title, RequiredAssets = parsed.AssetUrls });
                }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or JsonException)
                { extraction.Add(new { Candidate = group.Key, capture.Sha256, Status = "PARSE_BLOCKED", Error = ex.Message }); }
            }
            rows.Add(new(group.Key, existing ? "VERIFIED" : "PENDING", reason, nodes.Select(x => x.Url).Order().ToArray(),
                nodes.SelectMany(x => x.CategoryIds).Distinct().Order().ToArray(), captures.Select(x => x.Sha256).Distinct().Order().ToArray(),
                nodes.Where(x => x.Error != null).Select(x => x.Error!).Concat(state.Issues.Where(x => nodes.Any(n => n.Url == x.Url) && x.Code == "PROCESS_ERROR").Select(x => x.Detail)).Distinct().ToArray()));
        }
        var categories = state.Lists.Where(x => x.CategoryId.HasValue).Select(x => x.CategoryId!.Value)
            .Concat(rows.SelectMany(x => x.Categories)).Distinct().Order().Select(id =>
            {
                var candidates = rows.Where(x => x.Categories.Contains(id)).ToArray();
                var lists = state.Lists.Where(x => x.CategoryId == id).ToArray();
                return new CategoryCoverage(id, candidates.Length, candidates.Count(x => x.Status == "VERIFIED"), candidates.Count(x => x.Status == "PENDING"),
                    lists.Select(x => x.Page).Distinct().Order().ToArray(), lists.Select(x => x.DeclaredPages).DefaultIfEmpty().Max(),
                    lists.Where(x => x.Result is "NoAccessibleRowsUnknown" or "RepeatedFirstPage").Select(x => x.Result).Distinct().ToArray());
            }).ToArray();
        return new(state.Urls.Count, rows.Count, state.Items.Count, rows.Count(x => x.Status == "VERIFIED"), rows.Count(x => x.Status == "PENDING"),
            newlyVerified.Count, state.RequestsReserved, state.ApprovalReference, rows.ToArray(), categories, extraction.ToArray(),
            OnlineCollection:state.ApprovalReference.StartsWith("BOUNDED_ONLY",StringComparison.Ordinal)?"NEEDS_APPROVAL":"RECORDED_COLLECTION_AUTHORITY_REVALIDATE_LEASE");
    }
}
