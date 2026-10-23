using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Tests;

public static class V05FinishTests
{
    public static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    public static async Task Denied<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    public static byte[] Word(DateOnly date, string label, bool wrongHeader = false, bool eicar = false)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XElement Cell(string text, string? merge = null) => new(w + "tc", merge == null ? null : new XElement(w + "tcPr", new XElement(w + "vMerge", new XAttribute(w + "val", merge))), new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", text))));
        var table = new XElement(w + "tbl", new XElement(w + "tr", (wrongHeader ? new[] { "Sai mẫu" } : new[] { "Ngày", "Buổi", "Giờ", "Nội dung", "Chủ trì", "Thành phần", "Địa điểm", "Ghi chú" }).Select(x => Cell(x))),
            new XElement(w + "tr", Cell(date.ToString("dd/MM/yyyy"), "restart"), Cell("Sáng", "restart"), Cell("08:00"), Cell(label), Cell("QA chủ trì"), Cell("QA thành phần"), Cell("Phòng Test"), Cell("Technical only")),
            new XElement(w + "tr", Cell("", "continue"), Cell("", "continue"), Cell("09:00"), Cell("Dòng ô gộp " + label), Cell("QA"), Cell("Test"), Cell("Local"), Cell("")));
        using var memory = new MemoryStream(); using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            void Add(string name, string value) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(value); }
            Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add("word/document.xml", new XDocument(new XElement(w + "document", new XElement(w + "body", table))).ToString());
            Add("docProps/core.xml", "<properties>Anonymous technical fixture " + Guid.NewGuid() + "</properties>");
            if (eicar) { var entry = zip.CreateEntry("eicar.com"); using var stream = entry.Open(); stream.Write(ScannerTests.Eicar()); }
        }
        return memory.ToArray();
    }
    public static async Task<WorkflowResult> Weekly(PortalDbContext db, IConfiguration config, ContentActor actor, int year, int week)
    {
        var input = Phase2aSqlTests.Input("V05 Finish technical schedule " + Guid.NewGuid(), 76); input.Kind = ContentKind.StaticPage;
        input.Metadata = new() { WeeklyYear = year, WeeklyWeek = week, WeeklyRowsJson = JsonSerializer.Serialize(new[] { new WeeklyScheduleRow { Date = DateOnly.FromDateTime(ISOWeek.ToDateTime(year, week, DayOfWeek.Monday)), Period = "Sáng", Content = "Manual technical fixture" } }) };
        return await new WorkflowService(db, new(), MediaUploadOptions.FromConfiguration(config, "Development"), StorageOptions.FromConfiguration(config)).SaveAsync(null, input, actor, null, Guid.NewGuid());
    }
    public static EditorialDocumentService Documents(PortalDbContext db, IConfiguration config) => new(db, StorageOptions.FromConfiguration(config), new(db, StorageOptions.FromConfiguration(config), MediaUploadOptions.FromConfiguration(config, "Development")), new(db));
    private sealed class DelayedFailure : IInquiryScanner
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<(bool Ready,string Code)> HealthAsync(CancellationToken ct = default) => Task.FromResult((false, "Failure-only fence test; never Clean"));
        public async Task<InquiryScanResult> ScanAsync(Stream file, CancellationToken ct = default) { Entered.SetResult(); await Release.Task.WaitAsync(ct); return new(InquiryScanState.ScanFailed, "DelayedFailureOnly", null); }
    }
    public static async Task<Guid[]> SeedAnswers(PortalDbContext db, IConfiguration config, string marker)
    {
        var protection = DataProtectionProvider.Create(new DirectoryInfo(StorageOptions.FromConfiguration(config).KeysRoot), b => b.SetApplicationName("LandPortal"));
        var service = new InteractionService(db, protection, new(), new(db)); var ids = new List<Guid>();
        for (var i = 0; i < 25; i++)
        {
            var input = new InteractionInput { Kind = InteractionKind.Inquiry, Name = "Anonymous QA", Email = "private@landportal.invalid", Title = "Private title", Text = "PRIVATE ORIGINAL MUST NOT SEARCH", PublicConsent = true, Field = "Đất đai" };
            var s = new InteractionSubmission { ChallengeId = Guid.NewGuid(), Kind = i == 24 ? InteractionKind.Contact : InteractionKind.Inquiry, ProtectedOriginal = service.Protect(input), EmailHash = TextNormalizer.Hash(input.Email), ReceiptHash = TextNormalizer.Hash(Guid.NewGuid().ToString()), CreatedUtc = DateTimeOffset.UtcNow, State = i == 23 ? InteractionState.Rejected : InteractionState.Answered, PublicConsent = i != 22 };
            db.InteractionSubmissions.Add(s); await db.SaveChangesAsync();
            var p = new InteractionPublicSnapshot { SubmissionId = s.Id, DisplayName = "Anonymous QA", Text = marker + " Đất đai " + i, AgencyAnswer = "Câu trả lời công khai đã duyệt", ApprovedBy = "QA", ApprovedUtc = DateTimeOffset.UtcNow.AddMinutes(-i) };
            db.InteractionPublicSnapshots.Add(p); await db.SaveChangesAsync();
            if (i != 21) s.PublicSnapshotId = p.Id; // Old snapshot without current pointer must never search.
            await db.SaveChangesAsync(); if (i < 21) ids.Add(p.Id);
        }
        return ids.ToArray();
    }
    public static IEnumerable<(string Name, Func<Task> Test)> Cases(string root, DbContextOptions<PortalDbContext> options, IConfiguration config)
    {
        yield return ("V05-Finish FR17 genuine PDF Excel clean proof exact approval publication draft isolation and approval invalidation", async () =>
        {
            await using var db = new PortalDbContext(options); var actor = await new CurrentPermissionService(db).GetAsync(await InquiryFinishTests.User(db, Permissions.All, "Finish FR17 documents QA"));
            var wf = new WorkflowService(db, new(), MediaUploadOptions.FromConfiguration(config, "Development"), StorageOptions.FromConfiguration(config));
            var input = Phase2aSqlTests.Input("Finish FR17 file fixture " + Guid.NewGuid());
            input.Metadata = new() { DisclosureGroupId = "giay-chung-nhan-sai-hong-huy", Province = "Anonymous Test province", ProvinceCode = "QA", DisclosureYear = 2026, PublicationSource = "Anonymous Test fixture", LegalBasisReference = "Technical only", PublicationReviewEvidence = "QA reviewed, not official content" };
            var saved = await wf.SaveAsync(null, input, actor, null, Guid.NewGuid());
            var source = await db.MediaAssets.AsNoTracking().FirstAsync(x => x.SourceUrl != "" && x.MimeType == "application/pdf");
            var pdf = (await File.ReadAllBytesAsync(StorageOptions.FromConfiguration(config).ResolveMedia(source.StorageKey))).Concat(Encoding.ASCII.GetBytes("\n% FR17 anonymous fixture " + Guid.NewGuid())).ToArray();
            var excel = InquiryExcelService.Workbook([new[] { "FR17 anonymous fixture " + Guid.NewGuid() }, new[] { "=literal text only" }]);
            var scanner = InquiryScannerFactory.Create(ScannerTests.DefenderConfig(config, new[] { ScannerTests.Hash(pdf), ScannerTests.Hash(excel) }), "Development");
            var pdfAsset = await Documents(db, config).UploadAsync(saved.ContentId, new MemoryStream(pdf), "fr17.pdf", "application/pdf", actor);
            var excelAsset = await Documents(db, config).UploadAsync(saved.ContentId, new MemoryStream(excel), "fr17.xlsx", OfficeDocumentPolicy.Xlsx, actor);
            input.Assets = [new() { MediaAssetId = pdfAsset.Id, Usage = "Attachment" }, new() { MediaAssetId = excelAsset.Id, Usage = "Attachment" }];
            await Denied<InvalidOperationException>(() => wf.SaveAsync(saved.ContentId, input, actor, saved.RowVersion, Guid.NewGuid())); db.ChangeTracker.Clear();
            await Documents(db, config).ScanOnceAsync(scanner, pdfAsset.Id); await Documents(db, config).ScanOnceAsync(scanner, excelAsset.Id);
            saved = await wf.SaveAsync(saved.ContentId, input, actor, saved.RowVersion, Guid.NewGuid());
            saved = await wf.SubmitAsync(saved.ContentId, saved.CurrentRevisionId, actor, saved.RowVersion, Guid.NewGuid()); saved = await wf.ApproveAsync(saved.ContentId, saved.CurrentRevisionId!.Value, actor, saved.RowVersion, Guid.NewGuid());
            saved = await wf.PublishApprovedAsync(saved.ContentId, saved.CurrentRevisionId!.Value, actor, saved.RowVersion, Guid.NewGuid()); var published = saved.PublishedRevisionId;
            saved = await wf.BeginEditAsync(saved.ContentId, actor, saved.RowVersion, Guid.NewGuid()); input.Title += " private draft"; input.Metadata.Province = "Private province change";
            saved = await wf.SaveAsync(saved.ContentId, input, actor, saved.RowVersion, Guid.NewGuid());
            Check(saved.PublishedRevisionId == published && !await new PublicContentQuery(db).Filter(new PublicContentQuery(db).Published(), "Private province change").AnyAsync(x => x.Id == saved.ContentId), "Document draft changed public metadata.");
            await Denied<InvalidOperationException>(() => wf.PublishApprovedAsync(saved.ContentId, saved.CurrentRevisionId!.Value, actor, saved.RowVersion, Guid.NewGuid()));
            var dir = TestEvidencePaths.At(root, "v05-finish-fr17"); Directory.CreateDirectory(dir);
            var proof = await db.EditorialDocuments.AsNoTracking().Where(x => x.MediaAssetId == pdfAsset.Id || x.MediaAssetId == excelAsset.Id).Select(x => new { x.MediaAssetId, x.ScanState, x.EngineVersion, x.ScannedSha256, x.ScannedUtc }).ToListAsync();
            Check(proof.Count == 2 && proof.All(x => x.ScanState == InquiryScanState.Clean && x.EngineVersion!.StartsWith("Defender/")), "No genuine FR17 proof.");
            await File.WriteAllTextAsync(Path.Combine(dir, "publication.json"), JsonSerializer.Serialize(new { saved.ContentId, publishedRevision = published, currentDraft = saved.CurrentRevisionId, proof, approvalReuse = "DENIED" }, new JsonSerializerOptions { WriteIndented = true }));
        });
        yield return ("V05-Finish document SQL competing claims late lease result retry bound owner revocation fail closed without Clean mocks", async () =>
        {
            await using var db = new PortalDbContext(options); var actor = await new CurrentPermissionService(db).GetAsync(await InquiryFinishTests.User(db, Permissions.All, "Finish fence QA"));
            var schedule = await Weekly(db, config, actor, 2096, 12); var bytes = Word(DateOnly.FromDateTime(ISOWeek.ToDateTime(2096, 12, DayOfWeek.Monday)), "Failure-only lease fixture " + Guid.NewGuid());
            var asset = await Documents(db, config).UploadAsync(schedule.ContentId, new MemoryStream(bytes), "fence.docx", OfficeDocumentPolicy.Docx, actor);
            var offline = new ClamdInquiryScanner(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Scanner:ClamdPort"] = "1" }).Build());
            async Task<bool> Claim() { await using var c = new PortalDbContext(options); return await Documents(c, config).ScanOnceAsync(offline, asset.Id); }
            Check((await Task.WhenAll(Claim(), Claim())).Count(x => x) == 1, "Competing workers claimed the same attempt.");
            for (var i = 0; i < 2; i++) { await db.EditorialDocuments.Where(x => x.MediaAssetId == asset.Id).ExecuteUpdateAsync(x => x.SetProperty(d => d.AvailableUtc, DateTimeOffset.UtcNow.AddSeconds(-1))); await Claim(); }
            Check(!await Claim(), "Document retry exceeded bound3."); db.ChangeTracker.Clear(); var record = await db.EditorialDocuments.AsNoTracking().SingleAsync(x => x.MediaAssetId == asset.Id);
            Check(record.Attempts == 3 && record.ScanState == InquiryScanState.ScanFailed && record.ScannedSha256 == null, "Failed scanner supplied Clean proof.");
            var late = await Documents(db, config).UploadAsync(schedule.ContentId, new MemoryStream(Word(new(2096,3,19), "Late fixture " + Guid.NewGuid())), "late.docx", OfficeDocumentPolicy.Docx, actor);
            var delayed = new DelayedFailure(); await using (var c = new PortalDbContext(options))
            {
                var pending = Documents(c, config).ScanOnceAsync(delayed, late.Id); await delayed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await db.EditorialDocuments.Where(x => x.MediaAssetId == late.Id).ExecuteUpdateAsync(x => x.SetProperty(d => d.LeaseToken, Guid.NewGuid())); delayed.Release.SetResult();
                await Denied<DbUpdateConcurrencyException>(async () => await pending);
            }
            var revoked = await Documents(db, config).UploadAsync(schedule.ContentId, new MemoryStream(Word(new(2096,3,19), "Revoked fixture " + Guid.NewGuid())), "revoked.docx", OfficeDocumentPolicy.Docx, actor);
            await db.UserClaims.Where(x => x.UserId == actor.UserId && x.ClaimType == "permission" && x.ClaimValue == Permissions.ManageMedia).ExecuteDeleteAsync();
            await Documents(db, config).ScanOnceAsync(offline, revoked.Id); db.ChangeTracker.Clear(); Check((await db.EditorialDocuments.SingleAsync(x => x.MediaAssetId == revoked.Id)).ScanState == InquiryScanState.ScanFailed, "Worker ignored current permission.");
            var dir = TestEvidencePaths.At(root, "v05-finish-fences"); Directory.CreateDirectory(dir); await File.WriteAllTextAsync(Path.Combine(dir, "report.json"), JsonSerializer.Serialize(new { competing = "one claim", maximumAttempts = record.Attempts, lateResult = "DENIED", revokedOwner = "ScanFailed", cleanMocks = 0 }));
        });
        yield return ("V05-Finish SQL mixed search Vietnamese accent/d paging filters excludes private no-consent rejected stale/contact snapshots", async () =>
        {
            await using var db = new PortalDbContext(options); var marker = "FQA" + Guid.NewGuid().ToString("N"); var ids = await SeedAnswers(db, config, marker);
            var query = new GeneralSearchQuery(db, new(db));
            var dir = TestEvidencePaths.At(root, "v05-finish-search"); Directory.CreateDirectory(dir);
            var accented = query.Rows(marker + " Đất đai", null, null, null, null); var plain = query.Rows(marker + " dat dai", null, null, null, null);
            await File.WriteAllTextAsync(Path.Combine(dir, "count-query.sql"), accented.ToQueryString());
            Check(await accented.CountAsync() == 21 && await plain.CountAsync() == 21, "Vietnamese or public-only projection failed.");
            var pages = new List<Guid>(); for (var i = 0; i < 3; i++) pages.AddRange(await query.Order(plain, "recent", null).Skip(i * 10).Take(10).Select(x => x.Id).ToArrayAsync());
            Check(pages.Count == 21 && pages.Distinct().Count() == 21 && pages.ToHashSet().SetEquals(ids), "SQL paging lost or duplicated answers.");
            Check(!await query.Rows(marker, "Article", null, null, null).AnyAsync() && !await query.Rows(marker, "Inquiry", 75, null, null).AnyAsync() && !await query.Rows(marker, null, null, new(2200, 1, 1), null).AnyAsync(), "Type/category/date filters leaked answers.");
            Check(!await query.Rows("PRIVATE ORIGINAL MUST NOT SEARCH", null, null, null, null).AnyAsync(), "Protected original queried.");
            foreach (var sort in new[] { "title", "oldest", "relevant" }) Check(await query.Order(plain, sort, marker).Take(10).CountAsync() == 10, "SQL sort did not translate.");
            var d = TestEvidencePaths.At(root, "v05-finish-search"); Directory.CreateDirectory(d); await File.WriteAllTextAsync(Path.Combine(d, "sql-paging.sql"), query.Order(plain, "recent", null).Skip(10).Take(10).ToQueryString());
        });
        yield return ("V05-Finish SQL FR17 menu template immutable draft live approval filter retains source categories", async () =>
        {
            await using var db = new PortalDbContext(options); var actor = await new CurrentPermissionService(db).GetAsync(await InquiryFinishTests.User(db, Permissions.All, "Finish menu QA"));
            var service = new SiteConfigurationService(db, StorageOptions.FromConfiguration(config), MediaUploadOptions.FromConfiguration(config, "Development")); var state = (await service.ReadAsync(false))!;
            // Later real cached-source staging introduced categories 59/60 on Test.
            // Explicit Test configuration reconciliation retains exact source fields;
            // no production validator or protected-category guard is bypassed.
            var missingCategories=await db.Categories.AsNoTracking().Where(x=>x.SourceUrl!="").ToListAsync();
            var categoryIds = state.Revision.Categories.Select(x => x.CategoryId).Union(missingCategories.Select(x=>x.Id)).Order().ToArray(); var oldLive = state.Header.LiveRevisionId;
            state = await service.MutateAsync(actor, state.Header.RowVersion, r=>{foreach(var c in missingCategories.Where(c=>r.Categories.All(x=>x.CategoryId!=c.Id)))r.Categories.Add(new(){CategoryId=c.Id,DisplayName=c.Name,Slug=c.Slug,ParentCategoryId=c.ParentId,SortOrder=c.SortOrder,ShowInNavigation=true});SiteConfigurationService.AddDisclosureMenu(r);}, "Finish template Test with exact protected source catalog");
            Check(state.Header.LiveRevisionId == oldLive && state.Revision.Menus.Count(x => x.RouteKey.StartsWith("Disclosure")) == 4 && state.Revision.Categories.Select(x => x.CategoryId).Order().SequenceEqual(categoryIds), "Menu draft changed live/source or lost group.");
            state = await service.MutateAsync(actor, state.Header.RowVersion, SiteConfigurationService.AddDisclosureMenu, "Finish idempotence");
            Check(state.Revision.Menus.Count(x => x.RouteKey.StartsWith("Disclosure")) == 4, "Template duplicated targets.");
            var publicService = new PublicSiteService(db, service, new(db), MediaUploadOptions.FromConfiguration(config, "Development"), new Microsoft.AspNetCore.Http.HttpContextAccessor());
            var preview = await publicService.BuildAsync(state.Revision, true);
            Check(preview.Menus.Any(x => x.Url == "/cong-khai-thong-tin") && preview.Menus.Count(x => x.Url?.StartsWith("/cong-khai-thong-tin/") == true) == 3 && preview.Menus.Any(x => x.Label == "Người nước ngoài định cư ở Việt Nam"), "SRS 1.1 official menu missing; content approval remains separately enforced.");
            await service.PublishAsync(actor, state.Header.RowVersion, state.Revision.Id);
            Check((await service.ReadAsync(true))!.Revision.Id == state.Revision.Id, "Template publication failed.");
            await Denied<SiteConfigurationException>(() => service.MutateAsync(new("no-rights", new HashSet<string>()), state.Header.RowVersion, SiteConfigurationService.AddDisclosureMenu, "denied"));
        });
        yield return ("V05-Finish genuine document scanner DOCX XLSX PDF Clean EICAR Infected failure quarantine exact-byte proof and tamper", async () =>
        {
            EditorialDocumentService.RequireTestInfrastructure(config, "Development");
            foreach (var env in new[] { "Production", "Staging" }) await Denied<InvalidOperationException>(() => { EditorialDocumentService.RequireTestInfrastructure(config, env); return Task.CompletedTask; });
            await using var db = new PortalDbContext(options); var actor = await new CurrentPermissionService(db).GetAsync(await InquiryFinishTests.User(db, Permissions.All, "Finish scanner QA"));
            var schedule = await Weekly(db, config, actor, 2098, 20); var date = DateOnly.FromDateTime(ISOWeek.ToDateTime(2098, 20, DayOfWeek.Monday));
            var source = await db.MediaAssets.FirstAsync(x => x.SourceUrl != "" && x.MimeType == "application/pdf");
            // A distinct, anonymous valid PDF avoids reusing/changing any source identity.
            var pdf = (await File.ReadAllBytesAsync(StorageOptions.FromConfiguration(config).ResolveMedia(source.StorageKey))).Concat(Encoding.ASCII.GetBytes("\n% Anonymous QA " + Guid.NewGuid())).ToArray();
            var word = Word(date, "Technical document " + Guid.NewGuid()); var excel = InquiryExcelService.Workbook([new[] { "Anonymous technical fixture " + Guid.NewGuid() }, new[] { "=literal text, not a formula" }]); var eicar = Word(date, "Safe EICAR test", eicar: true);
            var scanner = InquiryScannerFactory.Create(ScannerTests.DefenderConfig(config, new[] { word, excel, pdf, eicar }.Select(ScannerTests.Hash)), "Development"); Check((await scanner.HealthAsync()).Ready, "Genuine engine not available; cannot PASS.");
            var evidence = new List<object>();
            foreach (var (bytes, name, expected) in new[] { (word, "technical.docx", InquiryScanState.Clean), (excel, "technical.xlsx", InquiryScanState.Clean), (pdf, "anonymous.pdf", InquiryScanState.Clean), (eicar, "eicar-technical.docx", InquiryScanState.Infected) })
            {
                var a = await Documents(db, config).UploadAsync(schedule.ContentId, new MemoryStream(bytes), name, OfficeDocumentPolicy.Mime(name), actor);
                Check(a.ScanStatus == "NotScanned" && a.IsQuarantined && await EditorialDocumentService.OpenVerifiedAsync(db, StorageOptions.FromConfiguration(config), a, true) == null, "Pending bytes readable.");
                await Documents(db, config).ScanOnceAsync(scanner, a.Id); db.ChangeTracker.Clear();
                var proof = await db.EditorialDocuments.AsNoTracking().SingleAsync(x => x.MediaAssetId == a.Id); a = await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == a.Id);
                Check(proof.ScanState == expected && proof.ScannedSha256 == ScannerTests.Hash(bytes) && proof.EngineVersion!.StartsWith("Defender/"), "Missing genuine document conclusion: " + name + " " + proof.Error);
                await using var stream = await EditorialDocumentService.OpenVerifiedAsync(db, StorageOptions.FromConfiguration(config), a, true);
                Check((stream != null) == (expected == InquiryScanState.Clean), "Quarantine or proof bypassed.");
                Check(!await Documents(db, config).ScanOnceAsync(scanner, a.Id), "Terminal result retried.");
                evidence.Add(new { a.Id, name, a.Sha256, proof.ScannedSha256, a.ByteLength, state = proof.ScanState.ToString(), proof.EngineVersion, proof.ScannedUtc });
            }
            var failed = await Documents(db, config).UploadAsync(schedule.ContentId, new MemoryStream(Word(date, "Offline " + Guid.NewGuid())), "offline.docx", OfficeDocumentPolicy.Docx, actor);
            await Documents(db, config).ScanOnceAsync(new ClamdInquiryScanner(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Scanner:ClamdPort"] = "1", ["Scanner:TimeoutSeconds"] = "1" }).Build()), failed.Id);
            db.ChangeTracker.Clear(); Check((await db.EditorialDocuments.SingleAsync(x => x.MediaAssetId == failed.Id)).ScanState == InquiryScanState.ScanFailed, "Offline scanner fail-open.");
            var clean = await db.MediaAssets.AsNoTracking().FirstAsync(x => x.Id == (Guid)evidence[0].GetType().GetProperty("Id")!.GetValue(evidence[0])!);
            await File.AppendAllTextAsync(StorageOptions.FromConfiguration(config).ResolveMedia(clean.StorageKey), "tamper");
            Check(await EditorialDocumentService.OpenVerifiedAsync(db, StorageOptions.FromConfiguration(config), clean, true) == null, "Tampered Clean bytes served.");
            var dir = TestEvidencePaths.At(root, "v05-finish-documents"); Directory.CreateDirectory(dir); await File.WriteAllTextAsync(Path.Combine(dir, "native-scan.json"), JsonSerializer.Serialize(new { provider = "Real Microsoft Defender; anonymous Test SHA only", evidence, failure = "ScanFailed", tamper = "DENIED", AC161 = "BLOCKED:NO_REAL_WORD_SAMPLES" }, new JsonSerializerOptions { WriteIndented = true }));
        });
    }
}
