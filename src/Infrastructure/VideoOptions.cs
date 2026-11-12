using Microsoft.Extensions.Configuration;
namespace LandPortal.Infrastructure;
public sealed class VideoOptions
{
    public bool Enabled { get; init; }
    public bool ApprovedManagedVideoWithoutAv { get; init; }
    public const string NoAvPending = "VideoNoAvPending";
    public const string NoAvValidated = "VideoTranscodedNoAv";
    public long MaxFileBytes { get; init; }=1024L*1024*1024;
    public int ChunkBytes { get; init; }=8*1024*1024;
    public long QuotaBytes { get; init; }=16L*1024*1024*1024;
    public long MinimumFreeBytes { get; init; }=512L*1024*1024;
    public int MaxSessions { get; init; }=3;
    public int SessionHours { get; init; }=24;
    public int ProcessSeconds { get; init; }=300;
    public int Threads { get; init; }=2;
    public string FfmpegPath { get; init; }="";
    public string FfprobePath { get; init; }="";
    public string FfmpegSha256 { get; init; }="";
    public string FfprobeSha256 { get; init; }="";
    public static VideoOptions FromConfiguration(IConfiguration c,string environment)
    {
        long Number(string k,long fallback,long min,long max){var raw=c["Video:"+k];if(raw is null)return fallback;if(!long.TryParse(raw,out var value)||value<min||value>max)throw new InvalidOperationException("Invalid video limit: "+k);return value;}
        var enabled=bool.TryParse(c["Video:Enabled"],out var requested)&&requested;
        var managedVideo=bool.TryParse(c["Video:AllowManagedVideoWithoutAv"],out var requestedNoAv)&&requestedNoAv;
        var storage=StorageOptions.FromConfiguration(c);var local=c["WorkspaceRoot"] is string root&&new[]{storage.MediaRoot,storage.TempRoot,storage.KeysRoot}.All(x=>Path.GetFullPath(x).StartsWith(Path.Combine(root,".local","tests")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
        return new(){Enabled=enabled&&(environment=="Development"&&c["Sql:ExpectedDatabase"]=="LandPortal_Test"&&local||LanDemoDeployment.Validate(c,environment)||UatDeployment.Validate(c,environment)),
            ApprovedManagedVideoWithoutAv=enabled && environment=="Staging" && UatDeployment.Validate(c,environment) && managedVideo,
            MaxFileBytes=Number("MaxFileBytes",1024L*1024*1024,1024,1024L*1024*1024),ChunkBytes=(int)Number("ChunkBytes",8*1024*1024,1024,8*1024*1024),QuotaBytes=Number("QuotaBytes",16L*1024*1024*1024,1024*1024,128L*1024*1024*1024),MinimumFreeBytes=Number("MinimumFreeBytes",512L*1024*1024,1024*1024,1024L*1024*1024*1024),MaxSessions=(int)Number("MaxSessions",3,1,10),SessionHours=(int)Number("SessionHours",24,1,48),ProcessSeconds=(int)Number("ProcessSeconds",300,10,1800),Threads=(int)Number("Threads",2,1,4),FfmpegPath=c["Video:FfmpegPath"]??"",FfprobePath=c["Video:FfprobePath"]??"",FfmpegSha256=c["Video:FfmpegSha256"]??"",FfprobeSha256=c["Video:FfprobeSha256"]??""};
    }
}
