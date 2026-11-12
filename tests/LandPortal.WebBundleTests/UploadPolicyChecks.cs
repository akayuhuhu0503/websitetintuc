using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using SkiaSharp;
using UglyToad.PdfPig.Writer;

internal static class UploadPolicyChecks
{
    public static async Task Run()
    {
        var passed=0;
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException("FAIL "+label);Console.WriteLine("PASS "+(++passed)+" "+label);}
        var root=Path.Combine(Path.GetTempPath(),"LandPortal-UploadPolicy-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // Every setting is synthetic. Configuration parsing validates UAT, but no SQL connection is opened.
        var data=new Dictionary<string,object?>
        {
            ["DOTNET_ENVIRONMENT"]="Staging",["ASPNETCORE_ENVIRONMENT"]="Staging",["WorkspaceRoot"]=root,
            ["Deployment"]=new{Profile="UAT",ExpectedSqlServer="fixture.invalid",ExpectedSqlServerIdentity="FIXTURE",ExpectedHost="fixture.invalid"},
            ["ConnectionStrings"]=new{Portal="Server=fixture.invalid;Database=LandPortal_FixtureUat;Integrated Security=True;Encrypt=True;TrustServerCertificate=False"},
            ["Sql"]=new{ExpectedDatabase="LandPortal_FixtureUat"},["Runtime"]=new{RolePassword="Synthetic fixture value only, never a credential"},
            ["Public"]=new{BaseUrl="https://fixture.invalid/"},
            ["Storage"]=new{MediaRoot=Path.Combine(root,"media"),TempRoot=Path.Combine(root,"temp"),SnapshotRoot=Path.Combine(root,"snapshots"),KeysRoot=Path.Combine(root,"keys")},
            ["Scanner"]=new{Provider="Disabled"}
        };
        IConfigurationRoot Config(object? flag,bool videoEnabled=true)
        {
            data["Media"]=new{AllowManagedImageWithoutAv=flag,AllowManagedDocumentsWithoutAv=flag};
            data["Video"]=new{Enabled=videoEnabled,AllowManagedVideoWithoutAv=flag};
            return new ConfigurationBuilder().AddJsonStream(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(data))).Build();
        }
        foreach(var flag in new object[]{true,"True","TRUE","true"})
        {
            var c=Config(flag);
            Check(MediaUploadOptions.FromConfiguration(c,"Staging") is {ApprovedManagedImageWithoutAv:true,ApprovedManagedDocumentsWithoutAv:true},"JSON "+JsonSerializer.Serialize(flag)+" enables image/doc under valid UAT");
            Check(VideoOptions.FromConfiguration(c,"Staging") is {Enabled:true,ApprovedManagedVideoWithoutAv:true},"JSON "+JsonSerializer.Serialize(flag)+" enables video under valid UAT");
        }
        Check(Config(true)["Media:AllowManagedImageWithoutAv"]=="True","Actual JSON boolean is exposed as capitalized True (old ordinal comparison failed)");
        foreach(var flag in new object?[]{false,"False","false","1","yes","",null})
        {
            var c=Config(flag);
            Check(MediaUploadOptions.FromConfiguration(c,"Staging") is {ApprovedManagedImageWithoutAv:false,ApprovedManagedDocumentsWithoutAv:false}
                && !VideoOptions.FromConfiguration(c,"Staging").ApprovedManagedVideoWithoutAv,"False/invalid/missing flag remains closed: "+JsonSerializer.Serialize(flag));
        }
        Check(!VideoOptions.FromConfiguration(Config(true,false),"Staging").Enabled,"Explicit Video.Enabled=false stays disabled");
        foreach(var environment in new[]{"Production","Development"})
        {
            var mediaDenied=false;var videoDenied=false;
            try{mediaDenied=!MediaUploadOptions.FromConfiguration(Config(true),environment).ApprovedManagedImageWithoutAv;}catch(InvalidOperationException){mediaDenied=true;}
            try{videoDenied=!VideoOptions.FromConfiguration(Config(true),environment).ApprovedManagedVideoWithoutAv;}catch(InvalidOperationException){videoDenied=true;}
            Check(mediaDenied&&videoDenied,environment+" cannot inherit UAT no-AV policy");
        }
        var invalid=Config(true);invalid["Public:BaseUrl"]="http://fixture.invalid/";
        try{MediaUploadOptions.FromConfiguration(invalid,"Staging");throw new Exception("HTTP origin accepted");}catch(InvalidOperationException){Check(true,"HTTPS guard retained");}

