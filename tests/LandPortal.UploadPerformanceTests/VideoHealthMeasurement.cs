using System.Diagnostics;
using System.Text.Json;
using LandPortal.Infrastructure;

// No SQL, operational storage or video upload. The input contains only the
// approved local FFmpeg/FFprobe paths and hashes used by the existing test suite.
internal static class VideoHealthMeasurement
{
    public static async Task Run(string toolsFile)
    {
        using var facts=JsonDocument.Parse(await File.ReadAllTextAsync(toolsFile));
        var tools=facts.RootElement;
        var directory=Path.GetFullPath(Path.Combine(".local","tests","ux-video-"+Guid.NewGuid().ToString("N")));
        var storage=new StorageOptions { MediaRoot=Path.Combine(directory,"media"),TempRoot=Path.Combine(directory,"temp"),
            KeysRoot=Path.Combine(directory,"keys"),SnapshotRoot=Path.Combine(directory,"snapshots") };
        foreach(var path in new[]{storage.MediaRoot,storage.TempRoot,storage.KeysRoot,storage.SnapshotRoot})Directory.CreateDirectory(path);
        var options=new VideoOptions { Enabled=true,FfmpegPath=tools.GetProperty("ffmpegPath").GetString()!,
            FfprobePath=tools.GetProperty("ffprobePath").GetString()!,FfmpegSha256=tools.GetProperty("ffmpegSha256").GetString()!,
            FfprobeSha256=tools.GetProperty("ffprobeSha256").GetString()! };
        var processor=new VideoProcessor(options,storage);
        var checks=new List<string>();
        void Check(bool ok,string name) { if(!ok)throw new Exception(name); checks.Add(name); }
        Check(processor.WorkerStatus().Code=="VIDEO_WORKER_STATUS_MISSING","Missing heartbeat never reports ready");
        var samples=new List<object>();
        for(var i=0;i<3;i++)
        {
            var watch=Stopwatch.StartNew();var health=await processor.HealthAsync();var nativeMs=watch.Elapsed.TotalMilliseconds;
            Check(health.Ready,"Real FFmpeg/FFprobe health "+i);
            VideoWorkerStatus.Write(storage,health,false,true);
            watch.Restart();var status=processor.WorkerStatus();var heartbeatMs=watch.Elapsed.TotalMilliseconds;
            Check(status.Code=="VIDEO_WORKER_READY"&&status.Observation?.Tools.Ready==true,"Authenticated observation "+i);
            samples.Add(new{i,nativeMs,heartbeatMs,health.FfmpegVersion});
        }
        var observation=processor.WorkerStatus().Observation!;
        Check(VideoWorkerStatus.StateCode(observation,options.ProcessSeconds,observation.Utc.AddHours(1))=="VIDEO_WORKER_STALE","Expired observation never reports ready");
        VideoWorkerStatus.Write(storage,observation.Tools,false,true,"Stopped");
        Check(processor.WorkerStatus().Code=="VIDEO_WORKER_STOPPED","Stopped processor never reports ready");
        await File.WriteAllTextAsync(Path.Combine(storage.TempRoot,"video-worker-status.dat"),"tampered");
        Check(processor.WorkerStatus().Code=="VIDEO_WORKER_STATUS_UNVERIFIED","Forged observation rejected");
        var result=JsonSerializer.Serialize(new{passed=checks.Count,checks,samples});
        await File.WriteAllTextAsync(Path.Combine(directory,"result.json"),result);
        Console.WriteLine(result);
    }
}
