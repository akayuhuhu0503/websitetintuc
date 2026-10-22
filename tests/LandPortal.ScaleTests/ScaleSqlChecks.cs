using System.Text.Json;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

static class ScaleSqlChecks
{
 public static async Task<int> Run(string report)
 {
  var workspace=PortalConfiguration.FindWorkspace();var config=PortalConfiguration.Load(workspace);
  if(config["Sql:ExpectedDatabase"]!="LandPortal_Test")throw new InvalidOperationException("Explicit Test only.");
  if(Directory.Exists(report))throw new IOException("Unique evidence required.");Directory.CreateDirectory(report);
  using var pointer=JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(workspace,".local/phase3h-current.json")));
  var root=pointer.RootElement.GetProperty("scanRoot").GetString()!;var invPath=Path.Combine(workspace,root,"b01/inventory/inventory.json");
  var sha=SourceInventory.Hash(await File.ReadAllBytesAsync(invPath));var inv=await MediaRecovery.ReadBoundAsync<RecoveryInventory>(invPath,sha);
  var set=JsonSerializer.Deserialize<RecoveryProofSet>(await File.ReadAllBytesAsync(Path.Combine(workspace,root,"b01/scan/proof-set.json")))!;
  await using var db=new PortalDbContext(DatabaseSettings.Options(config));var service=new MediaRecovery(db,StorageOptions.FromConfiguration(config),config,workspace);
  var tests=new List<object>();int failures=0;
  async Task Test(string name,Func<Task> body){try{await body();tests.Add(new{name,status="PASS"});}catch(Exception e){failures++;tests.Add(new{name,status="FAIL",error=e.GetType().Name});}}
  async Task Deny(Func<Task> body){try{await body();}catch(Exception e)when(e is InvalidDataException or InvalidOperationException){return;}throw new Exception("Unsafe evidence accepted.");}
  var before=await db.AuditLogs.CountAsync(x=>x.Actor==inv.JobId.ToString()&&x.Action=="Phase3C.ScanProof");
  await Test("Real native60 current-engine proofs validated against durable SQL receipts, no new scan",async()=>{var proofs=await service.ValidateSetAsync(inv,sha,set);if(proofs.Count!=60||proofs.Any(x=>x.State!="Clean"))throw new Exception("Incomplete native evidence");});
  await Test("Missing duplicate or wrong inventory proof sets rejected",async()=>{
   await Deny(()=>service.ValidateSetAsync(inv,sha,set with{Proofs=set.Proofs.Skip(1).ToList()}));
   await Deny(()=>service.ValidateSetAsync(inv,sha,set with{Proofs=[set.Proofs[0],set.Proofs[0]]}));
   await Deny(()=>service.ValidateSetAsync(inv,sha,set with{InventorySha256=new string('0',64)}));
  });
  await Test("Exact valid proof bytes without registered receipt refused",async()=>{
   var actual=set.Proofs[0];var original=Path.GetFullPath(actual.Path,workspace);var alternate=original+".unregistered-"+Guid.NewGuid().ToString("N");
   await File.WriteAllBytesAsync(alternate,await File.ReadAllBytesAsync(original));
   await Deny(()=>service.ValidateSetAsync(inv,sha,set with{Proofs=set.Proofs.Select(x=>x==actual?x with{Path=Path.GetRelativePath(workspace,alternate)}:x).ToList()}));
  });
  await Test("Dev scope cannot validate or scan Test evidence and read-only checks added zero receipts",async()=>{
   var wrong=new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{{"Sql:ExpectedDatabase","LandPortal_Dev"}}).Build();
   await Deny(()=>new MediaRecovery(db,StorageOptions.FromConfiguration(config),wrong,workspace).ValidateSetAsync(inv,sha,set));
   if(await db.AuditLogs.CountAsync(x=>x.Actor==inv.JobId.ToString()&&x.Action=="Phase3C.ScanProof")!=before)throw new Exception("Unexpected receipt write");
  });
  await File.WriteAllTextAsync(Path.Combine(report,"sql-focused.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,tests,failures,receiptCount=before,scope="Real SQLTest read-only proof checks; no fabricated Clean or additional native scan"},new JsonSerializerOptions{WriteIndented=true}));
  Console.WriteLine($"RESULT {tests.Count-failures}/{tests.Count} SQL proof groups");return failures==0?0:1;
 }
}
