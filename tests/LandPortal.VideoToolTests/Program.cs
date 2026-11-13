using System.Diagnostics;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using LandPortal.Infrastructure;

if(args.Length==2 && args[0]=="--bundled-tools")
{
    await BundledToolChecks.RunAsync(args[1]);
    return;
}

if(args.Length==1 && args[0]=="--worker-status")
{
    WorkerStatusChecks.Run();
    return;
}

if(args.Length==2 && args[0]=="--iis-runtime")
{
    await LocatorRuntimeChecks.RunAsync(args[1]);
    WorkerStatusChecks.Run();
    return;
}

if(args.Length==2 && args[0]=="--configuration")
{
    MinimalConfigurationChecks.Run(args[1]);
    return;
}

var repo=Path.GetFullPath(args.Length>0?args[0]:Environment.CurrentDirectory);
var run=Path.Combine(repo,".local","tests","video-tools-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(run);
var passed=new List<string>();
void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);passed.Add(name);Console.WriteLine("PASS "+name);}
var storage=new StorageOptions{MediaRoot=Path.Combine(run,"media"),TempRoot=Path.Combine(run,"temp"),KeysRoot=Path.Combine(run,"keys"),SnapshotRoot=Path.Combine(run,"snapshots")};
foreach(var path in new[]{storage.MediaRoot,storage.TempRoot,storage.KeysRoot,storage.SnapshotRoot})Directory.CreateDirectory(path);
var app=Path.Combine(run,"package");var web=Path.Combine(app,"web");var worker=Path.Combine(app,"worker");Directory.CreateDirectory(web);Directory.CreateDirectory(worker);
var fixedDir=Path.Combine(app,"tools","ffmpeg");Directory.CreateDirectory(fixedDir);
var pathDir=Path.Combine(run,"path-bin");Directory.CreateDirectory(pathDir);
var approved=new VideoOptions{Enabled=true};
var locator=new VideoToolLocator(web,pathDir);
Check(locator.FixedDirectory==new VideoToolLocator(worker,pathDir).FixedDirectory,"Web and Worker share fixed application tools directory");
Check(locator.Find(approved)==new VideoToolPaths(null,null),"Missing tools; no disk scan or download");
Check(!(await new VideoProcessor(approved,storage,locator).HealthAsync()).Ready,"Missing tools not ready");
Check(new VideoToolLocator(web,".;relative;").Find(approved).Ffmpeg==null,"Relative PATH entries never used");
File.WriteAllText(Path.Combine(pathDir,"ffmpeg.exe"),"not an executable");
Check(locator.Find(approved).Ffmpeg==Path.Combine(pathDir,"ffmpeg.exe"),"PATH fallback discovers exact executable name");
File.WriteAllText(Path.Combine(fixedDir,"ffmpeg.exe"),"not an executable");
File.WriteAllText(Path.Combine(fixedDir,"ffprobe.exe"),"not an executable");
Check(locator.Find(approved).Ffmpeg==Path.Combine(fixedDir,"ffmpeg.exe"),"Fixed directory precedes PATH");
var noApproval=await new VideoProcessor(new VideoOptions(),storage,locator).HealthAsync();
Check(noApproval.Code=="VIDEO_DISABLED"&&noApproval.FfmpegVersion==null,"Disabled video does not invoke executables");
Check((await new VideoProcessor(new VideoOptions{Enabled=true},storage,locator).HealthAsync()).Code=="VIDEO_PROCESSOR_START_FAILED","Enabled video checks executable without manual approval fields");
Check((await new VideoProcessor(approved,storage,locator).HealthAsync()).Code=="VIDEO_PROCESSOR_START_FAILED","Invalid executable reported safely");
Check(!VideoProcessor.HasRequiredEncoders("libx264 aac mjpeg mentioned in descriptions"),"Encoder names in prose cannot pass capability check");
Check(!VideoProcessor.HasRequiredEncoders(" V..... libx264 H264\n A..... aac AAC"),"Missing JPEG encoder denied");
Check(VideoProcessor.HasRequiredEncoders(" V....D libx264 H264\n A..... aac AAC\n VFS... mjpeg JPEG"),"Required actual encoder columns accepted");
var unsafeOptions=new VideoOptions{FfmpegPath="ffmpeg.exe"};
try{locator.Find(unsafeOptions);Check(false,"Relative override denied");}catch(VideoProcessingException e){Check(e.Message=="VIDEO_TOOL_PATH_UNSAFE","Relative override denied");}

