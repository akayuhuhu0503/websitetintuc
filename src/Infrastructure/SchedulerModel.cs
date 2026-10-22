using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public static class SchedulerModel
{
    public static void Configure(ModelBuilder b)
    {
        var job = b.Entity<PublicationJob>();
        job.Property(x => x.RowVersion).IsRowVersion();
        job.Property(x => x.RevisionHash).HasMaxLength(64).IsUnicode(false);
        job.Property(x => x.ApprovalHash).HasMaxLength(64).IsUnicode(false);
        job.Property(x => x.RequestedByUserId).HasMaxLength(450);
        job.Property(x => x.ReviewerUserId).HasMaxLength(450);
        job.Property(x => x.FailureReason).HasMaxLength(1000);
        job.HasIndex(x => x.ContentId).IsUnique().HasFilter("[IsActive] = 1");
        job.HasIndex(x => new { x.IsActive, x.State, x.DueUtc, x.NextAttemptUtc });
        job.HasIndex(x => x.PublishCommandId).IsUnique();
        job.HasOne<Content>().WithMany().HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.NoAction);
        job.HasOne<ContentRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.NoAction);
        var attempt = b.Entity<PublicationJobAttempt>();
        attempt.Property(x => x.WorkerId).HasMaxLength(120);
        attempt.Property(x => x.Outcome).HasMaxLength(40);
        attempt.Property(x => x.Reason).HasMaxLength(1000);
        attempt.HasIndex(x => new { x.JobId, x.LeaseToken }).IsUnique();
        attempt.HasOne<PublicationJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<WorkerHeartbeat>().HasKey(x => x.WorkerId);
        b.Entity<WorkerHeartbeat>().Property(x => x.WorkerId).HasMaxLength(120);
        b.Entity<WorkerHeartbeat>().Property(x => x.Environment).HasMaxLength(40);
    }
}
