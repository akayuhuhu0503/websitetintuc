using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;

namespace LandPortal.UatData;

public static class IisMediaOrigin
{
    const string Missing="BLOCKED_HTTPS_ORIGIN: không xác định được một binding HTTPS duy nhất của đúng site đang chạy. Giá trị cần cung cấp: Public.BaseUrl (URL HTTPS chính thức). Không sửa JSON đã bind manifest.";
    static string Full(string path)=>Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
    public static string Resolve(string publishRoot,string sitesXml,Func<string,string[]> physicalPaths)
    {
        using var reader=XmlReader.Create(new StringReader(sitesXml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});
        var doc=XDocument.Load(reader);var web=Full(Path.Combine(publishRoot,"web"));
        var matches=new List<string[]>();
        foreach(var site in doc.Descendants("SITE"))
        {
            if(!string.Equals((string?)site.Attribute("state"),"Started",StringComparison.OrdinalIgnoreCase))continue;
            var name=(string?)site.Attribute("SITE.NAME");if(string.IsNullOrWhiteSpace(name)||!string.Equals(name,"WWW_GDLA",StringComparison.OrdinalIgnoreCase))continue;
            if(!physicalPaths(name).Where(x=>!string.IsNullOrWhiteSpace(x)).Any(x=>string.Equals(Full(x.Trim()),web,StringComparison.OrdinalIgnoreCase)))continue;
            var origins=new List<string>();
            foreach(var binding in ((string?)site.Attribute("bindings")??"").Split(','))
            {
                var value=binding.Trim();if(!value.StartsWith("https/",StringComparison.OrdinalIgnoreCase))continue;
                var parts=value[6..].Split(':');
                if(parts.Length<3||!int.TryParse(parts[^2],out var port)||port is <1 or >65535||Uri.CheckHostName(parts[^1])!=UriHostNameType.Dns)throw new InvalidOperationException(Missing);
                origins.Add("https://"+parts[^1].ToLowerInvariant()+(port==443?"/":":"+port+"/"));
            }
            matches.Add(origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        if(matches.Count!=1||matches[0].Length!=1)throw new InvalidOperationException(Missing);
        return matches[0][0];
    }
    static string AppCmd(string executable,params string[] args)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new InvalidOperationException(Missing);
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(10000)){process.Kill(true);throw new InvalidOperationException(Missing);}
        var output=stdout.GetAwaiter().GetResult();var error=stderr.GetAwaiter().GetResult();
        if(process.ExitCode!=0||!string.IsNullOrWhiteSpace(error)||output.Length>1024*1024)throw new InvalidOperationException(Missing);
        return output;
    }
    public static string ResolveInstalled(string publishRoot)
    {
        if(!OperatingSystem.IsWindows())throw new InvalidOperationException(Missing);
        var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var executable=Path.Combine(windows,Environment.Is64BitOperatingSystem&&!Environment.Is64BitProcess?"Sysnative":"System32","inetsrv","appcmd.exe");
        if(!File.Exists(executable))throw new InvalidOperationException(Missing);
        return Resolve(publishRoot,AppCmd(executable,"list","site","/xml"),site=>AppCmd(executable,"list","vdir","/vdir.name:"+site+"/","/text:physicalPath").Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries));
    }
    public static void Apply(IConfiguration config,string publishRoot)=>Apply(config,publishRoot,ResolveInstalled);
    internal static void Apply(IConfiguration config,string publishRoot,Func<string,string> readIisOrigin)
    {
        if(string.IsNullOrWhiteSpace(config["Public:BaseUrl"]))
        {
            config["Public:BaseUrl"]=readIisOrigin(publishRoot);
        }
        if(Uri.TryCreate(config["Public:BaseUrl"],UriKind.Absolute,out var origin)&&string.IsNullOrWhiteSpace(config["Deployment:ExpectedHost"]))config["Deployment:ExpectedHost"]=origin.IdnHost;
        // UatDeployment.Validate still enforces HTTPS, exact configured host,
        // origin-only URL, SQL identity/TLS and private storage boundaries.
    }
}
