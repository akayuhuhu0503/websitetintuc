using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Infrastructure;

// A validated capability attached to StorageOptions, never a JSON bypass flag.
// Legacy/unbound storage keeps the original publish-segment prohibition.
internal sealed class PortableStorageBoundary
{
    readonly string home;
    readonly string[] stores;
    readonly Lazy<PortableStorageBoundary>? validated;
    PortableStorageBoundary(string home,string[] stores){this.home=home;this.stores=stores;}
    PortableStorageBoundary(IConfiguration config,string[] stores)
    {
        home="";this.stores=stores;
        // Reading/validating configuration during offline Publish must not
        // require an installed IIS. No path is permitted until Value succeeds.
        validated=new Lazy<PortableStorageBoundary>(()=>Validate(config,stores));
    }
    static string Full(string value)=>Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
    static bool Within(string child,string parent)=>child.Equals(parent,StringComparison.OrdinalIgnoreCase)||child.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    static void Fail(string reason)=>throw new MediaUploadException("Kho portable chưa được xác minh an toàn: "+reason,503);
    static void NoLinks(string path)
    {
        for(string? p=path;p!=null;p=Path.GetDirectoryName(p))
            if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)Fail("không chấp nhận reparse point.");
    }
    static XDocument Xml(string text)
    {
        using var reader=XmlReader.Create(new StringReader(text),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});
        return XDocument.Load(reader);
    }
    static string[] IisRoots()
    {
        // Worker processes do not normally receive this variable and use the
        // same read-only IIS metadata query as the data operator instead.
        var pool=Environment.GetEnvironmentVariable("APP_POOL_CONFIG");
        if(!string.IsNullOrWhiteSpace(pool)&&File.Exists(pool))
            return Xml(File.ReadAllText(pool)).Descendants("virtualDirectory").Select(v=>(string?)v.Attribute("physicalPath")).Where(v=>!string.IsNullOrWhiteSpace(v)).Select(v=>Full(Environment.ExpandEnvironmentVariables(v!))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var executable=Path.Combine(windows,Environment.Is64BitOperatingSystem&&!Environment.Is64BitProcess?"Sysnative":"System32","inetsrv","appcmd.exe");
        if(!File.Exists(executable))Fail("không đọc được metadata IIS để xác minh private nằm ngoài webroot.");
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var a in new[]{"list","vdir","/xml"})start.ArgumentList.Add(a);
        using var process=Process.Start(start)??throw new InvalidOperationException("IIS metadata unavailable.");
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(10000)){process.Kill(true);Fail("đọc metadata IIS quá thời gian.");}
        var text=stdout.GetAwaiter().GetResult();var error=stderr.GetAwaiter().GetResult();
        if(process.ExitCode!=0||!string.IsNullOrWhiteSpace(error)||text.Length>1024*1024)Fail("tài khoản hiện hành không đọc được metadata IIS; không tự cấp quyền.");
        return Xml(text).Descendants("VDIR").Select(v=>(string?)v.Attribute("physicalPath")).Where(v=>!string.IsNullOrWhiteSpace(v)).Select(v=>Full(Environment.ExpandEnvironmentVariables(v!))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static PortableStorageBoundary? Create(IConfiguration c,string[] stores)
    {
        if(!stores.Any(s=>Full(s).Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Any(x=>x.Equals("publish",StringComparison.OrdinalIgnoreCase))))return null;
        return new(c,stores);
    }
    internal void EnsureValidated(){_=validated?.Value;}
    static PortableStorageBoundary Validate(IConfiguration c,string[] stores)
    {
        var home=Full(c["WorkspaceRoot"]??throw new InvalidOperationException("Private workspace missing."));
        if(!Path.GetFileName(home).Equals("private",StringComparison.OrdinalIgnoreCase))Fail("đường dẫn publish chỉ được dùng trong private của gói IIS đã bind.");
        var package=Path.GetDirectoryName(home)!;var web=Path.Combine(package,"web");var file=Path.Combine(home,"uat.json");
        NoLinks(home);NoLinks(file);NoLinks(web);NoLinks(Path.Combine(package,"manifest.json"));
        if(!Directory.Exists(web)||!File.Exists(file))Fail("thiếu layout web/private của gói IIS.");
        using(var m=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(package,"manifest.json"))))
        {
            if(!m.RootElement.TryGetProperty("privateMode",out var mode)||mode.GetString()!="Portable")Fail("gói chưa xác định Portable.");
            var binding=m.RootElement.GetProperty("privateConfiguration");
            if(binding.GetProperty("path").GetString()!="private/uat.json"||!binding.GetProperty("confidential").GetBoolean()||binding.GetProperty("sha256").GetString()!=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))))Fail("hash/binding cấu hình private không khớp.");
        }
        if(c["Deployment:Profile"]!="UAT"||c["DOTNET_ENVIRONMENT"]!="Staging"||c["ASPNETCORE_ENVIRONMENT"]!="Staging")Fail("ngoại lệ portable chỉ dành cho UAT đã xác định.");
        var roots=stores.Select(Full).ToArray();
        if(roots.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=4||roots.Any(x=>!Within(x,home)||x.Equals(home,StringComparison.OrdinalIgnoreCase)||Within(x,web)||x.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Any(s=>s.Equals("wwwroot",StringComparison.OrdinalIgnoreCase))))Fail("kho phải tách biệt trong private, ngoài web/wwwroot.");
        if(roots.Any(a=>roots.Any(b=>a!=b&&Within(a,b))))Fail("kho private không được lồng nhau.");
        var served=IisRoots();
        if(!served.Any(x=>x.Equals(web,StringComparison.OrdinalIgnoreCase))||served.Any(x=>Within(home,x)||Within(x,home)))Fail("IIS mapping không đúng web hoặc đang phục vụ private.");
        foreach(var root in roots)NoLinks(root);
        return new(home,roots);
    }
    internal bool Allows(string path)
    {
        if(validated!=null)return validated.Value.Allows(path);
        path=Full(path);
        if(!stores.Any(root=>Within(path,root)))return false;
        NoLinks(path);
        return true;
    }

    internal void ValidateFile(string path)
    {
        if(validated!=null){validated.Value.ValidateFile(path);return;}
        if(!Allows(Path.GetDirectoryName(path)!))Fail("tệp nằm ngoài các kho đã xác minh.");
        NoLinks(path);
    }
}
