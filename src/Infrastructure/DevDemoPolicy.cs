using System.Security.Claims;
namespace LandPortal.Infrastructure;

public static class DevDemoPolicy
{
    public static bool Allowed(IEnumerable<Claim> claims,string? environment,string? database)
    {
        var all=claims.ToArray();
        if(!all.Any(x=>x.Type=="dev.demo"))return true;
        return environment=="Development"&&database is "LandPortal_Dev" or "LandPortal_Test"&&
            all.Count(x=>x.Type=="dev.demo")==1&&all.Any(x=>x.Type=="dev.demo"&&x.Value=="SRS1.2.DevDemo.v1")&&
            DateTimeOffset.TryParse(all.SingleOrDefault(x=>x.Type=="dev.demo.expires")?.Value,out var expires)&&expires>DateTimeOffset.UtcNow;
    }
}
