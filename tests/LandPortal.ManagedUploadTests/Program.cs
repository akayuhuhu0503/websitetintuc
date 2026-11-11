using LandPortal.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Web.Controllers;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using SkiaSharp;
using UglyToad.PdfPig.Writer;

using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
var options = new DbContextOptionsBuilder<PortalDbContext>().UseSqlite(connection).Options;
await using var db = new FixtureDb(options);
await db.Database.EnsureCreatedAsync();
Console.WriteLine("SQLite isolated fixture ready; no SQL Server connection or migration");
var root=Path.Combine(Path.GetTempPath(),"LandPortal-ManagedUpload-"+Guid.NewGuid().ToString("N"));
var storage=new StorageOptions { MediaRoot=Path.Combine(root,"media"),TempRoot=Path.Combine(root,"temp"),SnapshotRoot=Path.Combine(root,"snapshots"),KeysRoot=Path.Combine(root,"keys") };
Directory.CreateDirectory(storage.MediaRoot);Directory.CreateDirectory(storage.TempRoot);
var policy=new MediaUploadOptions { ApprovedManagedImageWithoutAv=true,ApprovedManagedDocumentsWithoutAv=true };
var off=new MediaUploadOptions();
var media=new MediaService(db,storage,policy); // No scanner or scanner configuration in this fixture.
var rights=new CurrentPermissionService(db);
var docs=new EditorialDocumentService(db,storage,media,rights,policy);
var owner=new PortalUser { Id="fixture-owner",UserName="fixture-owner" };
var outsider=new PortalUser { Id="fixture-outsider",UserName="fixture-outsider" };
db.Users.AddRange(owner,outsider);
foreach(var permission in new[]{Permissions.EditOwn,Permissions.ManageMedia,Permissions.ManageSite,Permissions.PublishSite})
 db.UserClaims.Add(new IdentityUserClaim<string>{UserId=owner.Id,ClaimType="permission",ClaimValue=permission});
var content=new Content { OwnerUserId=owner.Id,Kind=ContentKind.Article };
db.Contents.Add(content);await db.SaveChangesAsync();
var actor=await rights.GetAsync(owner.Id);var unprivileged=await rights.GetAsync(outsider.Id);
var count=0;
void Check(bool yes,string name){if(!yes)throw new Exception("FAIL: "+name);Console.WriteLine("PASS "+(++count)+" "+name);}
async Task Reject(Func<Task> run,string name){try{await run();}catch(Exception ex) when(ex is MediaUploadException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or System.Xml.XmlException){Check(true,name);return;}throw new Exception("FAIL: accepted "+name);}
byte[] Image(SKEncodedImageFormat format){using var bitmap=new SKBitmap(4,3);bitmap.Erase(SKColors.DarkRed);using var image=SKImage.FromBitmap(bitmap);using var encoded=image.Encode(format,90);return encoded.ToArray();}
var images=new List<MediaAsset>();
foreach(var (extension,mime,format) in new[]{("png","image/png",SKEncodedImageFormat.Png),("jpg","image/jpeg",SKEncodedImageFormat.Jpeg)}){
 var bytes=Image(format).Concat(Encoding.UTF8.GetBytes("CLIENT-TRAILER-MUST-NOT-SURVIVE")).ToArray();
 var result=await media.UploadAsync(new MemoryStream(bytes),"fixture."+extension,mime,owner.Id,false);
 images.Add(result.Asset);
 var stored=await File.ReadAllBytesAsync(storage.ResolveMedia(result.Asset.StorageKey));
 Check(result.Asset.ScanStatus=="NotScanned" && result.Asset.ValidationStatus==MediaUploadOptions.ManagedImageNoAvValidation
  && !Encoding.UTF8.GetString(stored).Contains("CLIENT-TRAILER") && Convert.ToHexStringLower(SHA256.HashData(stored))==result.Asset.Sha256,extension+" actual upload/reencode/hash without scanner");
 Check(!off.CanServe(result.Asset),extension+" disabled policy denies serving");
}
await Reject(()=>media.UploadAsync(new MemoryStream("not an image"u8.ToArray()),"fake.png","image/png",owner.Id,false),"fake image rejected");
await Reject(()=>media.UploadAsync(new MemoryStream(Image(SKEncodedImageFormat.Jpeg)),"fake.png","image/png",owner.Id,false),"MIME mismatch rejected");
await Reject(()=>new MediaService(db,storage,new MediaUploadOptions{ApprovedManagedImageWithoutAv=true,MaxImageBytes=2}).UploadAsync(new MemoryStream(Image(SKEncodedImageFormat.Png)),"big.png","image/png",owner.Id,false),"byte cap enforced");
await Reject(()=>new MediaService(db,storage,off).UploadAsync(new MemoryStream(Image(SKEncodedImageFormat.Png)),"off.png","image/png",owner.Id,false),"disabled image upload denied without scanner");
byte[] Office(bool word,string extra="",string? extraName=null){using var mem=new MemoryStream();using(var zip=new ZipArchive(mem,ZipArchiveMode.Create,true)){
 void Entry(string name,string value){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(value);}
 var main=word?"word/document.xml":"xl/workbook.xml";
 var type=word?OfficeDocumentPolicy.Docx+".main+xml":OfficeDocumentPolicy.Xlsx+".main+xml";
 Entry("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/"+main+"\" ContentType=\""+type+"\"/></Types>");
 Entry("_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\""+main+"\"/></Relationships>");
 Entry(main,word?"<document xmlns=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><body><p><r><t>Fixture</t></r></p>"+extra+"</body></document>":"<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheets/>"+extra+"</workbook>");
 if(extraName!=null)Entry(extraName,"fixture");
 }return mem.ToArray();}
