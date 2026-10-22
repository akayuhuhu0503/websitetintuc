using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public static class InteractionModel
{
    public static void Configure(ModelBuilder b)
    {
        var s=b.Entity<InteractionSubmission>();s.Property(x=>x.RowVersion).IsRowVersion();
        s.Property(x=>x.EmailHash).HasMaxLength(64).IsUnicode(false);s.Property(x=>x.ReceiptHash).HasMaxLength(64).IsUnicode(false);s.Property(x=>x.AssigneeId).HasMaxLength(450);
        s.HasIndex(x=>x.ChallengeId).IsUnique();s.HasIndex(x=>x.ReceiptHash).IsUnique();s.HasIndex(x=>new{x.Kind,x.State,x.CreatedUtc});s.HasIndex(x=>new{x.ContentId,x.PublicSnapshotId});
        s.HasOne<Content>().WithMany().HasForeignKey(x=>x.ContentId).OnDelete(DeleteBehavior.NoAction);s.HasOne<ContentRevision>().WithMany().HasForeignKey(x=>x.PublishedRevisionId).OnDelete(DeleteBehavior.NoAction);
        var p=b.Entity<InteractionPublicSnapshot>();p.Property(x=>x.DisplayName).HasMaxLength(120);p.Property(x=>x.Text).HasMaxLength(8000);p.Property(x=>x.AgencyAnswer).HasMaxLength(8000);p.Property(x=>x.ApprovedBy).HasMaxLength(450);
        p.HasOne<InteractionSubmission>().WithMany().HasForeignKey(x=>x.SubmissionId).OnDelete(DeleteBehavior.NoAction);s.HasOne<InteractionPublicSnapshot>().WithMany().HasForeignKey(x=>x.PublicSnapshotId).OnDelete(DeleteBehavior.NoAction);
        var c=b.Entity<InteractionChallenge>();c.Property(x=>x.RowVersion).IsRowVersion();foreach(var name in new[]{"EmailHash","SessionHash","PayloadHash","CodeHash"})c.Property<string>(name).HasMaxLength(64).IsUnicode(false);c.HasIndex(x=>new{x.EmailHash,x.CreatedUtc});
        var m=b.Entity<InteractionOutbox>();m.Property(x=>x.RowVersion).IsRowVersion();m.Property(x=>x.EventKey).HasMaxLength(160);m.HasIndex(x=>x.EventKey).IsUnique();m.HasIndex(x=>new{x.State,x.AvailableUtc});m.Property(x=>x.ErrorCode).HasMaxLength(80);m.Property(x=>x.RequestedBy).HasMaxLength(450);
        var command=b.Entity<InteractionCommand>();command.Property(x=>x.ActorId).HasMaxLength(450);command.Property(x=>x.RequestHash).HasMaxLength(64).IsUnicode(false);
    }
}
public partial class PortalDbContext
{
    public DbSet<InteractionSubmission> InteractionSubmissions=>Set<InteractionSubmission>();
    public DbSet<InteractionPublicSnapshot> InteractionPublicSnapshots=>Set<InteractionPublicSnapshot>();
    public DbSet<InteractionChallenge> InteractionChallenges=>Set<InteractionChallenge>();
    public DbSet<InteractionOutbox> InteractionOutbox=>Set<InteractionOutbox>();
    public DbSet<InteractionCommand> InteractionCommands=>Set<InteractionCommand>();
}
