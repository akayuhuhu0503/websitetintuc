using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.UatData;

public sealed record OwnerDbaConfirmation(string Kind,TargetApproval Scope,DateTimeOffset AcceptedUtc,string OwnerDecision);
public sealed record OwnerDbaAudit(string Method,string ConfirmationSha256,string WindowsSid,string DbaLogin,string DbaSid,DateTimeOffset VerifiedUtc);

// The owner explicitly accepted this alternative to Authenticode for ONE package.
// DPAPI binds the local confirmation to its Windows operator; it is not a
// certificate or a claim of an organizational digital signature. Other imports
// retain RequireApproval's existing Authenticode policy.
public static class Direct33OwnerDbaApproval
{
    public const string Kind="LandPortal.Direct33.OwnerDba.v1";
    public const string Manifest="d11d8a41d90dad15230f6a5203587d10d5ad94a16e7955db0d218f866c63b0c3";
    public const string PlanSha="24b3607fa17aa6ca0316c12f8ed09b7eec06fcb18447c0f27704bb888f65f664";
    public static readonly Guid PackageId=Guid.Parse("1830429d-8b36-4150-960a-fe53645ce70b");
    public static string Entropy=>Kind+"|"+PackageId+"|"+PlanSha;

    public static void Validate(OwnerDbaConfirmation confirmation,TargetPlan plan,string planSha,string action,string sid,DateTimeOffset now)
    {
        var scope=confirmation.Scope;
        if(action!="Import"||plan.PackageId!=PackageId||plan.ManifestSha256!=Manifest||planSha!=PlanSha||
            DataPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(plan,DataPackage.Json))!=planSha||
            plan.TargetServer!="45.120.229.43,1433"||plan.TargetSqlServerIdentity!="WIN-S1IBJB84OOO"||plan.TargetDatabase!="website_tin_tuc"||
            plan.Selection!="ReadyContentOnly"||plan.Contents.Length!=33||plan.Contents.Distinct().Count()!=33||plan.Media.Length!=0||plan.Aliases!=66||
            plan.Migration!="20261102042625_DraftConsultationEmail"||plan.OperatorSid!=sid||
            confirmation.Kind!=Kind||confirmation.AcceptedUtc>now.AddMinutes(5)||confirmation.AcceptedUtc<now.AddDays(-7)||
            string.IsNullOrWhiteSpace(confirmation.OwnerDecision)||string.IsNullOrWhiteSpace(scope.ApprovalReference)||
            scope.PackageId!=plan.PackageId||scope.ManifestSha256!=plan.ManifestSha256||scope.PlanSha256!=planSha||
            scope.TargetServer!=plan.TargetServer||scope.TargetDatabase!=plan.TargetDatabase||scope.TargetSqlServerIdentity!=plan.TargetSqlServerIdentity||
            scope.OperatorSid!=sid||scope.ExpiresUtc<=now||scope.ExpiresUtc>confirmation.AcceptedUtc.AddDays(7)||
            scope.Actions.Length!=1||scope.Actions[0]!="Import")
            throw new UnauthorizedAccessException("Owner + DBA confirmation is restricted to the exact approved direct33 package/plan/operator/action.");
    }

    public static OwnerDbaConfirmation Read(string path,string privateHome)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows operator confirmation required.");
        var full=Path.GetFullPath(path);DataPackage.NoReparse(full);
        if(!full.StartsWith(Path.GetFullPath(privateHome).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Owner confirmation must remain in private operator storage.");
        var file=new FileInfo(full);if(file.Length is <=0 or >65536)throw new InvalidDataException("Owner confirmation envelope length invalid.");
        using var envelope=JsonDocument.Parse(File.ReadAllBytes(full));var e=envelope.RootElement;
        if(e.GetProperty("kind").GetString()!=Kind||e.GetProperty("windowsSid").GetString()!=WindowsIdentity.GetCurrent().User?.Value)
            throw new UnauthorizedAccessException("Owner confirmation Windows identity differs.");
        var plaintext=ProtectedData.Unprotect(Convert.FromBase64String(e.GetProperty("protectedConfirmation").GetString()!),Encoding.UTF8.GetBytes(Entropy),DataProtectionScope.CurrentUser);
        try{return JsonSerializer.Deserialize<OwnerDbaConfirmation>(plaintext,DataPackage.Json)??throw new InvalidDataException("Owner confirmation missing.");}
        finally{CryptographicOperations.ZeroMemory(plaintext);}
    }

    public static async Task<OwnerDbaAudit> VerifyAsync(string path,string privateHome,TargetPlan plan,string planSha,string action,PortalDbContext db,bool separateOperator)
    {
        if(!separateOperator)throw new UnauthorizedAccessException("Owner + DBA method requires a separately authenticated scoped DBA session; runtime login is not an operator.");
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows operator confirmation required.");
        var confirmation=Read(path,privateHome);var sid=WindowsIdentity.GetCurrent().User!.Value;
        Validate(confirmation,plan,planSha,action,sid,DateTimeOffset.UtcNow);
        // Do not grant permissions or impersonate a principal. Verify the DBA
        // already authenticated by SqlCredential on this exact connection.
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var cmd=db.Database.GetDbConnection().CreateCommand();cmd.CommandTimeout=30;
            cmd.CommandText="SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME(),ORIGINAL_LOGIN(),CONVERT(varchar(172),SUSER_SID(ORIGINAL_LOGIN()),1),IS_SRVROLEMEMBER('sysadmin'),IS_MEMBER('db_owner');";
            await using var reader=await cmd.ExecuteReaderAsync();
            if(!await reader.ReadAsync()||reader.GetString(0)!=plan.TargetSqlServerIdentity||reader.GetString(1)!=plan.TargetDatabase||
                reader.GetString(2).Equals("website_tin_tuc_app",StringComparison.OrdinalIgnoreCase)||
                reader.GetString(2).Equals("landportal_giayhuy_reader",StringComparison.OrdinalIgnoreCase)||
                !(Equals(reader.GetValue(4),1)||Equals(reader.GetValue(5),1)))
                throw new UnauthorizedAccessException("Authenticated DBA/target confirmation failed; no permission changes permitted.");
            return new(Kind,await DataPackage.FileHash(path),sid,reader.GetString(2),reader.GetString(3),DateTimeOffset.UtcNow);
        }
        finally{await db.Database.CloseConnectionAsync();}
    }
}
