using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;
public static class MediaRecoveryTests
{
    static byte[]? scannedPng;static InquiryScanResult? scannedPngProof;static Guid? privateCleanId;
    static void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);}
    static (string Root,RecoveryInventory Inventory,string Sha,RecoveryProofSet Set) Input(string root)
    {
        using var pointer=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,".local/phase3c-current.json")));var folder=Path.Combine(root,pointer.RootElement.GetProperty("root").GetString()!);var p=Path.Combine(folder,"inventory/inventory.json");return(folder,JsonSerializer.Deserialize<RecoveryInventory>(File.ReadAllBytes(p))!,SourceInventory.Hash(File.ReadAllBytes(p)),JsonSerializer.Deserialize<RecoveryProofSet>(File.ReadAllBytes(Path.Combine(folder,"scan-resume/proof-set.json")))!);
    }
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(string root,DbContextOptions<PortalDbContext> options,IConfiguration config)
    {
        yield return("3C actual Defender anonymous Clean EICAR infected and real health cancellation fail closed",async()=>{
            using var bitmap=new SkiaSharp.SKBitmap(4,4);var nonce=Guid.NewGuid().ToByteArray();for(var i=0;i<16;i++)bitmap.SetPixel(i%4,i/4,new SkiaSharp.SKColor(nonce[i],(byte)i,27));using var encoded=bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);var png=encoded.ToArray();
            var zip=ScannerTests.EicarArchive();var scanner=InquiryScannerFactory.Create(ScannerTests.DefenderConfig(config,[ScannerTests.Hash(png),ScannerTests.Hash(zip)]),"Development");Check((await scanner.HealthAsync()).Ready,"Genuine engine unavailable: BLOCKED.");
            var clean=await scanner.ScanAsync(new MemoryStream(png));var infected=await scanner.ScanAsync(new MemoryStream(zip));using var cancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(50));var failed=await scanner.ScanAsync(new MemoryStream(png),cancel.Token);
            Check(clean.State==InquiryScanState.Clean&&infected.State==InquiryScanState.Infected&&failed.State==InquiryScanState.ScanFailed,"Genuine Clean/Infected/failure conclusions missing.");
            scannedPng=png;scannedPngProof=clean;
            await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root),"real-engine-controls.json"),new{clean,infected,failed,scope="Anonymous technical controls; not source records. Failure is actual health process cancellation, not a mock engine conclusion."});
        });
        yield return("3C durable resume exact engine policy expiry binding and no repeated scans; tampered policy denied",async()=>{
            var f=Input(root);await using var db=new PortalDbContext(options);var service=new MediaRecovery(db,StorageOptions.FromConfiguration(config),config,root);var count=await db.AuditLogs.CountAsync(x=>x.Action=="Phase3C.ScanProof"&&x.Actor==f.Inventory.JobId.ToString());var set=await service.RunAsync(f.Inventory,f.Sha);Check(set.Proofs.Count==60&&await db.AuditLogs.CountAsync(x=>x.Action=="Phase3C.ScanProof"&&x.Actor==f.Inventory.JobId.ToString())==count,"Resume rescanned valid proofs.");
            var proofs=await service.ValidateSetAsync(f.Inventory,f.Sha,set);Check(proofs.All(x=>x.State=="Clean"&&x.ExpiresUtc>DateTimeOffset.UtcNow),"Real source proofs invalid.");
            f.Inventory.Policy="UNAPPROVED_POLICY";try{await service.ValidateSetAsync(f.Inventory,f.Sha,set);throw new Exception("Policy bypass");}catch(InvalidDataException){}
            await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root),"resume.json"),new{f.Inventory.JobId,Proofs=set.Proofs.Count,NewNativeScanReceipts=0});
        });
        yield return("3C safe apply verify repeat and publication rollback actual Test backup/operator guard; no Dev authorization",async()=>{
            var input=Input(root);var f=await BatchImportTests.Fixture(root);Check(scannedPng!=null&&scannedPngProof?.State==InquiryScanState.Clean,"Genuine anonymous photo proof required, no Clean mock.");
            var original=f.State.Captures[0];var privateRoot=Path.Combine(root,".local/phase3a",f.State.Id.ToString("N"));var pngHash=SourceInventory.Hash(scannedPng!);var photoUrl=SourceCatalog.BaseUrl+"/Image/technical-"+pngHash+".png";
            var raw=(await File.ReadAllTextAsync(Path.Combine(privateRoot,original.Key))).Replace("</article>","<img src='"+photoUrl+"'></article>");var rawBytes=System.Text.Encoding.UTF8.GetBytes(raw);var rawHash=SourceInventory.Hash(rawBytes);var rawKey="responses/"+rawHash+".bin";await File.WriteAllBytesAsync(Path.Combine(privateRoot,rawKey),rawBytes);
            var item=f.State.Items[0];item.Source=SourceParser.Parse(raw,item.Source.SourceUrl,ContentKind.Article,item.Source.SourceId);item.Source.CategoryIds=[75];item.Source.PrimaryCategoryId=75;item.RawSha256=rawHash;f.State.Captures[0]=original with{Key=rawKey,Sha256=rawHash,Bytes=rawBytes.Length};
            var photoKey="quarantine/"+pngHash+".bin";Directory.CreateDirectory(Path.Combine(privateRoot,"quarantine"));await File.WriteAllBytesAsync(Path.Combine(privateRoot,photoKey),scannedPng!);
            f.State.Captures.Add(new(photoUrl,photoUrl,200,"image/png",photoKey,pngHash,scannedPng!.Length,DateTimeOffset.UtcNow,"TechnicalFixtureGenuineScan"));f.State.Urls.Add(new(){Url=photoUrl,Role="File",CaptureSha=pngHash,Mime="image/png",FormatStatus="Validated",ScanStatus="Clean",ScannedSha256=pngHash,ScanEngine=scannedPngProof!.EngineVersion,ScannedUtc=DateTimeOffset.UtcNow});
            using var packed=new MemoryStream();packed.Write("LP3A-BR1"u8);using(var br=new BrotliStream(packed,CompressionLevel.Fastest,true))await JsonSerializer.SerializeAsync(br,f.State,InventoryLimits.Json);var bytes=packed.ToArray();var hash=SourceInventory.Hash(bytes);var path=Path.Combine(privateRoot,"checkpoints",hash+".bin");Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,bytes);
            SourceBatchPlan plan;await using(var db=new PortalDbContext(options))plan=await new SourceBatch(db,StorageOptions.FromConfiguration(config),config,root,f.State,hash).PreviewAsync();var folder=TestEvidencePaths.At(root,"safe-apply");Directory.CreateDirectory(folder);var planPath=Path.Combine(folder,"plan.json");var sha=await MediaRecovery.WriteNewAsync(planPath,plan);
            var common=new[]{"--checkpoint",path,"--checkpoint-sha",hash,"--plan",planPath,"--plan-sha",sha,"--confirm-test",sha,"--backup-receipt",Path.Combine(input.Root,"backup-LandPortal_Test.json")};
            var materialized=MediaService.ResolveMediaPath(StorageOptions.FromConfiguration(config),"phase3b/"+pngHash+".png");
            await using(var db=new PortalDbContext(options)){var outcome=await new SourceBatch(db,StorageOptions.FromConfiguration(config),config,root,f.State,hash).CommitAsync(plan,sha,beforeCommit:_=>throw new IOException("TECHNICAL TEST interruption after file materialization"));Check(outcome.Single().Status=="Failed"&&!await db.MediaAssets.AnyAsync(x=>x.Sha256==pngHash)&&File.Exists(materialized)&&SourceInventory.Hash(await File.ReadAllBytesAsync(materialized))==pngHash,"SQL rollback/filesystem journal behavior incorrect.");}
            foreach(var (command,label) in new[]{("safe-apply","first"),("safe-verify","verify"),("safe-apply","repeat"),("safe-rollback","rollback"),("safe-rollback","rollback-repeat")}){
                await using var db=new PortalDbContext(options);var code=await SafeBatchCommand.ExecuteAsync([command,..common,"--report",Path.Combine(folder,label)],root,config,db);Check(code==0,"Safe command failed: "+label);
            }
            await using(var db=new PortalDbContext(options)){var record=await db.SourceRecords.SingleAsync(x=>x.SourceId==f.State.Items[0].Source.SourceId);var c=await db.Contents.SingleAsync(x=>x.Id==record.ContentId);Check(!c.IsPublic&&c.PublishedRevisionId==null&&await db.Revisions.CountAsync(x=>x.ContentId==c.Id)==1,"Rollback deleted or exposed content.");var media=await db.MediaAssets.SingleAsync(x=>x.Sha256==pngHash);privateCleanId=media.Id;Check(File.Exists(materialized)&&SourceInventory.Hash(await File.ReadAllBytesAsync(materialized))==pngHash,"Rollback removed exact original test bytes.");}
            await MediaRecovery.WriteNewAsync(Path.Combine(folder,"filesystem.json"),new{pngHash,materialized,SqlFaultLeftFilePrivate=true,RetryMaterializedOnce=true,RollbackRetainedFileAndRevision=true,privateCleanId,Proof=scannedPngProof});
            var deniedConfig=new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{{"Sql:ExpectedDatabase","LandPortal_Dev"},{"SafeImport:ApprovedSignerThumbprints",""}}).Build();plan.Database="LandPortal_Dev";
            await using(var db=new PortalDbContext(options)){try{await ApprovedBatchExecution.CreateAsync(db,deniedConfig,root,plan,sha,"Apply",Path.Combine(root,".local/approvals/phase3c/not-approved.ps1"),Path.Combine(input.Root,"backup-LandPortal_Test.json"));throw new Exception("Dev accepted unsigned approval");}catch(UnauthorizedAccessException){}}
        });
        foreach(var c in BatchImportTests.Cases(root,options,config).Where(x=>x.Name.StartsWith("3B SQL fault")||x.Name.StartsWith("3B SQL changed")))yield return c;
    }
    public static async Task HttpAsync(string root,IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        var input=Input(root);var folder=TestEvidencePaths.At(root,"http");Directory.CreateDirectory(folder);const string url="http://127.0.0.1:5303";
        var start=new ProcessStartInfo(Path.Combine(root,".local/dotnet/dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(TestRuntimePaths.Assembly(root,"Web"));start.ArgumentList.Add("--contentRoot");start.ArgumentList.Add(Path.Combine(root,"src/Web"));start.Environment["DOTNET_ENVIRONMENT"]="Development";start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";start.Environment["Public__BaseUrl"]=url;start.Environment["Mail__Transport"]="Disabled";start.Environment["Media__AllowUnscannedDevelopmentUploads"]="false";
        using var web=Process.Start(start)!;var stdout=web.StandardOutput.ReadToEndAsync();var stderr=web.StandardError.ReadToEndAsync();using var guest=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new(url),Timeout=TimeSpan.FromSeconds(30)};var checks=new List<object>();
        try{
            var ready=false;for(var i=0;i<100;i++){try{using var r=await guest.GetAsync("/health");if(r.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(200);}Check(ready,"Owned web unavailable.");
            await using var db=new PortalDbContext(options);foreach(var resource in input.Inventory.Resources){var m=await db.MediaAssets.SingleAsync(x=>x.Sha256==resource.Sha256);Check(m.ScanStatus=="Clean"&&!m.IsQuarantined,"Scanned Test asset status differs.");var grant=await db.ContentAssets.AnyAsync(a=>a.MediaAssetId==m.Id&&db.Contents.Any(c=>c.IsPublic&&!c.IsTrashed&&c.PublishedRevisionId==a.RevisionId));using var get=await guest.GetAsync("/media/"+m.Id);
                if(grant){Check(get.IsSuccessStatusCode&&SourceInventory.Hash(await get.Content.ReadAsByteArrayAsync())==m.Sha256,"Exact public bytes failed.");if(m.MimeType=="application/pdf"){using var request=new HttpRequestMessage(HttpMethod.Get,"/media/"+m.Id);request.Headers.Range=new RangeHeaderValue(0,31);using var range=await guest.SendAsync(request);Check(range.StatusCode==HttpStatusCode.PartialContent,"PDF range failed.");}}
                else Check(get.StatusCode==HttpStatusCode.NotFound,"Clean private media leaked.");checks.Add(new{m.Id,resource.IsProtectedSource,m.MimeType,grant,status=(int)get.StatusCode});}
            var blocked=await db.MediaAssets.FirstAsync(x=>x.ValidationStatus==SourceBatch.AssetValidation&&x.ScanStatus=="NotScanned");using(var denied=await guest.GetAsync("/media/"+blocked.Id))Check(denied.StatusCode==HttpStatusCode.NotFound,"NotScanned exposed.");using(var denied=await guest.GetAsync("/admin/media/"+blocked.Id+"/preview"))Check(denied.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized,"Admin ACL bypass.");
            Check(privateCleanId!=null,"Genuine private fixture missing.");using(var denied=await guest.GetAsync("/media/"+privateCleanId))Check(denied.StatusCode==HttpStatusCode.NotFound,"Clean rollback/orphan file exposed without public grant.");
            foreach(var id in new[]{"3349","3681","231","1780"}){var r=await db.SourceRecords.FirstAsync(x=>x.Source==SourceBatch.Source&&x.SourceId==id);using var page=await guest.GetAsync("/bai-viet/"+r.ContentId);Check(page.IsSuccessStatusCode,"Affected content smoke failed "+id);}
            using(var page=await guest.GetAsync("/chuyen-muc/xay-dung-pho-bien-chinh-sach-phap-luat"))Check(page.IsSuccessStatusCode,"Category failed.");
        }finally{if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(folder,"web.log"),await stdout+"\n"+await stderr);await MediaRecovery.WriteNewAsync(Path.Combine(folder,"http.json"),new{checks,ownedWebStopped=true});}
    }
}
