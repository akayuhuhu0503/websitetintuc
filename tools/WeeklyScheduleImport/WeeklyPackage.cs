using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using LandPortal.Infrastructure;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace LandPortal.WeeklyImport;

public sealed record WeeklyRow(int Stt, int ExcelRow, string Title, string DateType, string DateValue,
    string PdfEntry, string FileName, string PdfSha256, long Bytes, int Pages, int ExcelPages,
    string? ExcelPeriod, string? PdfPeriod, string[] Warnings);

// Never save the workbook through a library which rewrites sheets, styles, drawings or dates.
public sealed class WeeklyPackage
{
    public const string SheetName = "2.4.1_LichCT";
    public const string WorkbookName = "Tong hop link website_T11-2026_Fin.xlsx";
    public static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public required string ZipSha256 { get; init; }
    public required string WorkbookSha256 { get; init; }
    public required byte[] Workbook { get; init; }
    public required string SheetPath { get; init; }
    public required IReadOnlyList<WeeklyRow> Rows { get; init; }
    public required IReadOnlyDictionary<int, byte[]> PdfBytes { get; init; }
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static Guid Command(WeeklyRow row, string stage) => new(SHA256.HashData(Encoding.UTF8.GetBytes(
        "GDLA.WeeklyImport.v1\n" + row.Stt + "\n" + row.Title + "\n" + row.PdfSha256 + "\n" + stage)).AsSpan(0, 16));

