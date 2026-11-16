using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

static class VideoMeasurement
{
    public static async Task Run(DbContextOptions<PortalDbContext> dbOptions,HttpClient writer,string toolsFile,string run,Func<HttpClient,Task<string>> token,Action<bool,string> check)
    {
        var tools=JsonDocument.Parse(await File.ReadAllTextAsync(toolsFile)).RootElement;
        var sample=Path.Combine(run,"synthetic.mp4");var exe=tools.GetProperty("ffmpegPath").GetString()!;
        await using(var f=File.OpenRead(exe))if(Convert.ToHexStringLower(await SHA256.HashDataAsync(f))!=tools.GetProperty("ffmpegSha256").GetString()!.ToLowerInvariant())throw new Exception("Known fixture FFmpeg hash mismatch");
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-nostdin","-v","error","-f","lavfi","-i","testsrc2=size=320x180:rate=24","-f","lavfi","-i","sine=frequency=440:sample_rate=44100","-t","2","-threads","1","-c:v","libx264","-pix_fmt","yuv420p","-c:a","aac",sample})start.ArgumentList.Add(arg);
        using(var p=Process.Start(start)!){var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await p.WaitForExitAsync(deadline.Token);await output;await errors;if(p.ExitCode!=0)throw new Exception("Synthetic MP4 generation failed");}finally{if(!p.HasExited)p.Kill(true);}}
        var bytes=await File.ReadAllBytesAsync(sample);var hash=Convert.ToHexStringLower(SHA256.HashData(bytes));
        using(var list=await writer.GetAsync("/admin/videos")){await list.Content.ReadAsStringAsync();check(list.IsSuccessStatusCode,"Authorized video list measured");}
        var fields=new Dictionary<string,string>{{"Kind","Video"},{"Title","ISOLATED PERFORMANCE VIDEO"},{"Summary","Synthetic only"},{"Author","Fixture"},{"BodyHtml","<p>Fixture only</p>"},{"CategoryId","1"},{"intent","Save"},{"format","json"},{"RequestId",Guid.NewGuid().ToString()},{"__RequestVerificationToken",await token(writer)}};
        using var saved=await writer.PostAsync("/admin/save",new FormUrlEncodedContent(fields));saved.EnsureSuccessStatusCode();var cid=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("contentId").GetGuid();
        async Task<JsonElement> Post(string route,object body){using var req=new HttpRequestMessage(HttpMethod.Post,route){Content=JsonContent.Create(body)};req.Headers.Add("X-CSRF-TOKEN",await token(writer));using var response=await writer.SendAsync(req);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<JsonElement>();}
        var watch=Stopwatch.StartNew();var session=(await Post("/admin/videos/sessions",new{contentId=cid,name="synthetic.mp4",mime="video/mp4",bytes=bytes.Length,chunkManifestSha256=TextNormalizer.Hash($"0:{bytes.Length}:{hash}"),autoAttach=true})).GetProperty("id").GetGuid();var sessionMs=watch.Elapsed.TotalMilliseconds;watch.Restart();
        using(var req=new HttpRequestMessage(HttpMethod.Put,$"/admin/videos/sessions/{session}/chunks/0"){Content=new ByteArrayContent(bytes)}){req.Headers.Add("X-Chunk-SHA256",hash);req.Headers.Add("X-CSRF-TOKEN",await token(writer));using var response=await writer.SendAsync(req);response.EnsureSuccessStatusCode();}var chunkMs=watch.Elapsed.TotalMilliseconds;watch.Restart();
        var id=(await Post($"/admin/videos/sessions/{session}/complete",new{})).GetProperty("mediaAssetId").GetGuid();var completeMs=watch.Elapsed.TotalMilliseconds;watch.Restart();
        var polls=0;var ready=false;
        while(watch.Elapsed<TimeSpan.FromSeconds(90)){using var state=await writer.GetAsync($"/admin/videos/sessions/{session}");state.EnsureSuccessStatusCode();polls++;var result=await state.Content.ReadFromJsonAsync<JsonElement>();if(result.GetProperty("job").GetProperty("state").GetString()=="Ready"){ready=true;break;}await Task.Delay(1000);}
        check(ready,"Web background video reaches Ready");var processingMs=watch.Elapsed.TotalMilliseconds;
        await using var db=new PortalDbContext(dbOptions);var video=await db.Set<VideoFile>().Include(x=>x.Original).SingleAsync(x=>x.MediaAssetId==id);
        check(video.Original.ScanStatus=="NotScanned"&&video.Original.ValidationStatus==VideoOptions.NoAvValidated&&video.ScannedUtc==null,"Video remains truthful NotScanned");
        check(File.Exists(Path.Combine(run,"media",video.Mp4Key!.Replace('/',Path.DirectorySeparatorChar)))&&File.Exists(Path.Combine(run,"media",video.PosterKey!.Replace('/',Path.DirectorySeparatorChar))),"Generated MP4 and poster exist");
        await File.WriteAllTextAsync(Path.Combine(run,"video-timing.json"),JsonSerializer.Serialize(new{bytes=bytes.Length,sessionMs,chunkMs,completeMs,processingAndPollingMs=processingMs,polls,state="Ready",scan="NotScanned",scope="One synthetic 2s MP4; queue/polling included; not an IIS throughput benchmark"}));
    }
}
