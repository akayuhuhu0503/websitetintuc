using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using LandPortal.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32.SafeHandles;

namespace LandPortal.WeeklyImport;

public sealed class WeeklyIisException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

// Operator CLI adapter. Does not start Web, persist settings, create directories or change ACLs.
public sealed class IisBundleRuntime : IDisposable
{
    readonly List<FileStream> leases = [];
    public PortablePackageLayout Layout { get; private set; } = null!;
    public IConfigurationRoot Configuration { get; private set; } = null!;
    public StorageOptions Storage { get; private set; } = null!;
    public string Site { get; private set; } = "";
    string[] servedRoots = [];
    static readonly string[] BoundFiles = ["Web.dll", "Infrastructure.dll", "web.config", ".runtime/worker/Worker.dll", ".runtime/worker/Infrastructure.dll"];

    public static IisBundleRuntime Load(string webRoot) => LoadCore(webRoot, ReadInstalledMetadata);

    // Tests supply IIS metadata only; all package/config/storage guards execute unchanged.
    internal static IisBundleRuntime LoadCore(string webRoot, Func<(string Sites, string Vdirs)> metadata)
    {
        var result = new IisBundleRuntime();
        try
        {
            var web = Full(webRoot);
            result.Layout = PortablePackageLayout.Resolve(web);
            if (!result.Layout.WebBundle || !Equal(result.Layout.WebRoot, web))
                Fail("BUNDLE_LAYOUT", "Chỉ nhận thư mục web đang chạy, có .runtime/manifest.json; không dùng bản Publish khác hoặc cấu trúc cũ.");
            result.Layout.VerifyBinding();
            // Keep manifest, private snapshot and critical release files read-locked for this CLI invocation.
            // Configuration stays in memory; there is no copied runtime.json or environment overlay.
            foreach (var file in new[] { result.Layout.Manifest, result.Layout.Configuration }.Concat(BoundFiles.Select(p => Path.Combine(web, p))))
                result.leases.Add(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read));
            result.Configuration = result.Layout.LoadBoundSettings();
        }
        catch (WeeklyIisException) { result.Dispose(); throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { result.Dispose(); Fail("BUNDLE_READ", "Thiếu hoặc không đọc/khóa đọc được manifest, private/uat.json hoặc binary trong WebBundle. Dùng tài khoản vận hành đã có quyền đọc; chưa mở SQL hoặc ghi tệp."); }
        catch (Exception) { result.Dispose(); Fail("BUNDLE_HASH", "Manifest/hash hoặc ràng buộc WebBundle không khớp. Dùng nguyên gói web gồm .runtime; không sửa JSON, chưa mở SQL hoặc ghi tệp."); }
        try
        {
            var c = result.Configuration; var web = result.Layout.WebRoot;
            var webXml = Xml(File.ReadAllText(Path.Combine(web, "web.config")));
            var environments = webXml.Descendants("aspNetCore").Elements("environmentVariables").Elements("environmentVariable").ToArray();
            foreach (var key in new[] { "DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT" })
            {
                var values = environments.Where(e => (string?)e.Attribute("name") == key).Select(e => (string?)e.Attribute("value")).ToArray();
                if (values.Length != 1 || values[0] != "Staging" || !string.IsNullOrWhiteSpace(c[key]) && c[key] != "Staging")
                    Fail("BUNDLE_ENVIRONMENT", "web.config và cấu hình đã Publish phải dùng Staging/UAT; công cụ không đổi profile để vượt guard.");
            }
            if (c["Deployment:Profile"] != "UAT") Fail("BUNDLE_ENVIRONMENT", "Gói hiện hành không phải UAT.");
            if (!webXml.Descendants("hiddenSegments").Elements("add").Any(e => (string?)e.Attribute("segment") == ".runtime"))
                Fail("BUNDLE_HTTP_BOUNDARY", "Gói thiếu bảo vệ HTTP cho .runtime; không sử dụng cấu hình bí mật từ gói này.");
            result.Layout.SetRuntimeLocations(c);
            var evidence = metadata();
            var vdirs = Xml(evidence.Vdirs).Descendants("VDIR").ToArray();
            result.servedRoots = vdirs.Select(v => (string?)v.Attribute("physicalPath") ?? "").Select(Full).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var sites = Xml(evidence.Sites).Descendants("SITE").Where(s =>
            {
                var name = (string?)s.Attribute("SITE.NAME");
                return !string.IsNullOrWhiteSpace(name) && vdirs.Any(v => (string?)v.Attribute("VDIR.NAME") == name + "/" && Equal(Full((string?)v.Attribute("physicalPath") ?? ""), web));
            }).ToArray();
            if (sites.Length != 1 || (string?)sites[0].Attribute("state") != "Started")
                Fail("IIS_SITE", "Không xác định được duy nhất IIS site đang chạy với thư mục gốc đúng --iis-web. Không suy đoán từ tên thư mục.");
            result.Site = (string)sites[0].Attribute("SITE.NAME")!;
            var origins = ((string?)sites[0].Attribute("bindings") ?? "").Split(',')
                .Where(b => b.Trim().StartsWith("https/", StringComparison.OrdinalIgnoreCase)).Select(b => Origin(b.Trim()[6..])).Distinct().ToArray();
            if (string.IsNullOrWhiteSpace(c["Public:BaseUrl"]))
            {
                if (origins.Length != 1) Fail("IIS_HTTPS", "Không có một binding HTTPS duy nhất để xác định Public.BaseUrl; dừng, không sửa JSON đã Publish.");
                c["Public:BaseUrl"] = origins[0];
            }
            if (!Uri.TryCreate(c["Public:BaseUrl"], UriKind.Absolute, out var url) || url.Scheme != "https" || url.IdnHost != "gdla.gov.vn"
                || url.AbsolutePath != "/" || url.Query != "" || url.Fragment != "" || url.UserInfo != "" || !origins.Contains(url.AbsoluteUri))
                Fail("IIS_HTTPS", "Public.BaseUrl không khớp binding HTTPS gdla.gov.vn của đúng IIS site.");
            if (string.IsNullOrWhiteSpace(c["Deployment:ExpectedHost"])) c["Deployment:ExpectedHost"] = url.IdnHost;
            PortableIisConfiguration.ValidateExternalStores(c, web);
            result.Storage = StorageOptions.FromConfiguration(c);
            var home = Full(c["WorkspaceRoot"]!);
            if (result.servedRoots.Any(r => Overlap(home, r) || !Equal(r, web) && Overlap(result.Layout.Root, r)))
                Fail("IIS_STORAGE_BOUNDARY", "IIS có ánh xạ phục vụ kho private hoặc .runtime. Dừng; không tự sửa ánh xạ.");
            if (!UatDeployment.Validate(c, "Staging")) Fail("UAT_CONFIG", "Cấu hình UAT chưa hợp lệ.");
            var sql = new SqlConnectionStringBuilder(DatabaseSettings.Connection(c));
            if (sql.InitialCatalog != "website_tin_tuc" || c["Sql:ExpectedDatabase"] != "website_tin_tuc"
                || UatDeployment.PhysicalServer(c) != "WIN-S1IBJB84OOO" || sql.UserID != "website_tin_tuc_app" || c["Deployment:ExpectedSqlLogin"] != sql.UserID)
                Fail("SQL_TARGET", "Cấu hình không trỏ đúng WIN-S1IBJB84OOO / website_tin_tuc / website_tin_tuc_app; chưa kết nối SQL.");
            MediaService.ValidateStorage(result.Storage);
            // Request directory handles with the necessary access, OPEN_EXISTING only. Never create a probe file.
            CheckDirectoryAccess(result.Storage.MediaRoot, true);
            CheckDirectoryAccess(result.Storage.TempRoot, true);
            var documents = Path.Combine(result.Storage.MediaRoot, "editorial-private");
            if (Directory.Exists(documents)) { NoReparse(documents); CheckDirectoryAccess(documents, true); }
            PortableIisConfiguration.ResolveScannerDefaults(c);
            return result;
        }
        catch (WeeklyIisException) { result.Dispose(); throw; }
        catch (MediaUploadException) { result.Dispose(); Fail("STORAGE_BOUNDARY", "Kho private không vượt qua kiểm tra ranh giới dùng chung; kiểm tra quyền đọc metadata IIS, đường dẫn và manifest. Chưa mở SQL/ghi tệp."); }
        catch (Exception) { result.Dispose(); Fail("BUNDLE_CONFIG", "Cấu hình UAT/TLS/storage hoặc metadata IIS không hợp lệ/không đọc được. Không in secret; chưa mở SQL hoặc ghi tệp."); }
        throw new UnreachableException();
    }

