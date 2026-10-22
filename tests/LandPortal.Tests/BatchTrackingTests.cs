using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;

// Synthetic, asset-free SQL Test records only. These tests do not prove source migration or scanning.
public static class BatchTrackingTests
{
    static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static string Hash(SourceBatchPlan plan)=>SourceInventory.HashText(JsonSerializer.Serialize(plan));
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(string root,DbContextOptions<PortalDbContext> options,IConfiguration config)
    {
        var storage=StorageOptions.FromConfiguration(config);
        yield return("3G tracking SQL missing run checkpoint binding interruption/resume/repeat keep one tracking row",async()=>{
            var f=await BatchImportTests.Fixture(root,2);SourceBatchPlan plan;string hash;DateTimeOffset started;
            await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root),"new-tracking-input.json"),new{FixtureOnly=true,InventoryId=f.State.Id,CheckpointSha=f.Hash,SourceIds=f.State.Items.Select(x=>x.Source.SourceId)});
            await using(var db=new PortalDbContext(options)){
                var service=new SourceBatch(db,storage,config,root,f.State,f.Hash);
                plan=await service.PreviewAsync();hash=Hash(plan);
                Check(!await db.CrawlRuns.AnyAsync(x=>x.Id==f.State.Id),"Preview wrote tracking.");
                var result=await service.CommitAsync(plan,hash,1);
                Check(result.Count==1&&result[0].Status=="CreatePublishedTest","First commit failed.");
                var run=await db.CrawlRuns.AsNoTracking().SingleAsync(x=>x.Id==f.State.Id);
                Check(run.Status=="Phase3BCheckpointReplay"&&run.Checkpoint.Contains(f.Hash)&&run.Checkpoint.Contains(f.State.Id.ToString()),"Missing exact replay checkpoint provenance.");
                Check(!run.Checkpoint.Contains("Fixture-only")&&!run.Status.Contains("TechnicalFixture")&&run.RequestsUsed==0,"Replay mislabeled acquisition/fixture activity.");
                started=run.StartedUtc;
            }
            await using(var db=new PortalDbContext(options)){
                var service=new SourceBatch(db,storage,config,root,f.State,f.Hash);
                var resumed=await service.CommitAsync(plan,hash);
                Check(resumed.Count(x=>x.Status=="AlreadyCommitted")==1&&resumed.Count(x=>x.Status=="CreatePublishedTest")==1,"Resume duplicated or skipped content.");
                Check((await service.VerifyAsync(plan,hash)).Count==0,"Resume readback failed.");
                Check((await db.ImportRuns.AsNoTracking().SingleAsync(x=>x.Id==plan.BatchId)).CrawlRunId==f.State.Id,"Import lost inventory tracking.");
                foreach(var item in f.State.Items){
                    Check(await db.SourceRecords.AnyAsync(x=>x.Source==SourceBatch.Source&&x.Kind==item.Source.Kind&&x.SourceId==item.Source.SourceId),"Source identity/origin lost.");
                    Check(await db.AuditLogs.AnyAsync(x=>x.Action=="Phase3B.Import"&&x.Detail==plan.BatchId+"|"+hash+"|"+item.Source.Kind+":"+item.Source.SourceId),"Per-identity checkpoint-plan receipt lost.");
                }
                var counts=JsonSerializer.Serialize(await new SqlImporter(db,storage).CountsAsync());
                Check((await service.CommitAsync(plan,hash)).All(x=>x.Status=="AlreadyCommitted"),"Repeat duplicated batch.");
                Check(counts==JsonSerializer.Serialize(await new SqlImporter(db,storage).CountsAsync()),"Repeat changed content/revisions/assets.");
                Check(await db.CrawlRuns.CountAsync(x=>x.Id==f.State.Id)==1&&(await db.CrawlRuns.AsNoTracking().SingleAsync(x=>x.Id==f.State.Id)).StartedUtc==started,"Replay duplicated or reset tracking.");
                await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root),"new-tracking-readback.json"),new{FixtureOnly=true,InventoryId=f.State.Id,CheckpointSha=f.Hash,plan.BatchId,Run=await db.CrawlRuns.AsNoTracking().SingleAsync(x=>x.Id==f.State.Id),ActualSourceImported=0});
                Check(await service.RollbackPublicationAsync(plan,hash)==2,"Fixture publication withdrawal failed.");
            }
        });
        yield return("3G tracking SQL existing acquisition metadata retained across imports and comparison revision",async()=>{
            var f=await BatchImportTests.Fixture(root);string before;Guid published;
            await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root),"existing-tracking-input.json"),new{FixtureOnly=true,InventoryId=f.State.Id,CheckpointSha=f.Hash,SourceIds=f.State.Items.Select(x=>x.Source.SourceId)});
            await using(var db=new PortalDbContext(options)){
                db.CrawlRuns.Add(new(){Id=f.State.Id,Status="TechnicalFixtureExistingAcquisition",Checkpoint="Technical fixture provenance retained",RequestsUsed=2,RequestBudget=17,ContentBudget=1,StartedUtc=DateTimeOffset.UtcNow.AddDays(-2),EndedUtc=DateTimeOffset.UtcNow.AddDays(-1)});
                await db.SaveChangesAsync();before=JsonSerializer.Serialize(await db.CrawlRuns.AsNoTracking().SingleAsync(x=>x.Id==f.State.Id));
                var service=new SourceBatch(db,storage,config,root,f.State,f.Hash);var plan=await service.PreviewAsync();
                var result=(await service.CommitAsync(plan,Hash(plan))).Single();Check(result.Status=="CreatePublishedTest","Fixture import failed.");published=result.RevisionId!.Value;
            }
            var changed=await BatchImportTests.Fixture(root,version:"tracking comparison fixture v2",previous:f.State);
            await using(var db=new PortalDbContext(options)){
                var service=new SourceBatch(db,storage,config,root,changed.State,changed.Hash);var plan=await service.PreviewAsync();
                Check(plan.Rows.Single().Action=="SourceComparisonOnly","Changed source not comparison-only.");
                var result=(await service.CommitAsync(plan,Hash(plan))).Single();
                Check(result.Status=="SourceComparisonOnly"&&result.RevisionId!=published,"No comparison revision created.");
                var content=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==result.ContentId);
                Check(content.PublishedRevisionId==published,"Comparison altered existing publication.");
                Check(before==JsonSerializer.Serialize(await db.CrawlRuns.AsNoTracking().SingleAsync(x=>x.Id==f.State.Id)),"Existing acquisition tracking overwritten.");
                await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root),"existing-tracking-readback.json"),new{FixtureOnly=true,InventoryId=f.State.Id,Unchanged=true,ComparisonRevision=result.RevisionId,PublicRevision=published});
                // Withdraw only this newly created technical fixture, using its original batch receipt.
                db.ChangeTracker.Clear();var c=await db.Contents.SingleAsync(x=>x.Id==result.ContentId);c.IsPublic=false;c.PublishedRevisionId=null;c.State=WorkflowState.Unpublished;await db.SaveChangesAsync();
            }
        });
        yield return("3G tracking SQL precommit failure rolls back tracking and receipt; retry binds same checkpoint once",async()=>{
            var f=await BatchImportTests.Fixture(root);SourceBatchPlan plan;string hash;
            await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root),"fault-tracking-input.json"),new{FixtureOnly=true,InventoryId=f.State.Id,CheckpointSha=f.Hash,SourceIds=f.State.Items.Select(x=>x.Source.SourceId)});
            await using(var db=new PortalDbContext(options)){
                var service=new SourceBatch(db,storage,config,root,f.State,f.Hash);plan=await service.PreviewAsync();hash=Hash(plan);
                var result=await service.CommitAsync(plan,hash,beforeCommit:_=>throw new IOException("Technical Test precommit failure"));
                Check(result.Single().Status=="Failed","Failure not rolled back.");
                Check(!await db.CrawlRuns.AnyAsync(x=>x.Id==f.State.Id)&&!await db.ImportRuns.AnyAsync(x=>x.Id==plan.BatchId),"Tracking survived rollback.");
                Check(!await db.SourceRecords.AnyAsync(x=>x.SourceId==f.State.Items[0].Source.SourceId),"Source survived rollback.");
            }
            await using(var db=new PortalDbContext(options)){
                var service=new SourceBatch(db,storage,config,root,f.State,f.Hash);
                Check((await service.CommitAsync(plan,hash)).Single().Status=="CreatePublishedTest","Safe retry failed.");
                Check((await service.VerifyAsync(plan,hash)).Count==0&&await db.CrawlRuns.CountAsync(x=>x.Id==f.State.Id)==1,"Retry tracking/readback mismatch.");
                await service.RollbackPublicationAsync(plan,hash);
            }
        });
        foreach(var risk in BatchImportTests.Cases(root,options,config).Where(x=>x.Name.StartsWith("3B real-photo negative")))yield return risk;
    }
}
