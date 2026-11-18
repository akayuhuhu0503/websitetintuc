using System.IO.Compression;

namespace LandPortal.WeeklyImport;

// Date review reads the workbook only. It never opens/parses a PDF or infers a date from its title.
public sealed record WeeklyDateSource(string ZipSha256, string WorkbookSha256, byte[] Workbook, string SheetPath)
{
    public int Count { get; init; } = 43;
    public string SheetName { get; init; } = WeeklyPackage.SheetName;
    public bool AllowDoubleSlashCandidate { get; init; }
    public static WeeklyDateSource From(WeeklyPackage source) => new(source.ZipSha256, source.WorkbookSha256, source.Workbook, source.SheetPath);

    public static WeeklyDateSource Read(string path)
    {
        if (new FileInfo(path).Length > 128 * 1024 * 1024) throw new InvalidDataException("ZIP vượt 128 MiB.");
        var bytes = File.ReadAllBytes(path);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        WeeklyPackage.ValidateZip(zip, 128 * 1024 * 1024);
        var entry = zip.Entries.SingleOrDefault(x => x.FullName == WeeklyPackage.WorkbookName)
            ?? throw new InvalidDataException("Thiếu hoặc trùng workbook đúng tên.");
        var workbook = WeeklyPackage.ReadEntry(entry, 32 * 1024 * 1024);
        using var xlsx = new ZipArchive(new MemoryStream(workbook));
        WeeklyPackage.ValidateZip(xlsx, 128 * 1024 * 1024);
        var book = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(xlsx.GetEntry("xl/workbook.xml")!));
        var rels = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(xlsx.GetEntry("xl/_rels/workbook.xml.rels")!));
        var sheet = book.Descendants(WeeklyPackage.S + "sheet").Single(x => (string?)x.Attribute("name") == WeeklyPackage.SheetName);
        var relation = rels.Root!.Elements().Single(x => (string?)x.Attribute("Id") == (string?)sheet.Attribute(WeeklyPackage.R + "id"));
        if ((string?)relation.Attribute("TargetMode") == "External") throw new InvalidDataException("Sheet ngoài workbook.");
        var target = (string)relation.Attribute("Target")!;
        var sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        if (sheetPath.Contains("..") || sheetPath.Contains('\\') || xlsx.GetEntry(sheetPath) == null)
            throw new InvalidDataException("Đường dẫn sheet không hợp lệ.");
        return new(WeeklyPackage.Hash(bytes), WeeklyPackage.Hash(workbook), workbook, sheetPath);
    }
}
