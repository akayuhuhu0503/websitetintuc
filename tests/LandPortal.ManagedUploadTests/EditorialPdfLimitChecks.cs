using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Writer;

internal static class EditorialPdfLimitChecks
{
    sealed record Input(int Stt, string Path, string ExpectedSha256, int ExpectedPages, string SourceEntry);
    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("--editorial-pdf-limits five-inputs.json new-report.json");
        if (File.Exists(args[2])) throw new IOException("Report exists");
        var inputs = JsonSerializer.Deserialize<Input[]>(await File.ReadAllBytesAsync(args[1]))!;
        if (!inputs.Select(x => x.Stt).Order().SequenceEqual(new[] { 1, 26, 28, 29, 32 })) throw new InvalidDataException("Only the five reviewed PDFs");
        var root = Path.Combine(PortalConfiguration.FindWorkspace(), ".local", "tests", "editorial-pdf-" + Guid.NewGuid().ToString("N"));
        var storage = new StorageOptions { MediaRoot = Path.Combine(root,"media"), TempRoot = Path.Combine(root,"temp"), SnapshotRoot=Path.Combine(root,"snapshots"), KeysRoot=Path.Combine(root,"keys") };
        Directory.CreateDirectory(storage.TempRoot); Directory.CreateDirectory(storage.MediaRoot);
        // Only the in-memory SQLite fixture below is opened. This config string is solely the existing test-profile identity guard.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> {
            ["ConnectionStrings:Portal"]="Server=(localdb)\\MSSQLLocalDB;Database=LandPortal_Test;Integrated Security=true",
            ["Sql:ExpectedDatabase"]="LandPortal_Test",["Sql:CompatibilityLevel"]="160",
            ["Storage:MediaRoot"]=storage.MediaRoot,["Storage:TempRoot"]=storage.TempRoot,["Storage:SnapshotRoot"]=storage.SnapshotRoot,["Storage:KeysRoot"]=storage.KeysRoot });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.Configure<KestrelServerOptions>(o=>o.Limits.MaxRequestBodySize=20*1024*1024);
        using var connection=new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options=new DbContextOptionsBuilder<PortalDbContext>().UseSqlite(connection).Options;
        builder.Services.AddScoped<PortalDbContext>(_=>new FixtureDb(options));
        builder.Services.AddSingleton(storage);
        builder.Services.AddSingleton(new MediaUploadOptions { ApprovedManagedDocumentsWithoutAv=true });
        builder.Services.AddScoped<CurrentPermissionService>(); builder.Services.AddScoped<MediaService>(); builder.Services.AddScoped<EditorialDocumentService>();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuthentication>("fixture",_=>{});
        builder.Services.AddAuthorization(); builder.Services.AddAntiforgery(o=>o.HeaderName="X-CSRF-TOKEN");
        builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(storage.KeysRoot));
        builder.Services.AddControllersWithViews(o=>o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddApplicationPart(typeof(EditorialDocumentsController).Assembly);
        await using var app=builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        app.MapGet("/test-token",(HttpContext c,IAntiforgery a)=>a.GetAndStoreTokens(c).RequestToken!).RequireAuthorization();
        app.MapPost("/ordinary-upload",async (HttpContext c)=> { await c.Request.Body.CopyToAsync(Stream.Null); return Results.Ok(); });
        var originalTemp=Environment.GetEnvironmentVariable("ASPNETCORE_TEMP"); Environment.SetEnvironmentVariable("ASPNETCORE_TEMP",storage.TempRoot);
        var passed=new List<string>();
        void Check(bool ok,string name) { if(!ok)throw new Exception("FAIL "+name); passed.Add(name); Console.WriteLine("PASS "+name); }
        async Task Reject(Func<Task> action,string name)
        {
            try { await action(); } catch(Exception e) when(e is MediaUploadException or InvalidDataException or OperationCanceledException) { Check(true,name);return; }
            throw new Exception("FAIL accepted "+name);
        }
        var contentId=Guid.NewGuid();
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PortalDbContext>(); await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new PortalUser { Id="pdf-owner",UserName="pdf-owner" },new PortalUser { Id="pdf-outsider",UserName="pdf-outsider" });
            foreach(var permission in new[]{Permissions.EditOwn,Permissions.ManageMedia}) db.UserClaims.Add(new IdentityUserClaim<string>{UserId="pdf-owner",ClaimType="permission",ClaimValue=permission});
            db.Contents.Add(new Content{Id=contentId,OwnerUserId="pdf-owner",Kind=ContentKind.StaticPage}); await db.SaveChangesAsync();
        }
        try
        {
            await app.StartAsync();
            using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}) { BaseAddress=new Uri(app.Urls.Single()), Timeout=TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.ExpectContinue=true;
            client.DefaultRequestHeaders.Add("X-Fixture-User","pdf-owner");
            var token=await client.GetStringAsync("/test-token");
            async Task<HttpResponseMessage> Upload(string path,string name,string mime,string? csrf)
            {
                using var body=new MultipartFormDataContent(); using var file=File.OpenRead(path);
                var part=new StreamContent(file);part.Headers.ContentType=new MediaTypeHeaderValue(mime);body.Add(part,"file",name);
                using var request=new HttpRequestMessage(HttpMethod.Post,$"/admin/documents/{contentId}?format=json"){Content=body};
                if(csrf!=null)request.Headers.Add("X-CSRF-TOKEN",csrf);
                return await client.SendAsync(request);
            }
            foreach(var item in inputs)
            {
                using(var source=File.OpenRead(item.Path)) Check(Convert.ToHexStringLower(SHA256.HashData(source))==item.ExpectedSha256,$"STT {item.Stt} source SHA");
                using var response=await Upload(item.Path,Path.GetFileName(item.Path),"application/pdf",token);
                if(response.StatusCode!=HttpStatusCode.OK)throw new Exception($"STT {item.Stt} HTTP {(int)response.StatusCode}: "+(await response.Content.ReadAsStringAsync()).Split('\n')[0]);
                using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var id=json.RootElement.GetProperty("id").GetGuid();
                Check(json.RootElement.GetProperty("scanStatus").GetString()=="NotScanned",$"STT {item.Stt} actual HTTP upload NotScanned");
                using var download=await client.GetAsync($"/admin/documents/file/{id}",HttpCompletionOption.ResponseHeadersRead);
                await using var stream=await download.Content.ReadAsStreamAsync();
                Check(download.IsSuccessStatusCode && Convert.ToHexStringLower(await SHA256.HashDataAsync(stream))==item.ExpectedSha256,$"STT {item.Stt} guarded readback hash");
                using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                var proof=await db.EditorialDocuments.SingleAsync(x=>x.MediaAssetId==id);
                Check(proof.ScanState==InquiryScanState.NotScanned && proof.Attempts==0 && proof.ScannedSha256==null && !await db.Contents.AnyAsync(x=>x.IsPublic),$"STT {item.Stt} no fabricated scan/publication");
            }
            using(var payload=File.OpenRead(inputs.Single(x=>x.Stt==28).Path))
            using(var response=await client.PostAsync("/ordinary-upload",new StreamContent(payload)))
                Check(response.StatusCode==HttpStatusCode.RequestEntityTooLarge,"ordinary endpoint retains 20 MiB HTTP cap");
            var media=new MediaService(null!,null!,new MediaUploadOptions());
            string Pdf(int pages)
            {
                using var writer=new PdfDocumentBuilder();for(var n=0;n<pages;n++)writer.AddPage(100,100);
                var path=Path.Combine(root,$"pages-{pages}.pdf");File.WriteAllBytes(path,writer.Build());return path;
            }
            var max=Pdf(2000); var over=Pdf(2001);var small=Pdf(1);
            Check(await EditorialDocumentPolicy.ValidateAsync(max,"application/pdf",media,true)==2000,"2,000 pages accepted at boundary");
            await Reject(()=>EditorialDocumentPolicy.ValidateAsync(over,"application/pdf",media,true),"2,001 pages rejected");
            await Reject(()=>Task.Run(()=>media.ValidateContent(max,"application/pdf",CancellationToken.None)),"ordinary MediaService still rejects above 1,000 pages");
            await Reject(()=>EditorialDocumentPolicy.ValidateAsync(small,"application/pdf",media,true,new CancellationToken(true)),"cancellation is not acceptance");
            var slot=(SemaphoreSlim)typeof(EditorialDocumentPolicy).GetField("PdfSlot",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            await slot.WaitAsync(); try { await Reject(()=>EditorialDocumentPolicy.ValidateAsync(small,"application/pdf",media,true),"concurrent PDF rejected instead of unbounded parser queue"); } finally { slot.Release(); }
            foreach(var mime in new[]{OfficeDocumentPolicy.Docx,OfficeDocumentPolicy.Xlsx})
            {
                EditorialDocumentPolicy.RequireSize(10*1024*1024,mime);
                await Reject(()=>Task.Run(()=>EditorialDocumentPolicy.RequireSize(10*1024*1024+1,mime)),mime+" still capped at 10 MiB");
            }
            EditorialDocumentPolicy.RequireSize(EditorialDocumentPolicy.PdfMaximum,"application/pdf");
            var sparse=Path.Combine(root,"over-size.pdf");using(var f=File.Create(sparse))f.SetLength(EditorialDocumentPolicy.PdfMaximum+1);
            using(var response=await Upload(sparse,"over-size.pdf","application/pdf",token)) Check(response.StatusCode==HttpStatusCode.RequestEntityTooLarge,"HTTP PDF 104 MiB + 1 byte rejected");
            File.Delete(sparse);
            using(var response=await Upload(small,"fixture.pdf","application/pdf",null)) Check(response.StatusCode==HttpStatusCode.BadRequest,"HTTP missing CSRF rejected");
            client.DefaultRequestHeaders.Remove("X-Fixture-User");client.DefaultRequestHeaders.Add("X-Fixture-User","pdf-outsider");
            var outsiderToken=await client.GetStringAsync("/test-token");
            using(var response=await Upload(small,"fixture.pdf","application/pdf",outsiderToken))Check(response.StatusCode==HttpStatusCode.Forbidden,"HTTP insufficient permission rejected");
            client.DefaultRequestHeaders.Remove("X-Fixture-User");
            using(var response=await Upload(small,"fixture.pdf","application/pdf",null))Check(response.StatusCode==HttpStatusCode.Unauthorized,"HTTP anonymous rejected");
            var fake=Path.Combine(root,"fake.pdf");await File.WriteAllTextAsync(fake,"%PDF-1.4 not a real document %%EOF");
            await Reject(()=>EditorialDocumentPolicy.ValidateAsync(fake,"application/pdf",media,true),"fake PDF rejected");
            var active=Path.Combine(root,"active.pdf");
            using(var output=new MemoryStream())
            {
                void Write(string s)=>output.Write(System.Text.Encoding.ASCII.GetBytes(s));
                Write("%PDF-1.4\n");var offsets=new List<long>();
                foreach(var value in new[]{"<< /Type /Catalog /Pages 2 0 R /OpenAction 4 0 R >>","<< /Type /Pages /Kids [3 0 R] /Count 1 >>","<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>","<< /S /JavaScript /J#53 (bad) >>"})
                { offsets.Add(output.Position);Write(offsets.Count+" 0 obj\n"+value+"\nendobj\n"); }
                var xref=output.Position;Write("xref\n0 5\n0000000000 65535 f \n");foreach(var offset in offsets)Write(offset.ToString("D10")+" 00000 n \n");
                Write("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n"+xref+"\n%%EOF\n");await File.WriteAllBytesAsync(active,output.ToArray());
            }
            media.ValidateContent(active,"application/pdf",CancellationToken.None);
            await Reject(()=>EditorialDocumentPolicy.ValidateAsync(active,"application/pdf",media,true),"valid PDF with escaped JavaScript still rejected");
            using(var scope=app.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                Check(await db.MediaAssets.CountAsync()==5 && await db.EditorialDocuments.CountAsync()==5,"rejected uploads persist no extra records");
                Check(await db.AuditLogs.CountAsync(x=>x.Action=="document.uploaded")==5,"all five uploads audited");
            }
            var upload=typeof(EditorialDocumentsController).GetMethod(nameof(EditorialDocumentsController.Upload))!;
            Check((long)upload.CustomAttributes.Single(x=>x.AttributeType==typeof(RequestSizeLimitAttribute)).ConstructorArguments[0].Value! ==EditorialDocumentPolicy.RequestMaximum && upload.GetCustomAttribute<RequestFormLimitsAttribute>()!.MemoryBufferThreshold==65536,"multipart bounded to 105 MiB, spills above 64 KiB");
            await File.WriteAllTextAsync(args[2],JsonSerializer.Serialize(new{passed=passed.Count,checks=passed,actualHttp=true,database="SQLite isolated fixture",productionWrites=0,root},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally { await app.StopAsync();Environment.SetEnvironmentVariable("ASPNETCORE_TEMP",originalTemp); }
    }
    sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var name=Request.Headers["X-Fixture-User"].ToString();
            return Task.FromResult(name is "pdf-owner" or "pdf-outsider"
                ? AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,name)],Scheme.Name)),Scheme.Name))
                : AuthenticateResult.NoResult());
        }
    }
}
