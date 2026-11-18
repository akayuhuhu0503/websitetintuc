using LandPortal.Domain;

namespace LandPortal.MigrationCli;
public sealed class SampleManifest
{
    public int SchemaVersion { get; set; } = 1;
    public Guid CrawlRunId { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedUtc { get; set; }
    public string Status { get; set; } = "Running";
    public int RequestBudget { get; set; }
    public int RequestsUsed { get; set; }
    public int ContentBudget { get; set; }
    public List<SnapshotEntry> Snapshots { get; set; } = [];
    public List<StagedContent> Contents { get; set; } = [];
    public List<AssetEntry> Assets { get; set; } = [];
    public List<DiscoveredLink> Discovered { get; set; } = [];
    public List<IssueEntry> Issues { get; set; } = [];
    public List<ListEvidence> Lists { get; set; } = [];
    public Dictionary<string, PipelineCheckpoint> ItemStages { get; set; } = [];
}
public sealed record PipelineCheckpoint(string Stage, int Attempts, DateTimeOffset UpdatedUtc, string? Error = null);
public sealed record ListEvidence(string Url, int? CategoryId, PaginationEvidence Pagination, List<string> SourceIds);
public sealed record IssueEntry(string SourceUrl, string Stage, string Code, string Message, bool Resolved = false);
public sealed record SnapshotEntry(string Url, string FinalUrl, int Status, string ContentType, string StorageKey, string Sha256, long Bytes, DateTimeOffset FetchedUtc);
public sealed class StagedContent
{
    public required ParsedSource Source { get; set; }
    public string Status { get; set; } = "Staged";
    public string ContentHash { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public List<Guid> AssetIds { get; set; } = [];
    public int Attempts { get; set; } = 1;
}
public sealed record AssetEntry(Guid Id, string SourceUrl, string StorageKey, string Sha256, long Bytes, string MimeType, string OriginalName);
