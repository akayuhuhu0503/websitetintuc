using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

namespace LandPortal.Tests;

public static class Phase2bSiteTests
{
    public static IEnumerable<(string Name, Func<Task> Test)> Cases(DbContextOptions<PortalDbContext> options, IConfiguration config)
    {
        yield return ("2B site explicit initialize is idempotent and keeps all source categories", () => With(options, config, async f =>
        {
            var state = await f.State(); var count = await f.Db.Set<SiteConfigurationRevision>().CountAsync();
            Assert(!await f.Service.InitializeAsync("QA-site-rerun"), "Initialize reseeded an existing configuration.");
            Assert(await f.Db.Set<SiteConfigurationRevision>().CountAsync() == count, "Initialize inserted a duplicate revision.");
            var source = await f.Db.Categories.AsNoTracking().Where(x => x.SourceUrl != "").ToListAsync();
            Assert(source.Count(x => x.SortOrder < 14) == 14, "The original fourteen categories are incomplete.");
            foreach (var category in source) Assert(state.Revision.Categories.Any(x => x.CategoryId == category.Id && x.DisplayName == category.Name && x.Slug == category.Slug), "A source identity/display mapping changed.");
            Assert(new[] { "ExecutiveDocuments", "LandPlanning", "Weekly", "Organization", "Questions", "Contact", "Handbook" }.All(key => state.Revision.Menus.Any(x => x.RouteKey == key)), "A required source navigation item disappeared.");
        }));
        yield return ("2B site draft live exact revision optimistic concurrency and restore", () => With(options, config, async f =>
        {
            var original = await f.State(); var live = await f.Service.ReadAsync(true); var secret = "QA private footer " + Guid.NewGuid();
            var draft = await f.Service.MutateAsync(f.Manager, original.Header.RowVersion, r => r.FooterText = secret, "QA draft footer");
            Assert((await f.Service.ReadAsync(true))!.Revision.FooterText == live!.Revision.FooterText, "A draft footer changed the public version.");
            await Expect(409, () => f.Service.MutateAsync(f.Manager, original.Header.RowVersion, r => r.FooterText = "stale", "QA stale overwrite"));
            await Expect(403, () => f.Service.PublishAsync(f.Manager, draft.Header.RowVersion, draft.Revision.Id));
            await Expect(409, () => f.Service.PublishAsync(f.Publisher, draft.Header.RowVersion, original.Revision.Id));
            var published = await f.Service.PublishAsync(f.Publisher, draft.Header.RowVersion, draft.Revision.Id);
            Assert((await f.Service.ReadAsync(true))!.Revision.FooterText == secret, "Exact configuration was not atomically published.");
            var restored = await f.Service.RestoreAsync(f.Manager, published.Header.RowVersion, original.Revision.Id);
            Assert(restored.Revision.Id != original.Revision.Id && (await f.Service.ReadAsync(true))!.Revision.FooterText == secret, "Restore rewrote history or automatically published.");
            Assert(restored.Revision.FooterText == original.Revision.FooterText, "Restore did not copy the selected version.");
            await using var immutable = new PortalDbContext(options); var old = await immutable.Set<SiteConfigurationRevision>().SingleAsync(x => x.Id == original.Revision.Id); old.FooterText = "illegal history mutation";
            try { await immutable.SaveChangesAsync(); throw new InvalidOperationException("Historical configuration was editable."); } catch (InvalidOperationException error) when (error.Message != "Historical configuration was editable.") { }
        }));
        yield return ("2B site source protection cycles duplicate slugs and URL policy", () => With(options, config, async f =>
        {
            var original = await f.State(); var category = original.Revision.Categories.First(x => x.CategoryId == 75);
            await Expect(400, () => f.Service.MutateAsync(f.Manager, original.Header.RowVersion, r => r.Categories.RemoveAll(x => x.CategoryId == category.CategoryId), "QA source removal"));
            await Expect(400, () => f.Service.MutateAsync(f.Manager, original.Header.RowVersion, r => r.Categories.Single(x => x.CategoryId == category.CategoryId).DisplayName = "wrong", "QA source rename"));
            await Expect(400, () => f.Service.MutateAsync(f.Manager, original.Header.RowVersion, r => r.Categories.Add(new() { CategoryId = 199001, DisplayName = "QA duplicate", Slug = category.Slug }), "QA duplicate slug"));
            await Expect(400, () => f.Service.MutateAsync(f.Manager, original.Header.RowVersion, r => r.Categories.Add(new() { CategoryId = 199002, DisplayName = "QA cycle", Slug = "qa-cycle-" + Guid.NewGuid().ToString("N"), ParentCategoryId = 199002 }), "QA cycle"));
            foreach (var url in new[] { "javascript:alert(1)", "data:text/html,test", "//outside.invalid/path", "https://user:password@example.invalid/", "https://example.invalid\\admin", "https://example.invalid/admin/site/preview", "https://example.invalid/%61dmin/site/preview" })
                await Expect(400, () => f.Service.MutateAsync(f.Manager, original.Header.RowVersion, r => r.Menus.Add(new() { Label = "QA URL", Position = "footer", TargetKind = "External", ExternalUrl = url }), "QA URL policy"));
            await Expect(400, () => f.Service.MutateAsync(f.Manager, original.Header.RowVersion, r => r.Menus.Add(new() { Label = "QA private route", TargetKind = "Route", RouteKey = "/admin/site/preview" }), "QA private route"));
            var group = original.Revision.Menus.First(x => x.TargetKind == "Group");
            await Expect(400, () => f.Service.MutateAsync(f.Manager, original.Header.RowVersion, r => r.Menus.Single(x => x.Id == group.Id).ParentId = group.Id, "QA menu cycle"));
            var cycleId = Guid.NewGuid();
            await Expect(400, () => f.Service.MutateAsync(f.Manager, original.Header.RowVersion, r => r.Menus.Add(new() { Id = cycleId, ParentId = cycleId, Label = "QA non-source cycle", TargetKind = "Group", RouteKey = "" }), "QA additional menu cycle"));
            Assert((await f.State()).Revision.Id == original.Revision.Id, "A failed config validation left a committed draft.");
        }));
        yield return ("2B site additional category stays private until atomic publish", () => With(options, config, async f =>
        {
            var state = await f.State(); var id = Math.Max(await f.Db.Categories.MaxAsync(x => x.Id), state.Revision.Categories.Max(x => x.CategoryId)) + 1;
            var slug = "qa-site-" + Guid.NewGuid().ToString("N");
            var draft = await f.Service.MutateAsync(f.Manager, state.Header.RowVersion, r => { r.Categories.Add(new() { CategoryId = id, DisplayName = "QA additional category", Slug = slug, SortOrder = 100 }); r.Menus.Add(new() { Label = "QA category", TargetKind = "Category", CategoryId = id, Position = "footer" }); }, "QA add category");
            Assert(!await f.Db.Categories.AnyAsync(x => x.Id == id), "A new draft category materialized in the public identity table.");
            Assert((await f.Service.ReadAsync(true))!.Revision.Categories.All(x => x.CategoryId != id), "Public config contains a draft category.");
            var live = await f.Service.PublishAsync(f.Publisher, draft.Header.RowVersion, draft.Revision.Id);
            Assert(await f.Db.Categories.AnyAsync(x => x.Id == id && x.SourceUrl == "") && (await f.Service.ReadAsync(true))!.Revision.Categories.Any(x => x.CategoryId == id), "Publish did not atomically add a stable identity/live display.");
            var hidden = await f.Service.MutateAsync(f.Manager, live.Header.RowVersion, r => r.Categories.Single(x => x.CategoryId == id).ShowInNavigation = false, "QA navigation hidden draft");
            var before = await new PublicSiteService(f.Db, f.Service, new(f.Db), f.Media, new HttpContextAccessor()).GetAsync(); Assert(before!.Menus.Any(x => x.Url == "/chuyen-muc/" + slug), "Draft display flag changed public navigation.");
            await f.Service.PublishAsync(f.Publisher, hidden.Header.RowVersion, hidden.Revision.Id);
            var after = await new PublicSiteService(f.Db, f.Service, new(f.Db), f.Media, new HttpContextAccessor()).GetAsync(); Assert(after!.Menus.All(x => x.Url != "/chuyen-muc/" + slug), "Published navigation display flag had no effect.");
        }));
        yield return ("2B site media draft grant shared references expiry and scanner", () => With(options, config, async f =>
        {
            using var bitmap = new SKBitmap(2, 2); bitmap.Erase(new SKColor((uint)Random.Shared.NextInt64(0, uint.MaxValue) | 0xff000000)); using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            await using var input = new MemoryStream(encoded.ToArray()); var uploaded = await new MediaService(f.Db, f.Storage, f.Media).UploadAsync(input, "QA-site-banner-" + Guid.NewGuid() + ".png", "image/png", f.Manager.UserId, false);
            Assert(uploaded.Asset.ScanStatus == "NotScanned", "A local upload falsely became Clean.");
            var id = uploaded.Asset.Id; var now = DateTimeOffset.UtcNow; var original = await f.State();
            var draft = await f.Service.MutateAsync(f.Manager, original.Header.RowVersion, r => { r.LogoAssetId = id; r.Banners.Add(new() { MediaAssetId = id, Label = "QA banner", Alt = "QA image", RouteKey = "Home", Position = "home", EndUtc = now.AddMinutes(2) }); }, "QA private banner/logo");
            Assert(!await PublicSiteService.HasPublicAssetGrantAsync(f.Db, id, now), "Draft config leaked an asset grant.");
            var live = await f.Service.PublishAsync(f.Publisher, draft.Header.RowVersion, draft.Revision.Id);
            Assert(await PublicSiteService.HasPublicAssetGrantAsync(f.Db, id, now), "Live config did not grant media.");
            var logoOnly = await f.Service.MutateAsync(f.Manager, live.Header.RowVersion, r => r.Banners.RemoveAll(x => x.MediaAssetId == id), "QA remove one shared reference");
            var logoLive = await f.Service.PublishAsync(f.Publisher, logoOnly.Header.RowVersion, logoOnly.Revision.Id);
            Assert(await PublicSiteService.HasPublicAssetGrantAsync(f.Db, id, now.AddMinutes(3)), "Removing a banner revoked a shared live logo.");
            var expired = await f.Service.MutateAsync(f.Manager, logoLive.Header.RowVersion, r => { r.LogoAssetId = null; r.Banners.Add(new() { MediaAssetId = id, Label = "QA expired", Alt = "QA expired", RouteKey = "Home", EndUtc = now.AddSeconds(-1) }); }, "QA expired banner");
            var expiredLive = await f.Service.PublishAsync(f.Publisher, expired.Header.RowVersion, expired.Revision.Id);
            Assert(!await PublicSiteService.HasPublicAssetGrantAsync(f.Db, id, now), "Expired banner grants persisted without a worker.");
            var strict = new SiteConfigurationService(f.Db, f.Storage, MediaUploadOptions.FromConfiguration(config, "Production"));
            await Expect(400, () => strict.MutateAsync(f.Manager, expiredLive.Header.RowVersion, r => r.LogoAssetId = id, "QA production scanner"));
        }));
        yield return ("2B site removed public targets vanish from menus and pinned blocks", () => With(options, config, async f =>
        {
            var workflow = new WorkflowService(f.Db, new HtmlPolicy(), f.Media, f.Storage);
            var actor = new ContentActor(f.Manager.UserId, Permissions.All.ToHashSet(StringComparer.Ordinal));
            var content = await workflow.SaveAsync(null, new() { Title = "QA site target " + Guid.NewGuid(), BodyHtml = "<p>QA test only</p>", PrimaryCategoryId = 53 }, actor, null, Guid.NewGuid());
            content = await workflow.PublishDirectAsync(content.ContentId, content.CurrentRevisionId!.Value, actor, content.RowVersion, Guid.NewGuid());
            var state = await f.State(); var blockId = Guid.NewGuid();
            var draft = await f.Service.MutateAsync(f.Manager, state.Header.RowVersion, r => { r.Menus.Add(new() { Label = "QA public target", Position = "footer", TargetKind = "Content", ContentId = content.ContentId }); r.Blocks.Add(new() { Id = blockId, Title = "QA pins", Type = "Recent", Limit = 3, SortOrder = 900 }); r.Pins.Add(new() { BlockId = blockId, ContentId = content.ContentId }); }, "QA public target and pin");
            await f.Service.PublishAsync(f.Publisher, draft.Header.RowVersion, draft.Revision.Id);
            var web = new PublicSiteService(f.Db, f.Service, new(f.Db), f.Media, new HttpContextAccessor()); var site = await web.GetAsync();
            Assert(site!.Menus.Any(x => x.Label == "QA public target") && (await web.HomeAsync(site)).Blocks.Single(x => x.Block.Id == blockId).Items.Count(x => x.Id == content.ContentId) == 1, "Live target/pin missing or duplicated.");
            await workflow.UnpublishAsync(content.ContentId, actor, content.RowVersion, Guid.NewGuid());
            var fresh = new PublicSiteService(f.Db, f.Service, new(f.Db), f.Media, new HttpContextAccessor()); var after = await fresh.GetAsync();
            Assert(after!.Menus.All(x => x.Label != "QA public target") && (await fresh.HomeAsync(after)).Blocks.Single(x => x.Block.Id == blockId).Items.All(x => x.Id != content.ContentId), "An unpublished target leaked through menu/pins.");
        }));
        yield return ("2B site vocabulary draft rename and inactive identities retain history", () => With(options, config, async f =>
        {
            var state = await f.State(); var id = Guid.NewGuid();
            var draft = await f.Service.MutateAsync(f.Manager, state.Header.RowVersion, r => r.Vocabularies.Add(new() { Id = id, Kind = "Field", Label = "QA field " + id }), "QA vocabulary");
            Assert((await f.Service.ReadAsync(true))!.Revision.Vocabularies.All(x => x.Id != id), "Draft vocabulary leaked into live pickers.");
            var live = await f.Service.PublishAsync(f.Publisher, draft.Header.RowVersion, draft.Revision.Id);
            var edited = await f.Service.MutateAsync(f.Manager, live.Header.RowVersion, r => { var value = r.Vocabularies.Single(x => x.Id == id); value.Label = "QA renamed " + id; value.Active = false; }, "QA vocabulary rename");
            Assert((await f.Service.ReadAsync(true))!.Revision.Vocabularies.Single(x => x.Id == id).Label != edited.Revision.Vocabularies.Single(x => x.Id == id).Label, "Draft vocabulary rename changed live.");
            await Expect(400, () => f.Service.MutateAsync(f.Manager, edited.Header.RowVersion, r => r.Vocabularies.RemoveAll(x => x.Id == id), "QA hard delete"));
            await f.Service.PublishAsync(f.Publisher, edited.Header.RowVersion, edited.Revision.Id);
            Assert((await f.Service.ReadAsync(true))!.Revision.Vocabularies.Single(x => x.Id == id).Active == false, "Vocabulary inactive state did not publish.");
        }));
    }

