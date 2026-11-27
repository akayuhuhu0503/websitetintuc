using System.Reflection;
using System.Text;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;

static class SourceQueueChecks
{
    internal static async Task Run(IServiceProvider services,DbContextOptions<PortalDbContext> options,FixtureSource fixture,Action<bool,string> check)
    {
        const string id="919313";var url="https://qldd.mae.gov.vn/news-"+id+".htm";var entry=Guid.NewGuid();
        var images=string.Join("",Enumerable.Range(1,13).Select(i=>$"<img src='https://media-qldd.mae.gov.vn/queue-{i}.png'>"));
        var html=$"<h1 class='tieu-de-tin-tuc'>Thirteen image source</h1><a class='chi-tiet-tin-back-icon' href='/chuyen-muc/news-75.htm'></a><article class='noi-dung-chi-tiet-tin'><p>Real source body</p>{images}</article>";
        fixture.Custom=new(){[url]=new(url,200,"text/html",Encoding.UTF8.GetBytes(html),DateTimeOffset.UtcNow)};
        await using(var db=new PortalDbContext(options))
        {
            db.CrawlRuns.Add(new(){Id=entry,Status=SourceSyncPolicy.RunStatus,Checkpoint="{}"});
            db.CrawlItems.Add(new(){Id=entry,CrawlRunId=entry,SourceId=id,SourceUrl=url,Kind=ContentKind.Article,Status="Queued"});
            db.Jobs.Add(new(){Id=entry,Type=SourceSyncPolicy.EntryType,IdempotencyKey=SourceSyncPolicy.Key(id),Status="Queued"});
            var sweep=await db.Jobs.SingleAsync(x=>x.IdempotencyKey==SourceSyncPolicy.SweepKey);sweep.Status="Running";sweep.LeaseUntilUtc=DateTimeOffset.UtcNow.AddMinutes(5);await db.SaveChangesAsync();
        }
        async Task Process()
        {
            await using var scope=services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var worker=scope.ServiceProvider.GetRequiredService<SourceSyncRunner>();
            var job=await db.Jobs.AsNoTracking().SingleAsync(x=>x.Id==entry);var sweep=await db.Jobs.AsNoTracking().SingleAsync(x=>x.IdempotencyKey==SourceSyncPolicy.SweepKey);
            using var session=fixture.Open();
            await (Task)typeof(SourceSyncRunner).GetMethod("ProcessAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(worker,[job,sweep,session,default(CancellationToken)])!;
        }
        await Process();
        await using(var db=new PortalDbContext(options)){var (i,d)=await new SourceSyncStore(db).ReadAsync(entry);check(i.Status=="Queued"&&d.Assets.Count==12,"First bounded image slice checkpoints 12 without losing remaining item");}
        await Process();
        await using(var db=new PortalDbContext(options)){var (i,d)=await new SourceSyncStore(db).ReadAsync(entry);check(i.Status=="Ready"&&d.Assets.Count==13&&d.Warnings.Count==0,"Fresh runner resumes thirteenth asset and completes source");}
        check(Enumerable.Range(1,13).All(i=>fixture.Requested.Count(x=>x=="https://media-qldd.mae.gov.vn/queue-"+i+".png")==1),"Resume does not redownload accepted assets");
        fixture.Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);fixture.Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var running=Process();await fixture.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var expected=Guid.NewGuid();
        await using(var db=new PortalDbContext(options))
        {await using var tx=await db.Database.BeginTransactionAsync();await SourceSyncStore.LockAsync(db,"SourceSync.Item."+entry);var r=await db.CrawlRuns.SingleAsync(x=>x.Id==entry);var cp=SourceSyncPolicy.Read<SourceSyncCheckpoint>(r.Checkpoint);cp.ExpectedRevisionId=expected;r.Checkpoint=SourceSyncPolicy.Serialize(cp);await db.SaveChangesAsync();await tx.CommitAsync();}
        fixture.Gate.SetResult();await running;fixture.Gate=null;fixture.Entered=null;
        await using(var db=new PortalDbContext(options)){var (_,d)=await new SourceSyncStore(db).ReadAsync(entry);check(d.ExpectedRevisionId==expected,"In-flight fetch cannot overwrite a committed binding/revision checkpoint");}
    }
}