using var pdfBuilder=new PdfDocumentBuilder();pdfBuilder.AddPage(100,100);var pdfBytes=pdfBuilder.Build();
var documents=new List<MediaAsset>();
foreach(var (name,mime,bytes) in new[]{("fixture.pdf","application/pdf",pdfBytes),("fixture.docx",OfficeDocumentPolicy.Docx,Office(true)),("fixture.xlsx",OfficeDocumentPolicy.Xlsx,Office(false))}){
 var asset=await docs.UploadAsync(content.Id,new MemoryStream(bytes),name,mime,actor);documents.Add(asset);
 var proof=await db.EditorialDocuments.SingleAsync(x=>x.MediaAssetId==asset.Id);
 Check(asset.ScanStatus=="NotScanned" && policy.IsManagedDocument(asset) && proof.Attempts==0 && proof.EngineVersion==null,name+" actual upload truthful state/no scan");
 await using var read=await EditorialDocumentService.OpenVerifiedAsync(db,storage,asset,false,policy:policy);
 Check(read!=null,name+" verified private read");
 await using var denied=await EditorialDocumentService.OpenVerifiedAsync(db,storage,asset,false,policy:off);
 Check(denied==null,name+" disabled policy denies read");
}
await Reject(()=>docs.UploadAsync(content.Id,new MemoryStream(pdfBytes),"denied.pdf","application/pdf",unprivileged),"unprivileged document upload denied before persistence");
await Reject(()=>docs.UploadAsync(content.Id,new MemoryStream("%PDF-broken %%EOF"u8.ToArray()),"bad.pdf","application/pdf",actor),"broken PDF rejected");
await Reject(()=>docs.UploadAsync(content.Id,new MemoryStream(Office(true,extraName:"word/vbaProject.bin")),"bad.docx",OfficeDocumentPolicy.Docx,actor),"Office macro rejected");
await Reject(()=>docs.UploadAsync(content.Id,new MemoryStream(Office(false,"<f>1+1</f>")),"bad.xlsx",OfficeDocumentPolicy.Xlsx,actor),"Excel formula rejected");
await Reject(()=>docs.UploadAsync(content.Id,new MemoryStream(Office(true,"<Relationship TargetMode=\"External\" Target=\"https://invalid.example\"/>")),"bad.docx",OfficeDocumentPolicy.Docx,actor),"external relationship rejected");
await Reject(()=>docs.UploadAsync(content.Id,new MemoryStream(Office(true,"<!DOCTYPE evil>")),"bad.docx",OfficeDocumentPolicy.Docx,actor),"malformed XML rejected");
await Reject(()=>docs.UploadAsync(content.Id,new MemoryStream(pdfBytes),"duplicate.pdf","application/pdf",actor),"duplicate hash cannot grant rights");
// Valid PDF syntax with an escaped active-content name, not a broken-file rejection.
byte[] ActivePdf(){
 using var output=new MemoryStream();void Write(string s){output.Write(Encoding.ASCII.GetBytes(s));}
 Write("%PDF-1.4\n");var offsets=new List<long>();
 foreach(var value in new[]{"<< /Type /Catalog /Pages 2 0 R /OpenAction 4 0 R >>","<< /Type /Pages /Kids [3 0 R] /Count 1 >>","<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>","<< /S /JavaScript /J#53 (alert) >>"}){
  offsets.Add(output.Position);Write(offsets.Count+" 0 obj\n"+value+"\nendobj\n");
 }
 var xref=output.Position;Write("xref\n0 5\n0000000000 65535 f \n");foreach(var offset in offsets)Write(offset.ToString("D10")+" 00000 n \n");
 Write("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n"+xref+"\n%%EOF\n");return output.ToArray();
}
var activePdf=ActivePdf();var activePath=Path.Combine(root,"active-fixture.pdf");await File.WriteAllBytesAsync(activePath,activePdf);
media.ValidateContent(activePath,"application/pdf",CancellationToken.None); // Prove this is parsed PDF, not a corrupt fixture.
await Reject(()=>docs.UploadAsync(content.Id,new MemoryStream(activePdf),"active.pdf","application/pdf",actor),"active PDF action/escaped JavaScript name rejected");
var site=new SiteConfigurationService(db,storage,policy);
await site.InitializeAsync(owner.Id);var header=await db.Set<SiteConfiguration>().SingleAsync();header.RowVersion=new byte[8];await db.SaveChangesAsync();
var siteDraft=await site.MutateAsync(actor,header.RowVersion,r=>r.Banners.Add(new(){MediaAssetId=images[0].Id,Label="Fixture banner",Alt="Fixture image",Position="home_event_strip"}),"fixture");
Check(!(await site.ReadAsync(true))!.Revision.Banners.Any(),"banner draft does not change live configuration");
await site.PublishAsync(actor,header.RowVersion,siteDraft.Revision.Id);
Check((await site.ReadAsync(true))!.Revision.Banners.Single().MediaAssetId==images[0].Id,"real banner draft/publish accepts managed image");
// Exercise the actual draft asset business validator; SQL Server lock/transaction wrapper is intentionally NOT emulated.
var draft=new ContentDraft{ContentId=content.Id};db.ContentDrafts.Add(draft);
var input=new DraftInput {Title="Fixture",Kind=ContentKind.Article,CoverAssetId=images[0].Id,Assets=documents.Select(x=>new AssetSelection{MediaAssetId=x.Id,Usage="Attachment"}).ToList()};
var workflow=new WorkflowService(db,new HtmlPolicy(),policy,storage);
var apply=typeof(WorkflowService).GetMethod("ApplyAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
await (Task)apply.Invoke(workflow,new object[]{draft,input,actor,content,false})!;await db.SaveChangesAsync();
Check(draft.Assets.Count==4,"draft attaches image cover plus PDF DOCX XLSX using real business validator");
Check(await new MediaController(db,storage,policy,null!){ControllerContext=new(){HttpContext=new DefaultHttpContext()}}.File(documents[0].Id) is NotFoundResult,"draft document not public");
var usage=new DocumentUsage(db,new PublicContentQuery(db,policy),NullLogger<DocumentUsage>.Instance);
var revision=new ContentRevision {ContentId=content.Id,Title="Fixture published grant",BodyHtml="<p>Fixture</p>",Author="Fixture"};
db.Revisions.Add(revision);await db.SaveChangesAsync();content.IsPublic=true;content.PublishedRevisionId=revision.Id;
foreach(var a in documents.Concat(images))db.ContentAssets.Add(new(){RevisionId=revision.Id,MediaAssetId=a.Id,Usage="Attachment"});await db.SaveChangesAsync();
foreach(var asset in documents){
 var controller=new MediaController(db,storage,policy,usage){ControllerContext=new(){HttpContext=new DefaultHttpContext()}};
 var result=await controller.File(asset.Id);
 Check(result is FileStreamResult file && file.FileDownloadName==asset.OriginalName && controller.Response.Headers["X-Media-Scan-Status"]=="NotScanned" && controller.Response.Headers["X-Content-Type-Options"]=="nosniff",asset.OriginalName+" actual endpoint attachment with persisted public grant");
 if(result is FileStreamResult opened)await opened.FileStream.DisposeAsync();
}
var deniedPublish=false;try{await workflow.PublishApprovedAsync(content.Id,revision.Id,unprivileged,content.RowVersion,Guid.NewGuid());}catch(UnauthorizedAccessException){deniedPublish=true;}
Check(deniedPublish,"unprivileged publish denied");
var disabledController=new MediaController(db,storage,off,usage){ControllerContext=new(){HttpContext=new DefaultHttpContext()}};
Check(await disabledController.File(documents[0].Id) is NotFoundResult,"public download denied with flag off even with grant");
var privateController=new AdminMediaController(db,storage,policy,media,rights){ControllerContext=new(){HttpContext=new DefaultHttpContext()}};
privateController.HttpContext.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,outsider.Id)},"fixture"));
Check(await privateController.Preview(images[0].Id,null) is NotFoundResult,"unprivileged CMS image preview denied");
privateController.HttpContext.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,owner.Id)},"fixture"));
await File.AppendAllTextAsync(storage.ResolveMedia(images[1].StorageKey),"tamper");
Check(await privateController.Preview(images[1].Id,null) is NotFoundResult,"managed image private preview verifies hash");
content.IsPublic=false;await db.SaveChangesAsync();
Check(await new MediaController(db,storage,policy,usage){ControllerContext=new(){HttpContext=new DefaultHttpContext()}}.File(documents[0].Id) is NotFoundResult,"revoked public grant denied");
await File.AppendAllTextAsync(storage.ResolveMedia(documents[0].StorageKey),"tamper");
await using(var tampered=await EditorialDocumentService.OpenVerifiedAsync(db,storage,documents[0],false,policy:policy))Check(tampered==null,"tampered document denied and quarantine update exercised");
var legacy=new MediaAsset{Origin="Source",ScanStatus="NotScanned",ValidationStatus="SourceValidated",MimeType="image/png"};
Check(policy.CanServe(legacy)==off.CanServe(legacy) && !policy.CanServe(legacy),"source NotScanned unchanged by managed upload flags");
Check(UatDeployment.TemporaryRuntimeIdentityMatches("WIN-S1IBJB84OOO","website_tin_tuc","website_tin_tuc_app","LandPortalRuntime",70,18,"20261110050000_HomeEventBanner"),"Runtime18 retained");
Console.WriteLine($"TOTAL {count} PASS; fixture={root}; SQL Server workflow/E2E browser NOT RUN");

sealed class FixtureDb(DbContextOptions<PortalDbContext> options) : PortalDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var index in entity.GetIndexes()) index.SetFilter(null);
            foreach (var p in entity.GetProperties())
            {
                p.SetColumnType(null); p.SetDefaultValueSql(null); p.SetComputedColumnSql(null);
                if (p.ClrType == typeof(byte[]) && p.IsConcurrencyToken)
                { p.ValueGenerated = ValueGenerated.Never; }
                if (p.ClrType == typeof(DateTimeOffset)) p.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());
            }
        }
    }
}
