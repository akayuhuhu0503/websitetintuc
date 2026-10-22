using LandPortal.Domain;
using System.Text.Json;

namespace LandPortal.MigrationCli;

public sealed class InventoryPolicyException(string message) : Exception(message);

// Collection provenance, not a CMS import manifest. Schema 3 cannot be fed to the phase-1 publisher.
public sealed class InventoryState
{
    [System.Text.Json.Serialization.JsonIgnore] public string? LoadedCheckpointHash { get; set; }
    public int SchemaVersion { get; set; } = 3;
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; }
    public string Status { get; set; } = "Created";
    public string Root { get; set; } = "";
    public string ApprovalReference { get; set; } = "BOUNDED_ONLY_NO_BROAD_APPROVAL";
    public int RequestsReserved { get; set; }
    public long ReceivedBytes { get; set; }
    public string RobotsText { get; set; } = "";
    public string RobotsState { get; set; } = "Unknown";
    public List<InventoryUrl> Urls { get; set; } = [];
    public List<InventoryItem> Items { get; set; } = [];
    public List<InventoryCapture> Captures { get; set; } = [];
    public List<InventoryIssue> Issues { get; set; } = [];
    public List<InventoryList> Lists { get; set; } = [];
    public List<InventoryRequest> Requests { get; set; } = [];
    public List<IssueEntry> LegacyIssues { get; set; } = [];
}
public sealed class InventoryUrl
{
    public string Url { get; set; } = "";
    public string Role { get; set; } = "Other";
    public ContentKind Kind { get; set; }
    public string CandidateId { get; set; } = "";
    public List<int> CategoryIds { get; set; } = [];
    public List<string> FoundOn { get; set; } = [];
    public string Status { get; set; } = "Discovered";
    public int Attempts { get; set; }
    public DateTimeOffset AvailableUtc { get; set; }
    public string? Error { get; set; }
    public string? CaptureSha { get; set; }
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    public string? ListRoot { get; set; }
    public int Page { get; set; } = 1;
    public bool Refresh { get; set; }
    public string ScanStatus { get; set; } = "NotScanned";
    public string? ScannedSha256 { get; set; }
    public string? ScanEngine { get; set; }
    public DateTimeOffset? ScannedUtc { get; set; }
    public string? Mime { get; set; }
    public string? FormatStatus { get; set; }
}
public sealed class InventoryItem
{
    public required ParsedSource Source { get; set; }
    public string ContentHash { get; set; } = "";
    public string RawSha256 { get; set; } = "";
    public string NormalizedBody { get; set; } = "";
    public string Status { get; set; } = "PrivateStagedNeedsScan";
    public List<string> Aliases { get; set; } = [];
}
public sealed record InventoryCapture(string Url, string FinalUrl, int Status, string ContentType, string Key, string Sha256, long Bytes, DateTimeOffset FetchedUtc, string Origin);
public sealed record InventoryIssue(string Url, string Code, string Detail, DateTimeOffset Utc);
public sealed record InventoryList(string Url, int? CategoryId, int Page, int DeclaredPages, int? DeclaredRecords, List<string> Ids, string Result, string Template);
public sealed record InventoryRequest(string Url, DateTimeOffset ReservedUtc);
public sealed record InventoryApproval(string Source, string ApprovedBy, string Reference, DateTimeOffset ExpiresUtc, int MaximumRequests, int MaximumPages, int MaximumContents, long MaximumBytes);
public sealed class InventoryLimits
{
    public int Requests { get; set; } = 30;
    public int Pages { get; set; } = 20;
    public int Contents { get; set; } = 8;
    public int Assets { get; set; } = 8;
    public int Seconds { get; set; } = 300;
    public long Bytes { get; set; } = 128 * 1024 * 1024;
    public bool Offline { get; set; }
    public string? ApprovalFile { get; set; }
    public string? DetailBatchFile { get; set; }
    public string? DetailBatchSha256 { get; set; }
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public void Validate()
    {
        if (DetailBatchFile is not null && (Offline || string.IsNullOrWhiteSpace(ApprovalFile) || string.IsNullOrWhiteSpace(DetailBatchSha256))) throw new InvalidOperationException("A detail work order requires its exact SHA and a separate reviewed collection approval; offline audit uses no collector.");
        if (Requests is < 0 or > 2000 || Pages is < 1 or > 2000 || Contents is < 0 or > 10000 || Assets is < 0 or > 10000 || Seconds is < 1 or > 3600 || Bytes is < 1024 or > 4L*1024*1024*1024) throw new InvalidOperationException("Invalid finite inventory limits.");
        if (!Offline && ApprovalFile is null && (Requests > 40 || Pages > 40 || Contents > 12 || Assets > 16 || Seconds > 600 || Bytes > 256L*1024*1024)) throw new InvalidOperationException("Broad collection requires a recorded source-owner/PO approval; bounded defaults cannot be raised.");
    }
}
