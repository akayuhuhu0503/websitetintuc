using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace LandPortal.Infrastructure;

// Format checks are independent of any malware conclusion. Never execute Office or fetch relationships.
public static class OfficeDocumentPolicy
{
    public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public static bool IsDocument(string mime) => mime is "application/pdf" or Docx or Xlsx;
    public static string Mime(string filename) => Path.GetExtension(filename).ToLowerInvariant() switch
    { ".pdf" => "application/pdf", ".docx" => Docx, ".xlsx" => Xlsx, _ => throw new InvalidOperationException("Chỉ nhận PDF, Word DOCX, Excel XLSX (không nhận DOC/XLS cũ hoặc macro).") };
    public static void Validate(string path, string mime, MediaService media, bool managedWithoutAv = false)
    {
        if (mime == "application/pdf") { media.ValidateContent(path, mime, CancellationToken.None); return; }
        using var input = File.OpenRead(path); using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count is < 2 or > 100 || zip.Entries.Sum(x => x.Length) > 20 * 1024 * 1024) throw new InvalidDataException("Office vượt giới hạn ZIP.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zip.Entries)
        {
            if (!names.Add(e.FullName) || e.FullName.StartsWith('/') || e.FullName.Contains('\\') || e.FullName.Contains(':') || e.FullName.Any(char.IsControl) || e.FullName.Split('/').Any(x => x is "." or "..") || e.Length > 2 * 1024 * 1024 || e.Length > Math.Max(1024, e.CompressedLength * 100)) throw new InvalidDataException("Office ZIP không an toàn.");
            if (e.FullName.Contains("vba", StringComparison.OrdinalIgnoreCase) || e.FullName.Contains("activeX", StringComparison.OrdinalIgnoreCase) || e.FullName.Contains("embeddings/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Không nhận macro/nhúng/ActiveX.");
            if (!e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) continue;
            using var reader = XmlReader.Create(e.Open(), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
            var xml = XDocument.Load(reader);
            if (xml.Descendants().Any(x => x.Attribute("TargetMode")?.Value.Equals("External", StringComparison.OrdinalIgnoreCase) == true || x.Name.LocalName is "oleObject" or "externalLink" or "altChunk" or "instrText" or "fldSimple"
                || x.Attributes().Any(a => (a.Name.LocalName is "ContentType" or "Type") && new[] { "macroEnabled", "vbaProject", "oleObject", "activeX", "externalLink" }.Any(t => a.Value.Contains(t, StringComparison.OrdinalIgnoreCase)))
                || mime == Xlsx && x.Name.LocalName is "f" or "definedName")) throw new InvalidDataException("Office có quan hệ ngoài, nội dung chủ động hoặc công thức; cần bản giá trị thuần.");
        }
        var main = mime == Docx ? "word/document.xml" : "xl/workbook.xml";
        var types = zip.GetEntry("[Content_Types].xml") ?? throw new InvalidDataException("Thiếu Content Types.");
        using var typeReader = XmlReader.Create(types.Open(), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
        var expected = mime == Docx ? Docx + ".main+xml" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
        if (!managedWithoutAv)
        {
            if (zip.GetEntry(main) == null || !XDocument.Load(typeReader).Descendants().Any(x => x.Attribute("ContentType")?.Value == expected)) throw new InvalidDataException("Nội dung Office không khớp đuôi/MIME.");
            return;
        }
        var typesDocument = XDocument.Load(typeReader);
        if (zip.GetEntry(main) == null || !typesDocument.Descendants().Any(x => x.Name.LocalName == "Override"
            && x.Attribute("PartName")?.Value == "/" + main && x.Attribute("ContentType")?.Value == expected))
            throw new InvalidDataException("Nội dung Office không khớp đuôi/MIME.");
        using var mainReader = XmlReader.Create(zip.GetEntry(main)!.Open(), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
        var root = XDocument.Load(mainReader).Root;
        var expectedRoot = mime == Docx
            ? XName.Get("document", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")
            : XName.Get("workbook", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        if (root?.Name != expectedRoot) throw new InvalidDataException("Cấu trúc tài liệu Office không hợp lệ.");
        var relationships = zip.GetEntry("_rels/.rels") ?? throw new InvalidDataException("Thiếu quan hệ gói Office.");
        using var relReader = XmlReader.Create(relationships.Open(), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
        if (!XDocument.Load(relReader).Descendants().Any(x => x.Name.LocalName == "Relationship"
            && x.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
            && x.Attribute("Target")?.Value.TrimStart('/') == main)) throw new InvalidDataException("Quan hệ Office không khớp tài liệu.");
    }
}
