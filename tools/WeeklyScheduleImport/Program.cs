using System.Text;
using System.Text.Json;
using LandPortal.Infrastructure;
using LandPortal.WeeklyImport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// No automatic .env discovery, migrations, publication, backup SQL or operator grants.
try
{
    if (args.FirstOrDefault()?.StartsWith("web-", StringComparison.Ordinal) == true) { await WebImportCommand.RunAsync(args); return; }
    if (args.Length == 0 || args[0] is not ("preflight" or "inspect" or "dry-run" or "import" or "reconcile" or "sync-links"))
        throw new ArgumentException("Dùng preflight | inspect | dry-run | import | reconcile | sync-links. Xem docs/weekly-schedule-import.md.");
    string Required(string key)
    {
        var i = Array.IndexOf(args, key); if (i < 1 || i + 1 >= args.Length || args[i + 1].StartsWith("--")) throw new ArgumentException("Thiếu " + key);
        return args[i + 1];
    }
    if (args.Contains("--iis-web") && args.Contains("--config")) throw new ArgumentException("Chỉ dùng --iis-web hoặc --config, không trộn hai cấu hình.");
    if (args[0] == "preflight" && !args.Contains("--iis-web")) throw new ArgumentException("Tiền kiểm IIS cần --iis-web trỏ đúng web đang chạy.");
    var zip = Path.GetFullPath(Required("--zip"));
    var output = Path.GetFullPath(Required("--out"));
    // Verify the sealed installed bundle and real IIS/storage boundary BEFORE any SQL or output writes.
    using var bundle = args.Contains("--iis-web") ? IisBundleRuntime.Load(Required("--iis-web")) : null;
    bundle?.ValidateOperatorPaths(zip, output);
    var package = WeeklyPackage.Read(zip);
    async Task SaveSource()
    {
        if (Directory.Exists(output)) throw new IOException("Chọn thư mục kết quả mới để giữ bản đối soát cũ.");
        Directory.CreateDirectory(output);
        File.Copy(zip, Path.Combine(output, "source-backup.zip"), false);
        if (WeeklyPackage.Hash(File.ReadAllBytes(Path.Combine(output, "source-backup.zip"))) != package.ZipSha256) throw new IOException("Bản sao lưu nguồn không khớp SHA256.");
        await File.WriteAllBytesAsync(Path.Combine(output, "original.xlsx"), package.Workbook);
        await WeeklyImporter.AtomicJson(Path.Combine(output, "inventory.json"), new { summary = package.Summary(), rows = package.Rows });
        package.WriteWorkbook(Path.Combine(output, "LichCT-pages-reviewed.xlsx"), new Dictionary<int, string?>());
    }
    if (args[0] == "inspect") { await SaveSource(); Console.WriteLine(JsonSerializer.Serialize(package.Summary(), WeeklyPackage.Json)); return; }
    if (args[0] is not ("dry-run" or "preflight"))
    {
        if (!Directory.Exists(output)) throw new IOException("Cần thư mục dry-run chứa bản sao lưu nguồn.");
        if (WeeklyPackage.Hash(File.ReadAllBytes(Path.Combine(output, "source-backup.zip"))) != package.ZipSha256
            || WeeklyPackage.Hash(File.ReadAllBytes(Path.Combine(output, "original.xlsx"))) != package.WorkbookSha256)
            throw new InvalidDataException("Bản sao nguồn/Excel đã đổi; chưa mở SQL.");
    }
    var configPath = bundle == null ? Path.GetFullPath(Required("--config")) : null;
    var config = bundle?.Configuration ?? new ConfigurationBuilder().AddJsonFile(configPath!, false, false).Build();
    if (string.IsNullOrWhiteSpace(config["WorkspaceRoot"])) config["WorkspaceRoot"] = Path.GetDirectoryName(configPath!);
    var environment = config["DOTNET_ENVIRONMENT"] ?? config["ASPNETCORE_ENVIRONMENT"] ?? "Production";
    // The existing runtime profile/transport/boundary guards remain authoritative.
    if (!UatDeployment.Validate(config, environment) && !LanDemoDeployment.Validate(config, environment))
        throw new InvalidDataException("CLI cần cấu hình runtime UAT/LAN hợp lệ hiện có, với đường dẫn storage thực. Không tự đổi profile hoặc chính sách.");
    var storage = StorageOptions.FromConfiguration(config); MediaService.ValidateStorage(storage);
    var policy = MediaUploadOptions.FromConfiguration(config, environment);
    await using var db = new PortalDbContext(DatabaseSettings.Options(config));
    var target = bundle == null ? null : await bundle.VerifySqlAsync(db);
    if (args[0] == "preflight")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { status = "PREFLIGHT_PASS", site = bundle!.Site, publicUrl = config["Public:BaseUrl"],
            server = target!.Server, database = target.Database, role = target.Principal, migrations = target.Migrations.Length,
            mediaRoot = storage.MediaRoot, tempRoot = storage.TempRoot, managedPdfWithoutAv = policy.ApprovedManagedDocumentsWithoutAv,
            source = package.Summary(), sqlWrites = 0, filesCreated = 0, note = "Quyền Windows kiểm tra bằng handle thư mục; chưa thử ghi. Quyền CMS và scanner sẽ kiểm tra ở Dry-run/Import." }, WeeklyPackage.Json));
        return;
    }
    var username = Required("--user");
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedUserName == username.Normalize().ToUpperInvariant());
    if (user == null || user.IsDisabled || user.MustChangePassword || user.LockoutEnd > DateTimeOffset.UtcNow || user.TwoFactorEnabled || string.IsNullOrEmpty(user.PasswordHash))
        throw new UnauthorizedAccessException("Tài khoản không đủ điều kiện CLI (hoặc cần MFA); không bỏ xác thực, khóa hay yêu cầu đổi mật khẩu.");
    if (Console.IsInputRedirected) throw new UnauthorizedAccessException("Nhập mật khẩu CMS tại console bảo mật, không truyền qua tham số/file/log.");
    Console.Write("Mật khẩu CMS (không hiển thị): "); var secret = new StringBuilder();
    while (true) { var key = Console.ReadKey(true); if (key.Key == ConsoleKey.Enter) break; if (key.Key == ConsoleKey.Backspace) { if (secret.Length > 0) secret.Length--; } else if (!char.IsControl(key.KeyChar) && secret.Length < 1024) secret.Append(key.KeyChar); }
    Console.WriteLine(); var verified = new PasswordHasher<PortalUser>().VerifyHashedPassword(user, user.PasswordHash, secret.ToString()); secret.Clear();
    if (verified == PasswordVerificationResult.Failed) throw new UnauthorizedAccessException("Xác thực CMS không thành công; dừng, không thử lặp.");
    var importer = new WeeklyImporter(db, storage, policy, user.Id, config);
    if (args[0] == "dry-run")
    {
        var plan = await importer.PlanAsync(package);
        await SaveSource(); var path = Path.Combine(output, "plan.json");
        await WeeklyImporter.AtomicJson(path, plan);
        Console.WriteLine("DRY-RUN: " + string.Join(", ", plan.Rows.GroupBy(r => r.Action).Select(g => g.Key + "=" + g.Count())));
        Console.WriteLine("Plan SHA256: " + WeeklyPackage.Hash(File.ReadAllBytes(path))); return;
    }
    if (args[0] == "import")
    {
        var bytes = File.ReadAllBytes(Path.Combine(output, "plan.json"));
        if (WeeklyPackage.Hash(bytes) != Required("--confirm-plan")) throw new UnauthorizedAccessException("SHA256 plan chưa được xác nhận; không ghi.");
        var plan = JsonSerializer.Deserialize<ImportPlan>(bytes)!;
        var receipt = await importer.ImportAsync(package, plan, Path.Combine(output, "receipt.json"));
        Console.WriteLine($"IMPORT: {receipt.Rows.Count} hồ sơ đã đối soát; không gửi duyệt/xuất bản."); return;
    }
    if (args[0] == "reconcile")
    {
        var receipt = await importer.ReconcileAsync(package);
        await WeeklyImporter.AtomicJson(Path.Combine(output, "receipt.json"), receipt);
        Console.WriteLine($"RECONCILE: {receipt.Rows.Count}/43 hồ sơ; SQL chỉ đọc."); return;
    }
    var previous = JsonSerializer.Deserialize<ImportReceipt>(await File.ReadAllBytesAsync(Path.Combine(output, "receipt.json")))!;
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };
    var results = await new LinkSync(db, policy, http, new Uri(config["Public:BaseUrl"] ?? throw new InvalidDataException("Thiếu Public:BaseUrl thực tế.")))
        .VerifyAsync(package, previous, await importer.IdentifyAsync());
    var tag = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
    package.WriteWorkbook(Path.Combine(output, "LichCT-links-" + tag + ".xlsx"), results.ToDictionary(r => r.Stt, r => r.Url));
    await WeeklyImporter.AtomicJson(Path.Combine(output, "links-" + tag + ".json"), results);
    Console.WriteLine($"SYNC LINKS: {results.Count(r => r.Url != null)}/43 URL Published đã kiểm tra HTTP/PDF; các dòng khác để trống. SQL chỉ đọc.");
}
catch (Exception error)
{
    // Do not dump SQL exceptions, connection strings, config or stack traces to an operator log.
    Console.Error.WriteLine(error is WeeklyIisException iis ? "BLOCKED " + iis.Code + ": " + iis.Message : error is InvalidDataException or ArgumentException or UnauthorizedAccessException or IOException or MediaUploadException
        ? "BLOCKED: " + error.Message : "BLOCKED: " + error.GetType().Name + ". Dừng trước khi thử lại; chạy reconcile và xem báo cáo nguồn/plan. Không in cấu hình bí mật.");
    Environment.ExitCode = 1;
}
