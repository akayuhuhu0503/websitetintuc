using System.Text;
using System.Text.Json;

namespace LandPortal.WeeklyImport;

public static class PlanningCommand
{
    public static async Task RunAsync(string[] args)
    {
        var action = args[0];
        if (action == "planning-pdf-check") { await PlanningPdfCheck.RunAsync(args); return; }
        if (action is not ("planning-report" or "planning-inspect" or "planning-preflight" or "planning-dry-run" or "planning-import" or "planning-reconcile" or "planning-sync-links")) throw new ArgumentException("Lệnh QHKHSDD không hợp lệ.");
        var options = new Dictionary<string,string>();
        for (var i = 1; i < args.Length; i++)
        {
            var key = args[i];
            if (key == "--all") { if (action != "planning-import" || !options.TryAdd(key, "true")) throw new ArgumentException("--all chỉ dành cho Import."); continue; }
            if (key is not ("--zip" or "--out" or "--site" or "--stt" or "--pdf-overrides") || i + 1 >= args.Length || args[i + 1].StartsWith("--") || !options.TryAdd(key, args[++i])) throw new ArgumentException("Tham số sai; không nhận password/cookie/config/SQL.");
        }
        if (options.ContainsKey("--stt") && (action != "planning-import" || options.ContainsKey("--all"))) throw new ArgumentException("--stt chỉ dùng nhập thử, không ghép --all.");
        string Required(string key) => options.GetValueOrDefault(key) ?? throw new ArgumentException("Thiếu " + key);
        var output = Path.GetFullPath(Required("--out"));
        // Freeze the reviewed scope/dates into each new run. Resume uses only that run's copies.
        var initial = action is "planning-report" or "planning-inspect";
        var scopePath = Path.Combine(initial ? AppContext.BaseDirectory : output, "source-scope.json");
        var scope = File.Exists(scopePath) ? JsonSerializer.Deserialize<PlanningScope>(await File.ReadAllBytesAsync(scopePath))
            ?? throw new InvalidDataException("Phạm vi nguồn không hợp lệ.") : null;
        var replacements=options.TryGetValue("--pdf-overrides",out var prepared) ? PlanningPdfOverrides.Read(prepared) : null;
        var source = PlanningPackage.Read(Required("--zip"), scope, replacements);
        var bundledDatesPath = Path.Combine(AppContext.BaseDirectory, "date-decisions.json");
        var bundledDates = initial && File.Exists(bundledDatesPath) ? JsonSerializer.Deserialize<DateDecisions>(await File.ReadAllBytesAsync(bundledDatesPath))
            ?? throw new InvalidDataException("Xác nhận ngày đi kèm không hợp lệ.") : null;
        if (initial) _ = WeeklyDates.Resolve(source.Dates, bundledDates);
        if (action == "planning-report") { if (Directory.Exists(output)) throw new IOException("Báo cáo cần thư mục mới."); await WriteReportAsync(source,output,bundledDates); return; }
        if (action == "planning-inspect")
        {
            if (Directory.Exists(output)) throw new IOException("Inspect cần thư mục mới để giữ nguồn và nhật ký cũ.");
            Directory.CreateDirectory(output);
            File.Copy(source.Path, Path.Combine(output, "source-backup.zip"), false);
            if (PlanningPackage.FileHash(Path.Combine(output, "source-backup.zip")) != source.Dates.ZipSha256) throw new IOException("Bản sao ZIP không khớp.");
            await File.WriteAllBytesAsync(Path.Combine(output, "original.xlsx"), source.Dates.Workbook);
            await WriteReportAsync(source, output, bundledDates); return;
        }
        if (!Directory.Exists(output) || PlanningPackage.FileHash(Path.Combine(output, "source-backup.zip")) != source.Dates.ZipSha256
            || PlanningPackage.FileHash(Path.Combine(output, "original.xlsx")) != source.Dates.WorkbookSha256) throw new InvalidDataException("Cần thư mục Inspect gốc với bản sao ZIP/Excel đúng hash.");
        using var gate = new FileStream(Path.Combine(output, "planning-operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var datesPath = Path.Combine(output, "date-decisions.json");
        var decisions = File.Exists(datesPath) ? JsonSerializer.Deserialize<DateDecisions>(await File.ReadAllBytesAsync(datesPath)) : null;
        _ = WeeklyDates.Resolve(source.Dates, decisions); // Reject cross-sheet/date approvals before logging in.
        var planPath = Path.Combine(output, "planning-web-plan.json"); var journalPath = Path.Combine(output, "planning-web-journal.json");
        if (action == "planning-dry-run" && (File.Exists(planPath) || File.Exists(journalPath))) throw new IOException("Giữ kế hoạch/nhật ký đã có. Tiếp tục bằng Import/Reconcile; nguồn hoặc ngày thay đổi cần đợt Inspect/DryRun mới và đối soát tái sử dụng.");
        if (Console.IsInputRedirected) throw new UnauthorizedAccessException("Đăng nhập ở console, không lấy mật khẩu từ file/pipe.");
        using var cms = new WebCmsClient(new Uri(options.GetValueOrDefault("--site", "https://gdla.gov.vn/")));
        Console.WriteLine("CMS: " + cms.Origin.GetLeftPart(UriPartial.Authority));
        await cms.LoginAsync(question =>
        {
            Console.Write("Tài khoản CMS: "); var user = Console.ReadLine() ?? "";
            Console.Write("Mật khẩu (không hiển thị/lưu): "); var secret = new StringBuilder();
            while (true) { var k = Console.ReadKey(true); if (k.Key == ConsoleKey.Enter) break; if (k.Key == ConsoleKey.Backspace) { if (secret.Length > 0) secret.Length--; } else if (!char.IsControl(k.KeyChar) && secret.Length < 256) secret.Append(k.KeyChar); }
            Console.WriteLine(); Console.WriteLine("CAPTCHA: " + question); Console.Write("Trả lời: "); var answer = Console.ReadLine() ?? "";
            var value = (user, secret.ToString(), answer); secret.Clear(); return Task.FromResult(value);
        });
        var importer = new PlanningWebImporter(cms);
        if (action == "planning-preflight") { await importer.PreflightAsync(); Console.WriteLine("PREFLIGHT PASS: quyền tạo StaticPage/chuyên mục 77/tải tài liệu. Chưa ghi CMS; chưa chứng minh upload thật."); return; }
        if (action == "planning-dry-run")
        {
            var plan = await importer.PlanAsync(source, decisions);
            await WeeklyImporter.AtomicJson(planPath, plan); await WeeklyImporter.AtomicJson(journalPath, new PlanningJournal(PlanningWebImporter.PlanHash(plan), []));
            Console.WriteLine("DRY-RUN: " + string.Join(", ", plan.Rows.GroupBy(x => x.Action).Select(g => g.Key + "=" + g.Count())) + ". Chưa ghi CMS; xem lý do từng STT trong planning-web-plan.json."); return;
        }
        var savedPlan = JsonSerializer.Deserialize<PlanningPlan>(await File.ReadAllBytesAsync(planPath)) ?? throw new InvalidDataException("Thiếu kế hoạch.");
        var journal = JsonSerializer.Deserialize<PlanningJournal>(await File.ReadAllBytesAsync(journalPath)) ?? throw new InvalidDataException("Thiếu nhật ký.");
        if (action == "planning-import")
            await importer.ImportAsync(source, savedPlan, journal, journalPath, decisions, options.ContainsKey("--all"), options.TryGetValue("--stt", out var stt) ? int.Parse(stt, System.Globalization.CultureInfo.InvariantCulture) : null);
        else if (action == "planning-reconcile") await importer.ReconcileAsync(source, savedPlan, journal, decisions);
        else
        {
            var links = await importer.SyncAsync(source, savedPlan, journal, decisions); var tag = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
            source.WriteWorkbook(Path.Combine(output, "QHKHSDD-links-" + tag + ".xlsx"), links.ToDictionary(x => x.Stt, x => x.Url));
            await WeeklyImporter.AtomicJson(Path.Combine(output, "planning-links-" + tag + ".json"), links);
            Console.WriteLine($"SYNC LINKS: {links.Count(x => x.Url != null)}/{source.Rows.Count} URL đã kiểm tra revision, ngày Excel và toàn bộ PDF công khai. Không duyệt/xuất bản."); return;
        }
        Console.WriteLine($"{action}: {journal.Rows.Count(x => x.Stage == "Verified")}/{source.Rows.Count} đối soát; {savedPlan.Rows.Count(x => x.Action == "Blocked")} đang bị chặn. Chỉ lưu nháp/tái sử dụng, không duyệt/xuất bản.");
    }
    public static async Task WriteReportAsync(PlanningPackage source, string output, DateDecisions? confirmedDates = null)
    {
        Directory.CreateDirectory(output); var dates = WeeklyDates.Inspect(source.Dates);
        var normalized = WeeklyDates.Resolve(source.Dates, confirmedDates);
        var dateRows = dates.Select(x => normalized.TryGetValue(x.Stt, out var date) && x.Status == "NeedsConfirmation"
            ? x with { ProposedDate = date, Status = "ConfirmedByOwner", Warnings = ["Chủ dữ liệu đã xác nhận ngày cột B; không suy từ tiêu đề/PDF."] } : x).ToArray();
        await WeeklyImporter.AtomicJson(Path.Combine(output, "planning-inventory.json"), new { profile = PlanningPackage.Profile, sheet = source.Dates.SheetName, source.Dates.ZipSha256, source.Dates.WorkbookSha256, source.ScopeSha256, rows = source.Rows, archives = source.Archives, dates = dateRows });
        var decisions = confirmedDates ?? new DateDecisions(source.Dates.ZipSha256, source.Dates.WorkbookSha256, dates.Where(x => x.Status == "NeedsConfirmation").Select(x => new DateDecision(x.Stt, null, null)).ToArray()) { SheetName = source.Dates.SheetName };
        if (File.Exists(Path.Combine(output,"date-decisions.json"))) throw new IOException("Không ghi đè xác nhận ngày.");
        await WeeklyImporter.AtomicJson(Path.Combine(output, "date-decisions.json"), decisions);
        if (source.Scope != null)
        {
            var scopePath = Path.Combine(output, "source-scope.json");
            if (File.Exists(scopePath)) throw new IOException("Không ghi đè phạm vi nguồn.");
            await WeeklyImporter.AtomicJson(scopePath, source.Scope);
        }
        source.WriteWorkbook(Path.Combine(output, "QHKHSDD-pages-reviewed.xlsx"), new Dictionary<int,string?>());
        var report = new StringBuilder("# Kiểm kê QHKHSDD 2.4.2\n\nChỉ đọc nguồn, chưa kết nối CMS/SQL. Cột B là nguồn ngày duy nhất. Không dùng tiêu đề/PDF. Kiểm tra hash/số trang không thay thế kiểm định an toàn của CMS.\n\n");
        if(source.PdfOverrides!=null)
        {
            report.AppendLine($"PDF xử lý được chọn tường minh theo manifest SHA256 `{source.PdfOverrides.ManifestSha256}`; kế hoạch gắn hash này, không dùng lại kế hoạch/nhật ký cũ khác byte.\n");
            foreach(var row in source.Rows) foreach(var file in row.Files.Where(f=>f.DerivativeNotice!=null))
                report.AppendLine($"- STT {row.Stt}, `{file.Name}`: {file.DerivativeNotice} SHA nguồn `{file.OriginalSha256}` → bản xử lý `{file.Sha256}`.");
        }
        report.AppendLine($"Sheet thực tế: `{source.Dates.SheetName}`; {source.Rows.Count} hồ sơ. Tuyến có sẵn `{PlanningPackage.Route}`, StaticPage/chuyên mục 77; không phải Tin tức 48.\n");
        report.AppendLine($"Ngày cột B đã chuẩn hóa: **{normalized.Count}/{source.Rows.Count}**; chưa xác nhận: {source.Rows.Count - normalized.Count}.\n");
        report.AppendLine("| STT | Ô B / kiểu / định dạng | Giá trị gốc | Ngày lưu / ứng viên | Ngày CMS ISO | Trạng thái ngày | Tệp | Trang Excel → PDF | Chặn/cần rà soát |\n|---|---|---|---|---|---|---|---|---|");
        foreach (var row in source.Rows)
        {
            var d = dateRows.Single(x => x.Stt == row.Stt); var blocks = row.Blocks.Concat(row.Files.SelectMany(x => x.Blocks.Select(b => x.Name + ": " + b))).ToList();
            if (d.Status == "NeedsConfirmation") blocks.Add("Chờ xác nhận ngày cột B");
            report.AppendLine($"| {row.Stt} | {d.Cell} / {d.CellType} / {d.NumberFormat} | {d.RawValue} | {d.StoredDate ?? "—"} / {d.SwappedCandidate ?? "—"} | {d.ProposedDate ?? "Chờ xác nhận"} | {d.Status} | {row.Files.Count} | {row.Source.ExcelPages} → {(row.Files.All(x => x.Pages.HasValue) && row.Files.Count > 0 ? row.Source.Pages.ToString() : "Chưa đủ ánh xạ")} | {string.Join("; ",blocks)} |");
        }
        foreach (var exclusion in source.Scope?.ExcludedArchives ?? [])
            report.AppendLine($"\nĐã loại khỏi phạm vi nhập: `{exclusion.Entry}`, SHA256 `{exclusion.Sha256}`. {exclusion.Reason} Không xóa hoặc sửa ZIP nguồn; PDF trực tiếp vẫn phải qua đầy đủ kiểm định CMS.");
        report.AppendLine("\nZIP phụ lục chưa có xác nhận loại vẫn chặn hồ sơ tương ứng. STT 24 giữ cả hai PDF, cộng số trang thực tế. Bản sao Excel chỉ thay E khi đã đếm đủ tài liệu; D chỉ điền sau SyncLinks xác minh công khai. A/B/C và các sheet khác giữ nguyên.\n");
        await File.WriteAllTextAsync(Path.Combine(output, "BAO-CAO-QHKHSDD.md"), report.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"INSPECT: {source.Rows.Count} hồ sơ; {source.Rows.Sum(x=>x.Files.Count(f=>f.Pages.HasValue))} PDF đọc được; {source.Rows.Count(x=>x.Files.Count==0)} thiếu tệp; {normalized.Count}/{source.Rows.Count} ngày chuẩn hóa; {source.Rows.Count-normalized.Count} ngày cần xác nhận; {source.Rows.Count(x=>!x.CanImport)} hồ sơ bị chặn tệp. Chưa ghi CMS.");
    }
}
