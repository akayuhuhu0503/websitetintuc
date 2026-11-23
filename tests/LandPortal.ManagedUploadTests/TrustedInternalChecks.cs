using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig.Writer;

internal static class TrustedInternalChecks
{
    public static async Task Real(string inventoryPath, string output)
    {
        using var inventory = JsonDocument.Parse(await File.ReadAllTextAsync(inventoryPath));
        var root = Path.GetDirectoryName(Path.GetFullPath(inventoryPath))!;
        var records = new List<object>(); var failed = 0;
        foreach (var f in inventory.RootElement.GetProperty("files").EnumerateArray().Where(x => !x.GetProperty("insideFolder").GetBoolean()))
        {
            var name = f.GetProperty("name").GetString()!;
            var path = Path.Combine(root,"source",name);
            var before = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)));
            if (before != f.GetProperty("sha256").GetString()) throw new Exception("Source hash mismatch");
            var watch=Stopwatch.StartNew(); string? error=null;int? pages=null;
            try { pages=await EditorialDocumentPolicy.ValidateAsync(path,"application/pdf",null!,true,trustedInternal:true); }
            catch(Exception e) { error=e.GetBaseException().Message;failed++; }
            var after=Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)));
            if(after!=before)throw new Exception("Source changed");
            if(error==null && pages!=f.GetProperty("pages").GetInt32())throw new Exception("Page mismatch");
            records.Add(new{name,before,pages,error,milliseconds=watch.ElapsedMilliseconds,bytesUnchanged=true,scanStatus="NotScanned"});
            Console.WriteLine((error==null?"PASS ":"BLOCKED ")+name+": "+(error??pages+" pages"));
        }
        var zip=inventory.RootElement.GetProperty("zip");var zipPath=Path.Combine(root,zip.GetProperty("name").GetString()!);
        if(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(zipPath)))!=zip.GetProperty("sha256").GetString())throw new Exception("ZIP changed");
        var entries=await EditorialZipPolicy.ValidateAsync(zipPath,trustedInternal:true);
        await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{records,failed,zipEntries=entries,scanStatus="NotScanned",cmsWrites=0},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"REAL PDF {records.Count-failed}/{records.Count}, ZIP {entries.Count} leaves; unchanged bytes; no CMS writes");
        if(failed>0)Environment.ExitCode=1;
    }

    public static async Task Run()
    {
        var count=0;
        void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine($"PASS {++count}: {name}");}
        async Task Reject(Func<Task> action,string name){try{await action();}catch(Exception e)when(e is UnauthorizedAccessException or InvalidOperationException or InvalidDataException or MediaUploadException){Check(true,name);return;}throw new Exception("FAIL accepted "+name);}
        byte[] Pdf(string catalog, params string[] extra)
        {
            var objects = new[] { "<< /Type /Catalog /Pages 2 0 R " + catalog + " >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>" }.Concat(extra).ToArray();
            var text = new StringBuilder("%PDF-1.7\n"); var offsets = new List<int>();
            for (var i = 0; i < objects.Length; i++) { offsets.Add(text.Length); text.Append($"{i+1} 0 obj\n{objects[i]}\nendobj\n"); }
            var xref = text.Length; text.Append($"xref\n0 {objects.Length+1}\n0000000000 65535 f \n");
            foreach(var offset in offsets)text.Append(offset.ToString("D10")+" 00000 n \n");
            text.Append($"trailer\n<< /Size {objects.Length+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");return Encoding.ASCII.GetBytes(text.ToString());
        }
        void Validate(byte[] bytes) { using var stream = new MemoryStream(bytes); TrustedInternalPdfPolicy.Validate(stream); }
        var signaturePdf = Pdf("/AcroForm << /Fields [4 0 R] /SigFlags 3 >>", "<< /FT /Sig /V << /ByteRange [0 10 20 30] /Contents <00> >> >>");
        Validate(signaturePdf); Check(true,"signature field structure allowed without claiming cryptographic authenticity");
        Validate(Pdf("/StructTreeRoot 4 0 R", "<< /Type /StructTreeRoot /K 5 0 R >>", "<< /Type /StructElem /S /P /P 4 0 R /A << /O /Layout /Placement /Block /BBox [0 0 100 100] >> >>"));
        Check(true,"tagged layout A is not confused with an executable action");
        foreach(var link in new[]{"(https://example.invalid/document)","<68747470733A2F2F6578616D706C652E696E76616C69642F>"})
        { Validate(Pdf("/A << /S /URI /URI "+link+" >>")); Check(true,"safe click-only navigation in literal/hex string"); }
        foreach(var entry in new[]{
            ("/OpenAction 4 0 R", "<< /S /GoTo /D [3 0 R /Fit] >>", "automatic opening action"),
            ("/A 4 0 R", "<< /S /JavaScript /JS (alert) >>", "JavaScript"),
            ("/A 4 0 R", "<< /S /Launch /F (cmd.exe) >>", "executable launch"),
            ("/A 4 0 R", "<< /S /URI /URI (file:///private/test) >>", "local file URI"),
            ("/A 4 0 R", "<< /S /URI /URI (javascript:alert) >>", "script URI"),
            ("/A 4 0 R", "<< /S /URI /URI (https://example.invalid/) /Next << /S /Launch >> >>", "chained action"),
            ("/Names << /EmbeddedFiles 4 0 R >>", "<< /Names [] >>", "embedded file"),
            ("/AcroForm << /Fields [4 0 R] >>", "<< /FT /Tx >>", "interactive text field"),
            ("/AcroForm << /Fields [4 0 R] >>", "<< /FT /Sig /V << /ByteRange [0 10 5 30] /Contents <00> >> >>", "overlapping signature byte ranges"),
            ("/AcroForm << /Fields [4 0 R] >>", "<< /FT /Sig /Kids [4 0 R] >>", "cyclic signature fields"),
            ("/StructTreeRoot 4 0 R", "<< /Type /StructElem /A << /S /JavaScript /JS (alert) >> >>", "action disguised as layout") })
            await Reject(()=>{Validate(Pdf(entry.Item1,entry.Item2));return Task.CompletedTask;},"PDF rejects "+entry.Item3);
        using(var cancelled=new CancellationTokenSource()) { cancelled.Cancel(); try{using var stream=new MemoryStream(signaturePdf);TrustedInternalPdfPolicy.Validate(stream,cancelled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){Check(true,"PDF validation observes cancellation");} }
        var root=Path.Combine(Path.GetTempPath(),"LandPortal-Trusted-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        using var connection=new SqliteConnection("Data Source=:memory:");connection.Open();
        await using var db=new FixtureDb(new DbContextOptionsBuilder<PortalDbContext>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
        var storage=new StorageOptions{MediaRoot=Path.Combine(root,"media"),TempRoot=Path.Combine(root,"temp"),SnapshotRoot=Path.Combine(root,"snapshots"),KeysRoot=Path.Combine(root,"keys")};
        var policy=new MediaUploadOptions{NativeUatUploads=true}; // isolated DI only, not runtime configuration
        var rights=new CurrentPermissionService(db);var media=new MediaService(db,storage,policy);var docs=new EditorialDocumentService(db,storage,media,rights,policy);
        foreach(var id in new[]{"trusted-admin","writer"}){db.Users.Add(new(){Id=id,UserName=id});foreach(var p in new[]{Permissions.EditOwn,Permissions.ManageMedia})db.UserClaims.Add(new IdentityUserClaim<string>{UserId=id,ClaimType="permission",ClaimValue=p});}
        db.UserClaims.Add(new(){UserId="trusted-admin",ClaimType="permission",ClaimValue=Permissions.ManageSettings});
        var content=new Content{OwnerUserId="trusted-admin",Kind=ContentKind.StaticPage};var other=new Content{OwnerUserId="writer",Kind=ContentKind.StaticPage};db.Contents.AddRange(content,other);await db.SaveChangesAsync();
        var admin=await rights.GetAsync("trusted-admin");var writer=await rights.GetAsync("writer");
        using var builder=new PdfDocumentBuilder();builder.AddPage(100,100);var pdf=builder.Build();
        byte[] Zip(string name,byte[] bytes){using var mem=new MemoryStream();using(var z=new ZipArchive(mem,ZipArchiveMode.Create,true)){using var s=z.CreateEntry(name).Open();s.Write(bytes);}return mem.ToArray();}
        var zip=Zip("documents/original.pdf",pdf);
        await Reject(()=>docs.UploadAsync(content.Id,new MemoryStream(zip),"sample.zip",EditorialZipPolicy.Mime,admin),"ordinary ZIP path still needs scanner");
        await Reject(()=>docs.UploadAsync(other.Id,new MemoryStream(pdf),"sample.pdf","application/pdf",writer,trustedInternalSource:"Fixture source"),"ordinary writer cannot attest trust");
        await Reject(()=>docs.UploadAsync(other.Id,new MemoryStream(pdf),"sample.pdf","application/pdf",admin,trustedInternalSource:"Fixture source"),"admin permission alone does not bypass edit scope");
        await Reject(()=>docs.UploadAsync(content.Id,new MemoryStream(pdf),"sample.pdf","application/pdf",admin,trustedInternalSource:" "),"empty confirmation denied");
        var off=new EditorialDocumentService(db,storage,media,rights,new MediaUploadOptions());
        await Reject(()=>off.UploadAsync(content.Id,new MemoryStream(pdf),"sample.pdf","application/pdf",admin,trustedInternalSource:"Fixture source"),"outside UAT policy denied");
        await Reject(()=>docs.UploadAsync(content.Id,new MemoryStream(pdf),"sample.docx",OfficeDocumentPolicy.Docx,admin,trustedInternalSource:"Fixture source"),"trusted policy does not expand to Office");
        await Reject(()=>docs.UploadAsync(content.Id,new MemoryStream("fake"u8.ToArray()),"sample.pdf","application/pdf",admin,trustedInternalSource:"Fixture source"),"fake PDF rejected");
        foreach(var bad in new[]{Zip("../escape.pdf",pdf),Zip("run.exe",pdf),Zip("nested.zip",zip),Zip("bomb.pdf",new byte[1024*1024]),Zip("fake.pdf","broken"u8.ToArray())})
            await Reject(()=>docs.UploadAsync(content.Id,new MemoryStream(bad),"bad.zip",EditorialZipPolicy.Mime,admin,trustedInternalSource:"Fixture source"),"dangerous archive denied");
        var asset=await docs.UploadAsync(content.Id,new MemoryStream(zip),"docs.zip",EditorialZipPolicy.Mime,admin,trustedInternalSource:"Fixture internal collection");
        var pdfAsset=await docs.UploadAsync(content.Id,new MemoryStream(pdf),"original.pdf","application/pdf",admin,trustedInternalSource:"Fixture internal PDF");
        Check(asset.ScanStatus=="NotScanned"&&asset.ValidationStatus==TrustedInternalDocumentPolicy.Validation&&!asset.IsQuarantined,"explicit technical state; no fake Clean");
        Check(await db.AuditLogs.CountAsync(a=>a.Action==TrustedInternalDocumentPolicy.GrantAction&&a.Actor==admin.UserId)==2,"real actor per-file audit grants");
        Check(await db.EditorialDocuments.AllAsync(d=>d.ScanState==InquiryScanState.NotScanned&&d.Attempts==0&&d.EngineVersion==null&&d.ScannedUtc==null&&d.ScannedSha256==null),"no fabricated scanner proof");
        Check((await docs.UploadAsync(content.Id,new MemoryStream(zip),"docs.zip",EditorialZipPolicy.Mime,admin,trustedInternalSource:"Fixture internal collection")).Id==asset.Id && await db.MediaAssets.CountAsync()==2,"same-article retry reuses asset without duplicate");
        await using(var file=await EditorialDocumentService.OpenVerifiedAsync(db,storage,asset,false,policy:policy))Check(file!=null,"grant/hash opens private trusted ZIP");
        Check(await EditorialDocumentService.OpenVerifiedAsync(db,storage,asset,false,policy:new MediaUploadOptions())==null,"policy off denies existing trusted file");
        var grant=await db.AuditLogs.SingleAsync(a=>a.Action==TrustedInternalDocumentPolicy.GrantAction&&a.Detail==TrustedInternalDocumentPolicy.Binding(asset));var binding=grant.Detail;
        grant.Detail="invalid";await db.SaveChangesAsync();
        Check(await EditorialDocumentService.OpenVerifiedAsync(db,storage,asset,false,policy:policy)==null,"status without exact audit binding denied");grant.Detail=binding;await db.SaveChangesAsync();
        var draft=new ContentDraft{ContentId=content.Id};db.ContentDrafts.Add(draft);
        var workflow=new WorkflowService(db,new HtmlPolicy(),policy,storage);
        var input=new DraftInput{Title="Trusted fixture",Kind=ContentKind.StaticPage,Metadata=new(){PublicationDate=new DateOnly(2026,1,30)},Assets=[new(){MediaAssetId=asset.Id,Usage="Attachment"},new(){MediaAssetId=pdfAsset.Id,Usage="Attachment"}]};
        await (Task)typeof(WorkflowService).GetMethod("ApplyAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(workflow,[draft,input,admin,content,false])!;await db.SaveChangesAsync();
        Check(draft.Assets.Count==2&&draft.Metadata.PublicationDate==new DateOnly(2026,1,30),"draft attachment validator accepts proof without changing date");
        var controller=new MediaController(db,storage,policy,null!){ControllerContext=new(){HttpContext=new DefaultHttpContext()}};
        Check(await controller.File(asset.Id) is NotFoundResult,"draft ZIP not public");
        var rev=new ContentRevision{ContentId=content.Id,Title="Publication fixture",BodyHtml="<p>Fixture</p>"};db.Revisions.Add(rev);await db.SaveChangesAsync();content.IsPublic=true;content.PublishedRevisionId=rev.Id;
        db.ContentAssets.Add(new(){RevisionId=rev.Id,MediaAssetId=asset.Id,Usage="Attachment"});await db.SaveChangesAsync();
        var result=await controller.File(asset.Id);
        Check(result is FileStreamResult f&&f.FileDownloadName=="docs.zip"&&controller.Response.Headers["X-Media-Scan-Status"]=="NotScanned"&&controller.Response.Headers["X-Content-Type-Options"]=="nosniff","public grant serves ZIP attachment with honest headers");
        if(result is FileStreamResult responseStream)await responseStream.FileStream.DisposeAsync();
        content.IsPublic=false;await db.SaveChangesAsync();Check(await controller.File(asset.Id) is NotFoundResult,"withdrawal immediately denies download");
        await File.AppendAllTextAsync(storage.ResolveMedia(asset.StorageKey),"tamper");
        Check(await EditorialDocumentService.OpenVerifiedAsync(db,storage,asset,false,policy:policy)==null,"tamper denied and quarantined");
        Console.WriteLine($"TRUSTED TOTAL {count} PASS; isolated fixture, no real SQL/CMS/AV");
    }
}
