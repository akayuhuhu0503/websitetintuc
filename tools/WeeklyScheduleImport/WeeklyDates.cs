using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LandPortal.WeeklyImport;

public sealed record WeeklyDateRow(int Stt, string Cell, string RawValue, string CellType, string NumberFormat,
    string DisplayInterpretation, string? StoredDate, string? SwappedCandidate, string? ProposedDate,
    string Status, string[] Warnings);
public sealed record DateDecision(int Stt, string? ConfirmedDate, string? ConfirmedBy);
public sealed record DateDecisions(string ZipSha256, string WorkbookSha256, IReadOnlyList<DateDecision> Rows);

public static class WeeklyDates
{
    public static bool TryVietnamese(string text, out DateOnly date) => DateOnly.TryParseExact(text.Trim(),
        ["d/M/yyyy", "dd/M/yyyy", "d/MM/yyyy", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateOnly ParseIso(string text) => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? d : throw new InvalidDataException("Ngày xác nhận phải đúng yyyy-MM-dd, không dùng ngày/tháng mơ hồ.");

    public static IReadOnlyList<WeeklyDateRow> Inspect(WeeklyPackage source)
    {
        using var zip = new ZipArchive(new MemoryStream(source.Workbook));
        var styles = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(zip.GetEntry("xl/styles.xml")!));
        var formats = styles.Root!.Element(WeeklyPackage.S + "numFmts")?.Elements().ToDictionary(x => (int)x.Attribute("numFmtId")!, x => (string)x.Attribute("formatCode")!) ?? [];
        var xfs = styles.Root.Element(WeeklyPackage.S + "cellXfs")!.Elements().ToArray();
        var sheet = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(zip.GetEntry(source.SheetPath)!));
        var book = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(zip.GetEntry("xl/workbook.xml")!));
        var date1904 = (string?)book.Root!.Element(WeeklyPackage.S + "workbookPr")?.Attribute("date1904") is "1" or "true";
        var result = new List<WeeklyDateRow>();
        foreach (var row in source.Rows)
        {
            var cell = sheet.Descendants(WeeklyPackage.S + "c").Single(x => (string?)x.Attribute("r") == "B" + row.ExcelRow);
            var formatId = (int)xfs[(int?)cell.Attribute("s") ?? 0].Attribute("numFmtId")!;
            var format = formats.GetValueOrDefault(formatId, formatId == 14 ? "builtin 14: short date (locale-dependent)" : "builtin " + formatId);
            var warnings = row.Warnings.Where(x => !x.StartsWith("Ngày đăng là số Excel", StringComparison.Ordinal)).ToList();
            DateOnly? stored = null, swapped = null; string? proposed = null; var display = row.DateValue;
            if (row.DateType == "n")
            {
                if (double.TryParse(row.DateValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
                    && double.IsFinite(serial) && serial == Math.Truncate(serial) && serial >= (date1904 ? 0 : 1) && serial < 100000 && (date1904 || serial != 60))
                {
                    stored = DateOnly.FromDateTime((date1904 ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 31)).AddDays(serial - (!date1904 && serial > 60 ? 1 : 0)));
                    if (stored.Value.Day <= 12) swapped = new DateOnly(stored.Value.Year, stored.Value.Day, stored.Value.Month);
                    display = $"vi-VN: {stored:dd/MM/yyyy}; en-US: {stored:MM/dd/yyyy} (không biết locale của máy lập Excel)";
                    proposed = swapped is not null ? Iso(swapped.Value) : Iso(stored.Value);
                    warnings.Add("Ô số: ngày giải mã và ứng viên đảo ngày/tháng chỉ là đối chiếu, cần chủ dữ liệu xác nhận; không tự chọn theo tiêu đề.");
                }
                else warnings.Add("Số ngày Excel không hợp lệ, có phần giờ hoặc nằm ngoài phạm vi hỗ trợ.");
            }
            else if (row.DateType is "s" or "inlineStr" && TryVietnamese(row.DateValue, out var parsed)) { stored = parsed; proposed = Iso(parsed); }
            else warnings.Add("Không đọc được ngày text đúng ngày/tháng/năm Việt Nam.");
            var period = Regex.Match(row.Title, @"Từ\s*ngày\s*(\d{1,2}/\d{1,2}/\d{4})\s*đến\s*ngày\s*(\d{1,2}/\d{1,2}/\d{4})", RegexOptions.IgnoreCase);
            if (stored != null && period.Success && TryVietnamese(period.Groups[1].Value, out var start) && TryVietnamese(period.Groups[2].Value, out var end)
                && (stored < start || stored > end)) warnings.Add("Ngày lưu tại B nằm ngoài khoảng tuần trong tiêu đề; chỉ cảnh báo, không lấy ngày đầu tuần thay thế.");
            var status = row.DateType != "n" && stored != null && warnings.Count == 0 ? "VerifiedText" : "NeedsConfirmation";
            result.Add(new(row.Stt, "B" + row.ExcelRow, row.DateValue, row.DateType, format, display,
                stored is null ? null : Iso(stored.Value), swapped is null ? null : Iso(swapped.Value), proposed, status, warnings.ToArray()));
        }
        return result;
    }

    public static IReadOnlyDictionary<int, string> Resolve(WeeklyPackage source, DateDecisions? decisions)
    {
        var report = Inspect(source); var resolved = report.Where(x => x.Status == "VerifiedText").ToDictionary(x => x.Stt, x => x.StoredDate!);
        if (decisions == null) return resolved;
        if (decisions.ZipSha256 != source.ZipSha256 || decisions.WorkbookSha256 != source.WorkbookSha256
            || decisions.Rows.Any(x => x.Stt is < 1 or > 43) || decisions.Rows.GroupBy(x => x.Stt).Any(x => x.Count() != 1))
            throw new InvalidDataException("Xác nhận ngày không khớp nguồn hoặc trùng STT.");
        foreach (var item in decisions.Rows.Where(x => x.ConfirmedDate != null))
        {
            if (string.IsNullOrWhiteSpace(item.ConfirmedBy)) throw new InvalidDataException($"STT {item.Stt}: thiếu người xác nhận ngày.");
            resolved[item.Stt] = Iso(ParseIso(item.ConfirmedDate!));
        }
        return resolved;
    }

    public static async Task ReportAsync(WeeklyPackage source, string output)
    {
        Directory.CreateDirectory(output);
        var rows = Inspect(source); var tag = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        await WeeklyImporter.AtomicJson(Path.Combine(output, "date-report-" + tag + ".json"), rows);
        var text = new StringBuilder("# Đối chiếu ngày đăng 43 lịch tuần\n\nChỉ đọc nguồn. Ngày đề xuất CHƯA phải xác nhận. Không sửa A/B/C hoặc PDF. Format 14 phụ thuộc locale; hai cách hiển thị đều được liệt kê.\n\n");
        text.AppendLine("| STT | Ô / kiểu | Giá trị B | Định dạng | Hiển thị/diễn giải | Đề xuất CMS | Trạng thái | Cảnh báo |\n|---|---|---|---|---|---|---|---|");
        foreach (var r in rows) text.AppendLine($"| {r.Stt} | {r.Cell} / {r.CellType} | `{r.RawValue}` | {r.NumberFormat} | {r.DisplayInterpretation} | {r.ProposedDate ?? "Chưa xác định"} | {r.Status} | {string.Join("; ", r.Warnings).Replace("|", "/")} |");
        await File.WriteAllTextAsync(Path.Combine(output, "DOI-CHIEU-NGAY-" + tag + ".md"), text.ToString(), new UTF8Encoding(true));
        var path = Path.Combine(output, "date-decisions.json");
        if (!File.Exists(path)) await WeeklyImporter.AtomicJson(path, new DateDecisions(source.ZipSha256, source.WorkbookSha256,
            rows.Where(x => x.Status == "NeedsConfirmation").Select(x => new DateDecision(x.Stt, null, null)).ToArray()));
        Console.WriteLine($"DATE REPORT: {rows.Count}/43; {rows.Count(x => x.Status == "VerifiedText")} ngày text rõ; cần xác nhận STT {string.Join(", ", rows.Where(x => x.Status == "NeedsConfirmation").Select(x => x.Stt))}. Không kết nối CMS/SQL.");
    }
}
