namespace LandPortal.Domain;

public enum VideoUploadState { Uploading, Completed, Cancelled, Expired }
public enum VideoState { Pending, Scanning, Processing, Ready, Infected, ScanFailed, Failed, Blocked, Cancelled }
// The upload is bound to a CMS resource before accepting any bytes.
public sealed class VideoUploadSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentId { get; set; }
    public string OwnerUserId { get; set; } = "";
    public string Extension { get; set; } = "";
    public string Mime { get; set; } = "";
    public string ChunkManifestSha256 { get; set; } = "";
    public long ExpectedBytes { get; set; }
    public int ChunkBytes { get; set; }
    public int ChunkCount { get; set; }
    public long ReceivedBytes { get; set; }
    public VideoUploadState State { get; set; }
    public Guid? MediaAssetId { get; set; }
    public string? Sha256 { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public sealed class VideoChunk
{
    public Guid SessionId { get; set; }
    public int Index { get; set; }
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = "";
}
// One durable work item per immutable original; renditions never replace original bytes.
public sealed class VideoFile
{
    public Guid MediaAssetId { get; set; }
    public MediaAsset Original { get; set; } = null!;
    public Guid UploadSessionId { get; set; }
    public VideoState State { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset AvailableUtc { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public string Error { get; set; } = "";
    public string? ScannedSha256 { get; set; }
    public string? ScannerProvider { get; set; }
    public string? ScannerVersion { get; set; }
    public string? SignatureVersion { get; set; }
    public DateTimeOffset? SignatureUpdatedUtc { get; set; }
    public DateTimeOffset? ScannedUtc { get; set; }
    public string? Mp4Key { get; set; }
    public string? Mp4Sha256 { get; set; }
    public long? Mp4Bytes { get; set; }
    public string? PosterKey { get; set; }
    public string? PosterSha256 { get; set; }
    public long? PosterBytes { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSeconds { get; set; }
    public DateTimeOffset? ReadyUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public sealed class VideoSubtitle
{
    public Guid MediaAssetId {get;set;}
    public MediaAsset Original {get;set;}=null!;
    public Guid VideoMediaAssetId {get;set;}
    public Guid UploadSessionId {get;set;}
    public InquiryScanState State {get;set;}
    public double LastCueSeconds {get;set;}
    public string? ScannedSha256 {get;set;}
    public string? ScannerVersion {get;set;}
    public string? ScannerProvider {get;set;}
    public string? SignatureVersion {get;set;}
    public DateTimeOffset? ScannedUtc {get;set;}
    public string Error {get;set;}="";
    public int Attempts {get;set;}
    public DateTimeOffset AvailableUtc {get;set;}
    public Guid? LeaseToken {get;set;}
    public DateTimeOffset? LeaseUntilUtc {get;set;}
    public byte[] RowVersion {get;set;}=[];
}
