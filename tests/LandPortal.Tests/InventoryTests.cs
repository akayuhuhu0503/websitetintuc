using System.Text;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Tests;
public static class InventoryTests
{
    private static void Assert(bool yes,string detail){if(!yes)throw new InvalidOperationException(detail);}
    private static async Task Reject<T>(Func<Task> action)where T:Exception{try{await action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    private static IConfiguration Config(IConfiguration source,string root)=>new ConfigurationBuilder().AddConfiguration(source).AddInMemoryCollection(new Dictionary<string,string?>{{"WorkspaceRoot",root},{"DOTNET_ENVIRONMENT","Development"},{"Sync:Enabled","true"},{"Sync:CutoverUtc",""}}).Build();
    private static async Task<SampleManifest> Legacy(IConfiguration config)=>JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(Path.Combine(StorageOptions.FromConfiguration(config).SnapshotRoot,"sample-manifest.json")),SampleCollector.JsonOptions)!;
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(string root,DbContextOptions<PortalDbContext> options,IConfiguration source)
    {
        var config=Config(source,root);
        yield return ("3A unit read-only URL routes kind separation robots and bounded XML reject external/admin/write/query traps",async()=>
        {
            var parent=SourceCatalog.BaseUrl+"/";
            Assert(InventoryDiscovery.Classify("/tin/du-thao-chinh-sach-2024.htm",parent)!.Kind==ContentKind.Article,"A title keyword became a document type.");
            Assert(InventoryDiscovery.Classify("/chi-tiet-van-ban-du-thao-13.htm",parent)!.Kind==ContentKind.DraftDocument,"Draft route classification failed.");
            Assert(InventoryDiscovery.Classify("/Pages/van-ban.aspx?ItemID=251",parent)!.Kind==ContentKind.LegalDocument,"Verified public canonical route missing.");
            Assert(InventoryDiscovery.Classify("/chuyen-muc/site-a-75-p4.htm",parent)!.Page==4,"Category page parsed as an article id.");
            foreach(var u in new[]{"javascript:alert(1)","file:///C:/data","https://qldd.mae.gov.vn:8443/a.htm","https://user@qldd.mae.gov.vn/a.htm","/_api/lists","/_api/private.pdf","/file.pdf?delete=1","/_layouts/15/SignOut.aspx","/Pages/edit.aspx?save=1","/foo.htm?delete=true"})Assert(InventoryDiscovery.Classify(u,parent) is null,"Unsafe read frontier accepted "+u);
            Assert(InventoryDiscovery.Classify("https://gdla.gov.vn/uploads/a.mp4",parent)!.Status=="OutOfScope","External video was implicitly authorized.");
            var robots="User-agent: *\nDisallow: /Pages/\nAllow: /Pages/van-ban.aspx\n";
            Assert(!InventoryDiscovery.RobotsAllows(robots,parent+"Pages/secret.aspx")&&InventoryDiscovery.RobotsAllows(robots,parent+"Pages/van-ban.aspx?ItemID=1"),"Robots longest matching rule failed.");
            Assert(InventoryDiscovery.FeedLinks("<urlset><url><loc>https://qldd.mae.gov.vn/a-1.htm</loc></url></urlset>").Count()==1,"Sitemap not read.");
            await Reject<System.Xml.XmlException>(()=>Task.Run(()=>InventoryDiscovery.FeedLinks("<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///C:/private'>]><rss>&x;</rss>").ToArray()));
            await Reject<InvalidOperationException>(()=>Task.Run(()=>new InventoryLimits{Requests=41}.Validate()));
        });
        yield return ("3A real saved snapshots all 37 canonical identities and list page2 distinct terminal evidence without network",async()=>
        {
            var legacy=await Legacy(config);var storage=StorageOptions.FromConfiguration(config);
            foreach(var c in legacy.Contents)
            {
                var snapshot=legacy.Snapshots.Last(x=>x.Url==c.Source.SourceUrl&&x.Status==200);var html=await File.ReadAllTextAsync(storage.ResolveSnapshot(snapshot.StorageKey));
                var parsed=SourceParser.Parse(html,c.Source.SourceUrl,c.Source.Kind,c.Source.SourceId);
                Assert(parsed.SourceId==c.Source.SourceId&&parsed.Title==c.Source.Title,"Saved canonical/title changed.");
                Assert(InventoryDiscovery.Classify(parsed.CanonicalUrl,c.Source.SourceUrl) is not null,"Canonical route rejected.");
            }
            var pages=legacy.Lists.Where(x=>x.CategoryId==75).OrderBy(x=>x.Pagination.StartPage).ToArray();
            Assert(pages.Length>=3&&pages.Any(x=>x.Pagination.StartPage==2)&&pages.Any(x=>x.Pagination.StartPage==x.Pagination.TotalPages),"Missing real next/terminal snapshots.");
            Assert(pages.Single(x=>x.Pagination.StartPage==2).SourceIds.Except(pages[0].SourceIds).Any(),"Page2 repeats first list.");
        });
        yield return ("3A SQL private stage offline replay atomic checkpoint new-context resume alias dedup no CMS writes",async()=>
        {
            var legacy=await Legacy(config);var first=legacy.Contents.First(x=>x.Source.Kind==ContentKind.Article);var snap=legacy.Snapshots.First(x=>x.Url==first.Source.SourceUrl&&x.Status==200);
            legacy.Snapshots.Add(snap with{Url=first.Source.CanonicalUrl});legacy.Discovered.Add(new(first.Source.CanonicalUrl,first.Source.SourceId,first.Source.Kind));
            var state=new InventoryState();int beforeContents,beforeAssets,beforeRevisions;
            await using(var db=new PortalDbContext(options))
            {
                beforeContents=await db.Contents.CountAsync();beforeAssets=await db.MediaAssets.CountAsync();beforeRevisions=await db.Revisions.CountAsync();
                var collector=new SourceInventory(db,config,state);await collector.InitializeAsync(root);collector.Seed();
                await collector.SeedLegacyAsync(legacy,StorageOptions.FromConfiguration(config));collector.Add(first.Source.CanonicalUrl,first.Source.SourceUrl);
                await collector.RunAsync(new(){Offline=true,Requests=0,Pages=2000,Contents=10000,Assets=10000,Seconds=600});
                Assert(state.Items.Count==37&&state.RequestsReserved==0,"Offline stage lost an identity or accessed network.");
                Assert(state.Captures.All(x=>state.Urls.Any(u=>u.Url==x.Url)),"A failed captured URL vanished from the inventory denominator.");
                Assert(state.Items.Single(x=>x.Source.Kind==first.Source.Kind&&x.Source.SourceId==first.Source.SourceId).Aliases.Contains(first.Source.CanonicalUrl),"Alias not preserved.");
            }
            await using(var db=new PortalDbContext(options))
            {
                var loaded=await SourceInventory.LoadAsync(db,root,state.Id);var collector=new SourceInventory(db,config,loaded);await collector.InitializeAsync(root);
                await collector.RunAsync(new(){Offline=true,Requests=0,Pages=2000,Contents=10000,Assets=10000,Seconds=600});
                Assert(loaded.Items.Count==37&&loaded.RequestsReserved==0,"Resume duplicated content or downloaded cached bytes.");
                Assert(await db.Contents.CountAsync()==beforeContents&&await db.MediaAssets.CountAsync()==beforeAssets&&await db.Revisions.CountAsync()==beforeRevisions,"Stage altered CMS business rows.");
                Assert(await db.CrawlItems.Where(x=>x.CrawlRunId==state.Id).GroupBy(x=>x.SourceUrl).AnyAsync(x=>x.Count()>1)==false,"SQL crawl URL duplicate.");
                Assert(await db.SourceSnapshots.Where(x=>x.CrawlRunId==state.Id).GroupBy(x=>new{x.SourceUrl,x.Sha256}).AnyAsync(x=>x.Count()>1)==false,"SQL snapshot dedup failed.");
                var folder=TestEvidencePaths.At(root,"inventory-offline");await collector.WriteReportAsync(folder);
            }
        });
        yield return ("3A SQL fixture transport 429 backoff new-context retry conditional304 exact cached bytes and cumulative budget",async()=>
        {
            var state=new InventoryState{RobotsState="Valid",RobotsText="User-agent: *\nAllow: /"};var pdf=(await Legacy(config)).Assets.First(x=>x.MimeType=="application/pdf");var bytes=await File.ReadAllBytesAsync(StorageOptions.FromConfiguration(config).ResolveMedia(pdf.StorageKey));int calls=0;
            Task<SourceResponse> Fixture(string url,int max,CancellationToken ct,bool page,string? etag,string? modified)
            {
                calls++;return Task.FromResult(calls==1?new SourceResponse(url,429,"text/plain",[],DateTimeOffset.UtcNow){RetryAfterUtc=DateTimeOffset.UtcNow.AddMinutes(20)}:calls==2?new SourceResponse(url,200,"application/pdf",bytes,DateTimeOffset.UtcNow){ETag="\"one\""}:etag=="\"one\""?new SourceResponse(url,304,"application/pdf",[],DateTimeOffset.UtcNow):throw new InvalidOperationException("Conditional validator not sent."));
            }
            var limits=new InventoryLimits{Requests=1,Pages=1,Contents=0,Assets=1,Seconds=20};
            await using(var db=new PortalDbContext(options))
            {
                var collector=new SourceInventory(db,config,state,Fixture);await collector.InitializeAsync(root);collector.Seed();foreach(var n in state.Urls)n.Status="Captured";
                var file=collector.Add(SourceCatalog.BaseUrl+"/technical-fixture.pdf",SourceCatalog.BaseUrl+"/")!;
                // Persist fixture setup before starting the transport's finite 20-second window.
                // SQL command timeout is unchanged; initial multi-row INSERT compilation is not an HTTP backoff test.
                await collector.SaveAsync();
                await collector.RunAsync(limits);Assert(file.Status=="Retry"&&file.AvailableUtc>DateTimeOffset.UtcNow.AddMinutes(19)&&calls==1,"429 ignored backoff.");
                await collector.RunAsync(limits);Assert(calls==1,"Retry ran blindly before available time.");
            }
            await using(var db=new PortalDbContext(options))
            {
                var loaded=await SourceInventory.LoadAsync(db,root,state.Id);var file=loaded.Urls.Single(x=>x.Role=="File");file.AvailableUtc=DateTimeOffset.MinValue;
                // Initialize from the committed state before advancing a synthetic Test-only clock boundary.
                var original=await SourceInventory.LoadAsync(db,root,state.Id);var collector=new SourceInventory(db,config,original,Fixture);await collector.InitializeAsync(root);file=original.Urls.Single(x=>x.Role=="File");file.AvailableUtc=DateTimeOffset.MinValue;
                await collector.RunAsync(limits);Assert(file.Status=="PrivateDownloaded"&&file.ScanStatus=="NotScanned"&&file.CaptureSha==SourceInventory.Hash(bytes),"Retry did not keep genuine bytes private/unscanned.");
                file.Refresh=true;file.Status="Discovered";file.Attempts=0;await collector.RunAsync(limits);Assert(calls==3&&file.CaptureSha==SourceInventory.Hash(bytes)&&original.Captures.Count(x=>x.Status==304)==1,"304 lost cached exact bytes.");
                original.RequestsReserved=40;file.Refresh=true;file.Status="Discovered";file.Attempts=0;await collector.RunAsync(limits);Assert(calls==3&&original.Status=="PausedByBudget","Restart bypassed cumulative unapproved session bound.");
                await collector.WriteReportAsync(TestEvidencePaths.At(root,"inventory-fixture-retry"));
            }
        });
        yield return ("3A SQL private corrupt file rejection checkpoint tamper and stale writer no overwrite",async()=>
        {
            var state=new InventoryState();var original=StorageOptions.FromConfiguration(config);var bytes=Encoding.UTF8.GetBytes("<html>not a PDF</html>");
            var temp=StorageOptions.ResolveWithin(original.TempRoot,"inventory-bad-"+Guid.NewGuid()+".bin");await File.WriteAllBytesAsync(temp,bytes);
            var storage=new StorageOptions{MediaRoot=original.TempRoot,SnapshotRoot=original.TempRoot,TempRoot=original.TempRoot,KeysRoot=original.KeysRoot};
            var legacy=new SampleManifest{Assets=[new(Guid.NewGuid(),SourceCatalog.BaseUrl+"/fixture-corrupt.pdf",Path.GetFileName(temp),SourceInventory.Hash(bytes),bytes.Length,"application/pdf","fixture.pdf")]};
            await using var db=new PortalDbContext(options);var c=new SourceInventory(db,config,state);await c.InitializeAsync(root);c.Seed();await c.SeedLegacyAsync(legacy,storage);await c.RunAsync(new(){Offline=true,Requests=0,Pages=20,Contents=0,Assets=10});
            Assert(state.Urls.Single(x=>x.Role=="File").Status=="ValidationFailed"&&!state.Items.Any(),"HTML as PDF accepted.");
            var stale=await SourceInventory.LoadAsync(db,root,state.Id);await c.RunAsync(new(){Offline=true,Requests=0});
            await using var other=new PortalDbContext(options);await Reject<InvalidOperationException>(()=>new SourceInventory(other,config,stale).InitializeAsync(root));
            var row=await db.CrawlRuns.AsNoTracking().SingleAsync(x=>x.Id==state.Id);var hash=row.Checkpoint[SourceInventory.CheckpointPrefix.Length..];var path=Path.Combine(state.Root,"checkpoints",hash+".bin");await File.AppendAllTextAsync(path,"tamper-technical-fixture");
            await Reject<InvalidDataException>(()=>SourceInventory.LoadAsync(db,root,state.Id));
        });
        yield return ("3A target scope and persisted cutover guards block collector before source GET without changing Dev rights",async()=>
        {
            foreach(var database in new[]{"LandPortal_Dev","master","Foreign"})await Reject<InvalidOperationException>(()=>Task.Run(()=>SourceInventory.RequireTest(new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{{"Sql:ExpectedDatabase",database}}).Build())));
            await Reject<InvalidOperationException>(()=>Task.Run(()=>SourceInventory.RequireTest(new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{{"DOTNET_ENVIRONMENT","Production"}}).Build())));
            await using var db=new PortalDbContext(options);var state=new InventoryState();var c=new SourceInventory(db,config,state);await c.InitializeAsync(root);
            var flag=await db.SiteSettings.FindAsync("CutoverUtc");var prior=flag?.Value;
            if(flag is null){flag=new(){Key="CutoverUtc"};db.SiteSettings.Add(flag);}flag.Value="technical-Test-cutover";await db.SaveChangesAsync();
            try{await Reject<InvalidOperationException>(()=>c.RunAsync(new(){Requests=1}));Assert(state.RequestsReserved==0,"Cutover still attempted a GET.");}
            finally{db.ChangeTracker.Clear();flag=await db.SiteSettings.FindAsync("CutoverUtc");if(prior is null)db.SiteSettings.Remove(flag!);else flag!.Value=prior;await db.SaveChangesAsync();}
            var mid=new InventoryState{RobotsState="Valid",RobotsText="User-agent: *\nAllow: /"};var calls=0;
            var publicPdf=(await Legacy(config)).Assets.First(x=>x.MimeType=="application/pdf");var pdf=await File.ReadAllBytesAsync(StorageOptions.FromConfiguration(config).ResolveMedia(publicPdf.StorageKey));
            async Task<SourceResponse> StopAfterFirst(string url,int max,CancellationToken ct,bool page,string? tag,string? last)
            {
                calls++;await using var update=new PortalDbContext(options);var value=await update.SiteSettings.FindAsync("CutoverUtc");if(value is null){value=new(){Key="CutoverUtc"};update.SiteSettings.Add(value);}value.Value="technical-mid-run-cutover";await update.SaveChangesAsync();return new(url,200,"application/pdf",pdf,DateTimeOffset.UtcNow);
            }
            await using var middleDb=new PortalDbContext(options);var middle=new SourceInventory(middleDb,config,mid,StopAfterFirst);await middle.InitializeAsync(root);middle.Seed();foreach(var n in mid.Urls)n.Status="Captured";middle.Add(SourceCatalog.BaseUrl+"/fixture-first.pdf",SourceCatalog.BaseUrl+"/");middle.Add(SourceCatalog.BaseUrl+"/fixture-second.pdf",SourceCatalog.BaseUrl+"/");
            try{await middle.RunAsync(new(){Requests=2,Assets=2,Contents=0,Pages=1});Assert(calls==1&&mid.RequestsReserved==1&&mid.Status=="PausedByPolicy","Cutover during collection did not fence the next GET.");}
            finally{await using var restore=new PortalDbContext(options);var saved=await restore.SiteSettings.FindAsync("CutoverUtc");if(prior is null)restore.SiteSettings.Remove(saved!);else saved!.Value=prior;await restore.SaveChangesAsync();}
        });
    }
}
