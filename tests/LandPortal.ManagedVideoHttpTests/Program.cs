using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using UglyToad.PdfPig.Writer;

if(args.Length!=3 || !Regex.IsMatch(args[0],"^LPVideo_[a-f0-9]{12}$"))throw new ArgumentException("New isolated LocalDB instance/run/host required");
var root=Directory.GetCurrentDirectory();var run=Path.GetFullPath(args[1]);var host=Path.GetFullPath(args[2]);
if(!run.StartsWith(Path.Combine(root,".local","tests","video-http-"),StringComparison.OrdinalIgnoreCase)||!host.StartsWith(run+Path.DirectorySeparatorChar)||!File.Exists(host)||File.Exists(Path.Combine(run,"result.json")))throw new Exception("New exact test run required");
var cs=new SqlConnectionStringBuilder{DataSource=@"(localdb)\"+args[0],InitialCatalog="LandPortal_Test",IntegratedSecurity=true,Encrypt=true,TrustServerCertificate=true,Pooling=false};
var options=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString,o=>o.UseCompatibilityLevel(160)).Options;
var checks=new List<string>();var stage="create-empty-test";Process? web=null;Task<string>? stdout=null,stderr=null;
void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);Console.WriteLine("PASS "+name);}
try {
 await using(var sql=new SqlConnection(new SqlConnectionStringBuilder(cs.ConnectionString){InitialCatalog="master"}.ConnectionString)){
  await sql.OpenAsync();using var q=sql.CreateCommand();q.CommandText="SELECT CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),CONVERT(int,SERVERPROPERTY('IsLocalDB')),DB_ID('LandPortal_Test')";
  using(var r=await q.ExecuteReaderAsync()){await r.ReadAsync();Check(r.GetString(0).Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase)&&r.GetInt32(1)==1&&r.IsDBNull(2),"Fresh local SQL Server instance with absent test DB");}
  var mdf=Path.Combine(run,"LandPortal_Test.mdf");var ldf=Path.Combine(run,"LandPortal_Test_log.ldf");
  if(File.Exists(mdf)||File.Exists(ldf))throw new Exception("Fixture SQL files already exist; never overwrite");
  q.CommandText="CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'"+mdf.Replace("'","''")+"') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'"+ldf.Replace("'","''")+"')";await q.ExecuteNonQueryAsync();
  q.CommandText="ALTER DATABASE [LandPortal_Test] SET COMPATIBILITY_LEVEL=160";await q.ExecuteNonQueryAsync();
 }
 stage="migrate-new-test-only";
 var roleSecret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
 await using(var db=new PortalDbContext(options)){
  await db.Database.MigrateAsync();var history=(await db.Database.GetAppliedMigrationsAsync()).ToArray();
  Check(history.Length==18&&history.Last()=="20261110050000_HomeEventBanner","SQL Server exact schema18 via official migrations on new DB only");
  await RuntimeRoleInterceptor.ProvisionAsync(db,roleSecret);
  db.Categories.Add(new(){Id=1,Name="Isolated upload test",Slug="isolated-upload-test"});await db.SaveChangesAsync();
 }
 var services=new ServiceCollection();services.AddLogging();services.AddScoped(_=>new PortalDbContext(options));
 services.AddIdentityCore<PortalUser>(PortalIdentityPolicy.Configure).AddRoles<IdentityRole>().AddEntityFrameworkStores<PortalDbContext>();
 using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
 var accounts=new Dictionary<string,(PortalUser User,string Password)>();
 foreach(var entry in new[]{("writer",new[]{Permissions.Create,Permissions.ViewOwn,Permissions.EditOwn,Permissions.Submit,Permissions.ManageMedia,Permissions.ManageVideos}), ("reviewer",new[]{Permissions.Review}), ("publisher",new[]{Permissions.PublishApproved}), ("outsider",new[]{Permissions.ViewOwn})}){
  var user=new PortalUser{UserName="fixture-"+entry.Item1,Email=entry.Item1+"@example.invalid",DisplayName="Isolated "+entry.Item1,EmailConfirmed=true,LockoutEnabled=true};var password="Aa1!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
  Check((await users.CreateAsync(user,password)).Succeeded,"Identity fixture "+entry.Item1);
  Check((await users.AddClaimsAsync(user,entry.Item2.Select(p=>new Claim("permission",p)))).Succeeded,"Scoped claims "+entry.Item1);accounts[entry.Item1]=(user,password);
 }
 stage="start-real-http";
 var tools=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,".local","video-tools.json"))).RootElement;
 var videoOptions=new VideoOptions { Enabled=true,ApprovedManagedVideoWithoutAv=true,MaxFileBytes=10*1024*1024,
 FfmpegPath=tools.GetProperty("ffmpegPath").GetString()!,FfprobePath=tools.GetProperty("ffprobePath").GetString()!,
 FfmpegSha256=tools.GetProperty("ffmpegSha256").GetString()!,FfprobeSha256=tools.GetProperty("ffprobeSha256").GetString()! };

 var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var url="http://127.0.0.1:"+port;
 var start=new ProcessStartInfo(Path.Combine(root,".local","dotnet","dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
 start.ArgumentList.Add(host);start.ArgumentList.Add("--contentRoot");start.ArgumentList.Add(Path.Combine(root,"src","Web"));
 // No inherited deployment configuration/credentials enter the isolated test host.
 foreach(var key in start.Environment.Keys.Where(k=>k.Contains("__")||k.StartsWith("LANDPORTAL_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("ASPNETCORE_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("DOTNET_ENVIRONMENT",StringComparison.OrdinalIgnoreCase)).ToArray())start.Environment.Remove(key);
 foreach(var pair in new Dictionary<string,string>{{"ConnectionStrings__Portal",cs.ConnectionString},{"Sql__ExpectedDatabase","LandPortal_Test"},{"Runtime__RolePassword",roleSecret},{"Public__BaseUrl",url},{"Public__CanonicalHost","127.0.0.1"},{"DOTNET_ENVIRONMENT","Development"},{"ASPNETCORE_ENVIRONMENT","Development"},{"Mail__Transport","Disabled"},{"Deployment__Profile",""},{"Scanner__Provider","Disabled"},{"WorkspaceRoot",root},{"ManagedHttpTestRoot",run},{"Logging__LogLevel__Default","Warning"}})start.Environment[pair.Key]=pair.Value;
 foreach(var pair in new[]{("MediaRoot","media"),("SnapshotRoot","snapshots"),("TempRoot","temp"),("KeysRoot","keys")}){var dir=Path.Combine(run,pair.Item2);Directory.CreateDirectory(dir);start.Environment["Storage__"+pair.Item1]=dir;}
 start.Environment["VideoTestNoAv"]="true";
 foreach(var pair in new[]{("FfmpegPath",videoOptions.FfmpegPath),("FfprobePath",videoOptions.FfprobePath),("FfmpegSha256",videoOptions.FfmpegSha256),("FfprobeSha256",videoOptions.FfprobeSha256)}) start.Environment["Video__"+pair.Item1]=pair.Item2;
 var config=new ConfigurationBuilder().AddInMemoryCollection(start.Environment.Select(p=>new KeyValuePair<string,string?>(p.Key.Replace("__",":"),p.Value))).Build();
 config["Video:Enabled"]="true";config["Video:AllowManagedVideoWithoutAv"]="true";
 var storage=StorageOptions.FromConfiguration(config);
 Check(!VideoOptions.FromConfiguration(config,"Development").ApprovedManagedVideoWithoutAv,"Normal Development configuration cannot enable no-AV video");
 Check(!VideoOptions.FromConfiguration(config,"Production").ApprovedManagedVideoWithoutAv,"Production cannot enable no-AV video");
 Check(!VideoOptions.FromConfiguration(config,"Staging").ApprovedManagedVideoWithoutAv,"Unvalidated Staging cannot enable no-AV video");
 // Offline UAT configuration validation: fake endpoint is never connected to.
 var uat=new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{
  ["Deployment:Profile"]="UAT",["DOTNET_ENVIRONMENT"]="Staging",["ASPNETCORE_ENVIRONMENT"]="Staging",
  ["ConnectionStrings:Portal"]="Server=uat-fixture.invalid;Database=Video_UatFixture;Integrated Security=True;Encrypt=True;TrustServerCertificate=False",
  ["Sql:ExpectedDatabase"]="Video_UatFixture",["Deployment:ExpectedSqlServer"]="uat-fixture.invalid",
  ["Public:BaseUrl"]="https://video-fixture.invalid",["Deployment:ExpectedHost"]="video-fixture.invalid",["WorkspaceRoot"]=run}).Build();
 Check(VideoOptions.FromConfiguration(uat,"Staging").ApprovedManagedVideoWithoutAv,"Validated UAT accepts explicitly enabled video-only flag");
 uat["Video:AllowManagedVideoWithoutAv"]="false";Check(!VideoOptions.FromConfiguration(uat,"Staging").ApprovedManagedVideoWithoutAv,"UAT video exception off by explicit false");
 var rejectedProd=false;try{_ = VideoOptions.FromConfiguration(uat,"Production");}catch(InvalidOperationException){rejectedProd=true;}Check(rejectedProd,"UAT profile cannot be used in Production");
 var rejectedHost=false;uat["Public:BaseUrl"]="http://video-fixture.invalid";try{_ = VideoOptions.FromConfiguration(uat,"Staging");}catch(InvalidOperationException){rejectedHost=true;}Check(rejectedHost,"Video flag does not bypass HTTPS guard");uat["Public:BaseUrl"]="https://video-fixture.invalid";
 await using(var isolated=new PortalDbContext(options)){
  var denied=false;try{await new VideoUploadService(isolated,storage,new VideoOptions{Enabled=true},new CurrentPermissionService(isolated),media:new MediaService(isolated,storage,new MediaUploadOptions(),uat)).CreateAsync(Guid.NewGuid(),"synthetic", "fixture.mp4","video/mp4",100,new string('a',64));}catch(MediaUploadException e)when(e.StatusCode==503){denied=true;}
  Check(denied,"UAT policy off blocks upload without scanner before writing");
 }
 var processor=new VideoProcessor(videoOptions,storage);Check((await processor.HealthAsync()).Ready,"Real hash-pinned FFmpeg/FFprobe ready");
 web=Process.Start(start)!;stdout=web.StandardOutput.ReadToEndAsync();stderr=web.StandardError.ReadToEndAsync();
 HttpClient Client()=>new(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new()}){BaseAddress=new(url),Timeout=TimeSpan.FromSeconds(25)};
 using var guest=Client();bool ready=false;for(int i=0;i<60;i++){if(web.HasExited)break;try{using var r=await guest.GetAsync("/ready");if(r.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(200);}
 Check(ready,"Real Kestrel HTTP and SQL runtime role ready without scanner");
 async Task<AngleSharp.Dom.IDocument> Page(HttpClient c,string path){using var r=await c.GetAsync(path);if(r.StatusCode!=HttpStatusCode.OK)throw new Exception("GET "+path+" status "+(int)r.StatusCode);return new HtmlParser().ParseDocument(await r.Content.ReadAsStringAsync());}
 async Task<string> Token(HttpClient c,string path="/admin")=>(await Page(c,path)).QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
 async Task<HttpClient> Login(string who){var c=Client();var p=await Page(c,"/admin/login");var question=p.QuerySelector("#captcha-question")!.TextContent;var numbers=Regex.Matches(question,@"\d+").Select(m=>int.Parse(m.Value)).ToArray();if(numbers.Length!=2||!(question.Contains("cộng")||question.Contains("trừ")))throw new Exception("Unexpected CAPTCHA");
 using var r=await c.PostAsync("/admin/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",p.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!},{"CaptchaId",p.QuerySelector("input[name=CaptchaId]")!.GetAttribute("value")!},{"CaptchaAnswer",(question.Contains("cộng")?numbers[0]+numbers[1]:numbers[0]-numbers[1]).ToString()},{"UserName",accounts[who].User.UserName!},{"Password",accounts[who].Password}}));Check(r.StatusCode==HttpStatusCode.Redirect,"HTTP Identity/CAPTCHA login "+who);return c;}
 using var writer=await Login("writer");using var reviewer=await Login("reviewer");using var publisher=await Login("publisher");using var outsider=await Login("outsider");
 async Task<JsonElement> Post(HttpClient c,string route,Dictionary<string,string> fields,string form="/admin"){
 fields["__RequestVerificationToken"]=await Token(c,form);using var r=await c.PostAsync(route,new FormUrlEncodedContent(fields));var body=await r.Content.ReadAsStringAsync();if(r.StatusCode!=HttpStatusCode.OK){await File.WriteAllTextAsync(Path.Combine(run,"last-response.txt"),body);throw new Exception("POST "+route+" status "+(int)r.StatusCode);}return JsonDocument.Parse(body).RootElement.Clone();}
 Guid Id(JsonElement x)=>x.GetProperty("contentId").GetGuid();Guid Rev(JsonElement x)=>x.GetProperty("currentRevisionId").GetGuid();
 async Task<JsonElement> JsonPost(HttpClient client,string route,object data,HttpStatusCode expected=HttpStatusCode.OK){
  using var req=new HttpRequestMessage(HttpMethod.Post,route){Content=JsonContent.Create(data)};req.Headers.Add("X-CSRF-TOKEN",await Token(client));using var response=await client.SendAsync(req);
  var body=await response.Content.ReadAsStringAsync();if(response.StatusCode!=expected){await File.WriteAllTextAsync(Path.Combine(run,"last-response.txt"),body);throw new Exception(route+" status "+(int)response.StatusCode+" expected "+(int)expected);}
  return string.IsNullOrWhiteSpace(body)?default:JsonDocument.Parse(body).RootElement.Clone();
 }
 async Task<JsonElement> State(Guid id){await using var db=new PortalDbContext(options);var c=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==id);return JsonSerializer.SerializeToElement(new{contentId=c.Id,currentRevisionId=c.CurrentRevisionId,rowVersion=Convert.ToBase64String(c.RowVersion)});}
 async Task<JsonElement> Act(HttpClient c,JsonElement s,string action)=>await Post(c,"/admin/workflow/"+Id(s),new(){{"Action",action},{"RowVersion",s.GetProperty("rowVersion").GetString()!},{"RevisionId",Rev(s).ToString()},{"RequestId",Guid.NewGuid().ToString()}});
 async Task Native(string exe,params string[] arguments){var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var a in arguments)info.ArgumentList.Add(a);using var p=Process.Start(info)!;var aout=p.StandardOutput.ReadToEndAsync();var aerr=p.StandardError.ReadToEndAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await p.WaitForExitAsync(deadline.Token);await aout;var err=await aerr;if(p.ExitCode!=0)throw new Exception("Native fixture process failed: "+err);}finally{if(!p.HasExited)p.Kill(true);}}
 stage="native-fixture";
 var sample=Path.Combine(run,"synthetic.mp4");
 await Native(videoOptions.FfmpegPath,"-nostdin","-v","error","-f","lavfi","-i","testsrc2=size=320x180:rate=24","-f","lavfi","-i","sine=frequency=440:sample_rate=44100","-t","2","-threads","1","-c:v","libx264","-pix_fmt","yuv420p","-c:a","aac","-metadata","comment=ISOLATED TEST ONLY",sample);
 var sampleBytes=await File.ReadAllBytesAsync(sample);var probe=await processor.ProbeAsync(sample);Check(VideoProcessor.BrowserCompatible(probe),"Single synthetic MP4 H264/AAC fixture generated");
 stage="http-video-upload";
 var article=await Post(writer,"/admin/save",new(){{"Kind","Video"},{"Title","ISOLATED VIDEO NOAV HTTP TEST"},{"Summary","Synthetic video fixture"},{"BodyHtml","<p>Isolated video only.</p>"},{"Author","Fixture"},{"CategoryId","1"},{"RequestId",Guid.NewGuid().ToString()},{"intent","Save"},{"format","json"}},"/admin/create?kind=Video");
 Check(Id(article)!=Guid.Empty,"HTTP create Video draft");var contentId=Id(article);
 string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
 async Task<Guid> Session(HttpClient client,byte[] bytes,HttpStatusCode status=HttpStatusCode.OK){var data=await JsonPost(client,"/admin/videos/sessions",new{contentId,name="fixture.mp4",mime="video/mp4",bytes=bytes.Length,chunkManifestSha256=TextNormalizer.Hash($"0:{bytes.Length}:{Hash(bytes)}")},status);return status==HttpStatusCode.OK?data.GetProperty("id").GetGuid():Guid.Empty;}
 async Task Chunk(Guid session,byte[] bytes,bool csrf=true){using var req=new HttpRequestMessage(HttpMethod.Put,$"/admin/videos/sessions/{session}/chunks/0"){Content=new ByteArrayContent(bytes)};req.Headers.Add("X-Chunk-SHA256",Hash(bytes));if(csrf)req.Headers.Add("X-CSRF-TOKEN",await Token(writer));using var res=await writer.SendAsync(req);Check(res.StatusCode==(csrf?HttpStatusCode.OK:HttpStatusCode.BadRequest),csrf?"HTTP chunk exact byte/hash accepted":"Missing CSRF rejected");}
 await Session(outsider,sampleBytes,HttpStatusCode.Forbidden);Check(true,"Unauthorized video session rejected");
 var session=await Session(writer,sampleBytes);await Chunk(session,sampleBytes,false);await Chunk(session,sampleBytes);
 var completed=await JsonPost(writer,$"/admin/videos/sessions/{session}/complete",new{});var assetId=completed.GetProperty("mediaAssetId").GetGuid();
 await using(var db=new PortalDbContext(options)){var asset=await db.MediaAssets.SingleAsync(x=>x.Id==assetId);Check(asset.ScanStatus=="NotScanned"&&asset.ValidationStatus==VideoOptions.NoAvPending&&asset.IsQuarantined,"New no-AV queue marked explicitly; original private");}
 var scanner=new UnavailableInquiryScanner(); // Same fail-closed implementation returned for unconfigured UAT scanners.
 Check(!(await scanner.HealthAsync()).Ready,"Scanner genuinely disabled");
 stage="worker-transcode";
 await using(var db=new PortalDbContext(options))Check(await new VideoWorker(db,storage,videoOptions,scanner,processor,new CurrentPermissionService(db)).RunOnceAsync(assetId),"Actual VideoWorker processes single SQL job without scanner");
 VideoFile readyFile;
 await using(var db=new PortalDbContext(options)){
  readyFile=await db.Set<VideoFile>().Include(x=>x.Original).AsNoTracking().SingleAsync(x=>x.MediaAssetId==assetId);
  Check(readyFile.State==VideoState.Ready&&readyFile.Original.ScanStatus=="NotScanned"&&readyFile.Original.ValidationStatus==VideoOptions.NoAvValidated&&readyFile.ScannerVersion==null&&readyFile.ScannedUtc==null&&readyFile.ScannedSha256==null,"Ready with technical validation, no fake Clean/proof");
  Check(await VideoReadiness.ReadyAsync(db,storage,readyFile.Original,false,policy:videoOptions),"Readiness verifies original/MP4/poster bytes under explicit policy");
  Check(!await VideoReadiness.ReadyAsync(db,storage,readyFile.Original,false,policy:new VideoOptions{Enabled=true}),"Readiness denies NotScanned when no-AV off");
  Check(readyFile.Mp4Sha256!=readyFile.Original.Sha256&&readyFile.VideoCodec=="h264"&&readyFile.AudioCodec=="aac","Fresh output distinct from original H264/AAC");
 }
 var outputPath=MediaService.ResolveMediaPath(storage,readyFile.Mp4Key!);var outputProbe=await processor.ProbeAsync(outputPath);
 Check(VideoProcessor.BrowserCompatible(outputProbe),"FFprobe validates actual output codec/profile");
 await Native(videoOptions.FfmpegPath,"-nostdin","-v","error","-i",outputPath,"-f","null","-");Check(true,"Actual output decodes for playback");
 // Signed output cache records a distinct forced-transcode policy, not the old remux path.
 var reused=await processor.ProcessAsync(assetId,MediaService.ResolveMediaPath(storage,readyFile.Original.StorageKey),forceTranscode:true);
 Check(reused.Reused&&reused.Mp4Hash==readyFile.Mp4Sha256,"Only forced-transcode proof reused on same bytes/binaries");
 using(var response=await guest.GetAsync($"/video-media/{assetId}/play"))Check(response.StatusCode==HttpStatusCode.NotFound,"Unpublished video is not public");
 await JsonPost(writer,$"/admin/videos/{contentId}/select",new{assetId,rowVersion=article.GetProperty("rowVersion").GetString(),commandId=Guid.NewGuid()});article=await State(contentId);
 using(var response=await outsider.GetAsync($"/admin/videos/media/{assetId}/play?contentId={contentId}"))Check(response.StatusCode==HttpStatusCode.NotFound,"Out-of-scope private playback denied");
 stage="http-video-publish";
 article=await Act(writer,article,"Submit");Check(true,"HTTP Writer submits Video revision");article=await Act(reviewer,article,"Approve");Check(true,"HTTP Reviewer approves exact revision");
 using(var response=await guest.GetAsync("/bai-viet/"+contentId))Check(response.StatusCode==HttpStatusCode.NotFound,"Approved video hidden until publication");
 article=await Act(publisher,article,"PublishApproved");Check(true,"HTTP Publisher publishes Video");
 using(var response=await guest.GetAsync("/bai-viet/"+contentId)){var html=await response.Content.ReadAsStringAsync();Check(response.StatusCode==HttpStatusCode.OK&&html.Contains($"/video-media/{assetId}/play"),"Public detail includes real video player");}
 using(var response=await guest.GetAsync("/")){var html=await response.Content.ReadAsStringAsync();Check(response.StatusCode==HttpStatusCode.OK&&html.Contains("home-video-section")&&html.Contains($"/video-media/{assetId}/poster"),"Homepage renders single published video/poster");}
 foreach(var part in new[]{"play","poster"}){using var response=await guest.GetAsync($"/video-media/{assetId}/{part}");var bytes=await response.Content.ReadAsByteArrayAsync();Check(response.StatusCode==HttpStatusCode.OK&&Hash(bytes)==(part=="play"?readyFile.Mp4Sha256:readyFile.PosterSha256)&&response.Headers.GetValues("X-Media-Scan-Status").Single()=="NotScanned","Published "+part+" 200 correct hash, truthful status");}
 using(var req=new HttpRequestMessage(HttpMethod.Get,$"/video-media/{assetId}/play")){req.Headers.Range=new RangeHeaderValue(0,31);using var response=await guest.SendAsync(req);Check(response.StatusCode==HttpStatusCode.PartialContent&&response.Content.Headers.ContentRange?.From==0&&response.Content.Headers.ContentRange?.To==31&&(await response.Content.ReadAsByteArrayAsync()).SequenceEqual((await File.ReadAllBytesAsync(outputPath)).Take(32)),"HTTP Range 206 correct bytes/content-range");}
 stage="video-negative";
 // Reuse the draft only for malformed fixtures: no second valid video or public fake content.
 var draft=await Post(writer,"/admin/save",new(){{"Kind","Video"},{"Title","ISOLATED INVALID VIDEO FIXTURE"},{"CategoryId","1"},{"RequestId",Guid.NewGuid().ToString()},{"intent","Save"},{"format","json"}},"/admin/create?kind=Video");contentId=Id(draft);
 var fake=Encoding.UTF8.GetBytes("This is not a video file with sufficient bytes");var fakeSession=await Session(writer,fake);await Chunk(fakeSession,fake);await JsonPost(writer,$"/admin/videos/sessions/{fakeSession}/complete",new{},HttpStatusCode.BadRequest);Check(true,"Fake MP4 rejected by envelope validation");
 var corrupt=new byte[40];Encoding.ASCII.GetBytes("ftyp").CopyTo(corrupt,4);var corruptSession=await Session(writer,corrupt);await Chunk(corruptSession,corrupt);var corruptId=(await JsonPost(writer,$"/admin/videos/sessions/{corruptSession}/complete",new{})).GetProperty("mediaAssetId").GetGuid();
 await using(var db=new PortalDbContext(options)){await new VideoWorker(db,storage,videoOptions,scanner,processor,new CurrentPermissionService(db)).RunOnceAsync(corruptId);db.ChangeTracker.Clear();var bad=await db.Set<VideoFile>().Include(x=>x.Original).SingleAsync(x=>x.MediaAssetId==corruptId);Check(bad.State==VideoState.Failed&&bad.ReadyUtc==null&&bad.Original.ScanStatus=="NotScanned","Corrupt container rejected by actual FFprobe; never Ready/Clean");}
 // Existing quarantined jobs do not acquire the new exception just because the switch is enabled.
 await using(var db=new PortalDbContext(options)){var bad=await db.Set<VideoFile>().Include(x=>x.Original).SingleAsync(x=>x.MediaAssetId==corruptId);bad.Original.ValidationStatus="VideoEnvelopeOnly";bad.State=VideoState.Pending;bad.AvailableUtc=DateTimeOffset.UtcNow;await db.SaveChangesAsync();await new VideoWorker(db,storage,videoOptions,scanner,processor,new CurrentPermissionService(db)).RunOnceAsync(corruptId);db.ChangeTracker.Clear();bad=await db.Set<VideoFile>().Include(x=>x.Original).SingleAsync(x=>x.MediaAssetId==corruptId);Check(bad.State==VideoState.Pending&&bad.Original.ValidationStatus=="VideoEnvelopeOnly","Legacy quarantined job untouched without scanner");}
 stage="policy-disabled-http";
 web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(run,"enabled-host.log"),await stdout!+await stderr!);web.Dispose();
 start.Environment["VideoTestNoAv"]="false";web=Process.Start(start)!;stdout=web.StandardOutput.ReadToEndAsync();stderr=web.StandardError.ReadToEndAsync();ready=false;
 for(int i=0;i<60;i++){if(web.HasExited)break;try{using var r=await guest.GetAsync("/ready");if(r.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(200);}Check(ready,"Restart isolated host with no-AV disabled");
 using(var response=await guest.GetAsync($"/video-media/{assetId}/play"))Check(response.StatusCode==HttpStatusCode.NotFound,"Policy off denies existing NotScanned playback over HTTP");
 using(var response=await guest.GetAsync("/")){var html=await response.Content.ReadAsStringAsync();Check(response.StatusCode==HttpStatusCode.OK&&!html.Contains("home-video-section"),"Policy off hides unscanned video on homepage");}
 await using(var db=new PortalDbContext(options)){var job=await db.Set<VideoFile>().Include(x=>x.Original).SingleAsync(x=>x.MediaAssetId==assetId);Check(job.State==VideoState.Ready&&job.Original.ScanStatus=="NotScanned"&&job.ScannerProvider==null,"Disabling policy never rewrites media scan status");Check(await db.Users.CountAsync()==4&&await db.SourceRecords.CountAsync()==0,"Only synthetic accounts, no real source imports");}
 stage="COMPLETE";
}catch(Exception ex){Console.WriteLine("BLOCKED "+stage+" "+ex.GetType().Name+(ex is SqlException s?" SQL="+s.Number:""));await File.WriteAllTextAsync(Path.Combine(run,"failure.txt"),ex.ToString());Environment.ExitCode=1;}
finally{
 if(web!=null){if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(run,"host.log"),(stdout==null?"":await stdout)+(stderr==null?"":await stderr));web.Dispose();}
 await File.WriteAllTextAsync(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{stage,checks,passed=checks.Count,server=cs.DataSource,database=cs.InitialCatalog,operationalSqlTouched=false,accounts="synthetic only",smtp="Disabled",fullRegression=false},new JsonSerializerOptions{WriteIndented=true}));
 Console.WriteLine("RESULT "+stage+" checks="+checks.Count+" evidence="+run);
}
