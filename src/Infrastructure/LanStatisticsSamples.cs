using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Principal;
using LandPortal.Domain;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Infrastructure;

// Public synthetic bytes only. No arbitrary LAN upload is sent to cloud-enabled Defender.
public static class LanStatisticsSamples
{
    public const string Provider="MicrosoftDefenderLanSample";
    public static byte[] Workbook(bool invalid=false)
    {
        var rows=new List<string[]>{LandStatisticsWorkbook.Headers};
        rows.AddRange(LandStatisticsWorkbook.Provinces.Select(x=>new[]{x.Key,x.Value,"10","6","3","1"}));
        if(invalid)rows[1][2]="SAMPLE_INVALID_NUMBER";
        using var bytes=new MemoryStream();bytes.Write(InquiryExcelService.Workbook(rows));bytes.Position=0;
        using(var zip=new ZipArchive(bytes,ZipArchiveMode.Update,true))foreach(var entry in zip.Entries)entry.LastWriteTime=new DateTimeOffset(1980,1,1,0,0,0,TimeSpan.Zero);
        return bytes.ToArray();
    }
    // Standard harmless anti-malware test string, kept encoded, never written to source artifacts.
    public static byte[] MalwareControl=>Convert.FromBase64String("WDVPIVAlQEFQWzRcUFpYNTQoUF4pN0NDKTd9JEVJQ0FSLVNUQU5EQVJELUFOVElWSVJVUy1URVNULUZJTEUhJEgrSCo=");
    public static string Sha(byte[] value)=>Convert.ToHexStringLower(SHA256.HashData(value));
    public static IReadOnlySet<string> Hashes()=>new HashSet<string>([Sha(Workbook()),Sha(Workbook(true)),Sha(MalwareControl)],StringComparer.Ordinal);
    public static bool Enabled(IConfiguration config,string environment)
    {
        if(!LanDemoDeployment.Enabled(config))return false;
        LanDemoDeployment.Validate(config,environment);
        if(!OperatingSystem.IsWindows()||config["LanDemo:StatisticsSampleImports"]!="true"||config["Scanner:Provider"]!=Provider||config["Scanner:DefenderLanOperatorSid"]!=WindowsIdentity.GetCurrent().User?.Value)return false;
        var configured=(config["Scanner:DefenderAllowedLanSampleHashes"]??"").Split(',',StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return configured.SetEquals(Hashes())&&config["Scanner:DefenderBinarySha256"] is string pin&&pin.Length==64&&pin.All(Uri.IsHexDigit);
    }
    public static void Require(IConfiguration config,string environment)
    {if(!Enabled(config,environment))throw new InvalidOperationException("LAN statistics requires the explicit signed-engine/operator/public-sample-only policy; no Test or Production exception.");}
}
