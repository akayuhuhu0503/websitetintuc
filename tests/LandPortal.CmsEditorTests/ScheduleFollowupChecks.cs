using System.Net;
using System.Text.Json;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

static class ScheduleFollowupChecks
{
    public static async Task RunAsync(DbContextOptions<PortalDbContext> options, StorageOptions storage,
        HttpClient dual, HttpClient writer, string dualUserId, string reviewerUserId,
        Func<HttpClient, Task<string>> token, Action<bool, string> check)
    {
        string writerId;
        await using (var db = new PortalDbContext(options))
        {
            if (!db.Database.GetDbConnection().DataSource.StartsWith("(localdb)\\LPCms_", StringComparison.OrdinalIgnoreCase) || db.Database.GetDbConnection().Database != "LandPortal_Test")
                throw new InvalidOperationException("Schedule checks only use the fresh isolated SQL Server database.");
            writerId = (await db.Users.SingleAsync(x => x.UserName == "fixture-writer")).Id;
            if (!await db.UserClaims.AnyAsync(x => x.UserId == dualUserId && x.ClaimType == "permission" && x.ClaimValue == Permissions.PublishDirect))
                db.UserClaims.Add(new() { UserId = dualUserId, ClaimType = "permission", ClaimValue = Permissions.PublishDirect });
            await db.SaveChangesAsync();
            var actor = await new CurrentPermissionService(db).GetAsync(dualUserId);
            check(actor.Has(Permissions.Review) && actor.Has(Permissions.PublishApproved) && actor.Has(Permissions.PublishDirect), "Schedule fixture has Review, PublishApproved and PublishDirect simultaneously");
        }
        WorkflowService Workflow(PortalDbContext db, TimeProvider? clock = null) => new(db, new HtmlPolicy(), storage: storage, clock: clock);
        async Task<WorkflowResult> Draft(bool pending)
        {
            await using var db = new PortalDbContext(options); var actor = await new CurrentPermissionService(db).GetAsync(writerId);
            var result = await Workflow(db).SaveAsync(null, new DraftInput { Kind = ContentKind.Announcement, Title = "ISOLATED SCHEDULE FOLLOWUP " + Guid.NewGuid().ToString("N"), BodyHtml = "<p>Synthetic schedule test only.</p>", PrimaryCategoryId = 1 }, actor, null, Guid.NewGuid());
            return pending ? await Workflow(db).SubmitAsync(result.ContentId, result.CurrentRevisionId, actor, result.RowVersion, Guid.NewGuid()) : result;
        }
        async Task<WorkflowResult> Approve(WorkflowResult value, string reviewer)
        {
            await using var db = new PortalDbContext(options); var actor = await new CurrentPermissionService(db).GetAsync(reviewer);
            return await Workflow(db).ApproveAsync(value.ContentId, value.CurrentRevisionId!.Value, actor, value.RowVersion, Guid.NewGuid());
        }
        async Task<(HttpStatusCode Status, Guid Job)> Schedule(WorkflowResult value, string? mode, bool direct, DateTime due, HttpClient? client = null, Guid? revision = null, byte[]? version = null)
        {
            client ??= dual;
            var fields = new Dictionary<string, string> { ["ContentId"] = value.ContentId.ToString(), ["RevisionId"] = (revision ?? value.CurrentRevisionId!.Value).ToString(), ["ContentVersion"] = Convert.ToBase64String(version ?? value.RowVersion), ["DueVietnam"] = due.ToString("yyyy-MM-ddTHH:mm:ss"), ["Direct"] = direct ? "true" : "false", ["__RequestVerificationToken"] = await token(client) };
            if (mode != null) fields["EditorMode"] = mode;
            using var response = await client.PostAsync("/admin/editor-schedule/" + value.ContentId, new FormUrlEncodedContent(fields));
            Guid job = Guid.Empty;
            if (response.StatusCode == HttpStatusCode.OK) job = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("jobId").GetGuid();
            return (response.StatusCode, job);
        }
        async Task<PublicationJob> Job(Guid id) { await using var db = new PortalDbContext(options); return await db.PublicationJobs.AsNoTracking().SingleAsync(x => x.Id == id); }
        async Task SetReview(string user, bool allowed)
        {
            await using var db = new PortalDbContext(options);
            var claims = await db.UserClaims.Where(x => x.UserId == user && x.ClaimType == "permission" && x.ClaimValue == Permissions.Review).ToListAsync();
            if (!allowed) db.UserClaims.RemoveRange(claims);
            else if (claims.Count == 0) db.UserClaims.Add(new IdentityUserClaim<string> { UserId = user, ClaimType = "permission", ClaimValue = Permissions.Review });
            await db.SaveChangesAsync();
        }
        async Task<SchedulerOutcome> Execute(Guid id)
        {
            var job = await Job(id); var clock = new FixedClock(job.DueUtc.AddSeconds(1));
            await using var db = new PortalDbContext(options);
            var scheduler = new SchedulerService(db, Workflow(db, clock), new CurrentPermissionService(db), clock: clock);
            var lease = await scheduler.ClaimAsync("cms-schedule-followup", onlyJobId: id) ?? throw new InvalidOperationException("Fixture schedule not claimable");
            return await scheduler.ExecuteAsync(lease);
        }
        var due = SchedulerService.ToVietnam(DateTimeOffset.UtcNow.AddMinutes(10));
        var pending = await Draft(true);
        using (var response = await dual.GetAsync("/admin/edit/" + pending.ContentId))
        {
            var html = await response.Content.ReadAsStringAsync();
            var dom = new HtmlParser().ParseDocument(html);
            check(response.StatusCode == HttpStatusCode.OK && dom.QuerySelector("[data-editor-schedule='approved']")?.TextContent.Contains("Duyệt và hẹn giờ") == true && dom.QuerySelector("[data-editor-schedule='direct']") != null && dom.QuerySelector("[data-direct]") == null, "Dual-right editor renders distinct approved and explicitly direct schedule actions");
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(storage.TempRoot)!, "schedule-pending.html"), html);
        }
        var approved = await Approve(pending, dualUserId);
        using (var response = await dual.GetAsync("/admin/edit/" + approved.ContentId))
        {
            var html = await response.Content.ReadAsStringAsync();
            var dom = new HtmlParser().ParseDocument(html);
            check(response.StatusCode == HttpStatusCode.OK && dom.QuerySelector("[data-editor-schedule='approved']")?.TextContent.Contains("Hẹn giờ bản đã duyệt") == true && dom.QuerySelector("[data-editor-schedule='direct']") != null, "Already-approved dual-right editor keeps approved scheduling as its own action");
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(storage.TempRoot)!, "schedule-approved.html"), html);
        }
        var created = await Schedule(approved, "approved", false, due);
        check(created.Status == HttpStatusCode.OK, "HTTP approved schedule accepted for actor with direct permission");
        var jobRow = await Job(created.Job);
        check(!jobRow.Direct && jobRow.ReviewerUserId == dualUserId && jobRow.ApprovalHash?.Length > 0 && jobRow.RevisionId == approved.CurrentRevisionId, "Approved schedule persists exact reviewer, revision and approval hash without direct mode");
        var repeat = await Schedule(approved, "approved", false, due);
        check(repeat.Status == HttpStatusCode.OK && repeat.Job == created.Job, "Exact approved schedule retry acknowledges one job under permission checks");
        check((await Schedule(approved, null, true, due)).Status == HttpStatusCode.BadRequest, "Direct permission alone cannot select a schedule mode");
        check((await Schedule(approved, "approved", true, due)).Status == HttpStatusCode.BadRequest, "Contradictory approved/direct request rejected");
        check((await Schedule(approved, "approved", false, due, writer)).Status == HttpStatusCode.Forbidden, "Writer without schedule/publish rights cannot schedule");
        check((await Schedule(approved, "approved", false, due, revision: Guid.NewGuid())).Status != HttpStatusCode.OK, "Schedule rejects a revision other than the approved revision");
        var stale = approved.RowVersion.ToArray(); stale[^1] ^= 0x01;
        check((await Schedule(approved, "approved", false, due, version: stale)).Status == HttpStatusCode.Conflict, "Schedule retry rejects stale content rowversion");
        await SetReview(dualUserId, false);
        try
        {
            check((await Schedule(approved, "approved", false, due)).Status == HttpStatusCode.BadRequest, "Retry rechecks original reviewer even while PublishDirect remains granted");
            var outcome = await Execute(created.Job);
            await using var db = new PortalDbContext(options); var content = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == approved.ContentId);
            check(outcome.State == "NeedsReview" && !content.IsPublic && content.PublishedRevisionId == null, "Execution blocks approved schedule when original reviewer loses Review despite direct permission");
        }
        finally { await SetReview(dualUserId, true); }
        var approved2 = await Approve(await Draft(true), reviewerUserId);
        var second = await Schedule(approved2, "approved", false, due);
        check(second.Status == HttpStatusCode.OK, "Approved schedule with separate reviewer accepted");
        await SetReview(reviewerUserId, false);
        try { check((await Execute(second.Job)).State == "NeedsReview", "Execution rechecks the separate original reviewer's current rights"); }
        finally { await SetReview(reviewerUserId, true); }
        var approved3 = await Approve(await Draft(true), dualUserId);
        var third = await Schedule(approved3, "approved", false, due);
        check(third.Status == HttpStatusCode.OK && (await Execute(third.Job)).State == "Completed", "Valid approved schedule completes through normal publication workflow");
        await using (var db = new PortalDbContext(options))
        {
            var content = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == approved3.ContentId);
            check(content.IsPublic && content.PublishedRevisionId == approved3.CurrentRevisionId && await db.ContentCommands.CountAsync(x => x.ContentId == content.Id && x.Action == "PublishApproved") == 1 && !await db.ContentCommands.AnyAsync(x => x.ContentId == content.Id && x.Action == "PublishDirect"), "Approved schedule publishes exact approved revision once with approved audit path");
        }
        var directDraft = await Draft(false);
        check((await Schedule(directDraft, "approved", false, due)).Status == HttpStatusCode.BadRequest, "Approved mode does not use direct privilege to schedule an unapproved draft");
        var directJob = await Schedule(directDraft, "direct", true, due);
        check(directJob.Status == HttpStatusCode.OK && (await Job(directJob.Job)).Direct && (await Job(directJob.Job)).ReviewerUserId == null, "Only explicit direct choice creates a direct schedule");
        check((await Execute(directJob.Job)).State == "Completed", "Explicit direct schedule retains its authorized publication path");
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
