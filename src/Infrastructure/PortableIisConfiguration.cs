using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Infrastructure;

// Only fixed, non-secret diagnostics may cross the startup exception boundary.
public sealed class IisConfigurationException(string code,string safeMessage):InvalidOperationException(safeMessage)
{
    public string Code { get; }=code;
}

// Publish carries one confidential sibling of web/, never a developer-machine path.
public static class PortableIisConfiguration
{
    public static bool Enabled=>Environment.GetEnvironmentVariable("LANDPORTAL_PORTABLE_UAT")=="1";
    public static bool Detected {
        get {
            if(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")!="Staging")return false;
            try{var layout=PortablePackageLayout.Resolve(AppContext.BaseDirectory);using var m=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(layout.Manifest));return m.RootElement.GetProperty("privateMode").GetString()==(layout.WebBundle?"WebBundle":"Portable");}catch{return false;}
        }
    }
    public static bool IsServiceIdentityAllowed(string name,bool system,bool administrator) =>
        !system&&!administrator&&!string.IsNullOrWhiteSpace(name)&&
        !new[]{"ADMIN","Administrator"}.Contains(name.Split('\\')[^1],StringComparer.OrdinalIgnoreCase)&&
        !name.StartsWith("NT AUTHORITY\\",StringComparison.OrdinalIgnoreCase);

    public static string StartupHint(string stage)=>stage switch
    {
        "ENV"=>"Gói IIS này cần môi trường Staging/UAT; không dùng cấu hình Production.",
        "LAYOUT"=>"Gói mới: copy nguyên thư mục web gồm .runtime. Gói cũ: giữ đủ web/private/manifest.",
        "PRIVATE_READ"=>"Không đọc được cấu hình private hoặc đường dẫn không hợp lệ. Kiểm tra đã copy đủ gói và cấp Read cho đúng tài khoản Application Pool.",
        "PRIVATE_HASH"=>"Cấu hình private không khớp manifest. Copy lại nguyên cùng một gói Publish, không sửa JSON sau Publish.",
        "PROFILE"=>"Cấu hình gói phải dùng profile UAT.",
        "IDENTITY"=>"Pool phải chạy bằng ApplicationPoolIdentity hoặc tài khoản dịch vụ ít quyền; không chạy bằng Administrator hay SYSTEM.",
        "HTTPS"=>"Chưa xác định được URL HTTPS duy nhất. Kiểm tra binding HTTPS; nếu IIS không cung cấp metadata, Developer điền Public__BaseUrl và Deployment__ExpectedHost thực tế trước khi Publish.",
        "DATA_HOME"=>"Kho dữ liệu phải dùng đường dẫn tuyệt đối, private và nằm ngoài thư mục web.",
        "STORE_WRITE"=>"Tài khoản pool không tạo/ghi được kho dữ liệu. Cấp Modify cho media, snapshots, temp và keys; giữ file cấu hình chỉ đọc.",
        "STORAGE_BOUNDARY"=>"Kho private chưa khớp manifest hoặc ranh giới IIS webroot. IIS chỉ được trỏ vào web; không ánh xạ private.",
        _=>"Cấu hình kỹ thuật SQL/TLS hoặc kho dữ liệu không hợp lệ. Developer kiểm tra đầu vào; không tắt guard để khởi động."
    };

