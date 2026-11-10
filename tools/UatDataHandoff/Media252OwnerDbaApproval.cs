using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.UatData;

public sealed record Media252Confirmation(string Kind,TargetApproval Scope,DateTimeOffset AcceptedUtc,string OwnerDecision,string TargetMediaReceiptSha256,string TargetMachineName,string TargetOperatorSid,bool AllowUnscannedMedia=false);

// No confirmation is synthesized here. The owner must authorize this new scope
// and acknowledge the actual target operator's exported receipt/hash. A proof
// copied from LAN or created by an unacknowledged party is not release authority.
public static class Media252OwnerDbaApproval
{
    public const string Kind="LandPortal.Media252.OwnerDba.v1";
    public static string Entropy=>Kind+"|"+Media252Scope.PackageId+"|"+Media252Scope.ManifestSha;
    public static void Validate(Media252Confirmation c,TargetPlan p,string sha,string action,TargetMediaReceipt receipt,string receiptSha,string sid,DateTimeOffset now)
    {
        var s=c.Scope;
        if(receipt.IntegrityOnly&&!c.AllowUnscannedMedia)throw new UnauthorizedAccessException("ADMIN_UNSCANNED_MEDIA_EXCEPTION_REQUIRED: xác nhận riêng AllowUnscannedMedia phải gắn đúng plan/receipt; không dùng lại approval Clean.");
        if(action!="Import"||c.Kind!=Kind||p.PackageId!=Media252Scope.PackageId||p.ManifestSha256!=Media252Scope.ManifestSha||p.Selection!="PendingOnlyContentOnly"||
            p.Contents.Length!=252||p.Contents.Distinct().Count()!=252||p.Media.Length!=703||p.Media.Distinct().Count()!=703||p.Aliases!=504||
            p.TargetServer!="45.120.229.43,1433"||p.TargetSqlServerIdentity!="WIN-S1IBJB84OOO"||p.TargetDatabase!="website_tin_tuc"||p.OperatorSid!=sid||
            sha!=DataPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(p,DataPackage.Json))||p.TargetMediaRootSha256!=receipt.MediaRootSha256||
            s.PackageId!=p.PackageId||s.ManifestSha256!=p.ManifestSha256||s.PlanSha256!=sha||s.TargetServer!=p.TargetServer||s.TargetDatabase!=p.TargetDatabase||s.TargetSqlServerIdentity!=p.TargetSqlServerIdentity||s.OperatorSid!=sid||
            s.ExpiresUtc<=now||s.ExpiresUtc>c.AcceptedUtc.AddDays(7)||c.AcceptedUtc>now.AddMinutes(5)||c.AcceptedUtc<now.AddDays(-7)||s.Actions.Length!=1||s.Actions[0]!="Import"||
            string.IsNullOrWhiteSpace(c.OwnerDecision)||string.IsNullOrWhiteSpace(s.ApprovalReference)||c.TargetMediaReceiptSha256!=receiptSha||c.TargetMachineName!=receipt.MachineName||c.TargetOperatorSid!=receipt.OperatorSid||
            receipt.PackageId!=p.PackageId||receipt.ManifestSha256!=p.ManifestSha256||(receipt.IntegrityOnly?receipt.Status!=TargetMediaStage.IntegrityStatus:receipt.Status!="TARGET_MEDIA_READY_NOT_SQL_IMPORTED"))
            throw new UnauthorizedAccessException("Thiếu xác nhận mới đúng gói252/plan/hash receipt IIS/người vận hành. Không tái sử dụng phê duyệt 33 bài.");
    }
    public static async Task<OwnerDbaAudit> VerifyAsync(string path,string receiptPath,string home,TargetPlan plan,string planSha,string action,DataBundle data,PortalDbContext db,bool separateOperator)
    {
        if(!OperatingSystem.IsWindows()||!separateOperator)throw new UnauthorizedAccessException("Cần Windows operator và phiên DBA riêng có scope hợp lệ.");
        path=Path.GetFullPath(path);DataPackage.NoReparse(path);
        if(!path.StartsWith(Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||new FileInfo(path).Length>65536)throw new UnauthorizedAccessException("Xác nhận phải nằm trong private workspace.");
        var bytes=await File.ReadAllBytesAsync(path);using var envelope=JsonDocument.Parse(bytes);var e=envelope.RootElement;var sid=WindowsIdentity.GetCurrent().User!.Value;
        if(e.GetProperty("kind").GetString()!=Kind||e.GetProperty("windowsSid").GetString()!=sid)throw new UnauthorizedAccessException("Sai loại xác nhận hoặc Windows SID.");
        var plaintext=ProtectedData.Unprotect(Convert.FromBase64String(e.GetProperty("protectedConfirmation").GetString()!),Encoding.UTF8.GetBytes(Entropy),DataProtectionScope.CurrentUser);
        Media252Confirmation c;try{c=JsonSerializer.Deserialize<Media252Confirmation>(plaintext,DataPackage.Json)??throw new InvalidDataException("Confirmation missing.");}finally{CryptographicOperations.ZeroMemory(plaintext);}
        var(r,receiptSha)=await TargetMediaStage.Read(receiptPath,data,plan.ManifestSha256,true);Validate(c,plan,planSha,action,r,receiptSha,sid,DateTimeOffset.UtcNow);
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var cmd=db.Database.GetDbConnection().CreateCommand();cmd.CommandTimeout=30;
            cmd.CommandText="SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME(),ORIGINAL_LOGIN(),CONVERT(varchar(172),SUSER_SID(ORIGINAL_LOGIN()),1),IS_SRVROLEMEMBER('sysadmin'),IS_MEMBER('db_owner');";
            await using var reader=await cmd.ExecuteReaderAsync();
            if(!await reader.ReadAsync()||reader.GetString(0)!=plan.TargetSqlServerIdentity||reader.GetString(1)!=plan.TargetDatabase||reader.GetString(2).Equals("website_tin_tuc_app",StringComparison.OrdinalIgnoreCase)||reader.GetString(2).Equals("landportal_giayhuy_reader",StringComparison.OrdinalIgnoreCase)||!(Equals(reader.GetValue(4),1)||Equals(reader.GetValue(5),1)))throw new UnauthorizedAccessException("DBA/physical target chưa đạt; không cấp quyền tự động.");
            return new(Kind,DataPackage.Hash(bytes),sid,reader.GetString(2),reader.GetString(3),DateTimeOffset.UtcNow);
        }
        finally{await db.Database.CloseConnectionAsync();}
    }
}
