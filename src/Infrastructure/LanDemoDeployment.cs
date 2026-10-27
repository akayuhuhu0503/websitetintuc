using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Infrastructure;

// Separate, expiring local demo. This never grants a Dev/Test/Production exception.
public static class LanDemoDeployment
{
    public const string Database="LandPortal_LanDemo";
    public static bool Enabled(IConfiguration c)=>c["Deployment:Profile"]=="LAN_DEMO";
    public static bool PrivateV4(IPAddress ip){var b=ip.GetAddressBytes();return b.Length==4&&(b[0]==10||b[0]==172&&b[1] is >=16 and <=31||b[0]==192&&b[1]==168);}
    public static bool Validate(IConfiguration c,string environment)
    {
        if(!Enabled(c))return false;
        var sql=new SqlConnectionStringBuilder(DatabaseSettings.Connection(c));
        if(environment!="Development"||c["DOTNET_ENVIRONMENT"]!="Development"||c["ASPNETCORE_ENVIRONMENT"]!="Development"||
           sql.InitialCatalog!=Database||c["Sql:ExpectedDatabase"]!=Database||!sql.IntegratedSecurity||sql.DataSource!=@".\SQLEXPRESS"||
           c["Mail:Transport"]!="Disabled"||c["Media:AllowUnscannedDevelopmentUploads"]=="true"||c["AdminSession:RequireMfa"]!="true"||
           !DateTimeOffset.TryParse(c["LanDemo:ExpiresUtc"],out var expires)||expires<=DateTimeOffset.UtcNow||expires>DateTimeOffset.UtcNow.AddDays(15))
            throw new InvalidOperationException("LAN demo requires isolated local database, Development, live expiry, disabled mail and unscanned bypass, mandatory MFA.");
        if(!Uri.TryCreate(c["Public:BaseUrl"],UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||uri.AbsolutePath!="/"||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!=""||
           !IPAddress.TryParse(uri.Host,out var ip)||!PrivateV4(ip)||!NetworkInterface.GetAllNetworkInterfaces().Any(n=>n.OperationalStatus==OperationalStatus.Up&&n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.Equals(ip))))
            throw new InvalidOperationException("LAN demo requires an exact currently assigned RFC1918 IPv4 origin.");
        var root=Path.GetFullPath(c["WorkspaceRoot"]??throw new InvalidOperationException("Private demo home required"));
        var prefix=root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        var storage=StorageOptions.FromConfiguration(c);var stores=new[]{storage.MediaRoot,storage.SnapshotRoot,storage.TempRoot,storage.KeysRoot};
        if(stores.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=4||stores.Any(x=>!Path.GetFullPath(x).StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Separate demo stores must stay within private demo home.");
        return true;
    }
}
