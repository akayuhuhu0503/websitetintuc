namespace LandPortal.MigrationCli;

public sealed record NukeVietRedirect(string SourceIdentity, string Alias, string Target, string Status = "PENDING_TARGET_SNAPSHOT_NO_PUBLICATION")
{
    // Recorded public redirect is a discovery candidate, never a substitute for
    // a real 200 snapshot or permission to follow an arbitrary remote Location.
    public static NukeVietRedirect Resolve(string url, int status, string location)
    {
        var source = MigrationSourceIdentityPolicy.FromUrl(url);
        if (source is not { Source: "gdla.gov.vn", Module: "news" } || status is not (301 or 308) ||
            string.IsNullOrWhiteSpace(location) || location.Any(char.IsControl) || !Uri.TryCreate(new Uri(url), location, out var target))
            throw new InvalidDataException("Recorded permanent public news redirect required");
        var identity = MigrationSourceIdentityPolicy.FromUrl(target.AbsoluteUri);
        if (identity?.Key != source.Key || new Uri(url).Scheme == "https" && target.Scheme != "https")
            throw new InvalidDataException("Redirect changes source identity, host, route or downgrades TLS");
        return new(source.Key, url, target.AbsoluteUri);
    }
}