// Use existing operator-installed tools whose local test approval/hash is recorded privately.
using var tools=JsonDocument.Parse(File.ReadAllText(Path.Combine(repo,".local","video-tools.json")));
foreach(var name in new[]{"ffmpeg","ffprobe"})
{
    var source=tools.RootElement.GetProperty(name+"Path").GetString()!;
    Check(await VideoUploadService.HashFileAsync(source)==tools.RootElement.GetProperty(name+"Sha256").GetString(),"Existing approved local "+name+" binary matches evidence");
    File.Copy(source,Path.Combine(fixedDir,name+".exe"),true);
    File.Copy(source,Path.Combine(pathDir,name+".exe"),true);
}
var processor=new VideoProcessor(approved,storage,locator);
var health=await processor.HealthAsync();
Check(health.Ready&&health.FfmpegVersion!=null&&health.FfprobeVersion!=null,"Real discovery executes version/encoder checks without configured paths or hashes");
Check(health.FfmpegSha256==tools.RootElement.GetProperty("ffmpegSha256").GetString(),"Observed hash equals actual binary bytes");
Check((await new VideoProcessor(approved,storage,new VideoToolLocator(Path.Combine(run,"other"),pathDir)).HealthAsync()).Ready,"Real PATH fallback works without manual configuration");
var wrongPin=new VideoOptions{Enabled=true,FfmpegSha256=new string('0',64)};
Check((await new VideoProcessor(wrongPin,storage,locator).HealthAsync()).Code=="VIDEO_PROCESSOR_HASH_MISMATCH","Optional legacy pin still enforced");
if(OperatingSystem.IsWindows())
{
    var executable=new FileInfo(Path.Combine(pathDir,"ffprobe.exe"));
    var acl=executable.GetAccessControl();var restored=executable.GetAccessControl();
    acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,FileSystemRights.ExecuteFile,AccessControlType.Deny));
    try
    {
        executable.SetAccessControl(acl);
        var denied=await new VideoProcessor(approved,storage,new VideoToolLocator(Path.Combine(run,"other"),pathDir)).HealthAsync();
        Check(!denied.Ready&&denied.Code is "VIDEO_PROCESSOR_START_FAILED" or "VIDEO_PROCESSOR_ACCESS_DENIED","Actual Windows execute denial cannot report ready");
    }
    finally{executable.SetAccessControl(restored);}
}
var uploadTool=Path.Combine(storage.MediaRoot,"ffmpeg.exe");File.Copy(Path.Combine(fixedDir,"ffmpeg.exe"),uploadTool);
var badLocation=new VideoOptions{Enabled=true,FfmpegPath=uploadTool,FfprobePath=Path.Combine(fixedDir,"ffprobe.exe")};
Check((await new VideoProcessor(badLocation,storage).HealthAsync()).Code=="VIDEO_TOOL_PATH_UNSAFE","Tools inside upload storage never executed");

