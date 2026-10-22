using LandPortal.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public class PortalUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public bool IsDisabled { get; set; }
    public bool MustChangePassword { get; set; }
}
public partial class PortalDbContext(DbContextOptions<PortalDbContext> options) : IdentityDbContext<PortalUser>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Content> Contents => Set<Content>();
    public DbSet<ContentRevision> Revisions => Set<ContentRevision>();
    public DbSet<RevisionCategory> RevisionCategories => Set<RevisionCategory>();
    public DbSet<ContentDraft> ContentDrafts => Set<ContentDraft>();
    public DbSet<DraftCategory> DraftCategories => Set<DraftCategory>();
    public DbSet<DraftAsset> DraftAssets => Set<DraftAsset>();
    public DbSet<ContentCommand> ContentCommands => Set<ContentCommand>();
    public DbSet<ContentCategory> ContentCategories => Set<ContentCategory>();
    public DbSet<LegalDocument> LegalDocuments => Set<LegalDocument>();
    public DbSet<DraftDocument> DraftDocuments => Set<DraftDocument>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<ContentAsset> ContentAssets => Set<ContentAsset>();
    public DbSet<SourceRecord> SourceRecords => Set<SourceRecord>();
    public DbSet<SourceSnapshot> SourceSnapshots => Set<SourceSnapshot>();
    public DbSet<SourceUrlAlias> SourceUrlAliases => Set<SourceUrlAlias>();
    public DbSet<CrawlRun> CrawlRuns => Set<CrawlRun>();
    public DbSet<CrawlItem> CrawlItems => Set<CrawlItem>();
    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();
    public DbSet<ImportIssue> ImportIssues => Set<ImportIssue>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<PublicationJob> PublicationJobs => Set<PublicationJob>();
    public DbSet<PublicationJobAttempt> PublicationJobAttempts => Set<PublicationJobAttempt>();
    public DbSet<WorkerHeartbeat> WorkerHeartbeats => Set<WorkerHeartbeat>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        PublicCaptchaModel.Configure(b);
        V05ContentModel.Configure(b);
        EditorialDocumentModel.Configure(b);
        b.Entity<PortalUser>().Property(x => x.DisplayName).HasMaxLength(200);
        b.Entity<Content>().Property(x => x.ApprovedByUserId).HasMaxLength(450);
        ContentMetadataModel.Configure(b);
        SiteModel.Configure(b);
        SchedulerModel.Configure(b);
        InteractionModel.Configure(b);
        InquiryFinishModel.Configure(b);
        VideoModel.Configure(b);
        b.Entity<Category>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Category>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Category>().Property(x => x.Slug).HasMaxLength(180);
        b.Entity<Category>().Property(x => x.Name).HasMaxLength(300);
        b.Entity<Content>().Property(x => x.RowVersion).IsRowVersion();
        b.Entity<Content>().HasOne(x => x.PrimaryCategory).WithMany().HasForeignKey(x => x.PrimaryCategoryId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<Content>().HasMany(x => x.Revisions).WithOne(x => x.Content).HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Content>().HasOne<ContentRevision>().WithMany().HasForeignKey(x => x.CurrentRevisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<Content>().HasOne<ContentRevision>().WithMany().HasForeignKey(x => x.PublishedRevisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<Content>().HasOne<ContentRevision>().WithMany().HasForeignKey(x => x.SubmittedRevisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<Content>().HasOne<ContentRevision>().WithMany().HasForeignKey(x => x.ApprovedRevisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<Content>().Property(x => x.ApprovalHash).HasMaxLength(64).IsUnicode(false);
        b.Entity<Content>().HasIndex(x => new { x.Kind, x.PublishedAtUtc });
        b.Entity<ContentCategory>().HasKey(x => new { x.ContentId, x.CategoryId });
        b.Entity<ContentCategory>().HasOne(x => x.Content).WithMany(x => x.Categories).HasForeignKey(x => x.ContentId);
        b.Entity<ContentCategory>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<ContentRevision>().Property(x => x.Title).HasMaxLength(1000);
        b.Entity<ContentRevision>().Property(x => x.ContentHash).HasMaxLength(64).IsUnicode(false);
        b.Entity<ContentRevision>().Property(x => x.SearchText).HasColumnType("nvarchar(max)");
        b.Entity<ContentRevision>().HasOne<Category>().WithMany().HasForeignKey(x => x.PrimaryCategoryId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<ContentRevision>().HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.CoverAssetId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<RevisionCategory>().HasKey(x => new { x.RevisionId, x.CategoryId });
        b.Entity<RevisionCategory>().HasOne(x => x.Revision).WithMany(x => x.Categories).HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RevisionCategory>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<ContentDraft>().HasKey(x => x.ContentId);
        b.Entity<ContentDraft>().HasOne(x => x.Content).WithOne().HasForeignKey<ContentDraft>(x => x.ContentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ContentDraft>().Property(x => x.Title).HasMaxLength(1000);
        b.Entity<ContentDraft>().HasOne<Category>().WithMany().HasForeignKey(x => x.PrimaryCategoryId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<ContentDraft>().HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.CoverAssetId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<DraftCategory>().HasKey(x => new { x.ContentId, x.CategoryId });
        b.Entity<DraftCategory>().HasOne(x => x.Draft).WithMany(x => x.Categories).HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DraftCategory>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<DraftAsset>().HasKey(x => new { x.ContentId, x.MediaAssetId });
        b.Entity<DraftAsset>().HasOne(x => x.Draft).WithMany(x => x.Assets).HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DraftAsset>().HasOne(x => x.MediaAsset).WithMany().HasForeignKey(x => x.MediaAssetId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<ContentCommand>().HasKey(x => x.Id);
        b.Entity<ContentCommand>().Property(x => x.ActorUserId).HasMaxLength(450);
        b.Entity<ContentCommand>().Property(x => x.Action).HasMaxLength(80);
        b.Entity<ContentCommand>().Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false);
        b.Entity<ContentCommand>().HasIndex(x => new { x.ContentId, x.CreatedUtc });
        b.Entity<LegalDocument>().HasKey(x => x.ContentId);
        b.Entity<LegalDocument>().HasOne(x => x.Content).WithOne().HasForeignKey<LegalDocument>(x => x.ContentId);
        b.Entity<LegalDocument>().Property(x => x.Number).HasMaxLength(200);
        b.Entity<LegalDocument>().HasIndex(x => x.Number);
        b.Entity<DraftDocument>().HasKey(x => x.ContentId);
        b.Entity<DraftDocument>().HasOne(x => x.Content).WithOne().HasForeignKey<DraftDocument>(x => x.ContentId);
        b.Entity<MediaAsset>().Property(x => x.Sha256).HasMaxLength(64).IsUnicode(false);
        b.Entity<MediaAsset>().HasIndex(x => x.Sha256).IsUnique();
        b.Entity<MediaAsset>().Property(x => x.StorageKey).HasMaxLength(180);
        b.Entity<MediaAsset>().Property(x => x.UploadedByUserId).HasMaxLength(450);
        b.Entity<MediaAsset>().Property(x => x.ValidationStatus).HasMaxLength(80).HasDefaultValue("SourceValidated");
        b.Entity<MediaAsset>().Property(x => x.Origin).HasMaxLength(40).HasDefaultValue("Source");
        b.Entity<ContentAsset>().HasKey(x => new { x.RevisionId, x.MediaAssetId });
        b.Entity<ContentAsset>().HasOne(x => x.Revision).WithMany(x => x.Assets).HasForeignKey(x => x.RevisionId);
        b.Entity<ContentAsset>().HasOne(x => x.MediaAsset).WithMany().HasForeignKey(x => x.MediaAssetId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SourceRecord>().Property(x => x.Source).HasMaxLength(100);
        b.Entity<SourceRecord>().Property(x => x.SourceId).HasMaxLength(120);
        b.Entity<SourceRecord>().HasIndex(x => new { x.Source, x.Kind, x.SourceId }).IsUnique();
        b.Entity<SourceRecord>().HasOne(x => x.Content).WithMany().HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SourceRecord>().Property(x => x.LastContentHash).HasMaxLength(64).IsUnicode(false);
        b.Entity<SourceUrlAlias>().Property(x => x.UrlHash).HasMaxLength(64).IsUnicode(false);
        b.Entity<SourceUrlAlias>().HasIndex(x => x.UrlHash).IsUnique();
        b.Entity<SourceUrlAlias>().HasOne<Content>().WithMany().HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SourceSnapshot>().Property(x => x.Sha256).HasMaxLength(64).IsUnicode(false);
        b.Entity<CrawlItem>().HasIndex(x => new { x.CrawlRunId, x.Status });
        b.Entity<SiteSetting>().HasKey(x => x.Key);
        b.Entity<SiteSetting>().Property(x => x.Key).HasMaxLength(100);
        b.Entity<Job>().Property(x => x.IdempotencyKey).HasMaxLength(200);
        b.Entity<Job>().HasIndex(x => x.IdempotencyKey).IsUnique();
        b.Entity<Job>().HasIndex(x => new { x.Status, x.AvailableUtc });
        b.Entity<AuditLog>().HasIndex(x => new { x.ContentId, x.CreatedUtc });
    }
    private void ProtectHistory()
    {
        ChangeTracker.DetectChanges();
        if(ChangeTracker.Entries().Any(x=>InquiryFinishModel.IsImmutable(x.Entity)&&x.State is EntityState.Modified or EntityState.Deleted))throw new InvalidOperationException("Lịch và lịch sử tương tác là bất biến.");
        if(ChangeTracker.Entries<InteractionPublicSnapshot>().Any(x=>x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Bản tương tác đã duyệt là bất biến; tạo snapshot mới.");
        if (ChangeTracker.Entries().Any(x => x.State is EntityState.Modified or EntityState.Deleted && SiteModel.IsImmutable(x.Entity)) ||
            ChangeTracker.Entries<ContentMetadata>().Any(x => x.Metadata.FindOwnership()?.PrincipalEntityType.ClrType == typeof(ContentRevision) && x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Metadata và cấu hình lịch sử là bất biến.");
        if (ChangeTracker.Entries<ContentRevision>().Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<RevisionCategory>().Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<ContentAsset>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Phiên bản lịch sử và quan hệ của phiên bản là bất biến; tạo bản nháp/phiên bản mới để sửa.");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { ProtectHistory(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) { ProtectHistory(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
}
