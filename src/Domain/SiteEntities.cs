namespace LandPortal.Domain;

// Category/source identities stay in Category. These rows version their presentation.
public sealed class SiteConfiguration
{
    public int Id { get; set; } = 1;
    public Guid? DraftRevisionId { get; set; }
    public Guid? LiveRevisionId { get; set; }
    public long Generation { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public sealed class SiteConfigurationRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ContentHash { get; set; } = "";
    public string EditedByUserId { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string OrganizationName { get; set; } = "CỤC QUẢN LÝ ĐẤT ĐAI";
    public string FooterText { get; set; } = "";
    public string ContactAddress { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public string ContactHotline { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public Guid? LogoAssetId { get; set; }
    public List<SiteCategoryConfiguration> Categories { get; set; } = [];
    public List<SiteMenuItem> Menus { get; set; } = [];
    public List<SiteBanner> Banners { get; set; } = [];
    public List<SiteHomeBlock> Blocks { get; set; } = [];
    public List<SiteHomePin> Pins { get; set; } = [];
    public List<SiteVocabulary> Vocabularies { get; set; } = [];
}
public sealed class SiteCategoryConfiguration
{
    public Guid RevisionId { get; set; }
    public SiteConfigurationRevision Revision { get; set; } = null!;
    public int CategoryId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    public int? ParentCategoryId { get; set; }
    public int SortOrder { get; set; }
    public bool ShowInNavigation { get; set; } = true;
}
public sealed class SiteMenuItem
{
    public Guid RevisionId { get; set; }
    public SiteConfigurationRevision Revision { get; set; } = null!;
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ParentId { get; set; }
    public string Position { get; set; } = "main";
    public string Label { get; set; } = "";
    public string TargetKind { get; set; } = "Route";
    public int? CategoryId { get; set; }
    public Guid? ContentId { get; set; }
    public string RouteKey { get; set; } = "";
    public string ExternalUrl { get; set; } = "";
    public bool OpenNewWindow { get; set; }
    public int SortOrder { get; set; }
    public bool SystemProtected { get; set; }
}
public sealed class SiteBanner
{
    public Guid RevisionId { get; set; }
    public SiteConfigurationRevision Revision { get; set; } = null!;
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MediaAssetId { get; set; }
    public string Label { get; set; } = "";
    public string Alt { get; set; } = "";
    public string Position { get; set; } = "home";
    public string TargetKind { get; set; } = "Route";
    public int? CategoryId { get; set; }
    public Guid? ContentId { get; set; }
    public string RouteKey { get; set; } = "Home";
    public string ExternalUrl { get; set; } = "";
    public bool OpenNewWindow { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset? StartUtc { get; set; }
    public DateTimeOffset? EndUtc { get; set; }
}
public sealed class SiteHomeBlock
{
    public Guid RevisionId { get; set; }
    public SiteConfigurationRevision Revision { get; set; } = null!;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "Recent";
    public string Title { get; set; } = "";
    public int Limit { get; set; } = 4;
    public int? CategoryId { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}
public sealed class SiteHomePin
{
    public Guid RevisionId { get; set; }
    public SiteConfigurationRevision Revision { get; set; } = null!;
    public Guid BlockId { get; set; }
    public Guid ContentId { get; set; }
    public int SortOrder { get; set; }
}
public sealed class SiteVocabulary
{
    public Guid RevisionId { get; set; }
    public SiteConfigurationRevision Revision { get; set; } = null!;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "Authority";
    public string Label { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
}
