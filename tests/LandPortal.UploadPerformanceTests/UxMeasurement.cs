using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

// Called only after Program's fresh, isolated LocalDB guard. Identical fixture for both binaries.
public static class UxMeasurement
{
    public static async Task Seed(DbContextOptions<PortalDbContext> options,string owner)
    {
        await using var db=new PortalDbContext(options);
        for(int i=2;i<=14;i++)db.Categories.Add(new(){Id=i,Name="Category "+i,Slug="category-"+i,SortOrder=i-1,SourceUrl="https://example.invalid/category/"+i});
        await db.SaveChangesAsync();
        var revision=new SiteConfigurationRevision{OrganizationName="Isolated UX test"};
        foreach(var c in await db.Categories.OrderBy(c=>c.Id).ToListAsync()){
            revision.Categories.Add(new(){CategoryId=c.Id,DisplayName=c.Name,Slug=c.Slug,SortOrder=c.Id-1});
            revision.Blocks.Add(new(){Type="Category",Title=c.Name,CategoryId=c.Id,Limit=4,SortOrder=c.Id+3});
        }
        revision.Blocks.Add(new(){Type="Featured",Title="Featured",Limit=3});
        revision.Blocks.Add(new(){Type="Recent",Title="Recent",Limit=4,SortOrder=1});
        db.Add(revision);await db.SaveChangesAsync();
        db.Add(new SiteConfiguration{Id=1,LiveRevisionId=revision.Id,DraftRevisionId=revision.Id});
        var now=DateTimeOffset.Parse("2026-11-01T00:00:00Z");
        for(int category=1;category<14;category++)for(int i=0;i<20;i++){
            var c=new Content{Kind=ContentKind.Article,PrimaryCategoryId=category,OwnerUserId=owner,CreatedUtc=now.AddMinutes(i),State=WorkflowState.Published,IsPublic=true,PublishedAtUtc=now.AddMinutes(i)};
            var r=new ContentRevision{Content=c,Title=$"Fixture {category} article {i}",Summary="Synthetic article",BodyHtml="<p>"+new string('x',10000)+"</p>",PrimaryCategoryId=category,CreatedUtc=now.AddMinutes(i),EditedByUserId=owner};
            r.Categories.Add(new(){CategoryId=category});c.Revisions.Add(r);db.Add(c);
        }
        await db.SaveChangesAsync();
        foreach(var c in db.ChangeTracker.Entries<Content>().Select(e=>e.Entity)){c.CurrentRevisionId=c.Revisions.Single().Id;c.PublishedRevisionId=c.CurrentRevisionId;}
        await db.SaveChangesAsync();
    }
    public static async Task ReadPages(HttpClient client,string run,Action<bool,string> check,bool homeOnly=false,HttpClient? guest=null)
    {
        var rows=new List<object>();
        foreach(var path in homeOnly?new[]{"/"}:new[]{"/admin","/admin?q=Fixture","/admin?status=Published","/admin/create","/admin/videos","/admin/site","/admin/source-sync","/","/tin-tuc"})
            for(int i=0;i<4;i++){
                var timer=System.Diagnostics.Stopwatch.StartNew();using var r=await (path=="/"||path=="/tin-tuc"?guest??client:client).GetAsync(path,HttpCompletionOption.ResponseHeadersRead);var ttfb=timer.Elapsed.TotalMilliseconds;
                var body=await r.Content.ReadAsStringAsync();check(r.IsSuccessStatusCode,"UX HTTP "+path);
                rows.Add(new{path,iteration=i,ttfb,elapsedMs=timer.Elapsed.TotalMilliseconds,bytes=System.Text.Encoding.UTF8.GetByteCount(body)});
                if(path=="/")await File.WriteAllTextAsync(Path.Combine(run,"home.html"),body);
            }
        await File.WriteAllTextAsync(Path.Combine(run,"ux-http.json"),JsonSerializer.Serialize(rows));
    }
}
