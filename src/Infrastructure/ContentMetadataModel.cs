using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace LandPortal.Infrastructure;

public static class ContentMetadataModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<ContentRevision>().OwnsOne(x => x.Metadata, m => { m.ToTable("RevisionMetadata"); m.WithOwner().HasForeignKey("RevisionId"); ConfigureFields(m); });
        b.Entity<ContentRevision>().Navigation(x => x.Metadata).IsRequired();
        b.Entity<ContentDraft>().OwnsOne(x => x.Metadata, m => { m.ToTable("DraftMetadata"); m.WithOwner().HasForeignKey("ContentId"); ConfigureFields(m); });
        b.Entity<ContentDraft>().Navigation(x => x.Metadata).IsRequired();
        b.Entity<RevisionSearchProjection>().HasKey(x => x.RevisionId);
        b.Entity<RevisionSearchProjection>().HasOne<ContentRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<RevisionSearchProjection>().Property(x => x.Number).HasMaxLength(200);
        b.Entity<RevisionSearchProjection>().Property(x => x.Authority).HasMaxLength(500);
        b.Entity<RevisionSearchProjection>().Property(x => x.DocumentType).HasMaxLength(300);
        b.Entity<RevisionSearchProjection>().Property(x => x.Field).HasMaxLength(300);
        b.Entity<RevisionSearchProjection>().HasIndex(x => x.Number);
    }
    private static void ConfigureFields<TOwner>(OwnedNavigationBuilder<TOwner, ContentMetadata> m) where TOwner : class
    {
        m.Property(x=>x.Tags).HasMaxLength(500);
        m.Property(x => x.WeeklyOriginalReviewEvidence).HasMaxLength(2000);
        m.Property(x => x.Number).HasMaxLength(200);
        m.Property(x => x.DocumentType).HasMaxLength(300);
        m.Property(x => x.IssuingBody).HasMaxLength(500);
        m.Property(x => x.Signer).HasMaxLength(300);
        m.Property(x => x.Field).HasMaxLength(300);
        m.Property(x => x.Unit).HasMaxLength(500);
        m.Property(x => x.EffectivenessStatus).HasMaxLength(80);
        m.Property(x => x.EffectivenessEvidence).HasMaxLength(2000);
        m.Property(x => x.EffectivenessBoundary).HasMaxLength(40);
        m.Property(x => x.ConsultationDateEvidence).HasMaxLength(2000);
        m.Property(x => x.MappingStatus).HasMaxLength(40);
        m.Property(x => x.MetadataOrigin).HasMaxLength(80);
        m.Property(x=>x.DisclosureGroupId).HasMaxLength(50);m.Property(x=>x.Province).HasMaxLength(200);m.Property(x=>x.ProvinceCode).HasMaxLength(20);m.Property(x=>x.PublicationSource).HasMaxLength(1000);m.Property(x=>x.LegalBasisReference).HasMaxLength(2000);m.Property(x=>x.PublicationReviewEvidence).HasMaxLength(2000);m.Property(x=>x.WeeklyRowsJson).HasMaxLength(30000);m.Property(x=>x.WeeklyChangeNote).HasMaxLength(1000);
        m.HasIndex(x=>new{x.DisclosureGroupId,x.ProvinceCode,x.DisclosureYear});
    }
}
