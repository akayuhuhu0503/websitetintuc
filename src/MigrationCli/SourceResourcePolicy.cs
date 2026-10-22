using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.Extensions.Configuration;
namespace LandPortal.MigrationCli;
public sealed record SourceResourceApproval(int SchemaVersion,long MaxPdfBytes,string ApprovedBy,string Reference,string[] Urls);
public static class SourceResourcePolicy
{
    public const long StandardPdfBytes=32L*1024*1024;
    public const long ApprovedPdfBytes=48L*1024*1024;
    public static (SourceResourceApproval Approval,string Sha)? Read(IConfiguration c)
    {
        var path=c["SourceImport:PolicyFile"];if(path==null)return null;
        var workspace=c["WorkspaceRoot"]??PortalConfiguration.FindWorkspace();var full=Path.GetFullPath(path,workspace);
        if(!full.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/evidence/phase3f"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Source-only policy must be private reviewed migration evidence.");
        SourceInventory.NoReparse(full);var bytes=File.ReadAllBytes(full);var sha=SourceInventory.Hash(bytes);
        if(sha!=c["SourceImport:PolicySha256"])throw new InvalidDataException("Source resource policy SHA differs.");
        var p=JsonSerializer.Deserialize<SourceResourceApproval>(bytes)??throw new InvalidDataException("Invalid source policy");
        if(p.SchemaVersion!=1||p.MaxPdfBytes!=ApprovedPdfBytes||string.IsNullOrWhiteSpace(p.ApprovedBy)||string.IsNullOrWhiteSpace(p.Reference)||p.Urls.Length is <1 or >100||p.Urls.Distinct().Count()!=p.Urls.Length)throw new InvalidDataException("Invalid bounded source policy approval");
        var hosts=SafeSourceClient.AllowedHosts.Concat(new[]{"monre.gov.vn","btnmt.1cdn.vn","media-dlrpd.monre.gov.vn","moitruongachau.com"}).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(p.Urls.Any(url=>!Uri.TryCreate(url,UriKind.Absolute,out var u)||u.Scheme is not("https" or "http")||!u.IsDefaultPort||u.UserInfo.Length!=0||!hosts.Contains(u.Host)))throw new InvalidDataException("Source policy contains unapproved host/scheme/port");
        return(p,sha);
    }
    public static MediaUploadOptions MediaOptions(IConfiguration c,string url)=>Read(c) is{}p&&p.Approval.Urls.Contains(url,StringComparer.Ordinal)
        ?new(){MaxPdfBytes=ApprovedPdfBytes}:new();
    public static long MaxPdf(IConfiguration c)=>Read(c)?.Approval.MaxPdfBytes??StandardPdfBytes;
}
