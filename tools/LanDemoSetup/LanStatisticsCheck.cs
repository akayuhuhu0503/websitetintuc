using System.Text;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

static class LanStatisticsCheck
{
    public static async Task RunAsync(string root,IConfiguration config,PortalDbContext db,string[] args)
    {
        LanStatisticsSamples.Require(config,"Development");
        var offset=Array.IndexOf(args,"--report");var path=Path.GetFullPath(args[offset+1],root);if(offset<0||!path.StartsWith(Path.Combine(root,".local","evidence","lan-demo")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||File.Exists(path))throw new InvalidOperationException("Unique private LAN result required.");
        var user=await db.Users.SingleAsync(x=>x.UserName=="lan-ba-admin");var rights=new CurrentPermissionService(db);var actor=await rights.GetAsync(user.Id);
        var writer=await db.Users.SingleAsync(x=>x.UserName=="lan-ba-writer");var storage=StorageOptions.FromConfiguration(config);var service=new LandStatisticsService(db,storage,new MediaService(db,storage,new MediaUploadOptions(),config),rights,config);
        var before=await db.Set<LandStatisticsVersion>().CountAsync();var publicBefore=await db.Set<LandStatisticsVersion>().CountAsync(x=>x.IsCurrent);var checks=new List<string>();void Check(bool valid,string label){if(!valid)throw new InvalidOperationException(label);checks.Add(label);}
        Check(!(await rights.GetAsync(writer.Id)).Has(Permissions.ManageSite),"writer has no statistics import permission");
        try{await service.PreviewAsync(new MemoryStream(LanStatisticsSamples.Workbook()),"SAMPLE.xlsx",2026,"ha","TEST",writer.Id,"Development",default);throw new Exception("Writer allowed unexpectedly");}catch(UnauthorizedAccessException){checks.Add("unauthorized service import denied");}
        var scanner=InquiryScannerFactory.Create(config,"Development",LandStatisticsService.MaximumBytes);Check((await scanner.HealthAsync()).Ready,"real signed/pinned Defender healthy");
        InquiryScanResult control=new(InquiryScanState.NotScanned,"Not rerun in workflow-only mode",null);InquiryScanResult unknown=new(InquiryScanState.NotScanned,"Not rerun in workflow-only mode",null);
        if(!args.Contains("--workflow-only")){
            control=await scanner.ScanAsync(new MemoryStream(LanStatisticsSamples.MalwareControl));Check(control.State is InquiryScanState.Infected or InquiryScanState.ScanFailed,"real malware control blocked, never Clean");
            unknown=await scanner.ScanAsync(new MemoryStream(Encoding.UTF8.GetBytes("NONPUBLIC UNAPPROVED LAN BYTES")));Check(unknown.State==InquiryScanState.ScanFailed&&unknown.Error=="NotApprovedAnonymousLanSample","unknown bytes blocked before native/disk");
        }
        if(!actor.Has(Permissions.ManageSite))
        {
            var media=new MediaService(db,storage,new MediaUploadOptions(),config);
            var results=new List<object>();foreach(var invalid in new[]{false,true})
            {
                var bytes=LanStatisticsSamples.Workbook(invalid);var scan=await scanner.ScanAsync(new MemoryStream(bytes));Check(scan.State==InquiryScanState.Clean,"real signed Defender Clean for public XLSX sample");
                var testPath=Path.Combine(storage.TempRoot,"SAMPLE_TEST_"+Guid.NewGuid().ToString("N")+".xlsx");await File.WriteAllBytesAsync(testPath,bytes);OfficeDocumentPolicy.Validate(testPath,OfficeDocumentPolicy.Xlsx,media);var preview=LandStatisticsWorkbook.Read(testPath,true);
                Check(invalid?!preview.Valid&&preview.Issues.Any(x=>x.Row==2&&x.Column=="C"):preview.Valid&&preview.Rows.Count==34,"scanned sample format/parser result correct");results.Add(new{invalid,scan,preview.Issues});
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{status="PARTIAL_ACCOUNT_ONBOARDING",checks,checkCount=checks.Count,scannerAndParser=results,account=new{user.MustChangePassword,manageSiteActive=false},identityChanged=false,roleGrantsChanged=false,malwareControl=new{state=control.State.ToString(),control.Error,control.EngineVersion},draftImport="BLOCKED_ACCOUNT_ONBOARDING",officialStatisticsPublished=false},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("Real LAN scanner/parser checks PASS; admin draft import BLOCKED_ACCOUNT_ONBOARDING; no account reset/bypass.");return;
        }
        var bad=await service.PreviewAsync(new MemoryStream(LanStatisticsSamples.Workbook(true)),"SAMPLE_TEST_invalid.xlsx",2026,"ha","SAMPLE invalid",user.Id,"Development",default);
        Check(bad.Version==null&&!bad.Preview.Valid&&bad.Preview.Issues.Any(x=>x.Row==2&&x.Column=="C"),"real-scanned invalid workbook gives row2/C error, no dataset write");
        Check(await db.Set<LandStatisticsVersion>().CountAsync()==before,"invalid import leaves existing dataset intact");
        var good=await service.PreviewAsync(new MemoryStream(LanStatisticsSamples.Workbook()),"SAMPLE_TEST_valid.xlsx",2026,"ha","SAMPLE BA test only",user.Id,"Development",default);
        Check(good.Version!=null&&good.Preview.Valid&&good.Preview.Rows.Count==34,"real Clean workbook previews 34 provinces");
        var version=good.Version!;await service.ConfirmAsync(version.Id,user.Id,true,default);
        Check(await db.Set<LandStatisticsVersion>().AnyAsync(x=>x.Id==version.Id&&x.State=="Draft"&&!x.IsCurrent&&x.Source.StartsWith("SAMPLE/TEST LAN")),"admin confirmed SAMPLE draft, never official");
        try{await service.PublishAsync(version.Id,null,false,user.Id,"Development",default);throw new Exception("Sample publication allowed unexpectedly");}catch(InvalidOperationException){checks.Add("LAN sample publish endpoint service denied");}
        Check(await db.Set<LandStatisticsVersion>().CountAsync(x=>x.IsCurrent)==publicBefore,"public statistics unchanged");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{status="PASS",checks,checkCount=checks.Count,adminRightsVerified=true,identityChanged=false,serviceTestNotBrowserLogin=true,sampleDraftId=version.Id,cleanProof=JsonSerializer.Deserialize<JsonElement>(version.ScanProofJson),malwareControl=new{state=control.State.ToString(),control.Error,control.EngineVersion},unknownBytes=unknown.Error,officialStatisticsPublished=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("LAN statistics focused checks PASS; SAMPLE draft retained; no Identity/official/Dev changes.");
    }
}
