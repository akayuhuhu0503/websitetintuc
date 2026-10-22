using LandPortal.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

// Explicit independent modes: scheduler, mail, inquiry scanner, or video; never crawl.
var config = PortalConfiguration.Load();
var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
var expected = config["Sql:ExpectedDatabase"];
var connection = new SqlConnectionStringBuilder(DatabaseSettings.Connection(config));
var uat=UatDeployment.Validate(config,environment);
if (!uat && (expected is not ("LandPortal_Dev" or "LandPortal_Test") || !connection.IntegratedSecurity ||
    !new[] { @".\SQLEXPRESS", @"localhost\SQLEXPRESS", @"(local)\SQLEXPRESS" }.Contains(connection.DataSource, StringComparer.OrdinalIgnoreCase)))
    throw new InvalidOperationException("Worker local requires the named project's local SQL Server database.");
var storage = StorageOptions.FromConfiguration(config);
if (args.Contains("--documents"))
{
    EditorialDocumentService.RequireTestInfrastructure(config, environment);
    var scanner = InquiryScannerFactory.Create(config, environment);
    if (args.Contains("--health")) { var health = await scanner.HealthAsync(); Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { scope = "LandPortal_Test editorial documents only", health.Ready, health.Code })); return health.Ready ? 0 : 2; }
    Guid? only = null; var at = Array.IndexOf(args, "--asset");
    if (at >= 0) { if (at + 1 >= args.Length || !Guid.TryParse(args[at + 1], out var id)) throw new InvalidOperationException("Invalid document asset ID."); only = id; }
    using var stop = new CancellationTokenSource(); Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    do
    {
        try { await using var db = new PortalDbContext(DatabaseSettings.Options(config)); await new EditorialDocumentService(db, storage, new MediaService(db, storage, MediaUploadOptions.FromConfiguration(config, environment)), new(db)).ScanOnceAsync(scanner, only, stop.Token); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
        catch (Exception error) { Console.Error.WriteLine("Document scan: " + error.GetType().Name + "; SQL lease retained, access stays closed."); if (args.Contains("--once")) return 1; }
        if (args.Contains("--once")) break;
        try { await Task.Delay(TimeSpan.FromSeconds(5), stop.Token); } catch (OperationCanceledException) { break; }
    } while (!stop.IsCancellationRequested);
    return 0;
}
if(args.Contains("--video"))
{
    var video=VideoOptions.FromConfiguration(config,environment);if(!video.Enabled)throw new InvalidOperationException("Video worker requires explicit Development / LandPortal_Test / isolated storage. Production is not approved.");
    var scanner=InquiryScannerFactory.Create(config,environment,video.MaxFileBytes);var processor=new VideoProcessor(video,storage);var health=await processor.HealthAsync();Console.WriteLine("Video processor: "+health.Code+"; ready="+health.Ready+". Original source videos are never selected automatically.");
    if(args.Contains("--health")){var scanHealth=await scanner.HealthAsync();await using var check=new PortalDbContext(DatabaseSettings.Options(config));var queue=await check.Set<LandPortal.Domain.VideoFile>().GroupBy(x=>x.State).Select(x=>new{state=x.Key.ToString(),count=x.Count()}).ToListAsync();var subtitles=await check.Set<LandPortal.Domain.VideoSubtitle>().GroupBy(x=>x.State).Select(x=>new{state=x.Key.ToString(),count=x.Count()}).ToListAsync();var stale=await check.Set<LandPortal.Domain.VideoFile>().CountAsync(x=>(x.State==LandPortal.Domain.VideoState.Processing||x.State==LandPortal.Domain.VideoState.Scanning)&&x.LeaseUntilUtc<DateTimeOffset.UtcNow);Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,processor=new{health.Ready,health.Code},scanner=new{scanHealth.Ready,scanHealth.Code},queue,subtitles,expiredLeases=stale}));return health.Ready&&scanHealth.Ready?0:2;}
    if(args.Contains("--gc")){await using var cleanup=new PortalDbContext(DatabaseSettings.Options(config));await new VideoUploadService(cleanup,storage,video,new(cleanup)).SweepAsync();Console.WriteLine("Bounded video TTL cleanup completed; committed originals and revisions retained.");return 0;}
    Guid? only=null;var at=Array.IndexOf(args,"--asset");if(at>=0){if(at+1>=args.Length||!Guid.TryParse(args[at+1],out var id))throw new InvalidOperationException("Invalid Test video asset ID.");only=id;}
    using var stop=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
    do{try{await using var db=new PortalDbContext(DatabaseSettings.Options(config));var rights=new CurrentPermissionService(db);await new VideoUploadService(db,storage,video,rights).SweepAsync(stop.Token);await new VideoWorker(db,storage,video,scanner,processor,rights).RunOnceAsync(only,stop.Token);await new VideoSubtitleService(db,storage,video,rights).ScanOnceAsync(scanner,only,stop.Token);}catch(OperationCanceledException)when(stop.IsCancellationRequested){break;}catch(Exception e){Console.Error.WriteLine("Video worker error: "+e.GetType().Name+"; durable lease retained.");if(args.Contains("--once"))return 1;}if(args.Contains("--once"))break;try{await Task.Delay(TimeSpan.FromSeconds(5),stop.Token);}catch(OperationCanceledException){break;}}while(!stop.IsCancellationRequested);
    return 0;
}
if(args.Contains("--scan"))
{
    if(!uat&&(environment!="Development"||expected!="LandPortal_Test"))throw new InvalidOperationException("Scanner worker requires isolated Test or validated UAT; Production is not enabled.");
    var protection=DataProtectionProvider.Create(new DirectoryInfo(storage.KeysRoot),b=>b.SetApplicationName("LandPortal"));var scanner=InquiryScannerFactory.Create(config,environment);var health=await scanner.HealthAsync();Console.WriteLine("Inquiry scanner health: "+health.Code+"; ready="+health.Ready+". No source media is scanned or relabeled.");
    if(args.Contains("--health"))
    {
        await using var check=new PortalDbContext(DatabaseSettings.Options(config));var queue=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(check.InquiryAttachments.GroupBy(x=>x.ScanState).Select(g=>new{State=g.Key.ToString(),Count=g.Count()}));Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,provider=config["Scanner:Provider"]??"Clamd",health.Ready,health.Code,queue}));return health.Ready?0:2;
    }
    using var cancellation=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};Guid? only=null;var at=Array.IndexOf(args,"--file");if(at>=0){if(at+1>=args.Length||!Guid.TryParse(args[at+1],out var id))throw new InvalidOperationException("Invalid Test file ID.");only=id;}
    do
    {
        await using var db=new PortalDbContext(DatabaseSettings.Options(config));var media=new MediaService(db,storage,MediaUploadOptions.FromConfiguration(config,environment));var files=new InquiryAttachmentService(db,storage,media,protection,InquiryFileOptions.FromConfiguration(config,environment));await files.SweepAsync();await new InquiryScanWorker(db,files,scanner).RunOnceAsync(only,cancellation.Token);
        if(args.Contains("--once"))break;try{await Task.Delay(TimeSpan.FromSeconds(5),cancellation.Token);}catch(OperationCanceledException){break;}
    }while(!cancellation.IsCancellationRequested);
    return 0;
}
if(args.Contains("--mail"))
{
    var mail=InteractionMailOptions.FromConfiguration(config,environment);mail.RequireAvailable();
    var protection=DataProtectionProvider.Create(new DirectoryInfo(storage.KeysRoot),b=>b.SetApplicationName("LandPortal"));
    using var cancellation=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
    Guid? only=null;var index=Array.IndexOf(args,"--outbox");
    if(index>=0){if(index+1>=args.Length||!Guid.TryParse(args[index+1],out var parsed))throw new InvalidOperationException("Invalid Test outbox identifier.");only=parsed;}
    Console.WriteLine(mail.SmtpHost is null ? "Test outbox: loopback sink only." : "UAT outbox: explicit approved STARTTLS relay; Uncertain requires reconciliation.");
    do
    {
        try{await using var db=new PortalDbContext(DatabaseSettings.Options(config));await new InteractionOutboxService(db,protection,mail,new CurrentPermissionService(db)).RunOnceAsync(only);}
        catch(Exception error){Console.Error.WriteLine("Outbox operation failed: "+error.GetType().Name+". Durable state retained.");if(args.Contains("--once"))return 1;}
        if(args.Contains("--once"))break;
        try{await Task.Delay(TimeSpan.FromSeconds(2),cancellation.Token);}catch(OperationCanceledException){break;}
    }while(!cancellation.IsCancellationRequested);
    return 0;
}
var mediaOptions = MediaUploadOptions.FromConfiguration(config, environment);
if (mediaOptions.AllowUnscannedDevelopmentUploads && !mediaOptions.IsDevelopmentLoopback)
    throw new InvalidOperationException("Development media exception is forbidden outside Development loopback; worker startup was blocked.");
