using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;

static class SourceReadyAssets
{
    internal static async Task Run(string input,string output)
    {
        output=Path.GetFullPath(output);
        if(!output.StartsWith(Path.Combine(Directory.GetCurrentDirectory(),".local")+Path.DirectorySeparatorChar))throw new ArgumentException("Local output only");
        Directory.CreateDirectory(output);
        var requests=JsonSerializer.Deserialize<List<Request>>(await File.ReadAllTextAsync(input))!;
        if(requests.Count>12)throw new ArgumentException("Maximum twelve explicitly selected resources");
        await using var db=new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().Options);
        var storage=new StorageOptions{SnapshotRoot=output,MediaRoot=output,TempRoot=output,KeysRoot=output};var media=new MediaService(db,storage,new());var facts=new List<object>();
        using var client=new SafeSourceClient(16);using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var robots=await client.FetchAsync(SourceCatalog.BaseUrl+"/robots.txt",128*1024,timeout.Token,true);
        if(robots.Status is not(200 or 404))throw new IOException("Robots unavailable");
        var rules=new SourceSyncRobots(robots.Status==200?Encoding.UTF8.GetString(robots.Bytes):"");
        foreach(var r in requests)
        {
            if(new Uri(r.Url).Host=="qldd.mae.gov.vn"&&!rules.Allows(r.Url))throw new IOException("Robots denied");
            var response=await client.FetchAsync(r.Url,10*1024*1024,timeout.Token,false);
            SourceSyncResourcePolicy.ValidateResponse(response,r.Mime);
            var sha=Convert.ToHexStringLower(SHA256.HashData(response.Bytes));var path=Path.Combine(output,sha+(r.Mime=="application/pdf"?".pdf":r.Mime=="image/jpeg"?".jpg":".png"));
            if(!File.Exists(path))await File.WriteAllBytesAsync(path,response.Bytes,timeout.Token);
            media.ValidateContent(path,r.Mime,timeout.Token);
            string? header=null;
            if(r.Mime=="application/pdf")
            {
                ManagedPdfPolicy.Validate(path,timeout.Token);
                using var pdf=UglyToad.PdfPig.PdfDocument.Open(path,new(){UseLenientParsing=false,MaxStackDepth=64});
                header=string.Join(" ",pdf.GetPage(1).GetWords().Select(x=>x.Text));
                if(r.Week is int w&&SourceWeeklyEvidence.HeaderPeriod(header[..Math.Min(header.Length,1800)])!=(2025,w))throw new InvalidDataException("Actual PDF header period differs");
            }
            facts.Add(new{r.Id,r.Url,r.Mime,sha,bytes=response.Bytes.Length,file=Path.GetFileName(path),r.Week,header});
            await File.WriteAllTextAsync(Path.Combine(output,"ready.json"),JsonSerializer.Serialize(facts));Console.WriteLine("PASS captured/decoded "+r.Id+" "+sha);
        }
    }
    sealed record Request(string Id,string Url,string Mime,int? Week=null);
}
