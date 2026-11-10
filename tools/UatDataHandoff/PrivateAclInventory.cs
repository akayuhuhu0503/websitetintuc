using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LandPortal.Infrastructure;

namespace LandPortal.UatData;

public static class PrivateAclInventory
{
    public static int Run(string iisRoot,string outputRoot)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows ACL required.");
        var root=Path.GetFullPath(iisRoot);DataPackage.NoReparse(root);
        var home=Path.Combine(root,"private");
        var findings=new List<PrivateStorageAcl.Finding>();var pending=new Stack<string>();pending.Push(home);int checkedPaths=0;
        while(pending.TryPop(out var path))
        {
            if(++checkedPaths>20000){findings.Add(new(path,"ACL_AUDIT_LIMIT",null,null,null,null));break;}
            var entries=PrivateStorageAcl.Inspect(path);findings.AddRange(entries);
            if(entries.Any(e=>e.Code is "ACL_REPARSE_REFUSED" or "ACL_UNREADABLE"))continue;
            if(Directory.Exists(path))
                try{foreach(var child in Directory.EnumerateFileSystemEntries(path))pending.Push(child);}
                catch(Exception e)when(e is IOException or UnauthorizedAccessException){findings.Add(new(path,"ACL_ENUMERATION_DENIED",null,null,null,null));}
        }
        string? poolSid=null;
        try{poolSid=new NTAccount(@"IIS APPPOOL\WWW_GDLA").Translate(typeof(SecurityIdentifier)).Value;}catch(IdentityNotMappedException){}
        var output=Path.Combine(Path.GetFullPath(outputRoot),"ACL-"+Guid.NewGuid().ToString("N"));DataPackage.NoReparse(output);
        if(output.StartsWith(Path.Combine(root,"web")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Diagnostics must be outside webroot.");
        Directory.CreateDirectory(output);
        var report=Path.Combine(output,"ACL-CHI-TIET.json");
        var result=new{status=findings.Count==0?"PRIVATE_ACL_INVENTORY_PASS":"PRIVATE_ACL_INVENTORY_WARNING",utc=DateTimeOffset.UtcNow,checkedPaths,poolSid,findings,sqlWrites=0,aclChangesToIis=0,nativeScans=0};
        using(var stream=new FileStream(report,FileMode.CreateNew,FileAccess.Write,FileShare.None))JsonSerializer.Serialize(stream,result,new JsonSerializerOptions{WriteIndented=true});
        Console.WriteLine(JsonSerializer.Serialize(new{result.status,checkedPaths,warnings=findings.Count,blocked=0,report,examples=findings.Take(3),sqlWrites=0}));
        return 0;
    }
}
