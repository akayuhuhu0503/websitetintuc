using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Only PDF response authorization/disposition and weekly labels/filtering. No upload/workflow/video E2E.
static class PdfWeekChecks
{
    public static async Task Run(string checkout,string output)
    {
        var root=Path.GetFullPath(checkout);var run=Path.GetFullPath(output);
        if(!run.StartsWith(Path.Combine(root,".local","tests","pdf-week-"),StringComparison.OrdinalIgnoreCase)||Directory.Exists(run))throw new Exception("New isolated fixture required");
        Directory.CreateDirectory(run);
        var instance="LPPdfWeek_"+Guid.NewGuid().ToString("N")[..12];
        async Task LocalDb(string action)
        {
            var start=new ProcessStartInfo("SqlLocalDB.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(action);start.ArgumentList.Add(instance);
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();await stdout;await stderr;if(process.ExitCode!=0)throw new Exception("Isolated LocalDB "+action+" failed");
        }
        var checks=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
        WebApplication? app=null;bool started=false;
        try
        {
            await LocalDb("create");await LocalDb("start");started=true;
            var sql=new SqlConnectionStringBuilder{DataSource=@"(localdb)\"+instance,InitialCatalog="LandPortal_Test",IntegratedSecurity=true,Encrypt=true,TrustServerCertificate=true,Pooling=false};
            await using(var connection=new SqlConnection(new SqlConnectionStringBuilder(sql.ConnectionString){InitialCatalog="master"}.ConnectionString))
            {
                await connection.OpenAsync();using var command=connection.CreateCommand();
                command.CommandText="SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB')),CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),DB_ID('LandPortal_Test')";
                using(var r=await command.ExecuteReaderAsync()){await r.ReadAsync();Check(r.GetInt32(0)==1&&r.GetString(1).Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase)&&r.IsDBNull(2),"New local-only SQL target; no operational database");}
                command.CommandText="CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'"+Path.Combine(run,"test.mdf").Replace("'","''")+"') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'"+Path.Combine(run,"test.ldf").Replace("'","''")+"')";
                await command.ExecuteNonQueryAsync();
            }
            var options=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(sql.ConnectionString).Options;
            var role=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await using(var db=new PortalDbContext(options))
            {
                await db.Database.MigrateAsync();Check((await db.Database.GetAppliedMigrationsAsync()).Count()==18,"Isolated schema18");
                await RuntimeRoleInterceptor.ProvisionAsync(db,role);
                db.Users.Add(new(){Id="fixture-owner",UserName="fixture-owner"});db.Categories.Add(new(){Id=76,Name="Lịch tuần",Slug="lich-tuan"});await db.SaveChangesAsync();
            }
            var storage=new StorageOptions{MediaRoot=Path.Combine(run,"media"),TempRoot=Path.Combine(run,"temp"),SnapshotRoot=Path.Combine(run,"snapshots"),KeysRoot=Path.Combine(run,"keys")};
            foreach(var p in new[]{storage.MediaRoot,storage.TempRoot,storage.SnapshotRoot,storage.KeysRoot})Directory.CreateDirectory(p);
            var policy=new MediaUploadOptions{ApprovedManagedDocumentsWithoutAv=true};
            var activePolicy=policy; // Test-only DI toggle; no operational environment/profile is changed.
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=[],ContentRootPath=Path.Combine(root,"src","Web"),WebRootPath=Path.Combine(root,"src","Web","wwwroot"),EnvironmentName="Staging"});
            builder.Configuration.Sources.Clear();builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{{"Public:BaseUrl","http://127.0.0.1/"},{"Runtime:RolePassword",role}});
            builder.Logging.ClearProviders();builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddScoped(_=>new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(sql.ConnectionString).AddInterceptors(new RuntimeRoleInterceptor(builder.Configuration)).Options));
            builder.Services.AddSingleton(storage);builder.Services.AddScoped(_=>activePolicy);builder.Services.AddSingleton(new VideoOptions());builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            builder.Services.AddMemoryCache();builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<PublicContentQuery>();builder.Services.AddScoped<PublicSiteService>();builder.Services.AddScoped<SiteConfigurationService>();builder.Services.AddScoped<PublicNavigation>();
            builder.Services.AddScoped<CurrentPermissionService>();builder.Services.AddScoped<ArticleDiscovery>();builder.Services.AddScoped<DocumentUsage>();builder.Services.AddScoped<PublicTraffic>();builder.Services.AddSingleton<PublicUrlPolicy>();
            builder.Services.AddScoped<InteractionService>();builder.Services.AddSingleton(new InteractionMailOptions());
            builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuth>("Fixture",_=>{});builder.Services.AddAuthorization();
            builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PortalController).Assembly);
            app=builder.Build();app.Use(async(ctx,next)=>{ctx.Request.Headers.UserAgent="pdf-week-fixture-bot";await next();});app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
            using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(app.Urls.Single())};var parser=new HtmlParser();
            async Task<(Guid Content,Guid Revision)> Article(string title,int? year=null,int? week=null,bool published=true)
            {
                await using var db=new PortalDbContext(options);var c=new Content{Kind=ContentKind.StaticPage,OwnerUserId="fixture-owner",PrimaryCategoryId=76};db.Contents.Add(c);await db.SaveChangesAsync();
                var r=new ContentRevision{ContentId=c.Id,PrimaryCategoryId=76,Title=title,BodyHtml="<p>Fixture only</p>",Metadata=new(){PublicationDate=new DateOnly(2026,11,4),WeeklyYear=year,WeeklyWeek=week},ContentHash=Guid.NewGuid().ToString("N")};
                db.Revisions.Add(r);await db.SaveChangesAsync();c.CurrentRevisionId=r.Id;c.PublishedRevisionId=published?r.Id:null;c.IsPublic=published;c.PublishedAtUtc=DateTimeOffset.UtcNow;c.State=published?WorkflowState.Published:WorkflowState.Draft;await db.SaveChangesAsync();return(c.Id,r.Id);
            }
            var good=await Article("Tuần 45. Từ ngày 02/11/2026 đến ngày 08/11/2026");
            var mismatch=await Article("Tuần 44. Từ ngày 02/11/2026 đến ngày 08/11/2026");
            var metadata=await Article("Metadata fixture: Tuần 44. Từ ngày 02/11/2026 đến ngày 08/11/2026",2026,46);
            var rollover=await Article("Tuần 1. Từ ngày 29/12/2025 đến ngày 31/12/2025");
            var hidden=await Article("PRIVATE Tuần 45. Từ ngày 02/11/2026 đến ngày 08/11/2026",published:false);
            async Task<Guid> Asset((Guid Content,Guid Revision) item,string mime,byte[] bytes,string name)
            {
                await using var db=new PortalDbContext(options);var asset=new MediaAsset{Origin="EditorialDocument",MimeType=mime,ScanStatus="NotScanned",ValidationStatus=MediaUploadOptions.ManagedDocumentNoAvValidation,OriginalName=name,ByteLength=bytes.Length,Sha256=Convert.ToHexStringLower(SHA256.HashData(bytes))};
                asset.StorageKey=asset.Id.ToString("N")+".bin";await File.WriteAllBytesAsync(storage.ResolveMedia(asset.StorageKey),bytes);
                db.MediaAssets.Add(asset);db.EditorialDocuments.Add(new(){MediaAssetId=asset.Id,ContentId=item.Content,OwnerId="fixture-owner"});db.ContentAssets.Add(new(){RevisionId=item.Revision,MediaAssetId=asset.Id,Usage="Attachment"});await db.SaveChangesAsync();return asset.Id;
            }
            using var pdfBuilder=new UglyToad.PdfPig.Writer.PdfDocumentBuilder();pdfBuilder.AddPage(100,100);var pdf=pdfBuilder.Build();
            var pdfId=await Asset(good,"application/pdf",pdf,"Lịch tuần.pdf");ManagedPdfPolicy.Validate(storage.ResolveMedia(pdfId.ToString("N")+".bin"));
            var hiddenPdf=await Asset(hidden,"application/pdf",pdf.Concat("\n"u8.ToArray()).ToArray(),"private.pdf");
            using(var response=await client.GetAsync("/media/"+pdfId))
            {
                Check(response.StatusCode==HttpStatusCode.OK&&response.Content.Headers.ContentType?.MediaType=="application/pdf"&&response.Content.Headers.ContentDisposition?.DispositionType is not "attachment"&&(await response.Content.ReadAsByteArrayAsync()).SequenceEqual(pdf),"Public no-AV PDF view is inline with exact bytes");
                Check(response.Headers.GetValues("X-Media-Scan-Status").Single()=="NotScanned"&&response.Headers.GetValues("X-Content-Type-Options").Single()=="nosniff"&&response.Headers.GetValues("Content-Security-Policy").Single()=="sandbox; default-src 'none'; frame-ancestors 'self'"&&response.Headers.GetValues("X-Frame-Options").Single()=="SAMEORIGIN"&&response.Headers.CacheControl?.NoStore==true,"Truthful status, sandbox, same-origin framing, nosniff and no-store retained");
            }
            using(var response=await client.GetAsync("/media/"+pdfId+"?download=true"))Check(response.StatusCode==HttpStatusCode.OK&&response.Content.Headers.ContentDisposition?.DispositionType=="attachment"&&(await response.Content.ReadAsByteArrayAsync()).SequenceEqual(pdf),"Explicit PDF download remains attachment");
            using(var request=new HttpRequestMessage(HttpMethod.Head,"/media/"+pdfId))using(var response=await client.SendAsync(request))Check(response.StatusCode==HttpStatusCode.OK&&response.Content.Headers.ContentLength==pdf.Length&&response.Content.Headers.ContentDisposition?.DispositionType is not "attachment","HEAD preserves inline semantics");
            using(var request=new HttpRequestMessage(HttpMethod.Get,"/media/"+pdfId)){request.Headers.Range=new RangeHeaderValue(0,31);using var response=await client.SendAsync(request);Check(response.StatusCode==HttpStatusCode.PartialContent&&(await response.Content.ReadAsByteArrayAsync()).SequenceEqual(pdf[..32]),"PDF browser Range returns exact requested bytes");}
            foreach(var (mime,ext) in new[]{(OfficeDocumentPolicy.Docx,"docx"),(OfficeDocumentPolicy.Xlsx,"xlsx")})
            {
                // Fixture of already-validated publication metadata; upload validation is unchanged and not rerun.
                var id=await Asset(good,mime,Encoding.UTF8.GetBytes("stored fixture "+ext),"fixture."+ext);
                using var response=await client.GetAsync("/media/"+id);Check(response.StatusCode==HttpStatusCode.OK&&response.Content.Headers.ContentDisposition?.DispositionType=="attachment",ext+" default remains download-only");
            }
            var article=parser.ParseDocument(await client.GetStringAsync("/bai-viet/"+good.Content));
            Check(article.QuerySelector($".attachments a[href='/media/{pdfId}']")?.TextContent.Contains("Xem tài liệu")==true&&article.QuerySelector($".attachments a[href='/media/{pdfId}?download=true&contentId={good.Content}']")!=null,"Actual Article Razor keeps separate View and Download links");
            Check(article.QuerySelector(".article-meta")!.TextContent.Contains("04/11/2026"),"PublicationDate remains unchanged");
            var selected=parser.ParseDocument(await client.GetStringAsync("/lich-tuan?year=2026&week=45"));
            Check(selected.QuerySelectorAll(".weekly-selected .weekly-articles h3 a").Select(x=>x.GetAttribute("href")).SequenceEqual(new[]{"/bai-viet/"+good.Content}),"SQL title prefilter and exact week key select only valid public week45");
            Check(selected.QuerySelector(".weekly-selected .weekly-article-meta a")?.TextContent=="Tuần 45/2026","Actual schedule partial label agrees with filter");
            Check(selected.QuerySelectorAll("#weekly-archive .weekly-articles li").Any(x=>x.QuerySelector("h3 a")?.GetAttribute("href")=="/bai-viet/"+mismatch.Content&&x.QuerySelector(".weekly-article-meta a")==null),"Conflicting title remains in archive without guessed label");
            Check(!selected.Body!.TextContent.Contains("PRIVATE"),"Private revision never appears in week or archive");
            var metaPage=parser.ParseDocument(await client.GetStringAsync("/lich-tuan?year=2026&week=46"));Check(metaPage.QuerySelector(".weekly-selected h3 a")?.GetAttribute("href")=="/bai-viet/"+metadata.Content&&metaPage.QuerySelector(".weekly-selected .weekly-article-meta a")?.TextContent=="Tuần 46/2026","Validated metadata has same precedence for label and SQL filter");
            var yearPage=parser.ParseDocument(await client.GetStringAsync("/lich-tuan?year=2026&week=1"));Check(yearPage.QuerySelector(".weekly-selected h3 a")?.GetAttribute("href")=="/bai-viet/"+rollover.Content,"SQL prefilter supports ISO year different from title calendar year");
            using(var response=await client.GetAsync("/media/"+hiddenPdf))Check(response.StatusCode==HttpStatusCode.NotFound,"Draft-only PDF denied");
            activePolicy=new MediaUploadOptions();using(var response=await client.GetAsync("/media/"+pdfId))Check(response.StatusCode==HttpStatusCode.NotFound,"Disabled no-AV policy denies unscanned PDF even when public");activePolicy=policy;
            async Task ChangeAsset(Action<MediaAsset,EditorialDocument> change){await using var db=new PortalDbContext(options);change(await db.MediaAssets.SingleAsync(x=>x.Id==pdfId),await db.EditorialDocuments.SingleAsync(x=>x.MediaAssetId==pdfId));await db.SaveChangesAsync();}
            await ChangeAsset((a,p)=>a.IsQuarantined=true);using(var response=await client.GetAsync("/media/"+pdfId))Check(response.StatusCode==HttpStatusCode.NotFound,"Quarantined PDF denied");await ChangeAsset((a,p)=>a.IsQuarantined=false);
            await ChangeAsset((a,p)=>a.ValidationStatus="PendingScan");using(var response=await client.GetAsync("/media/"+pdfId))Check(response.StatusCode==HttpStatusCode.NotFound,"Missing technical validation denied");await ChangeAsset((a,p)=>a.ValidationStatus=MediaUploadOptions.ManagedDocumentNoAvValidation);
            await ChangeAsset((a,p)=>p.ScanState=InquiryScanState.ScanFailed);using(var response=await client.GetAsync("/media/"+pdfId))Check(response.StatusCode==HttpStatusCode.NotFound,"Failed proof cannot become viewable");await ChangeAsset((a,p)=>p.ScanState=InquiryScanState.NotScanned);
            await using(var db=new PortalDbContext(options)){var c=await db.Contents.SingleAsync(x=>x.Id==good.Content);c.IsPublic=false;await db.SaveChangesAsync();}
            using(var request=new HttpRequestMessage(HttpMethod.Get,"/media/"+pdfId)){request.Headers.Range=new(0,31);request.Headers.IfModifiedSince=DateTimeOffset.UtcNow;using var response=await client.SendAsync(request);Check(response.StatusCode==HttpStatusCode.NotFound,"Withdrawal denied even on conditional Range request");}
            await using(var db=new PortalDbContext(options)){var c=await db.Contents.SingleAsync(x=>x.Id==good.Content);c.IsPublic=true;await db.SaveChangesAsync();}
            await File.AppendAllTextAsync(storage.ResolveMedia(pdfId.ToString("N")+".bin"),"tamper");using(var response=await client.GetAsync("/media/"+pdfId))Check(response.StatusCode==HttpStatusCode.NotFound,"Changed PDF bytes denied");
            await using(var db=new PortalDbContext(options)){var a=await db.MediaAssets.SingleAsync(x=>x.Id==pdfId);Check(a.IsQuarantined&&a.ScanStatus=="ScanFailed","Existing tamper quarantine remains enforced");}
            await File.WriteAllTextAsync(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks,instance,operationalSqlTouched=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{if(app!=null){await app.StopAsync();await app.DisposeAsync();}SqlConnection.ClearAllPools();if(started)await LocalDb("stop");}
    }
}
