namespace LandPortal.Domain;
public enum InteractionKind { Comment, Inquiry, Contact, Consultation }
public enum InteractionState { Pending, Processing, Answered, Closed, Rejected }
public sealed class InteractionSubmission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChallengeId { get; set; }
    public InteractionKind Kind { get; set; }
    public Guid? ContentId { get; set; }
    public Guid? PublishedRevisionId { get; set; }
    public string ProtectedOriginal { get; set; } = "";
    public string EmailHash { get; set; } = "";
    public string ReceiptHash { get; set; } = "";
    public bool PublicConsent { get; set; }
    public InteractionState State { get; set; }
    public string? AssigneeId { get; set; }
    public string? InquiryField { get; set; }
    public Guid? InquiryCalendarId { get; set; }
    public string? SlaPolicySnapshot { get; set; }
    public DateTimeOffset? InquiryDueUtc { get; set; }
    public bool SlaIsOfficial { get; set; }
    public string ProtectedAnswer { get; set; } = "";
    public Guid? PublicSnapshotId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? DeadlineUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
// Only this immutable, reviewed projection can be returned to guests.
public sealed class InteractionPublicSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubmissionId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Text { get; set; } = "";
    public string AgencyAnswer { get; set; } = "";
    public string ApprovedBy { get; set; } = "";
    public DateTimeOffset ApprovedUtc { get; set; }
}
public sealed class InteractionChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public InteractionKind Kind { get; set; }
    public Guid? ContentId { get; set; }
    public Guid? PublishedRevisionId { get; set; }
    public string EmailHash { get; set; } = "";
    public string SessionHash { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public string ProtectedKey { get; set; } = "";
    public int FailedAttempts { get; set; }
    public bool Revoked { get; set; }
    public Guid? SubmissionId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public enum InteractionMailState { Pending, Leased, Accepted, Failed, Uncertain, Cancelled }
public sealed class InteractionOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventKey { get; set; } = "";
    public Guid? ChallengeId { get; set; }
    public Guid? LookupChallengeId { get; set; }
    public Guid? SupersedesId { get; set; }
    public Guid? SubmissionId { get; set; }
    public string? RequestedBy { get; set; }
    public string ProtectedMail { get; set; } = "";
    public InteractionMailState State { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset AvailableUtc { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public DateTimeOffset? AcceptedUtc { get; set; }
    public string ErrorCode { get; set; } = "";
    public byte[] RowVersion { get; set; } = [];
}
public sealed class InteractionCommand
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public string ActorId { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
}
