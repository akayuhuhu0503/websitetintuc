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
            var captured=item.GetProperty("Files").EnumerateArray().Select(x=>x.GetProperty("Url").GetString()!).ToArray();
            var draft=NukeVietDraftCandidate.Create(p,captured);
            if(draft.PublicAllowed||draft.SqlImported||draft.Author!=null||draft.BodyHtml!=p.BodyHtml||draft.RawSha256!=sha||draft.PublishedUtc==null||draft.PublishedUtc.Value.Offset!=TimeSpan.Zero||draft.MissingAssets.Length!=p.AssetUrls.Except(captured).Count())throw new InvalidDataException("Normalized private draft changed raw data, attribution, date or asset constraints");
            var badDate=new Dictionary<string,string>(p.RawMetadata!){["datePublished"]="2025-07-25+0711:15:00"};
            var held=NukeVietDraftCandidate.Create(p with{RawMetadata=badDate},Array.Empty<string>());
            if(held.PublishedUtc!=null||!held.ReviewReasons.Contains("PUBLISHED_DATE_MISSING_OR_INVALID")||held.Author!=null)throw new InvalidDataException("Malformed date/account silently promoted");
            foreach(var bad in new[]{html.Replace("nv_module_name=\"news\"","nv_module_name=\"users\""),html.Replace("nv_func_name=\"detail\"","nv_func_name=\"main\""),html.Replace("rel=\"canonical\"","rel=\"notcanonical\"")}){
                var denied=false;try{MigrationSourceIdentityPolicy.ParseNukeViet(bad,url,SourceInventory.HashText(bad));}catch(InvalidDataException){denied=true;}if(!denied)throw new InvalidDataException("Tampered source module/canonical accepted");
            }
            previews.Add(p);
        }
        if(previews.Count==0||previews.Select(x=>x.Identity.Key).Distinct().Count()!=previews.Count)throw new InvalidDataException("No unique real news");
        var redirects=new List<NukeVietRedirect>();
        if(Environment.GetEnvironmentVariable("LandPortal__NukeCapture") is{Length:>0}capture)
        {
            using var recorded=JsonDocument.Parse(await File.ReadAllBytesAsync(capture));
            foreach(var row in recorded.RootElement.GetProperty("rows").EnumerateArray().Where(x=>x.GetProperty("httpStatus").GetInt32()==301))
            {
                var url=row.GetProperty("url").GetString()!;var location=row.GetProperty("location").GetString()!;
                redirects.Add(NukeVietRedirect.Resolve(url,301,location));
                foreach(var bad in new[]{"http://other.invalid/news/x-1.html","/news/x-999999.html","/users/login.html"})
                {bool denied=false;try{NukeVietRedirect.Resolve(url,301,bad);}catch(InvalidDataException){denied=true;}if(!denied)throw new InvalidDataException("Untrusted redirect accepted");}
            }
        }
        var evidence=TestEvidencePaths.At(root);Directory.CreateDirectory(evidence);var output=Path.Combine(evidence,"real-nukeviet-parser.json");await using var file=new FileStream(output,FileMode.CreateNew);await JsonSerializer.SerializeAsync(file,new{status="PASS",snapshots=previews.Count,negativeChecks=previews.Count*4+redirects.Count*3,redirects,transport="Anonymous public HTTP; TLS source repair and business mapping still pending",sqlWrites=0,networkRequests=0,previews},new JsonSerializerOptions{WriteIndented=true});
    }
}
