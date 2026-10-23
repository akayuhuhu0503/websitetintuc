using System.Globalization;
using System.Text.RegularExpressions;

namespace LandPortal.MigrationCli;

// Normalized handoff, not a published CMS revision or an approval. Raw body,
// source timestamp and snapshot hash remain available for later reconciliation.
public sealed record NukeVietDraftCandidate(
    string SourceIdentity, string SqlSourceId, string SourceUrl, string CanonicalUrl,
    string RawSha256, string Title, string BodyHtml, string BodyTextSha256,
    DateTimeOffset? PublishedUtc, string? OriginalPublished, string? Author,
    string? OriginalAccount, string? OriginalSection, string[] MissingAssets,
    string[] ReviewReasons, bool PublicAllowed = false, bool SqlImported = false)
{
    public static NukeVietDraftCandidate Create(SecondarySourcePreview source, IEnumerable<string> capturedUrls)
    {
        if (source.Identity.Source != "gdla.gov.vn" || source.Identity.Module != "news" || source.PublicationStatus != "PrivateInventoryOnly")
            throw new InvalidDataException("Only verified private NukeViet news previews accepted");
        var metadata = source.RawMetadata;
        string? Read(string key) => metadata != null && metadata.TryGetValue(key, out var value) ? value : null;
        var original = Read("datePublished");
        // Never infer date from local PC time, a URL or malformed article metadata.
        var dated = original != null && Regex.IsMatch(original, @"\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})\z") &&
            DateTimeOffset.TryParse(original, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        DateTimeOffset? utc = dated ? DateTimeOffset.Parse(original!, CultureInfo.InvariantCulture).ToUniversalTime() : null;
        var missing = source.AssetUrls.Except(capturedUrls, StringComparer.Ordinal).ToArray();
        var reasons = new List<string> { "CATEGORY_MAPPING_NOT_APPROVED", "PUBLIC_HTTP_SOURCE_ACCEPTANCE_PENDING", "TARGET_SCANNER_PROOF_AND_SQL_IMPORT_ADAPTER_REQUIRED" };
        if (!dated) reasons.Add("PUBLISHED_DATE_MISSING_OR_INVALID");
        var account = Read("author");
        if (!string.IsNullOrWhiteSpace(account)) reasons.Add("SOURCE_ACCOUNT_IS_NOT_CONFIRMED_PUBLIC_AUTHOR");
        if (missing.Length > 0) reasons.Add("MISSING_BOUND_SOURCE_ASSETS");
        // Publisher/account metadata is retained, never silently turned into a
        // real person's author attribution.
        return new(source.Identity.Key, source.Identity.SqlSourceId, source.Url, source.CanonicalUrl,
            source.RawSha256, source.Title, source.BodyHtml, source.BodyTextSha256, utc, original,
            null, account, Read("article:section"), missing, reasons.ToArray());
    }
}