    public static WeeklyPackage Read(string path)
    {
        if (new FileInfo(path).Length > 128 * 1024 * 1024) throw new InvalidDataException("ZIP vượt 128 MiB.");
        var bytes = File.ReadAllBytes(path);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        ValidateZip(zip, 128 * 1024 * 1024);
        var workbookEntry = zip.Entries.SingleOrDefault(e => e.FullName == WorkbookName)
            ?? throw new InvalidDataException("Thiếu hoặc trùng workbook đúng tên.");
        var workbook = ReadEntry(workbookEntry, 32 * 1024 * 1024);
        using var xlsx = new ZipArchive(new MemoryStream(workbook));
        ValidateZip(xlsx, 128 * 1024 * 1024);
        if (xlsx.Entries.Any(x => x.FullName.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Workbook có chữ ký; không sửa làm mất hiệu lực chữ ký.");
        var book = Xml(ReadEntry(xlsx.GetEntry("xl/workbook.xml")!));
        var rels = Xml(ReadEntry(xlsx.GetEntry("xl/_rels/workbook.xml.rels")!));
        var sheet = book.Descendants(S + "sheet").Single(x => (string?)x.Attribute("name") == SheetName);
        var relation = rels.Root!.Elements().Single(x => (string?)x.Attribute("Id") == (string?)sheet.Attribute(R + "id"));
        if ((string?)relation.Attribute("TargetMode") == "External") throw new InvalidDataException("Sheet ngoài workbook.");
        var target = (string)relation.Attribute("Target")!;
        var sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        if (sheetPath.Contains("..")) throw new InvalidDataException("Đường dẫn sheet không hợp lệ.");
        var document = Xml(ReadEntry(xlsx.GetEntry(sheetPath)!));
        if (document.Root!.Element(S + "sheetProtection") != null) throw new InvalidDataException("Sheet đang khóa; không tự bỏ bảo vệ.");
        var shared = xlsx.GetEntry("xl/sharedStrings.xml") is { } ss ? Xml(ReadEntry(ss)).Root!.Elements(S + "si")
            .Select(x => string.Concat(x.Descendants(S + "t").Select(t => t.Value))).ToArray() : [];
        string Value(XElement cell) => (string?)cell.Attribute("t") == "s" ? shared[int.Parse(cell.Element(S + "v")!.Value, CultureInfo.InvariantCulture)]
            : (string?)cell.Attribute("t") == "inlineStr" ? string.Concat(cell.Descendants(S + "t").Select(t => t.Value)) : cell.Element(S + "v")?.Value ?? "";
        var pdfs = new Dictionary<int, ZipArchiveEntry>();
        foreach (var pdf in zip.Entries.Where(e => e.FullName.StartsWith("Lich Tuan/", StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
        {
            var match = Regex.Match(pdf.Name, @"^(\d{1,2})_.*\.pdf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success || pdf.FullName != "Lich Tuan/" + pdf.Name || !int.TryParse(match.Groups[1].Value, out var stt)
                || stt is < 1 or > 43 || !pdfs.TryAdd(stt, pdf)) throw new InvalidDataException("Tên/STT PDF thiếu, trùng hoặc không hợp lệ: " + pdf.Name);
        }
        if (pdfs.Count != 43) throw new InvalidDataException("Cần đúng 43 PDF theo STT đầu tên tệp.");
        var rows = new List<WeeklyRow>(); var files = new Dictionary<int, byte[]>();
        for (var stt = 1; stt <= 43; stt++)
        {
            var number = stt + 6;
            var row = document.Descendants(S + "row").Single(r => (string?)r.Attribute("r") == number.ToString(CultureInfo.InvariantCulture));
            XElement Cell(string column) => row.Elements(S + "c").Single(c => (string?)c.Attribute("r") == column + number);
            if (Value(Cell("A")) != stt.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException("STT không khớp dòng " + number);
            if (new[] { "A", "B", "C", "D", "E" }.Any(c => row.Elements(S + "c").Any(x => (string?)x.Attribute("r") == c + number && x.Element(S + "f") != null)))
                throw new InvalidDataException("Ô nguồn/đích có công thức tại dòng " + number + "; không ghi đè công thức.");
            var title = Value(Cell("C")); if (string.IsNullOrWhiteSpace(title) || title.Length > 1000) throw new InvalidDataException("Tiêu đề không hợp lệ ở STT " + stt);
            var pdf = ReadEntry(pdfs[stt], EditorialDocumentService.Maximum);
            // Validate actual PDF structure; this is NOT antivirus and does not grant publication.
            using var parsed = PdfDocument.Open(pdf);
            if (parsed.IsEncrypted || parsed.NumberOfPages is < 1 or > 1000) throw new InvalidDataException("PDF mã hóa hoặc số trang vượt giới hạn: " + stt);
            var header = ContentOrderTextExtractor.GetText(parsed.GetPage(1));
            var warnings = new List<string>();
            var excelPeriod = Period(title); var pdfPeriod = Period(header);
            if (excelPeriod == null || pdfPeriod == null) warnings.Add("Không đọc được đầy đủ tuần/khoảng ngày; cần đối chiếu bản gốc.");
            else if (excelPeriod != pdfPeriod) warnings.Add("Tuần/khoảng ngày Excel khác PDF; giữ nguyên tiêu đề cột C, cán bộ cần rà soát trước gửi duyệt.");
            var dateType = (string?)Cell("B").Attribute("t") ?? "n";
            if (dateType == "n") warnings.Add("Ngày đăng là số Excel; bảo toàn nguyên trạng, không dùng làm ngày công bố.");
            var pagesExcel = int.Parse(Value(Cell("E")), CultureInfo.InvariantCulture);
            rows.Add(new(stt, number, title, dateType, Value(Cell("B")), pdfs[stt].FullName, pdfs[stt].Name,
                Hash(pdf), pdf.Length, parsed.NumberOfPages, pagesExcel, excelPeriod, pdfPeriod, warnings.ToArray()));
            files.Add(stt, pdf);
        }
        if (rows.Select(x => x.PdfSha256).Distinct().Count() != 43) throw new InvalidDataException("PDF trùng byte giữa các STT; cần đối soát trước nhập.");
        return new() { ZipSha256 = Hash(bytes), WorkbookSha256 = Hash(workbook), Workbook = workbook, SheetPath = sheetPath, Rows = rows, PdfBytes = files };
    }

    public static string? Period(string text)
    {
        var m = Regex.Match(text.Normalize(), @"Tuần\s*(\d{1,2})\s*[.:]?\s*Từ\s*ngày\s*(\d{1,2}/\d{1,2}/\d{4})\s*đến\s*ngày\s*(\d{1,2}/\d{1,2}/\d{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return m.Success ? $"{int.Parse(m.Groups[1].Value)} | {m.Groups[2].Value} - {m.Groups[3].Value}" : null;
    }
    public object Summary() => new { records = Rows.Count, pdfs = PdfBytes.Count, totalPages = Rows.Sum(r => r.Pages),
        numericDateCellsPreserved = Rows.Count(r => r.DateType == "n"),
        weekReview = Rows.Where(r => r.ExcelPeriod != r.PdfPeriod || r.PdfPeriod == null).Select(r => new { r.Stt, r.ExcelPeriod, r.PdfPeriod }),
        pageCorrections = Rows.Where(r => r.Pages != r.ExcelPages).Select(r => new { r.Stt, cell = "E" + r.ExcelRow, before = r.ExcelPages, after = r.Pages }), ZipSha256, WorkbookSha256 };

    public void WriteWorkbook(string destination, IReadOnlyDictionary<int, string?> links)
    {
        if (File.Exists(destination)) throw new IOException("File kết quả đã tồn tại; chọn tên mới, không ghi đè workbook.");
        if (links.Keys.Any(k => k is < 1 or > 43)) throw new InvalidDataException("STT link ngoài phạm vi.");
        using var source = new ZipArchive(new MemoryStream(Workbook));
        var xml = Xml(ReadEntry(source.GetEntry(SheetPath)!));
        var relPath = Path.GetDirectoryName(SheetPath)!.Replace('\\', '/') + "/_rels/" + Path.GetFileName(SheetPath) + ".rels";
        var relations = source.GetEntry(relPath) is { } entry ? Xml(ReadEntry(entry)) : new XDocument(new XElement(P + "Relationships"));
        var originalRel = relations.ToString(SaveOptions.DisableFormatting);
        var hyperlinks = xml.Root!.Element(S + "hyperlinks");
        foreach (var item in Rows)
        {
            var row = xml.Descendants(S + "row").Single(r => (int?)r.Attribute("r") == item.ExcelRow);
            XElement Cell(string column)
            {
                var address = column + item.ExcelRow;
                var cell = row.Elements(S + "c").SingleOrDefault(c => (string?)c.Attribute("r") == address);
                if (cell != null) return cell;
                cell = new XElement(S + "c", new XAttribute("r", address));
                var after = row.Elements(S + "c").FirstOrDefault(c => string.CompareOrdinal(Regex.Replace((string)c.Attribute("r")!, @"\d", ""), column) > 0);
                if (after == null) row.Add(cell); else after.AddBeforeSelf(cell);
                return cell;
            }
            if (item.Pages != item.ExcelPages)
            {
                var e = Cell("E"); e.SetAttributeValue("t", null); e.RemoveNodes(); e.Add(new XElement(S + "v", item.Pages));
            }
            if (!links.TryGetValue(item.Stt, out var url)) continue;
            var d = Cell("D"); d.RemoveNodes(); d.SetAttributeValue("t", null);
            foreach (var old in hyperlinks?.Elements(S + "hyperlink").Where(h => (string?)h.Attribute("ref") == "D" + item.ExcelRow).ToArray() ?? [])
            {
                // Preserve unrelated relationships, even when shared with another hyperlink.
                var id = (string?)old.Attribute(R + "id"); old.Remove();
                if (id != null && !xml.Descendants().Attributes(R + "id").Any(a => a.Value == id))
                    relations.Root!.Elements().Where(r => (string?)r.Attribute("Id") == id).Remove();
            }
            if (url == null) continue; // Previously exported, now withdrawn: clear only the owned D cell.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0)
                throw new InvalidDataException("URL công khai không hợp lệ.");
            d.SetAttributeValue("t", "inlineStr"); d.Add(new XElement(S + "is", new XElement(S + "t", url)));
            hyperlinks ??= new XElement(S + "hyperlinks");
            if (hyperlinks.Parent == null)
            {
                var next = xml.Root.Elements().FirstOrDefault(e => new[] { "printOptions", "pageMargins", "pageSetup", "headerFooter", "rowBreaks", "colBreaks", "customProperties", "cellWatches", "ignoredErrors", "smartTags", "drawing", "legacyDrawing", "legacyDrawingHF", "picture", "oleObjects", "controls", "webPublishItems", "tableParts", "extLst" }.Contains(e.Name.LocalName));
                if (next == null) xml.Root.Add(hyperlinks); else next.AddBeforeSelf(hyperlinks);
            }
            var rid = "weekly" + item.Stt;
            while (relations.Root!.Elements().Any(r => (string?)r.Attribute("Id") == rid)) rid += "x";
            relations.Root.Add(new XElement(P + "Relationship", new XAttribute("Id", rid), new XAttribute("Type", R.NamespaceName + "/hyperlink"), new XAttribute("Target", url), new XAttribute("TargetMode", "External")));
            hyperlinks.Add(new XElement(S + "hyperlink", new XAttribute("ref", "D" + item.ExcelRow), new XAttribute(R + "id", rid)));
        }
        if (hyperlinks is { HasElements: false }) hyperlinks.Remove();
        var relChanged = originalRel != relations.ToString(SaveOptions.DisableFormatting);
        var full = Path.GetFullPath(destination); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new ZipArchive(File.Open(temp, FileMode.CreateNew, FileAccess.ReadWrite), ZipArchiveMode.Create))
            {
                foreach (var original in source.Entries)
                {
                    var copy = output.CreateEntry(original.FullName, CompressionLevel.Optimal); copy.LastWriteTime = original.LastWriteTime; copy.ExternalAttributes = original.ExternalAttributes;
                    using var stream = copy.Open();
                    if (original.FullName == SheetPath) WriteXml(xml, stream);
                    else if (original.FullName == relPath && relChanged) WriteXml(relations, stream);
                    else { using var input = original.Open(); input.CopyTo(stream); }
                }
                if (relChanged && source.GetEntry(relPath) == null) { using var stream = output.CreateEntry(relPath).Open(); WriteXml(relations, stream); }
            }
            File.Move(temp, full, overwrite: false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static void WriteXml(XDocument xml, Stream stream) { using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, CloseOutput = false }); xml.Save(writer); }
    public static XDocument Xml(byte[] bytes)
    {
        using var input = new MemoryStream(bytes); using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024 * 1024 });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }
    public static byte[] ReadEntry(ZipArchiveEntry entry, long max = 64 * 1024 * 1024)
    {
        if (entry.Length > max) throw new InvalidDataException("Tệp vượt giới hạn: " + entry.Name);
        using var input = entry.Open(); using var output = new MemoryStream(); var buffer = new byte[65536]; int read;
        while ((read = input.Read(buffer)) > 0) { if (output.Length + read > max) throw new InvalidDataException("ZIP vượt giới hạn giải nén."); output.Write(buffer, 0, read); }
        return output.ToArray();
    }
    internal static void ValidateZip(ZipArchive zip, long maximum)
    {
        if (zip.Entries.Count > 4000 || zip.Entries.Sum(e => e.Length) > maximum || zip.Entries.GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)
            || zip.Entries.Any(e => e.FullName.StartsWith('/') || e.FullName.Contains('\\') || e.FullName.Split('/').Any(s => s is ".." or ".") || e.FullName.Contains(':')))
            throw new InvalidDataException("ZIP có đường dẫn, mục trùng hoặc dung lượng không an toàn.");
    }
}
