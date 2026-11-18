using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Infrastructure;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace LandPortal.WeeklyImport;

// Offline only: invokes the same format/structure checks as CMS, never creates a DbContext or an upload.
public static class PlanningPdfCheck
{
    public sealed record Input(int Stt, string Path, string ExpectedSha256, int ExpectedPages, string SourceEntry);
    public static async Task RunAsync(string[] args)
    {
        if (args.Length is < 3 or > 4 || args.Length == 4 && args[3] != "--editorial") throw new ArgumentException("planning-pdf-check <files.json> <new-result.json> [--editorial]");
        var editorial = args.Length == 4;
        if (File.Exists(args[2])) throw new IOException("Không ghi đè báo cáo PDF cũ.");
        var inputs = JsonSerializer.Deserialize<Input[]>(await File.ReadAllBytesAsync(args[1])) ?? throw new InvalidDataException("Thiếu danh sách PDF.");
        if (inputs.Length is < 1 or > 64 || inputs.Any(x => x.Stt is < 1 or > 32)) throw new InvalidDataException("Ngoài phạm vi QHKHSDD.");
        var media = new MediaService(null!, null!, new MediaUploadOptions()); // ValidateContent never uses database/storage services.
        var results = new List<object>();
        foreach (var input in inputs)
        {
            var path = System.IO.Path.GetFullPath(input.Path);
            var bytes = new FileInfo(path).Length;
            if (bytes > 160L * 1024 * 1024) throw new InvalidDataException("Vượt giới hạn kiểm kê PDF.");
            using (var file = File.OpenRead(path))
                if (Convert.ToHexStringLower(SHA256.HashData(file)) != input.ExpectedSha256) throw new InvalidDataException("PDF đổi hash: " + input.Stt);
            string? formatError = null, structureError = null;
            var invalidPages = new List<object>();
            var pages = 0;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var beforeAllocated = GC.GetTotalAllocatedBytes();
            try
            {
                if (editorial) pages = (await EditorialDocumentPolicy.ValidateAsync(path, "application/pdf", media, managedWithoutAv: true))!.Value;
                else
                {
                    OfficeDocumentPolicy.Validate(path, "application/pdf", media, managedWithoutAv: true);
                    using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
                    pages = pdf.NumberOfPages;
                }
                if (pages != input.ExpectedPages) throw new InvalidDataException("Số trang khác nguồn.");
            }
            catch (Exception e) when (e is not (OutOfMemoryException or OperationCanceledException))
            {
                formatError = e.GetBaseException().Message;
                try
                {
                    if (editorial) throw new InvalidDataException("No duplicate parse after editorial failure.");
                    using var diagnostic = UglyToad.PdfPig.PdfDocument.Open(path, new UglyToad.PdfPig.ParsingOptions { UseLenientParsing = false, MaxStackDepth = 64 });
                    pages = diagnostic.NumberOfPages;
                    for (var n = 1; n <= pages; n++)
                        try { _ = diagnostic.GetPage(n); }
                        catch (Exception error) when (error is not (OutOfMemoryException or OperationCanceledException)) { invalidPages.Add(new { page = n, error = error.GetBaseException().Message }); }
                }
                catch (Exception error) when (error is not (OutOfMemoryException or OperationCanceledException)) { invalidPages.Add(new { page = 0, error = error.GetBaseException().Message }); }
            }
            // Size and structure are independent: an oversized source may also fail the structural policy.
            try { if (!editorial) ManagedPdfPolicy.Validate(path); }
            catch (Exception e) when (e is not (OutOfMemoryException or OperationCanceledException)) { structureError = e.GetBaseException().Message; }
            timer.Stop();
            var tooLarge = bytes > (editorial ? EditorialDocumentPolicy.PdfMaximum : EditorialDocumentService.Maximum);
            var pass = !tooLarge && formatError == null && structureError == null;
            results.Add(new { input.Stt, input.SourceEntry, path, sha256 = input.ExpectedSha256, bytes, pages, expectedPages = input.ExpectedPages,
                tooLarge, formatError, structureError, invalidPages,
                structureLimit = structureError == "PDF vượt giới hạn cấu trúc." ? DescribeStructureLimit(path) : null,
                cmsFileChecksPass = pass, scanStatus = "NotScanned", cmsWrites = 0,
                validationPolicy = editorial ? "EditorialPdf104MiB2000Pages" : "Legacy10MiB1000Pages",
                validationMilliseconds = timer.Elapsed.TotalMilliseconds,
                allocatedBytes = GC.GetTotalAllocatedBytes() - beforeAllocated,
                peakWorkingSetBytes = System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 });
            Console.WriteLine($"STT {input.Stt}: {(pass ? "PASS" : "BLOCKED")}; bytes={bytes}; size={!tooLarge}; format={formatError ?? "PASS"}; structure={structureError ?? "PASS"}");
        }
        await WeeklyImporter.AtomicJson(args[2], results);
    }
    // Diagnostic only. Admission still comes exclusively from ManagedPdfPolicy.Validate above.
    static object DescribeStructureLimit(string path)
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(path, new UglyToad.PdfPig.ParsingOptions { UseLenientParsing = false, MaxStackDepth = 64 });
        var seen = new HashSet<IndirectReference>(); var count = 0; var maximumDepth = 0; string? reason = null, location = null;
        void Visit(IToken token, int depth, string at)
        {
            if (reason != null) return;
            count++; maximumDepth = Math.Max(maximumDepth, depth);
            if (count > 100_000 || depth > 64) { reason = depth > 64 ? "Depth" : "TokenCount"; location = at; return; }
            switch (token)
            {
                case IndirectReferenceToken reference:
                    if (seen.Add(reference.Data)) Visit(pdf.Structure.GetObject(reference.Data).Data, depth + 1, at + "->" + reference.Data);
                    break;
                case StreamToken stream: Visit(stream.StreamDictionary, depth + 1, at + "/stream"); break;
                case ArrayToken array:
                    foreach (var child in array.Data) Visit(child, depth + 1, at + "[]");
                    break;
                case DictionaryToken dictionary:
                    foreach (var entry in dictionary.Data) Visit(entry.Value, depth + 1, at + "/" + entry.Key);
                    break;
            }
        }
        Visit(pdf.Structure.Catalog.CatalogDictionary, 0, "Catalog");
        return new { reason, location, tokensVisited = count, maximumDepth };
    }
}
