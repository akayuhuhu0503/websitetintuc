using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;

namespace LandPortal.UatData;

public sealed record CategoryRow(int Id,string Name,string Slug,string SourceUrl,int SortOrder,int? ParentId);
public sealed record SourceRow(string Source,string SourceId,ContentKind Kind,string CanonicalUrl,string LastContentHash);
public sealed record AssetLink(Guid MediaAssetId,string Usage,string Caption,int SortOrder);
public sealed record RevisionRow(Guid Id,string Title,string Summary,string BodyHtml,string SearchText,string Author,string SourceCredit,DateTimeOffset CreatedUtc,bool IsSourceRevision,string ContentHash,int? PrimaryCategoryId,Guid? CoverAssetId,ContentMetadata Metadata);
public sealed record LegalRow(string Number,string DocumentType,string IssuingBody,string Signer,string Field,DateOnly? IssuedDate,DateOnly? EffectiveDate,string? EffectivenessStatus);
public sealed record DraftRow(DateTimeOffset? Start,DateTimeOffset? End,string DateEvidence);
public sealed record ContentRow(Guid Id,ContentKind Kind,DateTimeOffset CreatedUtc,DateTimeOffset? PublishedAtUtc,DateOnly? SourcePublishedDate,string OriginalDateText,string DatePrecision,bool CommentsEnabled,RevisionRow Revision,List<int> CategoryIds,List<AssetLink> Assets,List<SourceRow> Sources,List<string> Aliases,LegalRow? Legal,DraftRow? Draft);
public sealed record MediaRow(Guid Id,string Sha256,long Bytes,string MimeType,string SourceUrl,string OriginalName,int? Width,int? Height,DateTimeOffset CreatedUtc,string File,string SourceScanStatus,string SourceValidationStatus,bool TargetPolicyReady);
public sealed record Exclusion(string Entity,string Identity,string[] Reasons);
public sealed class DataBundle
{
    public int SchemaVersion { get; set; }=1;
    public Guid PackageId { get; set; }=Guid.NewGuid();
    public string SourceCommit { get; set; }="";
    public string SourceMigration { get; set; }="";
    public List<CategoryRow> Categories { get; set; }=[];
    public List<ContentRow> Ready { get; set; }=[];
    public List<ContentRow> PendingReview { get; set; }=[];
    public List<MediaRow> Media { get; set; }=[];
    public JsonElement? PublishedSite { get; set; }
    public List<Exclusion> Excluded { get; set; }=[];
    public Dictionary<string,int> Inventory { get; set; }=[];
}
public sealed record PackageManifest(int SchemaVersion,Guid PackageId,DateTimeOffset CreatedUtc,string DataFile,Dictionary<string,string> Files,string ReleaseRule);
public static class DataPackage
{
    public static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=false,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow};
    public static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static async Task<string> FileHash(string path){await using var f=File.OpenRead(path);return Convert.ToHexStringLower(await SHA256.HashDataAsync(f));}
    public static string PathIn(string root,string relative)
    {
        if(string.IsNullOrEmpty(relative)||relative.Contains('\\')||relative.Contains(':')||Path.IsPathRooted(relative)||relative.Split('/').Any(x=>x is "" or "." or ".."))throw new InvalidDataException("Portable relative package path required.");
        var full=StorageOptions.ResolveWithin(root,relative);NoReparse(full);return full;
    }
    public static void NoReparse(string path)
    {
        for(string? node=Path.GetFullPath(path);node!=null;node=Path.GetDirectoryName(node))
            if((File.Exists(node)||Directory.Exists(node))&&(File.GetAttributes(node)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Reparse path refused.");
    }
    public static async Task<(DataBundle Data,string Sha)> Load(string root)
    {
        var file=PathIn(root,"manifest.json");var bytes=await File.ReadAllBytesAsync(file);var manifest=JsonSerializer.Deserialize<PackageManifest>(bytes,Json)??throw new InvalidDataException("Manifest missing.");
        if(manifest.SchemaVersion!=1||manifest.DataFile!="data.json"||manifest.Files.Count>2000||!manifest.Files.ContainsKey("data.json"))throw new InvalidDataException("Unsupported manifest.");
        foreach(var entry in manifest.Files)if(entry.Value.Length!=64||await FileHash(PathIn(root,entry.Key))!=entry.Value)throw new InvalidDataException("Package file/hash mismatch: "+entry.Key);
        foreach(var fileInPackage in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories)){NoReparse(fileInPackage);var relative=Path.GetRelativePath(root,fileInPackage).Replace('\\','/');if(!manifest.Files.ContainsKey(relative)&&relative is not("manifest.json" or "manifest.sha256" or "checksums.sha256"))throw new InvalidDataException("Unlisted package file: "+relative);}
        var data=JsonSerializer.Deserialize<DataBundle>(await File.ReadAllTextAsync(PathIn(root,manifest.DataFile)),Json)??throw new InvalidDataException("Data missing.");
        if(data.SchemaVersion!=1||data.PackageId!=manifest.PackageId)throw new InvalidDataException("Package binding mismatch.");
        foreach(var m in data.Media)if(!manifest.Files.TryGetValue(m.File,out var fileSha)||fileSha!=m.Sha256||new FileInfo(PathIn(root,m.File)).Length!=m.Bytes)throw new InvalidDataException("Media descriptor/bytes mismatch.");
        var manifestSha=Hash(bytes);
        if((await File.ReadAllTextAsync(PathIn(root,"manifest.sha256"))).Trim()!=manifestSha+"  manifest.json")throw new InvalidDataException("Manifest checksum file differs.");
        var checksumLines=await File.ReadAllLinesAsync(PathIn(root,"checksums.sha256"));var expected=manifest.Files.Select(x=>x.Value+"  "+x.Key).Append(manifestSha+"  manifest.json").Order(StringComparer.Ordinal);
        if(!checksumLines.Order(StringComparer.Ordinal).SequenceEqual(expected,StringComparer.Ordinal))throw new InvalidDataException("Checksum listing differs.");
        Validate(data);return(data,Hash(bytes));
    }
    public static void Validate(DataBundle data)
    {
        var all=data.Ready.Concat(data.PendingReview).ToArray();var ids=all.Select(x=>x.Id).ToHashSet();var media=data.Media.ToDictionary(x=>x.Id);var cats=data.Categories.Select(x=>x.Id).ToHashSet();
        if(all.Length!=ids.Count||all.Select(x=>x.Revision.Id).Distinct().Count()!=all.Length||data.Categories.Count!=cats.Count||data.Media.Select(x=>x.Sha256).Distinct().Count()!=media.Count)throw new InvalidDataException("Duplicate package identities.");
        if(all.SelectMany(x=>x.Aliases).Distinct(StringComparer.Ordinal).Count()!=all.Sum(x=>x.Aliases.Count))throw new InvalidDataException("Alias overlap.");
        foreach(var row in all)
        {
            if(row.Sources.Count!=1||row.Sources[0].Source!="qldd.mae.gov.vn"||!row.Revision.IsSourceRevision||row.Kind==ContentKind.Video||string.IsNullOrWhiteSpace(row.Revision.Title)||row.CategoryIds.Any(x=>!cats.Contains(x)))throw new InvalidDataException("Non-source, private, unsupported or invalid content in package.");
            if(row.Sources[0].Kind!=row.Kind||string.IsNullOrWhiteSpace(row.Sources[0].SourceId)||row.Aliases.Any(x=>!Uri.TryCreate(x,UriKind.Absolute,out var uri)||uri.UserInfo!=""||uri.Host!="qldd.mae.gov.vn"||uri.Scheme is not("https" or "http")||x.Any(char.IsControl)))throw new InvalidDataException("Invalid source identity or public alias.");
            if(row.Assets.Any(x=>!media.ContainsKey(x.MediaAssetId))||row.Revision.CoverAssetId is Guid cover&&!media.ContainsKey(cover))throw new InvalidDataException("Missing asset dependency.");
            if(row.Revision.Metadata.WeeklyOriginalAssetId!=null||row.Revision.Metadata.WeeklyOriginalReviewEvidence!=null||row.Revision.Metadata.PublicationReviewEvidence!=null)throw new InvalidDataException("Private workflow metadata in package.");
        }
        foreach(var m in data.Media)if(m.SourceScanStatus!="Clean"||m.Bytes<1||m.Bytes>48L*1024*1024||m.Sha256.Length!=64||m.MimeType is not("application/pdf" or "image/jpeg" or "image/png" or "image/gif" or "image/webp"))throw new InvalidDataException("Unsafe or unsupported source media.");
        if(data.Ready.Any(x=>x.Assets.Any(a=>!media[a.MediaAssetId].TargetPolicyReady)||x.Revision.CoverAssetId is Guid cover&&!media[cover].TargetPolicyReady))throw new InvalidDataException("Development-only media in ready selection.");
    }
    public static async Task Write(string path,object value){await using var file=new FileStream(path,FileMode.CreateNew);await JsonSerializer.SerializeAsync(file,value,Json);}
}
