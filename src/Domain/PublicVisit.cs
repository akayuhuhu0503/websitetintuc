namespace LandPortal.Domain;

// Anonymous first-party browsing sessions; no IP, user/account, URL or email.
public sealed class PublicVisit
{
    public string SessionHash { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}
