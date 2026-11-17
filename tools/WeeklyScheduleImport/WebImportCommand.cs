using System.Text;
using System.Text.Json;

namespace LandPortal.WeeklyImport;

public static class WebImportCommand
{
    public static async Task RunAsync(string[] args)
    {
        var command = args[0];
        if (command is not ("web-preflight" or "web-dry-run" or "web-import" or "web-reconcile" or "web-sync-links" or "web-date-report" or "web-date-plan" or "web-sync-dates" or "web-verify-dates"))
            throw new ArgumentException("Dùng web-preflight | web-dry-run | web-import | web-reconcile | web-sync-links | web-date-report | web-date-plan | web-sync-dates | web-verify-dates.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            var key = args[i];
            if (key == "--all") { if (!options.TryAdd(key, "true") || command != "web-import") throw new ArgumentException("--all chỉ dành cho web-import sau hồ sơ thử."); continue; }
            if (key is not ("--zip" or "--out" or "--site" or "--date-plan" or "--stt") || i + 1 >= args.Length || args[i + 1].StartsWith("--") || !options.TryAdd(key, args[++i]))
                throw new ArgumentException("Tham số không hợp lệ. Không nhận mật khẩu, cookie, SQL hoặc config private qua tham số.");
        }
        if (options.ContainsKey("--date-plan") && command is not ("web-date-plan" or "web-sync-dates") || options.ContainsKey("--stt") && command != "web-sync-dates") throw new ArgumentException("--date-plan chỉ dành cho kế hoạch/đồng bộ ngày; --stt chỉ dành cho web-sync-dates.");
        string Required(string key) => options.TryGetValue(key, out var value) ? value : throw new ArgumentException("Thiếu " + key);
        var sourcePath = Path.GetFullPath(Required("--zip")); var package = WeeklyPackage.Read(sourcePath);
        if (command == "web-date-report") { await WeeklyDates.ReportAsync(package, Path.GetFullPath(Required("--out"))); return; }
        var origin = new Uri(options.GetValueOrDefault("--site", "https://gdla.gov.vn/"));
        using var cms = new WebCmsClient(origin);
        if (Console.IsInputRedirected) throw new UnauthorizedAccessException("Đăng nhập tại console trực tiếp; không đọc mật khẩu từ file/pipe.");
        var output = command == "web-preflight" ? null : Path.GetFullPath(Required("--out"));
        if (output != null && command != "web-dry-run" && (!Directory.Exists(output)
            || WeeklyPackage.Hash(File.ReadAllBytes(Path.Combine(output, "source-backup.zip"))) != package.ZipSha256
            || WeeklyPackage.Hash(File.ReadAllBytes(Path.Combine(output, "original.xlsx"))) != package.WorkbookSha256))
            throw new InvalidDataException("Cần thư mục Dry-run gốc, có bản sao ZIP/Excel đúng hash.");
        if (output != null && command == "web-dry-run" && Directory.Exists(output)) throw new IOException("Dry-run cần thư mục mới; giữ nhật ký cũ để tiếp tục, không xóa/tạo lại.");
        // The only authentication material lives in process memory, including the cookie jar.
        Console.WriteLine("CMS: " + origin.GetLeftPart(UriPartial.Authority));
        await cms.LoginAsync(question =>
        {
            Console.Write("Tên đăng nhập CMS: "); var user = Console.ReadLine() ?? "";
            Console.Write("Mật khẩu (không hiển thị): "); var secret = new StringBuilder();
            while (true) { var key = Console.ReadKey(true); if (key.Key == ConsoleKey.Enter) break; if (key.Key == ConsoleKey.Backspace) { if (secret.Length > 0) secret.Length--; } else if (!char.IsControl(key.KeyChar) && secret.Length < 256) secret.Append(key.KeyChar); }
            Console.WriteLine(); Console.WriteLine("CAPTCHA CMS: " + question); Console.Write("Câu trả lời: "); var answer = Console.ReadLine() ?? "";
            var result = (user, secret.ToString(), answer); secret.Clear(); return Task.FromResult(result);
        });
        var importer = new WebWeeklyImporter(cms);
        if (command == "web-preflight") { await importer.PreflightAsync(); Console.WriteLine("WEB PREFLIGHT PASS: đăng nhập/CSRF, tạo StaticPage, chuyên mục 76 và chức năng PDF khả dụng. Chưa upload/chưa tạo bài; chưa chứng minh quyền kho/scanner bằng ghi thực tế."); return; }
        if (command == "web-dry-run")
        {
            var plan = await importer.PlanAsync(package);
            Directory.CreateDirectory(output!);
            using var fileLock = new FileStream(Path.Combine(output!, "web-operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            File.Copy(sourcePath, Path.Combine(output!, "source-backup.zip"), false);
            if (WeeklyPackage.Hash(File.ReadAllBytes(Path.Combine(output!, "source-backup.zip"))) != package.ZipSha256) throw new IOException("Bản sao ZIP không khớp.");
            await File.WriteAllBytesAsync(Path.Combine(output!, "original.xlsx"), package.Workbook);
            await WeeklyImporter.AtomicJson(Path.Combine(output!, "inventory.json"), new { summary = package.Summary(), rows = package.Rows });
            await WeeklyImporter.AtomicJson(Path.Combine(output!, "web-plan.json"), plan);
            await WeeklyImporter.AtomicJson(Path.Combine(output!, "web-journal.json"), new WebJournal(plan.Origin, plan.UserId, plan.ZipSha256, plan.WorkbookSha256, []));
            package.WriteWorkbook(Path.Combine(output!, "LichCT-pages-reviewed.xlsx"), new Dictionary<int, string?>());
            Console.WriteLine($"WEB DRY-RUN PASS: {plan.Rows.Count(x => x.Action == "Create")} tạo nháp, {plan.Rows.Count(x => x.Action == "Reuse")} tái sử dụng; STT 39–42 cần rà soát. Không ghi CMS. Dùng cùng thư mục này cho mọi lần tiếp tục."); return;
        }
        using var operationLock = new FileStream(Path.Combine(output!, "web-operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var savedPlan = JsonSerializer.Deserialize<WebPlan>(await File.ReadAllBytesAsync(Path.Combine(output!, "web-plan.json")))!;
        var journalPath = Path.Combine(output!, "web-journal.json");
        var journal = JsonSerializer.Deserialize<WebJournal>(await File.ReadAllBytesAsync(journalPath))!;
        var datesPath = Path.Combine(output!, "date-decisions.json");
        var dates = File.Exists(datesPath) ? JsonSerializer.Deserialize<DateDecisions>(await File.ReadAllBytesAsync(datesPath)) : null;
        if (command is "web-date-plan" or "web-sync-dates" or "web-verify-dates")
        {
            var sync = new WebDateSync(cms);
            if (command == "web-verify-dates")
            {
                var verified = await sync.VerifyPublicAsync(package, savedPlan, journal, dates);
                var file = Path.Combine(output!, "web-date-public-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + ".json");
                await WeeklyImporter.AtomicJson(file, verified); Console.WriteLine("Đã đối soát ngày công khai và PDF; xem " + file); return;
            }
            var planName = options.GetValueOrDefault("--date-plan", "web-date-plan.json");
            if (Path.GetFileName(planName) != planName || !planName.StartsWith("web-date-plan", StringComparison.Ordinal) || !planName.EndsWith(".json", StringComparison.Ordinal)) throw new ArgumentException("--date-plan phải là tên web-date-plan*.json trong thư mục nhật ký, không chứa đường dẫn.");
            var planPath = Path.Combine(output!, planName); var dateJournalPath = Path.ChangeExtension(planPath, ".journal.json");
            if (command == "web-date-plan")
            {
                if (File.Exists(planPath) || File.Exists(dateJournalPath)) throw new IOException("Giữ kế hoạch/nhật ký cũ. Muốn kế hoạch mới, dùng --date-plan web-date-plan-2.json.");
                var datePlan = await sync.PlanAsync(package, savedPlan, journal, dates);
                await WeeklyImporter.AtomicJson(planPath, datePlan);
                Console.WriteLine("DATE PLAN (chỉ đọc CMS): " + string.Join(", ", datePlan.Rows.GroupBy(x => x.Status).Select(x => x.Key + "=" + x.Count()))); return;
            }
            var bytes = await File.ReadAllBytesAsync(planPath); var planned = JsonSerializer.Deserialize<WebDatePlan>(bytes)!;
            var changes = File.Exists(dateJournalPath) ? JsonSerializer.Deserialize<WebDateJournal>(await File.ReadAllBytesAsync(dateJournalPath))!
                : new WebDateJournal(WeeklyPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(planned, WeeklyPackage.Json)), []);
            await sync.ApplyAsync(package, savedPlan, journal, dates, planned, changes, dateJournalPath,
                options.TryGetValue("--stt", out var stt) ? int.Parse(stt, System.Globalization.CultureInfo.InvariantCulture) : null);
            Console.WriteLine($"DATE SYNC: {changes.Rows.Count(x => x.Stage == "Verified")} hồ sơ đối soát; {planned.Rows.Count(x => x.Status == "NeedsConfirmation")} chờ xác nhận ngày. Chỉ lưu bản nháp, chưa duyệt/xuất bản; không tải lại PDF."); return;
        }
        if (command == "web-import")
        {
            await importer.ImportAsync(package, savedPlan, journal, journalPath, options.ContainsKey("--all"), dates);
            Console.WriteLine($"WEB IMPORT: {journal.Rows.Count(x => x.Stage == "Verified")}/43 đã đối soát PDF/bài; chỉ lưu nháp hoặc tái sử dụng hồ sơ, không duyệt/xuất bản. STT 39–42 giữ nguyên tiêu đề để cán bộ rà soát."); return;
        }
        if (command == "web-reconcile")
        {
            await importer.ReconcileAsync(package, savedPlan, journal);
            Console.WriteLine($"WEB RECONCILE: {journal.Rows.Count(x => x.Stage == "Verified")}/43 đã kiểm lại; {journal.Rows.Count(x => x.Stage != "Verified")} thao tác chưa hoàn tất. Lệnh này chỉ đọc; chạy lại web-import với cùng nhật ký để tiếp tục."); return;
        }
        var links = await importer.SyncAsync(package, savedPlan, journal); var tag = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        package.WriteWorkbook(Path.Combine(output!, "LichCT-links-" + tag + ".xlsx"), links.ToDictionary(x => x.Stt, x => x.Url));
        await WeeklyImporter.AtomicJson(Path.Combine(output!, "web-links-" + tag + ".json"), links);
        Console.WriteLine($"WEB SYNC LINKS: {links.Count(x => x.Url != null)}/43 link đã kiểm tra phiên bản công khai và PDF qua HTTP không đăng nhập; dòng chưa công khai để trống.");
    }
}
