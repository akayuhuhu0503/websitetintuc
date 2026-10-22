using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Infrastructure;
public sealed record PublicCaptchaPrompt(Guid Id,string Question,DateTimeOffset ExpiresUtc);
public sealed class PublicCaptchaException(string message,int statusCode=400):Exception(message)
{
    public int StatusCode { get; }=statusCode;
}
public sealed class PublicCaptchaOptions
{
    public int AttemptsPerWindow { get; init; }=5;
    public int LifetimeMinutes { get; init; }=5;
    public required byte[] AddressKey { get; init; }
    public static PublicCaptchaOptions Load(IConfiguration config,StorageOptions storage,string environment)
    {
        int Read(string key,int fallback,int max)=>int.TryParse(config[key],out var n)&&n>=1&&n<=max?n:fallback;
        Directory.CreateDirectory(storage.KeysRoot);
        var path=StorageOptions.ResolveWithin(storage.KeysRoot,"captcha-address.key");
        try{using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);file.Write(RandomNumberGenerator.GetBytes(32));file.Flush(true);}catch(IOException)when(File.Exists(path)){}
        var key=File.ReadAllBytes(path);if(key.Length!=32)throw new InvalidOperationException("CAPTCHA address key invalid.");
        if(config["Captcha:TestScope"] is string scope)
        {
            if(environment!="Development"||new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(DatabaseSettings.Connection(config)).InitialCatalog!="LandPortal_Test"||!Guid.TryParseExact(scope,"N",out _))throw new InvalidOperationException("CAPTCHA TestScope is restricted to isolated Development SQL Test.");
            key=HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(scope));
        }
        return new(){AddressKey=key,AttemptsPerWindow=Read("Captcha:AttemptsPer10Minutes",5,20),LifetimeMinutes=Read("Captcha:LifetimeMinutes",5,10)};
    }
}
public sealed class PublicCaptchaService(PortalDbContext db,IDataProtectionProvider protection,PublicCaptchaOptions options,TimeProvider? clock=null)
{
    private readonly IDataProtector protector=protection.CreateProtector("LandPortal.PublicCaptcha.v05");
    private DateTimeOffset Now=>(clock??TimeProvider.System).GetUtcNow();
    private string Address(string address)=>Convert.ToHexStringLower(HMACSHA256.HashData(options.AddressKey,Encoding.UTF8.GetBytes(address)));
    private static string Digest(byte[] key,string answer)=>Convert.ToHexStringLower(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(answer)));
    private Task Lock(string key)=>db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource={key},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;IF @r<0 THROW 51000,'Public form lock unavailable',1;");
    // Accessible, offline arithmetic challenge: readable by screen readers and keyboard.
    // Deliberately no claimed resistance to sophisticated bots; production abuse review remains a gate.
    public async Task<PublicCaptchaPrompt> IssueAsync(string session,string address,string purpose,Guid? contentId=null,Guid? revisionId=null)
    {
        if(purpose.Length>40||purpose.Length==0)throw new ArgumentException(nameof(purpose));
        var sh=TextNormalizer.Hash(session);var ip=Address(address);var now=Now;
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Lock("captcha-issue-"+ip);
        if(await db.PublicCaptchaChallenges.CountAsync(x=>x.AddressHash==ip&&x.CreatedUtc>now.AddMinutes(-10))>=100||await db.PublicCaptchaAttempts.CountAsync(x=>x.AddressHash==ip&&x.CreatedUtc>now.AddMinutes(-10))>=options.AttemptsPerWindow)
            throw new PublicCaptchaException("Đã đạt giới hạn xác thực. Vui lòng chờ 10 phút và thử lại.",429);
        // Replacing a challenge revokes previous unconsumed challenges for this exact form/session.
        await db.PublicCaptchaChallenges.Where(x=>x.SessionHash==sh&&x.Purpose==purpose&&x.ContentId==contentId&&x.ConsumedUtc==null).ExecuteUpdateAsync(x=>x.SetProperty(c=>c.ConsumedUtc,now));
        var a=RandomNumberGenerator.GetInt32(2,30);var b=RandomNumberGenerator.GetInt32(2,30);var add=RandomNumberGenerator.GetInt32(2)==0;
        if(!add&&a<b)(a,b)=(b,a);
        var answer=(add?a+b:a-b).ToString(CultureInfo.InvariantCulture);var key=RandomNumberGenerator.GetBytes(32);
        var row=new PublicCaptchaChallenge{SessionHash=sh,AddressHash=ip,Purpose=purpose,ContentId=contentId,RevisionId=revisionId,AnswerHash=Digest(key,answer),ProtectedKey=protector.Protect(Convert.ToBase64String(key)),CreatedUtc=now,ExpiresUtc=now.AddMinutes(options.LifetimeMinutes)};
        db.PublicCaptchaChallenges.Add(row);await db.SaveChangesAsync();await tx.CommitAsync();
        return new(row.Id,$"{a} {(add?"cộng":"trừ")} {b} bằng bao nhiêu?",row.ExpiresUtc);
    }
    // Consumed before any business write, even on a wrong answer. SQL locking fences replay across processes.
    public async Task ConsumeAsync(Guid id,string? answer,string session,string address,string purpose,Guid? contentId=null,Guid? revisionId=null)
    {
        var now=Now;var ip=Address(address);var sh=TextNormalizer.Hash(session);var valid=false;
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Lock("captcha-attempt-"+ip);
        if(await db.PublicCaptchaAttempts.CountAsync(x=>x.AddressHash==ip&&x.CreatedUtc>now.AddMinutes(-10))>=options.AttemptsPerWindow)throw new PublicCaptchaException("Đã đạt giới hạn 5 lần xác thực trong 10 phút (hoặc ngưỡng được cấu hình).",429);
        db.PublicCaptchaAttempts.Add(new(){AddressHash=ip,CreatedUtc=now});
        var row=await db.PublicCaptchaChallenges.FromSqlInterpolated($"SELECT * FROM dbo.PublicCaptchaChallenges WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync();
        if(row!=null&&row.SessionHash==sh&&row.AddressHash==ip&&row.Purpose==purpose&&row.ContentId==contentId&&row.RevisionId==revisionId&&row.ConsumedUtc==null)
        {
            row.ConsumedUtc=now;
            var key=Convert.FromBase64String(protector.Unprotect(row.ProtectedKey));
            var text=(answer??"").Trim();
            valid=row.ExpiresUtc>now&&text.Length<=3&&CryptographicOperations.FixedTimeEquals(Convert.FromHexString(row.AnswerHash),Convert.FromHexString(Digest(key,text)));
        }
        await db.SaveChangesAsync();await tx.CommitAsync();
        if(!valid)throw new PublicCaptchaException("Mã xác thực sai, đã dùng hoặc hết hạn. Hãy trả lời câu hỏi mới.");
    }
}
