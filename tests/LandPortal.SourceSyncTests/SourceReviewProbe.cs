using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;

static class SourceReviewProbe
{
    // Explicit read-only audit. Uses the runtime transport, robots and byte limits;
    // cached bytes can be replayed without downloading again. No configuration/SQL.
    public static async Task Run(string input, string output, bool offline=false)
    {
        output = Path.GetFullPath(output);
        if (!output.StartsWith(Path.Combine(Directory.GetCurrentDirectory(), ".local") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Local output only");
        Directory.CreateDirectory(output);
        var rows = JsonDocument.Parse(await File.ReadAllTextAsync(input)).RootElement.GetProperty("rows");
        await using var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().Options);
        var storage = new StorageOptions { MediaRoot=output, SnapshotRoot=output, TempRoot=output, KeysRoot=output };
        var media = new MediaService(db, storage, new MediaUploadOptions());
        var facts = new List<object>();
        foreach (var row in rows.EnumerateArray())
        {
            var url = row.GetProperty("sourceUrl").GetString()!;
            var id = SourceSyncPolicy.Id(url)!;
            if (row.GetProperty("text").GetString()!.Contains("Lịch cần đối soát") || row.GetProperty("text").GetString()!.Contains("Có lịch CMS")) continue;
            using var client = new SafeSourceClient(20);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var assets = new List<object>(); string? error=null;
            try
            {
                SourceSyncRobots? rules=null;
                async Task<SourceResponse> Fetch(string target,int max,bool page)
                {
                    var key=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(target)));
                    var cache=Path.Combine(output,key+".json");
                    if(File.Exists(cache))return JsonSerializer.Deserialize<SourceResponse>(await File.ReadAllTextAsync(cache))!;
                    if(offline)throw new IOException("NOT_CAPTURED: offline replay never fetches missing bytes");
                    if(new Uri(target).Host=="qldd.mae.gov.vn")
                    {
                        if(rules==null){var robots=await client.FetchAsync(SourceCatalog.BaseUrl+"/robots.txt",128*1024,timeout.Token,true);
                            if(robots.Status is not(200 or 404))throw new IOException("Robots unavailable");
                            rules=new SourceSyncRobots(robots.Status==200?Encoding.UTF8.GetString(robots.Bytes):"");}
                        if(!rules.Allows(target))throw new IOException("Robots denied");
                    }
                    var result=await client.FetchAsync(target,max,timeout.Token,page);
                    await File.WriteAllTextAsync(cache,JsonSerializer.Serialize(result));return result;
                }
                var page=await Fetch(url,2*1024*1024,true);
                if(page.Status!=200)throw new IOException("Page HTTP "+page.Status);
                var parsed=SourceParser.Parse(Encoding.UTF8.GetString(page.Bytes),page.Url,ContentKind.Article,id);
                foreach(var assetUrl in parsed.AssetUrls)
                {
                    string? failure=null; string? inner=null; string? sha=null;long bytes=0;
                    try
                    {
                        var response=await Fetch(assetUrl,10*1024*1024,false);
                        bytes=response.Bytes.Length;sha=Convert.ToHexStringLower(SHA256.HashData(response.Bytes));
                        var path=Path.Combine(output,sha+".bin");if(!File.Exists(path))await File.WriteAllBytesAsync(path,response.Bytes);
                        var extension=Path.GetExtension(new Uri(assetUrl).AbsolutePath).ToLowerInvariant();
                        var mime=extension switch{".jpg" or ".jpeg"=>"image/jpeg",".png"=>"image/png",".gif"=>"image/gif",".webp"=>"image/webp",_=>OfficeDocumentPolicy.Mime(assetUrl)};
                        SourceSyncResourcePolicy.ValidateResponse(response,mime);
                        if(mime.StartsWith("image/"))media.ValidateContent(path,mime,timeout.Token);
                        else { OfficeDocumentPolicy.Validate(path,mime,media,true); if(mime=="application/pdf")ManagedPdfPolicy.Validate(path,timeout.Token); }
                    }
                    catch(Exception e) when(e is not OutOfMemoryException){failure=e.GetType().Name+": "+e.Message;inner=e.InnerException?.Message;}
                    assets.Add(new{url=assetUrl,bytes,sha,failure,inner});
                }
            }
            catch(Exception e)when(e is not OutOfMemoryException){error=e.GetType().Name+": "+e.Message;}
            facts.Add(new{id,url,error,assets});
            await File.WriteAllTextAsync(Path.Combine(output,offline?"review-after.json":"review.json"),JsonSerializer.Serialize(facts,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"Reviewed {id}: {assets.Count} resources; {error??"page read"}");
        }
    }
}
