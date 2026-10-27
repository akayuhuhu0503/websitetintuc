using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.Extensions.Configuration;

// Offline exact-byte Test scanner preparation. SQL writes/import remain in the
// existing signed/backup/preflight pipeline, not this tool.
if(args.Length!=2)throw new InvalidOperationException("Exact work packet and output folder required");
var root=PortalConfiguration.FindWorkspace();
var workPath=Path.GetFullPath(args[0],root);var output=Path.GetFullPath(args[1],root);
var prefix=Path.GetFullPath(Path.Combine(root,".local","evidence","phase3f"))+Path.DirectorySeparatorChar;
if(!workPath.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!output.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||Directory.Exists(output))throw new InvalidOperationException("New private scoped output required");
using var work=JsonDocument.Parse(await File.ReadAllBytesAsync(workPath));var w=work.RootElement;
var parent=await SourceBatch.ReadCheckpointAsync(root,w.GetProperty("parent").GetString()!,w.GetProperty("parentSha").GetString()!);
var allowed=w.GetProperty("scanHashes").EnumerateArray().Select(x=>x.GetString()!).ToHashSet();
if(allowed.Count is <1 or >12)throw new InvalidOperationException("Bounded affected public resources required");
var original=PortalConfiguration.Load(root);var connection=new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(DatabaseSettings.Connection(original));
if(connection.DataSource!=@".\SQLEXPRESS"||connection.InitialCatalog!="LandPortal_Dev")throw new InvalidOperationException("Exact local project source configuration required");
connection.InitialCatalog="LandPortal_Test";
var testRoot=Path.Combine(root,".local/tests/phase2a");
var config=new ConfigurationBuilder().AddConfiguration(original).AddInMemoryCollection(new Dictionary<string,string?>{
 ["ConnectionStrings:Portal"]=connection.ConnectionString,["Sql:ExpectedDatabase"]="LandPortal_Test",["WorkspaceRoot"]=root,
 ["Storage:MediaRoot"]=Path.Combine(testRoot,"media"),["Storage:TempRoot"]=Path.Combine(testRoot,"temp"),["Storage:KeysRoot"]=Path.Combine(testRoot,"keys"),
 ["Scanner:Provider"]="DefenderTest",["Scanner:DefenderAnonymousTestOnly"]="true",["Scanner:DefenderAllowedTestHashes"]=string.Join(',',allowed)
}).Build();
var scanner=InquiryScannerFactory.Create(config,"Development",48*1024*1024);
var health=await scanner.HealthAsync(CancellationToken.None);if(!health.Ready)throw new InvalidOperationException("Real Defender is not ready: "+health.Code);
Directory.CreateDirectory(output);var proofs=new List<object>();
foreach(var sha in allowed.Order()){
 var capture=parent.Captures.FirstOrDefault(x=>x.Sha256==sha&&x.Status==200&&x.Origin=="Network")??throw new InvalidDataException("Only actual public network capture bytes accepted");
 var path=StorageOptions.ResolveWithin(parent.Root,capture.Key);
 for(string? p=path;p!=null;p=Path.GetDirectoryName(p))if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Reparse source refused");
 await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
 if(Convert.ToHexStringLower(await SHA256.HashDataAsync(file))!=sha||file.Length!=capture.Bytes)throw new InvalidDataException("Input hash/size differs");file.Position=0;
 var result=await scanner.ScanAsync(file,CancellationToken.None);file.Position=0;
 if(Convert.ToHexStringLower(await SHA256.HashDataAsync(file))!=sha||result.State!=InquiryScanState.Clean||result.Provider!="MicrosoftDefender"||string.IsNullOrWhiteSpace(result.EngineVersion))throw new InvalidDataException("Real exact-byte Clean proof failed; no historical fallback");
 var now=DateTimeOffset.UtcNow;proofs.Add(new{inputSha256=sha,capture.Url,capture.Bytes,scannedUtc=now,result});
 // Invalid-format bytes remain NotScanned/validation-blocked in source staging;
 // a Clean AV result alone never turns them into a published media asset.
 foreach(var node in parent.Urls.Where(x=>x.CaptureSha==sha&&x.FormatStatus=="Validated")){
  node.ScanStatus="Clean";node.ScannedSha256=sha;node.ScannedUtc=now;node.ScanEngine=result.EngineVersion;
 }
 Console.WriteLine("Native Clean affected public bytes: "+sha);
}
await File.WriteAllTextAsync(Path.Combine(output,"native-proofs.json"),JsonSerializer.Serialize(proofs,new JsonSerializerOptions{WriteIndented=true}));
async Task<object> Save(InventoryState state){
 var bytes=JsonSerializer.SerializeToUtf8Bytes(state,InventoryLimits.Json);using var packed=new MemoryStream();packed.Write("LP3A-BR1"u8);using(var br=new BrotliStream(packed,CompressionLevel.Fastest,true))await br.WriteAsync(bytes);
 var data=packed.ToArray();var sha=SourceInventory.Hash(data);var path=Path.Combine(root,".local/phase3a",state.Id.ToString("N"),"checkpoints",sha+".bin");await using var f=new FileStream(path,FileMode.CreateNew);await f.WriteAsync(data);return new{checkpoint=Path.GetRelativePath(root,path),sha};
}
var refreshedParent=await Save(parent);var selected=w.GetProperty("sourceIds").EnumerateArray().Select(x=>x.GetString()!).ToHashSet();
parent.Items=parent.Items.Where(x=>selected.Contains(x.Source.SourceId)).ToList();if(parent.Items.Count!=6)throw new InvalidDataException("Exact six source contents required");
var checkpoint=await Save(parent);
await File.WriteAllTextAsync(Path.Combine(output,"checkpoints.json"),JsonSerializer.Serialize(new{parent=refreshedParent,selected=checkpoint,nativeProofs=proofs.Count,sqlWrites=0},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("Exact native proofs and immutable six-content checkpoint prepared; no SQL writes.");
