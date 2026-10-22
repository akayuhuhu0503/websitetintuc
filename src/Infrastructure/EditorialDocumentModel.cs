using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;

public static class EditorialDocumentModel
{
    public static void Configure(ModelBuilder b)
    {
        var d = b.Entity<EditorialDocument>(); d.HasKey(x => x.MediaAssetId);
        d.HasOne<MediaAsset>().WithOne().HasForeignKey<EditorialDocument>(x => x.MediaAssetId).OnDelete(DeleteBehavior.NoAction);
        d.HasOne<Content>().WithMany().HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.NoAction);
        d.Property(x => x.OwnerId).HasMaxLength(450); d.Property(x => x.ScannedSha256).HasMaxLength(64).IsUnicode(false);
        d.Property(x => x.EngineVersion).HasMaxLength(300); d.Property(x => x.Error).HasMaxLength(100);
        d.Property(x => x.RowVersion).IsRowVersion(); d.HasIndex(x => new { x.ScanState, x.AvailableUtc });
    }
}
public partial class PortalDbContext
{
    public DbSet<EditorialDocument> EditorialDocuments => Set<EditorialDocument>();
}