    public void ValidateOperatorPaths(string source, string output)
    {
        var paths = new[] { AppContext.BaseDirectory, source, output }.Select(Full).ToArray();
        var protectedRoots = servedRoots.Append(Layout.WebRoot).Append(Full(Configuration["WorkspaceRoot"]!));
        foreach (var path in paths)
        {
            NoReparse(path);
            if (protectedRoots.Any(r => Overlap(path, r))) Fail("OPERATOR_LOCATION", "Đặt CLI, ZIP và thư mục kết quả riêng ngoài mọi IIS webroot, .runtime và kho dữ liệu hiện hành.");
        }
        var parent = paths[2];
        while (!Directory.Exists(parent)) parent = Path.GetDirectoryName(parent) ?? throw new IOException("Không tìm thấy thư mục cha của kết quả.");
        CheckDirectoryAccess(parent, true);
    }

    public async Task<TargetIdentity> VerifySqlAsync(PortalDbContext db)
    {
        try
        {
            // Existing interceptor activates the application role and preserves Runtime18/TLS identity checks.
            var importer = new WeeklyImporter(db, Storage, MediaUploadOptions.FromConfiguration(Configuration, "Staging"), "", Configuration);
            var target = await importer.IdentifyAsync();
            await db.Database.OpenConnectionAsync();
            try
            {
                using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT ORIGINAL_LOGIN(),USER_NAME(),(SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0)";
                using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
                VerifySqlFacts(target, reader.GetString(0), reader.GetString(1), reader.GetInt32(2));
                return target;
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
        catch (WeeklyIisException) { throw; }
        catch (SqlException e) { Fail("SQL_ACCESS", $"SQL không cho phép kết nối/đọc bằng tài khoản runtime (mã {e.Number}). Kiểm tra kết nối, quyền hiện có và Application Role; công cụ không cấp quyền."); }
        catch (Exception) { Fail("SQL_IDENTITY", "SQL thực tế không khớp danh tính/schema 18/Application Role đã cấu hình. Không ghi dữ liệu."); }
        throw new UnreachableException();
    }
    internal static void VerifySqlFacts(TargetIdentity target, string login, string role, int tables)
    {
        if (!UatDeployment.TemporaryRuntimeIdentityMatches(target.Server, target.Database, login, role, tables, target.Migrations.Length, target.Migrations.LastOrDefault() ?? ""))
            Fail("SQL_IDENTITY", "SQL thực tế khác WIN-S1IBJB84OOO / website_tin_tuc, login ứng dụng, LandPortalRuntime hoặc 70 bảng/18 migration.");
    }

    internal static void CheckDirectoryAccess(string path, bool write)
    {
        if (!OperatingSystem.IsWindows()) Fail("WINDOWS_REQUIRED", "Chế độ --iis-web cần chạy trực tiếp trên Windows IIS.");
        // FILE_LIST_DIRECTORY | FILE_TRAVERSE | FILE_READ_ATTRIBUTES; add FILE_ADD_FILE/SUBDIRECTORY for import.
        using var handle = CreateFile(path, (uint)(0x1 | 0x20 | 0x80 | (write ? 0x2 | 0x4 : 0)), 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) Fail("STORAGE_ACCESS", $"Không đọc/tạo tệp hoặc thư mục con được tại '{path}' bằng tài khoản Windows hiện hành (Win32 {Marshal.GetLastWin32Error()}). Không tự cấp quyền.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    static (string, string) ReadInstalledMetadata()
    {
        if (!OperatingSystem.IsWindows()) Fail("WINDOWS_REQUIRED", "Chạy công cụ trên máy Windows IIS hiện hành.");
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32", "inetsrv", "appcmd.exe");
        if (!File.Exists(exe)) Fail("IIS_METADATA", "Không tìm thấy công cụ đọc metadata IIS trên máy này.");
        return (Read("site"), Read("vdir"));
        string Read(string kind)
        {
            try
            {
                var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "list", kind, "/xml" }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(10000)) { process.Kill(true); Fail("IIS_METADATA", "Đọc metadata IIS quá thời gian; chưa mở SQL."); }
                var text = stdout.GetAwaiter().GetResult();
                if (process.ExitCode != 0 || stderr.GetAwaiter().GetResult().Length > 0 || text.Length > 1024 * 1024) Fail("IIS_METADATA", "Tài khoản Windows hiện hành không đọc được metadata site/vdir IIS. Dùng tài khoản vận hành đã có quyền; công cụ không cấp quyền.");
                return text;
            }
            catch (WeeklyIisException) { throw; }
            catch { Fail("IIS_METADATA", "Không thực thi được truy vấn chỉ đọc metadata IIS bằng tài khoản hiện hành."); throw; }
        }
    }
    static string Origin(string binding)
    {
        var parts = binding.Split(':');
        if (parts.Length < 3 || !int.TryParse(parts[^2], out var port) || port is < 1 or > 65535 || Uri.CheckHostName(parts[^1]) != UriHostNameType.Dns)
            throw new WeeklyIisException("IIS_HTTPS", "Binding HTTPS không có host/port rõ ràng.");
        return "https://" + parts[^1].ToLowerInvariant() + (port == 443 ? "/" : ":" + port + "/");
    }
    static XDocument Xml(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        return XDocument.Load(reader);
    }
    static string Full(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Thiếu đường dẫn tuyệt đối.");
        path = Environment.ExpandEnvironmentVariables(path);
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("Đường dẫn phải tuyệt đối.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    static bool Within(string a, string b) => Equal(a, b) || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    static bool Overlap(string a, string b) => Within(a, b) || Within(b, a);
    static void NoReparse(string path)
    {
        for (string? p = path; p != null; p = Path.GetDirectoryName(p))
            if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                Fail("REPARSE_PATH", "Đường dẫn có junction/symlink/reparse point; không suy đoán vị trí thực.");
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    static void Fail(string code, string message) => throw new WeeklyIisException(code, message);
    public void Dispose() { foreach (var stream in leases) stream.Dispose(); leases.Clear(); }
}
