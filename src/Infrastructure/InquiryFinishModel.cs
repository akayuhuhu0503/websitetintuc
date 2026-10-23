using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public static class InquiryFinishModel
{
    public static void Configure(ModelBuilder b)
    {
        var attempt=b.Entity<InquiryLookupAttempt>();
        attempt.Property(x=>x.ReceiptHash).HasMaxLength(64).IsUnicode(false);
        attempt.Property(x=>x.SessionHash).HasMaxLength(64).IsUnicode(false);
        attempt.HasIndex(x=>new{x.ReceiptHash,x.CreatedUtc});
        attempt.HasIndex(x=>new{x.SessionHash,x.CreatedUtc});
        var s=b.Entity<InteractionSubmission>();s.Property(x=>x.InquiryField).HasMaxLength(120);s.HasOne<InquiryCalendar>().WithMany().HasForeignKey(x=>x.InquiryCalendarId).OnDelete(DeleteBehavior.NoAction);s.HasIndex(x=>new{x.Kind,x.AssigneeId,x.InquiryDueUtc});
        var f=b.Entity<InquiryAttachment>();f.Property(x=>x.RowVersion).IsRowVersion();f.Property(x=>x.SessionHash).HasMaxLength(64).IsUnicode(false);f.Property(x=>x.StorageKey).HasMaxLength(200);f.Property(x=>x.Mime).HasMaxLength(80);f.Property(x=>x.Sha256).HasMaxLength(64).IsUnicode(false);f.Property(x=>x.ScannedSha256).HasMaxLength(64).IsUnicode(false);f.Property(x=>x.ScanError).HasMaxLength(80);f.Property(x=>x.EngineVersion).HasMaxLength(200);f.HasIndex(x=>x.SubmissionId).IsUnique().HasFilter("[SubmissionId] IS NOT NULL");f.HasIndex(x=>new{x.ScanState,x.AvailableUtc});f.HasIndex(x=>new{x.SessionHash,x.CreatedUtc});f.HasOne<InteractionSubmission>().WithMany().HasForeignKey(x=>x.SubmissionId).OnDelete(DeleteBehavior.NoAction);
        var l=b.Entity<InquiryLookupChallenge>();l.Property(x=>x.RowVersion).IsRowVersion();foreach(var name in new[]{"SessionHash","EmailHash","ReceiptHash","CodeHash"})l.Property<string>(name).HasMaxLength(64).IsUnicode(false);l.Property(x=>x.Purpose).HasMaxLength(40);l.HasIndex(x=>new{x.EmailHash,x.CreatedUtc});l.HasIndex(x=>new{x.SessionHash,x.CreatedUtc});
        var session=b.Entity<InquiryLookupSession>();session.Property(x=>x.TokenHash).HasMaxLength(64).IsUnicode(false);session.Property(x=>x.SessionHash).HasMaxLength(64).IsUnicode(false);session.HasIndex(x=>x.TokenHash).IsUnique();session.HasIndex(x=>x.ChallengeId).IsUnique();session.HasOne<InteractionSubmission>().WithMany().HasForeignKey(x=>x.SubmissionId).OnDelete(DeleteBehavior.NoAction);
        var a=b.Entity<InquiryAssignment>();a.Property(x=>x.ActorId).HasMaxLength(450);a.Property(x=>x.AssigneeId).HasMaxLength(450);a.Property(x=>x.PreviousAssigneeId).HasMaxLength(450);a.Property(x=>x.RequestHash).HasMaxLength(64).IsUnicode(false);a.HasIndex(x=>new{x.SubmissionId,x.CreatedUtc});
        var c=b.Entity<InquiryCalendar>();c.Property(x=>x.Label).HasMaxLength(200);c.Property(x=>x.PolicyVersion).HasMaxLength(80);c.Property(x=>x.CreatedBy).HasMaxLength(450);c.Property(x=>x.WeekendDays).HasMaxLength(20);c.HasIndex(x=>new{x.EffectiveFrom,x.CreatedUtc});
        var e=b.Entity<InquiryCalendarException>();e.HasKey(x=>new{x.CalendarId,x.Date});e.Property(x=>x.Note).HasMaxLength(200);e.HasOne(x=>x.Calendar).WithMany(x=>x.Exceptions).HasForeignKey(x=>x.CalendarId).OnDelete(DeleteBehavior.NoAction);
        var extension=b.Entity<InquiryExtension>();extension.Property(x=>x.ActorId).HasMaxLength(450);extension.Property(x=>x.RequestHash).HasMaxLength(64).IsUnicode(false);extension.HasIndex(x=>new{x.SubmissionId,x.CreatedUtc});
        var r=b.Entity<OutboxReconciliation>();r.Property(x=>x.ActorId).HasMaxLength(450);r.Property(x=>x.Decision).HasMaxLength(40);r.Property(x=>x.RequestHash).HasMaxLength(64).IsUnicode(false);r.Property(x=>x.ReceiptSha256).HasMaxLength(64).IsUnicode(false);r.HasIndex(x=>new{x.OutboxId,x.CreatedUtc});
    }
    public static bool IsImmutable(object value)=>value is InquiryAssignment or InquiryCalendar or InquiryCalendarException or InquiryExtension or OutboxReconciliation;
}
public partial class PortalDbContext
{
    public DbSet<InquiryAttachment> InquiryAttachments=>Set<InquiryAttachment>();
    public DbSet<InquiryLookupChallenge> InquiryLookupChallenges=>Set<InquiryLookupChallenge>();
    public DbSet<InquiryLookupSession> InquiryLookupSessions=>Set<InquiryLookupSession>();
    public DbSet<InquiryAssignment> InquiryAssignments=>Set<InquiryAssignment>();
    public DbSet<InquiryCalendar> InquiryCalendars=>Set<InquiryCalendar>();
    public DbSet<InquiryCalendarException> InquiryCalendarExceptions=>Set<InquiryCalendarException>();
    public DbSet<InquiryExtension> InquiryExtensions=>Set<InquiryExtension>();
    public DbSet<OutboxReconciliation> OutboxReconciliations=>Set<OutboxReconciliation>();
}