        foreach(var flag in new object[]{true,"True",false})
        {
            var c=Config(flag);var policy=MediaUploadOptions.FromConfiguration(c,"Staging");
            var storage=StorageOptions.FromConfiguration(c);
            foreach(var path in new[]{storage.MediaRoot,storage.TempRoot,storage.KeysRoot,storage.SnapshotRoot})Directory.CreateDirectory(path);
            using var sql=new SqliteConnection("Data Source=:memory:");await sql.OpenAsync();
            await using var db=new PolicyDb(new DbContextOptionsBuilder<PortalDbContext>().UseSqlite(sql).Options);await db.Database.EnsureCreatedAsync();
            db.Users.Add(new PortalUser{Id="fixture",UserName="fixture"});
            db.Users.Add(new PortalUser{Id="outsider",UserName="outsider"});
            foreach(var permission in new[]{Permissions.ManageMedia,Permissions.ManageSite,Permissions.EditOwn,Permissions.ViewOwn})db.UserClaims.Add(new IdentityUserClaim<string>{UserId="fixture",ClaimType="permission",ClaimValue=permission});
            var article=new Content{OwnerUserId="fixture",Kind=ContentKind.Article};db.Contents.Add(article);await db.SaveChangesAsync();
            var permissions=new CurrentPermissionService(db);var media=new MediaService(db,storage,policy,c);
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Staging",Args=[]});
            builder.Logging.ClearProviders();builder.Configuration.AddConfiguration(c);builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton<PortalDbContext>(db);builder.Services.AddSingleton(storage);builder.Services.AddSingleton(policy);builder.Services.AddSingleton(media);builder.Services.AddSingleton(permissions);
            builder.Services.AddSingleton(new EditorialDocumentService(db,storage,media,permissions,policy));
            builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions,PolicyAuthentication>("fixture",null);builder.Services.AddAuthorization();
            builder.Services.AddAntiforgery(o=>o.HeaderName="X-CSRF-TOKEN");
            builder.Services.AddControllersWithViews(o=>o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddApplicationPart(typeof(AdminMediaController).Assembly);
            await using var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();
            app.MapGet("/fixture-token",(HttpContext ctx,IAntiforgery anti)=>anti.GetAndStoreTokens(ctx).RequestToken!);await app.StartAsync();
            using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new()}){BaseAddress=new Uri(app.Urls.Single())};
            var token=await client.GetStringAsync("/fixture-token");
            using var outsiderTokenRequest=new HttpRequestMessage(HttpMethod.Get,"/fixture-token");outsiderTokenRequest.Headers.Add("X-Fixture-Outsider","true");
            using var outsiderTokenResponse=await client.SendAsync(outsiderTokenRequest);var outsiderToken=await outsiderTokenResponse.Content.ReadAsStringAsync();
            async Task<HttpResponseMessage> Upload(string url,string name,string mime,byte[] bytes,bool csrf=true,bool outsider=false)
            {
                using var request=new HttpRequestMessage(HttpMethod.Post,url);
                var form=new MultipartFormDataContent();var body=new ByteArrayContent(bytes);body.Headers.ContentType=new(mime);form.Add(body,"file",name);request.Content=form;
                if(csrf)request.Headers.Add("X-CSRF-TOKEN",outsider?outsiderToken:token);if(outsider)request.Headers.Add("X-Fixture-Outsider","true");
                return await client.SendAsync(request);
            }
            byte[] Image(SKEncodedImageFormat format){using var bitmap=new SKBitmap(9,6);bitmap.Erase(SKColors.DarkRed);using var image=SKImage.FromBitmap(bitmap);using var bytes=image.Encode(format,90);return bytes.ToArray();}
            using var pdf=new PdfDocumentBuilder();pdf.AddPage(100,100);var pdfBytes=pdf.Build();
            var payloads=new[]{("fixture.png","image/png",Image(SKEncodedImageFormat.Png)),("fixture.jpg","image/jpeg",Image(SKEncodedImageFormat.Jpeg)),("fixture.pdf","application/pdf",pdfBytes),("fixture.docx",OfficeDocumentPolicy.Docx,Office(true)),("fixture.xlsx",OfficeDocumentPolicy.Xlsx,Office(false))};
            foreach(var (name,mime,bytes) in payloads)
            {
                var url=mime.StartsWith("image/")?"/admin/media/upload?site=true":"/admin/documents/"+article.Id;
                using var response=await Upload(url,name,mime,bytes);
                if(flag is false){Check(response.StatusCode==HttpStatusCode.ServiceUnavailable,"Disabled policy retains real scanner gate: "+name);continue;}
                Check(response.StatusCode==(mime.StartsWith("image/")?HttpStatusCode.OK:HttpStatusCode.Redirect),"HTTP "+name+" succeeds with JSON "+JsonSerializer.Serialize(flag)+" without scanner");
                var asset=await db.MediaAssets.SingleAsync(x=>x.OriginalName==name);
                Check(asset.ScanStatus=="NotScanned"&&Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(storage.ResolveMedia(asset.StorageKey))))==asset.Sha256,name+" stores truthful status and matching SHA");
                if(!mime.StartsWith("image/")){
                    using var read=await client.GetAsync("/admin/documents/file/"+asset.Id);
                    Check(read.StatusCode==HttpStatusCode.OK&&read.Content.Headers.ContentDisposition?.DispositionType=="attachment"&&read.Headers.GetValues("X-Content-Type-Options").Single()=="nosniff"&&(await read.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes),name+" authorized download keeps bytes/attachment/nosniff");
                }
            }
            if(flag is true)
            {
                using var fake=await Upload("/admin/media/upload?site=true","fake.png","image/png","not an image"u8.ToArray());Check(fake.StatusCode==HttpStatusCode.BadRequest,"Banner fake PNG denied");
                using var noCsrf=await Upload("/admin/media/upload?site=true","csrf.png","image/png",payloads[0].Item3,false);Check(noCsrf.StatusCode==HttpStatusCode.BadRequest,"Banner missing CSRF denied");
                using var outsider=await Upload("/admin/media/upload?site=true","outsider.png","image/png",payloads[0].Item3,true,true);Check(outsider.StatusCode==HttpStatusCode.Forbidden,"Banner unprivileged account denied");
                using var fakeDoc=await Upload("/admin/documents/"+article.Id,"fake.pdf","application/pdf","not a PDF"u8.ToArray());Check(fakeDoc.StatusCode==HttpStatusCode.BadRequest,"Document fake PDF denied");
                using var docNoCsrf=await Upload("/admin/documents/"+article.Id,"csrf.pdf","application/pdf",pdfBytes,false);Check(docNoCsrf.StatusCode==HttpStatusCode.BadRequest,"Document missing CSRF denied");
                using var docOutsider=await Upload("/admin/documents/"+article.Id,"outsider.pdf","application/pdf",pdfBytes,true,true);Check(docOutsider.StatusCode==HttpStatusCode.Forbidden,"Document unprivileged account denied");
            }
            if(flag is false)Check(await db.MediaAssets.CountAsync()==0,"Disabled policies never persist media");
            await app.StopAsync();
        }
        Console.WriteLine($"TOTAL {passed} PASS: focused JSON policy + real HTTP Banner/documents, isolated SQLite; no SQL Server or IIS.");
    }
    private static byte[] Office(bool word)
    {
        using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)){
            void Entry(string name,string content){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(content);}
            var main=word?"word/document.xml":"xl/workbook.xml";var type=(word?OfficeDocumentPolicy.Docx:OfficeDocumentPolicy.Xlsx)+".main+xml";
            Entry("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/"+main+"\" ContentType=\""+type+"\"/></Types>");
            Entry("_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\""+main+"\"/></Relationships>");
            Entry(main,word?"<document xmlns=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><body><p><r><t>Fixture</t></r></p></body></document>":"<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheets/></workbook>");
        }return memory.ToArray();
    }
    private sealed class PolicyAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e):AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e)
    {protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,Request.Headers.ContainsKey("X-Fixture-Outsider")?"outsider":"fixture")],"fixture")),"fixture")));}
    private sealed class PolicyDb(DbContextOptions<PortalDbContext> options):PortalDbContext(options)
    {protected override void OnModelCreating(ModelBuilder b){base.OnModelCreating(b);foreach(var entity in b.Model.GetEntityTypes()){foreach(var index in entity.GetIndexes())index.SetFilter(null);foreach(var p in entity.GetProperties()){p.SetColumnType(null);p.SetDefaultValueSql(null);p.SetComputedColumnSql(null);if(p.ClrType==typeof(byte[])&&p.IsConcurrencyToken)p.ValueGenerated=ValueGenerated.Never;if(p.ClrType==typeof(DateTimeOffset))p.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());}}}}
}
