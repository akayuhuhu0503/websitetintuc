using System.Data.Common;
using System.Diagnostics;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;

public static class Phase2bSchedulerTests
{
    public static async Task WithdrawPublicFixturesAsync(DbContextOptions<PortalDbContext> options,IConfiguration config)
    {
        await using var db=new PortalDbContext(options);
        if(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog != "LandPortal_Test") throw new InvalidOperationException("Fixture cleanup only targets SQL Test.");
        var fixtures=await db.Contents.Where(c=>c.IsPublic&&!c.IsTrashed&&!db.SourceRecords.Any(s=>s.ContentId==c.Id)&&db.Users.Any(u=>u.Id==c.OwnerUserId&&u.UserName!=null&&u.UserName.StartsWith("qa-scheduler-"))).ToListAsync();
        var actor=new ContentActor("qa-scheduler-fixture-cleanup",new HashSet<string>{Permissions.ViewAll,Permissions.Unpublish});
        var workflow=new WorkflowService(db,new HtmlPolicy(),MediaUploadOptions.FromConfiguration(config,"Development"),StorageOptions.FromConfiguration(config));
        foreach(var c in fixtures) await workflow.UnpublishAsync(c.Id,actor,c.RowVersion,Guid.NewGuid());
        // Keep immutable revisions, jobs, attempts, timestamps and audit evidence.
        // Fake-clock publications must not displace real-clock HTTP fixtures in shared Test.
    }
    private sealed class TestClock(DateTimeOffset initial) : TimeProvider
    {
        public DateTimeOffset Now = initial;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class ShortContentQueryTimeout : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { if (command.CommandText.Contains("FROM [Contents]", StringComparison.Ordinal)) command.CommandTimeout = 1; return ValueTask.FromResult(result); }
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> operation) where T : Exception { try { await operation(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    public static IEnumerable<(string Name, Func<Task> Test)> Cases(DbContextOptions<PortalDbContext> options, IConfiguration config)
    {
        if (new SqlConnectionStringBuilder(DatabaseSettings.Connection(config)).InitialCatalog != "LandPortal_Test") throw new InvalidOperationException("Scheduler tests only run on verified LandPortal_Test.");
        var storage = StorageOptions.FromConfiguration(config);
        var root = Path.Combine(config["WorkspaceRoot"]!, ".local", "tests");
        Assert(storage.MediaRoot.StartsWith(root, StringComparison.OrdinalIgnoreCase), "Scheduler media fixtures must use isolated Test storage.");
        PortalDbContext Db() => new(options);
        WorkflowService W(PortalDbContext db, MediaUploadOptions? media = null, TimeProvider? clock = null) => new(db, new HtmlPolicy(), media, storage, clock);
        SchedulerService S(PortalDbContext db, TestClock clock, MediaUploadOptions? media = null) => new(db, W(db, media, clock), new CurrentPermissionService(db), new SchedulerOptions(), clock);
        async Task<ContentActor> User(PortalDbContext db)
        {
            var id = "qa-scheduler-" + Guid.NewGuid().ToString("N");
            db.Users.Add(new PortalUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString("N") });
            foreach (var value in Phase2aSqlTests.AllPermissions.Append(Permissions.Schedule)) db.UserClaims.Add(new IdentityUserClaim<string> { UserId = id, ClaimType = "permission", ClaimValue = value });
            await db.SaveChangesAsync(); return await new CurrentPermissionService(db).GetAsync(id);
        }
        async Task<(WorkflowResult Content, ContentActor Actor)> Draft(PortalDbContext db)
        {
            var actor = await User(db);
            var value = await W(db).SaveAsync(null, Phase2aSqlTests.Input("QA scheduler " + Guid.NewGuid().ToString("N")), actor, null, Guid.NewGuid());
            return (value, actor);
        }
        yield return ("2B scheduler UTC+7 mapping and SQL durable direct/approved publication", async () =>
        {
            var local = new DateTime(2026, 10, 13, 8, 30, 0, DateTimeKind.Unspecified);
            Assert(SchedulerService.VietnamTimeZone.BaseUtcOffset == TimeSpan.FromHours(7) && SchedulerService.VietnamTimeZone.Id == (OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Ho_Chi_Minh"), "Actual host Vietnam timezone ID mapping was not verified.");
            Assert(SchedulerService.VietnamToUtc(local) == new DateTimeOffset(2026, 10, 13, 1, 30, 0, TimeSpan.Zero), "Vietnam UTC conversion depended on host timezone.");
            Assert(SchedulerService.ToVietnam(SchedulerService.VietnamToUtc(local)) == local, "Vietnam date did not round trip.");
            await using var db = Db(); var clock = new TestClock(DateTimeOffset.UtcNow);
            var (content, actor) = await Draft(db); var service = S(db, clock);
            var job = await service.ScheduleAsync(content.ContentId, content.CurrentRevisionId!.Value, clock.Now.AddMinutes(1), true, actor, content.RowVersion);
            Assert(await service.ClaimAsync("qa-before", onlyJobId:job.Id) == null, "Future job was claimed before due time.");
            clock.Now = clock.Now.AddMinutes(1); var lease = await service.ClaimAsync("qa-direct", onlyJobId:job.Id);
            Assert(lease?.JobId == job.Id && (await service.ExecuteAsync(lease!)).State == "Completed", "Durable direct schedule did not publish.");
            db.ChangeTracker.Clear(); var published = await db.Contents.SingleAsync(x => x.Id == content.ContentId);
            Assert(published.IsPublic && published.PublishedRevisionId == content.CurrentRevisionId, "Scheduled publication selected a different revision.");
            var (next, requester) = await Draft(db); var reviewer = await User(db);
            var submitted = await W(db).SubmitAsync(next.ContentId, next.CurrentRevisionId, requester, next.RowVersion, Guid.NewGuid());
            var approved = await W(db).ApproveAsync(next.ContentId, submitted.CurrentRevisionId!.Value, reviewer, submitted.RowVersion, Guid.NewGuid());
            var approvedJob = await service.ScheduleAsync(next.ContentId, approved.CurrentRevisionId!.Value, clock.Now.AddSeconds(1), false, requester, approved.RowVersion);
            clock.Now = clock.Now.AddSeconds(1); var approvedLease = await service.ClaimAsync("qa-approved", onlyJobId:approvedJob.Id);
            Assert(approvedLease?.JobId == approvedJob.Id && (await service.ExecuteAsync(approvedLease!)).State == "Completed", "Approved schedule failed.");
            Assert(approvedJob.ReviewerUserId == reviewer.UserId, "Schedule did not bind approval principal.");
        });
        yield return ("2B scheduler revoked requester and reviewer rights become NeedsReview", async () =>
        {
            foreach (var revokeReviewer in new[] { false, true })
            {
                await using var db = Db(); var clock = new TestClock(DateTimeOffset.UtcNow); var (content, actor) = await Draft(db);
                var reviewer = await User(db); var submitted = await W(db).SubmitAsync(content.ContentId, content.CurrentRevisionId, actor, content.RowVersion, Guid.NewGuid());
                var approved = await W(db).ApproveAsync(content.ContentId, submitted.CurrentRevisionId!.Value, reviewer, submitted.RowVersion, Guid.NewGuid());
                var service = S(db, clock); var job = await service.ScheduleAsync(content.ContentId, approved.CurrentRevisionId!.Value, clock.Now.AddSeconds(1), false, actor, approved.RowVersion);
                var target = revokeReviewer ? reviewer.UserId : actor.UserId;
                var claims = await db.UserClaims.Where(x => x.UserId == target && x.ClaimType == "permission").ToListAsync(); db.UserClaims.RemoveRange(claims); await db.SaveChangesAsync();
                clock.Now = clock.Now.AddSeconds(1); var lease = await service.ClaimAsync("qa-revoked", onlyJobId:job.Id);
                Assert(lease?.JobId == job.Id && (await service.ExecuteAsync(lease!)).State == "NeedsReview", "Revoked publication rights were bypassed.");
                Assert(!await db.Contents.AnyAsync(x => x.Id == content.ContentId && x.IsPublic), "Revoked principal published.");
            }
        });
        yield return ("2B scheduler cancellation and replacement fence the old running lease", async () =>
        {
            await using var db = Db(); var clock = new TestClock(DateTimeOffset.UtcNow); var (content, actor) = await Draft(db); var service = S(db, clock);
            var job = await service.ScheduleAsync(content.ContentId, content.CurrentRevisionId!.Value, clock.Now.AddSeconds(1), true, actor, content.RowVersion);
            await Expect<DbUpdateConcurrencyException>(() => service.ScheduleAsync(content.ContentId, content.CurrentRevisionId!.Value, clock.Now.AddSeconds(2), true, actor, content.RowVersion));
            clock.Now = clock.Now.AddSeconds(1); var oldLease = await service.ClaimAsync("qa-old", onlyJobId:job.Id);
            var currentJob = await db.PublicationJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
            var next = await service.ScheduleAsync(content.ContentId, content.CurrentRevisionId!.Value, clock.Now.AddSeconds(1), true, actor, content.RowVersion, currentJob.Id, currentJob.RowVersion);
            Assert(next.Generation > currentJob.Generation, "Explicit replacement did not create a newer generation.");
            Assert((await service.ExecuteAsync(oldLease!)).State == "Superseded", "Old running lease published after replacement.");
            clock.Now = clock.Now.AddSeconds(1); var newLease = await service.ClaimAsync("qa-new", onlyJobId:next.Id);
            Assert(newLease?.JobId == next.Id, "Replacement was not the active SQL job.");
            var cancelJob = await db.PublicationJobs.AsNoTracking().SingleAsync(x => x.Id == next.Id);
            Assert((await service.CancelAsync(next.Id, actor, cancelJob.RowVersion)).State == "Canceled", "Running job cancellation failed.");
            Assert((await service.ExecuteAsync(newLease!)).State == "Superseded" && !await db.Contents.AnyAsync(x => x.Id == content.ContentId && x.IsPublic), "Committed cancellation did not fence publication.");
        });
        yield return ("2B scheduler two claimants, expired lease recovery and commit reconciliation publish once", async () =>
        {
            await using var seed = Db(); var clock = new TestClock(DateTimeOffset.UtcNow); var (content, actor) = await Draft(seed);
            var job = await S(seed, clock).ScheduleAsync(content.ContentId, content.CurrentRevisionId!.Value, clock.Now.AddSeconds(1), true, actor, content.RowVersion);
            clock.Now = clock.Now.AddSeconds(1);
            await using var first = Db(); await using var second = Db();
            var leases = await Task.WhenAll(S(first, clock).ClaimAsync("qa-race-one", onlyJobId:job.Id), S(second, clock).ClaimAsync("qa-race-two", onlyJobId:job.Id));
            Assert(leases.Count(x => x?.JobId == job.Id) == 1, "Two SQL claimants acquired the same live lease.");
            var oldLease = leases.Single(x => x != null)!; clock.Now = clock.Now.AddSeconds(61);
            var recovered = await S(seed, clock).ClaimAsync("qa-restarted", onlyJobId:job.Id);
            Assert(recovered?.JobId == job.Id && recovered.Token != oldLease.Token, "Expired SQL lease did not survive worker restart.");
            Assert((await S(first, clock).ExecuteAsync(oldLease)).State == "Superseded", "Expired worker token could commit.");
            Assert((await S(seed, clock).ExecuteAsync(recovered!)).State == "Completed", "Reclaimed lease failed to publish.");
            Assert((await S(second, clock).ExecuteAsync(recovered!)).State == "Completed", "Lost acknowledgement was not reconciled from SQL completion.");
            Assert(await seed.AuditLogs.CountAsync(x => x.ContentId == content.ContentId && (x.Action == "PublishDirect" || x.Action == "ScheduledPublishDirect")) == 1, "Business publication audit was duplicated.");
            Assert(await seed.PublicationJobAttempts.CountAsync(x => x.JobId == job.Id) == 2 && await seed.PublicationJobAttempts.AnyAsync(x => x.JobId == job.Id && x.Outcome == "LeaseExpired"), "Technical attempts did not preserve lease recovery history.");
        });
        yield return ("2B scheduler revision editing invalidates scheduled approval and manual publication cancels schedule", async () =>
        {
            await using var db = Db(); var clock = new TestClock(DateTimeOffset.UtcNow); var (content, actor) = await Draft(db); var service = S(db, clock);
            var job = await service.ScheduleAsync(content.ContentId, content.CurrentRevisionId!.Value, clock.Now.AddSeconds(1), true, actor, content.RowVersion);
            var revised = await W(db).SaveAsync(content.ContentId, Phase2aSqlTests.Input("QA changed exact revision"), actor, content.RowVersion, Guid.NewGuid());
            db.ChangeTracker.Clear(); var invalid = await db.PublicationJobs.SingleAsync(x => x.Id == job.Id);
            Assert(!invalid.IsActive && invalid.State == PublicationJobState.NeedsReview && invalid.Generation > job.Generation, "Editing did not invalidate exact scheduled revision.");
            var next = await service.ScheduleAsync(content.ContentId, revised.CurrentRevisionId!.Value, clock.Now.AddSeconds(2), true, actor, revised.RowVersion);
            await W(db).PublishDirectAsync(content.ContentId, revised.CurrentRevisionId!.Value, actor, revised.RowVersion, Guid.NewGuid());
            db.ChangeTracker.Clear(); Assert(!await db.PublicationJobs.AnyAsync(x => x.Id == next.Id && x.IsActive), "Manual publish left a stale active schedule.");
        });
        yield return ("2B scheduler catch-up policy and completed-job immutability", async () =>
        {
            foreach (var minutes in new[] { 14, 16 })
            {
                await using var db = Db(); var clock = new TestClock(DateTimeOffset.UtcNow); var (content, actor) = await Draft(db); var service = S(db, clock);
                var job = await service.ScheduleAsync(content.ContentId, content.CurrentRevisionId!.Value, clock.Now.AddSeconds(1), true, actor, content.RowVersion);
                clock.Now = clock.Now.AddMinutes(minutes).AddSeconds(1); var lease = await service.ClaimAsync("qa-catchup", onlyJobId:job.Id);
                Assert((await service.ExecuteAsync(lease!)).State == (minutes == 14 ? "Completed" : "NeedsReview"), "Catch-up threshold was ignored.");
                if (minutes == 14)
                {
                    var done = await db.PublicationJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
                    Assert((await service.CancelAsync(job.Id, actor, done.RowVersion)).State == "Completed", "Completed publication pretended to cancel.");
                }
            }
        });
        yield return ("2B scheduler actual SQL timeout retries with durable bounded backoff", async () =>
        {
            await using var seed = Db(); var clock = new TestClock(DateTimeOffset.UtcNow); var (content, actor) = await Draft(seed);
            var job = await S(seed, clock).ScheduleAsync(content.ContentId, content.CurrentRevisionId!.Value, clock.Now.AddSeconds(1), true, actor, content.RowVersion);
            clock.Now = clock.Now.AddSeconds(1); var lease = await S(seed, clock).ClaimAsync("qa-timeout", onlyJobId:job.Id);
            await using var blocker = new SqlConnection(DatabaseSettings.Connection(config)); await blocker.OpenAsync();
            await using var lockTx = (SqlTransaction)await blocker.BeginTransactionAsync();
            await using (var command = blocker.CreateCommand()) { command.Transaction = lockTx; command.CommandText = "SELECT Id FROM dbo.Contents WITH(XLOCK,HOLDLOCK) WHERE Id=@id"; command.Parameters.AddWithValue("@id", content.ContentId); await command.ExecuteScalarAsync(); }
            var shortOptions = new DbContextOptionsBuilder<PortalDbContext>(options).AddInterceptors(new ShortContentQueryTimeout()).Options;
            await using var timed = new PortalDbContext(shortOptions);
            var result = await S(timed, clock).ExecuteAsync(lease!);
            Assert(result.State == "Pending", "Actual SQL timeout did not queue bounded retry.");
            await lockTx.RollbackAsync();
            var pending = await seed.PublicationJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
            Assert(pending.NextAttemptUtc > clock.Now && pending.AttemptCount == 1 && pending.CompletedUtc == null, "Retry state was not durable or claimed completion.");
            Assert(await S(seed, clock).ClaimAsync("qa-backoff", onlyJobId:job.Id) == null, "Retry ignored SQL backoff time.");
            clock.Now = pending.NextAttemptUtc!.Value; var retry = await S(seed, clock).ClaimAsync("qa-recovered", onlyJobId:job.Id);
            Assert((await S(seed, clock).ExecuteAsync(retry!)).State == "Completed", "SQL recovery did not publish the queued exact revision.");
            var (exhaustedContent, exhaustedActor) = await Draft(seed);
            var exhaustedJob = await S(seed, clock).ScheduleAsync(exhaustedContent.ContentId, exhaustedContent.CurrentRevisionId!.Value, clock.Now.AddSeconds(1), true, exhaustedActor, exhaustedContent.RowVersion);
            clock.Now = clock.Now.AddSeconds(1);
            await using var exhaustedTx = (SqlTransaction)await blocker.BeginTransactionAsync();
            await using (var command = blocker.CreateCommand()) { command.Transaction = exhaustedTx; command.CommandText = "SELECT Id FROM dbo.Contents WITH(XLOCK,HOLDLOCK) WHERE Id=@id"; command.Parameters.AddWithValue("@id", exhaustedContent.ContentId); await command.ExecuteScalarAsync(); }
            for (var attempt = 1; attempt <= 4; attempt++)
            {
                var nextLease = await S(seed, clock).ClaimAsync("qa-exhausted", onlyJobId:exhaustedJob.Id);
                var failure = await S(timed, clock).ExecuteAsync(nextLease!);
                Assert(failure.State == (attempt < 4 ? "Pending" : "Failed"), "Transient SQL retry did not stop at configured attempt limit.");
                var persisted = await seed.PublicationJobs.AsNoTracking().SingleAsync(x => x.Id == exhaustedJob.Id);
                if (attempt < 4) clock.Now = persisted.NextAttemptUtc!.Value;
                else Assert(!persisted.IsActive && persisted.AttemptCount == 4, "Failed job remained active or attempt history was lost.");
            }
            await exhaustedTx.RollbackAsync();
            Assert(await S(seed, clock).ClaimAsync("qa-limit", onlyJobId:exhaustedJob.Id) == null, "Failed job kept retrying after its bounded limit.");
        });
        yield return ("2B scheduler applies scanner policy in Development Production Staging and quarantine", async () =>
        {
            await using var db = Db(); var actor = await User(db); var clock = new TestClock(DateTimeOffset.UtcNow);
            var development = new MediaUploadOptions { IsDevelopmentLoopback = true, AllowUnscannedDevelopmentUploads = true };
            using var bitmap = new SkiaSharp.SKBitmap(5, 7);
            for (var y = 0; y < 7; y++) for (var x = 0; x < 5; x++) bitmap.SetPixel(x, y, new SkiaSharp.SKColor((byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256)));
            using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100); using var stream = new MemoryStream(encoded.ToArray());
            var upload = await new MediaService(db, storage, development).UploadAsync(stream, "qa-scheduler.png", "image/png", actor.UserId, true);
            var input = Phase2aSqlTests.Input("QA scheduled media policy " + Guid.NewGuid().ToString("N")); input.CoverAssetId = upload.Asset.Id;
            var content = await W(db).SaveAsync(null, input, actor, null, Guid.NewGuid());
            foreach (var environment in new[] { "Production", "Staging", "Development" })
            {
                var policyConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Public:BaseUrl"] = "http://127.0.0.1:5189", ["Media:AllowUnscannedDevelopmentUploads"] = "true" }).Build();
                var policy = MediaUploadOptions.FromConfiguration(policyConfig, environment); var service = S(db, clock, policy);
                var job = await service.ScheduleAsync(content.ContentId, content.CurrentRevisionId!.Value, clock.Now.AddSeconds(1), true, actor, content.RowVersion);
                clock.Now = clock.Now.AddSeconds(1); var lease = await service.ClaimAsync("qa-scan", onlyJobId:job.Id);
                Assert((await service.ExecuteAsync(lease!)).State == (environment == "Development" ? "Completed" : "NeedsReview"), "Scheduler bypassed environment scanner policy.");
            }
            db.ChangeTracker.Clear(); var asset = await db.MediaAssets.SingleAsync(x => x.Id == upload.Asset.Id);
            Assert(asset.ScanStatus == "NotScanned", "Scheduler or test fabricated Clean scanning status.");
            var published = await db.Contents.SingleAsync(x => x.Id == content.ContentId);
            var edit = await W(db).BeginEditAsync(content.ContentId, actor, published.RowVersion, Guid.NewGuid());
            asset.IsQuarantined = true; await db.SaveChangesAsync();
            var quarantinedService = S(db, clock, development);
            var quarantined = await quarantinedService.ScheduleAsync(content.ContentId, edit.CurrentRevisionId!.Value, clock.Now.AddSeconds(1), true, actor, edit.RowVersion);
            clock.Now = clock.Now.AddSeconds(1); var quarantineLease = await quarantinedService.ClaimAsync("qa-quarantine", onlyJobId:quarantined.Id);
            Assert((await quarantinedService.ExecuteAsync(quarantineLease!)).State == "NeedsReview", "Development exception published quarantined media.");
        });
        yield return ("2B standalone worker publishes with web stopped and rejects Staging media bypass", async () =>
        {
            await using var db = Db(); var (content, actor) = await Draft(db);
            var clock = new TestClock(DateTimeOffset.UtcNow);
            var job = await S(db, clock).ScheduleAsync(content.ContentId, content.CurrentRevisionId!.Value, clock.Now.AddSeconds(2), true, actor, content.RowVersion);
            using (var probe = new System.Net.Sockets.TcpClient())
            {
                var listening = false;
                try { await probe.ConnectAsync("127.0.0.1", 5189); listening = probe.Connected; } catch (System.Net.Sockets.SocketException) { }
                Assert(!listening, "Standalone worker test requires the Test web server to be stopped.");
            }
            await Task.Delay(TimeSpan.FromSeconds(2.2));
            var workspace = config["WorkspaceRoot"]!;
            var dll = TestRuntimePaths.Assembly(workspace,"Worker");
            // Assembly name follows Directory.Build.props; find the worker runtime entry without guessing.
            if (!File.Exists(dll)) dll = Directory.GetFiles(Path.GetDirectoryName(dll)!, "*.runtimeconfig.json").Select(x => x.Replace(".runtimeconfig.json", ".dll", StringComparison.Ordinal)).Single();
            var dotnet = Path.Combine(workspace, ".local", "dotnet", "dotnet.exe");
            async Task<(int Exit, string Output)> Start(string environment)
            {
                var info = new ProcessStartInfo(dotnet) { WorkingDirectory = workspace, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                info.ArgumentList.Add(dll); info.ArgumentList.Add("--once"); info.ArgumentList.Add("--job"); info.ArgumentList.Add(job.Id.ToString());
                info.Environment["DOTNET_ENVIRONMENT"] = environment;
                info.Environment["ConnectionStrings__Portal"] = DatabaseSettings.Connection(config);
                info.Environment["Sql__ExpectedDatabase"] = "LandPortal_Test";
                info.Environment["Storage__MediaRoot"] = storage.MediaRoot; info.Environment["Storage__SnapshotRoot"] = storage.SnapshotRoot;
                info.Environment["Storage__TempRoot"] = storage.TempRoot; info.Environment["Storage__KeysRoot"] = storage.KeysRoot;
                info.Environment["Public__BaseUrl"] = "http://127.0.0.1:5189";
                info.Environment["Media__AllowUnscannedDevelopmentUploads"] = "true";
                using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not launch owned worker.");
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var started=DateTimeOffset.UtcNow;string? failure=null;
                try { await process.WaitForExitAsync(timeout.Token); }
                catch(Exception exception) { failure=exception.GetType().Name;throw; }
                finally
                {
                    if(!process.HasExited)process.Kill(entireProcessTree:true);
                    await process.WaitForExitAsync();var folder=TestEvidencePaths.At(workspace,"phase2b","standalone-worker");Directory.CreateDirectory(folder);
                    await File.WriteAllTextAsync(Path.Combine(folder,environment+".log"),await output+"\n"+await error);
                    await File.WriteAllTextAsync(Path.Combine(folder,environment+".json"),System.Text.Json.JsonSerializer.Serialize(new{started,finished=DateTimeOffset.UtcNow,environment,ownedPid=process.Id,exit=process.ExitCode,failure,deadlineSeconds=30,job.Id,ownedProcessExited=process.HasExited}));
                }
                return (process.ExitCode, await output + await error);
            }
            var worker = await Start("Development");
            Assert(worker.Exit == 0, "Standalone Development worker failed; output retained privately only.");
            db.ChangeTracker.Clear();
            Assert(await db.Contents.AnyAsync(x => x.Id == content.ContentId && x.IsPublic && x.PublishedRevisionId == content.CurrentRevisionId), "Standalone worker failed to publish the exact SQL revision while web was stopped.");
            Assert(await db.PublicationJobs.AnyAsync(x => x.Id == job.Id && x.State == PublicationJobState.Completed), "Worker effect and job completion were inconsistent.");
            var staging = await Start("Staging");
            Assert(staging.Exit != 0 && staging.Output.Contains("forbidden outside Development loopback", StringComparison.Ordinal), "Staging worker accepted Development media bypass.");
            var evidence = TestEvidencePaths.At(workspace,"phase2b"); Directory.CreateDirectory(evidence);
            var completedJob = await db.PublicationJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
            await File.WriteAllTextAsync(Path.Combine(evidence, "worker-web-off.json"), System.Text.Json.JsonSerializer.Serialize(new { database = "LandPortal_Test", utc = DateTimeOffset.UtcNow, jobId = job.Id, revisionId = content.CurrentRevisionId, dueUtc = completedJob.DueUtc, actualUtc = completedJob.CompletedUtc, latencyMilliseconds = (completedJob.CompletedUtc!.Value - completedJob.DueUtc).TotalMilliseconds, pollSeconds = 5, executionMode = "independent worker --once after due", developmentExit = worker.Exit, stagingExit = staging.Exit, web5189Stopped = true, exactRevisionPublished = true, workerProcessesExited = true }));
        });
    }
}
