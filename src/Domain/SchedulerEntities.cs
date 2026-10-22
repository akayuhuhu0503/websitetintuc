namespace LandPortal.Domain;

public enum PublicationJobState { Pending, Running, Completed, Canceled, NeedsReview, Failed }
public sealed class PublicationJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentId { get; set; }
    public Guid RevisionId { get; set; }
    public string RevisionHash { get; set; } = "";
    public string RequestedByUserId { get; set; } = "";
    public string? ReviewerUserId { get; set; }
    public string? ApprovalHash { get; set; }
    public bool Direct { get; set; }
    public DateTimeOffset DueUtc { get; set; }
    public PublicationJobState State { get; set; }
    public bool IsActive { get; set; } = true;
    public long Generation { get; set; } = 1;
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public string FailureReason { get; set; } = "";
    public Guid PublishCommandId { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public byte[] RowVersion { get; set; } = [];
}
public sealed class PublicationJobAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public long Generation { get; set; }
    public Guid LeaseToken { get; set; }
    public string WorkerId { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
    public string Outcome { get; set; } = "Running";
    public string Reason { get; set; } = "";
}
public sealed class WorkerHeartbeat
{
    public string WorkerId { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public string Environment { get; set; } = "";
}
