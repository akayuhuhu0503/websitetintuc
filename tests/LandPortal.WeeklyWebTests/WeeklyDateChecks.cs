using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.WeeklyImport;
using Microsoft.EntityFrameworkCore;

static class WeeklyDateChecks
{
    public static void PlanGuards(WeeklyPackage source)
    {
        var dates=WeeklyDates.Resolve(source,null);
        var plan=new WebDatePlan("https://example.invalid/","fixture",source.ZipSha256,source.WorkbookSha256,
            WeeklyPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(dates.OrderBy(x=>x.Key))),
            source.Rows.Select(x=>new DatePlanRow(x.Stt,null,null,dates.GetValueOrDefault(x.Stt),"Fixture",null)).ToArray());
        WebDateSync.ValidateDateChoices(source,null,plan);Console.WriteLine("PASS exact confirmed choices bound to plan");
        void Reject(WebDatePlan altered,string name){try{WebDateSync.ValidateDateChoices(source,null,altered);}catch(InvalidDataException){Console.WriteLine("PASS "+name);return;}throw new Exception("NOT DENIED "+name);}
        Reject(plan with { Rows=plan.Rows.Select(x=>x.Stt==1?x with{Date="2026-01-05"}:x).ToArray() },"unconfirmed numeric date injected into plan denied");
        Reject(plan with { Rows=plan.Rows.Select(x=>x.Stt==3?x with{Date="2026-01-20"}:x).ToArray() },"verified text date altered in plan denied");
        Reject(plan with {DatesHash="wrong"},"mismatched date-set hash denied");
        Console.WriteLine("RESULT 4 PASS; offline plan guards; no HTTP/SQL");
    }
    public static async Task RunAsync(WeeklyPackage source, WebCmsClient cms, DbContextOptions<PortalDbContext> options,
        MediaUploadOptions policy, StorageOptions storage, Dictionary<string,(string Id,string Password)> accounts, string run,
        Action<bool,string> check, Func<Func<Task>,string,string?,Task> denied, Action<string> lose)
    {
        Task Reject(Func<Task> action,string name,string? expected=null)=>denied(action,name,expected);
        var report=WeeklyDates.Inspect(source); var originalSha=WeeklyPackage.Hash(source.Workbook);
        check(report.Count==43 && report.Count(x=>x.CellType=="n")==17 && report.Count(x=>x.Status=="VerifiedText")==24,"Dates: all43 / 17 serials / 24 clear text dates");
        check(report[0].StoredDate=="2026-05-01" && report[0].SwappedCandidate=="2026-01-05" && report[0].Status=="NeedsConfirmation","Serial46143 reports both interpretations, does not assume January");
        check(report[4].Status=="NeedsConfirmation" && report[4].StoredDate==report[4].SwappedCandidate,"Even symmetric numeric date requires source confirmation");
        check(report[12].StoredDate=="2026-04-06" && report[12].Status=="VerifiedText","Vietnamese text with leading whitespace parsed day/month, raw unchanged");
        check(report.Skip(38).Take(4).All(x=>x.Status=="NeedsConfirmation") && report[42].ProposedDate=="2026-11-04","39-42 title/PDF conflicts held; 43 not replaced by title week start");
        check(!WeeklyDates.TryVietnamese("31/02/2026",out _) && !WeeklyDates.TryVietnamese("2026-01-05",out _) && WeeklyDates.TryVietnamese("6/4/2026",out var vi)&&vi==new DateOnly(2026,4,6),"Strict Vietnamese text parsing rejects impossible/other formats");
        var decisions=new DateDecisions(source.ZipSha256,source.WorkbookSha256,[new(1,"2026-01-05","Synthetic fixture only")]);
        check(!WeeklyDates.Resolve(source,null).ContainsKey(1)&&WeeklyDates.Resolve(source,decisions)[1]=="2026-01-05","Unconfirmed serial blocked; bound explicit decision resolves it");
        await Reject(()=>Task.Run(()=>WeeklyDates.Resolve(source,decisions with{ZipSha256="wrong"})),"Wrong-source confirmation rejected");
        await Reject(()=>Task.Run(()=>WeeklyDates.Resolve(source,decisions with{Rows=[new(1,"2026-01-05",null)]})),"Date confirmation without person rejected");
        await Reject(()=>Task.Run(()=>WeeklyDates.Resolve(source,decisions with{Rows=[new(1,"2026-01-05","a"),new(1,"2026-05-01","b")]})),"Duplicate STT decisions rejected");

        var importer=new WebWeeklyImporter(cms);var plan=await importer.PlanAsync(source);
        var imported=new WebJournal(plan.Origin,plan.UserId,plan.ZipSha256,plan.WorkbookSha256,[]);var importPath=Path.Combine(run,"web-journal.json");
        await Reject(()=>importer.ImportAsync(source,plan,imported,importPath,false),"New import refuses unresolved first-row date before any write","chưa được xác nhận");
        await using var db=new PortalDbContext(options);
        check(!await db.Contents.AnyAsync()&&!await db.MediaAssets.AnyAsync(),"Unconfirmed date produces zero content/media writes");
        await importer.ImportAsync(source,plan,imported,importPath,false,decisions);
        var item=imported.Rows.Single();var id=item.ContentId!.Value;
        check((await db.ContentDrafts.AsNoTracking().SingleAsync()).Metadata.PublicationDate==new DateOnly(2026,1,5),"New HTTP import sets exact confirmed PublicationDate");
        var permissions=new CurrentPermissionService(db);var workflow=new WorkflowService(db,new HtmlPolicy(),policy,storage);
        var writer=await permissions.GetAsync(accounts["writer"].Id);var reviewer=await permissions.GetAsync(accounts["reviewer"].Id);var publisher=await permissions.GetAsync(accounts["publisher"].Id);
        async Task<Content> Current(){db.ChangeTracker.Clear();return await db.Contents.AsNoTracking().SingleAsync();}
        async Task<DraftInput> Input()
        {
            db.ChangeTracker.Clear();var d=await db.ContentDrafts.AsNoTracking().Include(x=>x.Assets).Include(x=>x.Categories).SingleAsync();
            return new(){Kind=ContentKind.StaticPage,Title=d.Title,Summary=d.Summary,BodyHtml=d.BodyHtml,Author=d.Author,SourceCredit=d.SourceCredit,PrimaryCategoryId=d.PrimaryCategoryId,
                Metadata=d.Metadata.Copy(),CoverAssetId=d.CoverAssetId,SecondaryCategoryIds=d.Categories.Where(x=>x.CategoryId!=d.PrimaryCategoryId).Select(x=>x.CategoryId).ToList(),
                Assets=d.Assets.Select(x=>new AssetSelection{MediaAssetId=x.MediaAssetId,Usage=x.Usage,Caption=x.Caption,SortOrder=x.SortOrder}).ToList()};
        }
        async Task Publish()
        {
            var c=await Current();var submit=await workflow.SubmitAsync(id,c.CurrentRevisionId,writer,c.RowVersion,Guid.NewGuid());
            var approve=await workflow.ApproveAsync(id,submit.CurrentRevisionId!.Value,reviewer,submit.RowVersion,Guid.NewGuid());
            await workflow.PublishApprovedAsync(id,approve.CurrentRevisionId!.Value,publisher,approve.RowVersion,Guid.NewGuid());
        }
        // Emulate an older imported record; isolated SQL only through workflow.
        var input=await Input();input.Metadata.PublicationDate=null;input.Metadata.Unit="Đơn vị giữ nguyên";input.Metadata.MappingStatus="Reviewed";
        input.Summary="Giữ mô tả";input.Author="Tác giả thử";input.SourceCredit="Nguồn thử";input.Assets.Single().Caption="Nhãn PDF giữ nguyên";
        var current=await Current();await workflow.SaveAsync(id,input,writer,current.RowVersion,Guid.NewGuid());
        var beforeMetadata=JsonSerializer.Serialize(input.Metadata); var mediaBefore=await db.MediaAssets.AsNoTracking().SingleAsync();
        var sync=new WebDateSync(cms);
        var datePlan=await sync.PlanAsync(source,plan,imported,decisions);
        check(datePlan.Rows[0].Status=="Ready" && datePlan.Rows.Count(x=>x.Status=="NeedsConfirmation")==18,"Read-only date plan separates ready draft and held dates");
        WebDateJournal Journal(WebDatePlan p)=>new(WeeklyPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(p,WeeklyPackage.Json)),[]);
        var journal=Journal(datePlan);var journalPath=Path.Combine(run,"dates-draft.json");
        lose("save");await Reject(()=>sync.ApplyAsync(source,plan,imported,decisions,datePlan,journal,journalPath),"Lost date-save response stops safely","CMS HTTP 503");
        var revisions=await db.Revisions.CountAsync();journal=JsonSerializer.Deserialize<WebDateJournal>(await File.ReadAllBytesAsync(journalPath))!;
        await sync.ApplyAsync(source,plan,imported,decisions,datePlan,journal,journalPath);
        var after=await Input();var date=after.Metadata.PublicationDate;after.Metadata.PublicationDate=null;
        check(date==new DateOnly(2026,1,5)&&JsonSerializer.Serialize(after.Metadata)==beforeMetadata,"Date-only save preserves every metadata property including fields absent from form");
        check(after.Summary==input.Summary&&after.Author==input.Author&&after.SourceCredit==input.SourceCredit&&after.BodyHtml==input.BodyHtml&&after.Assets.Single().Caption==input.Assets.Single().Caption,"All article fields, PDF caption and body preserved");
        await sync.ApplyAsync(source,plan,imported,decisions,datePlan,journal,journalPath);
        check(await db.Revisions.CountAsync()==revisions&&await db.MediaAssets.CountAsync()==1&&await db.Contents.CountAsync()==1,"Resume and repeated date-sync: zero duplicate revisions/content/uploads");
        check(!(await Current()).IsPublic && !await db.WeeklyScheduleReleases.AnyAsync(),"Date sync never publishes or creates weekly releases");
        var unchanged=await sync.PlanAsync(source,plan,imported,decisions);check(unchanged.Rows[0].Status=="AlreadySet","Already-correct date needs no new save");

        // Staff edit after plan must block, including an exact date already present.
        var stale=unchanged;input=await Input();input.Summary="Cán bộ vừa sửa";current=await Current();await workflow.SaveAsync(id,input,writer,current.RowVersion,Guid.NewGuid());
        await Reject(()=>sync.ApplyAsync(source,plan,imported,decisions,stale,Journal(stale),Path.Combine(run,"stale.json")),"Rowversion conflict retains staff edit","thay đổi sau kế hoạch");
        input=await Input();current=await Current();await workflow.SaveAsync(id,input,writer,current.RowVersion,Guid.NewGuid(),autosave:true);
        await Reject(()=>sync.PlanAsync(source,plan,imported,decisions),"Unsnapshotted autosave blocks stale hidden-metadata inference","tự lưu");
        current=await Current();await workflow.SaveAsync(id,input,writer,current.RowVersion,Guid.NewGuid());

        // Published legacy record: first establish real old public date, then fix only draft.
        input=await Input();input.Metadata.PublicationDate=null;current=await Current();await workflow.SaveAsync(id,input,writer,current.RowVersion,Guid.NewGuid());await Publish();
        var publicBefore=await Current();var oldPublic=publicBefore.PublishedRevisionId;var publishedAt=publicBefore.PublishedAtUtc;
        var oldRevision=JsonSerializer.Serialize(await db.Revisions.AsNoTracking().SingleAsync(x=>x.Id==oldPublic));
        datePlan=await sync.PlanAsync(source,plan,imported,decisions);journal=Journal(datePlan);journalPath=Path.Combine(run,"dates-published.json");
        lose("begin");await Reject(()=>sync.ApplyAsync(source,plan,imported,decisions,datePlan,journal,journalPath),"Lost BeginEdit response leaves recoverable operation","CMS HTTP 503");
        journal=JsonSerializer.Deserialize<WebDateJournal>(await File.ReadAllBytesAsync(journalPath))!;
        lose("save");await Reject(()=>sync.ApplyAsync(source,plan,imported,decisions,datePlan,journal,journalPath),"Resume BeginEdit then lost save response","CMS HTTP 503");
        journal=JsonSerializer.Deserialize<WebDateJournal>(await File.ReadAllBytesAsync(journalPath))!;
        await sync.ApplyAsync(source,plan,imported,decisions,datePlan,journal,journalPath);
        current=await Current();check(current.IsPublic&&current.State==WorkflowState.Draft&&current.PublishedRevisionId==oldPublic&&current.PublishedAtUtc==publishedAt,"Published correction creates draft, preserves exact live revision and publication timestamp");
        check(JsonSerializer.Serialize(await db.Revisions.AsNoTracking().SingleAsync(x=>x.Id==oldPublic))==oldRevision,"Old published revision immutable");
        var oldPage=await cms.HtmlAsync("/bai-viet/"+id,true);check(!oldPage.QuerySelector(".article-meta")!.TextContent.Contains("05/01/2026"),"Public page does not expose draft date before review/publication");
        var verification=JsonSerializer.Serialize(await sync.VerifyPublicAsync(source,plan,imported,decisions));check(!verification.Contains("PublishedDateVerified"),"Verification does not fake PASS before corrected revision is public");
        await Publish();
        verification=JsonSerializer.Serialize(await sync.VerifyPublicAsync(source,plan,imported,decisions));check(verification.Contains("PublishedDateVerified"),"After legitimate review/publication, article and /lich-tuan both show 05/01/2026");
        var links=await importer.SyncAsync(source,plan,imported);check(links[0].Url==cms.Url("/bai-viet/"+id).AbsoluteUri,"SyncLinks retains original ContentId and URL after corrected publication");
        var output=Path.Combine(run,"dates-links.xlsx");source.WriteWorkbook(output,links.ToDictionary(x=>x.Stt,x=>x.Url));
        using(var original=new ZipArchive(new MemoryStream(source.Workbook)))using(var changed=ZipFile.OpenRead(output))
        {
            var a=WeeklyPackage.Xml(WeeklyPackage.ReadEntry(original.GetEntry(source.SheetPath)!));var b=WeeklyPackage.Xml(WeeklyPackage.ReadEntry(changed.GetEntry(source.SheetPath)!));
            bool same=true;for(var row=7;row<=49;row++)foreach(var col in new[]{"A","B","C"})same&=XNode.DeepEquals(a.Descendants(WeeklyPackage.S+"c").Single(x=>(string?)x.Attribute("r")==col+row),b.Descendants(WeeklyPackage.S+"c").Single(x=>(string?)x.Attribute("r")==col+row));
            check(same && WeeklyPackage.Hash(source.Workbook)==originalSha,"Excel A/B/C values, types and style attributes unchanged for all43");
            check(original.Entries.Where(x=>x.FullName!=source.SheetPath&&!x.FullName.EndsWith("sheet9.xml.rels")).Where(x=>!x.FullName.EndsWith(".rels")||!x.FullName.Contains("worksheets")).All(x=>WeeklyPackage.ReadEntry(x).SequenceEqual(WeeklyPackage.ReadEntry(changed.GetEntry(x.FullName)!))),"Other workbook entries/sheets/styles/formulas preserved byte-for-byte");
            check(b.Descendants(WeeklyPackage.S+"c").Single(x=>(string?)x.Attribute("r")=="E48").Element(WeeklyPackage.S+"v")!.Value=="3","E48 corrected to actual3 PDF pages");
        }
        var mediaAfter=await db.MediaAssets.AsNoTracking().SingleAsync();check(mediaAfter.Id==mediaBefore.Id&&mediaAfter.Sha256==mediaBefore.Sha256&&mediaAfter.OriginalName==mediaBefore.OriginalName&&mediaAfter.ScanStatus==mediaBefore.ScanStatus,"Original PDF identity/bytes/name/scan state unchanged");
        check(!File.ReadAllText(journalPath).Contains(accounts["writer"].Password)&&!File.ReadAllText(journalPath).Contains("__RequestVerificationToken"),"Date journal contains neither password nor CSRF");
        // Declined/changed source decisions must never replay an existing write plan.
        await Reject(()=>sync.ApplyAsync(source,plan,imported,decisions with{Rows=[new(1,"2026-05-01","Other decision")]},datePlan,journal,journalPath),"Changed date confirmation invalidates old plan");
    }
}
