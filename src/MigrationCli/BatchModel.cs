using LandPortal.Domain;
namespace LandPortal.MigrationCli;

public sealed class SourceBatchPlan
{
    public int SchemaVersion { get; set; } = 3;
    public Guid BatchId { get; set; } = Guid.NewGuid();
    public Guid InventoryId { get; set; }
    public string CheckpointSha256 { get; set; } = "";
    public string Database { get; set; } = "";
    public string? TargetMediaRootSha256 { get; set; }
    public string? RecoveryProofSha256 { get; set; }
    public string? SourceResourcePolicySha256 { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<SourceBatchRow> Rows { get; set; } = [];
}
public sealed class SourceBatchRow
{
    public ContentKind Kind { get; set; }
    public string SourceId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public Guid? ExistingCmsId { get; set; }
    public string ExistingStamp { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public string Classification { get; set; } = "";
    public string Action { get; set; } = "Blocked";
    public List<string> Validation { get; set; } = [];
    public List<string> Differences { get; set; } = [];
    public List<string> Aliases { get; set; } = [];
    public List<OptionalImageReview> OptionalImages { get; set; } = [];
    public List<SourceBatchFinding> Findings { get; set; } = [];
}
// Explicitly reviewed missing photos only. This never authorizes skipping a scan or a bad hash.
public sealed record OptionalImageReview(string Url,string Reason,string Evidence);
public sealed record SourceBatchFinding(string Severity,string Code,string? Url,string Role,string Resolution);
public sealed record SourceBatchOutcome(string SourceIdentity,string Status,Guid? ContentId,Guid? RevisionId,string? Error=null);
public sealed record SourceBatchAsset(InventoryUrl Node,InventoryCapture Capture,Guid Id);
public sealed record SourceBatchPrepared(InventoryItem Item,string Fingerprint,string Body,List<SourceBatchAsset> Assets,List<int> Categories,int? Primary,List<string> Errors)
{
    public List<string> Warnings { get; init; } = [];
    public List<SourceBatchFinding> Findings { get; init; } = [];
}
