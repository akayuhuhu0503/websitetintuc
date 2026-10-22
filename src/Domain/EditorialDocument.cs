namespace LandPortal.Domain;

// New uploads only. Source MediaAssets never enter this queue.
public sealed class EditorialDocument
{
    public Guid MediaAssetId { get; set; }
    public Guid ContentId { get; set; }
    public string OwnerId { get; set; } = "";
    public InquiryScanState ScanState { get; set; } = InquiryScanState.NotScanned;
    public int Attempts { get; set; }
    public DateTimeOffset AvailableUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public string? ScannedSha256 { get; set; }
    public DateTimeOffset? ScannedUtc { get; set; }
    public string? EngineVersion { get; set; }
    public string? Error { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
