using System.Data;
using System.Diagnostics;
using System.Net.Mail;
using System.Security.Cryptography;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

public sealed record InquiryStatusResult(InteractionState State, string Answer);

// Separate from retired OTP grants. Never returns original form, PII, files or internal audit.
public sealed class InquiryCaptchaLookup(PortalDbContext db, PublicCaptchaService captcha, InteractionService interaction, TimeProvider? clock=null)
{
    public const string Purpose="INQUIRY_LOOKUP_CAPTCHA";
    public async Task<InquiryStatusResult?> ReadAsync(string receipt,string email,Guid challenge,string answer,string session,string address)
    {
        var started=Stopwatch.StartNew();
        await captcha.ConsumeAsync(challenge,answer,session,address,Purpose);
        var now=(clock??TimeProvider.System).GetUtcNow();
        var canonicalReceipt=(receipt??"").Trim().ToLowerInvariant();var canonicalEmail=(email??"").Trim();
        var valid=canonicalReceipt.Length==32&&canonicalReceipt.All(Uri.IsHexDigit)&&canonicalEmail.Length<=254&&
            !canonicalEmail.Any(char.IsControl)&&MailAddress.TryCreate(canonicalEmail,out var mail)&&mail.Address==canonicalEmail;
        var at=canonicalEmail.LastIndexOf('@');if(at>=0)canonicalEmail=canonicalEmail[..(at+1)]+canonicalEmail[(at+1)..].ToLowerInvariant();
        var rh=TextNormalizer.Hash(canonicalReceipt);var sh=TextNormalizer.Hash(session);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        foreach(var key in new[]{"lookup12-receipt-"+rh,"lookup12-session-"+sh}.Order(StringComparer.Ordinal))
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource={key},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;IF @r<0 THROW 51000,'Lookup unavailable',1;");
        if(await db.Set<InquiryLookupAttempt>().CountAsync(x=>x.ReceiptHash==rh&&x.CreatedUtc>now.AddMinutes(-10))>=5||await db.Set<InquiryLookupAttempt>().CountAsync(x=>x.SessionHash==sh&&x.CreatedUtc>now.AddMinutes(-10))>=5)
            throw new PublicCaptchaException("Đã đạt giới hạn tra cứu. Vui lòng chờ 10 phút và thử lại.",429);
        db.Add(new InquiryLookupAttempt{ReceiptHash=rh,SessionHash=sh,CreatedUtc=now});
        var eh=TextNormalizer.Hash(canonicalEmail);
        var row=await db.InteractionSubmissions.AsNoTracking().Where(x=>x.Kind==InteractionKind.Inquiry&&x.ReceiptHash==rh&&x.EmailHash==eh).Select(x=>new{x.State,x.ProtectedAnswer}).SingleOrDefaultAsync();
        await db.SaveChangesAsync();await tx.CommitAsync();
        var floor=350+RandomNumberGenerator.GetInt32(76);if(started.ElapsedMilliseconds<floor)await Task.Delay(TimeSpan.FromMilliseconds(floor-started.ElapsedMilliseconds));
        if(!valid||row==null)return null;
        return new(row.State,(row.State is InteractionState.Answered or InteractionState.Closed)&&row.ProtectedAnswer!=""?interaction.Unprotect<string>(row.ProtectedAnswer):"");
    }
}
