using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace LandPortal.WeeklyImport;

public sealed record WeeklyDateRow(int Stt, string Cell, string RawValue, string CellType, string NumberFormat,
    string DisplayInterpretation, string? StoredDate, string? SwappedCandidate, string? ProposedDate,
    string Status, string[] Warnings);
public sealed record DateDecision(int Stt, string? ConfirmedDate, string? ConfirmedBy);
public sealed record DateDecisions(string ZipSha256, string WorkbookSha256, IReadOnlyList<DateDecision> Rows)
{
    public string? SheetName { get; init; }
}

public static class WeeklyDates
{
    public static bool TryVietnamese(string text, out DateOnly date) => DateOnly.TryParseExact(text.Trim(),
        ["d/M/yyyy", "dd/M/yyyy", "d/MM/yyyy", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateOnly ParseIso(string text) => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? d : throw new InvalidDataException("Ngày xác nhận phải đúng yyyy-MM-dd, không dùng ngày/tháng mơ hồ.");

    public static IReadOnlyList<WeeklyDateRow> Inspect(WeeklyPackage source) => Inspect(WeeklyDateSource.From(source));

    public static IReadOnlyList<WeeklyDateRow> Inspect(WeeklyDateSource source)
    {
        if (WeeklyPackage.Hash(source.Workbook) != source.WorkbookSha256) throw new InvalidDataException("Workbook không khớp hash nguồn.");
        using var zip = new ZipArchive(new MemoryStream(source.Workbook));
        var styles = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(zip.GetEntry("xl/styles.xml")!));
        var formats = styles.Root!.Element(WeeklyPackage.S + "numFmts")?.Elements().ToDictionary(x => (int)x.Attribute("numFmtId")!, x => (string)x.Attribute("formatCode")!) ?? [];
        var xfs = styles.Root.Element(WeeklyPackage.S + "cellXfs")!.Elements().ToArray();
        var sheet = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(zip.GetEntry(source.SheetPath)!));
        var book = WeeklyPackage.Xml(WeeklyPackage.ReadEntry(zip.GetEntry("xl/workbook.xml")!));
        var date1904 = (string?)book.Root!.Element(WeeklyPackage.S + "workbookPr")?.Attribute("date1904") is "1" or "true";
        var shared = zip.GetEntry("xl/sharedStrings.xml") is { } ss ? WeeklyPackage.Xml(WeeklyPackage.ReadEntry(ss)).Root!.Elements(WeeklyPackage.S + "si")
            .Select(x => string.Concat(x.Descendants(WeeklyPackage.S + "t").Select(t => t.Value))).ToArray() : [];
        string Value(XElement cell) => (string?)cell.Attribute("t") == "s" ? shared[int.Parse(cell.Element(WeeklyPackage.S + "v")!.Value, CultureInfo.InvariantCulture)]
            : (string?)cell.Attribute("t") == "inlineStr" ? string.Concat(cell.Descendants(WeeklyPackage.S + "t").Select(t => t.Value)) : cell.Element(WeeklyPackage.S + "v")?.Value ?? "";
        var result = new List<WeeklyDateRow>();
        for (var stt = 1; stt <= source.Count; stt++)
        {
            var excelRow = stt + 6;
            var a = sheet.Descendants(WeeklyPackage.S + "c").Single(x => (string?)x.Attribute("r") == "A" + excelRow);
            if (a.Element(WeeklyPackage.S + "f") != null || Value(a) != stt.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("STT không khớp dòng " + excelRow);
            var cell = sheet.Descendants(WeeklyPackage.S + "c").Single(x => (string?)x.Attribute("r") == "B" + excelRow);
            var type = (string?)cell.Attribute("t") ?? "n";
            var value = Value(cell);
            var formatId = (int)xfs[(int?)cell.Attribute("s") ?? 0].Attribute("numFmtId")!;
            var format = formats.GetValueOrDefault(formatId, formatId == 14 ? "builtin 14: short date (locale-dependent)" : "builtin " + formatId);
            var warnings = new List<string>();
            DateOnly? stored = null, swapped = null; string? proposed = null; var display = value;
            if (cell.Element(WeeklyPackage.S + "f") != null) warnings.Add("Ô B có công thức; không dùng giá trị cache chưa được xác minh.");
            else if (type == "n")
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
                    && double.IsFinite(serial) && serial == Math.Truncate(serial) && serial >= (date1904 ? 0 : 1) && serial < 100000 && (date1904 || serial != 60))
                {
                    stored = DateOnly.FromDateTime((date1904 ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 31)).AddDays(serial - (!date1904 && serial > 60 ? 1 : 0)));
                    if (stored.Value.Day <= 12) swapped = new DateOnly(stored.Value.Year, stored.Value.Day, stored.Value.Month);
                    display = $"Ngày lịch lưu: {stored.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}; hiển thị MM/dd: {stored.Value.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)}";
                    warnings.Add("Định dạng hiển thị không thay đổi ngày lịch của số Excel. Chưa rõ quy ước nhập của chủ dữ liệu: không tự chọn hoặc đảo ngày/tháng.");
                }
                else warnings.Add("Số ngày Excel không hợp lệ, có phần giờ hoặc nằm ngoài phạm vi hỗ trợ.");
            }
            else if (type is "s" or "inlineStr" && TryVietnamese(value, out var parsed)) { stored = parsed; proposed = Iso(parsed); }
            else if (source.AllowDoubleSlashCandidate && type is "s" or "inlineStr"
                && System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), @"^\d{1,2}//\d{1,2}/\d{4}$")
                && TryVietnamese(value.Replace("//", "/"), out var corrected))
            { swapped = corrected; warnings.Add("Ô B có hai dấu /. Ứng viên sửa lỗi ký tự cần chủ dữ liệu xác nhận; chưa dùng làm ngày đăng."); }
            else warnings.Add("Không đọc được ngày text đúng ngày/tháng/năm Việt Nam.");
            var status = type != "n" && stored != null && warnings.Count == 0 ? "VerifiedText" : "NeedsConfirmation";
            result.Add(new(stt, "B" + excelRow, value, type, format, display,
                stored is null ? null : Iso(stored.Value), swapped is null ? null : Iso(swapped.Value), proposed, status, warnings.ToArray()));
        }
        return result;
    }

    public static IReadOnlyDictionary<int, string> Resolve(WeeklyPackage source, DateDecisions? decisions) => Resolve(WeeklyDateSource.From(source), decisions);

    public static IReadOnlyDictionary<int, string> Resolve(WeeklyDateSource source, DateDecisions? decisions)
    {
        var report = Inspect(source); var resolved = report.Where(x => x.Status == "VerifiedText").ToDictionary(x => x.Stt, x => x.StoredDate!);
        if (decisions == null) return resolved;
        if (decisions.ZipSha256 != source.ZipSha256 || decisions.WorkbookSha256 != source.WorkbookSha256
            || (decisions.SheetName ?? WeeklyPackage.SheetName) != source.SheetName
            || decisions.Rows.Any(x => x.Stt < 1 || x.Stt > source.Count) || decisions.Rows.GroupBy(x => x.Stt).Any(x => x.Count() != 1))
            throw new InvalidDataException("Xác nhận ngày không khớp nguồn hoặc trùng STT.");
        foreach (var item in decisions.Rows.Where(x => x.ConfirmedDate != null))
        {
            if (string.IsNullOrWhiteSpace(item.ConfirmedBy)) throw new InvalidDataException($"STT {item.Stt}: thiếu người xác nhận ngày.");
            var date = Iso(ParseIso(item.ConfirmedDate!));
            var row = report.Single(x => x.Stt == item.Stt);
            if (date != row.StoredDate && date != row.SwappedCandidate)
                throw new InvalidDataException($"STT {item.Stt}: ngày xác nhận không xuất phát từ cột B; không lấy ngày từ tiêu đề/PDF hoặc ngày chạy công cụ.");
            resolved[item.Stt] = date;
        }
        return resolved;
    }

    public static Task ReportAsync(WeeklyPackage source, string output) => ReportAsync(WeeklyDateSource.From(source), output);

    public static async Task ReportAsync(WeeklyDateSource source, string output)
    {
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, "date-decisions.json");
        var decisions = File.Exists(path) ? JsonSerializer.Deserialize<DateDecisions>(await File.ReadAllBytesAsync(path))
            ?? throw new InvalidDataException("Tệp xác nhận ngày không hợp lệ.") : null;
        var normalized = Resolve(source, decisions);
        var rows = Inspect(source).Select(row => row.CellType == "n" && normalized.TryGetValue(row.Stt, out var date)
            ? row with { ProposedDate = date, Status = "ConfirmedNumeric", Warnings = ["Ngày từ cột B đã được chủ dữ liệu xác nhận trong date-decisions.json, gắn đúng hash nguồn."] }
            : row).ToArray();
        var tag = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        await WeeklyImporter.AtomicJson(Path.Combine(output, "date-report-" + tag + ".json"), rows);
        var text = new StringBuilder("# Đối chiếu ngày đăng 43 lịch tuần\n\nNguồn duy nhất: cột B, sheet 2.4.1_LichCT. Chỉ đọc workbook, không mở/phân tích PDF. Không dùng tiêu đề, PDF hoặc ngày chạy công cụ để xác định ngày đăng. Không sửa A/B/C. Format 14 phụ thuộc locale nhưng không làm thay đổi ngày lịch lưu trong số Excel. Ứng viên đảo chỉ để đối chiếu quy ước nhập, không được tự áp dụng.\n\n");
        text.AppendLine("| STT | Ô / kiểu | Giá trị B | Định dạng | Hiển thị/diễn giải | Ngày lưu / ứng viên đảo (ISO) | Ngày CMS đã chuẩn hóa (ISO) | Trạng thái | Cảnh báo |\n|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows) text.AppendLine($"| {r.Stt} | {r.Cell} / {r.CellType} | `{r.RawValue}` | {r.NumberFormat} | {r.DisplayInterpretation} | {r.StoredDate ?? "—"} / {r.SwappedCandidate ?? "—"} | {r.ProposedDate ?? "Chờ xác nhận quy ước ô số"} | {r.Status} | {string.Join("; ", r.Warnings).Replace("|", "/")} |");
        await File.WriteAllTextAsync(Path.Combine(output, "DOI-CHIEU-NGAY-" + tag + ".md"), text.ToString(), new UTF8Encoding(true));
        if (!File.Exists(path)) await WeeklyImporter.AtomicJson(path, new DateDecisions(source.ZipSha256, source.WorkbookSha256,
            rows.Where(x => x.Status == "NeedsConfirmation").Select(x => new DateDecision(x.Stt, null, null)).ToArray()));
        var pending = rows.Where(x => x.Status == "NeedsConfirmation").Select(x => x.Stt).ToArray();
        Console.WriteLine($"DATE REPORT: {rows.Length}/43; đã chuẩn hóa {normalized.Count}/43 ({rows.Count(x => x.Status == "VerifiedText")} text, {rows.Count(x => x.Status == "ConfirmedNumeric")} ô số được xác nhận); chờ xác nhận: {(pending.Length == 0 ? "không" : string.Join(", ", pending))}. Không kết nối CMS/SQL.");
    }
}
