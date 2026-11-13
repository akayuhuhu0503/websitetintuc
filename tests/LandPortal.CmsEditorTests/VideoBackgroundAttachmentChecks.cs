using System.Security.Cryptography;
using System.Text;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Narrow SQL integration checks for durable attachment only. Synthetic output
// bytes exercise integrity/policy checks; this is not a native FFmpeg rerun.
static class VideoBackgroundAttachmentChecks
{
    public static async Task RunAsync(DbContextOptions<PortalDbContext> options, StorageOptions storage, Action<bool,string> check)
    {
        await using (var guard = new PortalDbContext(options))
            if (!guard.Database.GetDbConnection().DataSource.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase) ||
                guard.Database.GetDbConnection().Database != "LandPortal_Test")
                throw new InvalidOperationException("Background attachment checks require isolated LocalDB test data.");

        var policy = new VideoOptions { Enabled = true, ApprovedManagedVideoWithoutAv = true, MaxFileBytes = 1024 * 1024, MinimumFreeBytes = 1024, MaxSessions = 5 };
        var user = "cms-video-background-" + Guid.NewGuid().ToString("N");
        var permissions = new HashSet<string> { Permissions.Create, Permissions.EditOwn, Permissions.ViewOwn, Permissions.Submit, Permissions.Review, Permissions.ManageVideos, Permissions.ManageMedia };
        var actor = new ContentActor(user, permissions);
        const int category = 90811;
        await using (var db = new PortalDbContext(options))
        {
            db.Users.Add(new PortalUser { Id = user, UserName = user, NormalizedUserName = user.ToUpperInvariant() });
            foreach (var permission in permissions) db.UserClaims.Add(new IdentityUserClaim<string> { UserId = user, ClaimType = "permission", ClaimValue = permission });
            db.Categories.Add(new Category { Id = category, Name = "Isolated background video", Slug = "background-video-" + Guid.NewGuid().ToString("N") });
            await db.SaveChangesAsync();
        }
        DraftInput Input(string title, List<AssetSelection>? assets = null) => new()
        {
            Kind = ContentKind.Video, Title = title, Summary = "Preserve the latest saved summary", BodyHtml = "<p>Preserve saved editor text.</p>",
            Author = "Synthetic author", SourceCredit = "Synthetic source", PrimaryCategoryId = category,
            Metadata = new() { Unit = "Synthetic unit", PublicationDate = new DateOnly(2026, 11, 13) }, Assets = assets ?? []
        };
        WorkflowService Workflow(PortalDbContext db) => new(db, new HtmlPolicy(), storage: storage, videoOptions: policy);
        VideoDraftAttachmentService Service(PortalDbContext db) => new(db, storage, policy, new CurrentPermissionService(db), Workflow(db), true);
        async Task<WorkflowResult> NewContent(string title)
        {
            await using var db = new PortalDbContext(options);
            return await Workflow(db).SaveAsync(null, Input(title), actor, null, Guid.NewGuid());
        }
        async Task<WorkflowResult> Save(Guid contentId, string title, byte[] version, List<AssetSelection>? assets = null, bool autosave = false)
        {
            await using var db = new PortalDbContext(options);
            return await Workflow(db).SaveAsync(contentId, Input(title, assets), actor, version, Guid.NewGuid(), autosave);
        }
        async Task<(Guid Session, Guid Asset, string Output, byte[] OutputBytes)> Fixture(Guid contentId, bool autoAttach, VideoState state)
        {
            await using var db = new PortalDbContext(options);
            var uploads = new VideoUploadService(db, storage, policy, new CurrentPermissionService(db));
            var session = await uploads.CreateAsync(contentId, user, "Bản tin.test.mp4", "video/mp4", 64, new string('a', 64), autoAttach: autoAttach);
            var prefix = "cms-background-fixture/" + session.Id.ToString("N") + "/";
            var original = RandomNumberGenerator.GetBytes(64);
            var output = Encoding.UTF8.GetBytes("ISOLATED OUTPUT " + session.Id);
            var poster = Encoding.UTF8.GetBytes("ISOLATED POSTER " + session.Id);
            async Task Write(string name, byte[] bytes)
            {
                var path = MediaService.ResolveMediaPath(storage, prefix + name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes);
            }
            await Write("original.mp4", original); await Write("output.mp4", output); await Write("poster.jpg", poster);
            string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
            var asset = new MediaAsset
            {
                Origin = "VideoUpload", UploadedByUserId = user, OriginalName = "video.mp4", MimeType = "video/mp4", StorageKey = prefix + "original.mp4",
                ByteLength = original.Length, Sha256 = Hash(original), ScanStatus = "NotScanned", ValidationStatus = VideoOptions.NoAvValidated, IsQuarantined = true
            };
            db.MediaAssets.Add(asset);
            db.Add(new VideoFile
            {
                MediaAssetId = asset.Id, Original = asset, UploadSessionId = session.Id, State = state, ReadyUtc = DateTimeOffset.UtcNow,
                VideoCodec = "h264", AudioCodec = "aac", Mp4Key = prefix + "output.mp4", Mp4Sha256 = Hash(output), Mp4Bytes = output.Length,
                PosterKey = prefix + "poster.jpg", PosterSha256 = Hash(poster), PosterBytes = poster.Length, Width = 320, Height = 180, DurationSeconds = 1
            });
            session.State = VideoUploadState.Completed; session.MediaAssetId = asset.Id; session.ReceivedBytes = session.ExpectedBytes; session.Sha256 = asset.Sha256;
            db.Entry(session).State = EntityState.Modified;
            await db.SaveChangesAsync();
            return (session.Id, asset.Id, MediaService.ResolveMediaPath(storage, prefix + "output.mp4"), output);
        }
        async Task Drain()
        {
            await using var db = new PortalDbContext(options);
            await Service(db).ProcessReadyAsync();
        }
        async Task Retry(Guid session)
        {
            await using var db = new PortalDbContext(options);
            await Service(db).AttachAsync(session, user);
        }
        async Task<bool> Attached(Guid content, Guid asset)
        {
            await using var db = new PortalDbContext(options);
            return await db.DraftAssets.AnyAsync(x => x.ContentId == content && x.MediaAssetId == asset && x.Usage == "Video");
        }
        async Task<int> Revisions(Guid content)
        {
            await using var db = new PortalDbContext(options);
            return await db.Revisions.CountAsync(x => x.ContentId == content);
        }
        async Task<byte[]> Version(Guid content)
        {
            await using var db = new PortalDbContext(options);
            return await db.Contents.Where(x => x.Id == content).Select(x => x.RowVersion).SingleAsync();
        }
        async Task<bool> HasReceipt(Guid session)
        {
            await using var db = new PortalDbContext(options);
            return await db.ContentCommands.AnyAsync(x => x.Id == session);
        }
        async Task SetReady(Guid asset)
        {
            await using var db = new PortalDbContext(options);
            var file = await db.Set<VideoFile>().SingleAsync(x => x.MediaAssetId == asset);
            file.State = VideoState.Ready;
            await db.SaveChangesAsync();
        }

