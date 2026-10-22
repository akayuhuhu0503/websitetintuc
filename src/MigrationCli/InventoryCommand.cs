using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace LandPortal.MigrationCli;

public static class InventoryCommand
{
    public static async Task<int> ExecuteAsync(string[] args,string workspace,IConfiguration config,PortalDbContext db)
    {
        SourceInventory.RequireTest(config);
        string? Option(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        int Number(string key,int value)=>int.TryParse(Option(key),out var n)?n:value;
        var command=args[0];
        if(command is not("inventory-start" or "inventory-resume" or "inventory-report" or "inventory-scan"))throw new InvalidOperationException("Unsupported inventory command.");
        var id=command=="inventory-start"?Guid.NewGuid():Guid.Parse(Option("--run")??throw new InvalidOperationException("Explicit --run is required; never adopts a previous sample run."));
        var sessionRoot=Path.Combine(workspace,".local","phase3a",id.ToString("N"));SourceInventory.NoReparse(Path.Combine(sessionRoot,"collector.lock"));Directory.CreateDirectory(sessionRoot);
        // OS releases this lease on kill/restart. Acquire before loading SQL, including report/scan.
        await using var exclusive=new FileStream(Path.Combine(sessionRoot,"collector.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var state=command=="inventory-start"?new InventoryState{Id=id}:await SourceInventory.LoadAsync(db,workspace,id);
        var collector=new SourceInventory(db,config,state);await collector.InitializeAsync(workspace);
        using var cancel=new CancellationTokenSource();ConsoleCancelEventHandler handler=(_,e)=>{e.Cancel=true;cancel.Cancel();};Console.CancelKeyPress+=handler;
        try
        {
            if(command=="inventory-start")
            {
                collector.Seed();
                if(Option("--seed-manifest") is string source)
                {
                    var file=Path.GetFullPath(source,workspace);
                    if(!file.StartsWith(Path.Combine(workspace,".local")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Seed manifest must remain in this workspace .local.");
                    var legacy=JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(file),SampleCollector.JsonOptions)!;
                    if(legacy.SchemaVersion!=1)throw new InvalidDataException("Only existing schema-1 source cache can seed this new inventory.");
                    await collector.SeedLegacyAsync(legacy,StorageOptions.FromConfiguration(config));
                }
            }
            if(Option("--refresh-url") is string refresh)
            {
                var n=collector.Add(refresh,SourceCatalog.BaseUrl+"/")??throw new InvalidDataException("Refresh URL rejected.");
                if(n.Status=="OutOfScope")throw new InvalidDataException("Refresh outside approved hosts.");n.Refresh=true;n.Status="Discovered";n.AvailableUtc=DateTimeOffset.MinValue;n.Attempts=0;
            }
            if(args.Contains("--reparse-failed"))foreach(var n in state.Urls.Where(x=>x.Status=="ValidationFailed"&&x.Role is "Detail" or "List" or "Feed")){n.Status="Discovered";n.Attempts=0;n.AvailableUtc=DateTimeOffset.MinValue;}
            if(args.Contains("--reparse-all"))
            {
                if(!args.Contains("--offline"))throw new InvalidOperationException("Rebuild of derived inventory requires offline mode; no HTTP fallback.");
                state.Items.Clear();state.Lists.Clear();
                foreach(var n in state.Urls.Where(x=>x.Status!="OutOfScope"&&state.Captures.Any(c=>c.Url==x.Url&&c.Status==200))){n.Status="Discovered";n.Refresh=false;if(n.Role=="Detail")n.CategoryIds.Clear();}
            }
            if(command is "inventory-start" or "inventory-resume")
            {
                var offline=args.Contains("--offline");
                await collector.RunAsync(new(){Offline=offline,Requests=Number("--requests",offline?0:20),Pages=Number("--pages",offline?2000:16),Contents=Number("--contents",offline?10000:8),Assets=Number("--assets",offline?10000:8),Seconds=Number("--seconds",offline?600:300),Bytes=(long)Number("--mib",128)*1024*1024,ApprovalFile=Option("--approval"),DetailBatchFile=Option("--detail-batch"),DetailBatchSha256=Option("--detail-batch-sha")},cancel.Token);
            }
            else if(command=="inventory-scan")await collector.ScanNewPublicFilesAsync(cancel.Token);
            var reportRoot=Option("--report")??Path.Combine(workspace,".local","evidence","phase3a","inventory-"+DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ")+"-"+Guid.NewGuid().ToString("N")[..8]);
            var report=Path.GetFullPath(reportRoot,workspace);
            if(!report.StartsWith(Path.Combine(workspace,".local","evidence","phase3a")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Reports require a unique project-local Phase3A evidence directory.");
            await collector.WriteReportAsync(report);
            Console.WriteLine(JsonSerializer.Serialize(new{RunId=state.Id,state.Status,Urls=state.Urls.Count,VerifiedIdentities=state.Items.Count,state.RequestsReserved,Report=Path.GetRelativePath(workspace,report),WholeSourceTotal="Unknown; not a full migration"},InventoryLimits.Json));
            return 0;
        }
        finally{Console.CancelKeyPress-=handler;}
    }
}
