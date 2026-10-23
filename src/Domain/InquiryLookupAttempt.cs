namespace LandPortal.Domain;

// No email, receipt or result is stored in plaintext. Shared SQL limits survive restarts.
public sealed class InquiryLookupAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ReceiptHash { get; set; } = "";
    public string SessionHash { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
}
