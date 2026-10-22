using System.Diagnostics;
using System.Text.Json;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Tests;

public static class PublicQueryProbe
{
    public static async Task RunAsync(string root, DbContextOptions<PortalDbContext> options)
    {
        await using var db = new PortalDbContext(options);
        var category = await db.Categories.AsNoTracking().SingleAsync(x => x.Slug == "xay-dung-pho-bien-chinh-sach-phap-luat");
        var query = new PublicContentQuery(db);
        var rows = query.Published().Where(x => db.RevisionCategories.Any(c => c.RevisionId == x.RevisionId && c.CategoryId == category.Id) || x.CategoryId == category.Id);
        var page = PublicContentQuery.Recent(PublicContentQuery.ForDisplay(rows)).Skip(0).Take(10);
        var dir = TestEvidencePaths.At(root, "v04");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "public-category-query.sql"), page.ToQueryString());
        var narrow=PublicContentQuery.Recent(rows).Skip(0).Take(10).Select(x=>x.RevisionId);
        await File.WriteAllTextAsync(Path.Combine(dir,"public-category-ids-query.sql"),narrow.ToQueryString());
        var samples = new List<object>();
        for (var i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew();
            var found = await page.ToListAsync();
            samples.Add(new { run=i+1, rows=found.Count, elapsedMs=watch.Elapsed.TotalMilliseconds });
        }
        await File.WriteAllTextAsync(Path.Combine(dir, "public-category-query-probe.json"), JsonSerializer.Serialize(new { utc=DateTimeOffset.UtcNow, database="LandPortal_Test", total=await rows.CountAsync(), samples },new JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine("PASS public category SQL read-only probe (three executions).");
        var bounded=new List<object>();for(var i=0;i<3;i++){var watch=Stopwatch.StartNew();var found=await query.CardsPageAsync(PublicContentQuery.Recent(rows),0,10);bounded.Add(new{run=i+1,rows=found.Count,elapsedMs=watch.Elapsed.TotalMilliseconds});}
        await File.WriteAllTextAsync(Path.Combine(dir,"public-bounded-page-probe.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,database="LandPortal_Test",samples=bounded},new JsonSerializerOptions{WriteIndented=true}));
        var storage=StorageOptions.FromConfiguration(PortalConfiguration.Load(root));
        var media=MediaUploadOptions.FromConfiguration(PortalConfiguration.Load(root),"Development");
        var homeSamples=new List<object>();
        for(var i=0;i<3;i++)
        {
            await using var read=new PortalDbContext(options);
            var service=new PublicSiteService(read,new SiteConfigurationService(read,storage,media),new PublicContentQuery(read),media,new Microsoft.AspNetCore.Http.HttpContextAccessor());
            var watch=Stopwatch.StartNew();
            try{var site=await service.GetAsync()??throw new InvalidOperationException("Missing live config");var home=await service.HomeAsync(site);homeSamples.Add(new{run=i+1,elapsedMs=watch.Elapsed.TotalMilliseconds,blocks=home.Blocks.Count,failedBlocks=home.Blocks.Count(x=>x.ErrorReference!=null),status="COMPLETED"});}
            catch(Microsoft.Data.SqlClient.SqlException error){homeSamples.Add(new{run=i+1,elapsedMs=watch.Elapsed.TotalMilliseconds,status="SQL ERROR",code=error.Number});}
        }
        await File.WriteAllTextAsync(Path.Combine(dir,"public-home-query-probe.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,database="LandPortal_Test",samples=homeSamples,limit="Three serial homepage executions, default command timeout unchanged"},new JsonSerializerOptions{WriteIndented=true}));
    }
}