        var first = await NewContent("Closed tab background video");
        var pending = await Fixture(first.ContentId, true, VideoState.Pending);
        await Drain();
        check(!await Attached(first.ContentId, pending.Asset) && !await HasReceipt(pending.Session), "Background attachment leaves Pending video and durable request untouched");
        var edited = await Save(first.ContentId, "Latest text saved while video processes", first.RowVersion);
        await SetReady(pending.Asset);
        var revisionCount = await Revisions(first.ContentId);
        // No HTTP controller, cookie, browser or authenticated session is involved.
        // A new context models a host restart after Ready was already committed.
        await using (var db = new PortalDbContext(options))
        {
            var worker = new VideoWorker(db, storage, policy, new UnavailableScanner(), new VideoProcessor(policy, storage), new CurrentPermissionService(db), development: true);
            await worker.AttachReadyAsync(pending.Asset);
        }
        check(await Attached(first.ContentId, pending.Asset), "Ready video autoattaches after host restart with the editor closed and no HTTP session");
        check(string.IsNullOrEmpty(policy.FfmpegPath) && string.IsNullOrEmpty(policy.FfprobePath), "Worker recovers previously verified Ready output without invoking unavailable scanner or native tools");
        await using (var db = new PortalDbContext(options))
        {
            var draft = await db.ContentDrafts.AsNoTracking().SingleAsync(x => x.ContentId == first.ContentId);
            var content = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == first.ContentId);
            var asset = await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == pending.Asset);
            check(draft.Title == "Latest text saved while video processes" && draft.Summary == "Preserve the latest saved summary" && draft.BodyHtml.Contains("Preserve saved editor text") && draft.Author == "Synthetic author" && draft.Metadata.Unit == "Synthetic unit", "Background attachment merges with the current saved draft instead of replaying stale text");
            check(!content.IsPublic && content.State == WorkflowState.Draft && asset.ScanStatus == "NotScanned" && asset.IsQuarantined, "Background attachment does not publish, fake Clean or expose the original");
            check(await db.ContentCommands.CountAsync(x => x.Id == pending.Session && x.ContentId == first.ContentId && x.ActorUserId == user) == 1 && await db.Revisions.CountAsync(x => x.ContentId == first.ContentId) == revisionCount + 1, "Background attachment saves one revision and one durable receipt atomically");
        }
        await Drain();
        check(await Revisions(first.ContentId) == revisionCount + 1, "Background polling after a lost response does not create another revision");
        var removed = await Save(first.ContentId, "Video deliberately removed", await Version(first.ContentId));
        await Drain();
        check(!await Attached(first.ContentId, pending.Asset) && await Revisions(first.ContentId) == revisionCount + 2, "Background polling never reattaches a video deliberately removed after automatic attachment");

        var concurrent = await NewContent("Concurrent attachment");
        var concurrentFile = await Fixture(concurrent.ContentId, true, VideoState.Ready);
        var concurrentBefore = await Revisions(concurrent.ContentId);
        await Task.WhenAll(Drain(), Drain());
        check(await Attached(concurrent.ContentId, concurrentFile.Asset) && await Revisions(concurrent.ContentId) == concurrentBefore + 1 && await HasReceipt(concurrentFile.Session), "Two concurrent background sweeps attach once without a duplicate revision");

        var revoke = await NewContent("Permission revoked after upload");
        var revokeFile = await Fixture(revoke.ContentId, true, VideoState.Ready);
        await using (var db = new PortalDbContext(options))
        {
            db.UserClaims.Remove(await db.UserClaims.SingleAsync(x => x.UserId == user && x.ClaimType == "permission" && x.ClaimValue == Permissions.ManageVideos));
            await db.SaveChangesAsync();
        }
        await Drain();
        check(!await Attached(revoke.ContentId, revokeFile.Asset) && !await HasReceipt(revokeFile.Session), "Background attachment rechecks current uploader permissions after the browser closes");
        await using (var db = new PortalDbContext(options))
        {
            db.UserClaims.Add(new IdentityUserClaim<string> { UserId = user, ClaimType = "permission", ClaimValue = Permissions.ManageVideos });
            await db.SaveChangesAsync();
        }
        await Drain();
        check(!await Attached(revoke.ContentId, revokeFile.Asset), "A blocked permission request is not silently retried forever");
        await Retry(revokeFile.Session);
        check(await Attached(revoke.ContentId, revokeFile.Asset), "Explicit retry rechecks restored permission before attaching a previously blocked intent");

        var corrupt = await NewContent("Corrupt output cannot attach");
        var corruptFile = await Fixture(corrupt.ContentId, true, VideoState.Ready);
        await File.AppendAllTextAsync(corruptFile.Output, "CORRUPTED");
        await Drain();
        check(!await Attached(corrupt.ContentId, corruptFile.Asset) && !await HasReceipt(corruptFile.Session), "Background attachment verifies actual MP4 bytes and does not consume a failed integrity check");
        await File.WriteAllBytesAsync(corruptFile.Output, corruptFile.OutputBytes);
        await Drain();
        check(!await Attached(corrupt.ContentId, corruptFile.Asset), "An integrity-blocked intent is not automatically retried without an explicit action");
        await Retry(corruptFile.Session);
        check(await Attached(corrupt.ContentId, corruptFile.Asset), "Explicit retry rechecks restored output bytes before attachment");

        var manual = await NewContent("Previously selected and deliberately removed");
        var manualFile = await Fixture(manual.ContentId, true, VideoState.Ready);
        var selected = await Save(manual.ContentId, "Selected manually", manual.RowVersion, [new() { MediaAssetId = manualFile.Asset, Usage = "Video" }]);
        await Save(manual.ContentId, "Removed manually before automatic attachment", selected.RowVersion);
        await Drain();
        check(!await Attached(manual.ContentId, manualFile.Asset), "Historical manual selection followed by removal also suppresses automatic reattachment");

        var autosaved = await NewContent("Autosaved selection then removal without a revision");
        var autosavedFile = await Fixture(autosaved.ContentId, true, VideoState.Ready);
        var autosavedSelection = await Save(autosaved.ContentId, "Selected by autosave", autosaved.RowVersion,
            [new() { MediaAssetId = autosavedFile.Asset, Usage = "Video" }], autosave: true);
        await Save(autosaved.ContentId, "Removed by autosave", autosavedSelection.RowVersion, autosave: true);
        await Drain();
        await Retry(autosavedFile.Session);
        await using (var db = new PortalDbContext(options))
        {
            check(!await Attached(autosaved.ContentId, autosavedFile.Asset) && !await HasReceipt(autosavedFile.Session) &&
                !await db.ContentAssets.AnyAsync(x => x.MediaAssetId == autosavedFile.Asset) &&
                await db.AuditLogs.AnyAsync(x => x.ContentId == autosaved.ContentId && x.Action == VideoDraftAttachmentService.Removed && x.Detail == autosavedFile.Asset.ToString("D")),
                "Autosave-only removal is durable without a historical revision and blocks background or explicit attachment retry");
            var guarded = false;
            try { await Workflow(db).AttachReadyVideoAsync(autosaved.ContentId, autosavedFile.Session, actor, await Version(autosaved.ContentId), true); }
            catch (DbUpdateConcurrencyException) { guarded = true; }
            check(guarded && !await Attached(autosaved.ContentId, autosavedFile.Asset), "Locked workflow also rejects an attachment that races with a committed autosave removal");
        }

        var full = await NewContent("Already autosaved video at the attachment limit");
        var fullFile = await Fixture(full.ContentId, true, VideoState.Ready);
        var fullSelection = new List<AssetSelection> { new() { MediaAssetId = fullFile.Asset, Usage = "Video" } };
        await using (var db = new PortalDbContext(options))
        {
            // Metadata-only test attachments are never published or served. They
            // fill the draft limit without another video/transcoding workload.
            for (var i = 0; i < 99; i++)
            {
                var filler = new MediaAsset { Origin = "Source", UploadedByUserId = user, OriginalName = "synthetic.pdf", MimeType = "application/pdf",
                    StorageKey = "cms-background-limit/" + Guid.NewGuid().ToString("N") + ".pdf", ByteLength = 1,
                    Sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full.ContentId + ":" + i))) };
                db.MediaAssets.Add(filler);
                fullSelection.Add(new() { MediaAssetId = filler.Id, Usage = "Attachment", SortOrder = i + 1 });
            }
            await db.SaveChangesAsync();
        }
        await Save(full.ContentId, "Already autosaved video at the attachment limit", full.RowVersion, fullSelection, autosave: true);
        var fullRevisionCount = await Revisions(full.ContentId);
        bool noHistoricalVideo;
        await using (var db = new PortalDbContext(options)) noHistoricalVideo = !await db.ContentAssets.AnyAsync(x => x.MediaAssetId == fullFile.Asset);
        await Drain();
        await using (var db = new PortalDbContext(options))
            check(noHistoricalVideo && await db.DraftAssets.CountAsync(x => x.ContentId == full.ContentId) == 100 &&
                await db.DraftAssets.CountAsync(x => x.ContentId == full.ContentId && x.MediaAssetId == fullFile.Asset) == 1 &&
                await HasReceipt(fullFile.Session) && await Revisions(full.ContentId) == fullRevisionCount + 1,
                "An already autosaved Ready video at 100 attachments consumes its intent without adding a duplicate or rejecting the existing asset");

        var legacy = await NewContent("Standalone upload has no intent");
        var legacyFile = await Fixture(legacy.ContentId, false, VideoState.Ready);
        await Drain();
        check(!await Attached(legacy.ContentId, legacyFile.Asset) && !await HasReceipt(legacyFile.Session), "Standalone uploads do not acquire an automatic attachment request");

        var lifecycle = await NewContent("Submitted draft does not accept pending attachment");
        var existing = await Fixture(lifecycle.ContentId, false, VideoState.Ready);
        var withExisting = await Save(lifecycle.ContentId, "Submitted draft does not accept pending attachment", lifecycle.RowVersion, [new() { MediaAssetId = existing.Asset, Usage = "Video" }]);
        var delayed = await Fixture(lifecycle.ContentId, true, VideoState.Pending);
        WorkflowResult submitted;
        await using (var db = new PortalDbContext(options)) submitted = await Workflow(db).SubmitAsync(lifecycle.ContentId, withExisting.CurrentRevisionId, actor, withExisting.RowVersion, Guid.NewGuid());
        await SetReady(delayed.Asset);
        await Drain();
        check(!await Attached(lifecycle.ContentId, delayed.Asset) && !await HasReceipt(delayed.Session), "A Ready video cannot mutate a submitted revision");
        WorkflowResult rejected;
        await using (var db = new PortalDbContext(options)) rejected = await Workflow(db).RejectAsync(lifecycle.ContentId, submitted.CurrentRevisionId!.Value, "Synthetic review return", actor, submitted.RowVersion, Guid.NewGuid());
        await using (var db = new PortalDbContext(options)) await Workflow(db).BeginEditAsync(lifecycle.ContentId, actor, rejected.RowVersion, Guid.NewGuid());
        await Drain();
        check(!await Attached(lifecycle.ContentId, delayed.Asset) && !await HasReceipt(delayed.Session), "An old upload intent is not applied to a reopened draft after submission and rejection");
    }

    private sealed class UnavailableScanner : IInquiryScanner
    {
        public Task<(bool Ready,string Code)> HealthAsync(CancellationToken cancellation = default) =>
            throw new InvalidOperationException("Ready attachment must not consult scanner health.");
        public Task<InquiryScanResult> ScanAsync(Stream file, CancellationToken cancellation = default) =>
            throw new InvalidOperationException("Ready attachment must not scan or relabel existing output.");
    }
}
