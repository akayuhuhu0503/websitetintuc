using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace LandPortal.Infrastructure;

// Diagnostic evidence, never an authorization/scan/publication grant. No SQL/schema required.
public sealed record VideoWorkerObservation(DateTimeOffset Utc, string Machine, int ProcessId, string State,
    VideoToolHealth Tools, bool ScannerReady, bool ManagedVideoWithoutAv,
    string? PreviousFfmpegSha256 = null, string? PreviousFfprobeSha256 = null, DateTimeOffset? ToolsChangedUtc = null);

public static class VideoWorkerStatus
{
    private static string StatusPath(StorageOptions storage)
    {
        var path=StorageOptions.ResolveWithin(storage.TempRoot,"video-worker-status.dat");
        VideoToolLocator.RequireNoLinks(path);
        return path;
    }
    private static IDataProtector Protector(StorageOptions storage) => DataProtectionProvider.Create(
        new DirectoryInfo(storage.KeysRoot),b=>b.SetApplicationName("LandPortal")).CreateProtector("LandPortal.VideoWorkerStatus.v1");

    public static VideoWorkerObservation? Read(StorageOptions storage)
    {
        try
        {
            var path=StatusPath(storage);
            if(!File.Exists(path)||new FileInfo(path).Length>32768)return null;
            return JsonSerializer.Deserialize<VideoWorkerObservation>(Protector(storage).Unprotect(File.ReadAllText(path)));
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or CryptographicException or JsonException or VideoProcessingException){return null;}
    }

    public static void Write(StorageOptions storage,VideoToolHealth tools,bool scannerReady,bool noAv,string state="Running")
    {
        var path=StatusPath(storage);
        var old=Read(storage);
        var changed=old?.Tools.FfmpegSha256!=null && tools.FfmpegSha256!=null &&
            (old.Tools.FfmpegSha256!=tools.FfmpegSha256||old.Tools.FfprobeSha256!=tools.FfprobeSha256);
        var observation=new VideoWorkerObservation(DateTimeOffset.UtcNow,Environment.MachineName,Environment.ProcessId,state,
            tools,scannerReady,noAv,changed?old?.Tools.FfmpegSha256:old?.PreviousFfmpegSha256,
            changed?old?.Tools.FfprobeSha256:old?.PreviousFfprobeSha256,changed?DateTimeOffset.UtcNow:old?.ToolsChangedUtc);
        var temporary=path+"."+Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary,Protector(storage).Protect(JsonSerializer.Serialize(observation)));
            File.Move(temporary,path,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }

    public static string StateCode(VideoWorkerObservation? value,int processSeconds,DateTimeOffset? now=null)
    {
        if(value==null)return "VIDEO_WORKER_UNKNOWN";
        var age=(now??DateTimeOffset.UtcNow)-value.Utc;
        if(age<TimeSpan.FromSeconds(-30)||age>TimeSpan.FromSeconds(Math.Clamp(processSeconds,10,1800)+150))return "VIDEO_WORKER_STALE";
        if(value.State=="Stopped")return "VIDEO_WORKER_STOPPED";
        if(value.State!="Running")return "VIDEO_WORKER_ERROR";
        return value.Tools.Ready&&(value.ScannerReady||value.ManagedVideoWithoutAv)?"VIDEO_WORKER_READY":"VIDEO_WORKER_BLOCKED";
    }
}
