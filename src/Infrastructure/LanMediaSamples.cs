using System.Security.Principal;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Infrastructure;

// Explicit anonymous fixture bytes only; never an arbitrary public upload exception.
public static class LanMediaSamples
{
    public const string Provider="MicrosoftDefenderLanMediaSample";
    public static HashSet<string> Require(IConfiguration config,string environment)
    {
        if(!OperatingSystem.IsWindows()||!LanDemoDeployment.Validate(config,environment)||
            config["Scanner:Provider"]!=Provider||config["LanDemo:AnonymousMediaSamples"]!="true"||
            config["Scanner:DefenderLanOperatorSid"]!=WindowsIdentity.GetCurrent().User?.Value)
            throw new InvalidOperationException("Anonymous LAN media requires exact local demo/operator policy.");
        var hashes=(config["Scanner:DefenderAllowedLanMediaHashes"]??"").Split(',',StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var pin=config["Scanner:DefenderBinarySha256"]??"";
        if(hashes.Count is <1 or >10||hashes.Any(x=>x.Length!=64||!x.All(Uri.IsHexDigit)||x!=x.ToLowerInvariant())||pin.Length!=64||!pin.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Explicit anonymous file hashes and engine pin required.");
        return hashes;
    }
}
