namespace LandPortal.Domain;
public enum InquiryScanState { NotScanned, Scanning, Clean, Infected, ScanFailed, Deleted }
public sealed class InquiryAttachment
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public string SessionHash { get; set; }="";
    public Guid? SubmissionId { get; set; }
    public Guid? ChallengeId { get; set; }
    public string StorageKey { get; set; }="";
    public string ProtectedName { get; set; }="";
    public string Mime { get; set; }="";
    public string Sha256 { get; set; }="";
    public long Bytes { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public InquiryScanState ScanState { get; set; }
    public int ScanAttempts { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public DateTimeOffset AvailableUtc { get; set; }
    public string ScanError { get; set; }="";
    public string? ScannedSha256 { get; set; }
    public string? EngineVersion { get; set; }
    public DateTimeOffset? ScannedUtc { get; set; }
    public byte[] RowVersion { get; set; }=[];
}
public sealed class InquiryLookupChallenge
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public string Purpose { get; set; }="INQUIRY_LOOKUP";
    public Guid? SubmissionId { get; set; }
    public string SessionHash { get; set; }="";
    public string EmailHash { get; set; }="";
    public string ReceiptHash { get; set; }="";
    public string CodeHash { get; set; }="";
    public string ProtectedKey { get; set; }="";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public int FailedAttempts { get; set; }
    public bool Revoked { get; set; }
    public DateTimeOffset? ConsumedUtc { get; set; }
    public byte[] RowVersion { get; set; }=[];
}
public sealed class InquiryLookupSession
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public Guid ChallengeId { get; set; }
    public Guid SubmissionId { get; set; }
    public string TokenHash { get; set; }="";
    public string SessionHash { get; set; }="";
    public DateTimeOffset ExpiresUtc { get; set; }
}
public sealed class InquiryAssignment
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public string? PreviousAssigneeId { get; set; }
    public string? AssigneeId { get; set; }
    public string ActorId { get; set; }="";
    public string ProtectedReason { get; set; }="";
    public string RequestHash { get; set; }="";
    public DateTimeOffset CreatedUtc { get; set; }
}
public sealed class InquiryCalendar
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public string Label { get; set; }="";
    public DateOnly EffectiveFrom { get; set; }
    public string PolicyVersion { get; set; }="next-day-v1";
    public int WorkingDays { get; set; }=10;
    public TimeOnly Cutoff { get; set; }=new(17,0);
    public bool CountReceiptDay { get; set; }
    public string WeekendDays { get; set; }="0,6";
    public bool IsApproved { get; set; }
    public string CreatedBy { get; set; }="";
    public DateTimeOffset CreatedUtc { get; set; }
    public List<InquiryCalendarException> Exceptions { get; set; }=[];
}
public sealed class InquiryCalendarException
{
    public Guid CalendarId { get; set; }
    public InquiryCalendar Calendar { get; set; }=null!;
    public DateOnly Date { get; set; }
    public bool WorkingDay { get; set; }
    public string Note { get; set; }="";
}
public sealed class InquiryExtension
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public DateTimeOffset? PreviousDueUtc { get; set; }
    public DateTimeOffset DueUtc { get; set; }
    public string ActorId { get; set; }="";
    public string ProtectedReason { get; set; }="";
    public string RequestHash { get; set; }="";
    public DateTimeOffset CreatedUtc { get; set; }
}
public sealed class OutboxReconciliation
{
    public Guid Id { get; set; }
    public Guid OutboxId { get; set; }
    public Guid? ResendOutboxId { get; set; }
    public string ActorId { get; set; }="";
    public string Decision { get; set; }="";
    public string ProtectedEvidence { get; set; }="";
    public string? ReceiptSha256 { get; set; }
    public bool DuplicateRiskAcknowledged { get; set; }
    public string RequestHash { get; set; }="";
    public DateTimeOffset CreatedUtc { get; set; }
}