    public static IConfigurationRoot Load()
    {
        var stage="ENV";
        try{return LoadCore(ref stage);}
        catch(Exception error) when(error is InvalidOperationException or IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException or System.Xml.XmlException or ArgumentException or System.Security.SecurityException or MediaUploadException or KeyNotFoundException)
        {throw new IisConfigurationException("IIS-CONFIG-003-"+stage,StartupHint(stage));}
    }
    private static IConfigurationRoot LoadCore(ref string stage)
    {
        if(!(Enabled||Detected)||Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")!="Staging"||Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")!="Staging")throw new InvalidOperationException("Portable configuration is UAT/Staging only.");
        stage="LAYOUT";
        var layout=PortablePackageLayout.Resolve(AppContext.BaseDirectory);
        if(Environment.GetEnvironmentVariable("LANDPORTAL_WEB_BUNDLE")=="1"&&!layout.WebBundle)throw new InvalidOperationException("Embedded runtime missing; legacy fallback refused.");
        var component=new DirectoryInfo(layout.WebRoot);
        var home=layout.DefaultDataHome;
        stage="PRIVATE_HASH";
        var config=layout.LoadBoundSettings();
        stage="PROFILE";
        if(config["Deployment:Profile"]!="UAT")throw new InvalidOperationException("Portable profile must remain UAT.");
        stage="DATA_HOME";
        if(!string.IsNullOrWhiteSpace(config["PortableRuntime:DataHome"])){
            if(!Path.IsPathFullyQualified(config["PortableRuntime:DataHome"]!))throw new InvalidOperationException("Absolute private data home required.");
            home=Path.GetFullPath(config["PortableRuntime:DataHome"]!);
        }
        stage="IDENTITY";
        if(OperatingSystem.IsWindows()){
            using var identity=WindowsIdentity.GetCurrent();
            if(!IsServiceIdentityAllowed(identity.Name,identity.IsSystem,new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)))throw new InvalidOperationException("A least-privilege application pool or service account is required.");
        }
        // These values are technical locations, not approval/credentials.
        layout.SetRuntimeLocations(config);
        stage="HTTPS";
        if(string.IsNullOrWhiteSpace(config["Public:BaseUrl"])){
            var poolFile=Environment.GetEnvironmentVariable("APP_POOL_CONFIG");
            if(!string.IsNullOrWhiteSpace(poolFile)&&File.Exists(poolFile)){
                var xml=XDocument.Load(poolFile);var path=Path.GetFullPath(component.FullName).TrimEnd('\\');
                var sites=xml.Descendants("site").Where(s=>s.Descendants("virtualDirectory").Any(v=>string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables((string?)v.Attribute("physicalPath")??".")).TrimEnd('\\'),path,StringComparison.OrdinalIgnoreCase))).ToArray();
                var bindings=sites.SelectMany(s=>s.Descendants("binding")).Where(b=>(string?)b.Attribute("protocol")=="https").Select(b=>((string?)b.Attribute("bindingInformation")??"").Split(':')).Where(x=>x.Length>=3&&Uri.CheckHostName(x[^1])==UriHostNameType.Dns).ToArray();
                if(bindings.Length==1&&int.TryParse(bindings[0][^2],out var port))config["Public:BaseUrl"]="https://"+bindings[0][^1]+(port==443?"/":":"+port+"/");
            }
        }
        if(Uri.TryCreate(config["Public:BaseUrl"],UriKind.Absolute,out var url)&&string.IsNullOrWhiteSpace(config["Deployment:ExpectedHost"]))config["Deployment:ExpectedHost"]=url.IdnHost;
        if(!Uri.TryCreate(config["Public:BaseUrl"],UriKind.Absolute,out var origin)||origin.Scheme!="https"||!string.Equals(origin.IdnHost,config["Deployment:ExpectedHost"],StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Exact HTTPS origin required.");
        stage="TECHNICAL";
        ResolveScannerDefaults(config);
        stage="STORAGE_BOUNDARY";
        if(layout.WebBundle)ValidateExternalStores(config,layout.WebRoot);
        StorageOptions.FromConfiguration(config).PortableBoundary?.EnsureValidated();
        stage="TECHNICAL";
        // Technical target/TLS guards remain mandatory; unavailable scanning blocks file operations.
        UatDeployment.Validate(config,"Staging");
        stage="STORE_WRITE";
        foreach(var name in new[]{"MediaRoot","SnapshotRoot","TempRoot","KeysRoot"})Directory.CreateDirectory(config["Storage:"+name]!);
        return config;
    }

    public static void ValidateExternalStores(IConfiguration config,string webRoot)
    {
        static string Full(string p)=>Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar);
        static bool Within(string a,string b)=>a.Equals(b,StringComparison.OrdinalIgnoreCase)||a.StartsWith(b+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        var web=Full(webRoot);
        var stores=new[]{"MediaRoot","SnapshotRoot","TempRoot","KeysRoot"}.Select(k=>config["Storage:"+k]??"").ToArray();
        if(stores.Any(p=>!Path.IsPathFullyQualified(p)))throw new InvalidOperationException("Absolute external storage required.");
        stores=stores.Select(Full).ToArray();
        if(stores.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=4||stores.Any(p=>Within(p,web)||Within(web,p)||p.Split(Path.DirectorySeparatorChar).Any(x=>x.Equals("wwwroot",StringComparison.OrdinalIgnoreCase)))||stores.Any(a=>stores.Any(b=>a!=b&&Within(a,b))))throw new InvalidOperationException("Stores must be separate and outside application.");
        foreach(var store in stores)for(string? p=store;p!=null;p=Path.GetDirectoryName(p))if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Reparse storage refused.");
    }

    // Shared measured process/binary defaults for portable Web and media tools.
    // Resolving these values is not a scanner health check or a Clean result.
    public static void ResolveScannerDefaults(IConfiguration config)
    {
        if(OperatingSystem.IsWindows()&&string.IsNullOrWhiteSpace(config["Scanner:DefenderServiceIdentity"]))config["Scanner:DefenderServiceIdentity"]=WindowsIdentity.GetCurrent().Name;
        if(config["Scanner:Provider"]=="MicrosoftDefenderOnPrem"&&string.IsNullOrWhiteSpace(config["Scanner:DefenderBinarySha256"])){
            var platform=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Microsoft","Windows Defender","Platform");
            var binary=Directory.Exists(platform)?Directory.GetDirectories(platform).OrderDescending(StringComparer.Ordinal).Select(d=>Path.Combine(d,"MpCmdRun.exe")).FirstOrDefault(File.Exists):null;
            binary??=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Windows Defender","MpCmdRun.exe");
            try{if(File.Exists(binary)){using var stream=File.OpenRead(binary);config["Scanner:DefenderBinarySha256"]=Convert.ToHexStringLower(SHA256.HashData(stream));}}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){/* Upload scanner stays unavailable; reading the site remains enabled. */}
        }
    }
}
