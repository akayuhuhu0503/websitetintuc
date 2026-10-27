using System.Security.Cryptography;
using System.Text;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LandPortal.Web.Services;

public sealed record PublicTrafficCounts(long Active,long Today,long Month,long Total,DateTimeOffset UpdatedUtc);

// SQL is authoritative across Web processes/restarts. Browser cookies are
// protected random session ids, expire after 30 minutes idle and confer no ACL.
public sealed class PublicTraffic(PortalDbContext db,IDataProtectionProvider protection,IMemoryCache cache,ILogger<PublicTraffic> logger,PublicContentQuery publicContent)
{
    const string Cookie="LandPortal.PublicSession";
    public void RegisterSuccessfulPage(HttpContext context)
    {
        if(!HttpMethods.IsGet(context.Request.Method)||context.Request.Headers.ContainsKey("Range")||!EligibleReader(context.Request.Headers.UserAgent.ToString()))return;
        var protector=protection.CreateProtector("LandPortal.PublicStatistics.Session.v1");var now=DateTimeOffset.UtcNow;Guid session=Guid.NewGuid();
        if(context.Request.Cookies[Cookie] is string cookie&&cookie.Length<1024)
        {
            try{var parts=protector.Unprotect(cookie).Split('|');if(parts.Length==2&&Guid.TryParseExact(parts[0],"N",out var value)&&long.TryParse(parts[1],out var timestamp)&&timestamp<=now.ToUnixTimeSeconds()+5&&timestamp>=now.AddMinutes(-30).ToUnixTimeSeconds())session=value;}
            catch(CryptographicException){}catch(FormatException){}
        }
        var hash=Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(session.ToString("N"))));
        context.Response.OnStarting(async()=>
        {
            if(context.Response.StatusCode!=200||context.Response.ContentType?.StartsWith("text/html",StringComparison.OrdinalIgnoreCase)!=true)return;
            try
            {
                var article=context.Items[ArticleDiscovery.ViewAction] is Guid id ? (Guid?)id : null;
                if(article!=null||!cache.TryGetValue("visit-written:"+hash,out bool _)){await RecordAsync(hash,DateTimeOffset.UtcNow,article);cache.Set("visit-written:"+hash,true,TimeSpan.FromSeconds(45));}
                context.Response.Cookies.Append(Cookie,protector.Protect(session.ToString("N")+"|"+DateTimeOffset.UtcNow.ToUnixTimeSeconds()),new(){HttpOnly=true,Secure=context.Request.IsHttps,SameSite=SameSiteMode.Lax,Path="/",MaxAge=TimeSpan.FromMinutes(30),IsEssential=false});
            }
            catch(Exception e)when(e is Microsoft.Data.SqlClient.SqlException or DbUpdateException or OperationCanceledException)
            {logger.LogWarning("Public session statistics unavailable; page response retained; {Code}",e.GetType().Name);}
        });
    }
    public static bool EligibleReader(string userAgent) => userAgent.Length is > 0 and <= 1024 && !new[]{"bot","spider","crawler","headless","curl/","wget/","python-requests"}.Any(x=>userAgent.Contains(x,StringComparison.OrdinalIgnoreCase));
    public async Task RecordAsync(string hash,DateTimeOffset now,Guid? article=null)
    {
        if(hash.Length!=64||!hash.All(Uri.IsHexDigit))throw new ArgumentException("Opaque session SHA256 required");
        // Row/key-range lock prevents duplicate starts and backward last-seen
        // when independent IIS processes receive simultaneous requests.
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await db.Database.ExecuteSqlInterpolatedAsync($"IF EXISTS(SELECT 1 FROM PublicVisit WITH(UPDLOCK,HOLDLOCK) WHERE SessionHash={hash}) UPDATE PublicVisit SET LastSeenUtc=CASE WHEN LastSeenUtc<{now} THEN {now} ELSE LastSeenUtc END WHERE SessionHash={hash}; ELSE INSERT INTO PublicVisit(SessionHash,StartedUtc,LastSeenUtc) VALUES({hash},{now},{now});");
        if(article is Guid id && await publicContent.Published().AnyAsync(x=>x.Id==id&&x.Kind==ContentKind.Article))
        {
            var actor="Reader:"+hash;
            // Serializable/key-range locks cover concurrent IIS processes, not just an in-process cache.
            if(!await db.AuditLogs.AnyAsync(x=>x.ContentId==id&&x.Action==ArticleDiscovery.ViewAction&&x.Actor==actor&&x.CreatedUtc>now.AddMinutes(-30)))
            {db.AuditLogs.Add(new(){Actor=actor,Action=ArticleDiscovery.ViewAction,ContentId=id,CreatedUtc=now});await db.SaveChangesAsync();}
        }
        await tx.CommitAsync();
    }
    public async Task<PublicTrafficCounts?> ReadAsync()
    {
        if(cache.TryGetValue("public-traffic-counts",out PublicTrafficCounts? current))return current;
        try
        {
            var now=DateTimeOffset.UtcNow;var vn=now.ToOffset(TimeSpan.FromHours(7));var day=new DateTimeOffset(vn.Year,vn.Month,vn.Day,0,0,0,vn.Offset);var month=new DateTimeOffset(vn.Year,vn.Month,1,0,0,0,vn.Offset);
            var rows=db.Set<PublicVisit>().AsNoTracking();
            var result=new PublicTrafficCounts(await rows.LongCountAsync(x=>x.LastSeenUtc>=now.AddMinutes(-5)),await rows.LongCountAsync(x=>x.StartedUtc>=day),await rows.LongCountAsync(x=>x.StartedUtc>=month),await rows.LongCountAsync(),now);
            cache.Set("public-traffic-counts",result,TimeSpan.FromSeconds(15));return result;
        }
        catch(Microsoft.Data.SqlClient.SqlException){logger.LogWarning("Public statistics block hidden while SQL is unavailable; no fabricated fallback counts.");return null;}
    }
}
