using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace LandPortal.Infrastructure;

// Read-only ACL inspection. No account-name substring matching and no ACL edits.
public static class PrivateStorageAcl
{
    public sealed record Finding(string Path,string Code,string? Sid,string? Rights,bool? Inherited,string? Propagation);
    static readonly HashSet<string> Broad = ["S-1-1-0","S-1-5-11","S-1-5-32-545","S-1-5-32-568"];
    public static IReadOnlyList<Finding> Inspect(string path)
    {
        path=System.IO.Path.GetFullPath(path);
        if(!OperatingSystem.IsWindows())return [new(path,"ACL_WINDOWS_REQUIRED",null,null,null,null)];
        try
        {
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return [new(path,"ACL_REPARSE_REFUSED",null,null,null,null)];
            var rules=Directory.Exists(path)?new DirectoryInfo(path).GetAccessControl().GetAccessRules(true,true,typeof(SecurityIdentifier)):new FileInfo(path).GetAccessControl().GetAccessRules(true,true,typeof(SecurityIdentifier));
            var result=new List<Finding>();
            foreach(FileSystemAccessRule rule in rules)
                if(rule.AccessControlType==AccessControlType.Allow&&Broad.Contains(rule.IdentityReference.Value))
                    result.Add(new(path,"ACL_BROAD_ALLOW",rule.IdentityReference.Value,rule.FileSystemRights.ToString(),rule.IsInherited,rule.PropagationFlags.ToString()));
            return result;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {return [new(path,"ACL_UNREADABLE",null,null,null,null)];}
    }
    public static void Require(string path)
    {
        // Compatibility entry point for older consumers. ACL composition is
        // advisory under the owner's current portable IIS handoff policy.
        // Actual filesystem opens, hashes, boundary and scanner guards decide.
        System.Diagnostics.Trace.WriteLine("ACL advisory only: "+path);
    }
}
