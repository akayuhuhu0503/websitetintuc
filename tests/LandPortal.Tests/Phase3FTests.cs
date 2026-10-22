using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;
public static class Phase3FTests
{
    static void Check(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    static async Task Denied<T>(Func<Task> action)where T:Exception{try{await action();}catch(T){return;}throw new InvalidOperationException("Unsafe input accepted.");}
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(string root,DbContextOptions<PortalDbContext> options,IConfiguration config)
    {
        yield return("3F actual real-plan canonical conflicts block both absent types before publication without title merging",async()=>{
            var t=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,".local/phase3f-current.json"))).RootElement;
            var plan=JsonSerializer.Deserialize<SourceBatchPlan>(await File.ReadAllTextAsync(Path.Combine(root,t.GetProperty("root").GetString()!,"test-preview/plan.json")))!;
            var before=plan.Rows.Count(x=>x.Action=="Blocked");BatchAliasPolicy.BlockConflicts(plan.Rows);
            Check(plan.Rows.Count(x=>x.Action=="Blocked")==before+6,"Expected three actual canonical collisions, not guessed title duplicates.");
            foreach(var id in new[]{"3589","3607","3684"})Check(plan.Rows.Where(x=>x.SourceId==id).All(x=>x.Action=="Blocked"&&x.Validation.Any(v=>v.StartsWith("INTRA_BATCH_ALIAS_COLLISION:"))),"A competing type could publish first.");
            var independent=new[]{new SourceBatchRow{SourceId="same-title-1",Action="CreatePublishedTest",Aliases=["https://qldd.mae.gov.vn/a-1.htm"]},new SourceBatchRow{SourceId="same-title-2",Action="CreatePublishedTest",Aliases=["https://qldd.mae.gov.vn/a-2.htm"]}};
            BatchAliasPolicy.BlockConflicts(independent);Check(independent.All(x=>x.Action=="CreatePublishedTest"),"Unrelated source identities were merged or blocked.");
        });
        yield return("3F real 1716 and 3668 canonical news override stale draft hint; empty source remains denied",async()=>{
            var t=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,".local/phase3f-current.json"))).RootElement;
            using var reference=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,t.GetProperty("root").GetString()!,"after-collect-012-resume.json")));
            var state=await SourceBatch.ReadCheckpointAsync(root,reference.RootElement.GetProperty("path").GetString()!,reference.RootElement.GetProperty("sha256").GetString()!);
            var results=new List<object>();
            foreach(var id in new[]{"1716","3668"}){
                var node=state.Urls.First(n=>n.CandidateId==id&&n.Kind==ContentKind.DraftDocument&&state.Captures.Any(c=>c.Url==n.Url&&c.Status==200));
                var cap=state.Captures.Last(c=>c.Url==node.Url&&c.Status==200);var bytes=await File.ReadAllBytesAsync(Path.Combine(root,".local/phase3a",state.Id.ToString("N"),cap.Key));
                Check(SourceInventory.Hash(bytes)==cap.Sha256,"Actual source snapshot changed.");
                var parsed=SourceParser.Parse(System.Text.Encoding.UTF8.GetString(bytes),node.Url,node.Kind,id);
                Check(parsed.Kind==ContentKind.Article&&parsed.SourceId==id&&parsed.Title.Length>20&&SourceParser.Document(parsed.BodyHtml).Body!.TextContent.Length>20,"Parser guessed or lost verified news.");
                results.Add(new{id,cap.Sha256,parsed.Kind,parsed.Title});
            }
            foreach(var id in new[]{"2024","3"}){
                var node=state.Urls.First(n=>n.CandidateId==id&&n.Kind==ContentKind.DraftDocument&&state.Captures.Any(c=>c.Url==n.Url&&c.Status==200));var cap=state.Captures.Last(c=>c.Url==node.Url&&c.Status==200);
                var text=await File.ReadAllTextAsync(Path.Combine(root,".local/phase3a",state.Id.ToString("N"),cap.Key));await Denied<InvalidDataException>(()=>Task.Run(()=>SourceParser.Parse(text,node.Url,node.Kind,id)));
            }
            await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root),"canonical-news-recovery.json"),results);
        });
        yield return("3F real offline checkpoint 228 unique detail work orders and scope guards",async()=>{
            const string cp=".local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3.bin";
            const string sha="d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3";
            var state=await SourceBatch.ReadCheckpointAsync(root,cp,sha);var coverage=await CoverageAudit.InspectAsync(root,state);
            var batches=DetailBatchPlanner.Create(state,coverage,cp,sha);Check(batches.Sum(x=>x.Urls.Length)==228&&batches.SelectMany(x=>x.SourceIdentities).Distinct().Count()==228,"Missing/duplicate actual candidates.");
            foreach(var b in batches)DetailBatchPlanner.Validate(b,state,state);
            Check(batches[0].SourceIdentities.Any(id=>coverage.Candidates.Single(x=>x.SourceIdentity==id).Categories.Contains(76))&&batches[0].SourceIdentities.Any(id=>coverage.Candidates.Single(x=>x.SourceIdentity==id).Categories.Contains(77)),"Uncovered categories were not prioritized.");
            var batch=batches[0];Check(!DetailBatchPlanner.Includes(batch,new(){Role="List",Url=SourceCatalog.BaseUrl+"/"}),"Work order fetched unrelated lists.");
            Check(DetailBatchPlanner.Includes(batch,new(){Role="File",FoundOn=[batch.Urls[0]]})&&!DetailBatchPlanner.Includes(batch,new(){Role="File",FoundOn=["https://unrelated/"]}),"File relation scope failed.");
            await Denied<InvalidDataException>(()=>Task.Run(()=>DetailBatchPlanner.Validate(batch with{InventoryId=Guid.NewGuid()},state,state)));
            await Denied<InvalidDataException>(()=>Task.Run(()=>DetailBatchPlanner.Validate(batch with{Urls=["https://foreign/"],SourceIdentities=[batch.SourceIdentities[0]]},state,state)));
            await Denied<InvalidOperationException>(()=>Task.Run(()=>new InventoryLimits{DetailBatchFile="unapproved",DetailBatchSha256=sha}.Validate()));
            await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root),"detail-work-orders.json"),batches);
        });
        yield return("3F private NukeViet technical snapshot asset hash mapping quarantine and tamper gates; not real migration",async()=>{
            var folder=TestEvidencePaths.At(root,"nukeviet-fixture");Directory.CreateDirectory(folder);
            const string url="https://gdla.gov.vn/vi/news/technical-test/sample-456.html";
            const string html="<link rel='canonical' href='https://gdla.gov.vn/vi/news/technical-test/sample-456.html'><h1>TECHNICAL FIXTURE</h1><div id='news-bodyhtml'><p>Technical test only.</p><a href='/uploads/test.pdf'>fixture</a></div>";
            var path=Path.Combine(folder,"input.html");await File.WriteAllTextAsync(path,html);
            // Reuse real public PDF bytes, never mark fixture bytes Clean or call them an official export.
            var legacy=JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(Path.Combine(StorageOptions.FromConfiguration(config).SnapshotRoot,"sample-manifest.json")),SampleCollector.JsonOptions)!;
            var pdf=legacy.Assets.First(x=>x.MimeType=="application/pdf");var file=StorageOptions.FromConfiguration(config).ResolveMedia(pdf.StorageKey);
            var snapshot=new NukeVietSnapshot(url,path,SourceInventory.Hash(await File.ReadAllBytesAsync(path)),[new("https://gdla.gov.vn/uploads/test.pdf",file,pdf.Sha256)]);
            var input=new NukeVietInput("TechnicalFixture","3F technical fixture, not official export",[snapshot]);
            var output=Path.Combine(folder,"prepared");var report=await NukeVietBundle.PrepareAsync(root,input,output,new());
            var json=JsonSerializer.Serialize(report);Check(json.Contains("NotScanned")&&json.Contains("NEEDS_BUSINESS_APPROVAL")&&json.Contains("BLOCKED_MAPPING_SCANNER")&&json.Contains("TechnicalFixture"),"Preparation bypassed safety gates.");
            Check(SourceInventory.Hash(await File.ReadAllBytesAsync(Path.Combine(output,"quarantine",pdf.Sha256+".bin")))==pdf.Sha256,"Private bytes differ.");
            await MediaRecovery.WriteNewAsync(Path.Combine(output,"checkpoint.json"),report);
            await Denied<InvalidDataException>(()=>NukeVietBundle.PrepareAsync(root,input with{Snapshots=[snapshot with{Sha256=new string('0',64)}]},Path.Combine(folder,"bad"),new()));
        });
        foreach(var test in InventoryTests.Cases(root,options,config).Where(x=>x.Name.StartsWith("3A unit")||x.Name.StartsWith("3A SQL fixture transport")))yield return test;
        yield return("3F actual oversized source PNG private replay rejects resource without GET or CMS writes",async()=>{
            var current=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,".local/phase3f-current.json"))).RootElement.GetProperty("root").GetString()!;
            using var reference=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,current,"batch009-failure.json")));
            var source=await SourceBatch.ReadCheckpointAsync(root,reference.RootElement.GetProperty("path").GetString()!,reference.RootElement.GetProperty("sha256").GetString()!);
            const string sha="8d3197a85aa5ca0dcc631e8f84af5ff3b83f81260cc1a0af1a984b6b462538d2";
            var capture=source.Captures.Last(x=>x.Sha256==sha);var rawRoot=Path.Combine(root,".local/phase3a",source.Id.ToString("N"));
            var legacy=new SampleManifest{Assets=[new(Guid.NewGuid(),capture.Url,capture.Key,sha,capture.Bytes,"image/png","REAL source oversized PNG - technical replay")]};
            var state=new InventoryState();await using var db=new PortalDbContext(options);var before=await db.Contents.CountAsync();
            var collector=new SourceInventory(db,config,state);await collector.InitializeAsync(root);
            await collector.SeedLegacyAsync(legacy,new(){MediaRoot=rawRoot,SnapshotRoot=rawRoot,TempRoot=rawRoot,KeysRoot=rawRoot});
            var node=state.Urls.Single();node.Status="Fetching";node.CaptureSha=sha;await collector.SaveAsync();
            await collector.RunAsync(new(){Offline=true,Requests=0,Pages=1,Contents=0,Assets=1,Seconds=60});
            Check(node.Status=="ValidationFailed"&&node.FormatStatus=="Rejected"&&node.ScanStatus=="NotScanned"&&state.RequestsReserved==0,"Unsafe image was promoted or re-fetched.");
            Check(await db.Contents.CountAsync()==before&&state.Issues.Any(x=>x.Detail.Contains("without another GET")),"Replay changed CMS or missed durable captured response.");
            await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root),"oversized-source-replay.json"),new{sha,node.Status,node.FormatStatus,node.ScanStatus,state.RequestsReserved,CmsDelta=0});
        });
        foreach(var test in BatchImportTests.Cases(root,options,config).Where(x=>x.Name.StartsWith("3B SQL fault")||x.Name.StartsWith("3B real-photo negative")))yield return test;
    }
}
