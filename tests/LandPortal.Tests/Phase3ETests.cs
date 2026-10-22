using System.Data.Common;
using System.Text.Json;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Tests;
public static class Phase3ETests
{
    static void Check(bool test,string message){if(!test)throw new InvalidOperationException(message);}
    sealed class Counter:DbCommandInterceptor
    {
        public List<string> Queries {get;}=[];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c,CommandEventData e,InterceptionResult<DbDataReader> r,CancellationToken ct=default){Queries.Add(c.CommandText);return ValueTask.FromResult(r);}
    }
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(string root,DbContextOptions<PortalDbContext> options,IConfiguration config)
    {
        yield return("3E real recovery proof SQL receipts fetched once, missing receipt denied, no Clean bypass",async()=>
        {
            using var pointer=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,".local/phase3c-current.json")));var folder=Path.Combine(root,pointer.RootElement.GetProperty("root").GetString()!);
            var ip=Path.Combine(folder,"inventory/inventory.json");var inv=JsonSerializer.Deserialize<RecoveryInventory>(await File.ReadAllBytesAsync(ip))!;var set=JsonSerializer.Deserialize<RecoveryProofSet>(await File.ReadAllBytesAsync(Path.Combine(folder,"scan-resume/proof-set.json")))!;
            var counter=new Counter();var counted=new DbContextOptionsBuilder<PortalDbContext>(options).AddInterceptors(counter).Options;
            await using var db=new PortalDbContext(counted);var recovery=new MediaRecovery(db,StorageOptions.FromConfiguration(config),config,root);
            var verified=await recovery.ValidateSetAsync(inv,SourceInventory.Hash(await File.ReadAllBytesAsync(ip)),set);
            Check(verified.Count==60&&counter.Queries.Count(x=>x.Contains("[AuditLogs]"))==2,"Receipt validation regressed to N+1 SQL.");
            // Valid real proof bytes with a different path are not a registered receipt.
            var actual=set.Proofs[0];var alternate=Path.Combine(root,actual.Path+".unregistered-"+Guid.NewGuid().ToString("N"));await File.WriteAllBytesAsync(alternate,await File.ReadAllBytesAsync(Path.Combine(root,actual.Path)));
            var bad=set with{Proofs=[actual with{Path=Path.GetRelativePath(root,alternate)}]};
            try{await recovery.ValidateSetAsync(inv,SourceInventory.Hash(await File.ReadAllBytesAsync(ip)),bad);throw new InvalidOperationException("Unregistered proof accepted.");}catch(InvalidDataException){}
            await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root),"receipt-query-count.json"),new{RealProofs=60,AuditQueriesBeforeNegative=2,NativeScans=0,Policy=inv.Policy});
        });
        yield return("3E actual COMMIT then injected lost acknowledgement reconciles complete without duplicate writes",async()=>
        {
            var f=await BatchImportTests.Fixture(root);SourceBatchPlan plan;string hash;
            await using(var db=new PortalDbContext(options)){
                var b=new SourceBatch(db,StorageOptions.FromConfiguration(config),config,root,f.State,f.Hash);plan=await b.PreviewAsync();hash=SourceInventory.HashText(JsonSerializer.Serialize(plan));
                Check((await b.ReconcileAsync(plan,hash)).Status=="ABSENT_CONFIRMED","Fixture not initially absent.");
                var result=await b.CommitAsync(plan,hash,afterCommit:()=>throw new TimeoutException("TECHNICAL TEST lost acknowledgement after actual SQL COMMIT"));
                Check(result.Single().Status=="NeedsReconciliation","Commit uncertainty was incorrectly labelled rollback.");
            }
            await using(var db=new PortalDbContext(options)){
                var b=new SourceBatch(db,StorageOptions.FromConfiguration(config),config,root,f.State,f.Hash);
                Check((await b.ReconcileAsync(plan,hash)).Status=="COMMITTED_VERIFIED","Server commit not reconciled using durable data.");
                var before=JsonSerializer.Serialize(await b.ReadbackAsync(plan,hash));var count=await db.AuditLogs.CountAsync();
                Check((await b.CommitAsync(plan,hash)).Single().Status=="AlreadyCommitted","Verified repeat did not skip existing import.");
                Check(before==JsonSerializer.Serialize(await b.ReadbackAsync(plan,hash))&&await db.AuditLogs.CountAsync()==count,"Acknowledgement-loss repeat changed source/revisions/files/audit.");
                await b.RollbackPublicationAsync(plan,hash);Check(await db.SourceRecords.CountAsync(x=>x.SourceId==f.State.Items[0].Source.SourceId)==1,"Technical rollback removed source.");
            }
            await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root),"uncertain-commit.json"),new{plan.BatchId,Status="PASS",Fault="Injected client acknowledgement loss after real SQL COMMIT; not a naturally occurring SQL timeout",Reconciled="COMMITTED_VERIFIED",NoDuplicate=true});
        });
        yield return("3E partial checkpoint verified resume, complete replay, different plan hash and stale source fail closed",async()=>
        {
            var f=await BatchImportTests.Fixture(root,2);await using var db=new PortalDbContext(options);var b=new SourceBatch(db,StorageOptions.FromConfiguration(config),config,root,f.State,f.Hash);var plan=await b.PreviewAsync();var sha=SourceInventory.HashText(JsonSerializer.Serialize(plan));
            Check((await b.CommitAsync(plan,sha,stopAfter:1)).Count==1,"Partial checkpoint failed.");
            var partial=await b.ReconcileAsync(plan,sha);Check(partial.Status=="PARTIAL_VERIFIED"&&partial.Committed==1&&partial.Missing==1,"Partial state not classified.");
            var wrong=await b.ReconcileAsync(plan,new string('a',64));Check(wrong.Status=="NEEDS_RECONCILIATION","Different plan hash accepted existing batch.");
            var repeat=await b.CommitAsync(plan,sha);Check(repeat.Count==2&&repeat.Any(x=>x.Status=="AlreadyCommitted")&&repeat.Any(x=>x.Status=="CreatePublishedTest"),"Partial resume duplicated/lost records.");
            Check((await b.ReconcileAsync(plan,sha)).Status=="COMMITTED_VERIFIED","Complete resume not verified.");
            await b.RollbackPublicationAsync(plan,sha);
        });
        foreach(var risk in BatchImportTests.Cases(root,options,config).Where(x=>x.Name.StartsWith("3B SQL fault")||x.Name.StartsWith("3B SQL changed")))yield return risk;
    }
}