var options = DatabaseSettings.Options(config); var schedulerOptions = SchedulerOptions.FromConfiguration(config);
_ = SchedulerService.VietnamTimeZone; // Fail clearly at startup if this host's Vietnam mapping is unavailable.
var workerId = "worker-" + Guid.NewGuid().ToString("N");
using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping.Cancel(); };
var once = args.Contains("--once");
Guid? onlyJobId = null;
var jobArgument = Array.IndexOf(args, "--job");
if (jobArgument >= 0)
{
    if (expected != "LandPortal_Test" || jobArgument + 1 >= args.Length || !Guid.TryParse(args[jobArgument + 1], out var parsedJob))
        throw new InvalidOperationException("--job scopes an isolated LandPortal_Test worker probe; it cannot target Dev.");
    onlyJobId = parsedJob;
}
Console.WriteLine($"Publication worker started; environment={environment}; poll={schedulerOptions.PollSeconds}s; catch-up={schedulerOptions.CatchUpMinutes}min; lease={schedulerOptions.LeaseSeconds}s. Ctrl+C stops; unfinished lease expires in SQL.");
do
{
    try
    {
        await using var db = new PortalDbContext(options);
        var service = new SchedulerService(db, new WorkflowService(db, new HtmlPolicy(), mediaOptions, storage), new CurrentPermissionService(db), schedulerOptions);
        await service.HeartbeatAsync(workerId, environment, stopping.Token);
        var lease = await service.ClaimAsync(workerId, stopping.Token, onlyJobId);
        if (lease != null)
        {
            var result = await service.ExecuteAsync(lease, stopping.Token);
            Console.WriteLine($"Publication job {lease.JobId}: {result.State}.");
        }
    }
    catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
    catch (Exception error)
    {
        Console.Error.WriteLine("Publication worker operation failed: " + error.GetType().Name + ". Lease remains durable; retry is bounded by job policy.");
        if (once) return 1;
    }
    if (once || stopping.IsCancellationRequested) break;
    try { await Task.Delay(TimeSpan.FromSeconds(schedulerOptions.PollSeconds), stopping.Token); }
    catch (OperationCanceledException) { break; }
} while (!stopping.IsCancellationRequested);
Console.WriteLine("Publication worker stopped; no pending schedules were reset.");
return 0;
