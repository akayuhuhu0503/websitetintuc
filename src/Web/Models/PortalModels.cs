using System.ComponentModel.DataAnnotations;

namespace LandPortal.Web.Models;

public sealed record MenuCategory(string Name, string Slug);
public sealed record ContentCard(Guid Id, string Slug, string Title, string? Summary, string? DisplayDate, string? ThumbnailUrl, string Kind, string? CategoryName = null, string? CategorySlug = null)
{
    // The identity route is stable across editorial title changes; historical
    // optional-slug URLs remain accepted by the same published-content endpoint.
    public string Url => $"/bai-viet/{Id}";
}
public sealed class HomeModel
{
    public IReadOnlyList<ContentCard> News { get; init; } = [];
    public IReadOnlyList<ContentCard> Announcements { get; init; } = [];
    public IReadOnlyList<ContentCard> Documents { get; init; } = [];
    public IReadOnlyList<ContentCard> Videos { get; init; } = [];
    public IReadOnlyList<CategorySection> Sections { get; init; } = [];
    public int ImportedCount { get; init; }
}
public sealed record CategorySection(string Name, string Slug, IReadOnlyList<ContentCard> Items);
public sealed class ListingModel
{
    public IReadOnlyList<LandPortal.Web.Services.GeneralSearchRow> SearchItems { get; init; } = [];
    public int? CategoryId { get; init; }
    public IReadOnlyList<EditorCategory> Categories { get; init; } = [];
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string Sort { get; init; } = "recent";
    public string Title { get; init; } = "Tin tức";
    public string? Description { get; init; }
    public string? CategorySlug { get; init; }
    public string? Query { get; init; }
    public string? Kind { get; init; }
    public IReadOnlyList<ContentCard> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}