    private static async Task With(DbContextOptions<PortalDbContext> options, IConfiguration config, Func<Fixture, Task> test)
    {
        await using var db = new PortalDbContext(options); var storage = StorageOptions.FromConfiguration(config);
        if (new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog != "LandPortal_Test" || !Path.GetFullPath(storage.MediaRoot).StartsWith(Path.Combine(PortalConfiguration.FindWorkspace(), ".local", "tests") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Site fixtures require verified SQL Test and private Test storage.");
        var media = MediaUploadOptions.FromConfiguration(config, "Development"); var service = new SiteConfigurationService(db, storage, media);
        await service.InitializeAsync("QA-site-explicit-initialize"); var original = (await service.ReadAsync(true))!;
        var f = new Fixture(db, service, storage, media);
        try { await test(f); }
        finally
        {
            db.ChangeTracker.Clear(); var state = (await service.ReadAsync(false))!;
            if (state.Header.LiveRevisionId != original.Revision.Id || state.Header.DraftRevisionId != original.Revision.Id)
            {
                var restored = await service.RestoreAsync(f.Manager, state.Header.RowVersion, original.Revision.Id);
                await service.PublishAsync(f.Publisher, restored.Header.RowVersion, restored.Revision.Id);
            }
        }
    }
    private sealed class Fixture(PortalDbContext db, SiteConfigurationService service, StorageOptions storage, MediaUploadOptions media)
    {
        public PortalDbContext Db => db; public SiteConfigurationService Service => service; public StorageOptions Storage => storage; public MediaUploadOptions Media => media;
        public ContentActor Manager { get; } = new("QA-site-manager-" + Guid.NewGuid(), new HashSet<string> { Permissions.ManageSite, Permissions.ViewSite, Permissions.ManageMedia });
        public ContentActor Publisher { get; } = new("QA-site-publisher-" + Guid.NewGuid(), new HashSet<string> { Permissions.PublishSite, Permissions.ViewSite });
        public async Task<SiteConfigurationState> State() => (await service.ReadAsync(false))!;
    }
    private static void Assert(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    private static async Task Expect(int status, Func<Task> operation) { try { await operation(); } catch (SiteConfigurationException error) when (error.StatusCode == status) { return; } throw new InvalidOperationException("Expected SiteConfigurationException " + status); }
}
