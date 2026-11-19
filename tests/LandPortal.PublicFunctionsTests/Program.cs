using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Writer;

if (args.Length == 3 && args[0] == "introduction") { await IntroductionChecks.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "pdf-week") { await PdfWeekChecks.Run(args[1], args[2]); return; }
if (args.Length == 4 && args[0] == "integration") { await IntegrationChecks.Run(args[1], args[2], args[3]); return; }

// New isolated LocalDB only. Never load .env, operational config or credentials.
if (args.Length != 4 || !Regex.IsMatch(args[1], "^LPPublic_[a-f0-9]{12}$")) throw new ArgumentException("checkout, new LocalDB instance, new run directory, tool facts JSON required");
var root = Path.GetFullPath(args[0]); var run = Path.GetFullPath(args[2]);
if (!run.StartsWith(Path.Combine(root, ".local", "tests") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(run)) throw new Exception("New isolated run required");
Directory.CreateDirectory(run);
var cs = new SqlConnectionStringBuilder { DataSource = @"(localdb)\" + args[1], InitialCatalog = "LandPortal_Test", IntegratedSecurity = true, Encrypt = true, TrustServerCertificate = true };
var dbOptions = new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString, o => o.UseCompatibilityLevel(160)).Options;
var checks = new List<string>(); WebApplication? app = null;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); Console.WriteLine("PASS " + name); }
try
{
    await using (var sql = new SqlConnection(new SqlConnectionStringBuilder(cs.ConnectionString) { InitialCatalog = "master" }.ConnectionString))
    {
        await sql.OpenAsync(); using var cmd = sql.CreateCommand();
        cmd.CommandText = "SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB')),CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),DB_ID('LandPortal_Test')";
        using (var r = await cmd.ExecuteReaderAsync()) { await r.ReadAsync(); Check(r.GetInt32(0) == 1 && r.GetString(1).Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) && r.IsDBNull(2), "Fresh isolated local SQL target"); }
        cmd.CommandText = "CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'" + Path.Combine(run, "test.mdf").Replace("'", "''") + "') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'" + Path.Combine(run, "test.ldf").Replace("'", "''") + "')";
        await cmd.ExecuteNonQueryAsync();
    }
    await using (var db = new PortalDbContext(dbOptions))
    {
        await db.Database.MigrateAsync(); Check((await db.Database.GetAppliedMigrationsAsync()).Count() == 18, "Isolated SQL schema 18");
        for (var i = 1; i <= 22; i++) db.Categories.Add(new() { Id = i, Name = "Chuyên mục " + i, Slug = "chuyen-muc-" + i, SortOrder = i, ParentId = i == 22 ? 21 : i == 20 ? 19 : null });
        db.Users.Add(new() { Id = "fixture-admin", UserName = "fixture-admin", NormalizedUserName = "FIXTURE-ADMIN", DisplayName = "Test only" });
        foreach (var permission in new[] { Permissions.ManageSite, Permissions.PublishSite }) db.UserClaims.Add(new() { UserId = "fixture-admin", ClaimType = "permission", ClaimValue = permission });
        await db.SaveChangesAsync();
    }
    var storage = new StorageOptions { MediaRoot = Path.Combine(run, "media"), SnapshotRoot = Path.Combine(run, "snapshots"), TempRoot = Path.Combine(run, "temp"), KeysRoot = Path.Combine(run, "keys") };
    foreach (var p in new[] { storage.MediaRoot, storage.SnapshotRoot, storage.TempRoot, storage.KeysRoot }) Directory.CreateDirectory(p);
    var tools = JsonDocument.Parse(await File.ReadAllTextAsync(args[3])).RootElement;
    var videoOptions = new VideoOptions { Enabled = true, ApprovedManagedVideoWithoutAv = true, FfmpegPath = tools.GetProperty("ffmpegPath").GetString()!, FfprobePath = tools.GetProperty("ffprobePath").GetString()! };
    var mediaOptions = new MediaUploadOptions { ApprovedManagedDocumentsWithoutAv = true };
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = Path.Combine(root, "src", "Web"), WebRootPath = Path.Combine(root, "src", "Web", "wwwroot"), EnvironmentName = "Staging" });
    builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Public:BaseUrl"] = "http://127.0.0.1/" });
    builder.Logging.ClearProviders(); builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddScoped(_ => new PortalDbContext(dbOptions)); builder.Services.AddSingleton(storage); builder.Services.AddSingleton(mediaOptions); builder.Services.AddSingleton(videoOptions);
    builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()); builder.Services.AddHttpContextAccessor(); builder.Services.AddMemoryCache();
    builder.Services.AddScoped<PublicContentQuery>(); builder.Services.AddScoped<PublicSiteService>(); builder.Services.AddScoped<PublicNavigation>(); builder.Services.AddScoped<SiteConfigurationService>();
    builder.Services.AddScoped<CurrentPermissionService>(); builder.Services.AddScoped<ArticleDiscovery>(); builder.Services.AddScoped<DocumentUsage>(); builder.Services.AddScoped<PublicTraffic>(); builder.Services.AddSingleton<PublicUrlPolicy>();
    builder.Services.AddScoped<InteractionService>(); builder.Services.AddSingleton(new InteractionMailOptions());
    builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuth>("Fixture", _ => { });
    builder.Services.AddAuthorization(o => { foreach (var p in new[] { Permissions.ManageSite, Permissions.PublishSite }) o.AddPolicy(p, x => x.RequireClaim("permission", p)); });
    builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddApplicationPart(typeof(HomeController).Assembly);
    app = builder.Build();
    app.Services.GetRequiredService<IMemoryCache>().Set<PublicTrafficCounts?>("public-traffic-counts", null);
    app.Use(async (ctx, next) => { ctx.Request.Headers.UserAgent = "Public-functions-fixture-bot"; await next(); });
    // Match Web middleware order: static assets must run before the alias catch-all route.
    app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
    var url = app.Urls.Single();
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new() }) { BaseAddress = new(url) };
    var parser = new HtmlParser();
    async Task<string> Html(string path) { var r = await client.GetAsync(path); Check(r.StatusCode == HttpStatusCode.OK, "HTTP 200 " + path); return await r.Content.ReadAsStringAsync(); }
    var fallback = parser.ParseDocument(await Html("/"));
    Check(fallback.QuerySelectorAll(".main-nav a[href^='/chuyen-muc/']").Length == 22, "Fallback includes empty catalog categories and children");
    Check(fallback.QuerySelector(".main-nav a[href='/tai-lieu']") != null, "Fallback documents group includes Materials");
    Check((await Html("/tai-lieu")).Contains("Chưa có tài liệu phù hợp"), "Unmapped Materials is empty, no guessed ID");

    await using (var db = new PortalDbContext(dbOptions)) await new SiteConfigurationService(db, storage, mediaOptions).InitializeAsync("fixture-admin");
    client.DefaultRequestHeaders.Add("X-Fixture-Actor", "admin");
    var admin = parser.ParseDocument(await Html("/admin/site?section=menus"));
    var form = admin.QuerySelector("form[action='/admin/site/materials-menu']")!;
    var fields = form.QuerySelectorAll("input[name]").ToDictionary(x => x.GetAttribute("name")!, x => x.GetAttribute("value")!);
    using (var r = await client.PostAsync("/admin/site/materials-menu", new FormUrlEncodedContent(fields))) Check(r.StatusCode == HttpStatusCode.Redirect, "CMS creates Materials catalog/menu in draft via CSRF HTTP");
    using (var r = await client.PostAsync("/admin/site/materials-menu", new FormUrlEncodedContent(new Dictionary<string, string> { ["rowVersion"] = fields["rowVersion"] }))) Check(r.StatusCode == HttpStatusCode.BadRequest, "Missing CSRF denied");
    client.DefaultRequestHeaders.Remove("X-Fixture-Actor");
    using (var r = await client.PostAsync("/admin/site/materials-menu", new FormUrlEncodedContent(fields))) Check(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, "Unauthenticated CMS creation denied");
    Check(parser.ParseDocument(await Html("/")).QuerySelector(".main-nav a[href='/tai-lieu']") is null, "Draft Materials menu not visible before publication");
    await using (var db = new PortalDbContext(dbOptions))
    {
        var header = await db.Set<SiteConfiguration>().SingleAsync(); var liveId = header.LiveRevisionId;
        header.LiveRevisionId = null; await db.SaveChangesAsync();
        Check(!parser.ParseDocument(await Html("/")).QuerySelectorAll(".main-nav a[href]").Any(), "Draft-only site does not expose fallback catalog");
        header.LiveRevisionId = liveId; await db.SaveChangesAsync();
    }
    var actor = new ContentActor("fixture-admin", new HashSet<string> { Permissions.ManageSite, Permissions.PublishSite });
    int materialsId;
    await using (var db = new PortalDbContext(dbOptions))
    {
        var service = new SiteConfigurationService(db, storage, mediaOptions); var draft = (await service.ReadAsync(false))!;
        materialsId = draft.Revision.Menus.Single(x => x.RouteKey == "Materials").CategoryId!.Value;
        var updated = await service.MutateAsync(actor, draft.Header.RowVersion, r => { r.Categories.Single(x => x.CategoryId == 21).ShowInNavigation = false; }, "fixture hidden parent");
        await service.PublishAsync(actor, updated.Header.RowVersion, updated.Revision.Id);
        Check(await db.AuditLogs.AnyAsync(x => x.Action == "SitePublish"), "Materials uses versioned CMS publication and audit");
    }
    var configured = parser.ParseDocument(await Html("/"));
    Check(configured.QuerySelectorAll(".main-nav a[href^='/chuyen-muc/']").Length == 21, "Configured menu includes every visible category, even original SortOrder >=14");
    Check(!configured.QuerySelectorAll(".main-nav a[href]").Any(x => x.GetAttribute("href") is "/chuyen-muc/chuyen-muc-21" or "/chuyen-muc/chuyen-muc-22"), "Hidden parent and child excluded");
    Check(configured.QuerySelector(".main-nav a[href='/tai-lieu']") != null, "Published Materials menu present");
    var news = parser.ParseDocument(await Html("/tin-tuc"));
    Check(news.QuerySelectorAll(".category-directory a").Length == 21, "News page exposes full category directory");
    await Html("/chuyen-muc/chuyen-muc-18");
    using (var r = await client.GetAsync("/chuyen-muc/chuyen-muc-21")) Check(r.StatusCode == HttpStatusCode.NotFound, "Hidden category route denied");

    async Task<(Content, ContentRevision)> Content(ContentKind kind, int category, string title, bool published = true)
    {
        await using var db = new PortalDbContext(dbOptions);
        var c = new Content { Kind = kind, PrimaryCategoryId = category, OwnerUserId = "fixture-admin", PublishedAtUtc = DateTimeOffset.UtcNow };
        db.Contents.Add(c); await db.SaveChangesAsync();
        var r = new ContentRevision { ContentId = c.Id, PrimaryCategoryId = category, Title = title, Summary = "Nội dung thử nghiệm cô lập.", BodyHtml = "<p>Test only.</p>", SearchText = title.ToLowerInvariant(), ContentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(title))) };
        db.Revisions.Add(r); await db.SaveChangesAsync(); c.CurrentRevisionId = r.Id; c.PublishedRevisionId = published ? r.Id : null; c.IsPublic = published; c.State = published ? WorkflowState.Published : WorkflowState.Draft; await db.SaveChangesAsync(); return (c, r);
    }
    var publicDoc = await Content(ContentKind.Article, materialsId, "Tài liệu PDF công khai");
    var privateDoc = await Content(ContentKind.Article, materialsId, "Tài liệu nháp không công khai", false);
    var legal = await Content(ContentKind.LegalDocument, 1, "Văn bản pháp luật riêng");
    var draftDoc = await Content(ContentKind.DraftDocument, 1, "Văn bản dự thảo riêng");
    Guid pdfId = Guid.Empty;
    foreach (var (pair, text) in new[] { (publicDoc, "Public fixture"), (privateDoc, "Private fixture"), (legal, "Legal fixture") })
    {
        var pdf = new PdfDocumentBuilder(); pdf.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText(text, 12, new(20, 700), pdf.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica)); var bytes = pdf.Build();
        var id = Guid.NewGuid(); var key = "fixtures/" + id + ".pdf"; Directory.CreateDirectory(Path.Combine(storage.MediaRoot, "fixtures")); await File.WriteAllBytesAsync(storage.ResolveMedia(key), bytes);
        ManagedPdfPolicy.Validate(storage.ResolveMedia(key));
        await using var db = new PortalDbContext(dbOptions);
        db.MediaAssets.Add(new() { Id = id, StorageKey = key, OriginalName = text + ".pdf", MimeType = "application/pdf", ByteLength = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), Origin = "EditorialDocument", ScanStatus = "NotScanned", ValidationStatus = MediaUploadOptions.ManagedDocumentNoAvValidation });
        db.EditorialDocuments.Add(new() { MediaAssetId = id, ContentId = pair.Item1.Id, OwnerId = "fixture-admin" });
        db.ContentAssets.Add(new() { RevisionId = pair.Item2.Id, MediaAssetId = id, Usage = "Attachment" }); await db.SaveChangesAsync();
        if (pair == publicDoc) pdfId = id;
    }
    var materials = parser.ParseDocument(await Html("/tai-lieu"));
    Check(materials.QuerySelectorAll(".material-card").Length == 1 && materials.QuerySelector(".material-card h2")!.TextContent == publicDoc.Item2.Title, "Materials lists only its published revision and permitted document");
    using (var r = await client.GetAsync($"/media/{pdfId}?download=true&contentId={publicDoc.Item1.Id}")) Check(r.StatusCode == HttpStatusCode.OK && r.Content.Headers.ContentDisposition?.DispositionType == "attachment" && r.Headers.GetValues("X-Media-Scan-Status").Single() == "NotScanned", "Existing protected download works with truthful scan status");
    Check(parser.ParseDocument(await Html("/van-ban")).Body!.TextContent.Contains(legal.Item2.Title), "Legal group retained");
    Check(parser.ParseDocument(await Html("/du-thao")).Body!.TextContent.Contains(draftDoc.Item2.Title), "Draft documents group retained");

    var videoContent = await Content(ContentKind.Video, 1, "Video kiểm tra phát tại trang chủ");
    var originalPath = Path.Combine(storage.MediaRoot, "sample.mp4");
    var start = new ProcessStartInfo(videoOptions.FfmpegPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
    foreach (var a in new[] { "-nostdin", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=12", "-t", "3", "-c:v", "libx264", "-pix_fmt", "yuv420p", originalPath }) start.ArgumentList.Add(a);
    using (var process = Process.Start(start)!) { var error = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); Check(process.ExitCode == 0, "Generated one real MP4 fixture"); }
    var videoId = Guid.NewGuid(); var processor = new VideoProcessor(videoOptions, storage);
    var output = await processor.ProcessAsync(videoId, originalPath, forceTranscode: true);
    Check(VideoProcessor.BrowserCompatible(output.Probe), "Real FFmpeg MP4/poster and FFprobe output validation");
    await using (var db = new PortalDbContext(dbOptions))
    {
        var original = new MediaAsset { Id = videoId, StorageKey = "sample.mp4", OriginalName = "sample.mp4", MimeType = "video/mp4", ByteLength = new FileInfo(originalPath).Length, Sha256 = await VideoUploadService.HashFileAsync(originalPath), Origin = "VideoUpload", ScanStatus = "NotScanned", ValidationStatus = VideoOptions.NoAvValidated };
        var session = new VideoUploadSession { ContentId = videoContent.Item1.Id, OwnerUserId = "fixture-admin", State = VideoUploadState.Completed, MediaAssetId = videoId };
        db.MediaAssets.Add(original); db.Add(session); await db.SaveChangesAsync();
        db.Add(new VideoFile { MediaAssetId = videoId, UploadSessionId = session.Id, State = VideoState.Ready, Mp4Key = output.Mp4, Mp4Bytes = output.Mp4Bytes, Mp4Sha256 = output.Mp4Hash, PosterKey = output.Poster, PosterBytes = output.PosterBytes, PosterSha256 = output.PosterHash, VideoCodec = output.Probe.Codec, AudioCodec = output.Probe.Audio, DurationSeconds = output.Probe.Duration, ReadyUtc = DateTimeOffset.UtcNow });
        db.ContentAssets.Add(new() { RevisionId = videoContent.Item2.Id, MediaAssetId = videoId, Usage = "Video" }); await db.SaveChangesAsync();
    }
    Guid publicTrack = Guid.Empty, privateTrack = Guid.Empty;
    await using (var db = new PortalDbContext(dbOptions))
    {
        var sessionId = await db.Set<VideoFile>().Where(x => x.MediaAssetId == videoId).Select(x => x.UploadSessionId).SingleAsync();
        // Synthetic scan-proof fixtures only: no native scan or operational Clean claim.
        // Exercise the unchanged subtitle policy, including unscanned and wrong-revision denial.
        foreach (var state in new[] { "published", "draft", "unscanned" })
        {
            var id = Guid.NewGuid(); var key = "fixtures/" + id + ".vtt";
            var bytes = Encoding.UTF8.GetBytes("WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nFixture " + state + "\n"); await File.WriteAllBytesAsync(storage.ResolveMedia(key), bytes);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes)); var scanned = state != "unscanned";
            db.MediaAssets.Add(new() { Id = id, OriginalName = state + ".vtt", MimeType = "text/vtt", StorageKey = key, ByteLength = bytes.Length, Sha256 = hash, Origin = "VideoSubtitle", ScanStatus = scanned ? "Clean" : "NotScanned", ValidationStatus = "WebVttValidated" });
            db.Add(new VideoSubtitle { MediaAssetId = id, VideoMediaAssetId = videoId, UploadSessionId = sessionId, State = scanned ? InquiryScanState.Clean : InquiryScanState.NotScanned, LastCueSeconds = 1, ScannedSha256 = scanned ? hash : null, ScannedUtc = scanned ? DateTimeOffset.UtcNow : null, ScannerProvider = scanned ? "IsolatedFixture" : null, ScannerVersion = scanned ? "IsolatedFixture" : null });
            db.ContentAssets.Add(new() { RevisionId = state == "draft" ? privateDoc.Item2.Id : videoContent.Item2.Id, MediaAssetId = id, Usage = "Subtitle" });
            if (state == "published") publicTrack = id; if (state == "draft") privateTrack = id;
        }
        await db.SaveChangesAsync();
    }
    using (var r = await client.GetAsync($"/video-media/{videoId}/tracks?contentId={videoContent.Item1.Id}"))
    { var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); Check(r.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("tracks").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "/video-track/" + publicTrack }), "Lazy captions include only eligible track on published video revision"); }
    using (var r = await client.GetAsync($"/video-track/{publicTrack}")) Check(r.StatusCode == HttpStatusCode.OK, "Eligible subtitle served for technically validated no-AV video");
    using (var r = await client.GetAsync($"/video-track/{privateTrack}")) Check(r.StatusCode == HttpStatusCode.NotFound, "Draft subtitle cannot be downloaded");
    var home = parser.ParseDocument(await Html("/"));
    Check(home.QuerySelector("[data-home-video] button") != null && home.QuerySelector("[data-home-video] video") == null && !home.QuerySelectorAll("[src]").Any(x => x.GetAttribute("src") == $"/video-media/{videoId}/play"), "Homepage renders lazy play button, no MP4 source");
    using (var request = new HttpRequestMessage(HttpMethod.Get, $"/video-media/{videoId}/play")) { request.Headers.Range = new RangeHeaderValue(0, 31); using var r = await client.SendAsync(request); Check(r.StatusCode == HttpStatusCode.PartialContent && r.Content.Headers.ContentRange?.To == 31, "Real video HTTP Range 206"); }
    using (var r = await client.GetAsync($"/video-media/{videoId}/tracks?contentId={privateDoc.Item1.Id}")) Check(r.StatusCode == HttpStatusCode.NotFound, "Caption catalog cannot cross unpublished content scope");
    await using (var db = new PortalDbContext(dbOptions)) { await db.Set<VideoFile>().Where(x => x.MediaAssetId == videoId).ExecuteUpdateAsync(x => x.SetProperty(v => v.State, VideoState.Pending)); }
    using (var r = await client.GetAsync($"/video-media/{videoId}/play")) Check(r.StatusCode == HttpStatusCode.NotFound, "Pending video cannot stream");
    Check(!parser.ParseDocument(await Html("/")).QuerySelectorAll("[data-home-video]").Any(), "Pending video not shown");
    await using (var db = new PortalDbContext(dbOptions)) { await db.Set<VideoFile>().Where(x => x.MediaAssetId == videoId).ExecuteUpdateAsync(x => x.SetProperty(v => v.State, VideoState.Ready)); }
    var mp4Path = storage.ResolveMedia(output.Mp4); var mp4Bytes = await File.ReadAllBytesAsync(mp4Path); var modified = mp4Bytes.ToArray(); modified[^1] ^= 1; await File.WriteAllBytesAsync(mp4Path, modified);
    using (var r = await client.GetAsync($"/video-media/{videoId}/play")) Check(r.StatusCode == HttpStatusCode.NotFound, "Same-length tampered MP4 denied by real SHA check");
    await File.WriteAllBytesAsync(mp4Path, mp4Bytes);
    await using (var db = new PortalDbContext(dbOptions)) { await db.Contents.Where(x => x.Id == videoContent.Item1.Id).ExecuteUpdateAsync(x => x.SetProperty(c => c.IsPublic, false)); }
    using (var r = await client.GetAsync($"/video-media/{videoId}/play")) Check(r.StatusCode == HttpStatusCode.NotFound, "Revoked video cannot stream");
    Check(!parser.ParseDocument(await Html("/")).QuerySelectorAll("[data-home-video]").Any(), "Revoked video hidden from homepage");
    await using (var db = new PortalDbContext(dbOptions)) { await db.Contents.Where(x => x.Id == videoContent.Item1.Id).ExecuteUpdateAsync(x => x.SetProperty(c => c.IsPublic, true)); }
    await File.WriteAllTextAsync(Path.Combine(run, "ready.json"), JsonSerializer.Serialize(new { url, videoId, contentId = videoContent.Item1.Id, pdfId, checks }));
    Console.WriteLine("BROWSER_READY " + run);
    var deadline = DateTimeOffset.UtcNow.AddMinutes(15);
    while (!File.Exists(Path.Combine(run, "browser.done")) && DateTimeOffset.UtcNow < deadline) await Task.Delay(250);
    Check(File.Exists(Path.Combine(run, "browser.done")), "Focused browser checks completed");
    await File.WriteAllTextAsync(Path.Combine(run, "result.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, operationalSqlTouched = false }));
}
catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(run, "failure.txt"), error.ToString()); Console.WriteLine("FAIL " + error.GetType().Name + "; private failure.txt contains details"); Environment.ExitCode = 1; }
finally { if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); } }

sealed class FixtureAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers["X-Fixture-Actor"] == "admin"
        ? AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "fixture-admin"), new Claim("permission", Permissions.ManageSite), new Claim("permission", Permissions.PublishSite) }, "Fixture")), "Fixture"))
        : AuthenticateResult.NoResult());
}
