using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.DataProtection;
namespace LandPortal.Tests;
public static class ScannerTests
{
    static void Assert(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
    public static byte[] Eicar()=>Encoding.ASCII.GetBytes(string.Concat(new[]{@"X5O!P%@AP[4\PZX54(P^)7CC)7}","$","EICAR-STANDARD-ANTIVIRUS-TEST-FILE!","$H+H*"}));
    public static string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
    public static byte[] EicarArchive(){using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)){var entry=zip.CreateEntry("eicar.com");entry.LastWriteTime=new(2000,1,1,0,0,0,TimeSpan.Zero);using var stream=entry.Open();stream.Write(Eicar());}return memory.ToArray();}
    public static byte[] Png(){using var bitmap=new SkiaSharp.SKBitmap(4,4);bitmap.Erase(SkiaSharp.SKColors.Blue);using var encoded=bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);return encoded.ToArray();}
    public static IConfiguration DefenderConfig(IConfiguration config,IEnumerable<string> hashes)=>new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{["Scanner:Provider"]="DefenderTest",["Scanner:DefenderAnonymousTestOnly"]="true",["Scanner:DefenderAllowedTestHashes"]=string.Join(',',hashes)}).Build();
    public static async Task<InquiryAttachment> Internal(PortalDbContext db,InquiryFinishTests.Fixture fixture,IConfiguration config,byte[] bytes,string session)
    {
        var row=new InquiryAttachment{SessionHash=TextNormalizer.Hash(session),StorageKey="inquiry-private/"+Guid.NewGuid().ToString("N")+".bin",ProtectedName=fixture.Protection.CreateProtector("LandPortal.InquiryFile.Name.v1").Protect("anonymous-test.bin"),Mime="application/octet-stream",Sha256=Hash(bytes),Bytes=bytes.Length,CreatedUtc=DateTimeOffset.UtcNow,ExpiresUtc=DateTimeOffset.UtcNow.AddHours(1),AvailableUtc=DateTimeOffset.UtcNow};
        var path=fixture.Files(db).PathFor(row);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,bytes);db.InquiryAttachments.Add(row);await db.SaveChangesAsync();return row;
    }
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(string root,DbContextOptions<PortalDbContext> options,IConfiguration config)
    {
        yield return("SCAN real signed Defender Clean/EICAR exact bytes SQL hash/signatures/audit no source promotion",async()=>
        {
            var dir=TestEvidencePaths.At(root,"scanner-sql");Directory.CreateDirectory(dir);await using var sink=new LocalMailSink(Path.Combine(dir,"mail"));var fixture=new InquiryFinishTests.Fixture(root,options,config,sink);var png=Png();var eicar=Eicar();Assert(eicar.Length==68&&Hash(eicar)=="275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f","Official EICAR fixture mismatch.");
            var archive=EicarArchive();var scanner=InquiryScannerFactory.Create(DefenderConfig(config,[Hash(png),Hash(archive)]),"Development");Assert((await scanner.HealthAsync()).Ready,"Real signed active Defender required, otherwise SCAN VERIFIED BLOCKED.");
            var rows=new List<object>();
            foreach(var (bytes,expected,label) in new[]{(png,InquiryScanState.Clean,"anonymous PNG"),(archive,InquiryScanState.Infected,"ZIP contains exact official 68-byte EICAR; internal Test only")})
            {
                Guid id;await using(var db=new PortalDbContext(options)){var f=await Internal(db,fixture,config,bytes,Guid.NewGuid().ToString());id=f.Id;await new InquiryScanWorker(db,fixture.Files(db),scanner).RunOnceAsync(id);}
                await using(var db=new PortalDbContext(options)){var f=await db.InquiryAttachments.AsNoTracking().SingleAsync(x=>x.Id==id);Assert(f.ScanState==expected&&f.ScannedSha256==Hash(bytes)&&f.Sha256==Hash(bytes)&&f.ScannedUtc!=null&&f.EngineVersion!.StartsWith("Defender/"),"Genuine engine conclusion/hash missing: "+label);var audits=await db.AuditLogs.AsNoTracking().Where(x=>x.Action=="inquiry.scan.completed"&&x.Detail.Contains(id.ToString())).ToListAsync();Assert(audits.Count==1&&audits[0].Detail.Contains("MicrosoftDefender")&&audits[0].Detail.Contains("SignatureVersion"),"Scan audit lacks exact proof.");Assert(!await new InquiryScanWorker(db,fixture.Files(db),scanner).RunOnceAsync(id),"Conclusive state automatically retried.");rows.Add(new{id,label,status=f.ScanState.ToString(),f.Sha256,f.ScannedSha256,f.Bytes,f.ScanAttempts,f.EngineVersion,f.ScannedUtc,engineOutput=JsonSerializer.Deserialize<JsonElement>(audits[0].Detail)});}
            }
            var noApproval=await scanner.ScanAsync(new MemoryStream("unapproved anonymous bytes"u8.ToArray()));Assert(noApproval.State==InquiryScanState.ScanFailed&&noApproval.Error=="NotApprovedAnonymousTestBytes","Unapproved citizen bytes admitted to Defender cloud policy.");
            var large=await scanner.ScanAsync(new MemoryStream(new byte[InquiryFileOptions.MaximumBytes+1]));Assert(large.State==InquiryScanState.ScanFailed,"Oversize scan not closed.");
            foreach(var env in new[]{"Production","Staging"}){try{_ = InquiryScannerFactory.Create(DefenderConfig(config,[Hash(png)]),env);throw new Exception("Environment guard bypassed.");}catch(InvalidOperationException){}}
            await File.WriteAllTextAsync(Path.Combine(dir,"real-engine.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,database="LandPortal_Test",provider="MicrosoftDefender",anonymousHashesOnly=true,policyChanged=false,rows},new JsonSerializerOptions{WriteIndented=true}));
        });
        yield return("SCAN negative real sockets offline malformed timeout limit; SQL claim restart retry fencing tamper no Clean fixtures",async()=>
        {
            var dir=TestEvidencePaths.At(root,"scanner-negative");Directory.CreateDirectory(dir);var cases=new List<object>();
            async Task Wire(bool delay)
            {
                using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var server=Task.Run(async()=>{using var client=await listener.AcceptTcpClientAsync(cts.Token);if(delay)await Task.Delay(2000,cts.Token);else await client.GetStream().WriteAsync("INVALID-PROTOCOL\0"u8.ToArray(),cts.Token);});
                var scanner=new ClamdInquiryScanner(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Scanner:ClamdPort"]=port.ToString(),["Scanner:TimeoutSeconds"]="1"}).Build());var result=await scanner.ScanAsync(new MemoryStream(Png()));Assert(result.State==InquiryScanState.ScanFailed,"Fault socket generated Clean.");await server;cases.Add(new{name=delay?"actual socket timeout":"actual malformed protocol",state=result.State.ToString(),result.Error});
            }
            await Wire(false);await Wire(true);
            var absent=new ClamdInquiryScanner(new ConfigurationBuilder().Build());Assert((await absent.ScanAsync(new MemoryStream(Png()))).State==InquiryScanState.ScanFailed,"No scanner became Clean.");
            Assert((await absent.ScanAsync(new MemoryStream(new byte[InquiryFileOptions.MaximumBytes+1]))).Error=="ScannerFileTooLarge","Stream limit not checked.");
            await using var sink=new LocalMailSink(Path.Combine(dir,"mail"));var clock=new InquiryFinishTests.Clock();var fixture=new InquiryFinishTests.Fixture(root,options,config,sink,clock);Guid id;
            await using(var db=new PortalDbContext(options))id=(await Internal(db,fixture,config,Png(),Guid.NewGuid().ToString())).Id;
            clock.Utc=clock.Utc.AddSeconds(1);
            async Task<bool> Claim(){await using var db=new PortalDbContext(options);return await new InquiryScanWorker(db,fixture.Files(db),absent,clock).RunOnceAsync(id);}
            Assert((await Task.WhenAll(Claim(),Claim())).Count(x=>x)==1,"Two workers scanned one attempt twice.");
            for(var i=0;i<2;i++){clock.Utc=clock.Utc.AddMinutes(10);await Claim();}
            clock.Utc=clock.Utc.AddMinutes(10);Assert(!await Claim(),"Retry was unbounded.");
            await using(var db=new PortalDbContext(options)){var f=await db.InquiryAttachments.SingleAsync(x=>x.Id==id);Assert(f.ScanState==InquiryScanState.ScanFailed&&f.ScanAttempts==3&&await db.AuditLogs.CountAsync(x=>x.Action=="inquiry.scan.completed"&&x.Detail.Contains(id.ToString()))==3,"Retry/audit count mismatch.");f.ScanState=InquiryScanState.Scanning;f.LeaseUntilUtc=clock.Utc.AddMinutes(-1);await db.SaveChangesAsync();}
            Assert(!await Claim(),"Exhausted expired lease rescanned.");await using(var db=new PortalDbContext(options))Assert((await db.InquiryAttachments.AsNoTracking().SingleAsync(x=>x.Id==id)).ScanState==InquiryScanState.ScanFailed,"Expired lease stuck forever Scanning.");
            Guid tamper;await using(var db=new PortalDbContext(options)){var f=await Internal(db,fixture,config,Png(),Guid.NewGuid().ToString());tamper=f.Id;await File.WriteAllTextAsync(fixture.Files(db).PathFor(f),"changed bytes");await new InquiryScanWorker(db,fixture.Files(db),absent).RunOnceAsync(tamper);db.ChangeTracker.Clear();Assert((await db.InquiryAttachments.SingleAsync(x=>x.Id==tamper)).ScanError=="FileHashMismatch","Tamper was not rejected before scanner.");}
            Guid late;await using(var db=new PortalDbContext(options))late=(await Internal(db,fixture,config,Png(),Guid.NewGuid().ToString())).Id;
            var gate=new DelayedFailure();Task<bool> task;await using(var db=new PortalDbContext(options))
            {
                task=new InquiryScanWorker(db,fixture.Files(db),gate).RunOnceAsync(late);await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await using(var concurrent=new PortalDbContext(options)){var f=await concurrent.InquiryAttachments.SingleAsync(x=>x.Id==late);f.LeaseToken=Guid.NewGuid();await concurrent.SaveChangesAsync();}gate.Release.SetResult();
                try{await task;throw new Exception("Late result overwrote newer token.");}catch(DbUpdateConcurrencyException){}
            }
            await using(var db=new PortalDbContext(options))Assert(!await db.AuditLogs.AnyAsync(x=>x.Action=="inquiry.scan.completed"&&x.Detail.Contains(late.ToString())),"Fenced result created audit effect.");
            await File.WriteAllTextAsync(Path.Combine(dir,"negative-results.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,cases,offline=true,boundedAttempts=3,concurrentSingleClaim=true,expiredLeaseRecovered=true,tamperBlocked=true,lateResultFenced=true,positiveMockUsed=false},new JsonSerializerOptions{WriteIndented=true}));
        });
    }
    // Negative orchestration only. This test double cannot conclude Clean or Infected.
    private sealed class DelayedFailure:IInquiryScanner
    {
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<(bool Ready,string Code)> HealthAsync(CancellationToken cancellation=default)=>Task.FromResult((false,"NegativeFaultInjection"));
        public async Task<InquiryScanResult> ScanAsync(Stream file,CancellationToken cancellation=default){Entered.SetResult();await Release.Task;return new(InquiryScanState.ScanFailed,"NegativeFaultInjection",null);}
    }
    public static async Task SourceReportAsync(string root,IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        var dir=TestEvidencePaths.At(root,"source-53");if(Directory.Exists(dir))throw new InvalidOperationException("Refuse source evidence overwrite.");Directory.CreateDirectory(dir);
        var pointer=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,".local/evidence/phase2c/scan-current.json")));var task=pointer.RootElement.GetProperty("directory").GetString()!;
        var baseline=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(task,"before-dev.json")));var media=baseline.RootElement.GetProperty("groups").GetProperty("media").EnumerateArray().ToArray();Assert(media.Length==53,"Expected source manifest53; do not force count.");
        var hashes=media.Select(x=>x.GetProperty("Sha256").GetString()!).ToArray();var scanner=InquiryScannerFactory.Create(DefenderConfig(config,hashes),"Development");Assert((await scanner.HealthAsync()).Ready,"Real engine source-copy scan blocked.");
        var storage=StorageOptions.FromConfiguration(config);var copies=StorageOptions.ResolveWithin(storage.TempRoot,"source-scan-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(copies);var rows=new List<object>();
        foreach(var item in media)
        {
            var id=item.GetProperty("Id").GetGuid();var expected=item.GetProperty("Sha256").GetString()!;var key=item.GetProperty("StorageKey").GetString()!;var source=MediaService.ResolveMediaPath(storage,key);var copy=StorageOptions.ResolveWithin(copies,id.ToString("N")+Path.GetExtension(key));
            File.Copy(source,copy,false);File.SetAttributes(copy,File.GetAttributes(copy)|FileAttributes.ReadOnly);await using var stream=File.OpenRead(copy);Assert(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream))==expected,"Copy differs from source manifest.");stream.Position=0;
            var result=await scanner.ScanAsync(stream);stream.Position=0;Assert(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream))==expected,"Read-only copy mutated.");
            rows.Add(new{AssetId=id,Sha256=expected,Bytes=stream.Length,State=result.State.ToString(),result.Error,result.Provider,result.EngineVersion,result.SignatureVersion,result.SignatureUpdatedUtc,result.OutputSha256,OriginalStatus="NotScanned",AppliedToOriginal=false});
            await File.WriteAllTextAsync(Path.Combine(dir,"report.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,complete=false,files=rows,sourceModified=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        await using(var db=new PortalDbContext(options)){var ids=media.Select(x=>x.GetProperty("Id").GetGuid()).ToArray();Assert(await db.MediaAssets.CountAsync(x=>ids.Contains(x.Id)&&x.ScanStatus=="NotScanned")==53,"Source Test statuses changed.");}
        await File.WriteAllTextAsync(Path.Combine(dir,"report.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,complete=true,total=rows.Count,files=rows,readOnlyCopies=copies,sourceModified=false,appliedToDev=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Real engine source Test-copy report completed; original53 statuses not changed.");
    }
}
