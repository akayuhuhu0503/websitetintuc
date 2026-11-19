using System.Diagnostics;
using System.Security.Cryptography;
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

// Focused HTTP/SQL route check. Synthetic data in a new isolated LocalDB only.
static class IntroductionChecks
{
    public static async Task Run(string checkout, string output)
    {
        var root=Path.GetFullPath(checkout);var run=Path.GetFullPath(output);
        if(!run.StartsWith(Path.Combine(root,".local","tests","introduction-"),StringComparison.OrdinalIgnoreCase)||Directory.Exists(run))throw new Exception("New isolated fixture required");
        Directory.CreateDirectory(run);var instance="LPIntro_"+Guid.NewGuid().ToString("N")[..12];
        async Task LocalDb(string action)
        {
            var start=new ProcessStartInfo("SqlLocalDB.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(action);start.ArgumentList.Add(instance);
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();await stdout;await stderr;if(process.ExitCode!=0)throw new Exception("LocalDB "+action+" failed");
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
                using(var r=await command.ExecuteReaderAsync()){await r.ReadAsync();Check(r.GetInt32(0)==1&&r.GetString(1).Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase)&&r.IsDBNull(2),"New local-only SQL target");}
                command.CommandText="CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'"+Path.Combine(run,"test.mdf").Replace("'","''")+"') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'"+Path.Combine(run,"test.ldf").Replace("'","''")+"')";await command.ExecuteNonQueryAsync();
            }
            var options=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(sql.ConnectionString).Options;
            var role=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await using(var db=new PortalDbContext(options))
            {
                await db.Database.MigrateAsync();Check((await db.Database.GetAppliedMigrationsAsync()).Count()==18,"Isolated schema18");
                await RuntimeRoleInterceptor.ProvisionAsync(db,role);
                db.Users.Add(new(){Id="fixture-owner",UserName="fixture-owner"});
                db.Categories.AddRange(new Category{Id=76,Name="Lịch tuần",Slug="lich-tuan"},new Category{Id=85,Name="Lãnh đạo cục",Slug="lanh-dao-cuc"});await db.SaveChangesAsync();
            }
            async Task<Guid> Add(string title,int category=85,bool published=true,ContentKind kind=ContentKind.StaticPage)
            {
                await using var db=new PortalDbContext(options);var c=new Content{Kind=kind,OwnerUserId="fixture-owner",PrimaryCategoryId=category};db.Contents.Add(c);await db.SaveChangesAsync();
                var r=new ContentRevision{ContentId=c.Id,PrimaryCategoryId=category,Title=title,BodyHtml="<p>Dedicated introduction fixture</p>",Author="Fixture author",Metadata=new(){PublicationDate=new DateOnly(2025,3,6)},ContentHash=Guid.NewGuid().ToString("N")};db.Revisions.Add(r);await db.SaveChangesAsync();
                c.CurrentRevisionId=r.Id;c.PublishedRevisionId=published?r.Id:null;c.IsPublic=published;c.PublishedAtUtc=DateTimeOffset.UtcNow;c.State=published?WorkflowState.Published:WorkflowState.Draft;await db.SaveChangesAsync();return c.Id;
            }
            await Add("Lịch công tác của lãnh đạo Cục Quản lý đất đai Tuần 45",76);
            await Add("Lãnh đạo cục",published:false);
            await Add("Lãnh đạo cục",kind:ContentKind.Article);
            var storage=new StorageOptions{MediaRoot=Path.Combine(run,"media"),TempRoot=Path.Combine(run,"temp"),SnapshotRoot=Path.Combine(run,"snapshots"),KeysRoot=Path.Combine(run,"keys")};
            foreach(var p in new[]{storage.MediaRoot,storage.TempRoot,storage.SnapshotRoot,storage.KeysRoot})Directory.CreateDirectory(p);
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=[],ContentRootPath=Path.Combine(root,"src","Web"),WebRootPath=Path.Combine(root,"src","Web","wwwroot"),EnvironmentName="Staging"});
            builder.Configuration.Sources.Clear();builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{{"Public:BaseUrl","http://127.0.0.1/"},{"Runtime:RolePassword",role}});
            builder.Logging.ClearProviders();builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddScoped(_=>new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(sql.ConnectionString).AddInterceptors(new RuntimeRoleInterceptor(builder.Configuration)).Options));
            builder.Services.AddSingleton(storage);builder.Services.AddSingleton(new MediaUploadOptions());builder.Services.AddSingleton(new VideoOptions());builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            builder.Services.AddMemoryCache();builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<PublicContentQuery>();builder.Services.AddScoped<PublicSiteService>();builder.Services.AddScoped<SiteConfigurationService>();builder.Services.AddScoped<PublicNavigation>();
            builder.Services.AddScoped<CurrentPermissionService>();builder.Services.AddScoped<ArticleDiscovery>();builder.Services.AddScoped<DocumentUsage>();builder.Services.AddScoped<PublicTraffic>();builder.Services.AddSingleton<PublicUrlPolicy>();
            builder.Services.AddScoped<InteractionService>();builder.Services.AddSingleton(new InteractionMailOptions());
            builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuth>("Fixture",_=>{});builder.Services.AddAuthorization();builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PortalController).Assembly);
            app=builder.Build();app.Use(async(ctx,next)=>{ctx.Request.Headers.UserAgent="introduction-fixture-bot";await next();});app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
            using var client=new HttpClient{BaseAddress=new Uri(app.Urls.Single())};var parser=new HtmlParser();
            async Task<AngleSharp.Dom.IDocument> Page(){using var r=await client.GetAsync("/gioi-thieu/lanh-dao-cuc");Check(r.IsSuccessStatusCode,"Introduction HTTP 200");return parser.ParseDocument(await r.Content.ReadAsStringAsync());}
            var empty=await Page();Check(empty.QuerySelector(".article-detail")==null&&empty.QuerySelector("main")!.TextContent.Contains("Chưa có nội dung riêng"),"Weekly title, news article and draft do not substitute for leadership introduction");
            var id=await Add("Lãnh đạo cục");var page=await Page();
            Check(page.QuerySelector(".article-detail h1")?.TextContent=="Lãnh đạo cục"&&page.QuerySelector(".article-body")?.TextContent.Contains("Dedicated introduction fixture")==true,"Exact published introduction opens article, not weekly list");
            Check(page.QuerySelector(".article-meta")?.TextContent.Contains("06/03/2025")==true,"Source PublicationDate retained");
            await using(var db=new PortalDbContext(options)){var c=await db.Contents.SingleAsync(x=>x.Id==id);var draft=new ContentRevision{ContentId=id,Title="Unpublished replacement",BodyHtml="Private replacement",ContentHash=Guid.NewGuid().ToString("N")};db.Revisions.Add(draft);await db.SaveChangesAsync();c.CurrentRevisionId=draft.Id;await db.SaveChangesAsync();}
            page=await Page();Check(page.QuerySelector(".article-detail h1")?.TextContent=="Lãnh đạo cục"&&!page.Body!.TextContent.Contains("Private replacement"),"Selection uses published revision while new draft is private");
            await using(var db=new PortalDbContext(options)){var c=await db.Contents.SingleAsync(x=>x.Id==id);c.IsPublic=false;await db.SaveChangesAsync();}
            Check((await Page()).QuerySelector(".article-detail")==null,"Withdrawal removes dedicated public introduction");
            await File.WriteAllTextAsync(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks,instance,operationalSqlTouched=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{if(app!=null){await app.StopAsync();await app.DisposeAsync();}SqlConnection.ClearAllPools();if(started)await LocalDb("stop");}
    }
}
