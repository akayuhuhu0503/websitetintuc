using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace LandPortal.Infrastructure;

// No password is returned to an HTTP caller, logger or audit. The operator
// transfers this private file through the approved out-of-band channel.
public sealed class TemporaryPasswordDelivery(string privateTempRoot,string? webRoot = null)
{
    public string Write(string userId,string userName,string password)
    {
        var directory=StorageOptions.ResolveWithin(privateTempRoot,"identity-credentials");
        foreach(var publicPath in new[]{AppContext.BaseDirectory,webRoot}.Where(x=>!string.IsNullOrWhiteSpace(x)))
        {
            var root=Path.GetFullPath(publicPath!).TrimEnd(Path.DirectorySeparatorChar);
            if(directory.Equals(root,StringComparison.OrdinalIgnoreCase)||directory.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Credential delivery must remain outside published code and webroot.");
        }
        for(string? node=directory;node!=null;node=Path.GetDirectoryName(node))
            if((Directory.Exists(node)||File.Exists(node))&&(File.GetAttributes(node)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidOperationException("Reparse credential path refused.");
        Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".json");
        using var file=CreatePrivate(path);
        JsonSerializer.Serialize(file,new { userId,userName,password,mustChangePassword=true,issuedUtc=DateTimeOffset.UtcNow },new JsonSerializerOptions{WriteIndented=true});
        file.Flush(true);
        return path;
    }
    private static FileStream CreatePrivate(string path)
    {
        if(OperatingSystem.IsWindows())
        {
            var security=new FileSecurity();security.SetAccessRuleProtection(true,false);
            foreach(var sid in new[]{WindowsIdentity.GetCurrent().User!,new SecurityIdentifier("S-1-5-18")})
                security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,AccessControlType.Allow));
            return new FileInfo(path).Create(FileMode.CreateNew,FileSystemRights.Write,FileShare.None,4096,FileOptions.None,security);
        }
        return new FileStream(path,new FileStreamOptions {Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None,UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite});
    }
}

// Receipt intentionally contains no plaintext credential. File survives uncertain
// SQL commit outcomes, restricted to the operator, for reconcile/reset handling.
public sealed record TemporaryPasswordReceipt(string UserId,string CredentialFile);
