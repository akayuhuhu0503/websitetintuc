using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using LandPortal.WeeklyImport;
using Microsoft.EntityFrameworkCore;

static class PlanningChecks
{
    public static async Task RunAsync(PlanningPackage source, WebCmsClient cms, WebCmsClient outsider, DbContextOptions<PortalDbContext> options,
        MediaUploadOptions policy, StorageOptions storage, Dictionary<string,(string Id,string Password)> accounts, string run,
        Action<bool,string> check, Func<Func<Task>,string,string?,Task> denied, Action<string?> lose)
    {
        check(source.Rows.Count==32 && source.Rows.Sum(x=>x.Files.Count(f=>f.Pages.HasValue))==33 && source.Rows.All(x=>x.Files.Count>0),"QHKHSDD 32 records / 33 top-level PDFs / no missing STT");
        check(source.Rows.Where(x=>x.CanImport).Select(x=>x.Stt).SequenceEqual(new[]{7,8,9,10,11,13,22,27,31}),"Nine records pass current source file guards; 23 held without weakening CMS validation");
        check(source.Rows.Single(x=>x.Stt==24).Files.Count==2 && source.Rows.Single(x=>x.Stt==24).PageFormula,"STT24 has two PDFs and preserved E30 formula");
        var dates=WeeklyDates.Inspect(source.Dates);
        check(dates.Count(x=>x.Status=="VerifiedText")==20 && dates.Count(x=>x.Status=="NeedsConfirmation")==12,"20 strict dd/MM dates; 11 serials and malformed text require confirmation");
        check(dates.Single(x=>x.Stt==1).StoredDate=="2026-06-01" && dates.Single(x=>x.Stt==1).SwappedCandidate=="2026-01-06","Serial B7 exposes both interpretations without selecting one");
        check(dates.Single(x=>x.Stt==31).StoredDate==null && dates.Single(x=>x.Stt==31).SwappedCandidate=="2026-12-07","Double slash date is a candidate only, never auto corrected");
        var decisions=new DateDecisions(source.Dates.ZipSha256,source.Dates.WorkbookSha256,dates.Where(x=>x.Status=="NeedsConfirmation").Select(x=>new DateDecision(x.Stt,x.SwappedCandidate??x.StoredDate,"Synthetic test only; no real source approval")).ToArray()){SheetName=source.Dates.SheetName};
        await denied(()=>Task.Run(()=>WeeklyDates.Resolve(source.Dates,decisions with{SheetName=null})),"Weekly approval cannot authorize QHKHSDD even with same workbook hash",null);
        await denied(()=>Task.Run(()=>WeeklyDates.Resolve(source.Dates,decisions with{Rows=[new(1,"2026-11-17","Test")]})),"Date outside column B candidates refused",null);
        check(WeeklyDates.Resolve(source.Dates,decisions).Count==32,"Confirmed test dates resolve all32 to ISO, no title/PDF input");
        var nav=PublicNavigation.Fallback([new(48,null,"News planning","quy-hoach-ke-hoach-su-dung-dat",0)]);
        var menu=nav.Items.Single(x=>x.Url==PlanningPackage.Route);
        check(nav.Items.Single(x=>x.Id==menu.ParentId).Label=="Văn bản" && menu.Label==PlanningPackage.Label && nav.Items.Any(x=>x.Url=="/chuyen-muc/quy-hoach-ke-hoach-su-dung-dat"),"Document route77 under Van ban, news48 remains distinct");
        // One synthetic Test-only record combines two unchanged, independently valid source PDFs.
        // The real STT24 remains BLOCKED by the CMS structural guard, never rewritten or presented as imported.
        var originalSource=source;
        var pilot=source.Rows.Single(x=>x.Stt==7);
        var fixtureFiles=pilot.Files.Concat(source.Rows.Single(x=>x.Stt==9).Files).ToArray();
        source=new PlanningPackage { Path=source.Path, Dates=source.Dates, Archives=source.Archives, Scope=source.Scope,
            Rows=source.Rows.Select(x=>x.Stt==7 ? x with { Source=x.Source with { Title="TEST ONLY — QHKHSDD two PDF attachments" }, Files=fixtureFiles }
            : x with { Blocks=["Test-only nonpilot record; never imported"] }).ToArray() };
        var importer=new PlanningWebImporter(cms); await importer.PreflightAsync();
        await denied(()=>new PlanningWebImporter(outsider).PreflightAsync(),"Insufficient account cannot create or upload",null);
        var plan=await importer.PlanAsync(source,decisions);
        check(plan.Rows.Count(x=>x.Action=="Create")==1 && plan.Rows.Count(x=>x.Action=="Blocked")==31,"Read-only test plan selects exactly one permitted multi-PDF fixture");
        var journal=new PlanningJournal(PlanningWebImporter.PlanHash(plan),[]);var path=Path.Combine(run,"planning-journal.json");
        await denied(()=>importer.ImportAsync(source,plan,journal,path,decisions,true),"Batch blocked until one pilot verified",null);
        await denied(()=>importer.ImportAsync(source,plan,journal,path,decisions,false,1),"Oversized STT cannot create incomplete draft",null);
        lose("save");await denied(()=>importer.ImportAsync(source,plan,journal,path,decisions,false,7),"Lost create response retained for idempotent resume","CMS HTTP 503");
        await using var db=new PortalDbContext(options);
        check(await db.Contents.CountAsync()==1 && !await db.MediaAssets.AnyAsync(),"One isolated draft, no duplicate/no file yet");
        journal=JsonSerializer.Deserialize<PlanningJournal>(await File.ReadAllBytesAsync(path))!;
        lose("upload");await denied(()=>importer.ImportAsync(source,plan,journal,path,decisions,false,7),"Lost PDF response reconciled by CMS upload hash receipt","CMS HTTP 503");
        journal=JsonSerializer.Deserialize<PlanningJournal>(await File.ReadAllBytesAsync(path))!;
        lose("save");await denied(()=>importer.ImportAsync(source,plan,journal,path,decisions,false,7),"Both PDFs attached before lost save response","CMS HTTP 503");
        journal=JsonSerializer.Deserialize<PlanningJournal>(await File.ReadAllBytesAsync(path))!;
        await importer.ImportAsync(source,plan,journal,path,decisions,false,7);await importer.ReconcileAsync(source,plan,journal,decisions);
        var content=await db.Contents.AsNoTracking().SingleAsync();var revision=await db.Revisions.AsNoTracking().SingleAsync(x=>x.Id==content.CurrentRevisionId);
        check(content.State==WorkflowState.Draft&&!content.IsPublic&&revision.Metadata.PublicationDate==new DateOnly(2026,3,26)&&revision.PrimaryCategoryId==77,"Draft preserves exact confirmed Excel date, category77, no publication");
        check(await db.MediaAssets.CountAsync()==2&&await db.DraftAssets.CountAsync()==2&&await db.Revisions.CountAsync()==2,"Exactly two PDF attachments, one draft and two revisions");
        var files=source.Rows.Single(x=>x.Stt==7).Files;
        var assets=await db.MediaAssets.AsNoTracking().ToListAsync();
        check(assets.All(x=>x.ScanStatus=="NotScanned"&&files.Any(f=>f.Name==x.OriginalName&&f.Sha256==x.Sha256&&f.Bytes==x.ByteLength)),"PDF names, bytes, hashes and truthful NotScanned preserved");
        await importer.ImportAsync(source,plan,journal,path,decisions,false,7);
        check(await db.Contents.CountAsync()==1&&await db.MediaAssets.CountAsync()==2&&await db.Revisions.CountAsync()==2,"Repeat pilot zero delta; no duplicate upload");
        check((await importer.PlanAsync(source,decisions)).Rows.Single(x=>x.Stt==7).Action=="Reuse","Matching complete multi-PDF record reused");
        var links=await importer.SyncAsync(source,plan,journal,decisions);check(links.All(x=>x.Url==null),"Draft has no exported public URL");
        await denied(()=>Task.Run(()=>importer.Validate(source,plan with{Rows=plan.Rows.Select(x=>x.Stt==7?x with{Date="2026-11-17"}:x).ToArray()},journal,decisions)),"Changed date rejects replay of old plan",null);
        var rights=new CurrentPermissionService(db);var workflow=new WorkflowService(db,new HtmlPolicy(),policy,storage);
        var writer=await rights.GetAsync(accounts["writer"].Id);var reviewer=await rights.GetAsync(accounts["reviewer"].Id);var publisher=await rights.GetAsync(accounts["publisher"].Id);
        var submitted=await workflow.SubmitAsync(content.Id,content.CurrentRevisionId,writer,content.RowVersion,Guid.NewGuid());
        var approved=await workflow.ApproveAsync(content.Id,submitted.CurrentRevisionId!.Value,reviewer,submitted.RowVersion,Guid.NewGuid());
        await workflow.PublishApprovedAsync(content.Id,approved.CurrentRevisionId!.Value,publisher,approved.RowVersion,Guid.NewGuid());
        links=await importer.SyncAsync(source,plan,journal,decisions);
        await WeeklyImporter.AtomicJson(Path.Combine(run,"planning-public-links.json"),links);
        check(links.Count(x=>x.Url!=null)==1&&links.Single(x=>x.Stt==7).PublishedRevisionId==approved.CurrentRevisionId,"After test-only review/publication, real public date+both PDF bytes verified");
        var listing=await cms.HtmlAsync(PlanningPackage.Route,true);
        check(listing.QuerySelector("h1")?.TextContent==PlanningPackage.Label&&listing.QuerySelector(".news-card .item-meta")?.TextContent.Contains("26/03/2026")==true,"Existing planning listing shows normalized Excel date");
        var output=Path.Combine(run,"planning-links.xlsx");originalSource.WriteWorkbook(output,links.ToDictionary(x=>x.Stt,x=>x.Url));
        using(var original=new ZipArchive(new MemoryStream(source.Dates.Workbook)))using(var updated=ZipFile.OpenRead(output))
        {
            var s=WeeklyPackage.S;var a=WeeklyPackage.Xml(WeeklyPackage.ReadEntry(original.GetEntry(source.Dates.SheetPath)!));var b=WeeklyPackage.Xml(WeeklyPackage.ReadEntry(updated.GetEntry(source.Dates.SheetPath)!));
            bool same=true;for(var n=7;n<=38;n++)foreach(var col in new[]{"A","B","C"})same&=XNode.DeepEquals(a.Descendants(s+"c").Single(x=>(string?)x.Attribute("r")==col+n),b.Descendants(s+"c").Single(x=>(string?)x.Attribute("r")==col+n));
            check(same,"All32 Excel A/B/C values, type and style unchanged");
            check(a.Descendants(s+"c").Where(x=>x.Element(s+"f")!=null).All(x=>XNode.DeepEquals(x,b.Descendants(s+"c").Single(y=>(string?)y.Attribute("r")== (string?)x.Attribute("r")))),"All formulas including E6/E30 preserved");
            var relPath=Path.GetDirectoryName(source.Dates.SheetPath)!.Replace('\\','/')+"/_rels/"+Path.GetFileName(source.Dates.SheetPath)+".rels";
            check(original.Entries.Where(x=>x.FullName!=source.Dates.SheetPath&&x.FullName!=relPath).All(x=>WeeklyPackage.ReadEntry(x).SequenceEqual(WeeklyPackage.ReadEntry(updated.GetEntry(x.FullName)!))),"All other sheets/styles/ZIP entries byte identical; only target sheet hyperlink relations updated");
            var originalRels=WeeklyPackage.Xml(WeeklyPackage.ReadEntry(original.GetEntry(relPath)!));var updatedRels=WeeklyPackage.Xml(WeeklyPackage.ReadEntry(updated.GetEntry(relPath)!));
            check(originalRels.Root!.Elements().All(e=>updatedRels.Root!.Elements().Any(n=>XNode.DeepEquals(e,n)))&&a.Descendants(s+"hyperlink").Where(h=>!((string?)h.Attribute("ref")??"").StartsWith('D')).All(h=>b.Descendants(s+"hyperlink").Any(n=>XNode.DeepEquals(h,n))),"Original source hyperlink C16 and unrelated relationships preserved");
            check(b.Descendants(s+"hyperlink").Where(h=>((string?)h.Attribute("ref")??"").StartsWith('D')).Single().Attribute("ref")?.Value=="D13","Hyperlink only for published test STT7, correct row");
        }
        db.ChangeTracker.Clear();content=await db.Contents.AsNoTracking().SingleAsync();await workflow.UnpublishAsync(content.Id,publisher,content.RowVersion,Guid.NewGuid());
        check((await importer.SyncAsync(source,plan,journal,decisions)).All(x=>x.Url==null),"Withdrawn content never exported as public");
        db.ChangeTracker.Clear();content=await db.Contents.AsNoTracking().SingleAsync();var editing=await workflow.BeginEditAsync(content.Id,writer,content.RowVersion,Guid.NewGuid());
        await workflow.SaveAsync(content.Id,new DraftInput{Kind=ContentKind.StaticPage,Title=source.Rows.Single(x=>x.Stt==7).Source.Title,PrimaryCategoryId=77,BodyHtml="<p>Test editor removed attachments.</p>",Metadata=new(){PublicationDate=new DateOnly(2026,3,26)}},writer,editing.RowVersion,Guid.NewGuid());
        await denied(()=>importer.ImportAsync(source,plan,journal,path,decisions,false,7),"Human removal not overwritten or reattached",null);
        check(!await db.WeeklyScheduleReleases.AnyAsync()&&!File.ReadAllText(path).Contains(accounts["writer"].Password)&&!File.ReadAllText(path).Contains("__RequestVerificationToken"),"No weekly release, stored password, cookie or CSRF");
    }
}
