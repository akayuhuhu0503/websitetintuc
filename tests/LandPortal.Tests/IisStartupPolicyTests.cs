using LandPortal.Infrastructure;
namespace LandPortal.Tests;

public static class IisStartupPolicyTests
{
    public static void Run()
    {
        var count=0;
        void Check(bool ok){if(!ok)throw new InvalidOperationException("IIS startup identity/diagnostic regression");count++;}
        foreach(var name in new[]{@"IIS APPPOOL\LandPortal",@"IIS APPPOOL\DefaultAppPool",@"TTDL\portal-service"})
            Check(PortableIisConfiguration.IsServiceIdentityAllowed(name,false,false));
        foreach(var identity in new[]{(@"NT AUTHORITY\SYSTEM",true,false),(@"TTDL\Administrator",false,true),(@"TTDL\operator",false,true)})
            Check(!PortableIisConfiguration.IsServiceIdentityAllowed(identity.Item1,identity.Item2,identity.Item3));
        Check(PortableIisConfiguration.StartupHint("IDENTITY").Contains("ApplicationPoolIdentity",StringComparison.Ordinal));
        Check(PortableIisConfiguration.StartupHint("PRIVATE_HASH")!=PortableIisConfiguration.StartupHint("PRIVATE_READ"));
        Check(PortableIisConfiguration.StartupHint("HTTPS")!=PortableIisConfiguration.StartupHint("PRIVATE_ACL"));
        Check(PortableIisConfiguration.StartupHint("untrusted-secret-value")==PortableIisConfiguration.StartupHint("TECHNICAL"));
        Console.WriteLine($"PASS {count} focused IIS startup identity/diagnostic checks; no SQL or IIS changes");
    }
}
