namespace LandPortal.Domain;

// Separate from historical email OTP records. Never stores a plaintext answer or IP.
public sealed class PublicCaptchaChallenge
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public string SessionHash { get; set; }="";
    public string AddressHash { get; set; }="";
    public string Purpose { get; set; }="";
    public Guid? ContentId { get; set; }
    public Guid? RevisionId { get; set; }
    public string AnswerHash { get; set; }="";
    public string ProtectedKey { get; set; }="";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? ConsumedUtc { get; set; }
}
public sealed class PublicCaptchaAttempt
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public string AddressHash { get; set; }="";
    public DateTimeOffset CreatedUtc { get; set; }
}