public sealed record AttachmentModel(Guid Id, string Name, string ContentType, long Bytes);
public sealed class ArticleModel
{
    public LandPortal.Domain.ContentMetadata Metadata { get; init; } = new();
    public bool IsDraftDocument { get; init; }
    public string? ConsultationState { get; init; }
    public required ContentCard Content { get; init; }
    public required string BodyHtml { get; init; }
    public string? Author { get; init; }
    public string? SourceName { get; init; }
    public string? SourceUrl { get; init; }
    public string? CoverUrl { get; init; }
    public string? CoverCaption { get; init; }
    public Guid? InlinePdfId { get; init; }
    public long DocumentViews { get; init; }
    public long DocumentDownloads { get; init; }
    public IReadOnlyList<AttachmentModel> Attachments { get; init; } = [];
    public IReadOnlyList<ContentCard> Related { get; init; } = [];
    public string? DocumentNumber { get; init; }
    public string? IssuingAuthority { get; init; }
    public string? IssuedDate { get; init; }
    public Guid? VideoFileId { get; init; }
    public bool IsVideo { get; init; }
}
public sealed class DocumentListModel
{
    public string? Signer { get; init; }
    public string? Effectiveness { get; init; }
    public string? Field { get; init; }
    public DateOnly? IssuedFrom { get; init; }
    public DateOnly? IssuedTo { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string Sort { get; init; } = "recent";
    public string Title { get; init; } = "Văn bản quy phạm pháp luật";
    public string? Query { get; init; }
    public string? Agency { get; init; }
    public string? Type { get; init; }
    public bool IsDraft { get; init; }
    public IReadOnlyList<DocumentRow> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}
public sealed record DocumentRow(ContentCard Content, string? Number, string? Agency, string? DocumentType, string? IssuedDate, Guid? FileId, string Effectiveness="Chưa xác định");
public sealed record StatusModel(string Title, string Message, string? SourceUrl = null, string? SourceLabel = null);
public sealed class LoginModel
{
    public Guid CaptchaId { get; set; }
    [StringLength(3)] public string? CaptchaAnswer { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever] public string CaptchaQuestion { get; set; } = "";
    [Required(ErrorMessage = "Nhập tên đăng nhập.")]
    [Display(Name = "Tên đăng nhập")]
    public string UserName { get; set; } = "";
    [Required(ErrorMessage = "Nhập mật khẩu.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
public sealed class EditorModel
{
    public IReadOnlyList<string> DocumentWarnings { get; set; } = [];
    [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]
    public List<LandPortal.Infrastructure.WeeklyScheduleRow> ScheduleRows { get; set; }=[];
    public LandPortal.Domain.ContentKind Kind { get; set; } = LandPortal.Domain.ContentKind.Article;
    [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]
    public LandPortal.Domain.ContentMetadata Metadata { get; set; } = new();
    public IReadOnlyList<LandPortal.Domain.SiteVocabulary> Vocabularies { get; set; } = [];
    public Guid? Id { get; set; }
    [Required(ErrorMessage = "Nhập tiêu đề bài viết.")]
    [StringLength(500, ErrorMessage = "Tiêu đề tối đa 500 ký tự.")]
    [Display(Name = "Tiêu đề")]
    public string Title { get; set; } = "";
    [StringLength(2000, ErrorMessage = "Mô tả tối đa 2.000 ký tự.")]
    [Display(Name = "Mô tả ngắn")]
    public string? Summary { get; set; }
    [Required(ErrorMessage = "Chọn chuyên mục.")]
    [Display(Name = "Chuyên mục chính")]
    public int? CategoryId { get; set; }
    [StringLength(1000000, ErrorMessage = "Nội dung vượt giới hạn 1.000.000 ký tự.")]
    [Display(Name = "Nội dung")]
    public string? BodyHtml { get; set; } = "";
    [StringLength(300)]
    [Display(Name = "Tác giả")]
    public string? Author { get; set; }
    [StringLength(300)]
    [Display(Name = "Nguồn")]
    public string? SourceName { get; set; }
    public string? RowVersion { get; set; }
    public string? CurrentStatus { get; set; }
    public IReadOnlyList<EditorCategory> Categories { get; set; } = [];
    public bool CanPublish { get; set; }
    public bool CanSubmit { get; set; }
    public bool IsPublished { get; set; }
    public Guid? CurrentRevisionId { get; set; }
    public int[] SecondaryCategoryIds { get; set; } = [];
    public Guid? CoverAssetId { get; set; }
    [StringLength(2000),Display(Name="Chú thích ảnh đại diện")] public string? CoverCaption { get; set; }
    public Guid[] AttachmentIds { get; set; } = [];
    public Guid[] VideoAssetIds { get; set; } = [];
    public Guid[] SubtitleAssetIds { get; set; } = [];
    public Dictionary<Guid,string> AttachmentLabels { get; set; } = [];
    public List<EditorAssetInput> PreservedAssets { get; set; } = [];
    public bool CanSave { get; set; }
    public bool CanBeginEdit { get; set; }
    public bool CanReview { get; set; }
    public bool CanPublishApproved { get; set; }
    public bool CanUnpublish { get; set; }
    public bool CanTrash { get; set; }
    public bool CanRestore { get; set; }
    public bool CanManageMedia { get; set; }
    public bool CanManageVideos { get; set; }
    public bool CanClone { get; set; }
    public bool CanSchedule { get; set; }
    public string DraftState { get; set; } = "Draft";
    public bool IsTrashed { get; set; }
    public string? RejectionReason { get; set; }
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public IReadOnlyList<EditorMedia> SelectedMedia { get; set; } = [];
}
public sealed record EditorCategory(int Id, string Name);
public sealed record EditorMedia(Guid Id, string Name, string MimeType, long Bytes);
public sealed class EditorAssetInput { public Guid Id { get; set; } public string Usage { get; set; } = "Inline"; [StringLength(1000), DisplayFormat(ConvertEmptyStringToNull = false)] public string Caption { get; set; } = ""; public int SortOrder { get; set; } }
public sealed record AdminRow(Guid Id, string Title, string Status, string? CategoryName, string UpdatedAt, bool IsPublished = false, bool IsTrashed = false, string? Owner = null);
public sealed class AdminListModel
{
    public string? Kind { get; init; }
    public IReadOnlyList<AdminRow> Items { get; init; } = [];
    public string? Query { get; init; }
    public string? Status { get; init; }
    public int Total { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 15;
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public int? CategoryId { get; init; }
    public string? Owner { get; init; }
    public IReadOnlyList<EditorCategory> Categories { get; init; } = [];
    public IReadOnlyList<AdminOwner> Owners { get; init; } = [];
    public bool CanCreate { get; init; }
}
public sealed record AdminOwner(string Id, string Name);
public sealed class WorkflowModel
{
    public string Action { get; set; } = "";
    public string RowVersion { get; set; } = "";
    public Guid? RevisionId { get; set; }
    public Guid RequestId { get; set; }
    [StringLength(2000)] public string? Reason { get; set; }
}
public sealed record RevisionRow(Guid Id, string Title, string Summary, string BodyText, string Author, string Source, string EditedBy, DateTimeOffset CreatedUtc, bool IsSource, bool IsCurrent, bool IsPublished);
public sealed class HistoryModel
{
    public Guid ContentId { get; init; }
    public string CurrentState { get; init; } = "";
    public string RowVersion { get; init; } = "";
    public bool CanRestore { get; init; }
    public bool CanClone { get; init; }
    public IReadOnlyList<RevisionRow> Revisions { get; init; } = [];
    public IReadOnlyList<HistoryAudit> Events { get; init; } = [];
}
public sealed record HistoryAudit(string Actor, string Action, Guid? RevisionId, string Detail, DateTimeOffset CreatedUtc);
public sealed class PreviewModel
{
    public string? CoverCaption { get; init; }
    public LandPortal.Domain.ContentMetadata Metadata { get; init; } = new();
    public required string Title { get; init; }
    public required string BodyHtml { get; init; }
    public string Summary { get; init; } = "";
    public string Author { get; init; } = "";
    public string Source { get; init; } = "";
    public string? Category { get; init; }
    public string? CoverUrl { get; init; }
    public Guid ContentId { get; init; }
    public Guid? RevisionId { get; init; }
    public IReadOnlyList<EditorMedia> Attachments { get; init; } = [];
}
