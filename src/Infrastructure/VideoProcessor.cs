using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
namespace LandPortal.Infrastructure;
public sealed class VideoProcessingException(string code,bool blocked=false):Exception(code){public bool Blocked {get;}=blocked;}
public sealed record VideoProbe(string Codec,string? Audio,int Width,int Height,double Duration,string Container,int Rotation)
{
    public string? PixelFormat {get;init;}
    public string? Profile {get;init;}
    public string? AudioProfile {get;init;}
    public double FrameRate {get;init;}
    public bool UsedRateFallback {get;init;}
}
public sealed record VideoOutput(string Mp4,string Mp4Hash,long Mp4Bytes,string Poster,string PosterHash,long PosterBytes,VideoProbe Probe,VideoProbe OriginalProbe){public bool Reused {get;init;} public double NativeCpuMilliseconds {get;init;}}
// Uses operator-installed, license-approved tools. Observed hashes detect change, not provenance.
public sealed class VideoProcessor(VideoOptions options,StorageOptions storage,VideoToolLocator? locator=null)
{
    private double nativeCpuMilliseconds;
    private VideoToolPaths? tools;
    private string? ffmpegHash,ffprobeHash;
    public VideoToolHealth? LastHealth {get;private set;}
    public VideoWorkerObservation? WorkerObservation()=>VideoWorkerStatus.Read(storage);
    public static bool BrowserCompatible(VideoProbe p)=>p.Codec=="h264"&&p.PixelFormat=="yuv420p"&&(p.Profile is "Baseline" or "Constrained Baseline" or "Main" or "High")&&(p.Audio==null||p.Audio=="aac"&&p.AudioProfile=="LC");
    private sealed record SavedOutput(int Policy,Guid Asset,string InputSha,string FfmpegSha,string FfprobeSha,VideoOutput Output);
    private IDataProtector ManifestProtector()=>DataProtectionProvider.Create(new DirectoryInfo(storage.KeysRoot),b=>b.SetApplicationName("LandPortal")).CreateProtector("LandPortal.VideoOutputManifest.v1");
    private async Task<VideoOutput?> ReuseAsync(Guid asset,string inputHash,CancellationToken ct,bool forceTranscode)
    {
        var root=Path.GetDirectoryName(MediaService.ResolveMediaPath(storage,$"video-output/{asset:N}/boundary"))!;if(!Directory.Exists(root))return null;
        foreach(var dir in Directory.EnumerateDirectories(root).Take(20))
        {
            if(!Guid.TryParseExact(Path.GetFileName(dir),"N",out _)||(File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)continue;
            var manifest=Path.Combine(dir,"result.dat");if(!File.Exists(manifest)||new FileInfo(manifest).Length>131072)continue;
            try
            {
                var saved=JsonSerializer.Deserialize<SavedOutput>(ManifestProtector().Unprotect(await File.ReadAllTextAsync(manifest,ct)));
                if(saved==null||saved.Policy!=(forceTranscode?3:2)||saved.Asset!=asset||saved.InputSha!=inputHash||saved.FfmpegSha!=ffmpegHash||saved.FfprobeSha!=ffprobeHash)continue;var result=saved.Output;
                if(!result.Mp4.StartsWith($"video-output/{asset:N}/",StringComparison.Ordinal)||!result.Poster.StartsWith($"video-output/{asset:N}/",StringComparison.Ordinal))continue;
                var mp4=MediaService.ResolveMediaPath(storage,result.Mp4);var poster=MediaService.ResolveMediaPath(storage,result.Poster);
                if(!File.Exists(mp4)||!File.Exists(poster)||new FileInfo(mp4).Length!=result.Mp4Bytes||new FileInfo(poster).Length!=result.PosterBytes||await VideoUploadService.HashFileAsync(mp4,ct)!=result.Mp4Hash||await VideoUploadService.HashFileAsync(poster,ct)!=result.PosterHash)continue;
                var actual=await ProbeAsync(mp4,ct);if(!BrowserCompatible(actual)||Math.Abs(actual.Duration-result.Probe.Duration)>0.1)continue;return result with{Reused=true,Probe=actual};
            }
            catch(Exception e)when(e is CryptographicException or JsonException or IOException or VideoProcessingException or InvalidOperationException or MediaUploadException){/* Invalid cache is not a scan/publication grant. */}
        }
        return null;
    }
    public async Task<VideoToolHealth> HealthAsync(CancellationToken ct=default)
    {
        var previous=LastHealth;
        var status=new VideoToolHealth(false,"VIDEO_PROCESSOR_NOT_AVAILABLE");
        try
        {
            var found=(locator??new VideoToolLocator()).Find(options);
            status=status with{FfmpegFound=found.Ffmpeg!=null,FfprobeFound=found.Ffprobe!=null};
            if(found.Ffmpeg==null || found.Ffprobe==null)return LastHealth=status;
            if(!options.ProcessorApproved||string.IsNullOrWhiteSpace(options.LicenseReference))
                return LastHealth=status with{Code="VIDEO_PROCESSOR_NOT_APPROVED"};
            foreach(var binary in new[]{found.Ffmpeg,found.Ffprobe})
                foreach(var root in new[]{storage.MediaRoot,storage.TempRoot,storage.KeysRoot,storage.SnapshotRoot})
                    if(binary.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                        throw new VideoProcessingException("VIDEO_TOOL_PATH_UNSAFE",true);
            var first=await VideoUploadService.HashFileAsync(found.Ffmpeg,ct);
            var second=await VideoUploadService.HashFileAsync(found.Ffprobe,ct);
            status=status with{FfmpegSha256=first,FfprobeSha256=second,BinaryChanged=previous?.FfmpegSha256 is not null && (previous.FfmpegSha256!=first||previous.FfprobeSha256!=second)};
            if((!string.IsNullOrEmpty(options.FfmpegSha256)&&!string.Equals(options.FfmpegSha256,first,StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrEmpty(options.FfprobeSha256)&&!string.Equals(options.FfprobeSha256,second,StringComparison.OrdinalIgnoreCase)))
                return LastHealth=status with{Code="VIDEO_PROCESSOR_HASH_MISMATCH"};
            tools=found;ffmpegHash=first;ffprobeHash=second;
            status=status with{FfmpegVersion=Version(await Run(found.Ffmpeg,["-version"],10,ct),"ffmpeg"),
                FfprobeVersion=Version(await Run(found.Ffprobe,["-version"],10,ct),"ffprobe")};
            var encoders=await Run(found.Ffmpeg,["-hide_banner","-encoders"],10,ct);
            if(!HasRequiredEncoders(encoders))return LastHealth=status with{Code="VIDEO_ENCODER_NOT_AVAILABLE"};
            return LastHealth=status with{Ready=true,Code="VIDEO_PROCESSOR_CONFIGURED"};
        }
        catch(VideoProcessingException e){return LastHealth=status with{Code=e.Message};}
        catch(UnauthorizedAccessException){return LastHealth=status with{Code="VIDEO_PROCESSOR_ACCESS_DENIED"};}
        catch(System.ComponentModel.Win32Exception){return LastHealth=status with{Code="VIDEO_PROCESSOR_START_FAILED"};}
        catch(IOException){return LastHealth=status with{Code="VIDEO_PROCESSOR_IO_ERROR"};}
    }
    public static bool HasRequiredEncoders(string text)
    {
        var names=text.Split('\n').Select(line=>line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries))
            .Where(parts=>parts.Length>=2&&parts[0].Length==6&&parts[0][0] is 'V' or 'A').Select(parts=>parts[1]).ToHashSet(StringComparer.Ordinal);
        return names.Contains("libx264")&&names.Contains("aac")&&names.Contains("mjpeg");
    }
    private static string Version(string output,string tool)
    {
        var match=System.Text.RegularExpressions.Regex.Match(output,@"\A"+tool+@" version ([A-Za-z0-9_.+\-]{1,100})(?:\s|$)",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
        if(!match.Success)throw new VideoProcessingException("VIDEO_PROCESSOR_VERSION_INVALID",true);
        return match.Groups[1].Value;
    }
    internal async Task<string> Run(string binary,IEnumerable<string> arguments,int seconds,CancellationToken ct)
    {
        VideoToolLocator.RequireNoLinks(binary);
        await using var binaryLock=new FileStream(binary,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
        var expected=string.Equals(binary,tools?.Ffmpeg,StringComparison.OrdinalIgnoreCase)?ffmpegHash:ffprobeHash;
        if(expected==null||Convert.ToHexStringLower(await SHA256.HashDataAsync(binaryLock,ct))!=expected)
            throw new VideoProcessingException("VIDEO_PROCESSOR_CHANGED_DURING_CHECK",true);
        var p=new ProcessStartInfo(binary){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=storage.TempRoot};p.ArgumentList.Add("-max_alloc");p.ArgumentList.Add("67108864");foreach(var arg in arguments)p.ArgumentList.Add(arg);
        // Native tools inherit the service token, never elevate or receive SQL/runtime secrets.
        // Keep only OS loader variables; operators run the worker as a dedicated non-admin identity.
        foreach(var key in p.Environment.Keys.Where(k=>!new[]{"SystemRoot","WINDIR","PATH","PATHEXT"}.Contains(k,StringComparer.OrdinalIgnoreCase)).ToArray())p.Environment.Remove(key);
        p.Environment["TEMP"]=storage.TempRoot;p.Environment["TMP"]=storage.TempRoot;
        using var process=Process.Start(p)??throw new VideoProcessingException("VIDEO_PROCESSOR_START_FAILED");using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        async Task<string> Read(StreamReader reader){var buffer=new char[1024];var text=new System.Text.StringBuilder();int n;while((n=await reader.ReadAsync(buffer,deadline.Token))>0){if(text.Length+n>131072)throw new VideoProcessingException("VIDEO_PROCESSOR_OUTPUT_LIMIT");text.Append(buffer,0,n);}return text.ToString();}
        var stdout=Read(process.StandardOutput);var stderr=Read(process.StandardError);
        async Task MemoryWatch()
        {
            while(true)
            {
                long workingSet;
                try{if(process.HasExited)return;process.Refresh();workingSet=process.WorkingSet64;}
                catch(InvalidOperationException)when(process.HasExited){return;}
                if(workingSet>512L*1024*1024)
                {
                    try{if(!process.HasExited)process.Kill(entireProcessTree:true);}catch(InvalidOperationException)when(process.HasExited){}
                    throw new VideoProcessingException("VIDEO_PROCESSOR_MEMORY_LIMIT");
                }
                await Task.Delay(100,deadline.Token);
            }
        }
        try{await Task.WhenAll(process.WaitForExitAsync(deadline.Token),stdout,stderr,MemoryWatch());if(process.ExitCode!=0)throw new VideoProcessingException("VIDEO_PROCESSOR_REJECTED");return await stdout;}
        catch(OperationCanceledException){throw new VideoProcessingException(ct.IsCancellationRequested?"VIDEO_PROCESSOR_CANCELLED":"VIDEO_PROCESSOR_TIMEOUT");}
        finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync(CancellationToken.None);}nativeCpuMilliseconds+=process.TotalProcessorTime.TotalMilliseconds;}
    }
    public async Task<VideoProbe> ProbeAsync(string path,CancellationToken ct=default)
    {
        if(LastHealth?.Ready!=true){var health=await HealthAsync(ct);if(!health.Ready)throw new VideoProcessingException(health.Code,true);}
        var json=await Run(tools!.Ffprobe!,["-v","error","-protocol_whitelist","file,pipe","-show_entries","stream=codec_type,codec_name,width,height,avg_frame_rate,r_frame_rate,pix_fmt,profile:stream_side_data=rotation:format=duration,format_name","-of","json",path],30,ct);
        using var doc=JsonDocument.Parse(json);var streams=doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();var video=streams.Where(x=>x.GetProperty("codec_type").GetString()=="video").ToArray();var audio=streams.Where(x=>x.GetProperty("codec_type").GetString()=="audio").ToArray();
        if(video.Length!=1||audio.Length>1||streams.Length!=video.Length+audio.Length)throw new VideoProcessingException("VIDEO_STREAMS_UNSUPPORTED");
        var width=video[0].GetProperty("width").GetInt32();var height=video[0].GetProperty("height").GetInt32();var duration=double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!,CultureInfo.InvariantCulture);
        double Rate(string field){if(!video[0].TryGetProperty(field,out var value))return double.NaN;var parts=(value.GetString()??"").Split('/');if(!double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var numerator))return double.NaN;var denominator=1d;if(parts.Length>1&&!double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out denominator))return double.NaN;return denominator==0?double.NaN:numerator/denominator;}
        var rate=Rate("avg_frame_rate");var fallback=!double.IsFinite(rate)||rate<=0;if(fallback)rate=Rate("r_frame_rate");
        if(width<2||height<2||(long)width*height>3840L*2160||duration<=0||!double.IsFinite(duration)||duration>3600||!double.IsFinite(rate)||rate<=0||rate>60)throw new VideoProcessingException("VIDEO_LIMITS_EXCEEDED");
        var codec=video[0].GetProperty("codec_name").GetString()!;var ac=audio.Length==0?null:audio[0].GetProperty("codec_name").GetString();if(codec is not("h264" or "hevc" or "vp8" or "vp9" or "av1")||ac is not(null or "aac" or "opus" or "vorbis" or "mp3" or "pcm_s16le"))throw new VideoProcessingException("VIDEO_CODEC_UNSUPPORTED");
        var container=doc.RootElement.GetProperty("format").GetProperty("format_name").GetString()!;if(!container.Contains("mp4",StringComparison.Ordinal)&&!container.Contains("mov",StringComparison.Ordinal)&&!container.Contains("webm",StringComparison.Ordinal))throw new VideoProcessingException("VIDEO_CONTAINER_UNSUPPORTED");
        var rotation=video[0].TryGetProperty("side_data_list",out var side)?side.EnumerateArray().Where(x=>x.TryGetProperty("rotation",out _)).Select(x=>x.GetProperty("rotation").GetInt32()).FirstOrDefault():0;
        return new(codec,ac,width,height,duration,container,(rotation%360+360)%360){PixelFormat=video[0].TryGetProperty("pix_fmt",out var pixel)?pixel.GetString():null,Profile=video[0].TryGetProperty("profile",out var profile)?profile.GetString():null,AudioProfile=audio.Length>0&&audio[0].TryGetProperty("profile",out var ap)?ap.GetString():null,FrameRate=rate,UsedRateFallback=fallback};
    }
    public async Task<VideoOutput> ProcessAsync(Guid asset,string input,CancellationToken ct=default,bool forceTranscode=false)
    {
        var health=await HealthAsync(ct);if(!health.Ready)throw new VideoProcessingException(health.Code,true);
        var cpuStart=nativeCpuMilliseconds;var inputHash=await VideoUploadService.HashFileAsync(input,ct);var reused=await ReuseAsync(asset,inputHash,ct,forceTranscode);if(reused!=null)return reused with{NativeCpuMilliseconds=nativeCpuMilliseconds-cpuStart};
        var before=await ProbeAsync(input,ct);var folder=MediaService.ResolveMediaPath(storage,$"video-output/{asset:N}/{Guid.NewGuid():N}/marker");var directory=Path.GetDirectoryName(folder)!;Directory.CreateDirectory(directory);var output=Path.Combine(directory,"play.mp4");var poster=Path.Combine(directory,"poster.jpg");
        var compatible=!forceTranscode && BrowserCompatible(before);
        var args=new List<string>{"-nostdin","-hide_banner","-loglevel","error","-protocol_whitelist","file,pipe","-threads",options.Threads.ToString(CultureInfo.InvariantCulture),"-i",input,"-map","0:v:0","-map","0:a:0?","-map_metadata","-1","-map_chapters","-1"};
        if(compatible)args.AddRange(["-c","copy"]);
        else
        {
            args.AddRange(["-c:v","libx264","-preset","veryfast","-crf","23","-pix_fmt","yuv420p","-fpsmax","60","-threads",options.Threads.ToString(CultureInfo.InvariantCulture),"-vf","scale=w='min(1920,iw)':h='min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2","-c:a","aac","-profile:a","aac_low","-b:a","128k"]);
        }
        args.AddRange(["-movflags","+faststart","-fs",Math.Min(new FileInfo(input).Length*3,options.QuotaBytes/3).ToString(CultureInfo.InvariantCulture),"-f","mp4",output]);await Run(tools!.Ffmpeg!,args,options.ProcessSeconds,ct);
        var after=await ProbeAsync(output,ct);if(!BrowserCompatible(after)||Math.Abs(before.Duration-after.Duration)>1||after.Width>Math.Max(before.Width,before.Height)||after.Height>Math.Max(before.Width,before.Height))throw new VideoProcessingException("VIDEO_OUTPUT_VERIFY_FAILED");
        await Run(tools!.Ffmpeg!,["-nostdin","-hide_banner","-loglevel","error","-protocol_whitelist","file,pipe","-threads",options.Threads.ToString(CultureInfo.InvariantCulture),"-ss",Math.Min(1,after.Duration/2).ToString(CultureInfo.InvariantCulture),"-i",output,"-frames:v","1","-vf","scale=w='min(960,iw)':h='min(540,ih)':force_original_aspect_ratio=decrease","-f","image2",poster],60,ct);
        using(var codec=SkiaSharp.SKCodec.Create(poster)){if(codec==null||codec.Info.Width<1||codec.Info.Height<1)throw new VideoProcessingException("VIDEO_POSTER_VERIFY_FAILED");}
        var mp4=new FileInfo(output);var jpg=new FileInfo(poster);if(mp4.Length<32||jpg.Length<16||mp4.Length>options.MaxFileBytes*3||jpg.Length>16*1024*1024)throw new VideoProcessingException("VIDEO_OUTPUT_SIZE_FAILED");
        string Key(string p)=>Path.GetRelativePath(storage.MediaRoot,p).Replace('\\','/');var result=new VideoOutput(Key(output),await VideoUploadService.HashFileAsync(output,ct),mp4.Length,Key(poster),await VideoUploadService.HashFileAsync(poster,ct),jpg.Length,after,before){NativeCpuMilliseconds=nativeCpuMilliseconds-cpuStart};
        var receipt=ManifestProtector().Protect(JsonSerializer.Serialize(new SavedOutput(forceTranscode?3:2,asset,inputHash,ffmpegHash!,ffprobeHash!,result)));await File.WriteAllTextAsync(Path.Combine(directory,"result.dat"),receipt,ct);return result;
    }
}
