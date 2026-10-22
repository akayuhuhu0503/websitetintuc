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
// Requires operator-supplied, hash-pinned and license-reviewed binaries. No downloader.
public sealed class VideoProcessor(VideoOptions options,StorageOptions storage)
{
    private double nativeCpuMilliseconds;
    public static bool BrowserCompatible(VideoProbe p)=>p.Codec=="h264"&&p.PixelFormat=="yuv420p"&&(p.Profile is "Baseline" or "Constrained Baseline" or "Main" or "High")&&(p.Audio==null||p.Audio=="aac"&&p.AudioProfile=="LC");
    private sealed record SavedOutput(int Policy,Guid Asset,string InputSha,string FfmpegSha,string FfprobeSha,VideoOutput Output);
    private IDataProtector ManifestProtector()=>DataProtectionProvider.Create(new DirectoryInfo(storage.KeysRoot),b=>b.SetApplicationName("LandPortal")).CreateProtector("LandPortal.VideoOutputManifest.v1");
    private async Task<VideoOutput?> ReuseAsync(Guid asset,string inputHash,CancellationToken ct)
    {
        var root=Path.GetDirectoryName(MediaService.ResolveMediaPath(storage,$"video-output/{asset:N}/boundary"))!;if(!Directory.Exists(root))return null;
        foreach(var dir in Directory.EnumerateDirectories(root).Take(20))
        {
            if(!Guid.TryParseExact(Path.GetFileName(dir),"N",out _)||(File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)continue;
            var manifest=Path.Combine(dir,"result.dat");if(!File.Exists(manifest)||new FileInfo(manifest).Length>131072)continue;
            try
            {
                var saved=JsonSerializer.Deserialize<SavedOutput>(ManifestProtector().Unprotect(await File.ReadAllTextAsync(manifest,ct)));
                if(saved==null||saved.Policy!=2||saved.Asset!=asset||saved.InputSha!=inputHash||saved.FfmpegSha!=options.FfmpegSha256||saved.FfprobeSha!=options.FfprobeSha256)continue;var result=saved.Output;
                if(!result.Mp4.StartsWith($"video-output/{asset:N}/",StringComparison.Ordinal)||!result.Poster.StartsWith($"video-output/{asset:N}/",StringComparison.Ordinal))continue;
                var mp4=MediaService.ResolveMediaPath(storage,result.Mp4);var poster=MediaService.ResolveMediaPath(storage,result.Poster);
                if(!File.Exists(mp4)||!File.Exists(poster)||new FileInfo(mp4).Length!=result.Mp4Bytes||new FileInfo(poster).Length!=result.PosterBytes||await VideoUploadService.HashFileAsync(mp4,ct)!=result.Mp4Hash||await VideoUploadService.HashFileAsync(poster,ct)!=result.PosterHash)continue;
                var actual=await ProbeAsync(mp4,ct);if(!BrowserCompatible(actual)||Math.Abs(actual.Duration-result.Probe.Duration)>0.1)continue;return result with{Reused=true,Probe=actual};
            }
            catch(Exception e)when(e is CryptographicException or JsonException or IOException or VideoProcessingException or InvalidOperationException or MediaUploadException){/* Invalid cache is not a scan/publication grant. */}
        }
        return null;
    }
    public async Task<(bool Ready,string Code)> HealthAsync(CancellationToken ct=default)
    {
        if(!Path.IsPathFullyQualified(options.FfmpegPath)||!File.Exists(options.FfmpegPath)||!Path.IsPathFullyQualified(options.FfprobePath)||!File.Exists(options.FfprobePath))return(false,"VIDEO_PROCESSOR_NOT_AVAILABLE");
        if(!options.ProcessorApproved||string.IsNullOrWhiteSpace(options.LicenseReference))return(false,"VIDEO_PROCESSOR_NOT_APPROVED");
        foreach(var item in new[]{(options.FfmpegPath,options.FfmpegSha256),(options.FfprobePath,options.FfprobeSha256)})
        {if(!Path.IsPathFullyQualified(item.Item1)||!File.Exists(item.Item1))return(false,"VIDEO_PROCESSOR_NOT_AVAILABLE");if(!VideoUploadService.IsHash(item.Item2)||await VideoUploadService.HashFileAsync(item.Item1,ct)!=item.Item2)return(false,"VIDEO_PROCESSOR_HASH_MISMATCH");}
        return(true,"VIDEO_PROCESSOR_CONFIGURED");
    }
    internal async Task<string> Run(string binary,IEnumerable<string> arguments,int seconds,CancellationToken ct)
    {
        var p=new ProcessStartInfo(binary){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=storage.TempRoot};p.ArgumentList.Add("-max_alloc");p.ArgumentList.Add("67108864");foreach(var arg in arguments)p.ArgumentList.Add(arg);
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
        var json=await Run(options.FfprobePath,["-v","error","-protocol_whitelist","file,pipe","-show_entries","stream=codec_type,codec_name,width,height,avg_frame_rate,r_frame_rate,pix_fmt,profile:stream_side_data=rotation:format=duration,format_name","-of","json",path],30,ct);
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
    public async Task<VideoOutput> ProcessAsync(Guid asset,string input,CancellationToken ct=default)
    {
        var health=await HealthAsync(ct);if(!health.Ready)throw new VideoProcessingException(health.Code,true);
        var cpuStart=nativeCpuMilliseconds;var inputHash=await VideoUploadService.HashFileAsync(input,ct);var reused=await ReuseAsync(asset,inputHash,ct);if(reused!=null)return reused with{NativeCpuMilliseconds=nativeCpuMilliseconds-cpuStart};
        var before=await ProbeAsync(input,ct);var folder=MediaService.ResolveMediaPath(storage,$"video-output/{asset:N}/{Guid.NewGuid():N}/marker");var directory=Path.GetDirectoryName(folder)!;Directory.CreateDirectory(directory);var output=Path.Combine(directory,"play.mp4");var poster=Path.Combine(directory,"poster.jpg");
        var compatible=BrowserCompatible(before);
        var args=new List<string>{"-nostdin","-hide_banner","-loglevel","error","-protocol_whitelist","file,pipe","-threads",options.Threads.ToString(CultureInfo.InvariantCulture),"-i",input,"-map","0:v:0","-map","0:a:0?","-map_metadata","-1","-map_chapters","-1"};
        if(compatible)args.AddRange(["-c","copy"]);
        else
        {
            var encoders=await Run(options.FfmpegPath,["-hide_banner","-encoders"],10,ct);if(!encoders.Contains("libx264",StringComparison.Ordinal))throw new VideoProcessingException("VIDEO_ENCODER_NOT_AVAILABLE",true);
            args.AddRange(["-c:v","libx264","-preset","veryfast","-crf","23","-pix_fmt","yuv420p","-fpsmax","60","-threads",options.Threads.ToString(CultureInfo.InvariantCulture),"-vf","scale=w='min(1920,iw)':h='min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2","-c:a","aac","-profile:a","aac_low","-b:a","128k"]);
        }
        args.AddRange(["-movflags","+faststart","-fs",Math.Min(new FileInfo(input).Length*3,options.QuotaBytes/3).ToString(CultureInfo.InvariantCulture),"-f","mp4",output]);await Run(options.FfmpegPath,args,options.ProcessSeconds,ct);
        var after=await ProbeAsync(output,ct);if(!BrowserCompatible(after)||Math.Abs(before.Duration-after.Duration)>1||after.Width>Math.Max(before.Width,before.Height)||after.Height>Math.Max(before.Width,before.Height))throw new VideoProcessingException("VIDEO_OUTPUT_VERIFY_FAILED");
        await Run(options.FfmpegPath,["-nostdin","-hide_banner","-loglevel","error","-protocol_whitelist","file,pipe","-threads",options.Threads.ToString(CultureInfo.InvariantCulture),"-ss",Math.Min(1,after.Duration/2).ToString(CultureInfo.InvariantCulture),"-i",output,"-frames:v","1","-vf","scale=w='min(960,iw)':h='min(540,ih)':force_original_aspect_ratio=decrease","-f","image2",poster],60,ct);
        using(var codec=SkiaSharp.SKCodec.Create(poster)){if(codec==null||codec.Info.Width<1||codec.Info.Height<1)throw new VideoProcessingException("VIDEO_POSTER_VERIFY_FAILED");}
        var mp4=new FileInfo(output);var jpg=new FileInfo(poster);if(mp4.Length<32||jpg.Length<16||mp4.Length>options.MaxFileBytes*3||jpg.Length>16*1024*1024)throw new VideoProcessingException("VIDEO_OUTPUT_SIZE_FAILED");
        string Key(string p)=>Path.GetRelativePath(storage.MediaRoot,p).Replace('\\','/');var result=new VideoOutput(Key(output),await VideoUploadService.HashFileAsync(output,ct),mp4.Length,Key(poster),await VideoUploadService.HashFileAsync(poster,ct),jpg.Length,after,before){NativeCpuMilliseconds=nativeCpuMilliseconds-cpuStart};
        var receipt=ManifestProtector().Protect(JsonSerializer.Serialize(new SavedOutput(2,asset,inputHash,options.FfmpegSha256,options.FfprobeSha256,result)));await File.WriteAllTextAsync(Path.Combine(directory,"result.dat"),receipt,ct);return result;
    }
}
