using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

if (args is ["--maintenance"]) { await MaintenanceChecks.RunAsync(); return; }
if (args.Length == 3 && args[0] == "--publication") { await PublicationChecks.RunAsync(args[1], args[2]); return; }

// Lifecycle delta only: the existing 60-case HTTP workflow suite remains separate.
// This harness never loads PortalConfiguration or any operational SQL configuration.
if (args.Length != 3 || !Regex.IsMatch(args[0], "^LPWebVideo_[a-f0-9]{12}$"))
    throw new ArgumentException("Fresh isolated LocalDB instance/run/tool metadata required");
var root = Directory.GetCurrentDirectory();
var run = Path.GetFullPath(args[1]);
if (!run.StartsWith(Path.Combine(root, ".local", "tests", "web-video-"), StringComparison.OrdinalIgnoreCase) ||
    File.Exists(Path.Combine(run, "result.json"))) throw new Exception("New isolated test path required");
var cs = new SqlConnectionStringBuilder { DataSource = @"(localdb)\" + args[0], InitialCatalog = "LandPortal_Test", IntegratedSecurity = true, Encrypt = true, TrustServerCertificate = true, Pooling = false };
DbContextOptions<PortalDbContext> DbOptions(IInterceptor? interceptor = null)
{
    var b = new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString, o => o.UseCompatibilityLevel(160));
    if (interceptor != null) b.AddInterceptors(interceptor);
    return b.Options;
}
var storage = new StorageOptions { MediaRoot = Path.Combine(run, "media"), SnapshotRoot = Path.Combine(run, "snapshots"), TempRoot = Path.Combine(run, "temp"), KeysRoot = Path.Combine(run, "keys") };
foreach (var path in new[] { storage.MediaRoot, storage.SnapshotRoot, storage.TempRoot, storage.KeysRoot }) Directory.CreateDirectory(path);
var toolData = JsonDocument.Parse(File.ReadAllText(args[2])).RootElement;
VideoOptions Policy(bool noAv) => new()
{
    Enabled = true, ApprovedManagedVideoWithoutAv = noAv, MaxFileBytes = 10 * 1024 * 1024, Threads = 1,
    FfmpegPath = toolData.GetProperty("ffmpegPath").GetString()!, FfprobePath = toolData.GetProperty("ffprobePath").GetString()!,
    FfmpegSha256 = toolData.GetProperty("ffmpegSha256").GetString()!, FfprobeSha256 = toolData.GetProperty("ffprobeSha256").GetString()!
};
var policy = Policy(true);
var processor = new VideoProcessor(policy, storage);
var checks = new List<string>();
var stage = "new-isolated-sql";
WebApplication? web = null;
void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks.Add(message); Console.WriteLine("PASS " + message); }
async Task<VideoFile> Job(Guid id) { await using var db = new PortalDbContext(DbOptions()); return await db.Set<VideoFile>().AsNoTracking().Include(x => x.Original).SingleAsync(x => x.MediaAssetId == id); }
async Task Wait(Func<Task<bool>> condition, string message, int seconds = 45)
{
    var watch = Stopwatch.StartNew();
    while (watch.Elapsed < TimeSpan.FromSeconds(seconds)) { if (await condition()) return; await Task.Delay(100); }
    throw new TimeoutException(message);
}
async Task Native(params string[] arguments)
{
    var info = new ProcessStartInfo(policy.FfmpegPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in arguments) info.ArgumentList.Add(a);
    using var p = Process.Start(info)!;
    var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try { await p.WaitForExitAsync(timeout.Token); await stdout; var error = await stderr; if (p.ExitCode != 0) throw new Exception("Native fixture process failed: " + error); }
    finally { if (!p.HasExited) p.Kill(true); }
}
async Task<WebApplication> StartHost(bool noAv)
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", ContentRootPath = run, Args = [] });
    builder.Configuration.Sources.Clear();
    // Missing local clamd is a genuine fail-closed scanner result, not a fake Clean.
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Scanner:Provider"] = "Clamd", ["Scanner:ClamdPort"] = "0", ["Mail:Transport"] = "Disabled" });
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddSingleton(storage);
    // Explicit test-only policy; no environment/profile guard is changed in application code.
    builder.Services.AddSingleton(Policy(noAv));
    builder.Services.AddScoped(_ => new PortalDbContext(DbOptions()));
    builder.Services.AddScoped<CurrentPermissionService>();
    builder.Services.AddHostedService<WebVideoProcessingService>();
    var app = builder.Build();
    app.MapGet("/alive", () => Results.Text("isolated lifecycle host"));
    await app.StartAsync();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var http = new HttpClient(); using var response = await http.GetAsync(address + "/alive");
    Check(response.StatusCode == HttpStatusCode.OK, "Kestrel host responds while production video hosted service is active");
    return app;
}
async Task StopHost()
{
    if (web == null) return;
    await web.StopAsync(TimeSpan.FromSeconds(12)); await web.DisposeAsync(); web = null;
}
try
{
    await using (var sql = new SqlConnection(new SqlConnectionStringBuilder(cs.ConnectionString) { InitialCatalog = "master" }.ConnectionString))
    {
        await sql.OpenAsync(); using var q = sql.CreateCommand();
        q.CommandText = "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),CONVERT(int,SERVERPROPERTY('IsLocalDB')),DB_ID('LandPortal_Test')";
        using (var r = await q.ExecuteReaderAsync()) { await r.ReadAsync(); Check(r.GetString(0).Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) && r.GetInt32(1) == 1 && r.IsDBNull(2), "Fresh local SQL Server instance, no existing database touched"); }
        var mdf = Path.Combine(run, "LandPortal_Test.mdf"); var ldf = Path.Combine(run, "LandPortal_Test_log.ldf");
        if (File.Exists(mdf) || File.Exists(ldf)) throw new Exception("Fixture SQL files already exist");
        q.CommandText = "CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'" + mdf.Replace("'", "''") + "') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'" + ldf.Replace("'", "''") + "')";
        await q.ExecuteNonQueryAsync(); q.CommandText = "ALTER DATABASE [LandPortal_Test] SET COMPATIBILITY_LEVEL=160"; await q.ExecuteNonQueryAsync();
    }
    stage = "fixture-schema-and-account";
    await using (var db = new PortalDbContext(DbOptions()))
    {
        await db.Database.MigrateAsync(); var history = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Check(history.Length == 18 && history.Last() == "20261110050000_HomeEventBanner", "Official schema18 applied only to new isolated database");
    }
    var setup = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
    setup.Services.AddLogging(); setup.Services.AddScoped(_ => new PortalDbContext(DbOptions()));
    setup.Services.AddIdentityCore<PortalUser>(PortalIdentityPolicy.Configure).AddRoles<IdentityRole>().AddEntityFrameworkStores<PortalDbContext>();
    using var identityHost = setup.Build(); using var scope = identityHost.Services.CreateScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
    var owner = new PortalUser { UserName = "fixture-video-owner", Email = "video@example.invalid", EmailConfirmed = true, DisplayName = "Synthetic test only" };
    Check((await users.CreateAsync(owner, "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)))).Succeeded &&
        (await users.AddClaimsAsync(owner, new[] { Permissions.Create, Permissions.EditOwn, Permissions.ViewOwn, Permissions.ManageMedia, Permissions.ManageVideos }.Select(x => new Claim("permission", x)))).Succeeded,
        "Synthetic Identity owner has only required CMS upload permissions");
    var content = new Content { Kind = ContentKind.Video, State = WorkflowState.Draft, OwnerUserId = owner.Id };
    await using (var db = new PortalDbContext(DbOptions())) { db.Contents.Add(content); await db.SaveChangesAsync(); }
    Check((await processor.HealthAsync()).Ready, "Real pinned FFmpeg/FFprobe executable and required encoders ready");
    var sample = Path.Combine(run, "synthetic.mp4");
    await Native("-nostdin", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "2", "-threads", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-metadata", "comment=ISOLATED TEST ONLY", sample);
    var bytes = await File.ReadAllBytesAsync(sample); var sha = Convert.ToHexStringLower(SHA256.HashData(bytes)); Guid assetId;
    await using (var db = new PortalDbContext(DbOptions()))
    {
        var upload = new VideoUploadService(db, storage, policy, new CurrentPermissionService(db));
        var session = await upload.CreateAsync(content.Id, owner.Id, "fixture.mp4", "video/mp4", bytes.Length, TextNormalizer.Hash($"0:{bytes.Length}:{sha}"));
        await upload.ChunkAsync(session.Id, owner.Id, 0, sha, new MemoryStream(bytes));
        assetId = (await upload.CompleteAsync(session.Id, owner.Id)).MediaAssetId!.Value;
    }
    var pending = await Job(assetId);
    Check(pending.State == VideoState.Pending && pending.Original.ScanStatus == "NotScanned" && pending.Original.ValidationStatus == VideoOptions.NoAvPending && pending.Original.IsQuarantined, "Official upload creates a durable private NotScanned job");
    stage = "claim-cancel-and-exclusion";
    var stopAtCommit = new PauseAfterClaimCommit(); using var recycle = new CancellationTokenSource();
    await using (var claimedDb = new PortalDbContext(DbOptions(stopAtCommit)))
    {
        var work = new VideoWorker(claimedDb, storage, policy, new UnavailableInquiryScanner(), processor, new CurrentPermissionService(claimedDb)).RunOnceAsync(assetId, recycle.Token);
        await stopAtCommit.ClaimCommitted.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var claimed = await Job(assetId);
        Check(claimed.State == VideoState.Scanning && claimed.LeaseToken.HasValue && claimed.Attempts == 1, "Real SQL claim commits before the simulated graceful recycle");
        await using (var secondDb = new PortalDbContext(DbOptions()))
            Check(!await new VideoWorker(secondDb, storage, policy, new UnavailableInquiryScanner(), processor, new CurrentPermissionService(secondDb)).RunOnceAsync(assetId), "Concurrent worker cannot claim the active SQL lease");
        recycle.Cancel(); var cancelled = false;
        try { await work; } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Graceful cancellation propagates instead of false successful processing");
    }
    pending = await Job(assetId);
    Check(pending.State == VideoState.Pending && pending.LeaseToken == null && pending.LeaseUntilUtc == null && pending.Attempts == 0 && pending.Error == "VIDEO_HOST_STOPPED" && pending.ReadyUtc == null && pending.Mp4Key == null,
        "Cancellation releases exact owned lease and refunds attempt without output/publication changes");
    stage = "policy-off-host";
    web = await StartHost(false);
    await Wait(() => Task.FromResult(VideoWorkerStatus.Inspect(storage).Code == "VIDEO_WORKER_BLOCKED"), "Host scanner/policy status missing");
    pending = await Job(assetId);
    Check(pending.State == VideoState.Pending && pending.Attempts == 0 && pending.Original.ScanStatus == "NotScanned", "Host with no-AV off and no scanner leaves durable job untouched");
    await StopHost();
    Check(VideoWorkerStatus.Inspect(storage).Code == "VIDEO_WORKER_STOPPED", "Graceful Web shutdown records stopped heartbeat");
    stage = "cancellation-ownership-fence";
    var replacedLease = Guid.NewGuid(); var pauseReplacement = new PauseAfterClaimCommit();
    using (var stoppedOwner = new CancellationTokenSource())
    await using (var claimedDb = new PortalDbContext(DbOptions(pauseReplacement)))
    {
        var work = new VideoWorker(claimedDb, storage, policy, new UnavailableInquiryScanner(), processor, new CurrentPermissionService(claimedDb)).RunOnceAsync(assetId, stoppedOwner.Token);
        await pauseReplacement.ClaimCommitted.Task.WaitAsync(TimeSpan.FromSeconds(15));
        // Concurrent ownership replacement is test-only: shutdown must not undo another lease.
        await using (var db = new PortalDbContext(DbOptions()))
            await db.Set<VideoFile>().Where(x => x.MediaAssetId == assetId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseToken, replacedLease).SetProperty(x => x.State, VideoState.Processing));
        stoppedOwner.Cancel();
        try { await work; } catch (OperationCanceledException) { }
        var changedOwner = await Job(assetId);
        Check(changedOwner.State == VideoState.Processing && changedOwner.LeaseToken == replacedLease && changedOwner.Attempts == 1,
            "Cancelled old processor cannot release or refund a replacement owner's lease");
    }
    // Simulate a process killed after claiming, without fabricating output or Clean.
    await using (var db = new PortalDbContext(DbOptions()))
    {
        var j = await db.Set<VideoFile>().SingleAsync(x => x.MediaAssetId == assetId);
        j.State = VideoState.Processing; j.Attempts = 1; j.LeaseToken = Guid.NewGuid(); j.LeaseUntilUtc = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
    }
    stage = "host-recovery-transcode";
    web = await StartHost(true);
    await Wait(async () => (await Job(assetId)).State == VideoState.Ready, "Hosted pipeline did not complete recovered SQL job");
    var ready = await Job(assetId);
    Check(ready.Attempts == 2 && ready.LeaseToken == null && ready.ReadyUtc.HasValue, "New Web host recovers expired lease and processes durable queue automatically");
    Check(ready.Original.ScanStatus == "NotScanned" && ready.Original.ValidationStatus == VideoOptions.NoAvValidated && ready.ScannerProvider == null && ready.ScannerVersion == null && ready.ScannedUtc == null && ready.ScannedSha256 == null,
        "Hosted processing keeps truthful NotScanned and separate technical validation");
    await using (var db = new PortalDbContext(DbOptions()))
    {
        Check(await VideoReadiness.ReadyAsync(db, storage, ready.Original, false, policy: policy), "Production readiness verifies original, MP4 and poster hashes");
        Check(!await VideoReadiness.ReadyAsync(db, storage, ready.Original, false, policy: Policy(false)), "Disabling no-AV still rejects unscanned output for publication");
    }
    var mp4 = MediaService.ResolveMediaPath(storage, ready.Mp4Key!); var poster = MediaService.ResolveMediaPath(storage, ready.PosterKey!);
    Check(ready.Mp4Sha256 != sha && VideoProcessor.BrowserCompatible(await processor.ProbeAsync(mp4)) && File.ReadAllBytes(poster).Take(3).SequenceEqual(new byte[] { 0xff, 0xd8, 0xff }), "Real hosted conversion creates new H264/AAC MP4 and JPEG poster");
    await Native("-nostdin", "-v", "error", "-i", mp4, "-f", "null", "-"); Check(true, "Hosted MP4 output decodes successfully with real FFmpeg");
    await Wait(() => Task.FromResult(VideoWorkerStatus.Inspect(storage).Code == "VIDEO_WORKER_READY"), "Ready host heartbeat missing");
    Check(VideoWorkerStatus.Read(storage)?.ProcessId == Environment.ProcessId, "Video heartbeat belongs to Web host process, no separate Worker process");
    await StopHost();
    stage = "restart-no-repeat";
    web = await StartHost(true);
    await Wait(() => Task.FromResult(VideoWorkerStatus.Inspect(storage).Code == "VIDEO_WORKER_READY"), "Restart heartbeat missing");
    await Task.Delay(6000); // Observe one complete queue poll after health/host startup.
    var unchanged = await Job(assetId);
    await using (var db = new PortalDbContext(DbOptions()))
    {
        Check(unchanged.State == VideoState.Ready && unchanged.Attempts == ready.Attempts && unchanged.ReadyUtc == ready.ReadyUtc && unchanged.Mp4Sha256 == ready.Mp4Sha256 && unchanged.PosterSha256 == ready.PosterSha256 && await db.Set<VideoFile>().CountAsync() == 1 && await db.AuditLogs.CountAsync(x => x.Action == "video.processing.completed") == 1,
            "Restart does not reprocess Ready output or duplicate durable job/completion receipt");
        Check(await db.Users.CountAsync() == 1 && await db.Contents.CountAsync() == 1 && await db.Contents.AllAsync(x => !x.IsPublic && x.State == WorkflowState.Draft), "Fixture remains private; no automatic publication or real accounts");
    }
    stage = "COMPLETE";
}
catch (Exception e)
{
    Environment.ExitCode = 1; Console.WriteLine("BLOCKED " + stage + " " + e.GetType().Name);
    await File.WriteAllTextAsync(Path.Combine(run, "failure.txt"), e.ToString());
}
finally
{
    await StopHost();
    await File.WriteAllTextAsync(Path.Combine(run, "result.json"), JsonSerializer.Serialize(new { stage, passed = checks.Count, checks, operationalSqlTouched = false, sourceAccountsTouched = false, realHttp = "Kestrel lifecycle probe; CMS workflow not repeated", smtp = "Disabled", fullRegression = false, simulatedHardKill = "expired durable lease fixture", gracefulRecycle = "actual SQL claim commit followed by cancellation" }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"RESULT {stage} checks={checks.Count} evidence={run}");
}

sealed class PauseAfterClaimCommit : DbTransactionInterceptor
{
    public TaskCompletionSource ClaimCommitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ClaimCommitted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
