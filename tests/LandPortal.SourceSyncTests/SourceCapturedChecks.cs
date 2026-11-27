using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;

static class SourceCapturedChecks
{
    // Read-only replay of explicitly captured public pages, not a crawler.
    public static async Task Run(string cache,string evidence,string output)
    {
        output=Path.GetFullPath(output);
        if(!output.StartsWith(Path.Combine(Directory.GetCurrentDirectory(),".local")+Path.DirectorySeparatorChar)||Directory.Exists(output))throw new ArgumentException("New local output required");
        Directory.CreateDirectory(output);
        using var before=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(cache,"review.json")));
        using var after=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(cache,"review-after.json")));
        var fixedUrls=after.RootElement.EnumerateArray().Where(r=>r.GetProperty("error").ValueKind==JsonValueKind.Null&&r.GetProperty("assets").EnumerateArray().All(a=>a.GetProperty("failure").ValueKind==JsonValueKind.Null))
            .Where(r=>before.RootElement.EnumerateArray().Single(b=>b.GetProperty("id").GetString()==r.GetProperty("id").GetString()).GetProperty("assets").EnumerateArray().Any(a=>a.GetProperty("failure").ToString().Contains("URL_POLICY"))).ToArray();
        var passed=0;
        foreach(var row in fixedUrls.Concat(after.RootElement.EnumerateArray().Where(r=>r.GetProperty("id").GetString() is "3781" or "3788")))
        {
            var id=row.GetProperty("id").GetString()!;var url=row.GetProperty("url").GetString()!;
            var key=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
            var response=JsonSerializer.Deserialize<SourceResponse>(await File.ReadAllTextAsync(Path.Combine(cache,key+".json")))!;
            var source=SourceParser.Parse(Encoding.UTF8.GetString(response.Bytes),url,ContentKind.Article,id);
            if(source.SourceId!=id||string.IsNullOrWhiteSpace(source.Title)||source.PublishedDate==null||source.AssetUrls.Any(u=>!SafeSourceClient.AllowedHosts.Contains(new Uri(u).Host)))throw new Exception("Source evidence mismatch "+id);
            if(id is "3781" or "3788" && source.PrimaryCategoryId!=80)throw new Exception("Official category 80 lost");
            Console.WriteLine("PASS captured source identity/date/first-party resources "+id);passed++;
        }
        using var weekly=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence,"weekly-comparison-verified.json")));
        var rows=weekly.RootElement.EnumerateArray().Where(r=>r.GetProperty("candidates").GetArrayLength()>0).Select(r=>new
        {
            sourceId=r.GetProperty("sourceId").GetString(),
            candidates=r.GetProperty("candidates").EnumerateArray().Select(c=>new
            {
                contentId=c.GetProperty("contentId").GetString(),title=c.GetProperty("title").GetString(),
                periodMatches=SourceWeeklySchedule.Period(c.GetProperty("title").GetString()!)==SourceWeeklySchedule.Period(r.GetProperty("title").GetString()!),
                identicalBasePdf=r.GetProperty("baseHashMatches").GetArrayLength()>0
            }).ToArray()
        }).ToArray();
        if(rows.Length!=27)throw new Exception("Unexpected weekly review input");
        await File.WriteAllTextAsync(Path.Combine(output,"weekly-candidates.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed,fixedFirstPartySources=fixedUrls.Length,weeklyCandidates=rows.Length,matchingTitles=rows.Count(r=>r.candidates.Any(c=>c.periodMatches)),liveWrites=0}));
        Console.WriteLine("PASS "+passed+" captured parser checks; wrote 27 weekly candidate comparisons");
    }
}
