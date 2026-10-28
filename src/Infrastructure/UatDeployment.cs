using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Infrastructure;

// Explicit isolated staging target. This does not authorize provisioning, data
// import, real mail, or deployment, and never enables a Production exception.
public static class UatDeployment
{
    public static bool IsTargetDatabase(string? database) => database is { Length: > 0 and <= 128 } &&
        System.Text.RegularExpressions.Regex.IsMatch(database, @"\A[A-Za-z][A-Za-z0-9_]*\z") &&
        !new[] { "master", "model", "msdb", "tempdb", "LandPortal_Dev", "LandPortal_Test" }.Contains(database, StringComparer.OrdinalIgnoreCase) &&
        !database.Contains("restore", StringComparison.OrdinalIgnoreCase) && !database.Contains("prod", StringComparison.OrdinalIgnoreCase) &&
        !database.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);

    public static bool Enabled(IConfiguration c) => c["Deployment:Profile"] == "UAT";
    public static bool AuthenticationMatches(IConfiguration c, SqlConnectionStringBuilder sql)
    {
        var mode=c["Deployment:SqlAuthentication"]??"Windows";
        if(mode=="Windows")return sql.IntegratedSecurity&&string.IsNullOrEmpty(sql.UserID)&&string.IsNullOrEmpty(sql.Password);
        var login=c["Deployment:ExpectedSqlLogin"];
        return mode=="SqlPassword"&&!sql.IntegratedSecurity&&
            (sql.Authentication is SqlAuthenticationMethod.NotSpecified or SqlAuthenticationMethod.SqlPassword)&&
            !string.IsNullOrWhiteSpace(login)&&!login.Equals("sa",StringComparison.OrdinalIgnoreCase)&&
            !login.Contains("REPLACE",StringComparison.OrdinalIgnoreCase)&&!login.Any(char.IsControl)&&
            sql.UserID==login&&!string.IsNullOrWhiteSpace(sql.Password)&&!sql.PersistSecurityInfo;
    }
    // A TCP address/port is not SERVERPROPERTY('ServerName'). Binding the latter
    // is mandatory for SQL-password deployment after the read-only DBA probe.
    public static string PhysicalServer(IConfiguration c)
    {
        var name=c["Deployment:ExpectedSqlServerIdentity"];
        if(string.IsNullOrWhiteSpace(name))
        {
            if(c["Deployment:SqlAuthentication"]=="SqlPassword")throw new InvalidOperationException("Bind the DBA-confirmed physical SQL server identity before runtime/schema/import.");
            return c["Deployment:ExpectedSqlServer"]??throw new InvalidOperationException("SQL target missing.");
        }
        if(name.Contains("REPLACE",StringComparison.OrdinalIgnoreCase)||name.Any(char.IsControl))throw new InvalidOperationException("Confirmed physical SQL identity required.");
        return name;
    }
    public static bool Validate(IConfiguration c, string environment)
    {
        if (!Enabled(c)) return false;
        if (environment != "Staging" || c["DOTNET_ENVIRONMENT"] != "Staging" || c["ASPNETCORE_ENVIRONMENT"] != "Staging")
            throw new InvalidOperationException("UAT requires explicit Staging for both Web and Worker; never Production/Development.");
        var sql = new SqlConnectionStringBuilder(DatabaseSettings.Connection(c));
        if (c["Deployment:TargetApproved"] != "true" || c["Deployment:TargetApprovalReference"] is not { Length: > 4 } ||
            !IsTargetDatabase(c["Sql:ExpectedDatabase"]) || sql.InitialCatalog != c["Sql:ExpectedDatabase"] ||
            !AuthenticationMatches(c,sql) || sql.DataSource != c["Deployment:ExpectedSqlServer"] ||
            string.IsNullOrWhiteSpace(c["Deployment:ExpectedSqlServer"]) || sql.DataSource.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) ||
            sql.Encrypt == SqlConnectionEncryptOption.Optional || sql.TrustServerCertificate)
            throw new InvalidOperationException("UAT requires allocated database, target approval, exact server/login authentication and validated encrypted SQL transport.");
        _=PhysicalServer(c);
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
        var clamd=c["Scanner:Provider"]=="Clamd"&&int.TryParse(c["Scanner:ClamdPort"],out var port)&&port is >=1024 and <=65535;
        var defender=c["Scanner:Provider"]=="MicrosoftDefenderOnPrem"&&c["Scanner:DefenderPrivacyPolicyReference"] is {Length:>4}&&
            c["Scanner:DefenderServiceIdentity"] is {Length:>3} identity&&!identity.Contains("REPLACE",StringComparison.OrdinalIgnoreCase)&&!identity.Any(char.IsControl)&&
            c["Scanner:DefenderBinarySha256"] is {Length:64} binarySha&&binarySha.All(Uri.IsHexDigit);
        if (c["Media:AllowUnscannedDevelopmentUploads"] == "true" || !clamd&&!defender ||
            c["Scanner:PolicyReference"] is not { Length: > 4 } || c["Runtime:RolePassword"] is not { Length: >= 24 })
            throw new InvalidOperationException("UAT requires approved on-prem Clamd or pinned Microsoft Defender with privacy policy/service identity, plus runtime role secret; unscanned/DefenderTest bypass is forbidden.");
        return true;
    }
}
