using System.Data;
using System.Security.Cryptography;
using System.Text;
using LandPortal.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

public sealed class AdminMfaService(PortalDbContext db, UserManager<PortalUser> users)
{
    public static string Normalize(string? code) => (code ?? "").Replace(" ", "").Replace("-", "");
    public async Task<bool> ConsumeAuthenticatorAsync(PortalUser user, string? input)
    {
        var code=Normalize(input);
        if(code.Length!=6||code.Any(c=>c is <'0' or >'9')||user.IsDisabled||user.MustChangePassword||await users.IsLockedOutAsync(user))return false;
        var key=await users.GetAuthenticatorKeyAsync(user);if(string.IsNullOrEmpty(key))return false;
        var digest=Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key),Encoding.UTF8.GetBytes(code)));
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource={"admin-mfa-"+TextNormalizer.Hash(user.Id)},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;IF @r<0 THROW 51000,'MFA lock unavailable',1;");
        var cutoff=DateTimeOffset.UtcNow.AddMinutes(-5);
        if(await db.AuditLogs.AnyAsync(x=>x.Actor==user.Id&&x.Action=="AdminMfaTokenUsed"&&x.Detail==digest&&x.CreatedUtc>=cutoff)||
            !await users.VerifyTwoFactorTokenAsync(user,users.Options.Tokens.AuthenticatorTokenProvider,code))return false;
        db.AuditLogs.Add(new(){Actor=user.Id,Action="AdminMfaTokenUsed",Detail=digest});await db.SaveChangesAsync();await tx.CommitAsync();return true;
    }
}
