using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Infrastructure;

// Layout is discovered from the executing component, never from an HTTP header.
public sealed record PortablePackageLayout(string Root, string WebRoot, string DefaultDataHome, bool WebBundle)
{
    public string Manifest => Path.Combine(Root,"manifest.json");
    public string Configuration => Path.Combine(Root,"private","uat.json");

    public static PortablePackageLayout Resolve(string baseDirectory)
    {
        // AppContext.BaseDirectory ends with a separator. Preserve one canonical component
        // path: Directory.GetParent(".../web/") otherwise returns web, not its package.
        var component=new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory)));
        var embedded=Path.Combine(component.FullName,".runtime");
        if(File.Exists(Path.Combine(embedded,"manifest.json")))
            return new(embedded,component.FullName,Path.Combine(component.Parent!.FullName,"private"),true);
        var root=component.Name=="uat-data"?component.Parent?.Parent:component.Parent;
        if(root==null||component.Name is not("web" or "worker" or "tools" or "uat-data"))
            throw new InvalidOperationException("Unknown portable component layout.");
        if(root.Name==".runtime")
            return new(root.FullName,root.Parent!.FullName,Path.Combine(root.Parent.Parent!.FullName,"private"),true);
        return new(root.FullName,Path.Combine(root.FullName,"web"),Path.Combine(root.FullName,"private"),false);
    }

    public void VerifyBinding()
    {
        foreach(var path in new[]{Root,WebRoot,Manifest,Configuration})
            for(string? p=path;p!=null;p=Path.GetDirectoryName(p))
                if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidOperationException("Reparse package path refused.");
        using var document=JsonDocument.Parse(File.ReadAllBytes(Manifest));
        var m=document.RootElement;
        if(m.GetProperty("privateMode").GetString()!=(WebBundle?"WebBundle":"Portable"))throw new InvalidOperationException("Package mode mismatch.");
        var binding=m.GetProperty("privateConfiguration");
        if(binding.GetProperty("path").GetString()!="private/uat.json"||!binding.GetProperty("confidential").GetBoolean())
            throw new InvalidOperationException("Private package binding missing.");
        using var stream=File.OpenRead(Configuration);
        if(binding.GetProperty("sha256").GetString()!=Convert.ToHexStringLower(SHA256.HashData(stream)))
            throw new InvalidOperationException("Private config changed after Publish.");
        if(WebBundle) {
            // Bind Web/Worker and HTTP protections as one release. Do not execute a mixed copy.
            foreach(var relative in new[]{"Web.dll","Infrastructure.dll","web.config",".runtime/worker/Worker.dll",".runtime/worker/Infrastructure.dll"}) {
                var entries=m.GetProperty("files").EnumerateArray().Where(x=>x.GetProperty("path").GetString()==relative).ToArray();
                var file=Path.Combine(WebRoot,relative.Replace('/',Path.DirectorySeparatorChar));
                if(entries.Length!=1||!File.Exists(file))throw new InvalidOperationException("Incomplete Web/Worker bundle.");
                using var bytes=File.OpenRead(file);
                if(entries[0].GetProperty("sha256").GetString()!=Convert.ToHexStringLower(SHA256.HashData(bytes)))throw new InvalidOperationException("Mixed release binary or web.config.");
            }
        }
    }

    public IConfigurationRoot LoadBoundSettings()
    {
        VerifyBinding();
        var builder=new ConfigurationBuilder().AddJsonFile(Configuration,false,false);
        // Existing legacy packages retain precedence; new bundles deliberately use only the sealed .env snapshot.
        if(!WebBundle)builder.AddEnvironmentVariables();
        var config=builder.Build();
        if(WebBundle)config["PortableRuntime:BundleRoot"]=Root;
        return config;
    }

    // Pure configuration normalization shared by startup and offline Worker package inspection.
    public void SetRuntimeLocations(IConfiguration config)
    {
        var home=config["PortableRuntime:DataHome"];
        if(string.IsNullOrWhiteSpace(home))home=DefaultDataHome;
        if(!Path.IsPathFullyQualified(home))throw new InvalidOperationException("Absolute private data home required.");
        config["WorkspaceRoot"]=Path.GetFullPath(home);
        config["DOTNET_ENVIRONMENT"]="Staging";config["ASPNETCORE_ENVIRONMENT"]="Staging";
        foreach(var name in new[]{"MediaRoot","SnapshotRoot","TempRoot","KeysRoot"})if(string.IsNullOrWhiteSpace(config["Storage:"+name]))config["Storage:"+name]=Path.Combine(home,name switch{"MediaRoot"=>"media","SnapshotRoot"=>"snapshots","TempRoot"=>"temp",_=>"keys"});
    }
}
