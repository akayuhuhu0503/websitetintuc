using System.Text;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;

public static class BatchImportTests
{
    static void Check(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    static IConfiguration Config(IConfiguration c,string root)=>new ConfigurationBuilder().AddConfiguration(c).AddInMemoryCollection(new Dictionary<string,string?>{{"DOTNET_ENVIRONMENT","Development"},{"WorkspaceRoot",root}}).Build();
    internal static async Task<(InventoryState State,string Hash)> Fixture(string root,int count=1,string version="v1",InventoryState? previous=null)
    {
        var state=new InventoryState{Id=previous?.Id??Guid.NewGuid()};
        for(var i=0;i<count;i++){
            var id=previous?.Items[i].Source.SourceId??("8"+Random.Shared.NextInt64(100000000,999999999));
            var url=SourceCatalog.BaseUrl+"/phase3b-technical-test-"+id+".htm";
            var html=$"<html><head><link rel='canonical' href='{SourceCatalog.BaseUrl}/Pages/chi-tiet-tin-tuc.aspx?ItemID={id}'></head><body><h1 class='tieu-de-tin-tuc'>TECHNICAL TEST Phase3B {id}</h1><span class='thoi-gian-tao-chi-tiet-tin'>18/10/2026</span><article class='noi-dung-chi-tiet-tin'><p>Kiểm thử kỹ thuật riêng, không phải dữ liệu nguồn. {version}</p></article></body></html>";
            var bytes=Encoding.UTF8.GetBytes(html);var sha=SourceInventory.Hash(bytes);var key="responses/"+sha+".bin";var folder=Path.Combine(root,".local","phase3a",state.Id.ToString("N"));Directory.CreateDirectory(Path.Combine(folder,"responses"));
            if(!File.Exists(Path.Combine(folder,key)))await File.WriteAllBytesAsync(Path.Combine(folder,key),bytes);
            var source=SourceParser.Parse(html,url,ContentKind.Article,id);source.CategoryIds=[75];source.PrimaryCategoryId=75;
            state.Items.Add(new(){Source=source,RawSha256=sha,Aliases=[url,source.CanonicalUrl]});state.Urls.Add(new(){Url=url,Role="Detail",Kind=source.Kind,CandidateId=id});state.Captures.Add(new(url,url,200,"text/html",key,sha,bytes.Length,DateTimeOffset.UtcNow,"TechnicalFixture"));
        }
        return(state,SourceInventory.HashText(JsonSerializer.Serialize(state,InventoryLimits.Json)));
    }
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(string root,DbContextOptions<PortalDbContext> options,IConfiguration source)
    {
        var config=Config(source,root);var storage=StorageOptions.FromConfiguration(config);
        yield return("3B sealed real40 preview read-only source overlap provenance and byte validation without live crawl",async()=>{
            const string hash="d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3";
            var state=await SourceBatch.ReadCheckpointAsync(root,Path.Combine(root,".local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/"+hash+".bin"),hash);
            await using var db=new PortalDbContext(options);var before=JsonSerializer.Serialize(await new SqlImporter(db,storage).CountsAsync());
            var plan=await new SourceBatch(db,storage,config,root,state,hash).PreviewAsync();
            Check(plan.Rows.Count==40&&plan.Rows.Select(x=>(x.Kind,x.SourceId)).Distinct().Count()==40,"Wrong source denominator.");
            Check(plan.Rows.Count(x=>x.SourceId is "3673" or "3670" or "3666")==3,"Three new baseline identities lost.");
            Check(before==JsonSerializer.Serialize(await new SqlImporter(db,storage).CountsAsync()),"Preview wrote CMS.");
            var folder=TestEvidencePaths.At(root,"batch-real-preview");Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,"preview.json"),JsonSerializer.Serialize(plan,new JsonSerializerOptions{WriteIndented=true}));
        });
        yield return("3B SQL technical fixture per-item transaction interruption fresh-context resume exact-once revisions assets audit and rollback",async()=>{
            var f=await Fixture(root,2);SourceBatchPlan plan;var hash="";
            await using(var db=new PortalDbContext(options)){var svc=new SourceBatch(db,storage,config,root,f.State,f.Hash);plan=await svc.PreviewAsync();hash=SourceInventory.HashText(JsonSerializer.Serialize(plan));Check(plan.Rows.All(x=>x.Action=="CreatePublishedTest"),"Fixture preview blocked.");var partial=await svc.CommitAsync(plan,hash,1);Check(partial.Count==1&&partial[0].Status=="CreatePublishedTest","Interruption checkpoint failed.");}
            await using(var db=new PortalDbContext(options)){var svc=new SourceBatch(db,storage,config,root,f.State,f.Hash);var result=await svc.CommitAsync(plan,hash);Check(result.Count==2&&result[0].Status=="AlreadyCommitted"&&result[1].Status=="CreatePublishedTest","Resume duplicate or missing item.");Check((await svc.VerifyAsync(plan,hash)).Count==0,"Verify failed.");var counts=JsonSerializer.Serialize(await new SqlImporter(db,storage).CountsAsync());Check((await svc.CommitAsync(plan,hash)).All(x=>x.Status=="AlreadyCommitted"),"Repeat not idempotent.");Check(counts==JsonSerializer.Serialize(await new SqlImporter(db,storage).CountsAsync()),"Repeat created records.");Check(await svc.RollbackPublicationAsync(plan,hash)==2&&await svc.RollbackPublicationAsync(plan,hash)==0,"Rollback not safe/idempotent.");}
        });
        yield return("3B SQL fault before commit rolls back source content revision alias and audit; new context retries",async()=>{
            var f=await Fixture(root);SourceBatchPlan plan;string hash;
            await using(var db=new PortalDbContext(options)){var svc=new SourceBatch(db,storage,config,root,f.State,f.Hash);plan=await svc.PreviewAsync();hash=SourceInventory.HashText(JsonSerializer.Serialize(plan));var before=JsonSerializer.Serialize(await new SqlImporter(db,storage).CountsAsync());var failed=await svc.CommitAsync(plan,hash,beforeCommit:_=>throw new IOException("Injected technical precommit interruption"));Check(failed.Single().Status=="Failed"&&before==JsonSerializer.Serialize(await new SqlImporter(db,storage).CountsAsync()),"Partial writes survived rollback.");}
            await using(var db=new PortalDbContext(options)){var svc=new SourceBatch(db,storage,config,root,f.State,f.Hash);Check((await svc.CommitAsync(plan,hash)).Single().Status=="CreatePublishedTest","Retry failed.");await svc.RollbackPublicationAsync(plan,hash);}
        });
        yield return("3B SQL changed source preserves editorial live/current approval and taxonomy; stale preview rejected",async()=>{
            var f=await Fixture(root);Guid id,live;SourceBatchPlan first;string firstHash;
            await using(var db=new PortalDbContext(options)){var svc=new SourceBatch(db,storage,config,root,f.State,f.Hash);first=await svc.PreviewAsync();firstHash=SourceInventory.HashText(JsonSerializer.Serialize(first));var outcome=(await svc.CommitAsync(first,firstHash)).Single();id=outcome.ContentId!.Value;live=outcome.RevisionId!.Value;var c=await db.Contents.SingleAsync(x=>x.Id==id);c.HasEditorialChanges=true;await db.SaveChangesAsync();}
            var changed=await Fixture(root,version:"v2 changed source",previous:f.State);
            await using(var db=new PortalDbContext(options)){var svc=new SourceBatch(db,storage,config,root,changed.State,changed.Hash);var plan=await svc.PreviewAsync();Check(plan.Rows.Single().Action=="SourceComparisonOnly","Changed source not classified.");var before=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==id);var outc=(await svc.CommitAsync(plan,SourceInventory.HashText(JsonSerializer.Serialize(plan)))).Single();Check(outc.Status=="SourceComparisonOnly"&&outc.RevisionId!=live,"No comparison revision.");var after=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==id);Check(after.PublishedRevisionId==live&&after.CurrentRevisionId==live&&after.HasEditorialChanges&&after.ApprovedRevisionId==before.ApprovedRevisionId,"Editor/public overwritten.");
                var stale=await svc.PreviewAsync();db.ChangeTracker.Clear();var row=await db.Contents.SingleAsync(x=>x.Id==id);row.EditGeneration++;await db.SaveChangesAsync();var conflict=await svc.CommitAsync(stale,SourceInventory.HashText(JsonSerializer.Serialize(stale)));Check(conflict.Single().Status=="NeedsReconciliation"&&conflict.Single().Error!.Contains("Uncommitted source changed since preview"),"Stale preview must stop for reconciliation before writes.");row.IsPublic=false;row.PublishedRevisionId=null;row.State=WorkflowState.Unpublished;await db.SaveChangesAsync();}
        });
        yield return("3B SQL alias collision distinct identities and competing commits reject overwrite with one materialization",async()=>{
            var f=await Fixture(root);SourceBatchPlan plan;
            await using(var db=new PortalDbContext(options))plan=await new SourceBatch(db,storage,config,root,f.State,f.Hash).PreviewAsync();var hash=SourceInventory.HashText(JsonSerializer.Serialize(plan));
            async Task<List<SourceBatchOutcome>> Commit(){await using var db=new PortalDbContext(options);return await new SourceBatch(db,storage,config,root,f.State,f.Hash).CommitAsync(plan,hash);}
            var both=await Task.WhenAll(Commit(),Commit());Check(both.SelectMany(x=>x).Count(x=>x.Status=="CreatePublishedTest")==1&&both.SelectMany(x=>x).Count(x=>x.Status=="AlreadyCommitted")==1,"Concurrent duplicate.");
            var other=await Fixture(root);other.State.Items[0].Aliases.Add(f.State.Items[0].Source.SourceUrl);
            await using(var db=new PortalDbContext(options)){var svc=new SourceBatch(db,storage,config,root,other.State,other.Hash);var collision=await svc.PreviewAsync();Check(collision.Rows.Single().Action=="Blocked"&&collision.Rows.Single().Validation.Any(x=>x.StartsWith("ALIAS_COLLISION")),"Alias overwrite accepted.");await new SourceBatch(db,storage,config,root,f.State,f.Hash).RollbackPublicationAsync(plan,hash);}
        });
        yield return("3B real-photo negative NotScanned Infected missing proof and changed bytes denied; Dev apply and Production denied",async()=>{
            const string h="d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3";var path=Path.Combine(root,".local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/"+h+".bin");
            var state=await SourceBatch.ReadCheckpointAsync(root,path,h);var item=state.Items.First(x=>x.Source.SourceId=="3673");var node=state.Urls.Single(x=>x.Url==item.Source.AssetUrls[0]);
            await using var db=new PortalDbContext(options);var svc=new SourceBatch(db,storage,config,root,state,h);node.ScanStatus="NotScanned";Check((await svc.PrepareAsync(item)).Errors.Any(x=>x.StartsWith("SCAN_NOT_CLEAN")),"NotScanned accepted.");node.ScanStatus="Infected";Check((await svc.PrepareAsync(item)).Errors.Any(x=>x.StartsWith("SCAN_NOT_CLEAN")),"Infected accepted.");
            node.ScanStatus="Clean";node.ScannedSha256=new string('0',64);Check((await svc.PrepareAsync(item)).Errors.Any(x=>x.StartsWith("SCAN_NOT_CLEAN")),"Changed scan proof accepted.");
            var c=state.Captures.Last(x=>x.Url==node.Url);var index=state.Captures.IndexOf(c);state.Captures[index]=c with{Key="quarantine/missing-technical-fixture.bin"};try{await svc.PrepareAsync(item);throw new Exception("Missing file accepted");}catch(IOException){}
            state.Captures[index]=c with{Bytes=c.Bytes+1};try{await svc.PrepareAsync(item);throw new Exception("Wrong bytes accepted");}catch(InvalidDataException){}
            state.Captures[index]=c;
            var denied=new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{{"DOTNET_ENVIRONMENT","Production"}}).Build();try{await new SourceBatch(db,storage,denied,root,state,h).PreviewAsync();throw new Exception("Production allowed");}catch(InvalidOperationException){}
            var dev=new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{{"Sql:ExpectedDatabase","LandPortal_Dev"}}).Build();try{await new SourceBatch(db,storage,dev,root,state,h).CommitAsync(new(),h);throw new Exception("Dev write allowed");}catch(InvalidOperationException){}
        });
        yield return("3B genuine materialized media remains editable in 2A; Test-only Clean exact bytes tamper and Production gates",async()=>{
            await using var db=new PortalDbContext(options);var asset=await db.MediaAssets.AsNoTracking().FirstAsync(x=>x.ValidationStatus==BatchMediaProof.Validation&&x.ScanStatus=="Clean");
            var local=MediaUploadOptions.FromConfiguration(config,"Development");Check(await BatchMediaProof.ValidAsync(storage,asset,true),"Real imported media proof unavailable.");
            Check(!MediaUploadOptions.FromConfiguration(config,"Production").CanServe(asset)&&!await BatchMediaProof.ValidAsync(storage,asset,false),"Test proof authorized Production.");
            var workflow=new WorkflowService(db,new(),local,storage);var actor=Phase2aSqlTests.Actor();var input=Phase2aSqlTests.Input("TECHNICAL TEST batch media edit "+Guid.NewGuid());input.CoverAssetId=asset.Id;input.Assets=[new(){MediaAssetId=asset.Id,Usage="Cover"}];
            var saved=await workflow.SaveAsync(null,input,actor,null,Guid.NewGuid());var published=await workflow.PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());Check(published.IsPublic,"Imported image cannot pass existing editor workflow.");
            await workflow.UnpublishAsync(saved.ContentId,actor,published.RowVersion,Guid.NewGuid());
            var path=MediaService.ResolveMediaPath(storage,asset.StorageKey);var bytes=await File.ReadAllBytesAsync(path);
            try{var changed=bytes.ToArray();changed[^1]^=1;await File.WriteAllBytesAsync(path,changed);Check(!await BatchMediaProof.ValidAsync(storage,asset,true),"Stale Clean accepted changed bytes.");try{await workflow.SaveAsync(null,input,actor,null,Guid.NewGuid());throw new Exception("Editor accepted tampered batch bytes");}catch(InvalidOperationException){}}
            finally{await File.WriteAllBytesAsync(path,bytes);}
            Check(await BatchMediaProof.ValidAsync(storage,asset,true),"Test media exact restoration failed; scan state was never relabeled.");
        });
    }
}
