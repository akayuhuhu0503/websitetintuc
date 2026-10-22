namespace LandPortal.Domain;

public enum ContentKind { Article, Announcement, StaticPage, LegalDocument, DraftDocument, Video }
public enum WorkflowState { Draft, PendingReview, Published, Rejected, Unpublished, Archived, Approved }
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public int SortOrder { get; set; }
    public int? ParentId { get; set; }
}
public class Content
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ContentKind Kind { get; set; }
    public int? PrimaryCategoryId { get; set; }
    public Category? PrimaryCategory { get; set; }
    public Guid? CurrentRevisionId { get; set; }
    public Guid? PublishedRevisionId { get; set; }
    public Guid? SubmittedRevisionId { get; set; }
    public Guid? ApprovedRevisionId { get; set; }
    public string? ApprovalHash { get; set; }
    public string? ApprovedByUserId { get; set; }
    public bool IsPublic { get; set; }
    public bool IsTrashed { get; set; }
    public long EditGeneration { get; set; }
    public WorkflowState State { get; set; } = WorkflowState.Draft;
    public string? OwnerUserId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public DateOnly? SourcePublishedDate { get; set; }
    public string OriginalDateText { get; set; } = "";
    public string DatePrecision { get; set; } = "Unknown";
    public bool HasEditorialChanges { get; set; }
    public bool CommentsEnabled { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<ContentRevision> Revisions { get; set; } = [];
    public List<ContentCategory> Categories { get; set; } = [];
}
public class ContentCategory
{
    public Guid ContentId { get; set; }
    public Content Content { get; set; } = null!;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
public class ContentRevision
{
    public ContentMetadata Metadata { get; set; } = new();
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentId { get; set; }
    public Content Content { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public string SearchText { get; set; } = "";
    public string Author { get; set; } = "";
    public string SourceCredit { get; set; } = "";
    public string? EditedByUserId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool IsSourceRevision { get; set; }
    public string ContentHash { get; set; } = "";
    public int? PrimaryCategoryId { get; set; }
    public Guid? CoverAssetId { get; set; }
    public List<RevisionCategory> Categories { get; set; } = [];
    public List<ContentAsset> Assets { get; set; } = [];
}
public class RevisionCategory
{
    public Guid RevisionId { get; set; }
    public ContentRevision Revision { get; set; } = null!;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
// Mutable working area. Historical, submitted and published revisions are never autosaved.
public class ContentDraft
{
    public ContentMetadata Metadata { get; set; } = new();
    public Guid ContentId { get; set; }
    public Content Content { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public string SearchText { get; set; } = "";
    public string Author { get; set; } = "";
    public string SourceCredit { get; set; } = "";
    public int? PrimaryCategoryId { get; set; }
    public Guid? CoverAssetId { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedByUserId { get; set; } = "";
    public List<DraftCategory> Categories { get; set; } = [];
    public List<DraftAsset> Assets { get; set; } = [];
}
public class DraftCategory
{
    public Guid ContentId { get; set; }
    public ContentDraft Draft { get; set; } = null!;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
public class DraftAsset
{
    public Guid ContentId { get; set; }
    public ContentDraft Draft { get; set; } = null!;
    public Guid MediaAssetId { get; set; }
    public MediaAsset MediaAsset { get; set; } = null!;
    public string Usage { get; set; } = "Inline";
    public string Caption { get; set; } = "";
    public int SortOrder { get; set; }
}
public class ContentCommand
{
    public Guid Id { get; set; }
    public Guid ContentId { get; set; }
    public string ActorUserId { get; set; } = "";
    public string Action { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public Guid? ResultRevisionId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
public class LegalDocument
{
    public Guid ContentId { get; set; }
    public Content Content { get; set; } = null!;
    public string Number { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string IssuingBody { get; set; } = "";
    public string Signer { get; set; } = "";
    public string Field { get; set; } = "";
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public string? EffectivenessStatus { get; set; }
}
public class DraftDocument
{
    public Guid ContentId { get; set; }
    public Content Content { get; set; } = null!;
    public DateTimeOffset? ConsultationStartUtc { get; set; }
    public DateTimeOffset? ConsultationEndUtc { get; set; }
    public string ConsultationDateEvidence { get; set; } = "";
}
public class MediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sha256 { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long ByteLength { get; set; }
    public string SourceUrl { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string ScanStatus { get; set; } = "NotScanned";
    public string ValidationStatus { get; set; } = "SourceValidated";
    public string Origin { get; set; } = "Source";
    public string? UploadedByUserId { get; set; }
    public bool IsQuarantined { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
public class ContentAsset
{
    public Guid RevisionId { get; set; }
    public ContentRevision Revision { get; set; } = null!;
    public Guid MediaAssetId { get; set; }
    public MediaAsset MediaAsset { get; set; } = null!;
    public string Usage { get; set; } = "Inline";
    public string Caption { get; set; } = "";
    public int SortOrder { get; set; }
}
public class SourceRecord
{
    public long Id { get; set; }
    public string Source { get; set; } = "qldd.mae.gov.vn";
    public ContentKind Kind { get; set; }
    public string SourceId { get; set; } = "";
    public Guid ContentId { get; set; }
    public Content Content { get; set; } = null!;
    public string CanonicalUrl { get; set; } = "";
    public string LastContentHash { get; set; } = "";
    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;
}
public class SourceSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CrawlRunId { get; set; }
    public string SourceUrl { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int HttpStatus { get; set; }
    public string ContentType { get; set; } = "";
    public DateTimeOffset FetchedUtc { get; set; } = DateTimeOffset.UtcNow;
    public int SchemaVersion { get; set; } = 1;
}
public class SourceUrlAlias
{
    public long Id { get; set; }
    public Guid ContentId { get; set; }
    public string Url { get; set; } = "";
    public string UrlHash { get; set; } = "";
}
public class CrawlRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedUtc { get; set; }
    public string Status { get; set; } = "Running";
    public int RequestBudget { get; set; }
    public int RequestsUsed { get; set; }
    public int ContentBudget { get; set; }
    public string Checkpoint { get; set; } = "";
}
public class CrawlItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CrawlRunId { get; set; }
    public string SourceUrl { get; set; } = "";
    public string SourceId { get; set; } = "";
    public ContentKind Kind { get; set; }
    public string Status { get; set; } = "Discovered";
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
public class ImportRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CrawlRunId { get; set; }
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "Running";
    public int Imported { get; set; }
    public int Unchanged { get; set; }
    public int Failed { get; set; }
}
public class ImportIssue
{
    public long Id { get; set; }
    public Guid CrawlRunId { get; set; }
    public string SourceUrl { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
public class SiteSetting { public string Key { get; set; } = ""; public string Value { get; set; } = ""; }
public class AuditLog
{
    public long Id { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public Guid? ContentId { get; set; }
    public Guid? RevisionId { get; set; }
    public string Detail { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string IdempotencyKey { get; set; } = "";
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public DateTimeOffset AvailableUtc { get; set; } = DateTimeOffset.UtcNow;
    public int Attempts { get; set; }
    public string? Error { get; set; }
}
