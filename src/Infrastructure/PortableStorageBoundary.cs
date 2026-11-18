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
    static void Fail(string reason,string code="STORAGE_PATH")=>throw IisStorageMetadata.Failure(code);
    static void NoLinks(string path)
    {
        for(string? p=path;p!=null;p=Path.GetDirectoryName(p))
            if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)Fail("không chấp nhận reparse point.");
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
        var bundle=c["PortableRuntime:BundleRoot"];
        var package=Path.GetDirectoryName(home)!;
        var web=bundle==null?Path.Combine(package,"web"):Path.GetDirectoryName(Full(bundle))!;
        if(bundle!=null && (!Path.GetFileName(Full(bundle)).Equals(".runtime",StringComparison.Ordinal)||!home.Equals(Path.Combine(Path.GetDirectoryName(web)!,"private"),StringComparison.OrdinalIgnoreCase)))Fail("bundle/data boundary mismatch.");
        var layout=new PortablePackageLayout(bundle??package,web,home,bundle!=null);
        NoLinks(home);NoLinks(web);
        if(!Directory.Exists(web))Fail("missing IIS web directory.");
        try { layout.VerifyBinding(); }
        catch(Exception e) when(e is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException or System.Text.Json.JsonException or KeyNotFoundException)
        { throw IisStorageMetadata.Failure("STORAGE_MANIFEST"); }
        if(c["Deployment:Profile"]!="UAT"||c["DOTNET_ENVIRONMENT"]!="Staging"||c["ASPNETCORE_ENVIRONMENT"]!="Staging")Fail("ngoại lệ portable chỉ dành cho UAT đã xác định.");
        var roots=stores.Select(Full).ToArray();
        if(roots.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=4||roots.Any(x=>!Within(x,home)||x.Equals(home,StringComparison.OrdinalIgnoreCase)||Within(x,web)||x.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Any(s=>s.Equals("wwwroot",StringComparison.OrdinalIgnoreCase))))Fail("kho phải tách biệt trong private, ngoài web/wwwroot.");
        if(roots.Any(a=>roots.Any(b=>a!=b&&Within(a,b))))Fail("kho private không được lồng nhau.");
        var served=IisStorageMetadata.ReadRoots(web);
        if(!served.Any(x=>x.Equals(web,StringComparison.OrdinalIgnoreCase))||served.Any(x=>Within(home,x)||Within(x,home)))Fail("IIS mapping không đúng web hoặc đang phục vụ private.","IIS_MAPPING");
        if(bundle!=null&&served.Any(x=>!x.Equals(web,StringComparison.OrdinalIgnoreCase)&&(Within(x,Full(bundle))||Within(Full(bundle),x))))Fail("IIS must not separately map the embedded runtime.","IIS_MAPPING");
        foreach(var root in roots)NoLinks(root);
        return new(home,roots);
    }
    internal bool Allows(string path)
    {
        try
        {
            if(validated!=null)return validated.Value.Allows(path);
            path=Full(path);
            if(!stores.Any(root=>Within(path,root)))return false;
            NoLinks(path);
            return true;
        }
        catch(IisConfigurationException e){throw new MediaUploadException(e.Message,503);}
    }

    internal void ValidateFile(string path)
    {
        try
        {
            if(validated!=null){validated.Value.ValidateFile(path);return;}
            if(!Allows(Path.GetDirectoryName(path)!))Fail("tệp nằm ngoài các kho đã xác minh.");
            NoLinks(path);
        }
        catch(IisConfigurationException e){throw new MediaUploadException(e.Message,503);}
    }
}
