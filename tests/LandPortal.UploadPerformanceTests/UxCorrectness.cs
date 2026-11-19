using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

// Only the fresh LocalDB test created and guarded by Program. Never operational SQL.
public static class UxCorrectness
{
    public static async Task Run(DbContextOptions<PortalDbContext> options,HttpClient guest,string run,Action<bool,string> check)
    {
        await using var db=new PortalDbContext(options);
        var cs=new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        if(!Regex.IsMatch(cs.DataSource,@"^\(localdb\)\\LPPerf_[a-f0-9]{12}$")||cs.InitialCatalog!="LandPortal_Test")throw new Exception("Isolated fixture only");
        await db.Database.OpenConnectionAsync();
        var query=new PublicContentQuery(db,new());
        var ids=Enumerable.Range(1,14).ToArray();
        var cards=await query.CategoryCardsAsync(ids);
        check(cards.Count==13&&cards.Values.All(x=>x.Count==4),"Bounded four cards per nonempty category");
        foreach(var id in ids){
            var expected=await PublicContentQuery.Recent(query.HomeArticles().Where(r=>db.RevisionCategories.Any(rc=>rc.RevisionId==r.RevisionId&&rc.CategoryId==id))).Take(4).Select(r=>r.RevisionId).ToArrayAsync();
            check(cards.GetValueOrDefault(id,[]).Select(r=>r.RevisionId).SequenceEqual(expected),"Batched category matches public query "+id);
        }
        var storage=new StorageOptions{MediaRoot=Path.Combine(run,"media"),TempRoot=Path.Combine(run,"temp"),SnapshotRoot=Path.Combine(run,"snapshots"),KeysRoot=Path.Combine(run,"keys")};
        var configService=new SiteConfigurationService(db,storage,new());
        var state=(await configService.ReadAsync(true))!;
        var site=new SitePublicConfiguration(state.Revision,[],[],null,false);
        var service=new PublicSiteService(db,configService,query,new(),new HttpContextAccessor());
        var home=await service.HomeAsync(site);
        check(home.VisibleBlocks.Count(b=>b.Block.Type=="Category")==14,"Configured homepage retains all fourteen including empty");
        check(home.VisibleBlocks.Single(b=>b.Block.CategoryId==14).Items.Count==0,"Empty category has no fabricated article");
        var firstBlock=site.Revision.Blocks.Single(b=>b.CategoryId==1);
        var old=await db.Contents.Where(c=>c.PrimaryCategoryId==1).OrderBy(c=>c.CreatedUtc).FirstAsync();
        var foreign=cards[2][0].Id;
        site.Revision.Pins.Add(new(){BlockId=firstBlock.Id,ContentId=old.Id,SortOrder=0});
        site.Revision.Pins.Add(new(){BlockId=firstBlock.Id,ContentId=foreign,SortOrder=1});
        var pinned=(await service.HomeAsync(site)).Blocks.Single(b=>b.Block.Id==firstBlock.Id).Items;
        check(pinned[0].Id==old.Id&&pinned.Count==4&&pinned.DistinctBy(x=>x.Id).Count()==4&&!pinned.Any(x=>x.Id==foreign),"Pins respect category and never duplicate cards");
        site.Revision.Blocks.Single(b=>b.CategoryId==2).Enabled=false;
        site.Revision.Categories.Single(c=>c.CategoryId==3).ShowInNavigation=false;
        home=await service.HomeAsync(site);
        check(!home.Blocks.Any(b=>b.Block.CategoryId is 2 or 3),"Disabled blocks and hidden categories remain hidden");
        // Changes above are preview objects; never save a configuration revision.
        db.ChangeTracker.Clear();
        var removed=await db.Contents.SingleAsync(c=>c.Id==cards[1][0].Id);
        removed.IsPublic=false;await db.SaveChangesAsync();
        check(!(await query.CategoryCardsAsync([1]))[1].Any(x=>x.Id==removed.Id),"Withdrawal visible immediately without shared cache");
        removed.IsPublic=true;removed.IsTrashed=true;await db.SaveChangesAsync();
        check(!(await query.CategoryCardsAsync([1]))[1].Any(x=>x.Id==removed.Id),"Trashed article excluded despite public flag");

        var blocked=new DisclosureGroup{Id="ux-unapproved",Name="Synthetic",Status="NeedsNameApproval"};db.DisclosureGroups.Add(blocked);
        foreach(var marker in new[]{"weekly","unapproved","draft","no-pointer"}){
            var c=new Content{Kind=ContentKind.Article,PrimaryCategoryId=1,OwnerUserId=old.OwnerUserId,CreatedUtc=DateTimeOffset.UtcNow,IsPublic=marker!="draft",State=WorkflowState.Published,PublishedAtUtc=DateTimeOffset.UtcNow};
            var r=new ContentRevision{Content=c,Title="Excluded "+marker,PrimaryCategoryId=1,EditedByUserId=old.OwnerUserId,CreatedUtc=DateTimeOffset.UtcNow};
            if(marker=="weekly")r.Metadata.WeeklyWeek=1;
            if(marker=="unapproved")r.Metadata.DisclosureGroupId=blocked.Id;
            r.Categories.Add(new(){CategoryId=1});c.Revisions.Add(r);db.Add(c);await db.SaveChangesAsync();
            c.CurrentRevisionId=r.Id;if(marker!="no-pointer")c.PublishedRevisionId=r.Id;await db.SaveChangesAsync();
        }
        check((await query.CategoryCardsAsync([1]))[1].All(x=>!x.Title.StartsWith("Excluded ")),"Weekly draft missing-pointer and unapproved disclosure excluded");
        using(var r=await guest.GetAsync("/time")){
            var utc=JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("utc").GetDateTimeOffset();
            check(r.IsSuccessStatusCode&&r.Headers.CacheControl?.NoStore==true&&Math.Abs((utc-DateTimeOffset.UtcNow).TotalSeconds)<2,"Time endpoint returns uncached UTC within two seconds on synchronized test host");
        }
        // Exercise real fallback Razor via HTTP without changing production or creating CMS defaults.
        var header=await db.Set<SiteConfiguration>().SingleAsync();var live=header.LiveRevisionId;
        header.LiveRevisionId=null;
        var firstCategory=await db.Categories.SingleAsync(c=>c.Id==1);firstCategory.SourceUrl="https://example.invalid/category/1";
        await db.SaveChangesAsync();
        try{
            using var response=await guest.GetAsync("/");var html=await response.Content.ReadAsStringAsync();var doc=new HtmlParser().ParseDocument(html);
            check(response.IsSuccessStatusCode&&doc.QuerySelectorAll(".category-block, .home-compact-category").Length==14,"Fallback HTTP renders fourteen categories");
            await File.WriteAllTextAsync(Path.Combine(run,"fallback.html"),html);
        }finally{header.LiveRevisionId=live;await db.SaveChangesAsync();}
    }
}
