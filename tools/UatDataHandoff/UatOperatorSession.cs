using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.UatData;

// A scoped CLI credential, separate from Web's unchanged app login/role.
// DPAPI grants no SQL permission or data-release authority; RequireApproval still runs.
public sealed class UatOperatorSession:IAsyncDisposable
{
    public PortalDbContext Db { get; }
    readonly SqlConnection? connection;
    readonly SecureString? password;
    UatOperatorSession(PortalDbContext db,SqlConnection? connection=null,SecureString? password=null){Db=db;this.connection=connection;this.password=password;}
    public static UatOperatorSession Create(IConfiguration c,string? credentialFile,Guid packageId,string manifestSha,string? requestedPlanSha,bool writeOperator)
    {
        if(credentialFile==null)return new(new PortalDbContext(DatabaseSettings.Options(c,forMigration:writeOperator)));
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows CurrentUser operator credential required.");
        var path=Path.GetFullPath(credentialFile);DataPackage.NoReparse(path);
        var home=Path.GetFullPath(c["WorkspaceRoot"]!).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!path.StartsWith(home,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Operator file must stay in private workspace.");
        using var json=JsonDocument.Parse(File.ReadAllBytes(path));var r=json.RootElement;
        var plan=r.GetProperty("PlanSha256").GetString();
        if(r.GetProperty("Kind").GetString()!="LandPortal.UatDataOperator.Dpapi.v1"||r.GetProperty("WindowsSid").GetString()!=WindowsIdentity.GetCurrent().User?.Value||
            r.GetProperty("TargetServer").GetString()!=c["Deployment:ExpectedSqlServer"]||r.GetProperty("TargetSqlServerIdentity").GetString()!=UatDeployment.PhysicalServer(c)||r.GetProperty("TargetDatabase").GetString()!=c["Sql:ExpectedDatabase"]||
            r.GetProperty("PackageId").GetString()!=packageId.ToString()||r.GetProperty("ManifestSha256").GetString()!=manifestSha||
            requestedPlanSha!=null&&requestedPlanSha!=plan||r.GetProperty("ExpiresUtc").GetDateTimeOffset()<=DateTimeOffset.UtcNow||r.GetProperty("ExpiresUtc").GetDateTimeOffset()>DateTimeOffset.UtcNow.AddHours(2.1))throw new UnauthorizedAccessException("Operator credential scope, identity or expiry differs.");
        var username=r.GetProperty("Username").GetString();
        if(string.IsNullOrWhiteSpace(username)||username.Equals("website_tin_tuc_app",StringComparison.OrdinalIgnoreCase)||username.Equals("landportal_giayhuy_reader",StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Separate DBA/import operator required.");
        var entropy="LandPortal.UatOperator|"+packageId+"|"+plan;
        if(r.GetProperty("Entropy").GetString()!=entropy)throw new UnauthorizedAccessException("Operator entropy binding differs.");
        var bytes=ProtectedData.Unprotect(Convert.FromBase64String(r.GetProperty("ProtectedPassword").GetString()!),Encoding.UTF8.GetBytes(entropy),DataProtectionScope.CurrentUser);
        var password=new SecureString();
        try{var chars=Encoding.Unicode.GetChars(bytes);try{foreach(var ch in chars)password.AppendChar(ch);}finally{Array.Clear(chars);}password.MakeReadOnly();}finally{CryptographicOperations.ZeroMemory(bytes);}
        var sql=new SqlConnectionStringBuilder(DatabaseSettings.Connection(c));sql.Remove("User ID");sql.Remove("Password");sql.IntegratedSecurity=false;sql.Pooling=false;sql.PersistSecurityInfo=false;sql.ApplicationName="LandPortal.UatDataOperator.Direct33";
        var connection=new SqlConnection(sql.ConnectionString,new SqlCredential(username,password));
        var options=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(connection,x=>x.UseCompatibilityLevel(int.Parse(c["Sql:CompatibilityLevel"]??"160"))).Options;
        return new(new PortalDbContext(options),connection,password);
    }
    public async ValueTask DisposeAsync(){await Db.DisposeAsync();if(connection!=null)await connection.DisposeAsync();password?.Dispose();}
}
