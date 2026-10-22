using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using LandPortal.MigrationCli;

if(args.Length!=2)throw new ArgumentException("Public technical signed fixture and new private report directory required.");
var fixture=Path.GetFullPath(args[0]);var report=Path.GetFullPath(args[1]);
if(Directory.Exists(report))throw new IOException("Unique report directory required.");
Directory.CreateDirectory(report);
const string thumb="94797BD79E275B09DEF613036BC00DF4EA91345F";
var trusted=new HashSet<string>{thumb};
var sha=Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(fixture)));
var tests=new List<object>();int failures=0;
async Task Run(string name,Func<Task> test){try{await test();tests.Add(new{name,status="PASS"});}catch(Exception e){failures++;tests.Add(new{name,status="FAIL",error=e.GetType().Name});}}
void Check(bool ok){if(!ok)throw new InvalidOperationException("Signature test assertion failed.");}
async Task Reject(Func<Task> action){try{await action();}catch(Exception e)when(e is UnauthorizedAccessException or InvalidDataException){return;}throw new InvalidOperationException("Unsafe signature accepted.");}
var nonce=new string('a',64);var now=DateTimeOffset.UtcNow;
Dictionary<string,object> Valid()=>new(){["Protocol"]=1,["Nonce"]=nonce,["FileSha256"]=sha,["Success"]=true,["Status"]="Valid",["Thumbprint"]=thumb,["CertificateTrusted"]=true,["CodeSigningEku"]=true,["NotBeforeUtc"]=now.AddDays(-1).ToString("O"),["NotAfterUtc"]=now.AddDays(1).ToString("O")};
Task Validate(Dictionary<string,object> envelope,int code=0,byte[]? error=null){PowerShellSignatureBridge.ValidateResult(code,JsonSerializer.SerializeToUtf8Bytes(envelope),error??[],nonce,sha,trusted,now);return Task.CompletedTask;}
async Task<(int Exit,byte[] Out,byte[] Error)> Child(string script){
 var host=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
 var start=new ProcessStartInfo(host){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
 foreach(var a in new[]{"-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))})start.ArgumentList.Add(a);
 using var p=Process.Start(start)!;using var output=new MemoryStream();using var error=new MemoryStream();
 var aout=p.StandardOutput.BaseStream.CopyToAsync(output);var aerr=p.StandardError.BaseStream.CopyToAsync(error);
 using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));await p.WaitForExitAsync(timeout.Token);await Task.WhenAll(aout,aerr);return(p.ExitCode,output.ToArray(),error.ToArray());
}
byte[] progress=[];
await Run("Native Valid fixture + raw legacy CLIXML progress reproduced; fixed bridge suppresses progress at source",async()=>{
 var old=await Child("Write-Progress -Activity 'LandPortal public technical fixture' -Completed; $s=Get-AuthenticodeSignature -LiteralPath '"+fixture.Replace("'","''")+"'; @{Status=$s.Status.ToString();Thumbprint=$s.SignerCertificate.Thumbprint}|ConvertTo-Json -Compress");
 await File.WriteAllBytesAsync(Path.Combine(report,"public-fixture-legacy-stdout.bin"),old.Out);await File.WriteAllBytesAsync(Path.Combine(report,"public-fixture-legacy-stderr.bin"),old.Error);
 using var native=JsonDocument.Parse(old.Out);Check(old.Exit==0&&native.RootElement.GetProperty("Status").GetString()=="Valid"&&native.RootElement.GetProperty("Thumbprint").GetString()==thumb);
 var raw=Encoding.UTF8.GetString(old.Error);Check(raw.StartsWith("#< CLIXML")&&raw.Length>0);
 using var reader=XmlReader.Create(new StringReader(raw[(raw.IndexOf('\n')+1)..]),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});
 var xml=XDocument.Load(reader);Check(xml.Root!.Elements().All(x=>x.Attribute("S")?.Value=="progress")&&!xml.Descendants().Any(x=>x.Attribute("S")?.Value=="Error"));progress=old.Error;
 await PowerShellSignatureBridge.VerifyAsync(fixture,sha,trusted);
});
await Run("Native tampered signature and unsigned public data cannot supply trusted stdout",async()=>{
 var tampered=Path.Combine(report,"tampered.ps1");await File.WriteAllBytesAsync(tampered,(await File.ReadAllBytesAsync(fixture)).Concat(Encoding.UTF8.GetBytes("\r\n# tampered fixture\r\n")).ToArray());
 await Reject(()=>PowerShellSignatureBridge.VerifyAsync(tampered,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(tampered))),trusted));
 var forged=Path.Combine(report,"forged.ps1");await File.WriteAllTextAsync(forged,JsonSerializer.Serialize(Valid()));
 await Reject(()=>PowerShellSignatureBridge.VerifyAsync(forged,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(forged))),trusted));
});
await Run("Native valid but unpinned certificate and mismatching exact byte SHA are denied",async()=>{
 await Reject(()=>PowerShellSignatureBridge.VerifyAsync(fixture,sha,new HashSet<string>{new string('0',40)}));
 await Reject(()=>PowerShellSignatureBridge.VerifyAsync(fixture,new string('0',64),trusted));
});
await Run("Structured untrusted expired future certificate or missing Code Signing EKU are denied",async()=>{
 foreach(var pair in new (string,object)[]{("CertificateTrusted",false),("CodeSigningEku",false),("NotAfterUtc",now.AddSeconds(-1).ToString("O")),("NotBeforeUtc",now.AddDays(2).ToString("O"))}){var data=Valid();data[pair.Item1]=pair.Item2;await Reject(()=>Validate(data));}
});
await Run("Real PowerShell Error record and abnormal exit are denied",async()=>{
 var actual=await Child("$ErrorActionPreference='Stop'; Write-Error 'Public technical fixture error'");
 Check(actual.Exit!=0&&actual.Error.Length>0);await File.WriteAllBytesAsync(Path.Combine(report,"public-fixture-real-error.bin"),actual.Error);
 await Reject(()=>{PowerShellSignatureBridge.ValidateResult(actual.Exit,actual.Out,actual.Error,nonce,sha,trusted,now);return Task.CompletedTask;});
 await Reject(()=>Validate(Valid(),23));
});
await Run("All stderr fails closed including progress, malformed CLIXML, Error record and invalid bytes",async()=>{
 foreach(var raw in new[]{progress,Encoding.UTF8.GetBytes("#< CLIXML\n<broken>"),Encoding.UTF8.GetBytes("#< CLIXML\n<Objs><S S=\"Error\">error</S></Objs>"),new byte[]{255,254,0}}){Check(raw.Length>0);await Reject(()=>Validate(Valid(),0,raw));}
});
await Run("Missing duplicate contradictory unknown or forged result fails closed",async()=>{
 var data=Valid();data.Remove("Success");await Reject(()=>Validate(data));
 foreach(var pair in new (string,object)[]{("Success",false),("Status","HashMismatch"),("Nonce","forged"),("FileSha256",new string('0',64)),("Protocol",2),("Thumbprint",new string('0',40)),("Extra","unknown")}){data=Valid();data[pair.Item1]=pair.Item2;await Reject(()=>Validate(data));}
 foreach(var output in new[]{Array.Empty<byte>(),Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Valid())+"{}"),Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Valid()).Replace("\"Protocol\":1","\"Protocol\":1,\"Protocol\":1")),new byte[]{255}})await Reject(()=>{PowerShellSignatureBridge.ValidateResult(0,output,[],nonce,sha,trusted,now);return Task.CompletedTask;});
});
await Run("Strict complete bound success envelope accepted; no certificate/date/status inference from empty stderr",()=>Validate(Valid()));
await File.WriteAllTextAsync(Path.Combine(report,"signature-tests.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,failures,tests,scope="Native public technical fixture and strict envelope negatives; certificate expiry/trust envelope cases are policy validation, not newly installed certificates; no SQL or actual approval signing"},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"RESULT {tests.Count-failures}/{tests.Count} signature-focused groups; SQL/full regression NOT RUN.");
return failures==0?0:1;
