using System.Text.Json;
using LandPortal.MigrationCli;
namespace LandPortal.Tests;
public static class NukeRealTests
{
    public static async Task RunAsync(string root)
    {
        var input=Environment.GetEnvironmentVariable("LandPortal__NukeBundle")??throw new InvalidDataException("Exact current real bundle required");
        using var json=JsonDocument.Parse(await File.ReadAllBytesAsync(input));var previews=new List<SecondarySourcePreview>();
        foreach(var item in json.RootElement.GetProperty("Snapshots").EnumerateArray()){
            var raw=await File.ReadAllBytesAsync(Path.GetFullPath(item.GetProperty("Path").GetString()!,root));var sha=item.GetProperty("Sha256").GetString()!;if(SourceInventory.Hash(raw)!=sha)throw new InvalidDataException("Raw drift");
            var html=System.Text.Encoding.UTF8.GetString(raw);var url=item.GetProperty("Url").GetString()!;var p=MigrationSourceIdentityPolicy.ParseNukeViet(html,url,sha);
            if(p.Title=="Bộ nông nghiệp và môi trường"||p.Title.Length<10||p.Identity.Module!="news"||p.PublicationStatus!="PrivateInventoryOnly"||p.RawMetadata?.ContainsKey("datePublished")!=true)throw new InvalidDataException("Actual article headline/metadata/namespace absent");
            foreach(var bad in new[]{html.Replace("nv_module_name=\"news\"","nv_module_name=\"users\""),html.Replace("nv_func_name=\"detail\"","nv_func_name=\"main\""),html.Replace("rel=\"canonical\"","rel=\"notcanonical\"")}){
                var denied=false;try{MigrationSourceIdentityPolicy.ParseNukeViet(bad,url,SourceInventory.HashText(bad));}catch(InvalidDataException){denied=true;}if(!denied)throw new InvalidDataException("Tampered source module/canonical accepted");
            }
            previews.Add(p);
        }
        if(previews.Count==0||previews.Select(x=>x.Identity.Key).Distinct().Count()!=previews.Count)throw new InvalidDataException("No unique real news");
        var evidence=TestEvidencePaths.At(root);Directory.CreateDirectory(evidence);await File.WriteAllTextAsync(Path.Combine(evidence,"real-nukeviet-parser.json"),JsonSerializer.Serialize(new{status="PASS",snapshots=previews.Count,negativeChecks=previews.Count*3,transport="Anonymous public HTTP; TLS source repair and business mapping still pending",sqlWrites=0,networkRequests=0,previews},new JsonSerializerOptions{WriteIndented=true}));
    }
}
