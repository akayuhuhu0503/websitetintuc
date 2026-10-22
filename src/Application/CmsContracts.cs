using LandPortal.Domain;
namespace LandPortal.Application;
public sealed record ContentActor(string UserId, IReadOnlySet<string> Permissions)
{
    // Null preserves the existing explicitly unrestricted accounts. An explicit
    // finite scope (including empty) restricts all primary/secondary placements.
    public IReadOnlySet<int>? CategoryIds { get; init; }
    public bool Covers(IEnumerable<int> categories) => CategoryIds is null || categories.All(CategoryIds.Contains);
    public bool Has(string permission) => Permissions.Contains(permission);
}
public sealed class DraftInput
{
    public ContentKind Kind { get; set; } = ContentKind.Article;
    public ContentMetadata Metadata { get; set; } = new();
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public string Author { get; set; } = "";
    public string SourceCredit { get; set; } = "";
    public int? PrimaryCategoryId { get; set; }
    public List<int> SecondaryCategoryIds { get; set; } = [];
    public Guid? CoverAssetId { get; set; }
    public List<AssetSelection> Assets { get; set; } = [];
}
public sealed class AssetSelection
{
    public Guid MediaAssetId { get; set; }
    public string Usage { get; set; } = "Inline";
    public string Caption { get; set; } = "";
    public int SortOrder { get; set; }
}
public sealed record WorkflowResult(Guid ContentId, Guid? CurrentRevisionId, Guid? PublishedRevisionId, byte[] RowVersion, WorkflowState State, bool IsPublic, bool IsTrashed, bool AlreadyApplied, DateTimeOffset SavedUtc);
