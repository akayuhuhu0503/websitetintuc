using Microsoft.Extensions.Configuration;
namespace LandPortal.Infrastructure;
// Absolute URLs come exclusively from trusted deployment configuration, never Host/Forwarded headers.
public sealed class PublicUrlPolicy
{
    public Uri BaseUri { get; }
    public string CanonicalHost=>BaseUri.IdnHost;
    public string LocalLegacySourceHost { get; }
    public const string TargetDomain="gdla.gov.vn";
    public const string ImportSource="qldd.mae.gov.vn";
    public PublicUrlPolicy(IConfiguration config)
    {
        var uat=UatDeployment.Validate(config,config["DOTNET_ENVIRONMENT"]??"Production");
        if(!Uri.TryCreate(config["Public:BaseUrl"],UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||uri.AbsolutePath!="/"||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!=""||(!uri.IsLoopback&&!uat&&(uri.Scheme!="https"||uri.IdnHost!=TargetDomain||!uri.IsDefaultPort)))throw new InvalidOperationException("Public:BaseUrl must be loopback, approved isolated UAT, or the approved HTTPS gdla.gov.vn origin.");
        if(config["Public:CanonicalHost"] is string host&&!string.Equals(host,uri.IdnHost,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("CanonicalHost must match the configured public origin.");
        BaseUri=uri;LocalLegacySourceHost=config["Public:LocalLegacySourceHost"]??ImportSource;
        if(LocalLegacySourceHost is not(ImportSource or TargetDomain))throw new InvalidOperationException("Legacy source host is not allowlisted.");
    }
    public bool AllowsHost(string host)=>host is "localhost" or "127.0.0.1" or "::1" or "[::1]"||host.Equals(CanonicalHost,StringComparison.OrdinalIgnoreCase)||(!BaseUri.IsLoopback&&host.Equals(ImportSource,StringComparison.OrdinalIgnoreCase));
    public string LegacyOrigin(string requestHost)=>requestHost.Equals(ImportSource,StringComparison.OrdinalIgnoreCase)?ImportSource:requestHost.Equals(TargetDomain,StringComparison.OrdinalIgnoreCase)?TargetDomain:BaseUri.IsLoopback&&AllowsHost(requestHost)?LocalLegacySourceHost:throw new InvalidOperationException("Unknown legacy origin.");
    public string Absolute(string path)
    {
        if(!path.StartsWith('/')||path.StartsWith("//",StringComparison.Ordinal)||path.Contains('\\')||path.Any(char.IsControl))throw new ArgumentException("Only local absolute paths are permitted.");
        var uri=new Uri(BaseUri,path);if(uri.Scheme!=BaseUri.Scheme||uri.Authority!=BaseUri.Authority)throw new ArgumentException("External URL is not permitted.");return uri.AbsoluteUri;
    }
}
