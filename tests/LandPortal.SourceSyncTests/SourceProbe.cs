using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;

static class SourceProbe
{
    // Explicit opt-in read-only probe: robots + home + one recent article, no
    // SQL, no media downloads, no retry, at most three requests and 45 seconds.
    public static async Task Run(string output)
    {
        output=Path.GetFullPath(output);
        if(!output.StartsWith(Path.Combine(Directory.GetCurrentDirectory(),".local")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(output))throw new ArgumentException("New local probe directory required");
        Directory.CreateDirectory(output);using var source=new SafeSourceClient(3);using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var facts=new List<object>();
        try
        {
            var robots=await source.FetchAsync(SourceCatalog.BaseUrl+"/robots.txt",128*1024,ct.Token,true);
            facts.Add(new{stage="robots",robots.Status});if(robots.Status is not(200 or 404))return;
            var rules=new SourceSyncRobots(robots.Status==200?Encoding.UTF8.GetString(robots.Bytes):"");
            if(!rules.Allows(SourceCatalog.BaseUrl+"/")){facts.Add(new{blocked="robots"});return;}
            var home=await source.FetchAsync(SourceCatalog.BaseUrl+"/",2*1024*1024,ct.Token,true);
            facts.Add(new{stage="home",home.Status,bytes=home.Bytes.Length});if(home.Status!=200)return;
            await Save(home);
            var links=SourceParser.DiscoverNewsLinks(Encoding.UTF8.GetString(home.Bytes),home.Url).Where(x=>SourceSyncPolicy.Id(x.Url)==x.SourceId).Take(20).ToArray();
            facts.Add(new{discovered=links.Length});var first=links.FirstOrDefault();if(first==null||!rules.Allows(first.Url))return;
            var detail=await source.FetchAsync(first.Url,2*1024*1024,ct.Token,true);await Save(detail);
            var parsed=SourceParser.Parse(Encoding.UTF8.GetString(detail.Bytes),detail.Url,ContentKind.Article,first.SourceId);
            facts.Add(new{stage="detail",detail.Status,parsed.SourceId,parsed.PrimaryCategoryId,titlePresent=parsed.Title.Length>0,bodyPresent=parsed.BodyHtml.Length>0,identityMatches=SourceSyncPolicy.Id(parsed.CanonicalUrl)==first.SourceId,assets=parsed.AssetUrls.Count});
        }
        catch(Exception e){facts.Add(new{blocked=e.GetType().Name});}
        finally{var json=JsonSerializer.Serialize(new{source.RequestsUsed,facts,sqlTouched=false});await File.WriteAllTextAsync(Path.Combine(output,"result.json"),json);Console.WriteLine(json);}
        async Task Save(SourceResponse response){var sha=Convert.ToHexStringLower(SHA256.HashData(response.Bytes));await File.WriteAllBytesAsync(Path.Combine(output,sha+".html"),response.Bytes);}
    }
}
