using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

var root=PortalConfiguration.FindWorkspace();var run=Path.GetFullPath(args.Single(),root);
if(!run.StartsWith(Path.Combine(root,".local","tests")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(run))throw new InvalidOperationException("New isolated Test directory required.");
Directory.CreateDirectory(run);var passed=new List<string>();void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);passed.Add(name);Console.WriteLine("PASS "+name);}
var valid=new List<string[]>{LandStatisticsWorkbook.Headers};valid.AddRange(LandStatisticsWorkbook.Provinces.Select(p=>new[]{p.Key,p.Value,"10","6","3","1"}));
string Fixture(string name,List<string[]> data){var path=Path.Combine(run,name+".xlsx");File.WriteAllBytes(path,InquiryExcelService.Workbook(data));return path;}
var good=Fixture("anonymous-technical-valid",valid);Check(LandStatisticsWorkbook.Read(good,true).Valid,"34 provinces / exact columns / totals");
var bad=valid.Select(x=>x.ToArray()).ToList();bad[2][2]="not-number";var invalid=Fixture("invalid-number",bad);var error=LandStatisticsWorkbook.Read(invalid,true);Check(!error.Valid&&error.Issues.Any(x=>x.Row==3&&x.Column=="C"),"row/column numeric error");
bad=valid.Select(x=>x.ToArray()).ToList();bad[1][2]="11";var mismatch=Fixture("total-mismatch",bad);Check(!LandStatisticsWorkbook.Read(mismatch,true).Valid&&LandStatisticsWorkbook.Read(mismatch,false).Valid,"BR18 Block/Warn");
bad=valid.Select(x=>x.ToArray()).ToList();bad[2][0]=bad[1][0];Check(!LandStatisticsWorkbook.Read(Fixture("duplicate",bad),true).Valid,"duplicate province rejected");
bad=valid.Select(x=>x.ToArray()).ToList();bad[0][2]="unexpected";Check(!LandStatisticsWorkbook.Read(Fixture("wrong-header",bad),true).Valid,"exact header required");
Check(!PublicTraffic.EligibleReader("Googlebot")&&!PublicTraffic.EligibleReader("")&&PublicTraffic.EligibleReader("Mozilla/5.0"),"bot / missing UA filtering");
var original=PortalConfiguration.Load();if(original["Sql:ExpectedDatabase"]!="LandPortal_Test")throw new InvalidOperationException("SQL Test only.");
var overrides=new Dictionary<string,string?>{["Scanner:Provider"]="DefenderTest",["Scanner:DefenderAnonymousTestOnly"]="true",["Scanner:DefenderAllowedTestHashes"]=string.Join(',',new[]{good,invalid}.Select(p=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))))),["Storage:MediaRoot"]=Path.Combine(run,"media"),["Storage:TempRoot"]=Path.Combine(run,"temp"),["Storage:KeysRoot"]=Path.Combine(run,"keys"),["LandStatistics:TotalMismatch"]="Block"};
foreach(var folder in new[]{"media","temp","keys"})Directory.CreateDirectory(Path.Combine(run,folder));
var config=new ConfigurationBuilder().AddConfiguration(original).AddInMemoryCollection(overrides).Build();var storage=StorageOptions.FromConfiguration(config);
await using var db=new PortalDbContext(DatabaseSettings.Options(config,forMigration:true));await SchemaManager.VerifyLocalProjectAsync(db,"LandPortal_Test");
var rights=new CurrentPermissionService(db);string? owner=null;foreach(var id in await db.Users.Select(x=>x.Id).Take(1000).ToListAsync()){var actor=await rights.GetAsync(id);if(actor.Has(Permissions.ManageSite)&&actor.Has(Permissions.PublishSite)){owner=id;break;}}
if(owner==null)throw new InvalidOperationException("BLOCKED no existing Test actor with ManageSite + PublishSite; no grants performed.");
var media=new MediaService(db,storage,new MediaUploadOptions(),config);var service=new LandStatisticsService(db,storage,media,rights,config);
try{await using var f=File.OpenRead(good);await service.PreviewAsync(f,"x.xlsx",2026,"ha","TECHNICAL FIXTURE ONLY", "not-an-account","Development",default);Check(false,"unauthorized import");}catch(UnauthorizedAccessException){Check(true,"endpoint service rights recheck");}
var before=await db.Set<LandStatisticsVersion>().CountAsync();var created=new List<Guid>();
var hash=Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(run)));
if(await db.Set<LandStatisticsVersion>().AnyAsync(x=>x.Year==2027&&x.IsCurrent))throw new InvalidOperationException("Test year occupied; no overwrite permitted.");
try
{
    await using(var f=File.OpenRead(invalid)){var result=await service.PreviewAsync(f,"bad.xlsx",2026,"ha","TECHNICAL FIXTURE ONLY",owner,"Development",default);Check(result.Version==null&&await db.Set<LandStatisticsVersion>().CountAsync()==before,"real Defender + invalid file writes zero dataset rows");}
    await using(var f=File.OpenRead(good)){var result=await service.PreviewAsync(f,"good.xlsx",2027,"ha","TECHNICAL FIXTURE ONLY",owner,"Development",default);Check(result.Preview.Valid&&result.Version!=null,"real Defender Clean + SQL immutable preview");created.Add(result.Version!.Id);File.WriteAllText(Path.Combine(run,"real-scan-proof.json"),result.Version.ScanProofJson);}
    var edition=created[0];await service.ConfirmAsync(edition,owner,true,default);Check(!await db.Set<LandStatisticsVersion>().AnyAsync(x=>x.Id==edition&&x.IsCurrent),"confirm leaves public data unchanged");
    try{await service.PublishAsync(edition,null,false,"not-an-account","Development",default);Check(false,"unauthorized publication");}catch(UnauthorizedAccessException){Check(true,"publish permission recheck");}
    try{await service.PublishAsync(edition,Guid.NewGuid(),true,owner,"Development",default);Check(false,"stale live pointer");}catch(InvalidOperationException){Check(!await db.Set<LandStatisticsVersion>().AnyAsync(x=>x.Id==edition&&x.IsCurrent),"stale revision publication rolls back");}
    await service.PublishAsync(edition,null,false,owner,"Development",default);Check(await db.Set<LandStatisticsVersion>().AnyAsync(x=>x.Id==edition&&x.IsCurrent&&x.State=="Published"),"exact confirmed revision publishes on SQL Test only");
    await using(var f=File.OpenRead(good)){var result=await service.PreviewAsync(f,"replacement.xlsx",2027,"ha","TECHNICAL FIXTURE REPLACEMENT ONLY",owner,"Development",default);created.Add(result.Version!.Id);}
    await service.ConfirmAsync(created[1],owner,true,default);
    Check(await db.Set<LandStatisticsVersion>().AnyAsync(x=>x.Id==edition&&x.IsCurrent),"new draft preserves previous public version");
    try{await service.PublishAsync(created[1],edition,false,owner,"Development",default);Check(false,"replace confirmation");}catch(InvalidOperationException){Check(true,"explicit replace confirmation required");}
    await service.PublishAsync(created[1],edition,true,owner,"Development",default);
    Check(await db.Set<LandStatisticsVersion>().CountAsync(x=>x.Year==2027&&x.IsCurrent)==1&&await db.Set<LandStatisticsVersion>().AnyAsync(x=>x.Id==edition&&!x.IsCurrent&&x.State=="Published"),"replace is atomic and retains previous immutable version");
    // No official fixture is published: transactionally exercise live-pointer queries and immediate withdrawal.
    var query=new PublicContentQuery(db,new MediaUploadOptions{IsDevelopmentLoopback=true});var discovery=new ArticleDiscovery(db,query);var article=await query.Published().FirstAsync(x=>x.Kind==ContentKind.Article);
    var traffic=new PublicTraffic(db,DataProtectionProvider.Create(new DirectoryInfo(storage.KeysRoot)),new MemoryCache(new MemoryCacheOptions()),NullLogger<PublicTraffic>.Instance,query);
    var now=DateTimeOffset.UtcNow;await traffic.RecordAsync(hash,now,article.Id);await traffic.RecordAsync(hash,now.AddMinutes(29),article.Id);
    Check(await db.AuditLogs.CountAsync(x=>x.Actor=="Reader:"+hash&&x.Action==ArticleDiscovery.ViewAction)==1,"SQL reader duplicate suppressed within 30 minutes");
    await traffic.RecordAsync(hash,now.AddMinutes(31),article.Id);Check(await db.AuditLogs.CountAsync(x=>x.Actor=="Reader:"+hash&&x.Action==ArticleDiscovery.ViewAction)==2,"SQL reader view allowed after 30 minutes");
    Check((await discovery.PopularAsync(7)).Count<=5&&(await discovery.PopularAsync(30)).Count<=5,"7/30 day ranking SQL execution");
    Check((await discovery.RelatedAsync(article,"đất đai, pháp luật")).Count<=5,"tag/category/recent SQL execution max 5");
    await using(var tx=await db.Database.BeginTransactionAsync())
    {
        var candidate=await query.Published().FirstAsync(x=>x.Kind==article.Kind&&x.Id!=article.Id);
        var uniqueTag="completion-"+Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RevisionMetadata SET Tags={uniqueTag} WHERE RevisionId={candidate.RevisionId}");
        Check((await discovery.RelatedAsync(article,uniqueTag)).First().Id==candidate.Id,"shared exact tag outranks category/recency");
        await db.Contents.Where(x=>x.Id==article.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsPublic,false));
        Check(!(await discovery.PopularAsync(7)).Any(x=>x.Id==article.Id),"withdrawn article immediately excluded");await tx.RollbackAsync();
    }
    await db.AuditLogs.Where(x=>x.Actor=="Reader:"+hash&&x.Action==ArticleDiscovery.ViewAction).ExecuteDeleteAsync();await db.Set<PublicVisit>().Where(x=>x.SessionHash==hash).ExecuteDeleteAsync();
}
finally{foreach(var id in created)await db.Set<LandStatisticsVersion>().Where(x=>x.Id==id).ExecuteDeleteAsync();await db.AuditLogs.Where(x=>x.Actor=="Reader:"+hash&&x.Action==ArticleDiscovery.ViewAction).ExecuteDeleteAsync();await db.Set<PublicVisit>().Where(x=>x.SessionHash==hash).ExecuteDeleteAsync();}
Check(await db.Set<LandStatisticsVersion>().CountAsync()==before,"technical dataset cleanup / no official data fabricated");
File.WriteAllText(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{status="PASS",checks=passed.Count,passed,officialStatisticsPublished=false,database="LandPortal_Test"},new JsonSerializerOptions{WriteIndented=true}));
