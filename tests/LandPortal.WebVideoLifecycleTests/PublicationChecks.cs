using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

static class PublicationChecks
{
    public static async Task RunAsync(string instance, string runPath)
    {
        var root = Directory.GetCurrentDirectory(); var run = Path.GetFullPath(runPath);
        if (!Regex.IsMatch(instance, "^LPWebPublication_[a-f0-9]{12}$") ||
            !run.StartsWith(Path.Combine(root, ".local", "tests", "web-publication-"), StringComparison.OrdinalIgnoreCase) ||
            File.Exists(Path.Combine(run, "result.json"))) throw new InvalidOperationException("New isolated publication run required");
        var cs = new SqlConnectionStringBuilder { DataSource = @"(localdb)\" + instance, InitialCatalog = "LandPortal_Test", IntegratedSecurity = true, Encrypt = true, TrustServerCertificate = true, Pooling = false };
        var options = new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString, o => o.UseCompatibilityLevel(160)).Options;
        var clock = new FixtureClock(DateTimeOffset.UtcNow);
        var storage = new StorageOptions { MediaRoot = Path.Combine(run, "media"), SnapshotRoot = Path.Combine(run, "snapshots"), TempRoot = Path.Combine(run, "temp"), KeysRoot = Path.Combine(run, "keys") };
        foreach (var p in new[] { storage.MediaRoot, storage.SnapshotRoot, storage.TempRoot, storage.KeysRoot }) Directory.CreateDirectory(p);
        PortalDbContext Db() => new(options);
        WorkflowService Workflow(PortalDbContext db) => new(db, new HtmlPolicy(), new MediaUploadOptions(), storage, clock);
        SchedulerService Scheduler(PortalDbContext db) => new(db, Workflow(db), new CurrentPermissionService(db), new SchedulerOptions(), clock);
        var checks = new List<string>(); var stage = "new-isolated-sql"; WebApplication? web = null;
        void Check(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); checks.Add(description); Console.WriteLine("PASS " + description); }
        DraftInput Input(string title) => new() { Kind = ContentKind.StaticPage, Title = title, Summary = "Synthetic isolated schedule", BodyHtml = "<p>Private test fixture, no real source content.</p>", Author = "Fixture", PrimaryCategoryId = 1 };
        async Task<ContentActor> Actor(PortalDbContext db, bool reviewer)
        {
            var id = "fixture-" + Guid.NewGuid().ToString("N");
            db.Users.Add(new PortalUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString("N") });
            var rights = reviewer ? new[] { Permissions.Review } : new[] { Permissions.Create, Permissions.EditOwn, Permissions.ViewOwn, Permissions.Submit, Permissions.PublishApproved, Permissions.Schedule };
            foreach (var permission in rights) db.UserClaims.Add(new IdentityUserClaim<string> { UserId = id, ClaimType = "permission", ClaimValue = permission });
            await db.SaveChangesAsync(); return await new CurrentPermissionService(db).GetAsync(id);
        }
        async Task<Fixture> Approved(string suffix, bool future = false)
        {
            await using var db = Db(); var author = await Actor(db, false); var reviewer = await Actor(db, true);
            var draft = await Workflow(db).SaveAsync(null, Input("Fixture " + suffix), author, null, Guid.NewGuid());
            var submitted = await Workflow(db).SubmitAsync(draft.ContentId, draft.CurrentRevisionId, author, draft.RowVersion, Guid.NewGuid());
            var approved = await Workflow(db).ApproveAsync(submitted.ContentId, submitted.CurrentRevisionId!.Value, reviewer, submitted.RowVersion, Guid.NewGuid());
            var job = await Scheduler(db).ScheduleAsync(approved.ContentId, approved.CurrentRevisionId!.Value, clock.Now.AddMinutes(future ? 120 : 1), false, author, approved.RowVersion);
            return new(approved, author, reviewer, job);
        }
        async Task<bool> Public(Guid id) { await using var db = Db(); return await db.Contents.AnyAsync(x => x.Id == id && x.IsPublic); }
        try
        {
            await using (var sql = new SqlConnection(new SqlConnectionStringBuilder(cs.ConnectionString) { InitialCatalog = "master" }.ConnectionString))
            {
                await sql.OpenAsync(); using var q = sql.CreateCommand();
                q.CommandText = "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),CONVERT(int,SERVERPROPERTY('IsLocalDB')),DB_ID('LandPortal_Test')";
                using (var r = await q.ExecuteReaderAsync()) { await r.ReadAsync(); Check(r.GetString(0).Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) && r.GetInt32(1) == 1 && r.IsDBNull(2), "Publication fixture uses a new local-only SQL instance"); }
                var mdf = Path.Combine(run, "LandPortal_Test.mdf"); var ldf = Path.Combine(run, "LandPortal_Test_log.ldf");
                if (File.Exists(mdf) || File.Exists(ldf)) throw new Exception("Fixture SQL files already exist");
                q.CommandText = "CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'" + mdf.Replace("'", "''") + "') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'" + ldf.Replace("'", "''") + "')";
                await q.ExecuteNonQueryAsync(); q.CommandText = "ALTER DATABASE [LandPortal_Test] SET COMPATIBILITY_LEVEL=160"; await q.ExecuteNonQueryAsync();
            }
            await using (var db = Db())
            {
                await db.Database.MigrateAsync(); var history = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
                Check(history.Length == 18 && history.Last() == "20261110050000_HomeEventBanner", "Publication test SQL has official schema18");
                db.Categories.Add(new() { Id = 1, Name = "Fixture category", Slug = "fixture-category" }); await db.SaveChangesAsync();
            }
            stage = "approved-durable-schedules";
            var valid = await Approved("late approved"); var future = await Approved("future", true);
            var revoked = await Approved("revoked reviewer"); var revised = await Approved("revised draft"); var cancelled = await Approved("cancelled");
            await using (var db = Db())
            {
                var rights = await db.UserClaims.Where(x => x.UserId == revoked.Reviewer.UserId && x.ClaimValue == Permissions.Review).ToListAsync(); db.UserClaims.RemoveRange(rights); await db.SaveChangesAsync();
                var opened = await Workflow(db).BeginEditAsync(revised.Value.ContentId, revised.Author, revised.Value.RowVersion, Guid.NewGuid());
                var edited = await Workflow(db).SaveAsync(opened.ContentId, Input("UNAPPROVED newer revision"), revised.Author, opened.RowVersion, Guid.NewGuid());
                Check(edited.CurrentRevisionId != revised.Value.CurrentRevisionId, "Real CMS workflow creates a newer unapproved revision");
                await Scheduler(db).CancelAsync(cancelled.Job.Id, cancelled.Author, cancelled.Job.RowVersion);
            }
            clock.Now = clock.Now.AddMinutes(31);
            Check(!await Public(valid.Value.ContentId), "Overdue approved article is still private before the first relevant request");
            stage = "http-request-catch-up";
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = run, EnvironmentName = "Development", Args = [] });
            builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Mail:Transport"] = "Disabled" });
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddScoped(_ => Db());
            builder.Services.AddScoped(s => Workflow(s.GetRequiredService<PortalDbContext>()));
            builder.Services.AddScoped<CurrentPermissionService>();
            builder.Services.AddScoped(s => new SchedulerService(s.GetRequiredService<PortalDbContext>(), s.GetRequiredService<WorkflowService>(), s.GetRequiredService<CurrentPermissionService>(), new SchedulerOptions(), clock));
            builder.Services.AddSingleton<WebPublicationScheduler>();
            web = builder.Build(); web.UseRouting(); web.UseMiddleware<DuePublicationMiddleware>();
            // The production middleware runs before the endpoint's first published SQL read.
            // Existing CMS/view rendering is deliberately not repeated in this lifecycle delta.
            web.MapGet("/fixture/article/{id:guid}", async (Guid id, PortalDbContext db) =>
            {
                var title = await (from c in db.Contents.AsNoTracking() join r in db.Revisions.AsNoTracking() on c.PublishedRevisionId equals r.Id where c.Id == id && c.IsPublic select r.Title).SingleOrDefaultAsync();
                return title == null ? Results.NotFound() : Results.Text(title);
            }).WithMetadata(new ControllerActionDescriptor { ControllerName = "Portal", ActionName = "Detail" });
            web.MapGet("/health", () => Results.Text("isolated healthy"));
            await web.StartAsync();
            using var http = new HttpClient { BaseAddress = new(web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            using (var health = await http.GetAsync("/health")) Check(health.StatusCode == HttpStatusCode.OK && !await Public(valid.Value.ContentId), "Health request does not trigger publication work");
            using (var post = await http.PostAsync("/fixture/article/" + valid.Value.ContentId, new StringContent(""))) Check(post.StatusCode == HttpStatusCode.MethodNotAllowed && !await Public(valid.Value.ContentId), "Non-GET request does not trigger catch-up");
            using (var first = await http.GetAsync("/fixture/article/" + valid.Value.ContentId)) Check(first.StatusCode == HttpStatusCode.OK && await first.Content.ReadAsStringAsync() == "Fixture late approved", "First relevant HTTP request publishes overdue exact approved revision before reading it");
            await using (var db = Db())
            {
                var current = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == valid.Value.ContentId);
                Check(current.PublishedRevisionId == valid.Value.CurrentRevisionId && await db.PublicationJobs.AnyAsync(x => x.Id == valid.Job.Id && x.State == PublicationJobState.Completed && !x.IsActive), "Catch-up binds exact approved revision and commits durable completion");
                Check(!await Public(future.Value.ContentId) && await db.PublicationJobs.AnyAsync(x => x.Id == future.Job.Id && x.State == PublicationJobState.Pending && x.AttemptCount == 0), "Future schedule remains private and unclaimed");
                Check(!await Public(revoked.Value.ContentId) && await db.PublicationJobs.AnyAsync(x => x.Id == revoked.Job.Id && x.State == PublicationJobState.NeedsReview), "Reviewer permission revocation blocks overdue publication");
                Check(!await Public(revised.Value.ContentId) && await db.PublicationJobs.AnyAsync(x => x.Id == revised.Job.Id && !x.IsActive && x.State == PublicationJobState.NeedsReview), "Newer unapproved revision cannot replace scheduled revision or revive invalidated job");
                Check(!await Public(cancelled.Value.ContentId) && await db.PublicationJobs.AnyAsync(x => x.Id == cancelled.Job.Id && !x.IsActive && x.State == PublicationJobState.Canceled && x.AttemptCount == 0), "Cancelled terminal job is never revived by Web catch-up");
            }
            stage = "concurrent-request-and-poll";
            var race = await Approved("concurrent"); clock.Now = clock.Now.AddMinutes(31);
            var coordinator = web.Services.GetRequiredService<WebPublicationScheduler>();
            var responseOne = http.GetAsync("/fixture/article/" + race.Value.ContentId);
            var responseTwo = http.GetAsync("/fixture/article/" + race.Value.ContentId);
            var background = coordinator.DrainDueAsync();
            await Task.WhenAll(responseOne, responseTwo, background);
            using (var one = await responseOne) using (var two = await responseTwo) Check(one.StatusCode == HttpStatusCode.OK && two.StatusCode == HttpStatusCode.OK, "Concurrent public requests and background coordinator complete successfully");
            await using (var db = Db())
                Check(await db.ContentCommands.CountAsync(x => x.Id == race.Job.PublishCommandId) == 1 && await db.PublicationJobAttempts.CountAsync(x => x.JobId == race.Job.Id) == 1 && await db.AuditLogs.CountAsync(x => x.ContentId == race.Value.ContentId && x.Action == "PublishApproved") == 1, "Concurrent catch-up commits one publication command, attempt and business audit");
            stage = "standalone-policy-preservation";
            var legacy = await Approved("legacy late cutoff"); clock.Now = clock.Now.AddMinutes(20);
            await using (var db = Db())
            {
                var service = Scheduler(db); var lease = await service.ClaimAsync("isolated-legacy", onlyJobId: legacy.Job.Id);
                Check(lease != null && (await service.ExecuteAsync(lease)).State == "NeedsReview" && !await Public(legacy.Value.ContentId), "Standalone scheduler retains lateness cutoff without explicit Web catch-up mode");
                Check(await db.SourceRecords.CountAsync() == 0 && await db.MediaAssets.CountAsync() == 0 && (await db.Database.GetAppliedMigrationsAsync()).Count() == 18, "No source/media imports or extra schema changes during scheduler tests");
            }
            stage = "COMPLETE";
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1; Console.WriteLine("BLOCKED " + stage + " " + error.GetType().Name);
            await File.WriteAllTextAsync(Path.Combine(run, "failure.txt"), error.ToString());
        }
        finally
        {
            if (web != null) { await web.StopAsync(TimeSpan.FromSeconds(10)); await web.DisposeAsync(); }
            await File.WriteAllTextAsync(Path.Combine(run, "result.json"), JsonSerializer.Serialize(new { stage, passed = checks.Count, checks, operationalSqlTouched = false, sourceAccountsTouched = false, mail = "No sender registered", realHttp = "Kestrel with production request catch-up middleware and isolated public SQL read", fullRegression = false }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"RESULT {stage} checks={checks.Count} evidence={run}");
        }
    }
    sealed record Fixture(WorkflowResult Value, ContentActor Author, ContentActor Reviewer, PublicationJob Job);
    sealed class FixtureClock(DateTimeOffset initial) : TimeProvider { public DateTimeOffset Now = initial; public override DateTimeOffset GetUtcNow() => Now; }
}
