using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LandPortal.Infrastructure;
using UglyToad.PdfPig;

namespace LandPortal.WeeklyImport;

public sealed record PlanningFile(string Entry, string Name, long Bytes, string Sha256, int? Pages, string[] Blocks)
{
    public string? OriginalSha256 { get; init; }
    public string? DerivativeNotice { get; init; }
}
public sealed record PlanningArchiveExclusion(string Entry, string Sha256, string Reason);
public sealed record PlanningScope(string ZipSha256, IReadOnlyList<PlanningArchiveExclusion> ExcludedArchives);
public sealed record PlanningRow(WeeklyRow Source, IReadOnlyList<PlanningFile> Files, string[] Blocks, bool PageFormula = false)
{
    public int Stt => Source.Stt;
    public bool CanImport => Blocks.Length == 0 && Files.Count > 0 && Files.All(x => x.Blocks.Length == 0);
    public WeeklyRow Attachment(PlanningFile file) => Source with { PdfEntry = file.Entry, FileName = file.Name, PdfSha256 = file.Sha256, Bytes = file.Bytes, Pages = file.Pages ?? 0 };
}

// Separate profile and journal: the weekly sheet's confirmations/command IDs never authorize 2.4.2.
public sealed class PlanningPackage
{
    public const string Profile = "2.4.2-QHKHSDD-v1";
    public const int Category = 77;
    public const string Route = "/quy-hoach-su-dung-dat";
    public const string Label = "Quy hoạch, kế hoạch sử dụng đất";
    const string Folder = "2.4_Lich tuan/QH, KH SDĐ/";
    const long ZipLimit = 768L * 1024 * 1024;
    public required string Path { get; init; }
    public required WeeklyDateSource Dates { get; init; }
    public required IReadOnlyList<PlanningRow> Rows { get; init; }
    public required IReadOnlyList<object> Archives { get; init; }
    public PlanningScope? Scope { get; init; }
    public PlanningPdfOverrides? PdfOverrides { get; init; }
    public string ScopeSha256 => PdfOverrides == null ? WeeklyPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(Scope, WeeklyPackage.Json))
        : WeeklyPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(new { Scope, PdfOverrides.ManifestSha256 }, WeeklyPackage.Json));
    public static string FileHash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }

    public static PlanningPackage Read(string path, PlanningScope? scope = null, PlanningPdfOverrides? pdfOverrides = null)
    {
        path = System.IO.Path.GetFullPath(path);
        if (new FileInfo(path).Length > ZipLimit) throw new InvalidDataException("ZIP QHKHSDD vượt giới hạn khảo sát 768 MiB.");
        var zipHash = FileHash(path);
        if (scope != null && (scope.ZipSha256 != zipHash || scope.ExcludedArchives == null
            || scope.ExcludedArchives.GroupBy(x => x.Entry, StringComparer.Ordinal).Any(x => x.Count() != 1)
            || scope.ExcludedArchives.Any(x => !x.Entry.StartsWith(Folder, StringComparison.Ordinal)
                || !x.Entry.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                || !Regex.IsMatch(x.Sha256 ?? "", "^[a-f0-9]{64}$") || string.IsNullOrWhiteSpace(x.Reason))))
            throw new InvalidDataException("Phạm vi loại ZIP không khớp nguồn, bị trùng hoặc thiếu lý do; không áp dụng sang PDF.");
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        using var zip = ZipFile.OpenRead(path);
        WeeklyPackage.ValidateZip(zip, ZipLimit);
        var workbook = WeeklyPackage.ReadEntry(zip.GetEntry(WeeklyPackage.WorkbookName) ?? throw new InvalidDataException("Thiếu workbook nguồn."), 32 * 1024 * 1024);
        if(pdfOverrides != null && (pdfOverrides.Set.ZipSha256!=zipHash || pdfOverrides.Set.WorkbookSha256!=WeeklyPackage.Hash(workbook)))
            throw new InvalidDataException("Manifest PDF xử lý không khớp ZIP/Excel nguồn.");
        var usedOverrides=new HashSet<string>(StringComparer.Ordinal);
        using var xlsx = new ZipArchive(new MemoryStream(workbook)); WeeklyPackage.ValidateZip(xlsx, 128 * 1024 * 1024);
        if (xlsx.Entries.Any(e => e.FullName.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Workbook có chữ ký; không sửa.");
        var s = WeeklyPackage.S; var book = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(xlsx.GetEntry("xl/workbook.xml")!));
        var sheet = book.Descendants(s + "sheet").Where(e => (string?)e.Attribute("name") is "2.4.2 QHKHSDD" or "2.4.2_QHKHSDD").ToArray();
        if (sheet.Length != 1) throw new InvalidDataException("Cần đúng một sheet 2.4.2 QHKHSDD (hoặc 2.4.2_QHKHSDD); không dùng nhóm 2.2.");
        var rels = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(xlsx.GetEntry("xl/_rels/workbook.xml.rels")!));
        var rel = rels.Root!.Elements().Single(x => (string?)x.Attribute("Id") == (string?)sheet[0].Attribute(WeeklyPackage.R + "id"));
        if ((string?)rel.Attribute("TargetMode") == "External") throw new InvalidDataException("Sheet ngoài workbook.");
        var target = (string)rel.Attribute("Target")!; var sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        if (sheetPath.Contains("..") || sheetPath.Contains('\\')) throw new InvalidDataException("Đường dẫn sheet sai.");
        var xml = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(xlsx.GetEntry(sheetPath)!));
        if (xml.Root!.Element(s + "sheetProtection") != null) throw new InvalidDataException("Không sửa sheet bị khóa.");
        var shared = xlsx.GetEntry("xl/sharedStrings.xml") is {} ss ? WeeklyPackage.Xml(WeeklyPackage.ReadEntry(ss)).Root!.Elements(s + "si").Select(e => string.Concat(e.Descendants(s + "t").Select(t => t.Value))).ToArray() : [];
        string Value(XElement? c) => c == null ? "" : (string?)c.Attribute("t") == "s" ? shared[int.Parse(c.Element(s + "v")!.Value, CultureInfo.InvariantCulture)] : (string?)c.Attribute("t") == "inlineStr" ? string.Concat(c.Descendants(s + "t").Select(t => t.Value)) : c.Element(s + "v")?.Value ?? "";
        XElement? Cell(string address) => xml.Descendants(s + "c").SingleOrDefault(c => (string?)c.Attribute("r") == address);
        foreach (var (cell, title) in new[] { ("A4", "STT"), ("B4", "Ngày đăng"), ("C4", "Tên file"), ("D4", "Link đăng tải"), ("E4", "số lượng (trang)") })
            if (Value(Cell(cell)).Trim() != title) throw new InvalidDataException("Cột nguồn không đúng: " + cell);
        var numbered = xml.Descendants(s + "row").Where(r => int.TryParse(Value(r.Elements(s + "c").FirstOrDefault(c => Regex.IsMatch((string?)c.Attribute("r") ?? "", "^A[0-9]+$"))), out _)).ToArray();
        if (numbered.Length != 32) throw new InvalidDataException("Gói này cần đủ 32 STT, dòng 7–38; dừng nếu nguồn thay đổi.");
        var files = new Dictionary<int,List<PlanningFile>>(); var archives = new List<object>();
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(Folder, StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
        {
            var match = Regex.Match(entry.Name, @"^(\d{1,2})_.+\.(pdf|zip)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success || entry.FullName != Folder + entry.Name || !int.TryParse(match.Groups[1].Value, out var stt) || stt is < 1 or > 32)
                throw new InvalidDataException("Không ghép được STT đầu tên tệp: " + entry.Name);
            var bytes = WeeklyPackage.ReadEntry(entry, 160L * 1024 * 1024); var hash = WeeklyPackage.Hash(bytes);
            var originalHash=hash;
            var replacement=pdfOverrides?.For(entry.FullName);
            var blocks = new List<string>(); int? pages = null;
            if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var exclusion = scope?.ExcludedArchives.SingleOrDefault(x => x.Entry == entry.FullName);
                if (exclusion != null)
                {
                    if (exclusion.Sha256 != hash) throw new InvalidDataException("ZIP được loại đã thay đổi hash: " + entry.Name);
                    archives.Add(new { stt, entry = entry.FullName, sha256 = hash, bytes = entry.Length, status = "ExcludedFromImport", reason = exclusion.Reason });
                    excluded.Add(entry.FullName);
                    continue; // Keep the source ZIP intact; never unpack/upload this explicitly excluded attachment.
                }
                using var nested = new ZipArchive(new MemoryStream(bytes)); WeeklyPackage.ValidateZip(nested, 256L * 1024 * 1024);
                archives.Add(new { stt, entry = entry.FullName, sha256 = hash, entries = nested.Entries.Select(e => new { name = e.FullName, bytes = e.Length }).ToArray() });
                blocks.Add("ZIP đính kèm cần ánh xạ nội dung được xác nhận; không tự bung/gắn hoặc bỏ qua các tệp bên trong.");
            }
            else
            {
                try { using var pdf = PdfDocument.Open(bytes); if (pdf.IsEncrypted || pdf.NumberOfPages < 1) blocks.Add("PDF mã hóa hoặc không có trang."); else pages = pdf.NumberOfPages; }
                catch (Exception e) when (e is not OutOfMemoryException) { blocks.Add("Không đọc được cấu trúc/số trang PDF."); }
                if(replacement!=null)
                {
                    if(replacement.Stt!=stt || replacement.SourceSha256!=hash || replacement.Pages!=pages || blocks.Count!=0)
                        throw new InvalidDataException("Bản xử lý không khớp đúng STT/hash/số trang PDF nguồn: "+stt);
                    bytes=pdfOverrides!.ReadBytes(entry.FullName);hash=WeeklyPackage.Hash(bytes);
                    if(hash!=replacement.Sha256 || bytes.LongLength!=replacement.Bytes)throw new InvalidDataException("PDF xử lý đổi trong khi đọc.");
                    usedOverrides.Add(entry.FullName);
                }
                if (bytes.LongLength > EditorialDocumentPolicy.PdfMaximum) blocks.Add(EditorialDocumentPolicy.SizeHelp);
                else if (pages.HasValue)
                {
                    var temporary = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gdla-planning-" + Guid.NewGuid().ToString("N") + ".pdf");
                    try
                    {
                        File.WriteAllBytes(temporary, bytes);
                        var parsed=EditorialDocumentPolicy.ValidateAsync(temporary,"application/pdf",new MediaService(null!,null!,new MediaUploadOptions()),true).GetAwaiter().GetResult();
                        if(parsed!=pages)throw new InvalidDataException("Bản xử lý đổi số trang.");
                    }
                    catch (Exception e) when (e is not (OutOfMemoryException or OperationCanceledException))
                    { blocks.Add("PDF không qua kiểm định cấu trúc nghiêm ngặt CMS; cần rà soát bản nguồn, không tự sửa byte. Chi tiết: " + e.GetBaseException().Message); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
            }
            if (!files.TryGetValue(stt, out var list)) files[stt] = list = [];
            list.Add(new(entry.FullName, entry.Name, bytes.LongLength, hash, pages, blocks.ToArray()) { OriginalSha256=replacement==null?null:originalHash, DerivativeNotice=replacement?.Notice });
        }
        if(pdfOverrides!=null && usedOverrides.Count!=pdfOverrides.Set.Files.Count)throw new InvalidDataException("Manifest chứa PDF không có trong nguồn; không bỏ qua mapping thừa.");
        if (scope != null && excluded.Count != scope.ExcludedArchives.Count) throw new InvalidDataException("Không tìm thấy đúng ZIP trong phạm vi loại đã xác nhận.");
        var duplicateHashes = files.Values.SelectMany(x => x).GroupBy(x => x.Sha256).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var rows = new List<PlanningRow>();
        for (var stt = 1; stt <= 32; stt++)
        {
            var n = stt + 6; var blocks = new List<string>();
            if (Value(Cell("A" + n)) != stt.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException("STT sai dòng " + n);
            if (new[] { "A", "B", "C", "D" }.Any(c => Cell(c + n)?.Element(s + "f") != null)) throw new InvalidDataException("Không ghi đè ô công thức nguồn/link dòng " + n);
            var title = Value(Cell("C" + n)); if (string.IsNullOrWhiteSpace(title) || title.Length > 1000) throw new InvalidDataException("Tiêu đề không hợp lệ STT " + stt);
            var excelPages = int.Parse(Value(Cell("E" + n)), CultureInfo.InvariantCulture);
            var attachments = files.GetValueOrDefault(stt, []).OrderBy(x => x.Entry, StringComparer.Ordinal).ToArray();
            if (attachments.Length == 0) blocks.Add("Thiếu tài liệu khớp STT.");
            if (attachments.Any(x => duplicateHashes.Contains(x.Sha256))) blocks.Add("Tài liệu trùng hash giữa hồ sơ; cần đối soát.");
            var totalPages = attachments.Length > 0 && attachments.All(x => x.Pages.HasValue) ? attachments.Sum(x => x.Pages!.Value) : excelPages;
            // The aggregate hash binds the complete ordered attachment set; never silently select just the first PDF.
            var setHash = WeeklyPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(attachments.Select(x => new { x.Entry, x.Sha256, x.Bytes })));
            var row = new WeeklyRow(stt, n, title, (string?)Cell("B" + n)?.Attribute("t") ?? "n", Value(Cell("B" + n)), "", "", setHash,
                attachments.Sum(x => x.Bytes), totalPages, excelPages, null, null, []);
            rows.Add(new(row, attachments, blocks.ToArray(), Cell("E" + n)?.Element(s + "f") != null));
        }
        if (FileHash(path) != zipHash) throw new InvalidDataException("ZIP thay đổi trong khi kiểm kê.");
        return new() { Path = path, Dates = new(zipHash, WeeklyPackage.Hash(workbook), workbook, sheetPath) { Count = 32, SheetName = (string)sheet[0].Attribute("name")!, AllowDoubleSlashCandidate = true }, Rows = rows, Archives = archives, Scope = scope, PdfOverrides=pdfOverrides };
    }
    public byte[] ReadFile(PlanningFile file)
    {
        using var zip = ZipFile.OpenRead(Path);
        var data = PdfOverrides?.Paths.ContainsKey(file.Entry)==true ? PdfOverrides.ReadBytes(file.Entry)
            : WeeklyPackage.ReadEntry(zip.GetEntry(file.Entry) ?? throw new InvalidDataException("Thiếu tệp nguồn."), EditorialDocumentPolicy.PdfMaximum);
        if (data.LongLength != file.Bytes || WeeklyPackage.Hash(data) != file.Sha256) throw new InvalidDataException("Tệp nguồn đã đổi hash/byte.");
        return data;
    }
    public void WriteWorkbook(string path, IReadOnlyDictionary<int,string?> links) => new WeeklyPackage {
        ZipSha256 = Dates.ZipSha256, WorkbookSha256 = Dates.WorkbookSha256, Workbook = Dates.Workbook, SheetPath = Dates.SheetPath,
        Rows = Rows.Select(x => x.PageFormula ? x.Source with { Pages = x.Source.ExcelPages } : x.Source).ToArray(), PdfBytes = new Dictionary<int,byte[]>() }.WriteWorkbook(path, links);
    public Guid Command(PlanningRow row, string stage) => WeeklyPackage.Command(row.Source, Profile + ":" + Dates.ZipSha256 + ":" + stage);
}
