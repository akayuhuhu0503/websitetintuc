using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

namespace LandPortal.Tests;

public static class MediaValidationTests
{
    public static async Task RunAsync(string root, IConfiguration config)
    {
        if (new SqlConnectionStringBuilder(DatabaseSettings.Connection(config)).InitialCatalog != "LandPortal_Test")
            throw new InvalidOperationException("Media validation tests require exactly LandPortal_Test.");
        var testRoot = Path.Combine(root, ".local", "phase2a-media-tests", Guid.NewGuid().ToString("N"));
        var storage = new StorageOptions { MediaRoot = Path.Combine(testRoot, "media"), TempRoot = Path.Combine(testRoot, "temp"),
            KeysRoot = Path.Combine(testRoot, "keys"), SnapshotRoot = Path.Combine(testRoot, "snapshots") };
        Directory.CreateDirectory(storage.MediaRoot);
        var configOverride = new Dictionary<string, string?> { ["Public:BaseUrl"] = "http://127.0.0.1:5189", ["Media:AllowUnscannedDevelopmentUploads"] = "true",
            ["Media:MaxImageBytes"] = "16777216", ["Media:MaxPdfBytes"] = "33554432", ["Media:MaxImagePixels"] = "30000000",
            ["Media:MaxDecodedFramePixels"] = "80000000", ["Media:MaxImageFrames"] = "100", ["Media:MaxPdfPages"] = "1000" };
        var development = new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(configOverride).Build();
        var settings = MediaUploadOptions.FromConfiguration(development, "Development");
        await using var db = new PortalDbContext(DatabaseSettings.Options(config, forMigration: true));
        var service = new MediaService(db, storage, settings);
        var owner = "QA-MEDIA-" + Guid.NewGuid().ToString("N");
        var cases = new List<object>();
        string status = "FAIL";
        void Check(bool condition, string explanation) { if (!condition) throw new InvalidOperationException(explanation); }
        async Task Case(string name, Func<Task> run)
        {
            try { await run(); cases.Add(new { name, status = "PASS" }); }
            catch (Exception exception) { cases.Add(new { name, status = "FAIL", error = exception.GetType().Name }); throw; }
        }
        async Task Rejected(Func<Task> run, int expectedStatus)
        {
            try { await run(); }
            catch (MediaUploadException error) { Check(error.StatusCode == expectedStatus, "Upload rejection status differs from contract."); return; }
            throw new InvalidOperationException("Invalid upload was accepted.");
        }
        static async Task<string> HashFile(string path)
        { await using var file = System.IO.File.OpenRead(path); return Convert.ToHexStringLower(await SHA256.HashDataAsync(file)); }
        async Task EnsureTestCopy(string originalPath)
        {
            var digest = await HashFile(originalPath);
            var existing = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Sha256 == digest);
            if (existing is null) return;
            var copy = storage.ResolveMedia(existing.StorageKey);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            System.IO.File.Copy(originalPath, copy, false);
        }
        try
        {
            // Only read the preserved project originals. Every write goes to isolated Test SQL/storage.
            var preservedRoot = Path.Combine(root, ".local", "media");
            var sourceImage = Directory.EnumerateFiles(preservedRoot, "*.jpg", SearchOption.AllDirectories).Order().First();
            var sourcePdf = Directory.EnumerateFiles(preservedRoot, "*.pdf", SearchOption.AllDirectories).Order().First();
            var originalImageHash = await HashFile(sourceImage);
            var originalPdfHash = await HashFile(sourcePdf);
            await EnsureTestCopy(sourceImage); await EnsureTestCopy(sourcePdf);
            using var bitmap = SKBitmap.Decode(sourceImage) ?? throw new InvalidOperationException("Actual source JPEG could not decode in the test fixture.");
            var random = Guid.NewGuid().ToByteArray();
            bitmap.SetPixel(0, 0, new SKColor(random[0], random[1], random[2], 255));
            using var picture = SKImage.FromBitmap(bitmap);
            using var encoded = picture.Encode(SKEncodedImageFormat.Png, 100);
            var png = encoded.ToArray();
            MediaAsset? uploaded = null;

            await Case("Default scanner exception is off and Production never inherits Development exception", async () =>
            {
                var defaults = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Public:BaseUrl"] = "http://127.0.0.1:5189" }).Build();
                Check(!MediaUploadOptions.FromConfiguration(defaults, "Development").DevelopmentExceptionEnabled, "Default configuration enabled unscanned uploads.");
                var production = MediaUploadOptions.FromConfiguration(development, "Production");
                Check(!production.DevelopmentExceptionEnabled, "Production enabled Development scanner exception.");
                var source = new MediaAsset { Origin = "Uploaded", ScanStatus = "NotScanned", ValidationStatus = "Validated" };
                Check(!production.CanServe(source), "Production served an unscanned upload.");
                Check(!production.CanServe(new MediaAsset { Origin="Source",ScanStatus="NotScanned",ValidationStatus="SourceValidated" }), "Production grandfathered an unscanned source file outside local Development.");
                Check(!MediaUploadOptions.FromConfiguration(development,"Staging").CanServe(new MediaAsset { Origin="Source",ScanStatus="NotScanned",ValidationStatus="SourceValidated" }), "Staging grandfathered an unscanned source file.");
                Check(!settings.CanServe(new MediaAsset { Origin = "Source", ScanStatus = "Infected", ValidationStatus = "SourceValidated", IsQuarantined = false }), "Known failed source scan was ignored.");
                Check(!settings.CanServe(new MediaAsset { Origin = "Uploaded", ScanStatus = "Failed", ValidationStatus = "Validated", IsQuarantined = false }), "Failed upload scan was ignored.");
                using var input = new MemoryStream(png, false);
                await Rejected(() => new MediaService(db, storage, production).UploadAsync(input, "test.png", "image/png", owner, false), 503);
            });

            await Case("Real PNG pixels decode and SQL/file preserve truthful NotScanned state and dimensions", async () =>
            {
                using var input = new MemoryStream(png, false);
                var result = await service.UploadAsync(input, "../test.png", "image/png", owner, false);
                uploaded = result.Asset;
                Check(!result.Reused && uploaded.Origin == "Uploaded" && uploaded.UploadedByUserId == owner, "New upload identity is wrong.");
                Check(uploaded.Width == bitmap.Width && uploaded.Height == bitmap.Height, "Decoded image dimensions were not recorded.");
                Check(uploaded.ScanStatus == "NotScanned" && uploaded.ValidationStatus == "Validated", "Validation was misreported as malware scanning.");
                Check(uploaded.OriginalName == "test.png" && uploaded.StorageKey.StartsWith("uploads/") && !uploaded.StorageKey.Contains("test.png"), "Client path/name leaked into storage key.");
                Check(await db.MediaAssets.AsNoTracking().AnyAsync(x => x.Id == uploaded.Id), "SQL did not store the upload.");
                Check(await HashFile(storage.ResolveMedia(uploaded.StorageKey)) == uploaded.Sha256, "Stored original bytes/hash differ.");
            });

            await Case("Actual source JPEG validates without changing its original bytes or existing source metadata", async () =>
            {
                var before = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Sha256 == originalImageHash);
                var metadata = before is null ? null : JsonSerializer.Serialize(before);
                await using var input = System.IO.File.OpenRead(sourceImage);
                var result = await service.UploadAsync(input, "source.jpg", "image/jpeg", owner, true);
                Check(result.Asset.Sha256 == originalImageHash && await HashFile(sourceImage) == originalImageHash, "Source JPEG bytes/hash changed.");
                if (before is not null)
                    Check(result.Reused && JsonSerializer.Serialize(await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == before.Id)) == metadata, "Duplicate upload changed source asset metadata.");
            });

            await Case("Actual source PDF parses pages and preserves the original PDF bytes/hash", async () =>
            {
                var before = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Sha256 == originalPdfHash);
                var metadata = before is null ? null : JsonSerializer.Serialize(before);
                await using var input = System.IO.File.OpenRead(sourcePdf);
                var result = await service.UploadAsync(input, "source.pdf", "application/pdf", owner, true);
                Check(result.Asset.Sha256 == originalPdfHash && await HashFile(sourcePdf) == originalPdfHash, "Source PDF bytes/hash changed.");
                if (before is not null)
                    Check(result.Reused && JsonSerializer.Serialize(await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == before.Id)) == metadata, "Duplicate upload changed source PDF metadata.");
            });

            await Case("Duplicate same-owner bytes reuse immutable SQL/file and reject another owner's private duplicate", async () =>
            {
                Check(uploaded is not null, "PNG fixture missing.");
                var before = await db.MediaAssets.CountAsync();
                using (var input = new MemoryStream(png, false))
                {
                    var result = await service.UploadAsync(input, "other-name.png", "image/png", owner, false);
                    Check(result.Reused && result.Asset.Id == uploaded!.Id && await db.MediaAssets.CountAsync() == before, "Duplicate created/renamed a media record.");
                }
                using var forbidden = new MemoryStream(png, false);
                await Rejected(() => service.UploadAsync(forbidden, "private.png", "image/png", "QA-OTHER-OWNER", false), 403);
                Check(await db.MediaAssets.CountAsync() == before, "Private duplicate created a record or transferred ownership.");
            });

            await Case("Disguised HTML, signature-only image/PDF, wrong MIME and wrong extension do not pass", async () =>
            {
                var before = await db.MediaAssets.CountAsync();
                foreach (var example in new[] {
                    (Bytes: "<html><script>alert(1)</script></html>"u8.ToArray(), Name: "evil.png", Mime: "image/png"),
                    (Bytes: png[..Math.Min(30, png.Length)], Name: "corrupt.png", Mime: "image/png"),
                    (Bytes: "%PDF-1.7\nnot-a-document\n%%EOF"u8.ToArray(), Name: "fake.pdf", Mime: "application/pdf"),
                    (Bytes: png, Name: "wrong.png", Mime: "image/jpeg"),
                    (Bytes: png, Name: "wrong.pdf", Mime: "application/pdf"),
                    (Bytes: png, Name: "active.svg", Mime: "image/svg+xml") })
                {
                    using var input = new MemoryStream(example.Bytes, false);
                    await Rejected(() => service.UploadAsync(input, example.Name, example.Mime, owner, false), 400);
                }
                Check(await db.MediaAssets.CountAsync() == before, "Invalid files were stored in SQL.");
            });

            await Case("Streaming byte cap and decoded pixel cap reject oversized data and leave no temp uploads", async () =>
            {
                var before = await db.MediaAssets.CountAsync();
                var byteLimits = new MediaUploadOptions { MaxImageBytes = 1024, AllowUnscannedDevelopmentUploads = true, IsDevelopmentLoopback = true };
                using (var large = new MemoryStream(new byte[2048], false))
                    await Rejected(() => new MediaService(db, storage, byteLimits).UploadAsync(large, "large.png", "image/png", owner, false), 413);
                var pixelLimits = new MediaUploadOptions { MaxImagePixels = 1, AllowUnscannedDevelopmentUploads = true, IsDevelopmentLoopback = true };
                using (var large = new MemoryStream(png, false))
                    await Rejected(() => new MediaService(db, storage, pixelLimits).UploadAsync(large, "pixels.png", "image/png", owner, false), 413);
                Check(await db.MediaAssets.CountAsync() == before, "Oversized files created SQL records.");
                var incoming = StorageOptions.ResolveWithin(storage.TempRoot, "media-incoming");
                Check(!Directory.EnumerateFiles(incoming).Any(), "Rejected uploads left a temporary upload.");
            });

            await Case("Same display name does not overwrite another uploaded original", async () =>
            {
                var firstHash = uploaded!.Sha256;
                bitmap.SetPixel(0, 0, new SKColor((byte)(random[0] ^ 1), random[1], random[2], 255));
                using var secondImage = SKImage.FromBitmap(bitmap);
                using var secondEncoded = secondImage.Encode(SKEncodedImageFormat.Png, 100);
                using var secondInput = new MemoryStream(secondEncoded.ToArray(), false);
                var result = await service.UploadAsync(secondInput, "test.png", "image/png", owner, false);
                Check(!result.Reused && result.Asset.Id != uploaded.Id && result.Asset.StorageKey != uploaded.StorageKey, "Same filename reused the wrong storage object.");
                Check(await HashFile(storage.ResolveMedia(uploaded.StorageKey)) == firstHash, "Same filename overwrote the first original.");
            });

            await Case("Invalid multipart envelope, missing duplicate original and public storage root never report saved", async () =>
            {
                var before = await db.MediaAssets.CountAsync();
                using (var input = new MemoryStream(png, false))
                    await Rejected(() => service.UploadAsync(input, "test.png", "image/png", owner, false,
                        validateEnvelope: () => throw new MediaUploadException("Test malformed envelope")), 400);
                Check(await db.MediaAssets.CountAsync() == before, "Invalid multipart envelope wrote SQL.");
                var original = storage.ResolveMedia(uploaded!.StorageKey);
                var held = original + ".qa-held";
                System.IO.File.Move(original, held, false);
                try
                {
                    using var input = new MemoryStream(png, false);
                    await Rejected(() => service.UploadAsync(input, "test.png", "image/png", owner, false), 409);
                }
                finally { System.IO.File.Move(held, original, false); }
                var unsafeStorage = new StorageOptions { MediaRoot = Path.Combine(testRoot, "wwwroot"), TempRoot = storage.TempRoot,
                    KeysRoot = storage.KeysRoot, SnapshotRoot = storage.SnapshotRoot };
                using (var input = new MemoryStream(png, false))
                    await Rejected(() => new MediaService(db, unsafeStorage, settings).UploadAsync(input, "test.png", "image/png", owner, false), 503);
                Check(await db.MediaAssets.CountAsync() == before && await HashFile(original) == uploaded.Sha256, "Rejected upload changed existing media.");
            });
            status = "PASS";
        }
        finally
        {
            var report = new { phase = "2A", status, database = "LandPortal_Test", storage = Path.GetRelativePath(root, testRoot),
                realSourceNetworkRequests = 0, malwareScannerExecuted = false, cases };
            var destination = Environment.GetEnvironmentVariable("LandPortal__EvidenceRoot") is null?Path.Combine(root, "docs", "evidence", "media-validation-report.json"):TestEvidencePaths.At(root,"media-validation-report.json");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await System.IO.File.WriteAllTextAsync(destination, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
