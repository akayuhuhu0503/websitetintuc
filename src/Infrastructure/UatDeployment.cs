using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Infrastructure;

// Explicit isolated staging target. This does not authorize provisioning, data
// import, real mail, or deployment, and never enables a Production exception.
public static class UatDeployment
{
    public static bool Enabled(IConfiguration c) => c["Deployment:Profile"] == "UAT";
    public static bool RequireMfa(IConfiguration c) => Enabled(c) || c["AdminSession:RequireMfa"] == "true";
    public static bool Validate(IConfiguration c, string environment)
    {
        if (!Enabled(c)) return false;
        if (environment != "Staging" || c["DOTNET_ENVIRONMENT"] != "Staging" || c["ASPNETCORE_ENVIRONMENT"] != "Staging")
            throw new InvalidOperationException("UAT requires explicit Staging for both Web and Worker; never Production/Development.");
        var sql = new SqlConnectionStringBuilder(DatabaseSettings.Connection(c));
        if (sql.InitialCatalog != "LandPortal_Uat" || c["Sql:ExpectedDatabase"] != "LandPortal_Uat" ||
            !sql.IntegratedSecurity || sql.DataSource != c["Deployment:ExpectedSqlServer"] ||
            sql.Encrypt == SqlConnectionEncryptOption.Optional || sql.TrustServerCertificate)
            throw new InvalidOperationException("UAT requires exact approved server/LandPortal_Uat, Windows identity and validated encrypted SQL transport.");
        if (!Uri.TryCreate(c["Public:BaseUrl"], UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.AbsolutePath != "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !string.Equals(uri.IdnHost, c["Deployment:ExpectedHost"], StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("UAT requires exact configured HTTPS host; no request-header origin.");
        var files = StorageOptions.FromConfiguration(c);
        var roots = new[] { files.MediaRoot, files.SnapshotRoot, files.TempRoot, files.KeysRoot }.Select(Path.GetFullPath).ToArray();
        var data = Path.GetFullPath(c["WorkspaceRoot"] ?? throw new InvalidOperationException("Private UAT home required")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (roots.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4 || roots.Any(x => !x.StartsWith(data, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("UAT stores must be separate private children of configured data home.");
        if (roots.Any(a => roots.Any(b => a != b && a.StartsWith(b.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Nested UAT stores refused.");
        var application = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach(var root in roots.Append(data.TrimEnd(Path.DirectorySeparatorChar)))
            for(string? node=root;node!=null;node=Path.GetDirectoryName(node))
                if((Directory.Exists(node)||File.Exists(node))&&(File.GetAttributes(node)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidOperationException("Reparse UAT private store refused.");
        if (data.StartsWith(application, StringComparison.OrdinalIgnoreCase) || application.StartsWith(data, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("UAT private stores/config must be outside published application.");
        if (c["Media:AllowUnscannedDevelopmentUploads"] == "true" || c["Scanner:Provider"] != "Clamd" ||
            !int.TryParse(c["Scanner:ClamdPort"], out var port) || port is < 1024 or > 65535 ||
            c["Scanner:PolicyReference"] is not { Length: > 4 } || c["Runtime:RolePassword"] is not { Length: >= 24 })
            throw new InvalidOperationException("UAT needs on-prem Clamd policy/port and runtime role secret; unscanned/DefenderTest bypass is forbidden.");
        return true;
    }
}
