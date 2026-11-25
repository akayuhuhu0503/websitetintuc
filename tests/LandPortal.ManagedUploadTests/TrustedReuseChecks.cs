using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using UglyToad.PdfPig.Writer;

internal static class TrustedReuseChecks
{
    internal static async Task Run()
    {
        var passed=0;
        void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine($"PASS {++passed}: {name}");}
        async Task Reject(Func<Task> call,string name){try{await call();}catch(Exception e)when(e is UnauthorizedAccessException or InvalidOperationException or DbUpdateConcurrencyException){Check(true,name);return;}throw new Exception("FAIL accepted "+name);}
        using var connection=new SqliteConnection("Data Source=:memory:");connection.Open();
        await using var db=new FixtureDb(new DbContextOptionsBuilder<PortalDbContext>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
        var root=Path.Combine(Path.GetTempPath(),"LandPortal-Reuse-"+Guid.NewGuid().ToString("N"));
        var storage=new StorageOptions{MediaRoot=Path.Combine(root,"media"),TempRoot=Path.Combine(root,"temp"),SnapshotRoot=Path.Combine(root,"snapshots"),KeysRoot=Path.Combine(root,"keys")};
        var policy=new MediaUploadOptions{NativeUatUploads=true};
        foreach(var id in new[]{"reuse-admin","reuse-writer"}){
            db.Users.Add(new(){Id=id,UserName=id});
            foreach(var permission in new[]{Permissions.EditOwn,Permissions.ViewOwn,Permissions.ManageMedia})db.UserClaims.Add(new IdentityUserClaim<string>{UserId=id,ClaimType="permission",ClaimValue=permission});
        }
        db.UserClaims.Add(new(){UserId="reuse-admin",ClaimType="permission",ClaimValue=Permissions.ManageSettings});
        var source=new Content{OwnerUserId="reuse-admin",Kind=ContentKind.StaticPage};var target=new Content{OwnerUserId="reuse-admin",Kind=ContentKind.StaticPage};var other=new Content{OwnerUserId="reuse-writer",Kind=ContentKind.StaticPage};
        db.Contents.AddRange(source,target,other);await db.SaveChangesAsync();
        var rights=new CurrentPermissionService(db);var admin=await rights.GetAsync("reuse-admin");var writer=await rights.GetAsync("reuse-writer");
        var media=new MediaService(db,storage,policy);var docs=new EditorialDocumentService(db,storage,media,rights,policy);
        var bytes=TrustedPdfCompatibilityChecks.Pdf("/Gs1 gs /Missing Do 0 0 20 20 re f");
        var asset=await docs.UploadAsync(source.Id,new MemoryStream(bytes),"158_original.pdf","application/pdf",admin,trustedInternalSource:"Isolated fixture source");
        var hash=Convert.ToHexStringLower(SHA256.HashData(bytes));
        var confirmation=await db.AuditLogs.SingleAsync(x=>x.Action=="document.trusted.confirmed");
        Check(confirmation.Detail.Contains("PDF_UNDEFINED_GRAPHICS_STATE:1",StringComparison.Ordinal)
            && confirmation.Detail.Contains("PDF_UNDEFINED_XOBJECT:1",StringComparison.Ordinal),"source compatibility warnings are durably audited");
        Check(asset.ScanStatus=="NotScanned"&&asset.Sha256==hash,"accepted original retains truthful scan state and SHA");
        Check(!await TrustedInternalDocumentPolicy.HasReuseGrantAsync(db,asset,target.Id,policy),"hash alone gives no destination grant");
        var controller=new EditorialDocumentsController(db,rights,docs,storage,new ReuseTestEnvironment(),new ConfigurationBuilder().Build(),policy){ControllerContext=new(){HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,admin.UserId)},"fixture"))}}};
        Check(await controller.Result(target.Id,hash,default) is NotFoundResult,"result endpoint denies another article before grant");
        Check(await controller.Download(asset.Id,target.Id) is NotFoundResult,"private download denies destination without a grant or attachment");
        await Reject(()=>docs.ReuseTrustedAsync(target.Id,source.Id,asset.Id,hash,target.RowVersion,"Fixture",writer),"writer cannot attest reuse");
        await Reject(()=>docs.ReuseTrustedAsync(other.Id,source.Id,asset.Id,hash,other.RowVersion,"Fixture",admin),"destination edit scope enforced");
        await Reject(()=>docs.ReuseTrustedAsync(target.Id,other.Id,asset.Id,hash,target.RowVersion,"Fixture",admin),"source read scope enforced");
        await Reject(()=>docs.ReuseTrustedAsync(target.Id,source.Id,asset.Id,new string('0',64),target.RowVersion,"Fixture",admin),"incorrect source hash denied");
        await Reject(()=>docs.ReuseTrustedAsync(target.Id,source.Id,asset.Id,hash,[9,9],"Fixture",admin),"stale destination version denied");
        await Reject(()=>docs.ReuseTrustedAsync(target.Id,source.Id,asset.Id,hash,target.RowVersion," ",admin),"empty source confirmation denied");
        var off=new EditorialDocumentService(db,storage,media,rights,new MediaUploadOptions());
        await Reject(()=>off.ReuseTrustedAsync(target.Id,source.Id,asset.Id,hash,target.RowVersion,"Fixture",admin),"UAT policy off denied");
        target.State=WorkflowState.Published;await db.SaveChangesAsync();
        await Reject(()=>docs.ReuseTrustedAsync(target.Id,source.Id,asset.Id,hash,target.RowVersion,"Fixture",admin),"closed revision denied");target.State=WorkflowState.Draft;await db.SaveChangesAsync();
        var originalGrant=await db.AuditLogs.SingleAsync(x=>x.Action==TrustedInternalDocumentPolicy.GrantAction);var binding=originalGrant.Detail;originalGrant.Detail="invalid";await db.SaveChangesAsync();
        await Reject(()=>docs.ReuseTrustedAsync(target.Id,source.Id,asset.Id,hash,target.RowVersion,"Fixture",admin),"original attestation remains required");originalGrant.Detail=binding;await db.SaveChangesAsync();
        var reused=await docs.ReuseTrustedAsync(target.Id,source.Id,asset.Id,hash,target.RowVersion,"Separate Excel record, same internal source",admin);
        Check(reused.Id==asset.Id&&await db.MediaAssets.CountAsync()==1&&await db.EditorialDocuments.CountAsync()==1,"reuse retains one byte object and original proof");
        Check(await db.AuditLogs.CountAsync(x=>x.Action==TrustedInternalDocumentPolicy.ReuseAction&&x.ContentId==target.Id&&x.Actor==admin.UserId)==1,"actual administrator audit bound to destination");
        await docs.ReuseTrustedAsync(target.Id,source.Id,asset.Id,hash,target.RowVersion,"Separate Excel record, same internal source",admin);
        Check(await db.AuditLogs.CountAsync(x=>x.Action==TrustedInternalDocumentPolicy.ReuseAction)==1,"repeat is idempotent");
        Check(await controller.Result(target.Id,hash,default) is JsonResult,"result endpoint reconciles explicit reuse");
        Check(!await TrustedInternalDocumentPolicy.HasReuseGrantAsync(db,asset,other.Id,policy)&&!await TrustedInternalDocumentPolicy.HasReuseGrantAsync(db,asset,target.Id,new MediaUploadOptions()),"grant cannot escape destination or enabled policy");
        var draft=new ContentDraft{ContentId=target.Id};db.ContentDrafts.Add(draft);
        var workflow=new WorkflowService(db,new HtmlPolicy(),policy,storage);
        var input=new DraftInput{Kind=ContentKind.StaticPage,Title="Separate STT 186",Metadata=new(){PublicationDate=new DateOnly(2026,11,30)},Assets=[new(){MediaAssetId=asset.Id,Usage="Attachment",Caption="186_original.pdf"}]};
        await (Task)typeof(WorkflowService).GetMethod("ApplyAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(workflow,[draft,input,admin,target,false])!;await db.SaveChangesAsync();
        Check(draft.Assets.Single().MediaAssetId==asset.Id&&target.Id!=source.Id&&draft.Metadata.PublicationDate==new DateOnly(2026,11,30),"separate draft attaches exact asset and preserves record date");
        Check(await controller.Items(target.Id) is JsonResult,"recipient attachment listing works");
        var download=await controller.Download(asset.Id,target.Id);Check(download is FileStreamResult,"authorized recipient can download");if(download is FileStreamResult file)await file.FileStream.DisposeAsync();
        var publicController=new MediaController(db,storage,policy,null!){ControllerContext=new(){HttpContext=new DefaultHttpContext()}};
        Check(await publicController.File(asset.Id) is NotFoundResult,"reuse never publishes an unpublished file");
        Check(asset.ScanStatus=="NotScanned"&&Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(storage.ResolveMedia(asset.StorageKey))))==hash,"no fake scan or changed bytes");
        await File.AppendAllTextAsync(storage.ResolveMedia(asset.StorageKey),"tamper");
        await Reject(()=>docs.ReuseTrustedAsync(target.Id,source.Id,asset.Id,hash,target.RowVersion,"Fixture",admin),"tampering denied even on repeat");
        Console.WriteLine($"TRUSTED REUSE {passed} PASS; isolated SQLite/service/controller only, no production SQL or IIS.");
    }
}
internal sealed class ReuseTestEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Staging";
    public string ApplicationName { get; set; } = "ReuseFixture";
    public string WebRootPath { get; set; } = "";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
