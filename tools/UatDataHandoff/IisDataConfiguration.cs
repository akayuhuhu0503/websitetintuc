using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace LandPortal.UatData;

// Explicit data-operator CLI only. Never starts Web, changes the bound IIS JSON,
// grants rights, installs a signer or replaces an application/runtime credential.
public static class IisDataConfiguration
{
    public static IConfigurationRoot Load(string publishRoot)=>LoadCore(publishRoot,IisMediaOrigin.ResolveInstalled);
    // Reader is fixed to IIS metadata in the public entry point. A private seam
    // permits exercising the complete bound-config loader with offline IIS fixtures.
    static IConfigurationRoot LoadCore(string publishRoot,Func<string,string> readIisOrigin)
    {
        var root=Path.GetFullPath(publishRoot);DataPackage.NoReparse(root);
        var file=Path.Combine(root,"private","uat.json");DataPackage.NoReparse(file);
        using(var m=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"manifest.json"))))
        {
            var binding=m.RootElement.GetProperty("privateConfiguration");
            if(binding.GetProperty("path").GetString()!="private/uat.json"||!binding.GetProperty("confidential").GetBoolean()||DataPackage.FileHash(file).GetAwaiter().GetResult()!=binding.GetProperty("sha256").GetString())throw new InvalidDataException("IIS private config binding differs; no SQL connection attempted.");
        }
        var c=new ConfigurationBuilder().AddJsonFile(file,false,false).Build();
        c["DOTNET_ENVIRONMENT"]=Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");c["ASPNETCORE_ENVIRONMENT"]=Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var home=string.IsNullOrWhiteSpace(c["PortableRuntime:DataHome"])?Path.Combine(root,"private"):c["PortableRuntime:DataHome"]!;
        if(!Path.IsPathFullyQualified(home))throw new InvalidDataException("Absolute private IIS data home required.");
        c["WorkspaceRoot"]=Path.GetFullPath(home);
        foreach(var field in new[]{"MediaRoot","SnapshotRoot","TempRoot","KeysRoot"})if(string.IsNullOrWhiteSpace(c["Storage:"+field]))c["Storage:"+field]=Path.Combine(home,field switch{"MediaRoot"=>"media","SnapshotRoot"=>"snapshots","TempRoot"=>"temp",_=>"keys"});
        // Portable Web resolves an omitted origin from IIS metadata. The CLI
        // has no APP_POOL_CONFIG, so use the installed read-only IIS tool and
        // bind its result to this exact published web directory instead.
        IisMediaOrigin.Apply(c,root,readIisOrigin);
        UatDeployment.Validate(c,"Staging");
        MediaService.ValidateStorage(StorageOptions.FromConfiguration(c));
        PortableIisConfiguration.ResolveScannerDefaults(c);
        return c;
    }
}
