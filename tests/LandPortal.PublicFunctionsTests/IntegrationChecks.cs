using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Reuse only this test project's completed disposable fixture. No import,
// upload, transcode, performance run, real account or operational config.
static class IntegrationChecks
{
    public static async Task Run(string checkout, string fixturePath, string outputPath)
    {
        var root=Path.GetFullPath(checkout);var fixture=Path.GetFullPath(fixturePath);var output=Path.GetFullPath(outputPath);
        var prefix=Path.Combine(root,".local","tests")+Path.DirectorySeparatorChar;
        var match=Regex.Match(Path.GetFileName(fixture),"^public-functions-(LPPublic_[a-f0-9]{12})$");
        if(!fixture.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!output.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!match.Success||Directory.Exists(output))throw new Exception("Owned fixture and new output directory required");
        using var prior=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture,"result.json")));
        if(prior.RootElement.GetProperty("passed").GetInt32()!=48)throw new Exception("Completed functions fixture required");
        using var ready=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture,"ready.json")));
        var videoId=ready.RootElement.GetProperty("videoId").GetGuid();var contentId=ready.RootElement.GetProperty("contentId").GetGuid();var pdfId=ready.RootElement.GetProperty("pdfId").GetGuid();
        var sql=new SqlConnectionStringBuilder {DataSource=@"(localdb)\"+match.Groups[1].Value,InitialCatalog="LandPortal_Test",IntegratedSecurity=true,Encrypt=true,TrustServerCertificate=true,Pooling=false};
        await using(var connection=new SqlConnection(sql.ConnectionString))
        {
            await connection.OpenAsync();using var command=connection.CreateCommand();
            command.CommandText="SELECT DB_NAME(),CONVERT(int,SERVERPROPERTY('IsLocalDB')),CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),(SELECT physical_name FROM sys.database_files WHERE file_id=1),(SELECT COUNT(*) FROM __EFMigrationsHistory)";
            using var reader=await command.ExecuteReaderAsync();await reader.ReadAsync();
            if(reader.GetString(0)!="LandPortal_Test"||reader.GetInt32(1)!=1||!reader.GetString(2).Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase)||!Path.GetFullPath(reader.GetString(3)).Equals(Path.Combine(fixture,"test.mdf"),StringComparison.OrdinalIgnoreCase)||reader.GetInt32(4)!=18)throw new Exception("Fixture SQL identity/file/schema mismatch");
        }
        Directory.CreateDirectory(output);
        // Deterministic synthetic test role secret, scoped to the allowlisted
        // local fixture. Never read or change any runtime/operator credentials.
        var role=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("public-integration-fixture-"+match.Groups[1].Value)));
        var operatorOptions=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(sql.ConnectionString).Options;
        await using(var db=new PortalDbContext(operatorOptions))await RuntimeRoleInterceptor.ProvisionAsync(db,role);
        var storage=new StorageOptions{MediaRoot=Path.Combine(fixture,"media"),SnapshotRoot=Path.Combine(fixture,"snapshots"),TempRoot=Path.Combine(fixture,"temp"),KeysRoot=Path.Combine(fixture,"keys")};
        var media=new MediaUploadOptions{ApprovedManagedDocumentsWithoutAv=true};var video=new VideoOptions{Enabled=true,ApprovedManagedVideoWithoutAv=true};
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=[],ContentRootPath=Path.Combine(root,"src","Web"),WebRootPath=Path.Combine(root,"src","Web","wwwroot"),EnvironmentName="Staging"});
        builder.Configuration.Sources.Clear();builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{{"Public:BaseUrl","http://127.0.0.1/"},{"Runtime:RolePassword",role}});
        builder.Logging.ClearProviders();builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton<ConnectionProof>();builder.Services.AddMemoryCache();
        builder.Services.AddScoped(sp=>new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(sql.ConnectionString).AddInterceptors(new RuntimeRoleInterceptor(builder.Configuration),sp.GetRequiredService<ConnectionProof>()).Options));
        builder.Services.AddSingleton(storage);builder.Services.AddSingleton(media);builder.Services.AddSingleton(video);builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddScoped<PublicContentQuery>();builder.Services.AddScoped<PublicSiteService>();builder.Services.AddScoped<PublicNavigation>();builder.Services.AddScoped<SiteConfigurationService>();builder.Services.AddScoped<CurrentPermissionService>();builder.Services.AddScoped<ArticleDiscovery>();builder.Services.AddScoped<DocumentUsage>();builder.Services.AddScoped<PublicTraffic>();builder.Services.AddSingleton<PublicUrlPolicy>();
        builder.Services.AddScoped<InteractionService>();builder.Services.AddSingleton(new InteractionMailOptions());builder.Services.AddSingleton<HtmlPolicy>();builder.Services.AddScoped<WorkflowService>();builder.Services.AddSingleton(new SchedulerOptions());builder.Services.AddScoped<SchedulerService>();builder.Services.AddSingleton<WebPublicationScheduler>();
        builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuth>("Fixture",_=>{});
        builder.Services.AddAuthorization(o=>{foreach(var p in new[]{Permissions.ManageSite,Permissions.PublishSite})o.AddPolicy(p,x=>x.RequireClaim("permission",p));});
        builder.Services.AddControllersWithViews(o=>o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddApplicationPart(typeof(HomeController).Assembly);
        await using var app=builder.Build();var proof=app.Services.GetRequiredService<ConnectionProof>();var completed=new ConcurrentDictionary<string,(int Opens,bool Closed)>();
        app.UseStaticFiles();app.UseRouting();app.UseAuthentication();app.UseAuthorization();
        app.Use(async(ctx,next)=>
        {
            ctx.Request.Headers.UserAgent="Public-integration-fixture-bot";ctx.Response.Headers["X-Fixture-Request"]=ctx.TraceIdentifier;
            var db=ctx.RequestServices.GetRequiredService<PortalDbContext>();
            try{await next();}finally{completed[ctx.TraceIdentifier]=(proof.Count(ctx.TraceIdentifier),db.Database.GetDbConnection().State==ConnectionState.Closed);}
        });
        app.UseMiddleware<DuePublicationMiddleware>();app.UseMiddleware<PublicRequestConnectionMiddleware>();app.MapControllers();await app.StartAsync();
        var url=app.Urls.Single();using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new(url)};
        var checks=new List<string>();void Check(bool valid,string name){if(!valid)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
        var parser=new HtmlParser();
        try
        {
            foreach(var path in new[]{"/","/tin-tuc","/chuyen-muc/chuyen-muc-18","/bai-viet/"+contentId,"/tai-lieu"})
            {
                using var response=await client.GetAsync(path);var html=await response.Content.ReadAsStringAsync();Check(response.IsSuccessStatusCode,"HTTP 200 "+path);
                var id=response.Headers.GetValues("X-Fixture-Request").Single();for(var i=0;i<100&&!completed.ContainsKey(id);i++)await Task.Delay(20);
                Check(completed.TryGetValue(id,out var state)&&state.Opens==2&&state.Closed,"One scheduler connection plus one retained role connection, closed: "+path);
                if(path=="/tai-lieu")Check(parser.ParseDocument(html).QuerySelectorAll(".material-card").Length==1&&!html.Contains("Tài liệu nháp không công khai"),"Materials remains scoped to eligible published attachment");
            }
            Guid articleId;
            await using(var db=new PortalDbContext(operatorOptions))articleId=await db.Contents.Where(c=>c.Kind==ContentKind.Article&&c.IsPublic&&!c.IsTrashed).Select(c=>c.Id).FirstAsync();
            using(var before=await client.GetAsync("/tin-tuc/moi-nhat"))Check((await before.Content.ReadAsStringAsync()).Contains(articleId.ToString()),"Freshness fixture article exists in ticker before revocation");
            await FocusedChecks.Run(sql.ConnectionString,role,output,client,articleId);
            Check(true,"21 reused role/connection/freshness/statistics checks completed");
            using(var rangeRequest=new HttpRequestMessage(HttpMethod.Get,"/video-media/"+videoId+"/play"))
            {
                rangeRequest.Headers.Range=new System.Net.Http.Headers.RangeHeaderValue(0,63);
                using var response=await client.SendAsync(rangeRequest);Check(response.StatusCode==HttpStatusCode.PartialContent&&(await response.Content.ReadAsByteArrayAsync()).Length==64,"Integrated MP4 Range 206 returns exact requested bytes");
            }
            await using(var db=new PortalDbContext(operatorOptions))
            {
                var file=await db.Set<VideoFile>().SingleAsync(v=>v.MediaAssetId==videoId);var content=await db.Contents.SingleAsync(c=>c.Id==contentId);
                var savedState=file.State;var savedPublic=content.IsPublic;
                try
                {
                    file.State=VideoState.Pending;await db.SaveChangesAsync();
                    using(var response=await client.GetAsync("/video-media/"+videoId+"/play"))Check(response.StatusCode==HttpStatusCode.NotFound,"Integrated non-Ready video is denied");
                    file.State=savedState;content.IsPublic=false;await db.SaveChangesAsync();
                    using(var response=await client.GetAsync("/video-media/"+videoId+"/play"))Check(response.StatusCode==HttpStatusCode.NotFound,"Integrated withdrawn video is denied");
                    Check(!parser.ParseDocument(await client.GetStringAsync("/")).QuerySelectorAll("[data-home-video]").Any(),"Integrated homepage excludes withdrawn video");
                }
                finally{file.State=savedState;content.IsPublic=savedPublic;await db.SaveChangesAsync();}
            }
            using(var scope=app.Services.CreateScope())
            {
                var service=scope.ServiceProvider.GetRequiredService<SiteConfigurationService>();var before=(await service.ReadAsync(false))!;
                string[] Documents(SiteConfigurationRevision r)=>r.Menus.Where(m=>m.RouteKey is "Legal" or "DraftDocuments").Select(m=>JsonSerializer.Serialize(new{m.Label,m.RouteKey,m.ParentId,m.SortOrder,m.Enabled})).Order().ToArray();
                var old=Documents(before.Revision);var actor=new ContentActor("fixture-admin",new HashSet<string>{Permissions.ManageSite,Permissions.PublishSite});
                var updated=await service.MutateAsync(actor,before.Header.RowVersion,_=>{},"IntegrationFixtureMenuRoundtrip");
                await service.PublishAsync(actor,updated.Header.RowVersion,updated.Revision.Id);
                var after=(await service.ReadAsync(true))!;
                Check(old.SequenceEqual(Documents(after.Revision))&&after.Revision.Menus.Single(m=>m.RouteKey=="Materials").CategoryId.HasValue,"CMS draft/publication preserves both original document menus and Materials scope");
            }
            client.DefaultRequestHeaders.Add("X-Fixture-Actor","admin");using(var r=await client.GetAsync("/admin/site?section=menus"))Check(r.IsSuccessStatusCode,"CMS menu page works with the real application role");
            using(var r=await client.PostAsync("/admin/site/materials-menu",new FormUrlEncodedContent(new Dictionary<string,string>{{"rowVersion","invalid"}})))Check(r.StatusCode==HttpStatusCode.BadRequest,"CMS CSRF guard retained");client.DefaultRequestHeaders.Remove("X-Fixture-Actor");
            using(var r=await client.GetAsync("/admin/site?section=menus"))Check(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,"Anonymous CMS request denied");
            foreach(var method in new[]{"GET","HEAD","POST"}){var c=new DefaultHttpContext();c.Request.Method=method;c.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(new ControllerActionDescriptor{ControllerName="Materials"}),"fixture"));Check(DuePublicationMiddleware.ChecksRequest(c)==(method!="POST"),"Materials publication/request gate "+method);}
            await File.WriteAllTextAsync(Path.Combine(output,"ready.json"),JsonSerializer.Serialize(new{url,videoId,contentId,pdfId}));Console.WriteLine("BROWSER_READY "+output);
            var deadline=DateTimeOffset.UtcNow.AddMinutes(10);while(!File.Exists(Path.Combine(output,"browser.done"))&&DateTimeOffset.UtcNow<deadline)await Task.Delay(200);
            Check(File.Exists(Path.Combine(output,"browser.done")),"Integrated focused browser completed");
            await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks,operationalSqlTouched=false,videoPipelineRerun=false}));
        }
        catch(Exception e){await File.WriteAllTextAsync(Path.Combine(output,"failure.txt"),e.ToString());throw;}
        finally{await app.StopAsync();}
    }
    sealed class ConnectionProof(IHttpContextAccessor accessor):DbConnectionInterceptor
    {
        readonly ConcurrentDictionary<string,int> counts=new();public int Count(string id)=>counts.GetValueOrDefault(id);
        public override async Task ConnectionOpenedAsync(DbConnection c,ConnectionEndEventData d,CancellationToken ct=default)
        {
            using var command=c.CreateCommand();command.CommandText="SELECT USER_NAME()";
            if(!Equals(await command.ExecuteScalarAsync(ct),"LandPortalRuntime"))throw new Exception("Real runtime role missing");
            if(accessor.HttpContext is { } context)counts.AddOrUpdate(context.TraceIdentifier,1,(_,n)=>n+1);
        }
    }
}