var sample=Path.Combine(run,"synthetic.mp4");
var start=new ProcessStartInfo(Path.Combine(fixedDir,"ffmpeg.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
foreach(var key in start.Environment.Keys.Where(k=>!new[]{"SystemRoot","WINDIR","PATH","PATHEXT"}.Contains(k,StringComparer.OrdinalIgnoreCase)).ToArray())start.Environment.Remove(key);
start.Environment["TEMP"]=storage.TempRoot;start.Environment["TMP"]=storage.TempRoot;
foreach(var arg in new[]{"-nostdin","-hide_banner","-loglevel","error","-f","lavfi","-i","testsrc2=size=320x180:rate=10","-f","lavfi","-i","sine=frequency=440:sample_rate=44100","-t","2","-c:v","libx264","-threads","1","-pix_fmt","yuv420p","-c:a","aac","-f","mp4",sample})start.ArgumentList.Add(arg);
using(var process=Process.Start(start)!){var error=process.StandardError.ReadToEndAsync();var output=process.StandardOutput.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}finally{if(!process.HasExited)process.Kill(true);}await Task.WhenAll(error,output);Check(process.ExitCode==0,"One synthetic sample generated using approved real FFmpeg");}
var asset=Guid.NewGuid();
var converted=await processor.ProcessAsync(asset,sample,forceTranscode:true);
Check(VideoProcessor.BrowserCompatible(converted.Probe)&&converted.Probe.Audio=="aac"&&!converted.Reused,"Auto-discovered processor really transcodes H264/AAC");
Check(File.Exists(storage.ResolveMedia(converted.Poster))&&converted.PosterBytes>0,"Fresh real JPEG poster created");
Check(await VideoUploadService.HashFileAsync(storage.ResolveMedia(converted.Mp4))==converted.Mp4Hash,"Output byte/hash validated");
Check((await processor.ProcessAsync(asset,sample,forceTranscode:true)).Reused,"Cache uses observed tool hashes, not empty config fields");

VideoWorkerStatus.Write(storage,health,false,true);
var observed=VideoWorkerStatus.Read(storage);
Check(observed?.Tools.FfmpegSha256==health.FfmpegSha256,"Protected Worker diagnostic persisted/read back");
Check(VideoWorkerStatus.StateCode(observed,300)=="VIDEO_WORKER_READY","Fresh Worker evidence distinguishes scanner exception");
Check(VideoWorkerStatus.StateCode(observed! with{ManagedVideoWithoutAv=false},300)=="VIDEO_WORKER_BLOCKED","No antivirus without approved exception remains blocked");
Check(VideoWorkerStatus.StateCode(observed,300,DateTimeOffset.UtcNow.AddHours(1))=="VIDEO_WORKER_STALE","Stale Worker is not shown ready");
Check(VideoWorkerStatus.StateCode(null,300)=="VIDEO_WORKER_UNKNOWN","Absent Worker evidence is not fabricated");

await File.AppendAllTextAsync(Path.Combine(fixedDir,"ffprobe.exe"),"synthetic integrity change");
try{await processor.ProbeAsync(sample);Check(false,"Change during processing denied");}catch(VideoProcessingException e){Check(e.Message=="VIDEO_PROCESSOR_CHANGED_DURING_CHECK","Change during processing denied");}
var changed=await processor.HealthAsync();
Check(changed.BinaryChanged&&changed.FfprobeSha256!=health.FfprobeSha256,"Next check detects changed bytes without manual SHA");
VideoWorkerStatus.Write(storage,changed,false,true);
var changeReceipt=VideoWorkerStatus.Read(storage);
Check(changeReceipt!=null&&changeReceipt.PreviousFfprobeSha256==health.FfprobeSha256&&changeReceipt.ToolsChangedUtc!=null,"Hash transition retained across Worker restarts");
VideoWorkerStatus.Write(storage,changed,false,true,"Stopped");
Check(VideoWorkerStatus.StateCode(VideoWorkerStatus.Read(storage),300)=="VIDEO_WORKER_STOPPED","Stopped Worker not shown active");
File.WriteAllText(Path.Combine(storage.TempRoot,"video-worker-status.dat"),"forged ready");
Check(VideoWorkerStatus.Read(storage)==null,"Tampered Worker evidence rejected");

var result=new{passed=passed.Count,checks=passed,run,sqlTouched=false,iisTouched=false,fullRegression=false};
File.WriteAllText(Path.Combine(run,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{passed=passed.Count,run}));
