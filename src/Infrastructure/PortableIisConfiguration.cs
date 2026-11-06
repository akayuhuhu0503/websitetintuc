using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Infrastructure;

// Publish carries one confidential sibling of web/, never a developer-machine path.
public static class PortableIisConfiguration
{
    public static bool Enabled=>Environment.GetEnvironmentVariable("LANDPORTAL_PORTABLE_UAT")=="1";
    public static bool Detected {
        get {
            if(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")!="Staging")return false;
            var dir=new DirectoryInfo(AppContext.BaseDirectory);var root=dir.Name=="uat-data"?dir.Parent?.Parent:dir.Parent;
            if(root==null||dir.Name is not("web" or "worker" or "tools" or "uat-data"))return false;
            try{using var m=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName,"manifest.json")));return m.RootElement.TryGetProperty("privateMode",out var mode)&&mode.GetString()=="Portable";}catch{return false;}
        }
    }
    public static IConfigurationRoot Load()
    {
        if(!(Enabled||Detected)||Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")!="Staging"||Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")!="Staging")throw new InvalidOperationException("Portable configuration is UAT/Staging only.");
        var component=new DirectoryInfo(AppContext.BaseDirectory);
        var package=component.Name=="uat-data"?component.Parent?.Parent:component.Parent;
        if(package==null||component.Name is not("web" or "worker" or "tools" or "uat-data")||!File.Exists(Path.Combine(package.FullName,"manifest.json")))throw new InvalidOperationException("Published package layout required.");
        var home=Path.Combine(package.FullName,"private");var file=Path.Combine(home,"uat.json");
        for(string? p=file;p!=null;p=Path.GetDirectoryName(p))if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Reparse private configuration refused.");
        using(var manifest=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(package.FullName,"manifest.json")))){
            if(!manifest.RootElement.TryGetProperty("privateConfiguration",out var binding)||!binding.TryGetProperty("path",out var boundPath)||boundPath.GetString()!="private/uat.json"||!binding.TryGetProperty("confidential",out var confidential)||!confidential.GetBoolean()||!binding.TryGetProperty("sha256",out var sha))throw new InvalidOperationException("Private package binding missing.");
            using var bytes=File.OpenRead(file);if(!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)),sha.GetString(),StringComparison.Ordinal))throw new InvalidOperationException("Private config changed after Publish; republish from the approved .env.");
        }
        if(OperatingSystem.IsWindows()){
            var broad=new[]{"S-1-1-0","S-1-5-11","S-1-5-32-545","S-1-5-32-568"};
            var rules=new FileInfo(file).GetAccessControl().GetAccessRules(true,true,typeof(SecurityIdentifier));
            foreach(FileSystemAccessRule rule in rules)if(rule.AccessControlType==AccessControlType.Allow&&broad.Contains(rule.IdentityReference.Value))throw new InvalidOperationException("Private config ACL permits broad access; grant only the allocated operator/service account.");
        }
        var config=new ConfigurationBuilder().AddJsonFile(file,false,false).AddEnvironmentVariables().Build();
        if(config["Deployment:Profile"]!="UAT")throw new InvalidOperationException("Portable profile must remain UAT.");
        if(!string.IsNullOrWhiteSpace(config["PortableRuntime:DataHome"])){
            if(!Path.IsPathFullyQualified(config["PortableRuntime:DataHome"]!))throw new InvalidOperationException("Absolute private data home required.");
            home=Path.GetFullPath(config["PortableRuntime:DataHome"]!);
        }
        if(OperatingSystem.IsWindows()){
            using var identity=WindowsIdentity.GetCurrent();
            if(identity.IsSystem||identity.Name.StartsWith("IIS APPPOOL\\",StringComparison.OrdinalIgnoreCase)||new[]{"ADMIN","Administrator"}.Contains(identity.Name.Split('\\')[^1],StringComparer.OrdinalIgnoreCase)||new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)||identity.Name.StartsWith("NT AUTHORITY\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("A dedicated least-privilege service account is required for Web and workers.");
        }
        // These values are technical locations, not approval/credentials.
        config["WorkspaceRoot"]=home;config["DOTNET_ENVIRONMENT"]="Staging";config["ASPNETCORE_ENVIRONMENT"]="Staging";
        foreach(var name in new[]{"MediaRoot","SnapshotRoot","TempRoot","KeysRoot"})if(string.IsNullOrWhiteSpace(config["Storage:"+name]))config["Storage:"+name]=Path.Combine(home,name switch{"MediaRoot"=>"media","SnapshotRoot"=>"snapshots","TempRoot"=>"temp",_=>"keys"});
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
        if(OperatingSystem.IsWindows()&&string.IsNullOrWhiteSpace(config["Scanner:DefenderServiceIdentity"]))config["Scanner:DefenderServiceIdentity"]=WindowsIdentity.GetCurrent().Name;
        if(config["Scanner:Provider"]=="MicrosoftDefenderOnPrem"&&string.IsNullOrWhiteSpace(config["Scanner:DefenderBinarySha256"])){
            var platform=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Microsoft","Windows Defender","Platform");
            var binary=Directory.Exists(platform)?Directory.GetDirectories(platform).OrderDescending(StringComparer.Ordinal).Select(d=>Path.Combine(d,"MpCmdRun.exe")).FirstOrDefault(File.Exists):null;
            binary??=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Windows Defender","MpCmdRun.exe");
            if(File.Exists(binary)){using var stream=File.OpenRead(binary);config["Scanner:DefenderBinarySha256"]=Convert.ToHexStringLower(SHA256.HashData(stream));}
        }
        // Actual authority and all original target/scanner/TLS guards remain mandatory.
        UatDeployment.Validate(config,"Staging");
        foreach(var name in new[]{"MediaRoot","SnapshotRoot","TempRoot","KeysRoot"})Directory.CreateDirectory(config["Storage:"+name]!);
        return config;
    }
}
