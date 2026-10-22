using System.Text.Json;
using LandPortal.MigrationCli;

if(args.FirstOrDefault()=="--sql-checks")return await ScaleSqlChecks.Run(args[1]);
if(args.Length!=4)throw new ArgumentException("Parent checkpoint, SHA, work plan, unique report required.");
var workspace=Directory.GetCurrentDirectory();var report=Path.GetFullPath(args[3]);
if(Directory.Exists(report))throw new IOException("Unique evidence required.");Directory.CreateDirectory(report);
var parent=await SourceBatch.ReadCheckpointAsync(workspace,args[0],args[1]);
using var work=JsonDocument.Parse(await File.ReadAllBytesAsync(args[2]));
var tests=new List<object>();var failures=0;
async Task Run(string name,Func<Task> body){try{await body();tests.Add(new{name,status="PASS"});}catch(Exception e){failures++;tests.Add(new{name,status="FAIL",error=e.GetType().Name});}}
void Check(bool value){if(!value)throw new InvalidOperationException("Assertion failed.");}
void Reject(Action body){try{body();}catch(InvalidDataException){return;}throw new InvalidOperationException("Unsafe scope accepted.");}
InventoryState Clone(InventoryState state)=>JsonSerializer.Deserialize<InventoryState>(JsonSerializer.Serialize(state))!;
RecoveryInventory Inventory(int n,bool legacy=false)=>new(){Scope=legacy?null:MediaRecovery.PublicBatchScope,ParentCheckpointSha256=args[1],Resources=Enumerable.Range(0,n).Select(x=>new RecoveryResource(x.ToString("x64"),100,"image/jpeg","public-cache.bin",["https://qldd.mae.gov.vn/public"],["Article:1"],legacy&&x<53)).ToList()};
await Run("All bounded real checkpoint projections preserve parent provenance and complete items",async()=>{
 var before=JsonSerializer.Serialize(parent);var identities=new HashSet<string>();var total=0;
 foreach(var batch in work.RootElement.GetProperty("batches").EnumerateArray()){
  var state=await SourceBatch.ReadCheckpointAsync(workspace,batch.GetProperty("checkpoint").GetString()!,batch.GetProperty("checkpointSha").GetString()!);
  MediaRecovery.ValidatePublicProjection(state,parent);total+=state.Items.Count;
  foreach(var item in state.Items)Check(identities.Add(item.Source.Kind+":"+item.Source.SourceId));
 }
 Check(total==210&&identities.Count==210&&before==JsonSerializer.Serialize(parent));
});
var first=work.RootElement.GetProperty("batches")[0];var selected=await SourceBatch.ReadCheckpointAsync(workspace,first.GetProperty("checkpoint").GetString()!,first.GetProperty("checkpointSha").GetString()!);
await Run("Source content tampering duplicate identity and changed acquisition provenance rejected",()=>{
 foreach(var mutate in new Action<InventoryState>[] {s=>s.Items[0].NormalizedBody+="tamper",s=>s.Items[0].RawSha256=new string('0',64),s=>s.Items.Add(s.Items[0]),s=>s.Root+="/other",s=>s.RequestsReserved++,s=>s.Id=Guid.NewGuid(),s=>s.Captures.RemoveAt(0)}){
  var clone=Clone(selected);mutate(clone);var before=JsonSerializer.Serialize(parent);Reject(()=>MediaRecovery.ValidatePublicProjection(clone,parent));Check(before==JsonSerializer.Serialize(parent));
 }return Task.CompletedTask;
});
await Run("Empty and oversized content projections rejected",()=>{
 var clone=Clone(selected);clone.Items=[];Reject(()=>MediaRecovery.ValidatePublicProjection(clone,parent));clone=Clone(selected);clone.Items=parent.Items.Take(21).ToList();Reject(()=>MediaRecovery.ValidatePublicProjection(clone,parent));return Task.CompletedTask;
});
await Run("Existing legacy53 to60 boundary preserved; public jobs never enlist protected Dev resources",()=>{
 MediaRecovery.ValidateShape(Inventory(53,true));MediaRecovery.ValidateShape(Inventory(60,true));Reject(()=>MediaRecovery.ValidateShape(Inventory(52,true)));Reject(()=>MediaRecovery.ValidateShape(Inventory(61,true)));
 MediaRecovery.ValidateShape(Inventory(0));MediaRecovery.ValidateShape(Inventory(60));Reject(()=>MediaRecovery.ValidateShape(Inventory(61)));
 var value=Inventory(1);value.Resources[0]=value.Resources[0] with{IsProtectedSource=true};Reject(()=>MediaRecovery.ValidateShape(value));return Task.CompletedTask;
});
await Run("Duplicate resources unknown policy scope and missing parent binding rejected",()=>{
 var value=Inventory(2);value.Resources[1]=value.Resources[0];Reject(()=>MediaRecovery.ValidateShape(value));
 value=Inventory(1);value.Scope="unreviewed";Reject(()=>MediaRecovery.ValidateShape(value));value=Inventory(1);value.Policy="relaxed";Reject(()=>MediaRecovery.ValidateShape(value));value=Inventory(1);value.ParentCheckpointSha256=null;Reject(()=>MediaRecovery.ValidateShape(value));return Task.CompletedTask;
});
await Run("Fresh non-Clean or incomplete public proof cannot fall back to old Clean checkpoint metadata",()=>{
 var inventory=Inventory(1);
 Reject(()=>MediaRecovery.RequirePublicClean(inventory,[]));
 foreach(var state in new[]{"Infected","ScanFailed","NotScanned"}){
  var proof=new RecoveryProof(inventory.JobId,inventory.Resources[0].Sha256,100,MediaRecovery.Policy,state,null,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(24),new(LandPortal.Domain.InquiryScanState.ScanFailed,"negative structural case; not a native scan",null),"private.bin");
  Reject(()=>MediaRecovery.RequirePublicClean(inventory,[proof]));
 }return Task.CompletedTask;
});
await File.WriteAllTextAsync(Path.Combine(report,"focused.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,tests,failures,scope="Projection and bounded scope only. Real Defender/SQL checks recorded separately; no full regression."},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"RESULT {tests.Count-failures}/{tests.Count} scale-focused groups");return failures==0?0:1;
