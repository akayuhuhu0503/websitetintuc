using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig.Writer;

internal static class EditorialZipChecks
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "LandPortal-Zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); Console.WriteLine($"PASS {++count}: {name}"); }
        async Task Reject(Func<Task> action, string name)
        {
            try { await action(); }
            catch (Exception e) when (e is MediaUploadException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            { Check(true, name); return; }
            throw new Exception("FAIL accepted " + name);
        }
        using var builder = new PdfDocumentBuilder(); builder.AddPage(100, 100); var pdf = builder.Build();
        byte[] Zip(params (string Name, byte[] Bytes)[] entries)
        {
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                foreach (var entry in entries) { using var stream = zip.CreateEntry(entry.Name).Open(); stream.Write(entry.Bytes); }
            return memory.ToArray();
        }
        async Task<string> Store(byte[] bytes)
        { var path = Path.Combine(root, Guid.NewGuid() + ".zip"); await File.WriteAllBytesAsync(path, bytes); return path; }
        async Task Invalid(byte[] bytes, string name) { var path = await Store(bytes); await Reject(() => EditorialZipPolicy.ValidateAsync(path), name); }
        var valid = Zip(("Tai lieu/ban-ve.pdf", pdf)); var path = await Store(valid);
        var inventory = await EditorialZipPolicy.ValidateAsync(path);
        Check(inventory.Count == 1 && inventory[0].Bytes == pdf.Length && inventory[0].Sha256 == Convert.ToHexStringLower(SHA256.HashData(pdf)), "real PDF parser, leaf SHA and size");
        Check(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))) == Convert.ToHexStringLower(SHA256.HashData(valid)), "original ZIP byte unchanged");
        Check(Directory.GetDirectories(root).Length == 0, "no ZIP entries extracted to filesystem");
        await Invalid("not a zip"u8.ToArray(), "fake ZIP");
        await Invalid(Zip(("fake.pdf", "%PDF-broken %%EOF"u8.ToArray())), "fake PDF inside ZIP");
        await Invalid(Zip(("../escape.pdf", pdf)), "path traversal");
        await Invalid(Zip(("C:/escape.pdf", pdf)), "absolute/drive path");
        await Invalid(Zip(("a\\b.pdf", pdf)), "Windows path");
        await Invalid(Zip(("CON.pdf", pdf)), "Windows device name");
        await Invalid(Zip(("same.pdf", pdf), ("SAME.pdf", pdf)), "case-insensitive duplicate");
        await Invalid(Zip(("same.pdf", pdf), ("same.pdf/other.pdf", pdf)), "file-directory collision");
        await Invalid(Zip(("run.exe", pdf)), "executable extension");
        await Invalid(Zip(("old.xls", pdf)), "legacy XLS outside allowed policy");
        await Invalid(Zip(("nested.zip", valid)), "nested ZIP outside allowed policy");
        await Invalid(Zip(("bomb.pdf", new byte[1024 * 1024])), "compression ratio bomb");
        await Invalid(Zip(Enumerable.Range(0, 257).Select(i => (i + ".pdf", pdf)).ToArray()), "entry count bound");
        var encrypted = valid.ToArray();
        var central = Find(encrypted, new byte[] { 0x50, 0x4b, 1, 2 });
        encrypted[6] |= 1; encrypted[central + 8] |= 1;
        await Invalid(encrypted, "encrypted archive");
        var symlink = valid.ToArray(); BitConverter.GetBytes(unchecked((int)0xa1ff0000)).CopyTo(symlink, central + 38);
        await Invalid(symlink, "symlink entry");
        var badCrc = valid.ToArray(); badCrc[14] ^= 1; badCrc[central + 16] ^= 1;
        await Invalid(badCrc, "CRC mismatch");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Reject(() => EditorialZipPolicy.ValidateAsync(path, canceled.Token), "cancellation honored");
        await Reject(() => { EditorialDocumentPolicy.RequireSize(EditorialZipPolicy.Maximum + 1, EditorialZipPolicy.Mime); return Task.CompletedTask; }, "ZIP size bound");
        Check(EditorialDocumentPolicy.MaximumFor("application/pdf") == 104L * 1024 * 1024 && EditorialDocumentPolicy.MaximumFor(OfficeDocumentPolicy.Xlsx) == 10L * 1024 * 1024, "PDF/Office limits unchanged");
        Check(!OfficeDocumentPolicy.IsDocument(EditorialZipPolicy.Mime) && EditorialDocumentPolicy.IsDocument(EditorialZipPolicy.Mime), "no widening of Office/source no-AV allowlist");
        var localMismatch = valid.ToArray(); localMismatch[30] ^= 1;
        await Invalid(localMismatch, "local header/central name mismatch");
        var zip64 = valid.ToArray(); var end = Find(zip64, new byte[] { 0x50, 0x4b, 5, 6 }); zip64[end + 10] = 255; zip64[end + 11] = 255;
        await Invalid(zip64, "ZIP64/huge directory rejected before entries allocation");
        using (var office = new MemoryStream())
        {
            using (var z = new ZipArchive(office, ZipArchiveMode.Create, true))
            {
                void Xml(string name, string value) { using var writer = new StreamWriter(z.CreateEntry(name).Open()); writer.Write(value); }
                Xml("[Content_Types].xml", "<Types><Override PartName=\"/word/document.xml\" ContentType=\"" + OfficeDocumentPolicy.Docx + ".main+xml\"/></Types>");
                Xml("_rels/.rels", "<Relationships><Relationship Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
                Xml("word/document.xml", "<document xmlns=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><body/></document>");
            }
            var officePath = await Store(Zip(("valid.docx", office.ToArray())));
            Check((await EditorialZipPolicy.ValidateAsync(officePath)).Count == 1, "Office leaf uses actual package/XML validator");
        }
        await Invalid(Zip(("fake.docx", pdf)), "Office filename cannot conceal PDF");
        var scanner = new FixtureScanner();
        var scanned = await EditorialZipPolicy.ScanAsync(path, scanner);
        Check(scanned.State == InquiryScanState.Clean && scanner.Calls == 2 && scanned.OutputSha256?.Length == 64, "test scanner must scan container and each leaf, with audit digest");
        scanner = new FixtureScanner { InfectedLeaf = true };
        Check((await EditorialZipPolicy.ScanAsync(path, scanner)).State == InquiryScanState.Infected, "infected leaf never becomes Clean");
        scanner = new FixtureScanner { Drift = true };
        Check((await EditorialZipPolicy.ScanAsync(path, scanner)).State == InquiryScanState.ScanFailed, "scanner version drift denied");

        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        await using var db = new FixtureDb(new DbContextOptionsBuilder<PortalDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var storage = new StorageOptions { MediaRoot = Path.Combine(root, "media"), TempRoot = Path.Combine(root, "temp"), SnapshotRoot = Path.Combine(root, "snapshots"), KeysRoot = Path.Combine(root, "keys") };
        Directory.CreateDirectory(storage.MediaRoot);
        var policy = new MediaUploadOptions { ApprovedManagedDocumentsWithoutAv = true, ApprovedManagedImageWithoutAv = true, AllowUnscannedDevelopmentUploads = true, IsDevelopmentLoopback = true };
        var rights = new CurrentPermissionService(db);
        db.Users.Add(new() { Id = "zip-fixture", UserName = "zip-fixture" });
        db.Users.Add(new() { Id = "zip-outsider", UserName = "zip-outsider" });
        foreach (var permission in new[] { Permissions.EditOwn, Permissions.ManageMedia }) db.UserClaims.Add(new IdentityUserClaim<string> { UserId = "zip-fixture", ClaimType = "permission", ClaimValue = permission });
        var content = new Content { OwnerUserId = "zip-fixture", Kind = ContentKind.Article };
        db.Contents.Add(content); await db.SaveChangesAsync();
        var media = new MediaService(db, storage, policy);
        var documents = new EditorialDocumentService(db, storage, media, rights, policy);
        var actor = await rights.GetAsync("zip-fixture");
        Check(!policy.CanServe(new MediaAsset { MimeType = EditorialZipPolicy.Mime, Origin = "Source", ScanStatus = "Clean", ValidationStatus = "Validated" }), "ZIP cannot enter through source/media no-AV policy");
        await Reject(async () => await documents.UploadAsync(content.Id, new MemoryStream(valid), "file.zip", EditorialZipPolicy.Mime, actor), "no scanner denies ZIP upload despite all no-AV flags");
        var outsider = await rights.GetAsync("zip-outsider");
        await Reject(async () => await documents.UploadAsync(content.Id, new MemoryStream(valid), "file.zip", EditorialZipPolicy.Mime, outsider), "unauthorized upload denied");
        Check(!await db.MediaAssets.AnyAsync(), "failed uploads persist no asset");
        var asset = new MediaAsset { Origin = "EditorialDocument", MimeType = EditorialZipPolicy.Mime, OriginalName = "file.zip", StorageKey = "fixture.zip", UploadedByUserId = actor.UserId,
            ByteLength = valid.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(valid)), ScanStatus = "NotScanned", ValidationStatus = MediaUploadOptions.ManagedDocumentNoAvValidation };
        var proof = new EditorialDocument { MediaAssetId = asset.Id, ContentId = content.Id, OwnerId = actor.UserId };
        db.MediaAssets.Add(asset); db.EditorialDocuments.Add(proof); await db.SaveChangesAsync();
        await File.WriteAllBytesAsync(Path.Combine(storage.MediaRoot, asset.StorageKey), valid);
        Check(!policy.CanServe(asset) && !policy.IsManagedDocument(asset), "forged Office no-AV state cannot serve ZIP");
        Check(await EditorialDocumentService.OpenVerifiedAsync(db, storage, asset, true, policy: policy) == null, "no-AV proof cannot open ZIP");
        // Explicit fixture-only scan evidence. This is not a real antivirus test or production receipt.
        asset.ScanStatus = "Clean"; asset.ValidationStatus = "Validated";
        Check(await EditorialDocumentService.OpenVerifiedAsync(db, storage, asset, true, policy: policy) == null, "Clean string without scan proof denied");
        proof.ScanState = InquiryScanState.Clean; proof.Attempts = 1; proof.EngineVersion = "FixtureScanner/NOT-ANTIVIRUS"; proof.ScannedSha256 = asset.Sha256; proof.ScannedUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await using (var opened = await EditorialDocumentService.OpenVerifiedAsync(db, storage, asset, true, policy: policy)) Check(opened != null, "matching fixture proof opens ZIP");
        var originalPdf = await documents.UploadAsync(content.Id, new MemoryStream(pdf), "existing.pdf", "application/pdf", actor);
        var draft = new ContentDraft { ContentId = content.Id }; db.ContentDrafts.Add(draft);
        var workflow = new WorkflowService(db, new HtmlPolicy(), policy, storage);
        var input = new DraftInput { Title = "Fixture", Kind = ContentKind.Article, Metadata = new() { PublicationDate = new DateOnly(2026, 7, 9) }, Assets = [new() { MediaAssetId = originalPdf.Id, Usage = "Attachment" }] };
        var apply = typeof(WorkflowService).GetMethod("ApplyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)apply.Invoke(workflow, [draft, input, actor, content, false])!; await db.SaveChangesAsync();
        input.Assets.Add(new() { MediaAssetId = asset.Id, Usage = "Attachment" });
        await (Task)apply.Invoke(workflow, [draft, input, actor, content, false])!; await db.SaveChangesAsync();
        Check(draft.Assets.Count == 2 && draft.Assets.Any(a => a.MediaAssetId == originalPdf.Id) && draft.Assets.Any(a => a.MediaAssetId == asset.Id)
            && draft.Metadata.PublicationDate == new DateOnly(2026, 7, 9) && draft.Title == "Fixture", "attach ZIP preserves original PDF/title/date using real draft validator");
        var controller = new MediaController(db, storage, policy, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Check(await controller.File(asset.Id) is NotFoundResult, "private draft ZIP never publicly downloadable");
        var revision = new ContentRevision { ContentId = content.Id, Title = "Fixture public grant", BodyHtml = "<p>Fixture</p>" };
        db.Revisions.Add(revision); await db.SaveChangesAsync();
        content.IsPublic = true; content.PublishedRevisionId = revision.Id;
        db.ContentAssets.Add(new() { RevisionId = revision.Id, MediaAssetId = asset.Id, Usage = "Attachment" }); await db.SaveChangesAsync();
        var response = await controller.File(asset.Id);
        Check(response is FileStreamResult f && f.FileDownloadName == "file.zip" && f.ContentType == EditorialZipPolicy.Mime
            && controller.Response.Headers["X-Content-Type-Options"] == "nosniff" && controller.Response.Headers["Content-Security-Policy"].ToString().Contains("sandbox"), "ZIP always attachment even without download query; sandbox/nosniff");
        if (response is FileStreamResult result) await result.FileStream.DisposeAsync();
        content.IsPublic = false; await db.SaveChangesAsync();
        Check(await controller.File(asset.Id) is NotFoundResult, "revoked publication denies ZIP");
        await File.AppendAllTextAsync(Path.Combine(storage.MediaRoot, asset.StorageKey), "tamper");
        Check(await EditorialDocumentService.OpenVerifiedAsync(db, storage, asset, true, policy: policy) == null, "hash tamper quarantined");
        Console.WriteLine($"ZIP TOTAL {count} PASS. Isolated SQLite/service/controller fixture; no real antivirus, SQL Server, CMS writes or IIS test. Artifacts: {root}");
    }
    private static int Find(byte[] bytes, byte[] pattern)
    { for (var i = 0; i <= bytes.Length - pattern.Length; i++) if (bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern)) return i; throw new Exception("Missing fixture header"); }
    private sealed class FixtureScanner : IInquiryScanner
    {
        public int Calls;
        public bool InfectedLeaf, Drift;
        public Task<(bool Ready, string Code)> HealthAsync(CancellationToken cancellation = default) => Task.FromResult((true, "TEST ONLY"));
        public async Task<InquiryScanResult> ScanAsync(Stream file, CancellationToken cancellation = default)
        {
            Calls++; var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellation));
            return new(Calls > 1 && InfectedLeaf ? InquiryScanState.Infected : InquiryScanState.Clean, "FIXTURE ONLY", Calls > 1 && Drift ? "Fixture/2" : "Fixture/1") { OutputSha256 = hash };
        }
    }
}
