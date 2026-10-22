using System.Data;
using System.Diagnostics;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public sealed record InquiryLookupResult(InteractionState State,DateTimeOffset ReceivedUtc,DateTimeOffset? DueUtc,string SlaLabel,string Answer,IReadOnlyList<string> History,string? AttachmentStatus);
public sealed class InquiryLookupService(PortalDbContext db,IDataProtectionProvider protection,InteractionMailOptions mail,InteractionService interaction,TimeProvider? clock=null)
{
    private readonly IDataProtector protector=protection.CreateProtector("LandPortal.Inquiry.Lookup.v1");
    private DateTimeOffset Now=>(clock??TimeProvider.System).GetUtcNow();
    private static (string Receipt,string Email) Canonical(string receipt,string email)
    {
        receipt=receipt.Trim().ToLowerInvariant();email=email.Trim();
        if(receipt.Length!=32||!receipt.All(Uri.IsHexDigit)||email.Length>254||!MailAddress.TryCreate(email,out var address)||address.Address!=email||email.Any(char.IsControl))throw new InvalidOperationException("Kiểm tra định dạng mã và email.");
        var at=email.LastIndexOf('@');return(receipt,email[..(at+1)]+email[(at+1)..].ToLowerInvariant());
    }
    private static string CodeHash(byte[] key,string code)=>Convert.ToHexStringLower(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(code)));
    public async Task<Guid> RequestAsync(string receipt,string email,string session)
    {
        mail.RequireAvailable();(receipt,email)=Canonical(receipt,email);var started=Stopwatch.StartNew();var now=Now;var eh=TextNormalizer.Hash(email);var rh=TextNormalizer.Hash(receipt);var sh=TextNormalizer.Hash(session);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource={"inquiry-lookup-email-"+eh},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;IF @r<0 THROW 51000,'Lookup lock unavailable',1;");
        var recent=await db.InquiryLookupChallenges.Where(x=>x.EmailHash==eh&&x.CreatedUtc>now.AddHours(-1)).ToListAsync();
        if(recent.Count>=5||recent.Any(x=>x.CreatedUtc>now.AddSeconds(-60))||await db.InquiryLookupChallenges.CountAsync(x=>x.SessionHash==sh&&x.CreatedUtc>now.AddHours(-1))>=20)throw new InvalidOperationException("Chưa thể yêu cầu mã mới. Vui lòng thử lại sau.");
        foreach(var old in recent.Where(x=>x.SessionHash==sh&&x.ConsumedUtc==null))old.Revoked=true;
        var target=await db.InteractionSubmissions.AsNoTracking().Where(x=>x.Kind==InteractionKind.Inquiry&&x.EmailHash==eh&&x.ReceiptHash==rh).Select(x=>(Guid?)x.Id).SingleOrDefaultAsync();
        var code=RandomNumberGenerator.GetInt32(0,1000000).ToString("D6");var key=RandomNumberGenerator.GetBytes(32);var challenge=new InquiryLookupChallenge{SubmissionId=target,SessionHash=sh,EmailHash=eh,ReceiptHash=rh,CodeHash=CodeHash(key,code),ProtectedKey=protector.Protect(Convert.ToBase64String(key)),CreatedUtc=now,ExpiresUtc=now.AddMinutes(5)};db.InquiryLookupChallenges.Add(challenge);
        if(target!=null)db.InteractionOutbox.Add(new(){EventKey="lookup:"+challenge.Id,LookupChallengeId=challenge.Id,ProtectedMail=interaction.Protect(new InteractionMail(email,"Mã xác minh tra cứu hồ sơ",$"Mã tra cứu: {code}\nChỉ dùng cho INQUIRY_LOOKUP trong 5 phút. Mã gửi hồ sơ không dùng để tra cứu.")),AvailableUtc=now});
        await db.SaveChangesAsync();await tx.CommitAsync();
        // Uniform visible response with a bounded response floor; no existence/status endpoint before verification.
        var floor=350+RandomNumberGenerator.GetInt32(0,76);if(started.ElapsedMilliseconds<floor)await Task.Delay(TimeSpan.FromMilliseconds(floor-started.ElapsedMilliseconds));return challenge.Id;
    }
    public async Task<string> VerifyAsync(Guid id,string receipt,string email,string session,string code)
    {
        (receipt,email)=Canonical(receipt,email);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var c=await db.InquiryLookupChallenges.FromSqlInterpolated($"SELECT * FROM dbo.InquiryLookupChallenges WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync();
        if(c==null||c.Purpose!="INQUIRY_LOOKUP"||c.SessionHash!=TextNormalizer.Hash(session)||c.EmailHash!=TextNormalizer.Hash(email)||c.ReceiptHash!=TextNormalizer.Hash(receipt)||c.ConsumedUtc!=null||c.Revoked||c.ExpiresUtc<=Now||c.FailedAttempts>=5)throw new InvalidOperationException("Không xác minh được yêu cầu tra cứu.");
        var key=Convert.FromBase64String(protector.Unprotect(c.ProtectedKey));var valid=code.Length==6&&CryptographicOperations.FixedTimeEquals(Convert.FromHexString(c.CodeHash),Convert.FromHexString(CodeHash(key,code)));
        if(!valid||c.SubmissionId==null||!await db.InteractionOutbox.AnyAsync(x=>x.LookupChallengeId==id&&x.State==InteractionMailState.Accepted))
        {c.FailedAttempts++;await db.SaveChangesAsync();await tx.CommitAsync();throw new InvalidOperationException("Không xác minh được yêu cầu tra cứu.");}
        // Recheck the exact kind and receipt/email binding at consumption time.
        if(!await db.InteractionSubmissions.AnyAsync(x=>x.Id==c.SubmissionId&&x.Kind==InteractionKind.Inquiry&&x.ReceiptHash==c.ReceiptHash&&x.EmailHash==c.EmailHash))throw new InvalidOperationException("Không xác minh được yêu cầu tra cứu.");
        c.ConsumedUtc=Now;var token=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));db.InquiryLookupSessions.Add(new(){ChallengeId=c.Id,SubmissionId=c.SubmissionId.Value,SessionHash=c.SessionHash,TokenHash=TextNormalizer.Hash(token),ExpiresUtc=Now.AddMinutes(15)});await db.SaveChangesAsync();await tx.CommitAsync();return token;
    }
    public async Task<InquiryLookupResult?> ReadAsync(string token,string session)
    {
        if(token.Length!=64||!token.All(Uri.IsHexDigit))return null;var th=TextNormalizer.Hash(token);var sh=TextNormalizer.Hash(session);
        var grant=await db.InquiryLookupSessions.AsNoTracking().SingleOrDefaultAsync(x=>x.TokenHash==th&&x.SessionHash==sh&&x.ExpiresUtc>Now);if(grant==null)return null;
        var row=await db.InteractionSubmissions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==grant.SubmissionId&&x.Kind==InteractionKind.Inquiry);if(row==null)return null;
        var history=await db.AuditLogs.FromSqlInterpolated($"SELECT TOP(20) * FROM dbo.AuditLogs WHERE JSON_VALUE(CASE WHEN ISJSON(Detail)=1 THEN Detail ELSE N'{{}}' END,'$.Id')={row.Id.ToString()} AND Action IN('interaction.received','interaction.process','interaction.answer','interaction.close','interaction.reject') ORDER BY CreatedUtc DESC,Id DESC").AsNoTracking().ToListAsync();
        var statuses=history.Select(x=>x.Action.Replace("interaction.","")+" · "+x.CreatedUtc.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm")).ToArray();
        var file=await db.InquiryAttachments.AsNoTracking().Where(x=>x.SubmissionId==row.Id).Select(x=>(InquiryScanState?)x.ScanState).SingleOrDefaultAsync();
        return new(row.State,row.CreatedUtc,row.InquiryDueUtc,row.SlaIsOfficial?"SLA theo phiên bản đã xác nhận":InquiryCalendarService.TemporaryLabel,row.ProtectedAnswer==""?"":interaction.Unprotect<string>(row.ProtectedAnswer),statuses,file?.ToString());
    }
}
