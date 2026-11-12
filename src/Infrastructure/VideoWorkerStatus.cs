using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace LandPortal.Infrastructure;

// Diagnostic evidence, never an authorization/scan/publication grant. No SQL/schema required.
public sealed record VideoWorkerObservation(DateTimeOffset Utc, string Machine, int ProcessId, string State,
    VideoToolHealth Tools, bool ScannerReady, bool ManagedVideoWithoutAv,
    string? PreviousFfmpegSha256 = null, string? PreviousFfprobeSha256 = null, DateTimeOffset? ToolsChangedUtc = null);
public sealed record VideoWorkerInspection(VideoWorkerObservation? Observation, string Code);

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

    public static VideoWorkerObservation? Read(StorageOptions storage) => Inspect(storage).Observation;

    public static VideoWorkerInspection Inspect(StorageOptions storage,int processSeconds=300,DateTimeOffset? now=null)
    {
        try
        {
            var path=StatusPath(storage);
            // Open directly: File.Exists would hide access denial as a missing Worker.
            using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(file.Length is 0 or >32768)return new(null,"VIDEO_WORKER_STATUS_INVALID");
            using var reader=new StreamReader(file);
            var observation=JsonSerializer.Deserialize<VideoWorkerObservation>(Protector(storage).Unprotect(reader.ReadToEnd()));
            if(observation is null||observation.Tools is null||string.IsNullOrWhiteSpace(observation.Machine)||observation.ProcessId<=0||observation.Utc==default)
                return new(null,"VIDEO_WORKER_STATUS_INVALID");
            return new(observation,StateCode(observation,processSeconds,now));
        }
        catch(FileNotFoundException){return new(null,"VIDEO_WORKER_STATUS_MISSING");}
        catch(DirectoryNotFoundException){return new(null,"VIDEO_WORKER_STATUS_MISSING");}
        catch(UnauthorizedAccessException){return new(null,"VIDEO_WORKER_STATUS_ACCESS_DENIED");}
        catch(System.Security.SecurityException){return new(null,"VIDEO_WORKER_STATUS_ACCESS_DENIED");}
        catch(CryptographicException){return new(null,"VIDEO_WORKER_STATUS_UNVERIFIED");}
        catch(System.Xml.XmlException){return new(null,"VIDEO_WORKER_STATUS_UNVERIFIED");}
        catch(JsonException){return new(null,"VIDEO_WORKER_STATUS_INVALID");}
        catch(VideoProcessingException){return new(null,"VIDEO_WORKER_STATUS_PATH_UNSAFE");}
        catch(InvalidOperationException){return new(null,"VIDEO_WORKER_STATUS_UNVERIFIED");}
        catch(IOException){return new(null,"VIDEO_WORKER_STATUS_READ_FAILED");}
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
