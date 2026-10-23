using System.ComponentModel.DataAnnotations;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace LandPortal.Web.Models;

public sealed class SiteAdminModel
{
    public required SiteConfigurationState State { get; init; }
    public bool CanSave { get; init; }
    public bool CanPublish { get; init; }
    public string Section { get; init; } = "contact";
    public string RowVersion => Convert.ToBase64String(State.Header.RowVersion);
    public IReadOnlyList<SiteHistoryRow> History { get; init; } = [];
    public IReadOnlySet<int> ProtectedCategoryIds { get; init; } = new HashSet<int>();
}
public sealed record SiteHistoryRow(Guid Id, string Hash, string Actor, DateTimeOffset CreatedUtc);
public sealed record SiteSelectItem(string Value, string Label);
public sealed record SiteMoveModel(string RowVersion, string Section, Guid Id);
[ValidateNever] // Typed fields are validated by the shared application service; binding errors still reject.
public sealed class SiteEditModel
{
    public string Section { get; set; } = "contact";
    public string RowVersion { get; set; } = "";
    public Guid? ItemId { get; set; }
    public int? CategoryId { get; set; }
    public SiteContactInput Contact { get; set; } = new();
    public SiteCategoryInput Category { get; set; } = new();
    public SiteMenuInput Menu { get; set; } = new();
    public SiteBannerInput Banner { get; set; } = new();
    public SiteBlockInput Block { get; set; } = new();
    public SiteVocabularyInput Vocabulary { get; set; } = new();
    public IReadOnlyList<SiteSelectItem> Categories { get; set; } = [];
    public IReadOnlyList<SiteSelectItem> Parents { get; set; } = [];
    public IReadOnlyList<SiteSelectItem> Contents { get; set; } = [];
    public IReadOnlyList<SiteSelectItem> Images { get; set; } = [];
    public bool SystemProtected { get; set; }
    public bool CanUpload { get; set; }
    public string ContentSearch { get; set; } = "";
    public string MediaSearch { get; set; } = "";
}
public sealed class SiteContactInput
{
    [StringLength(300)] public string OrganizationName { get; set; } = "";
    [StringLength(5000)] public string FooterText { get; set; } = "";
    [StringLength(300)] public string? EnglishName { get; set; }
    [StringLength(300)] public string? SupervisingAgency { get; set; }
    [StringLength(300)] public string? ResponsiblePerson { get; set; }
    [StringLength(300)] public string? LicenseNumber { get; set; }
    [StringLength(1000)] public string? SeoDescription { get; set; }
    [StringLength(1000)] public string ContactAddress { get; set; } = "";
    [StringLength(100)] public string ContactPhone { get; set; } = "";
    [StringLength(100)] public string ContactHotline { get; set; } = "";
    [StringLength(254)] public string ContactEmail { get; set; } = "";
    public Guid? LogoAssetId { get; set; }
}
public sealed class SiteCategoryInput
{
    [StringLength(300)] public string DisplayName { get; set; } = "";
    [StringLength(180)] public string Slug { get; set; } = "";
    [StringLength(2000)] public string Description { get; set; } = "";
    public int? ParentCategoryId { get; set; }
    [Range(0, 10000)] public int SortOrder { get; set; }
    public bool ShowInNavigation { get; set; } = true;
}
public class SiteMenuInput
{
    public bool Enabled { get; set; } = true;
    [StringLength(300)] public string Label { get; set; } = "";
    public string Position { get; set; } = "main";
    public Guid? ParentId { get; set; }
    public string TargetKind { get; set; } = "Route";
    public int? CategoryId { get; set; }
    public Guid? ContentId { get; set; }
    public string RouteKey { get; set; } = "Home";
    [StringLength(2000)] public string ExternalUrl { get; set; } = "";
    public bool OpenNewWindow { get; set; }
    [Range(0, 10000)] public int SortOrder { get; set; }
}
public sealed class SiteBannerInput : SiteMenuInput
{
    public Guid? MediaAssetId { get; set; }
    [StringLength(500)] public string Alt { get; set; } = "";
    public DateTime? StartVietnam { get; set; }
    public DateTime? EndVietnam { get; set; }
}
public sealed class SiteBlockInput
{
    public string Type { get; set; } = "Recent";
    [StringLength(300)] public string Title { get; set; } = "";
    [Range(1, 12)] public int Limit { get; set; } = 4;
    public int? CategoryId { get; set; }
    [Range(0, 10000)] public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
    public Guid[] PinnedContentIds { get; set; } = [];
}
public sealed class SiteVocabularyInput
{
    public string Kind { get; set; } = "Authority";
    [StringLength(300)] public string Label { get; set; } = "";
    [Range(0, 10000)] public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
}
