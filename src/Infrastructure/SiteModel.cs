using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

public static class SiteModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<SiteConfiguration>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<SiteConfiguration>().Property(x => x.RowVersion).IsRowVersion();
        b.Entity<SiteConfiguration>().HasOne<SiteConfigurationRevision>().WithMany().HasForeignKey(x => x.DraftRevisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SiteConfiguration>().HasOne<SiteConfigurationRevision>().WithMany().HasForeignKey(x => x.LiveRevisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SiteConfigurationRevision>().Property(x => x.ContentHash).HasMaxLength(64).IsUnicode(false);
        b.Entity<SiteConfigurationRevision>().Property(x => x.EditedByUserId).HasMaxLength(450);
        b.Entity<SiteConfigurationRevision>().Property(x => x.OrganizationName).HasMaxLength(300);
        b.Entity<SiteConfigurationRevision>().Property(x=>x.EnglishName).HasMaxLength(300);
        b.Entity<SiteConfigurationRevision>().Property(x=>x.SupervisingAgency).HasMaxLength(300);
        b.Entity<SiteConfigurationRevision>().Property(x=>x.ResponsiblePerson).HasMaxLength(300);
        b.Entity<SiteConfigurationRevision>().Property(x=>x.LicenseNumber).HasMaxLength(300);
        b.Entity<SiteConfigurationRevision>().Property(x=>x.SeoDescription).HasMaxLength(1000);
        b.Entity<SiteConfigurationRevision>().Property(x => x.ContactAddress).HasMaxLength(1000);
        b.Entity<SiteConfigurationRevision>().Property(x => x.ContactPhone).HasMaxLength(100);
        b.Entity<SiteConfigurationRevision>().Property(x => x.ContactHotline).HasMaxLength(100);
        b.Entity<SiteConfigurationRevision>().Property(x => x.ContactEmail).HasMaxLength(254);
        b.Entity<SiteConfigurationRevision>().Property(x => x.FooterText).HasMaxLength(5000);
        b.Entity<SiteConfigurationRevision>().HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.LogoAssetId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SiteCategoryConfiguration>().HasKey(x => new { x.RevisionId, x.CategoryId });
        b.Entity<SiteCategoryConfiguration>().HasOne(x => x.Revision).WithMany(x => x.Categories).HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SiteCategoryConfiguration>().Property(x => x.DisplayName).HasMaxLength(300);
        b.Entity<SiteCategoryConfiguration>().Property(x => x.Slug).HasMaxLength(180);
        b.Entity<SiteCategoryConfiguration>().Property(x => x.Description).HasMaxLength(2000);
        b.Entity<SiteMenuItem>().HasKey(x => new { x.RevisionId, x.Id });
        b.Entity<SiteMenuItem>().Property(x=>x.Enabled).HasDefaultValue(true);
        b.Entity<SiteMenuItem>().HasOne(x => x.Revision).WithMany(x => x.Menus).HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SiteMenuItem>().Property(x => x.Label).HasMaxLength(300);
        b.Entity<SiteMenuItem>().Property(x => x.Position).HasMaxLength(20);
        b.Entity<SiteMenuItem>().Property(x => x.TargetKind).HasMaxLength(20);
        b.Entity<SiteMenuItem>().Property(x => x.RouteKey).HasMaxLength(80);
        b.Entity<SiteMenuItem>().Property(x => x.ExternalUrl).HasMaxLength(2000);
        b.Entity<SiteBanner>().HasKey(x => new { x.RevisionId, x.Id });
        b.Entity<SiteBanner>().HasOne(x => x.Revision).WithMany(x => x.Banners).HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SiteBanner>().HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.MediaAssetId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SiteBanner>().Property(x => x.Label).HasMaxLength(300);
        b.Entity<SiteBanner>().Property(x => x.Alt).HasMaxLength(500);
        b.Entity<SiteBanner>().Property(x => x.Position).HasMaxLength(20);
        b.Entity<SiteBanner>().Property(x => x.TargetKind).HasMaxLength(20);
        b.Entity<SiteBanner>().Property(x => x.RouteKey).HasMaxLength(80);
        b.Entity<SiteBanner>().Property(x => x.ExternalUrl).HasMaxLength(2000);
        b.Entity<SiteHomeBlock>().HasKey(x => new { x.RevisionId, x.Id });
        b.Entity<SiteHomeBlock>().HasOne(x => x.Revision).WithMany(x => x.Blocks).HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SiteHomeBlock>().Property(x => x.Type).HasMaxLength(40);
        b.Entity<SiteHomeBlock>().Property(x => x.Title).HasMaxLength(300);
        b.Entity<SiteHomePin>().HasKey(x => new { x.RevisionId, x.BlockId, x.ContentId });
        b.Entity<SiteHomePin>().HasOne(x => x.Revision).WithMany(x => x.Pins).HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SiteHomePin>().HasOne<Content>().WithMany().HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SiteVocabulary>().HasKey(x => new { x.RevisionId, x.Id });
        b.Entity<SiteVocabulary>().HasOne(x => x.Revision).WithMany(x => x.Vocabularies).HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<SiteVocabulary>().Property(x => x.Kind).HasMaxLength(30);
        b.Entity<SiteVocabulary>().Property(x => x.Label).HasMaxLength(300);
    }
    public static bool IsImmutable(object entity) => entity is SiteConfigurationRevision or SiteCategoryConfiguration
        or SiteMenuItem or SiteBanner or SiteHomeBlock or SiteHomePin or SiteVocabulary;
}
