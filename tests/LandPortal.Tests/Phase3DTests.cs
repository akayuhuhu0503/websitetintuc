using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Tests;
public static class Phase3DTests
{
    static void Check(bool condition,string why){if(!condition)throw new InvalidOperationException(why);}
    static async Task Denied(Func<Task> operation){try{await operation();}catch(InvalidDataException){return;}throw new InvalidOperationException("Unsafe request was accepted.");}
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(string root,DbContextOptions<PortalDbContext> options,IConfiguration config)
    {
        yield return("3D offline 512 URL/270 candidate reason reconciliation and immutable checkpoint",async()=>
        {
            var path=Path.Combine(root,".local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3.bin");
            var sha=SourceInventory.Hash(await File.ReadAllBytesAsync(path));var state=await SourceBatch.ReadCheckpointAsync(root,path,sha);
            var report=await CoverageAudit.InspectAsync(root,state);
            Check(report.Urls==512&&report.CandidateIdentities==270&&report.VerifiedItems==40,"Actual baseline differs; do not force counts.");
            Check(report.CandidateVerified+report.PendingCandidates==270&&report.Candidates.All(x=>x.Reason.Length>0)&&report.NewVerified==0,"Coverage not fully reconciled or unsupported newly verified claim.");
            Check(SourceInventory.Hash(await File.ReadAllBytesAsync(path))==sha&&state.RequestsReserved==39,"Offline audit mutated checkpoint/network counters.");
            await MediaRecovery.WriteNewAsync(Path.Combine(TestEvidencePaths.At(root),"coverage.json"),report);
        });
        yield return("3D NukeViet namespace canonical hash and cross-source duplicate suggestions; technical fixture only",async()=>
        {
            const string url="https://gdla.gov.vn/vi/news/tin-tuc/technical-fixture-1780.html";
            const string html="<html><head><link rel='canonical' href='https://gdla.gov.vn/vi/news/tin-tuc/technical-fixture-1780.html'></head><body><h1>TECHNICAL TEST</h1><div id='news-bodyhtml'><p>Anonymous technical fixture, not migrated source.</p><img src='/uploads/news/test.jpg'></div></body></html>";
            var sha=SourceInventory.HashText(html);var preview=MigrationSourceIdentityPolicy.ParseNukeViet(html,url,sha);
            var qldd=new MigrationSourceIdentity("qldd.mae.gov.vn","sharepoint",ContentKind.Article,"1780");
            Check(preview.Identity!=qldd&&preview.Identity.SqlSourceId!="1780"&&preview.CategoryMappingStatus=="NEEDS_APPROVAL"&&preview.PublicationStatus=="PrivateInventoryOnly","Secondary source identity or private staging incorrect.");
            Check(MigrationSourceIdentityPolicy.FromUrl("https://gdla.gov.vn/admin/index.php")==null&&MigrationSourceIdentityPolicy.FromUrl(url+"?delete=1")==null&&MigrationSourceIdentityPolicy.FromUrl(url.Replace("gdla.gov.vn","other.example"))==null,"Unapproved route accepted.");
            await Denied(()=>Task.Run(()=>MigrationSourceIdentityPolicy.ParseNukeViet(html.Replace("fixture-1780.html'","fixture-1781.html'"),url,sha)));
            var article=new ParsedSource{Title=preview.Title,BodyHtml="<p>Other body</p>"};
            Check(MigrationSourceIdentityPolicy.DuplicateEvidence(preview,article)=="TITLE_ONLY_NOT_DUPLICATE","Title-only auto merge.");
            article.BodyHtml=preview.BodyHtml;Check(MigrationSourceIdentityPolicy.DuplicateEvidence(preview,article)=="BODY_MATCH_NEEDS_REVIEW","Exact body should be a review suggestion only.");
            var snapshot=Path.Combine(TestEvidencePaths.At(root),"nukeviet-technical.html");await File.WriteAllTextAsync(snapshot,html);
            var checkpoint=Path.Combine(root,".local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3.bin");
            var common=new[]{"nukeviet-preview","--html",snapshot,"--url",url,"--checkpoint",checkpoint,"--checkpoint-sha",Path.GetFileNameWithoutExtension(checkpoint)};
            Check(await Phase3DCommand.OfflineAsync([..common,"--sha",sha,"--report",Path.Combine(TestEvidencePaths.At(root),"nukeviet-preview")],root)==0,"Actual preview command failed.");
            await Denied(()=>Phase3DCommand.OfflineAsync([..common,"--sha",new string('a',64),"--report",Path.Combine(TestEvidencePaths.At(root),"nukeviet-bad-hash")],root));
        });
        yield return("3D target media store plan binding denies commit verify and rollback before effects",async()=>
        {
            var f=await BatchImportTests.Fixture(root);await using var db=new PortalDbContext(options);
            var storage=StorageOptions.FromConfiguration(config);var batch=new SourceBatch(db,storage,config,root,f.State,f.Hash);
            var plan=await batch.PreviewAsync();Check(plan.SchemaVersion==3&&plan.TargetMediaRootSha256==SourceBatch.MediaRootIdentity(storage),"Schema3 store binding missing.");
            plan.TargetMediaRootSha256=new string('0',64);var sha=SourceInventory.HashText(JsonSerializer.Serialize(plan));
            var audit=await db.AuditLogs.CountAsync();await Denied(()=>batch.CommitAsync(plan,sha));await Denied(()=>batch.VerifyAsync(plan,sha));await Denied(()=>batch.RollbackPublicationAsync(plan,sha));
            Check(await db.AuditLogs.CountAsync()==audit&&!await db.SourceRecords.AnyAsync(x=>x.SourceId==f.State.Items[0].Source.SourceId),"Rejected store binding wrote SQL.");
        });
        yield return("3D explicit FR17 name decision Test only backed up audited idempotent; no menu/content publication",async()=>
        {
            using var pointer=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,".local/phase3d-current.json")));var folder=Path.Combine(root,pointer.RootElement.GetProperty("root").GetString()!);
            using var old=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,".local/phase3c-current.json")));var backup=Path.Combine(root,old.RootElement.GetProperty("root").GetString()!,"backup-LandPortal_Test.json");
            var decision=Path.Combine(folder,"request.txt");var sha=SourceInventory.Hash(await File.ReadAllBytesAsync(decision));
            await using var db=new PortalDbContext(options);var publicIds=await db.Contents.Where(x=>x.IsPublic).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.PublishedRevisionId}).ToArrayAsync();
            var menu=await db.SiteSettings.AsNoTracking().OrderBy(x=>x.Key).Select(x=>new{x.Key,x.Value}).ToArrayAsync();
            await Phase3DCommand.ApplyNameDecisionTestAsync(db,config,root,decision,sha,backup);
            var count=await db.AuditLogs.CountAsync(x=>x.Action=="Phase3D.FR17NameDecision");await Phase3DCommand.ApplyNameDecisionTestAsync(db,config,root,decision,sha,backup);
            Check((await db.DisclosureGroups.SingleAsync(x=>x.Id=="nguoi-nuoc-ngoai-dinh-cu-o-viet-nam")).Status=="NameApproved"&&await db.AuditLogs.CountAsync(x=>x.Action=="Phase3D.FR17NameDecision")==count,"Name decision missing or non-idempotent.");
            Check(JsonSerializer.Serialize(publicIds)==JsonSerializer.Serialize(await db.Contents.Where(x=>x.IsPublic).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.PublishedRevisionId}).ToArrayAsync())&&JsonSerializer.Serialize(menu)==JsonSerializer.Serialize(await db.SiteSettings.AsNoTracking().OrderBy(x=>x.Key).Select(x=>new{x.Key,x.Value}).ToArrayAsync()),"Name decision published content/menu implicitly.");
            var dev=new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{{"Sql:ExpectedDatabase","LandPortal_Dev"}}).Build();
            try{await Phase3DCommand.ApplyNameDecisionTestAsync(db,dev,root,decision,sha,backup);throw new InvalidOperationException("Dev name decision accepted.");}catch(UnauthorizedAccessException){}
        });
    }
}
