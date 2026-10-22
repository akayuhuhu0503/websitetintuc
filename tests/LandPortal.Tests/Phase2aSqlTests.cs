using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using LandPortal.MigrationCli;
using System.Text.Json;

namespace LandPortal.Tests;

public static class Phase2aSqlTests
{
    public static readonly string[] AllPermissions = [Permissions.Create, Permissions.ViewOwn, Permissions.ViewAll, Permissions.EditOwn, Permissions.EditAll, Permissions.Submit, Permissions.Review, Permissions.PublishApproved, Permissions.PublishDirect, Permissions.Unpublish, Permissions.Trash, Permissions.Restore, Permissions.ManageMedia];
    public static ContentActor Actor(string id = "qa-sql-owner") => new(id, AllPermissions.ToHashSet());
    public static DraftInput Input(string title, int? category = null, string body = "<p>Nội dung kiểm thử riêng trong SQL test.</p>") => new() { Title = title, Summary = "Mô tả kiểm thử", BodyHtml = body, PrimaryCategoryId = category ?? 75, Author = "QA Test", SourceCredit = "Fixture test" };
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    public static IEnumerable<(string Name, Func<Task> Test)> Cases(DbContextOptions<PortalDbContext> options, IConfiguration config)
    {
        PortalDbContext Db() => new(options);
        WorkflowService Workflow(PortalDbContext db) => new(db, new HtmlPolicy(), MediaUploadOptions.FromConfiguration(config,"Development"), StorageOptions.FromConfiguration(config));
        async Task<Content> Get(Guid id) { await using var db = Db(); return await db.Contents.AsNoTracking().SingleAsync(x => x.Id == id); }
        yield return ("2A SQL retired media selections are pruned while selected PDF and immutable history remain", async () =>
        {
            await using var db = Db(); var workflow = Workflow(db); var actor = Actor();
            var images = await db.MediaAssets.Where(x => x.SourceUrl != "" && x.MimeType.StartsWith("image/")).OrderBy(x => x.Id).Take(4).Select(x => x.Id).ToArrayAsync();
            var pdf = await db.MediaAssets.Where(x => x.SourceUrl != "" && x.MimeType == "application/pdf").OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
            Assert(images.Length == 4, "Real source image fixtures were not available.");
            var first = Input("QA retired media " + Guid.NewGuid().ToString("N"), body: $"<p>Ảnh trong bản gốc.</p><img src='/media/{images[1]}'>");
            first.CoverAssetId = images[0];
            first.Assets = [new() { MediaAssetId = images[0], Usage = "Cover" }, new() { MediaAssetId = images[1], Usage = "Inline" }, new() { MediaAssetId = pdf, Usage = "Attachment", Caption = "PDF trong panel" }];
            var saved = await workflow.SaveAsync(null, first, actor, null, Guid.NewGuid());
            var published = await workflow.PublishDirectAsync(saved.ContentId, saved.CurrentRevisionId!.Value, actor, saved.RowVersion, Guid.NewGuid());
            var historicalId = published.PublishedRevisionId!.Value;
            var editing = await workflow.BeginEditAsync(saved.ContentId, actor, published.RowVersion, Guid.NewGuid());
            var revised = Input(first.Title, body: "<p>Bản mới đã bỏ ảnh cũ; PDF vẫn được chọn.</p>");
            revised.CoverAssetId = images[2];
            revised.Assets = [new() { MediaAssetId = images[0], Usage = "Cover" }, new() { MediaAssetId = images[1], Usage = "Inline" }, new() { MediaAssetId = images[3], Usage = "Thumbnail" }, new() { MediaAssetId = pdf, Usage = "Attachment", Caption = "PDF vẫn chọn" }];
            var next = await workflow.SaveAsync(saved.ContentId, revised, actor, editing.RowVersion, Guid.NewGuid());
            var selected = await db.ContentAssets.AsNoTracking().Where(x => x.RevisionId == next.CurrentRevisionId).ToListAsync();
            Assert(selected.Count == 2 && selected.Any(x => x.MediaAssetId == images[2]) && selected.Any(x => x.MediaAssetId == pdf && x.Usage == "Attachment" && x.Caption == "PDF vẫn chọn"), "Retired image selections survived or a valid selected PDF was removed.");
            Assert(await db.ContentAssets.CountAsync(x => x.RevisionId == historicalId) == 3, "Pruning the new revision changed immutable historical asset membership.");
            var current = await workflow.PublishDirectAsync(saved.ContentId, next.CurrentRevisionId!.Value, actor, next.RowVersion, Guid.NewGuid());
            var reopen = await workflow.BeginEditAsync(saved.ContentId, actor, current.RowVersion, Guid.NewGuid());
            var bad = Input(first.Title, body: "<p>Không có placement cho ảnh này.</p>");
            bad.Assets = [new() { MediaAssetId = images[0], Usage = "Attachment" }];
            var revisionCount = await db.Revisions.CountAsync(x => x.ContentId == saved.ContentId);
            await Expect<InvalidOperationException>(() => workflow.SaveAsync(saved.ContentId, bad, actor, reopen.RowVersion, Guid.NewGuid()));
            await using var verify = Db();
            Assert(await verify.Revisions.CountAsync(x => x.ContentId == saved.ContentId) == revisionCount && (await verify.Contents.SingleAsync(x => x.Id == saved.ContentId)).CurrentRevisionId == reopen.CurrentRevisionId, "A forged image attachment persisted a revision despite rejection.");
            Assert(await verify.ContentAssets.CountAsync(x => x.RevisionId == historicalId) == 3, "Rejected image attachment changed historical membership.");
        });
        yield return ("2A SQL approval and approved publishing target the exact immutable submitted revision", async () =>
        {
            await using var db = Db(); var w = Workflow(db); var actor = Actor();
            var saved = await w.SaveAsync(null, Input("QA duyệt đúng phiên bản " + Guid.NewGuid().ToString("N")), actor, null, Guid.NewGuid());
            var submitted = await w.SubmitAsync(saved.ContentId, saved.CurrentRevisionId, actor, saved.RowVersion, Guid.NewGuid());
            Assert(!submitted.IsPublic && submitted.State == WorkflowState.PendingReview, "Submission unexpectedly public.");
            var approved = await w.ApproveAsync(saved.ContentId, submitted.CurrentRevisionId!.Value, Actor("qa-sql-reviewer"), submitted.RowVersion, Guid.NewGuid());
            var published = await w.PublishApprovedAsync(saved.ContentId, approved.CurrentRevisionId!.Value, Actor("qa-sql-publisher"), approved.RowVersion, Guid.NewGuid());
            Assert(published.IsPublic && published.PublishedRevisionId == submitted.CurrentRevisionId, "Approval published another revision.");
            var exact = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == published.PublishedRevisionId);
            Assert(exact.Title.StartsWith("QA duyệt đúng phiên bản") && exact.BodyHtml.Contains("SQL test"), "Approved immutable payload was lost.");
        });
        yield return ("2A SQL all public metadata and categories remain unchanged while draft metadata changes", async () =>
        {
            await using var db = Db(); var w = Workflow(db); var actor = Actor(); var categories = await db.Categories.Where(x => x.Id > 0).OrderBy(x => x.Id).Take(2).Select(x => x.Id).ToArrayAsync();
            Assert(categories.Length == 2, "Source category fixtures missing.");
            var images = await db.MediaAssets.Where(x => x.SourceUrl != "" && x.MimeType.StartsWith("image/")).Take(2).Select(x => x.Id).ToArrayAsync();
            Assert(images.Length == 2, "Real local image fixtures missing.");
            var first = Input("QA public old " + Guid.NewGuid().ToString("N"), categories[0], "<table><tr><td>Bảng gốc</td></tr></table>"); first.CoverAssetId = images[0]; first.Assets = [new() { MediaAssetId = images[0], Usage = "Cover" }];
            var saved = await w.SaveAsync(null, first, actor, null, Guid.NewGuid()); var published = await w.PublishDirectAsync(saved.ContentId, saved.CurrentRevisionId!.Value, actor, saved.RowVersion, Guid.NewGuid());
            var publicId = published.PublishedRevisionId; var original = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == publicId);
            var second = Input("QA private new " + Guid.NewGuid().ToString("N"), categories[1], "<p>Nội dung mới chưa công khai</p>"); second.Summary = "Mô tả mới riêng"; second.Author = "Tác giả riêng"; second.CoverAssetId = images[1]; second.Assets = [new() { MediaAssetId = images[1], Usage = "Cover" }];
            var editing = await w.BeginEditAsync(saved.ContentId, actor, published.RowVersion, Guid.NewGuid()); var draft = await w.SaveAsync(saved.ContentId, second, actor, editing.RowVersion, Guid.NewGuid());
            Assert(draft.IsPublic && draft.PublishedRevisionId == publicId && draft.CurrentRevisionId != publicId, "Draft removed/replaced live content.");
            var submitted = await w.SubmitAsync(saved.ContentId, draft.CurrentRevisionId, actor, draft.RowVersion, Guid.NewGuid());
            Assert(submitted.IsPublic && submitted.PublishedRevisionId == publicId, "Submitting edited draft hid old publication.");
            var old = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == publicId);
            Assert(old.Title == original.Title && old.Summary == first.Summary && old.Author == first.Author && old.PrimaryCategoryId == categories[0] && old.CoverAssetId == images[0] && old.BodyHtml.Contains("Bảng gốc"), "Draft metadata leaked into public revision.");
            var approved = await w.ApproveAsync(saved.ContentId, submitted.CurrentRevisionId!.Value, actor, submitted.RowVersion, Guid.NewGuid()); var next = await w.PublishApprovedAsync(saved.ContentId, approved.CurrentRevisionId!.Value, actor, approved.RowVersion, Guid.NewGuid());
            var current = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == next.PublishedRevisionId);
            Assert(current.Title == second.Title && current.Summary == second.Summary && current.Author == second.Author && current.PrimaryCategoryId == categories[1] && current.CoverAssetId == images[1], "Publishing lost revision metadata.");
        });
        yield return ("2A SQL resource ownership and action permissions reject direct unauthorized workflow calls", async () =>
        {
            await using var db = Db(); var w = Workflow(db); var owner = new ContentActor("qa-owner-a", new[] { Permissions.Create, Permissions.ViewOwn, Permissions.EditOwn, Permissions.Submit }.ToHashSet());
            var saved = await w.SaveAsync(null, Input("QA quyền sở hữu " + Guid.NewGuid().ToString("N")), owner, null, Guid.NewGuid()); var foreign = owner with { UserId = "qa-owner-b" };
            await Expect<UnauthorizedAccessException>(() => w.SaveAsync(saved.ContentId, Input("QA forbidden"), foreign, saved.RowVersion, Guid.NewGuid()));
            await Expect<UnauthorizedAccessException>(() => w.SubmitAsync(saved.ContentId, saved.CurrentRevisionId, foreign, saved.RowVersion, Guid.NewGuid()));
            await Expect<UnauthorizedAccessException>(() => w.PublishDirectAsync(saved.ContentId, saved.CurrentRevisionId!.Value, owner, saved.RowVersion, Guid.NewGuid()));
            await Expect<UnauthorizedAccessException>(() => w.UnpublishAsync(saved.ContentId, owner, saved.RowVersion, Guid.NewGuid()));
            Assert((await Get(saved.ContentId)).CurrentRevisionId == saved.CurrentRevisionId, "Unauthorized action changed current content.");
        });
        yield return ("2A SQL stale concurrent saves roll back without leaving revisions or changing working data", async () =>
        {
            Guid id; byte[] stale; int count;
            await using (var db = Db()) { var w = Workflow(db); var saved = await w.SaveAsync(null, Input("QA race first"), Actor(), null, Guid.NewGuid()); id = saved.ContentId; stale = saved.RowVersion.ToArray(); await w.SaveAsync(id, Input("QA race second"), Actor(), saved.RowVersion, Guid.NewGuid()); count = await db.Revisions.CountAsync(x => x.ContentId == id); }
            await using var old = Db(); await Expect<DbUpdateConcurrencyException>(() => Workflow(old).SaveAsync(id, Input("QA race stale"), Actor(), stale, Guid.NewGuid()));
            await using var verify = Db(); Assert(await verify.Revisions.CountAsync(x => x.ContentId == id) == count, "Stale save left an immutable revision."); Assert((await verify.ContentDrafts.SingleAsync(x => x.ContentId == id)).Title == "QA race second", "Stale save overwrote newer working draft.");
        });
        yield return ("2A SQL delayed autosave is fenced after submit and publish and never rewinds workflow", async () =>
        {
            await using var db = Db(); var w = Workflow(db); var actor = Actor(); var saved = await w.SaveAsync(null, Input("QA autosave fence"), actor, null, Guid.NewGuid());
            var submitted = await w.SubmitAsync(saved.ContentId, saved.CurrentRevisionId, actor, saved.RowVersion, Guid.NewGuid());
            await Expect<DbUpdateConcurrencyException>(() => w.SaveAsync(saved.ContentId, Input("QA late autosave"), actor, saved.RowVersion, Guid.NewGuid(), autosave: true));
            await using var fresh = Db(); var wf = Workflow(fresh); var latest = await fresh.Contents.SingleAsync(x => x.Id == saved.ContentId);
            Assert(latest.State == WorkflowState.PendingReview && latest.CurrentRevisionId == submitted.CurrentRevisionId, "Delayed autosave rewound review state.");
            var approved = await wf.ApproveAsync(saved.ContentId, latest.CurrentRevisionId!.Value, actor, latest.RowVersion, Guid.NewGuid()); var published = await wf.PublishApprovedAsync(saved.ContentId, approved.CurrentRevisionId!.Value, actor, approved.RowVersion, Guid.NewGuid());
            await Expect<DbUpdateConcurrencyException>(() => wf.SaveAsync(saved.ContentId, Input("QA late published autosave"), actor, approved.RowVersion, Guid.NewGuid(), autosave: true));
            Assert((await Get(saved.ContentId)).IsPublic, "Delayed autosave hid publication.");
        });
        yield return ("2A SQL editing an approved revision invalidates approval and old revision cannot publish", async () =>
        {
            await using var db = Db(); var w = Workflow(db); var actor = Actor(); var saved = await w.SaveAsync(null, Input("QA approval invalidation"), actor, null, Guid.NewGuid()); var submitted = await w.SubmitAsync(saved.ContentId, saved.CurrentRevisionId, actor, saved.RowVersion, Guid.NewGuid()); var approved = await w.ApproveAsync(saved.ContentId, submitted.CurrentRevisionId!.Value, actor, submitted.RowVersion, Guid.NewGuid());
            var reopened = await w.BeginEditAsync(saved.ContentId, actor, approved.RowVersion, Guid.NewGuid()); var edited = await w.SaveAsync(saved.ContentId, Input("QA newer unapproved payload"), actor, reopened.RowVersion, Guid.NewGuid());
            await Expect<DbUpdateConcurrencyException>(() => w.PublishApprovedAsync(saved.ContentId, approved.CurrentRevisionId!.Value, actor, edited.RowVersion, Guid.NewGuid()));
            var content = await Get(saved.ContentId); Assert(!content.IsPublic && content.ApprovedRevisionId == null && content.CurrentRevisionId == edited.CurrentRevisionId, "Old approval authorized a newer payload.");
        });
        yield return ("2A SQL duplicate publish command is idempotent with one audit and conflicts on stale different command", async () =>
        {
            await using var db = Db(); var w = Workflow(db); var actor = Actor(); var saved = await w.SaveAsync(null, Input("QA double publish"), actor, null, Guid.NewGuid()); var command = Guid.NewGuid(); var published = await w.PublishDirectAsync(saved.ContentId, saved.CurrentRevisionId!.Value, actor, saved.RowVersion, command);
            var before = await db.AuditLogs.CountAsync(x => x.ContentId == saved.ContentId); var repeated = await w.PublishDirectAsync(saved.ContentId, saved.CurrentRevisionId!.Value, actor, saved.RowVersion, command);
            Assert(repeated.AlreadyApplied && repeated.PublishedRevisionId == published.PublishedRevisionId && await db.AuditLogs.CountAsync(x => x.ContentId == saved.ContentId) == before, "Publish retry duplicated side effects.");
            await Expect<DbUpdateConcurrencyException>(() => w.PublishDirectAsync(saved.ContentId, saved.CurrentRevisionId!.Value, actor, saved.RowVersion, Guid.NewGuid()));
        });
        yield return ("2A SQL invalid category/media save transaction preserves immutable and working revision", async () =>
        {
            await using var db = Db(); var w = Workflow(db); var actor = Actor(); var saved = await w.SaveAsync(null, Input("QA transaction fixture"), actor, null, Guid.NewGuid()); var before = await db.Revisions.CountAsync(x => x.ContentId == saved.ContentId);
            await Expect<InvalidOperationException>(() => w.SaveAsync(saved.ContentId, Input("QA invalid category", int.MinValue), actor, saved.RowVersion, Guid.NewGuid()));
            await using var verify = Db(); Assert(await verify.Revisions.CountAsync(x => x.ContentId == saved.ContentId) == before && (await verify.ContentDrafts.SingleAsync(x => x.ContentId == saved.ContentId)).Title == "QA transaction fixture", "Rejected save left partial state.");
            var invalid=Input("QA missing asset must rollback");invalid.CoverAssetId=Guid.NewGuid();await Expect<InvalidOperationException>(()=>w.SaveAsync(saved.ContentId,invalid,actor,saved.RowVersion,Guid.NewGuid()));
            var recovered=await w.SaveAsync(saved.ContentId,Input("QA valid same-context recovery"),actor,saved.RowVersion,Guid.NewGuid());
            await using var after=Db();Assert((await after.ContentDrafts.SingleAsync(x=>x.ContentId==saved.ContentId)).Title=="QA valid same-context recovery"&&await after.Revisions.CountAsync(x=>x.ContentId==saved.ContentId)==before+1&&recovered.CurrentRevisionId!=saved.CurrentRevisionId,"A later command persisted failed pending entities or could not reuse the clean context.");
        });
        yield return ("2A SQL concurrent retry of one publish command has one committed audit and exact revision", async () =>
        {
            WorkflowResult saved; await using(var seed=Db()) saved=await Workflow(seed).SaveAsync(null,Input("QA concurrent publish"),Actor(),null,Guid.NewGuid());
            var command=Guid.NewGuid(); await using var first=Db();await using var second=Db();
            var results=await Task.WhenAll(Workflow(first).PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,Actor(),saved.RowVersion,command),Workflow(second).PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,Actor(),saved.RowVersion,command));
            await using var verify=Db();Assert(results.All(x=>x.IsPublic&&x.PublishedRevisionId==saved.CurrentRevisionId),"Concurrent retry published wrong snapshot.");Assert(await verify.AuditLogs.CountAsync(x=>x.ContentId==saved.ContentId&&x.Action=="PublishDirect")==1,"Concurrent retry committed duplicate audit.");
        });
        yield return ("2A SQL autosave changes only working area without history per keystroke and submit freezes latest content", async () =>
        {
            await using var db=Db();var w=Workflow(db);var saved=await w.SaveAsync(null,Input("QA autosave starting"),Actor(),null,Guid.NewGuid());var before=await db.Revisions.CountAsync(x=>x.ContentId==saved.ContentId);
            var a=await w.SaveAsync(saved.ContentId,Input("QA autosave newest",body:"<p>Bản tự lưu mới nhất</p>"),Actor(),saved.RowVersion,Guid.NewGuid(),autosave:true);var b=await w.SaveAsync(saved.ContentId,Input("QA autosave final",body:"<p>Bản tự lưu cuối cùng</p>"),Actor(),a.RowVersion,Guid.NewGuid(),autosave:true);
            Assert(b.CurrentRevisionId==saved.CurrentRevisionId&&await db.Revisions.CountAsync(x=>x.ContentId==saved.ContentId)==before,"Autosave inflated immutable history.");var submitted=await w.SubmitAsync(saved.ContentId,b.CurrentRevisionId,Actor(),b.RowVersion,Guid.NewGuid());var frozen=await db.Revisions.AsNoTracking().SingleAsync(x=>x.Id==submitted.CurrentRevisionId);Assert(frozen.Title=="QA autosave final"&&frozen.BodyHtml.Contains("cuối cùng"),"Submit froze obsolete pre-autosave content.");
        });
        yield return ("2A SQL cloning a source article creates private draft without copying unique source identity or aliases", async () =>
        {
            await using var db=Db();var source=await db.SourceRecords.Where(x=>x.Kind==ContentKind.Article&&!x.SourceId.StartsWith("QA-")).FirstAsync();var before=await db.SourceRecords.CountAsync();var cloned=await Workflow(db).CloneAsync(source.ContentId,null,Actor(),Guid.NewGuid());
            Assert(cloned.ContentId!=source.ContentId&&!cloned.IsPublic&&cloned.State==WorkflowState.Draft,"Clone changed original or published automatically.");Assert(!await db.SourceRecords.AnyAsync(x=>x.ContentId==cloned.ContentId)&&!await db.SourceUrlAliases.AnyAsync(x=>x.ContentId==cloned.ContentId)&&await db.SourceRecords.CountAsync()==before,"Clone copied unique source mapping.");
        });
        yield return ("2A SQL unscanned uploaded files cannot publish by default or Production and explicit Development stays NotScanned", async () =>
        {
            await using var db=Db();var actor=Actor();var settings=MediaUploadOptions.FromConfiguration(config,"Development");var storage=StorageOptions.FromConfiguration(config);
            using var bitmap=new SkiaSharp.SKBitmap(8,8);for(var y=0;y<8;y++)for(var x=0;x<8;x++)bitmap.SetPixel(x,y,new SkiaSharp.SKColor((byte)Random.Shared.Next(256),(byte)Random.Shared.Next(256),(byte)Random.Shared.Next(256)));using var bytes=bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);using var stream=new MemoryStream(bytes.ToArray());
            var upload=await new MediaService(db,storage,settings).UploadAsync(stream,"qa-scan-test.png","image/png",actor.UserId,true);Assert(upload.Asset.ScanStatus=="NotScanned","Test file falsely marked scanned.");var input=Input("QA explicit scanner exception");input.CoverAssetId=upload.Asset.Id;input.Assets=[new(){MediaAssetId=upload.Asset.Id,Usage="Cover"}];var saved=await Workflow(db).SaveAsync(null,input,actor,null,Guid.NewGuid());
            await Expect<InvalidOperationException>(()=>new WorkflowService(db,new HtmlPolicy(),storage:storage).PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid()));
            var production=MediaUploadOptions.FromConfiguration(config,"Production");await Expect<InvalidOperationException>(()=>new WorkflowService(db,new HtmlPolicy(),production,storage).PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid()));
            var published=await new WorkflowService(db,new HtmlPolicy(),settings,storage).PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());Assert(published.IsPublic&&upload.Asset.ScanStatus=="NotScanned","Explicit development publish invented scanner status or failed.");
        });
        yield return ("2A SQL unpublish trash restore and historical restore preserve history without automatic republish", async () =>
        {
            await using var db = Db(); var w = Workflow(db); var actor = Actor(); var saved = await w.SaveAsync(null, Input("QA history original"), actor, null, Guid.NewGuid()); var original = saved.CurrentRevisionId!.Value; var published = await w.PublishDirectAsync(saved.ContentId, original, actor, saved.RowVersion, Guid.NewGuid()); var removed = await w.UnpublishAsync(saved.ContentId, actor, published.RowVersion, Guid.NewGuid());
            Assert(!removed.IsPublic, "Unpublish kept live flag."); var history = await db.Revisions.CountAsync(x => x.ContentId == saved.ContentId); var trashed = await w.TrashAsync(saved.ContentId, actor, removed.RowVersion, Guid.NewGuid()); var restored = await w.RestoreTrashAsync(saved.ContentId, actor, trashed.RowVersion, Guid.NewGuid());
            Assert(!restored.IsPublic && !restored.IsTrashed && await db.Revisions.CountAsync(x => x.ContentId == saved.ContentId) == history, "Recycle restore published content or erased history.");
            var historical = await w.RestoreRevisionAsync(saved.ContentId, original, actor, restored.RowVersion, Guid.NewGuid());
            Assert(!historical.IsPublic && historical.State == WorkflowState.Draft && historical.CurrentRevisionId != original && await db.Revisions.CountAsync(x => x.ContentId == saved.ContentId) > history, "Historical restore mutated old revision or silently republished.");
        });
        yield return ("2A SQL missing uploaded original blocks publish and restored Test file enables the same exact revision", async () =>
        {
            await using var db=Db();var actor=Actor();var settings=MediaUploadOptions.FromConfiguration(config,"Development");var storage=StorageOptions.FromConfiguration(config);
            using var bitmap=new SkiaSharp.SKBitmap(4,12);for(var y=0;y<12;y++)for(var x=0;x<4;x++)bitmap.SetPixel(x,y,new SkiaSharp.SKColor((byte)Random.Shared.Next(256),(byte)Random.Shared.Next(256),(byte)Random.Shared.Next(256)));using var encoded=bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);using var stream=new MemoryStream(encoded.ToArray());
            var uploaded=await new MediaService(db,storage,settings).UploadAsync(stream,"qa-missing-original.png","image/png",actor.UserId,true);var input=Input("QA missing file publication");input.CoverAssetId=uploaded.Asset.Id;var service=new WorkflowService(db,new HtmlPolicy(),settings,storage);var saved=await service.SaveAsync(null,input,actor,null,Guid.NewGuid());var original=storage.ResolveMedia(uploaded.Asset.StorageKey);var held=storage.ResolveMedia(uploaded.Asset.StorageKey+".qa-held");
            File.Move(original,held);try{await Expect<InvalidOperationException>(()=>service.PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid()));Assert(!(await Get(saved.ContentId)).IsPublic,"Publish succeeded while original file missing.");}finally{File.Move(held,original);}
            var published=await service.PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());Assert(published.IsPublic&&published.PublishedRevisionId==saved.CurrentRevisionId,"Restored genuine Test original could not publish exact revision.");
        });
        yield return ("2A SQL copied historical source without primary category remains editable and publishable while new templates still require category", async () =>
        {
            var storage=StorageOptions.FromConfiguration(config);var manifest=JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(Path.Combine(storage.SnapshotRoot,"sample-manifest.json")),SampleCollector.JsonOptions)!;var staged=manifest.Contents.First(x=>x.Source.Kind==ContentKind.Article);var key="QA-compat-"+Guid.NewGuid().ToString("N");staged.Source.SourceId=key;staged.Source.Title="[QA copied source compatibility] "+staged.Source.Title;staged.Source.BodyHtml="<p>"+System.Net.WebUtility.HtmlEncode(new HtmlPolicy().PlainText(staged.Source.BodyHtml))+"</p>";staged.Source.PrimaryCategoryId=null;staged.Source.CategoryIds=[];staged.AssetIds=[];staged.Aliases=[];staged.ContentHash=TextNormalizer.Hash(staged.Source.Title+staged.Source.BodyHtml+key);manifest.Contents=[staged];manifest.Discovered=[];
            await using var db=Db();await new SqlImporter(db,storage).ImportAsync(manifest);var source=await db.SourceRecords.SingleAsync(x=>x.Kind==ContentKind.Article&&x.SourceId==key);var content=await db.Contents.SingleAsync(x=>x.Id==source.ContentId);Assert(content.PrimaryCategoryId==null&&content.IsPublic,"Copied legacy fixture unexpectedly gained category or lost publication.");var w=Workflow(db);var draft=await w.BeginEditAsync(content.Id,Actor(),content.RowVersion,Guid.NewGuid());var input=Input("[QA copied source compatibility] Edited fixture",body:staged.Source.BodyHtml);input.PrimaryCategoryId=null;var saved=await w.SaveAsync(content.Id,input,Actor(),draft.RowVersion,Guid.NewGuid());var submitted=await w.SubmitAsync(content.Id,saved.CurrentRevisionId,Actor(),saved.RowVersion,Guid.NewGuid());var approved=await w.ApproveAsync(content.Id,submitted.CurrentRevisionId!.Value,Actor(),submitted.RowVersion,Guid.NewGuid());var published=await w.PublishApprovedAsync(content.Id,approved.CurrentRevisionId!.Value,Actor(),approved.RowVersion,Guid.NewGuid());Assert(published.IsPublic&&(await Get(content.Id)).PrimaryCategoryId==null,"Legacy missing-category compatibility forced manual data repair.");
            var fresh=Input("QA new template no category");fresh.PrimaryCategoryId=null;var created=await w.SaveAsync(null,fresh,Actor(),null,Guid.NewGuid());await Expect<InvalidOperationException>(()=>w.SubmitAsync(created.ContentId,created.CurrentRevisionId,Actor(),created.RowVersion,Guid.NewGuid()));await Expect<InvalidOperationException>(()=>w.PublishDirectAsync(created.ContentId,created.CurrentRevisionId!.Value,Actor(),created.RowVersion,Guid.NewGuid()));Assert(!(await Get(created.ContentId)).IsPublic,"New template bypassed category requirement.");
            var blank=await w.SaveAsync(null,Input("QA blank rich text",body:"<p><br>&nbsp;</p>"),Actor(),null,Guid.NewGuid());await Expect<InvalidOperationException>(()=>w.SubmitAsync(blank.ContentId,blank.CurrentRevisionId,Actor(),blank.RowVersion,Guid.NewGuid()));await Expect<InvalidOperationException>(()=>w.PublishDirectAsync(blank.ContentId,blank.CurrentRevisionId!.Value,Actor(),blank.RowVersion,Guid.NewGuid()));Assert(!(await Get(blank.ContentId)).IsPublic,"Empty rich-text markup passed publication validation.");
        });
        yield return ("2A SQL withdrawal audit identifies the withdrawn public revision while preserving a different pending draft",async()=>
        {
            await using var db=Db();var w=Workflow(db);var actor=Actor();var saved=await w.SaveAsync(null,Input("QA withdrawal original"),actor,null,Guid.NewGuid());var published=await w.PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());var oldPublic=published.PublishedRevisionId;var editing=await w.BeginEditAsync(saved.ContentId,actor,published.RowVersion,Guid.NewGuid());var changed=await w.SaveAsync(saved.ContentId,Input("QA withdrawal newer pending"),actor,editing.RowVersion,Guid.NewGuid());var pending=await w.SubmitAsync(saved.ContentId,changed.CurrentRevisionId,actor,changed.RowVersion,Guid.NewGuid());var removed=await w.UnpublishAsync(saved.ContentId,actor,pending.RowVersion,Guid.NewGuid());
            var audit=await db.AuditLogs.AsNoTracking().SingleAsync(x=>x.ContentId==saved.ContentId&&x.Action=="Unpublish");Assert(audit.RevisionId==oldPublic&&removed.CurrentRevisionId==pending.CurrentRevisionId&&!removed.IsPublic&&removed.State==WorkflowState.PendingReview,"Unpublish audit named the pending edit or rewound its state.");
            var approved=await w.ApproveAsync(saved.ContentId,pending.CurrentRevisionId!.Value,actor,removed.RowVersion,Guid.NewGuid());var republished=await w.PublishApprovedAsync(saved.ContentId,approved.CurrentRevisionId!.Value,actor,approved.RowVersion,Guid.NewGuid());var reopened=await w.BeginEditAsync(saved.ContentId,actor,republished.RowVersion,Guid.NewGuid());var newest=await w.SaveAsync(saved.ContentId,Input("QA draft during trash"),actor,reopened.RowVersion,Guid.NewGuid());var trashed=await w.TrashAsync(saved.ContentId,actor,newest.RowVersion,Guid.NewGuid());var trashAudit=await db.AuditLogs.AsNoTracking().SingleAsync(x=>x.ContentId==saved.ContentId&&x.Action=="Trash");Assert(trashAudit.RevisionId==republished.PublishedRevisionId&&trashed.CurrentRevisionId==newest.CurrentRevisionId&&trashed.IsTrashed&&!trashed.IsPublic,"Trash audit did not identify the withdrawn public revision or lost draft history.");
        });
    }
}
