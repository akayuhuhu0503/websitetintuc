using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.MigrationCli;
public sealed record BatchReconciliation(string Status,int Expected,int Committed,int Missing,List<string> Errors);
public sealed partial class SourceBatch
{
    public async Task<BatchReconciliation> ReconcileAsync(SourceBatchPlan plan,string planHash)
    {
        await Guard(false);DemandStore(plan);
        if(plan.Database!=config["Sql:ExpectedDatabase"]||plan.InventoryId!=state.Id||plan.CheckpointSha256!=checkpointHash||plan.RecoveryProofSha256!=recoveryProofHash||plan.Rows.Count!=state.Items.Count||plan.Rows.GroupBy(x=>(x.Kind,x.SourceId)).Any(x=>x.Count()!=1))throw new InvalidDataException("Reconciliation target/session/proof/rows differ.");
        var prefix=plan.BatchId+"|";var exactPrefix=prefix+planHash+"|";
        var eligible=plan.Rows.Where(x=>x.Action!="Blocked").ToArray();
        // Stable batch id, no full audit body materialization or anonymous retry.
        var receipts=await db.AuditLogs.AsNoTracking().Where(x=>x.Action=="Phase3B.Import"&&x.Detail.StartsWith(prefix))
            .Select(x=>new{x.Detail,x.ContentId,x.RevisionId}).Take(plan.Rows.Count+1).ToListAsync();
        var errors=new List<string>();
        if(receipts.Count>eligible.Length||receipts.Any(x=>!x.Detail.StartsWith(exactPrefix,StringComparison.Ordinal))||receipts.GroupBy(x=>x.Detail).Any(x=>x.Count()!=1))errors.Add("Unexpected/duplicate receipt or batch id bound to another plan.");
        var expected=eligible.Select(x=>exactPrefix+x.Kind+":"+x.SourceId).ToHashSet();
        if(receipts.Any(x=>!expected.Contains(x.Detail)))errors.Add("Receipt identity is outside the reviewed eligible set.");
        var run=await db.ImportRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==plan.BatchId);
        if(run!=null&&(run.CrawlRunId!=state.Id||receipts.Count==0))errors.Add("Run/checkpoint exists without matching receipts; investigate before retry.");
        if(errors.Count>0)return new("NEEDS_RECONCILIATION",eligible.Length,receipts.Count,eligible.Length-receipts.Count,errors);
        if(receipts.Count>0)
        {
            var verification=await VerifyAsync(plan,planHash);
            var absent=eligible.Where(x=>!receipts.Any(r=>r.Detail==exactPrefix+x.Kind+":"+x.SourceId)).Select(x=>"NotCommitted:"+x.Kind+":"+x.SourceId).ToHashSet();
            errors.AddRange(verification.Where(x=>!absent.Contains(x)));
        }
        foreach(var row in eligible.Where(x=>!receipts.Any(r=>r.Detail==exactPrefix+x.Kind+":"+x.SourceId)))
        {
            var record=await db.SourceRecords.AsNoTracking().SingleOrDefaultAsync(x=>x.Source==Source&&x.Kind==row.Kind&&x.SourceId==row.SourceId);
            if(await Stamp(record)!=row.ExistingStamp)errors.Add("Uncommitted source changed since preview:"+row.Kind+":"+row.SourceId);
        }
        return new(errors.Count>0?"NEEDS_RECONCILIATION":receipts.Count==0?"ABSENT_CONFIRMED":receipts.Count==eligible.Length?"COMMITTED_VERIFIED":"PARTIAL_VERIFIED",eligible.Length,receipts.Count,eligible.Length-receipts.Count,errors);
    }
}
