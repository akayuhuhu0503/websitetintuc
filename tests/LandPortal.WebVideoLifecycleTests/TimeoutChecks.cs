using System.Data.Common;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

static class TimeoutChecks
{
    public static async Task RunAsync(string instance, string runPath, string toolFile)
    {
        var root=Directory.GetCurrentDirectory();var run=Path.GetFullPath(runPath);
        if(!Regex.IsMatch(instance,"^LPVideoTimeout_[a-f0-9]{12}$") ||
            !run.StartsWith(Path.Combine(root,".local","tests","video-timeout-"),StringComparison.OrdinalIgnoreCase) ||
            File.Exists(Path.Combine(run,"result.json")))throw new Exception("New isolated Test instance/path required");
        var cs=new SqlConnectionStringBuilder{DataSource=@"(localdb)\"+instance,InitialCatalog="LandPortal_Test",IntegratedSecurity=true,Encrypt=true,TrustServerCertificate=true,Pooling=false};
        DbContextOptions<PortalDbContext> DbOptions(IInterceptor? interceptor=null){var b=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString,o=>o.UseCompatibilityLevel(160));if(interceptor!=null)b.AddInterceptors(interceptor);return b.Options;}
        PortalDbContext Db()=>new(DbOptions());
        var storage=new StorageOptions{MediaRoot=Path.Combine(run,"media"),TempRoot=Path.Combine(run,"temp"),KeysRoot=Path.Combine(run,"keys"),SnapshotRoot=Path.Combine(run,"snapshots")};
        foreach(var p in new[]{storage.MediaRoot,storage.TempRoot,storage.KeysRoot,storage.SnapshotRoot})Directory.CreateDirectory(p);
        var tools=JsonDocument.Parse(File.ReadAllText(toolFile)).RootElement;
        VideoOptions Policy(int seconds=1800,bool noAv=true)=>new(){Enabled=true,ApprovedManagedVideoWithoutAv=noAv,ProcessSeconds=seconds,
            FfmpegPath=tools.GetProperty("ffmpegPath").GetString()!,FfprobePath=tools.GetProperty("ffprobePath").GetString()!,
            FfmpegSha256=tools.GetProperty("ffmpegSha256").GetString()!,FfprobeSha256=tools.GetProperty("ffprobeSha256").GetString()!};
        var policy=Policy();var processor=new VideoProcessor(policy,storage);var checks=new List<string>();WebApplication? web=null;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add(text);Console.WriteLine("PASS "+text);}
        async Task<VideoFile> Job(Guid id){await using var db=Db();return await db.Set<VideoFile>().AsNoTracking().Include(x=>x.Original).SingleAsync(x=>x.MediaAssetId==id);}
        async Task Queue(Guid id,string actor="admin",VideoOptions? p=null){await using var db=Db();await new VideoTimeoutRecovery(db,storage,p??policy,new(db)).QueueAsync(id,actor);}
        async Task Deny(Func<Task> action,int status,string text){try{await action();}catch(MediaUploadException e)when(e.StatusCode==status){Check(true,text);return;}throw new Exception(text);}
        async Task Mutate(Guid id,Action<VideoFile> change){await using var db=Db();var file=await db.Set<VideoFile>().Include(x=>x.Original).SingleAsync(x=>x.MediaAssetId==id);change(file);await db.SaveChangesAsync();}
        byte[] sample=[];
        async Task<Guid> Uploaded(string name,bool duplicate=false)
        {
            if(!duplicate){
                var input=Path.Combine(storage.TempRoot,"sample.mp4");var distinct=Path.Combine(storage.TempRoot,name+".mp4");
                await processor.Run(policy.FfmpegPath,["-nostdin","-v","error","-i",input,"-c","copy","-metadata","title="+name,distinct],20,CancellationToken.None);
                sample=await File.ReadAllBytesAsync(distinct);
            }
            await using var db=Db();var actor=await new CurrentPermissionService(db).GetAsync("owner");
            var draft=await new WorkflowService(db,new HtmlPolicy(),storage:storage,videoOptions:policy).SaveAsync(null,new(){Kind=ContentKind.Video,Title="Fixture "+name,BodyHtml="<p>Isolated timeout test.</p>",PrimaryCategoryId=1},actor,null,Guid.NewGuid());
            var sha=Convert.ToHexStringLower(SHA256.HashData(sample));var uploads=new VideoUploadService(db,storage,policy,new(db));
            var s=await uploads.CreateAsync(draft.ContentId,"owner",name+".mp4","video/mp4",sample.Length,TextNormalizer.Hash($"0:{sample.Length}:{sha}"));
            await uploads.ChunkAsync(s.Id,"owner",0,sha,new MemoryStream(sample));s=await uploads.CompleteAsync(s.Id,"owner");
            var f=await db.Set<VideoFile>().SingleAsync(x=>x.MediaAssetId==s.MediaAssetId);f.State=VideoState.Failed;f.Attempts=3;f.Error="VIDEO_PROCESSOR_TIMEOUT";
            var session=await db.Set<VideoUploadSession>().SingleAsync(x=>x.Id==s.Id);session.ExpiresUtc=DateTimeOffset.UtcNow.AddDays(-2);
            await db.SaveChangesAsync();return f.MediaAssetId;
        }
        async Task<bool> Work(Guid id,VideoOptions? p=null){await using var db=Db();var effective=p??policy;return await new VideoWorker(db,storage,effective,new UnavailableInquiryScanner(),new(effective,storage),new(db)).RunOnceAsync(id);}
        var stage="setup";
        try
        {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Storage:MediaRoot"]=storage.MediaRoot,["Storage:TempRoot"]=storage.TempRoot,["Storage:KeysRoot"]=storage.KeysRoot,["Storage:SnapshotRoot"]=storage.SnapshotRoot}).Build();
            Check(VideoOptions.FromConfiguration(config,"Development").ProcessSeconds==1800&&new VideoOptions().ProcessSeconds==1800&&policy.Threads==2,"Default timeout1800 and thread budget2; no policy bypass");
            config["Video:ProcessSeconds"]="1801";try{VideoOptions.FromConfiguration(config,"Development");throw new Exception("Limit not rejected");}catch(InvalidOperationException){Check(true,"Runtime rejects timeout above1800");}config["Video:ProcessSeconds"]="300";
            Check(VideoOptions.FromConfiguration(config,"Development").ProcessSeconds==300,"Explicit lower operator limit preserved");
            await using(var sql=new SqlConnection(new SqlConnectionStringBuilder(cs.ConnectionString){InitialCatalog="master"}.ConnectionString))
            {
                await sql.OpenAsync();using var q=sql.CreateCommand();q.CommandText="SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB')),DB_ID('LandPortal_Test')";
                using(var r=await q.ExecuteReaderAsync()){await r.ReadAsync();Check(r.GetInt32(0)==1&&r.IsDBNull(1),"Fresh LocalDB only, no operational configuration loaded");}
                q.CommandText="CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'"+Path.Combine(run,"test.mdf").Replace("'","''")+"') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'"+Path.Combine(run,"test.ldf").Replace("'","''")+"')";await q.ExecuteNonQueryAsync();
            }
            await using(var db=Db())
            {
                await db.Database.MigrateAsync();Check((await db.Database.GetAppliedMigrationsAsync()).Count()==18,"Only disposable Test gets official schema18");
                db.Categories.Add(new(){Id=1,Name="Fixture",Slug="fixture"});
                foreach(var user in new[]{"owner","admin","other"})
                {
                    db.Users.Add(new(){Id=user,UserName=user,NormalizedUserName=user.ToUpperInvariant(),SecurityStamp=Guid.NewGuid().ToString("N")});
                    var rights=user=="admin"?new[]{Permissions.ManageSettings,Permissions.ManageVideos,Permissions.ManageMedia,Permissions.EditAll,Permissions.ViewAll}:new[]{Permissions.Create,Permissions.EditOwn,Permissions.ViewOwn,Permissions.ManageMedia,Permissions.ManageVideos};
                    foreach(var right in rights)db.UserClaims.Add(new IdentityUserClaim<string>{UserId=user,ClaimType="permission",ClaimValue=right});
                }
                await db.SaveChangesAsync();
            }
            Check((await processor.HealthAsync()).Ready,"Real pinned FFmpeg/FFprobe and encoders available");
            var samplePath=Path.Combine(storage.TempRoot,"sample.mp4");
            await processor.Run(policy.FfmpegPath,["-nostdin","-v","error","-f","lavfi","-i","testsrc2=size=320x180:rate=25","-t","2","-threads","2","-c:v","libx264","-pix_fmt","yuv420p",samplePath],20,CancellationToken.None);
            sample=await File.ReadAllBytesAsync(samplePath);
            var slowArgs=new[]{"-nostdin","-v","error","-re","-f","lavfi","-i","testsrc2=size=320x180:rate=25","-t","2","-threads","2","-f","null","-"};
            try{await processor.Run(policy.FfmpegPath,slowArgs,1,CancellationToken.None,"transcode");throw new Exception("Native timeout missing");}
            catch(VideoProcessingException e)when(e.Message=="VIDEO_PROCESSOR_TIMEOUT"){Check(processor.LastOperation=="transcode"&&processor.LastDeadlineSeconds==1,"Real FFmpeg deadline terminates with timeout and phase evidence");}
            await processor.Run(policy.FfmpegPath,slowArgs,policy.ProcessSeconds,CancellationToken.None,"transcode");Check(true,"Same native workload completes under1800-second budget");
            stage="recovery-guards";
            var asset=await Uploaded("first");var before=await Job(asset);
            Check(await Uploaded("duplicate",true)==asset,"Identical upload sessions reuse one canonical asset/job");
            Check(!await Work(asset),"Attempts3 timeout is not automatically requeued");
            await Deny(()=>Queue(asset,"owner"),403,"Uploader without ManageSettings cannot grant fourth attempt");
            await Deny(()=>Queue(asset,p:Policy(300)),409,"Old300-second configuration cannot consume recovery grant");
            await Deny(()=>Queue(asset,p:Policy(noAv:false)),409,"Disabled no-AV does not enroll quarantined original");
            await Mutate(asset,x=>x.Error="VIDEO_PROCESSOR_REJECTED");await Deny(()=>Queue(asset),409,"Non-timeout failed job is not eligible");
            await Mutate(asset,x=>{x.Error="VIDEO_PROCESSOR_TIMEOUT";x.Original.ScanStatus="Infected";});await Deny(()=>Queue(asset),409,"Infected original is not eligible");
            await Mutate(asset,x=>x.Original.ScanStatus="NotScanned");
            await using(var db=Db()){var c=await db.Contents.SingleAsync(x=>x.Id==db.Set<VideoUploadSession>().Where(s=>s.Id==before.UploadSessionId).Select(s=>s.ContentId).First());c.State=WorkflowState.PendingReview;await db.SaveChangesAsync();}
            await Deny(()=>Queue(asset),409,"Closed editorial draft is not reopened by recovery");
            await using(var db=Db()){var c=await db.Contents.SingleAsync(x=>x.Id==db.Set<VideoUploadSession>().Where(s=>s.Id==before.UploadSessionId).Select(s=>s.ContentId).First());c.State=WorkflowState.Draft;await db.SaveChangesAsync();}
            stage="http";
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development",ApplicationName=typeof(VideoAdminController).Assembly.GetName().Name});
            builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddAuthentication("Fixture").AddCookie("Fixture");builder.Services.AddAuthorization();
            builder.Services.AddAntiforgery(o=>o.HeaderName="X-CSRF-TOKEN");
            builder.Services.AddControllersWithViews(o=>o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddApplicationPart(typeof(VideoAdminController).Assembly);
            builder.Services.AddScoped(_=>Db());builder.Services.AddSingleton(storage);builder.Services.AddSingleton(policy);
            builder.Services.AddScoped<CurrentPermissionService>();builder.Services.AddScoped<VideoUploadService>();builder.Services.AddScoped<VideoProcessor>();builder.Services.AddScoped<VideoSubtitleService>();
            builder.Services.AddScoped(sp=>new WorkflowService(sp.GetRequiredService<PortalDbContext>(),new HtmlPolicy(),storage:storage,videoOptions:policy));
            web=builder.Build();web.UseAuthentication();web.UseAuthorization();web.MapControllers();
            web.MapPost("/fixture/login/{id}",async (string id,HttpContext h)=>{if(id is not("admin" or "owner" or "other"))return Results.NotFound();await h.SignInAsync("Fixture",new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,id)},"Fixture")));return Results.Ok();});
            web.MapGet("/fixture/token",(HttpContext h,IAntiforgery af)=>Results.Text(af.GetAndStoreTokens(h).RequestToken!));
            await web.StartAsync();var url=web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(url)};
            await client.PostAsync("/fixture/login/owner",null);var token=await client.GetStringAsync("/fixture/token");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token);
            Check((await client.PostAsync($"/admin/videos/{asset}/retry-timeout",null)).StatusCode==HttpStatusCode.Forbidden,"Real MVC route rejects non-admin with valid CSRF");
            await client.PostAsync("/fixture/login/admin",null);client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Check((await client.PostAsync($"/admin/videos/{asset}/retry-timeout",null)).StatusCode==HttpStatusCode.BadRequest,"Real recovery POST requires CSRF");
            token=await client.GetStringAsync("/fixture/token");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token);
            using(var page=await client.GetAsync("/admin/videos")){var html=await page.Content.ReadAsStringAsync();Check(page.StatusCode==HttpStatusCode.OK&&html.Contains($"/admin/videos/{asset}/retry-timeout")&&html.Contains("1800"),"Admin Razor renders recovery form and actual1800-second configuration");}
            var responses=await Task.WhenAll(client.PostAsync($"/admin/videos/{asset}/retry-timeout",null),client.PostAsync($"/admin/videos/{asset}/retry-timeout",null));
            Check(responses.Count(x=>x.StatusCode==HttpStatusCode.OK)==1&&responses.Count(x=>x.StatusCode==HttpStatusCode.Conflict)==1,"Concurrent admin double click grants exactly once");
            var queued=await Job(asset);Check(queued.Attempts==3&&queued.Error==VideoTimeoutRecovery.Authorized&&queued.State==VideoState.Pending&&queued.Original.Sha256==before.Original.Sha256,"Recovery keeps attempts3 and immutable original; expired upload session reused");
            await using(var db=Db()){Check(await db.AuditLogs.CountAsync(x=>x.Action==VideoTimeoutRecovery.AuditAction)==1,"Exactly one administrative audit receipt");}
            Check(!await Work(asset,Policy(300)),"Rollback to lower time budget leaves grant pending");
            stage="worker-recycle";
            var stop=new PauseAfterClaimCommit();using var cancel=new CancellationTokenSource();
            await using(var db=new PortalDbContext(DbOptions(stop)))
            {
                var task=new VideoWorker(db,storage,policy,new UnavailableInquiryScanner(),new(policy,storage),new(db)).RunOnceAsync(asset,cancel.Token);
                await stop.ClaimCommitted.Task.WaitAsync(TimeSpan.FromSeconds(15));var active=await Job(asset);
                Check(active.Attempts==4&&active.LeaseUntilUtc>DateTimeOffset.UtcNow.AddMinutes(55),"Fourth claim uses original attempt counter and sufficiently long exclusive lease");
                Check(!await Work(asset),"Second worker cannot process leased recovery job");cancel.Cancel();
                try{await task;throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
            }
            queued=await Job(asset);Check(queued.Attempts==3&&queued.State==VideoState.Pending&&queued.Error==VideoTimeoutRecovery.Authorized,"Graceful recycle refunds only active recovery and keeps its grant");
            Check(await Work(asset),"Worker consumes authorized recovery with original upload");
            var ready=await Job(asset);Check(ready.State==VideoState.Ready&&ready.Attempts==4&&ready.Original.ScanStatus=="NotScanned"&&ready.Original.ValidationStatus==VideoOptions.NoAvValidated&&ready.ScannerProvider==null,"Fourth attempt transcodes to Ready without fabricating Clean");
            await using(var db=Db()){Check(await VideoReadiness.ReadyAsync(db,storage,ready.Original,false,policy:policy),"Original/output/poster SHA and readiness remain enforced");Check(await db.Contents.AllAsync(x=>!x.IsPublic)&&await db.Revisions.CountAsync()==2,"Recovery does not publish or create editorial revision");}
            await Deny(()=>Queue(asset),409,"Ready or exhausted fourth attempt cannot receive another grant");
            stage="failure-bounds";
            var second=await Uploaded("second");await Queue(second);var original=MediaService.ResolveMediaPath(storage,(await Job(second)).Original.StorageKey);
            var corrupt=sample.ToArray();corrupt[^1]^=1;await File.WriteAllBytesAsync(original,corrupt);
            Check(await Work(second),"Worker verifies reused original bytes before native processing");var failed=await Job(second);
            Check(failed.Attempts==4&&failed.State==VideoState.Failed&&failed.Error=="VIDEO_ORIGINAL_HASH_MISMATCH"&&failed.Mp4Key==null,"Same-size original tampering fails hash validation");
            await Mutate(second,x=>{x.Error="VIDEO_PROCESSOR_TIMEOUT";x.AvailableUtc=DateTimeOffset.UtcNow.AddMinutes(-1);});
            await Deny(()=>Queue(second),409,"Fourth timeout cannot grant fifth attempt");Check(!await Work(second),"Worker never auto-retries exhausted fourth attempt");
            var third=await Uploaded("third");await Queue(third);
            await Mutate(third,x=>{x.State=VideoState.Processing;x.Attempts=4;x.LeaseToken=Guid.NewGuid();x.LeaseUntilUtc=DateTimeOffset.UtcNow.AddMinutes(-1);});
            Check(!await Work(third)&&(await Job(third)).State==VideoState.Failed,"Hard crash expired fourth lease fails closed without fifth attempt");
            await using(var db=Db())
            {
                var audits=await db.AuditLogs.Where(x=>x.Action=="video.processing.completed").Select(x=>x.Detail).ToListAsync();
                Check(audits.Any(x=>x.Contains("\"ProcessSeconds\":1800")&&x.Contains("\"Operation\":\"poster\"")),"Completion audit records actual time budget and native phase");
                Check(audits.Any(x=>x.Contains("VIDEO_ORIGINAL_HASH_MISMATCH")&&x.Contains("\"Operation\":null")&&x.Contains("\"DeadlineSeconds\":0")),"Failure before FFmpeg does not inherit a stale native operation");
                Check(await db.Users.CountAsync()==3&&await db.Contents.AllAsync(x=>!x.IsPublic),"Only synthetic accounts; no automatic publication");
            }
            stage="COMPLETE";
        }
        catch(Exception e){Environment.ExitCode=1;await File.WriteAllTextAsync(Path.Combine(run,"failure.txt"),e.ToString());Console.WriteLine("BLOCKED "+stage+" "+e.GetType().Name);}
        finally
        {
            if(web!=null){await web.StopAsync();await web.DisposeAsync();}
            await File.WriteAllTextAsync(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{stage,passed=checks.Count,checks,instance,operationalSqlTouched=false,liveVideoTouched=false,fullRegression=false},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"RESULT {stage} checks={checks.Count} evidence={run}");
        }
    }
}
