using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using LandPortal.Web.Controllers;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SkiaSharp;
using UglyToad.PdfPig.Writer;

if(args.Length==2&&args[0]=="probe"){await SourceProbe.Run(args[1]);return;}
if(args.Length==2&&args[0]=="image-probe"){await SourceImageProbe.Run(args[1]);return;}
if(args.Length==2&&args[0]=="resource-tests"){await SourceResourceTests.Run(args[1]);return;}
// A new, uniquely named LocalDB instance and database; no operational config is read.
var networkOnly=args.Length==5&&args[4]=="--network-only";
var imageOnly=args.Length==5&&args[4]=="--image-only";
if((args.Length!=4&&!networkOnly&&!imageOnly)||!Regex.IsMatch(args[1],"^LPSync_[a-f0-9]{12}$"))throw new ArgumentException("checkout, new isolated LocalDB instance, new test directory, saved QLDD snapshot required");
var root=Path.GetFullPath(args[0]);var run=Path.GetFullPath(args[2]);
if(!run.StartsWith(Path.Combine(root,".local","tests")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(run))throw new Exception("New isolated run required");
Directory.CreateDirectory(run);
var cs=new SqlConnectionStringBuilder{DataSource=@"(localdb)\"+args[1],InitialCatalog="LandPortal_Test",IntegratedSecurity=true,Encrypt=true,TrustServerCertificate=true,Pooling=false};
var adminOptions=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString,o=>o.UseCompatibilityLevel(160)).Options;
var checks=new List<string>();WebApplication? app=null;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
try
{
    await using(var sql=new SqlConnection(new SqlConnectionStringBuilder(cs.ConnectionString){InitialCatalog="master"}.ConnectionString))
    {
        await sql.OpenAsync();using var cmd=sql.CreateCommand();
        cmd.CommandText="SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB')),CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),DB_ID('LandPortal_Test')";
        using(var r=await cmd.ExecuteReaderAsync()){await r.ReadAsync();Check(r.GetInt32(0)==1&&r.GetString(1).Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase)&&r.IsDBNull(2),"Fresh isolated SQL target");}
        cmd.CommandText="CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'"+Path.Combine(run,"test.mdf").Replace("'","''")+"') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'"+Path.Combine(run,"test.ldf").Replace("'","''")+"')";
        await cmd.ExecuteNonQueryAsync();
    }
    var roleSecret=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    await using(var db=new PortalDbContext(adminOptions))
    {
        await db.Database.MigrateAsync();Check((await db.Database.GetAppliedMigrationsAsync()).Count()==18,"Schema 18, no new migration required");
        await RuntimeRoleInterceptor.ProvisionAsync(db,roleSecret);
        db.Categories.Add(new(){Id=1,Name="Fixture news",Slug="fixture-news"});
        foreach(var name in new[]{"admin","reviewer","publisher","denied"})db.Users.Add(new(){Id=name,UserName=name,NormalizedUserName=name.ToUpperInvariant()});
        foreach(var p in new[]{Permissions.ManageSite,Permissions.Create,Permissions.EditOwn,Permissions.ViewOwn,Permissions.ManageMedia,Permissions.Submit})db.UserClaims.Add(new(){UserId="admin",ClaimType="permission",ClaimValue=p});
        db.UserClaims.Add(new(){UserId="reviewer",ClaimType="permission",ClaimValue=Permissions.Review});
        db.UserClaims.Add(new(){UserId="publisher",ClaimType="permission",ClaimValue=Permissions.PublishApproved});
        await db.SaveChangesAsync();
    }
    var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Runtime:RolePassword"]=roleSecret}).Build();
    var options=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString,o=>o.UseCompatibilityLevel(160)).AddInterceptors(new RuntimeRoleInterceptor(config)).Options;
    var storage=new StorageOptions{MediaRoot=Path.Combine(run,"media"),SnapshotRoot=Path.Combine(run,"snapshots"),TempRoot=Path.Combine(run,"temp"),KeysRoot=Path.Combine(run,"keys")};
    foreach(var p in new[]{storage.MediaRoot,storage.SnapshotRoot,storage.TempRoot,storage.KeysRoot})Directory.CreateDirectory(p);
    var policy=new MediaUploadOptions{ApprovedManagedImageWithoutAv=true,ApprovedManagedDocumentsWithoutAv=true};
    var source=new FixtureSource();
    using(var bitmap=new SKBitmap(64,32)){bitmap.Erase(SKColors.Brown);using var image=SKImage.FromBitmap(bitmap);using var bytes=image.Encode(SKEncodedImageFormat.Png,100);source.Png=bytes.ToArray();}
    var pdf=new PdfDocumentBuilder();pdf.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("Isolated fixture",12,new(20,700),pdf.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica));source.Pdf=pdf.Build();
    if(!networkOnly&&!imageOnly){
    var saved=await File.ReadAllTextAsync(args[3]);var parsed=SourceParser.Parse(saved,"https://qldd.mae.gov.vn/cuc-quan-ly-dat-dai-trao-doi-huong-dan-thao-go-vuong-mac-trong-quan-ly-dat-dai-tai-son-la-3681.htm",ContentKind.Article,"3681");
    Check(parsed.Title.Length>20&&parsed.BodyHtml.Length>100&&parsed.PrimaryCategoryId!=null,"Previously saved real QLDD HTML still parses with verified identity/category");
    Check(SourceSyncPolicy.Id(parsed.CanonicalUrl)=="3681","Saved canonical identity remains correct");
    }
    var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=[],ContentRootPath=Path.Combine(root,"src","Web"),WebRootPath=Path.Combine(root,"src","Web","wwwroot"),EnvironmentName="Staging"});
    builder.Configuration.Sources.Clear();builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Public:BaseUrl"]="http://127.0.0.1/"});
    builder.Logging.ClearProviders();builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddScoped(_=>new PortalDbContext(options));builder.Services.AddSingleton(storage);builder.Services.AddSingleton(policy);builder.Services.AddSingleton(new VideoOptions());
    builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());builder.Services.AddHttpContextAccessor();builder.Services.AddMemoryCache();
    builder.Services.AddScoped<CurrentPermissionService>();builder.Services.AddScoped<MediaService>();builder.Services.AddScoped<EditorialDocumentService>();builder.Services.AddScoped<HtmlPolicy>();builder.Services.AddScoped<WorkflowService>();
    builder.Services.AddScoped<SourceSyncStore>();builder.Services.AddScoped<SourceSyncRunner>();builder.Services.AddScoped<SourceSyncMedia>();builder.Services.AddSingleton<IQlddSourceFactory>(source);
    builder.Services.AddScoped<SiteConfigurationService>();builder.Services.AddScoped<PublicContentQuery>();builder.Services.AddScoped<PublicSiteService>();builder.Services.AddScoped<PublicNavigation>();builder.Services.AddScoped<ArticleDiscovery>();builder.Services.AddScoped<DocumentUsage>();builder.Services.AddScoped<PublicTraffic>();builder.Services.AddSingleton<PublicUrlPolicy>();
    builder.Services.AddScoped<InteractionService>();builder.Services.AddSingleton(new InteractionMailOptions());
    builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuth>("Fixture",_=>{});
    builder.Services.AddAuthorization(o=>o.AddPolicy(Permissions.ManageSite,p=>p.RequireClaim("permission",Permissions.ManageSite)));
    builder.Services.AddControllersWithViews(o=>o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddApplicationPart(typeof(HomeController).Assembly);
    app=builder.Build();app.Services.GetRequiredService<IMemoryCache>().Set<PublicTrafficCounts?>("public-traffic-counts",null);
    app.Use(async(ctx,next)=>{ctx.Request.Headers.UserAgent="Source-sync-fixture-bot";await next();});
    app.UseStaticFiles();app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
    using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new()}){BaseAddress=new(app.Urls.Single())};client.DefaultRequestHeaders.Add("X-Fixture-Actor","admin");
    async Task<T> InScope<T>(Func<IServiceProvider,Task<T>> action){await using var scope=app.Services.CreateAsyncScope();return await action(scope.ServiceProvider);}
    async Task Cycle(){await InScope(async s=>{await s.GetRequiredService<SourceSyncStore>().RequestAsync("admin");return await s.GetRequiredService<SourceSyncRunner>().RunOnceAsync();});}
    async Task Due(Guid? id=null){await using var db=new PortalDbContext(options);await db.Jobs.Where(x=>id==null||x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.AvailableUtc,DateTimeOffset.UtcNow.AddMinutes(-1)));}
    async Task<(CrawlItem Item,SourceSyncCheckpoint Data)> Entry(string id="900001"){await using var db=new PortalDbContext(options);var key=SourceSyncPolicy.Key(id);var j=await db.Jobs.SingleAsync(x=>x.IdempotencyKey==key);return await new SourceSyncStore(db).ReadAsync(j.Id);}
    async Task<string> Get(string path){var r=await client.GetAsync(path);Check(r.StatusCode==HttpStatusCode.OK,"HTTP 200 "+path);return await r.Content.ReadAsStringAsync();}
    var parser=new HtmlParser();var page=await Get("/admin/source-sync");var token=parser.ParseDocument(page).QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
    async Task<HttpResponseMessage> Post(string path,Dictionary<string,string>? form=null,bool csrf=true){form??=[];if(csrf)form["__RequestVerificationToken"]=token;return await client.PostAsync(path,new FormUrlEncodedContent(form));}
    Check(!await InScope(s=>s.GetRequiredService<SourceSyncRunner>().RunOnceAsync())&&source.Opens==0,"Disabled by default: no source fetch");
    using(var r=await Post("/admin/source-sync/check",csrf:false))Check(r.StatusCode==HttpStatusCode.BadRequest,"POST without CSRF refused");
    client.DefaultRequestHeaders.Remove("X-Fixture-Actor");client.DefaultRequestHeaders.Add("X-Fixture-Actor","denied");
    using(var r=await client.GetAsync("/admin/source-sync"))Check(r.StatusCode==HttpStatusCode.Forbidden,"Account without management permission refused");
    client.DefaultRequestHeaders.Remove("X-Fixture-Actor");client.DefaultRequestHeaders.Add("X-Fixture-Actor","admin");
    using(var r=await Post("/admin/source-sync/configure",new(){["sourceCategory"]="75",["targetCategory"]="1"}))Check(r.IsSuccessStatusCode,"Explicit source category mapping through CSRF HTTP");
    using(var r=await Post("/admin/source-sync/check"))Check(r.IsSuccessStatusCode&&source.Opens==0,"Manual check only enqueues; HTTP never crawls");
    await InScope(s=>s.GetRequiredService<SourceSyncRunner>().RunOnceAsync());
    var entry=await Entry();Check(entry.Item.Status=="Ready"&&entry.Data.Assets.Count==2,"Bounded discovery, full content and PNG/PDF staged privately");
    if(imageOnly)
    {
        using var capture=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(args[3],"result.json")));
        source.ReplayPages=new Dictionary<string,byte[]>();source.ReplayAssets=new Dictionary<string,SourceResponse>();
        foreach(var id in new[]{"3758","3767"})source.ReplayPages[id]=await File.ReadAllBytesAsync(Path.Combine(args[3],id+".html"));
        foreach(var fact in capture.RootElement.GetProperty("facts").EnumerateArray().Where(x=>x.TryGetProperty("requested",out _)))
        {
            var bytes=await File.ReadAllBytesAsync(Path.Combine(args[3],fact.GetProperty("sha256").GetString()+".bin"));
            Check(Convert.ToHexStringLower(SHA256.HashData(bytes))==fact.GetProperty("sha256").GetString(),"Captured real image hash intact");
            source.ReplayAssets[fact.GetProperty("requested").GetString()!]=new(fact.GetProperty("final").GetString()!,200,fact.GetProperty("ContentType").GetString()!,bytes,DateTimeOffset.UtcNow);
        }
        await InScope(async s=>{await s.GetRequiredService<SourceSyncStore>().ConfigureAsync("admin",false,53,1);return true;});
        foreach(var id in new[]{"3758","3767"})
        {
            source.SourceId=id;source.ReplayBlocked=true;await Due();await Cycle();var held=await Entry(id);
            Check(held.Item.Status=="Review"&&held.Data.Assets.Count==0&&held.Data.Warnings.Any(x=>x.Contains("CONTENT_TYPE")),id+": invalid header keeps required resource in Review");
            source.ReplayBlocked=false;await Due();
            using(var r=await Post($"/admin/source-sync/{held.Item.Id}/state",new(){["action"]="retry"}))Check(r.IsSuccessStatusCode,id+": scoped retry through CMS");
            await InScope(s=>s.GetRequiredService<SourceSyncRunner>().RunOnceAsync());var ready=await Entry(id);
            Check(ready.Item.Id==held.Item.Id&&ready.Item.Status=="Ready"&&ready.Data.Assets.Count==1&&ready.Data.Warnings.Count==0,id+": same checkpoint Review -> Ready with captured JPEG/image-png response");
            var staged=ready.Data.Assets.Single();
            Check(staged.Mime=="image/jpeg"&&Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(storage.ResolveSnapshot(staged.Key))))==staged.Sha256,id+": snapshot exact SHA and canonical JPEG MIME");
            await Due();await Cycle();
            await using(var db=new PortalDbContext(options))Check(await db.Jobs.CountAsync(x=>x.IdempotencyKey==SourceSyncPolicy.Key(id))==1&&await db.Contents.CountAsync()==(id=="3758"?0:1),id+": repeated discovery creates no duplicate or automatic CMS content");
            using(var r=await Post($"/admin/source-sync/{ready.Item.Id}/draft",new(){["hash"]=ready.Data.Hash}))Check(r.IsSuccessStatusCode,id+": receive into draft through existing workflow");
            var received=await Entry(id);
            await using(var db=new PortalDbContext(options))
            {
                var content=await db.Contents.SingleAsync(x=>x.Id==received.Data.ContentId);var draft=await db.ContentDrafts.SingleAsync(x=>x.ContentId==content.Id);
                var asset=await db.MediaAssets.SingleAsync(x=>x.Id==draft.CoverAssetId);
                Check(content.State==WorkflowState.Draft&&content.PublishedRevisionId==null&&draft.BodyHtml.Contains("/media/"+asset.Id)&&!draft.BodyHtml.Contains("media-qldd.mae.gov.vn"),id+": image placed in body and cover, no hotlink or publication");
                Check(asset.ScanStatus=="NotScanned"&&asset.ValidationStatus==MediaUploadOptions.ManagedImageNoAvValidation,id+": real decode policy, never fake Clean");
            }
        }
        await File.WriteAllTextAsync(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks,operationalSqlTouched=false}));
        Console.WriteLine("PASS focused image recovery checks: "+checks.Count);return;
    }
    if(networkOnly)
    {
        await Due();await Cycle();
        await using(var db=new PortalDbContext(options))Check(await db.Jobs.CountAsync(x=>x.Type==SourceSyncPolicy.EntryType)==1&&await db.Contents.CountAsync()==0,"Repeated discovery does not duplicate or publish content");
        source.Fail=true;
        await InScope(async s=>{await s.GetRequiredService<SourceSyncStore>().ConfigureAsync("admin",true,null,null);return true;});
        for(var i=1;i<=3;i++)
        {
            await Due();await InScope(s=>s.GetRequiredService<SourceSyncRunner>().RunOnceAsync());
            await using var db=new PortalDbContext(options);
            var sweep=await db.Jobs.SingleAsync(x=>x.IdempotencyKey==SourceSyncPolicy.SweepKey);
            Check(sweep.Status==(i==3?"Paused":"Backoff")&&sweep.AvailableUtc>DateTimeOffset.UtcNow.AddMinutes(15*i-1),"Bounded failure delay "+i);
            Check(!await InScope(s=>s.GetRequiredService<SourceSyncRunner>().RunOnceAsync()),"No automatic retry within delay "+i);
            using var response=await Post("/admin/source-sync/check");Check(response.StatusCode==HttpStatusCode.Conflict,"Manual request cannot bypass delay "+i);
        }
        await Due();Check(!await InScope(s=>s.GetRequiredService<SourceSyncRunner>().RunOnceAsync()),"Paused after third failure even when due");
        source.Fail=false;await Cycle();
        await using(var db=new PortalDbContext(options))Check((await new SourceSyncStore(db).SettingsAsync()).ConsecutiveFailures==0&&await db.Contents.CountAsync()==0,"Explicit recovery resets failure count, never publishes");
        await File.WriteAllTextAsync(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks,operationalSqlTouched=false}));
        Console.WriteLine("PASS focused network/backoff checks: "+checks.Count);return;
    }
    Check(entry.Data.Source!.DatePrecision=="Day"&&entry.Data.Source.Summary=="Fixture sapo"&&entry.Data.Source.Author=="Fixture author","Source sapo, author and date precision preserved");
    await using(var db=new PortalDbContext(options))Check(!await db.Contents.AnyAsync()&&!await db.MediaAssets.AnyAsync(),"Collecting does not create CMS content or publish media");
    await Get("/admin/source-sync?q=Fixture&status=Ready");var detail=await Get("/admin/source-sync/"+entry.Item.Id);
    Check(!detail.Contains("https://media-qldd.mae.gov.vn/fixture.png")&&!detail.Contains("onerror="),"Preview has no hotlinks or executable HTML");
    using(var r=await Post($"/admin/source-sync/{entry.Item.Id}/draft",new(){["hash"]=new string('0',64)}))Check(r.StatusCode==HttpStatusCode.Conflict,"Stale preview hash refused");
    using(var r=await Post($"/admin/source-sync/{entry.Item.Id}/draft",new(){["hash"]=entry.Data.Hash}))Check(r.IsSuccessStatusCode,"Create CMS draft through authenticated HTTP");
    var drafted=await Entry();var contentId=drafted.Data.ContentId!.Value;
    await using(var db=new PortalDbContext(options))
    {
        var c=await db.Contents.SingleAsync();var draft=await db.ContentDrafts.SingleAsync();var assets=await db.MediaAssets.ToListAsync();
        Check(c.State==WorkflowState.Draft&&!c.IsPublic&&c.PublishedRevisionId==null&&c.OwnerUserId=="admin","CMS draft uses actual actor, never auto-published");
        Check(assets.Count==2&&assets.All(x=>x.ScanStatus=="NotScanned"&&!x.IsQuarantined),"PNG/PDF use current technical policy with truthful NotScanned");
        Check(draft.BodyHtml.Contains("/media/")&&draft.BodyHtml.Contains("Caption fixture")&&draft.BodyHtml.Contains("<table")&&!draft.BodyHtml.Contains("https://media-qldd"),"Local media mapping preserves captions/table without hotlinks");
        Check(await db.SourceRecords.CountAsync()==1&&await db.SourceUrlAliases.CountAsync()==2,"Source identity and two aliases committed atomically");
    }
    using(var r=await Post($"/admin/source-sync/{entry.Item.Id}/draft",new(){["hash"]=entry.Data.Hash}))Check(r.IsSuccessStatusCode,"Same CMS command replay is idempotent");
    await Due();await Cycle();
    await using(var db=new PortalDbContext(options))Check(await db.Contents.CountAsync()==1&&await db.Jobs.CountAsync(x=>x.Type==SourceSyncPolicy.EntryType)==1,"Repeat/restart does not duplicate entry or CMS content");
    async Task<WorkflowResult> Workflow(string who,Func<WorkflowService,Content,ContentActor,Task<WorkflowResult>> action)=>await InScope(async s=>{
        var db=s.GetRequiredService<PortalDbContext>();var c=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==contentId);var actor=await s.GetRequiredService<CurrentPermissionService>().GetAsync(who);return await action(s.GetRequiredService<WorkflowService>(),c,actor);});
    await Workflow("admin",(w,c,a)=>w.SubmitAsync(c.Id,c.CurrentRevisionId,a,c.RowVersion,Guid.NewGuid()));
    await Workflow("reviewer",(w,c,a)=>w.ApproveAsync(c.Id,c.SubmittedRevisionId!.Value,a,c.RowVersion,Guid.NewGuid()));
    await Workflow("publisher",(w,c,a)=>w.PublishApprovedAsync(c.Id,c.ApprovedRevisionId!.Value,a,c.RowVersion,Guid.NewGuid()));
    Check(true,"Existing submit -> review -> approved publication workflow succeeds");
    string before;await using(var db=new PortalDbContext(options))before=JsonSerializer.Serialize(await db.Contents.AsNoTracking().SingleAsync());
    source.Changed=true;await Due();await Cycle();var changed=await Entry();
    await using(var db=new PortalDbContext(options))Check(changed.Item.Status=="Changed"&&before==JsonSerializer.Serialize(await db.Contents.AsNoTracking().SingleAsync()),"Changed source is comparison-only: published content unchanged");
    await Get("/");await Get("/tin-tuc");await Get("/bai-viet/"+contentId);
    // Simulate an application recycle while a source call waits: a live lease is exclusive,
    // an expired lease is recovered with a new fence. No production scheduler is started.
    await Due();await InScope(async s=>{await s.GetRequiredService<SourceSyncStore>().RequestAsync("admin");return true;});
    var claim=await InScope(s=>s.GetRequiredService<SourceSyncRunner>().ClaimAsync());
    Check(claim!=null&&await InScope(s=>s.GetRequiredService<SourceSyncRunner>().ClaimAsync())==null,"Second Web process cannot claim a live sweep");
    await using(var db=new PortalDbContext(options))await db.Jobs.Where(x=>x.Id==claim!.Id).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.LeaseUntilUtc,DateTimeOffset.UtcNow.AddMinutes(-1)));
    var recovered=await InScope(s=>s.GetRequiredService<SourceSyncRunner>().ClaimAsync());Check(recovered!.Attempts==claim!.Attempts+1,"Expired sweep recovers with a fresh fencing token");
    await using(var db=new PortalDbContext(options))await db.Jobs.Where(x=>x.Id==claim.Id).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.LeaseUntilUtc,DateTimeOffset.UtcNow.AddMinutes(-1)));
    source.Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);source.Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
    var waiting=InScope(s=>s.GetRequiredService<SourceSyncRunner>().RunOnceAsync());await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    using(var response=await client.GetAsync("/").WaitAsync(TimeSpan.FromSeconds(5)))Check(response.IsSuccessStatusCode&&!waiting.IsCompleted,"Public homepage responds while background source fetch remains blocked");
    source.Gate.SetResult();await waiting;source.Gate=null;source.Entered=null;
    source.Fail=true;
    for(var i=0;i<3;i++){await Due();await using(var db=new PortalDbContext(options)){await new SourceSyncStore(db).ConfigureAsync("admin",true,null,null);}await InScope(s=>s.GetRequiredService<SourceSyncRunner>().RunOnceAsync());}
    await Due();Check(!await InScope(s=>s.GetRequiredService<SourceSyncRunner>().RunOnceAsync()),"Automatic retries pause after three source failures");
    await using(var db=new PortalDbContext(options))Check(before==JsonSerializer.Serialize(await db.Contents.AsNoTracking().SingleAsync()),"Source failure retains prior CMS data");
    source.Fail=false;source.InvalidAsset=true;source.SourceId="900002";source.Changed=false;await Cycle();
    var invalid=await Entry("900002");Check(invalid.Item.Status=="Review"&&invalid.Data.Assets.Count==1&&invalid.Data.Warnings.Count>0,"Spoofed PNG is rejected; missing resource warning retained");
    await Due();await Cycle();Check((await Entry("900002")).Data.Warnings.Count>0,"Retry does not incorrectly clear missing-asset warnings");
    using(var r=await Post($"/admin/source-sync/{invalid.Item.Id}/state",new(){["action"]="ignore"}))Check(r.IsSuccessStatusCode,"Ignore via authenticated CSRF HTTP");
    await Due();await Cycle();Check((await Entry("900002")).Item.Status=="Ignored","Ignored entry is not resurrected by discovery");
    var robots=new SourceSyncRobots("User-agent: *\nDisallow: /admin\nDisallow: /private/*\nAllow: /private/open$\n");
    Check(robots.Allows("https://qldd.mae.gov.vn/fixture-900001.htm")&&!robots.Allows("https://qldd.mae.gov.vn/private/a")&&robots.Allows("https://qldd.mae.gov.vn/private/open"),"Robots permits public pages while respecting restricted paths");
    Check(!new SourceSyncRobots("User-agent: *\nDisallow: /\n").Allows("https://qldd.mae.gov.vn/"),"Source robots deny-all remains blocked");
    Check(SourceSyncPolicy.Id("https://attacker.invalid/fixture-900001.htm")==null&&SourceSyncPolicy.Id("http://127.0.0.1/fixture-900001.htm")==null,"Foreign/private source identity refused before discovery");
    source.InvalidAsset=false;source.SourceId="900005";await Due();await Cycle();
    var retryEntry=await Entry("900005");
    await InScope(async s=>{await s.GetRequiredService<SourceSyncStore>().ConfigureAsync("admin",false,null,null);return true;});
    await using(var db=new PortalDbContext(options))
    {
        await db.CrawlItems.Where(x=>x.Id==retryEntry.Item.Id).ExecuteUpdateAsync(x=>x.SetProperty(i=>i.Status,"Error"));
        await db.Jobs.Where(x=>x.Id==retryEntry.Item.Id).ExecuteUpdateAsync(x=>x.SetProperty(j=>j.Status,"Error").SetProperty(j=>j.Attempts,3));
    }
    await Due();using(var r=await Post($"/admin/source-sync/{retryEntry.Item.Id}/state",new(){["action"]="retry"}))Check(r.IsSuccessStatusCode,"Explicit retry queues a manual sweep while periodic sync is off");
    Check(await InScope(s=>s.GetRequiredService<SourceSyncRunner>().RunOnceAsync())&&(await Entry("900005")).Item.Status=="Ready","Explicit retry resumes a bounded collection without enabling periodic sync");
    var concurrent=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>InScope(s=>s.GetRequiredService<SourceSyncRunner>().DiscoverAsync(new("https://qldd.mae.gov.vn/fixture-900003.htm","900003",ContentKind.Article,75)))));
    Check(concurrent[0]==concurrent[1],"Concurrent discovery creates one durable entry");
    source.InvalidAsset=false;source.SourceId="900003";
    await InScope(async s=>{await s.GetRequiredService<SourceSyncStore>().ConfigureAsync("admin",false,75,null);return true;});await Due();await Cycle();
    var unmapped=await Entry("900003");Check(unmapped.Item.Status=="Review","Missing explicit category mapping requires editorial review");
    using(var r=await Post($"/admin/source-sync/{unmapped.Item.Id}/draft",new(){["hash"]=unmapped.Data.Hash}))Check(r.StatusCode==HttpStatusCode.Conflict,"No guessed category when creating draft");
    await InScope(async s=>{await s.GetRequiredService<SourceSyncStore>().ConfigureAsync("admin",false,75,1);return true;});
    // Tamper only this isolated staged file; no hash bypass and no public link.
    var stagedImage=unmapped.Data.Assets.Single(x=>x.Mime=="image/png");await File.WriteAllBytesAsync(storage.ResolveSnapshot(stagedImage.Key),[0,1,2]);
    using(var r=await Post($"/admin/source-sync/{unmapped.Item.Id}/draft",new(){["hash"]=unmapped.Data.Hash}))Check(r.IsSuccessStatusCode,"Draft retains content when a staged asset fails SHA validation");
    var partial=await Entry("900003");
    await using(var db=new PortalDbContext(options))
    {
        var draft=await db.ContentDrafts.SingleAsync(x=>x.ContentId==partial.Data.ContentId);
        Check(draft.CoverAssetId==null&&!draft.BodyHtml.Contains("<img")&&partial.Data.Warnings.Count>0,"Tampered staged image excluded, warning persists in CMS queue");
        var old=new Content{Kind=ContentKind.Article,OwnerUserId="admin",PrimaryCategoryId=1,IsTrashed=true};db.Contents.Add(old);await db.SaveChangesAsync();
        db.SourceRecords.Add(new(){ContentId=old.Id,Kind=ContentKind.Article,SourceId="900004",CanonicalUrl="https://qldd.mae.gov.vn/Pages/chi-tiet-tin-tuc.aspx?ItemID=900004"});await db.SaveChangesAsync();
    }
    source.SourceId="900004";await Due();await Cycle();var prior=await Entry("900004");
    await using(var db=new PortalDbContext(options))Check(prior.Item.Status=="Existing"&&prior.Data.Assets.Count==0&&(await db.Contents.SingleAsync(x=>x.Id==prior.Data.ContentId)).IsTrashed,"Prior imported identity detected; trashed item not restored or redownloaded");
    await using(var db=new PortalDbContext(options))await db.UserClaims.Where(x=>x.UserId=="admin"&&x.ClaimValue==Permissions.ManageSite).ExecuteDeleteAsync();
    using(var r=await client.GetAsync("/admin/source-sync"))Check(r.StatusCode==HttpStatusCode.Forbidden,"Revoked management permission denied despite stale authentication claim");
    using(var r=await Post("/admin/source-sync/check"))Check(r.StatusCode==HttpStatusCode.Forbidden,"Revoked management permission denied for POST");
    await using(var db=new PortalDbContext(options)){db.UserClaims.Add(new(){UserId="admin",ClaimType="permission",ClaimValue=Permissions.ManageSite});await db.SaveChangesAsync();}
    await File.WriteAllTextAsync(Path.Combine(run,"ready.json"),JsonSerializer.Serialize(new{url=app.Urls.Single(),checks}));
    if(Environment.GetEnvironmentVariable("SOURCE_SYNC_BROWSER")=="1")
    {
        Console.WriteLine("BROWSER_READY "+run);var deadline=DateTimeOffset.UtcNow.AddMinutes(5);
        while(!File.Exists(Path.Combine(run,"browser.done"))&&DateTimeOffset.UtcNow<deadline)await Task.Delay(250);
        Check(File.Exists(Path.Combine(run,"browser.done")),"Focused Admin browser checks completed");
    }
    await File.WriteAllTextAsync(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks,operationalSqlTouched=false}));
    Console.WriteLine("TOTAL PASS "+checks.Count);
}
catch(Exception error){await File.WriteAllTextAsync(Path.Combine(run,"failure.txt"),error.ToString());Console.WriteLine("FAIL "+error.GetType().Name+"; isolated failure.txt contains details");Environment.ExitCode=1;}
finally{if(app!=null){await app.StopAsync();await app.DisposeAsync();}}

sealed class FixtureAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var actor=Request.Headers["X-Fixture-Actor"].ToString();if(actor is not("admin" or "denied"))return Task.FromResult(AuthenticateResult.NoResult());
        var claims=new List<Claim>{new(ClaimTypes.NameIdentifier,actor),new(ClaimTypes.Name,actor)};if(actor=="admin")claims.Add(new("permission",Permissions.ManageSite));
        return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims,"Fixture")),"Fixture")));
    }
}
sealed class FixtureSource:IQlddSourceFactory
{
    public Dictionary<string,byte[]>? ReplayPages;public Dictionary<string,SourceResponse>? ReplayAssets;public bool ReplayBlocked;
    public byte[] Png=[],Pdf=[];public int Opens;public bool Changed,Fail,InvalidAsset;public string SourceId="900001";
    public TaskCompletionSource? Gate,Entered;
    public IQlddSourceSession Open(){Opens++;return new Session(this);}
    sealed class Session(FixtureSource f):IQlddSourceSession
    {
        public int RequestsUsed{get;private set;}
        public void Dispose(){}
        public async Task<SourceResponse> FetchAsync(string url,int maximum,bool page,CancellationToken ct)
        {
            RequestsUsed++;if(RequestsUsed>SourceSyncPolicy.RequestsPerRun)throw new InvalidOperationException("Fixture budget exceeded");
            if(f.Gate!=null){f.Entered!.TrySetResult();await f.Gate.Task.WaitAsync(ct);}
            if(f.Fail)throw new HttpRequestException("Isolated source outage");
            if(f.ReplayAssets?.TryGetValue(url,out var captured)==true)return captured with { ContentType=f.ReplayBlocked?"text/html":captured.ContentType };
            if(SourceSyncPolicy.Id(url)==f.SourceId&&f.ReplayPages?.TryGetValue(f.SourceId,out var html)==true)return new(url,200,"text/html",html,DateTimeOffset.UtcNow);
            var uri=new Uri(url);byte[] bytes;var mime="text/html";
            if(uri.AbsolutePath=="/robots.txt"){bytes=Encoding.UTF8.GetBytes("User-agent: *\nDisallow:\n");mime="text/plain";}
            else if(uri.AbsolutePath.EndsWith(".png")){bytes=f.InvalidAsset?Encoding.UTF8.GetBytes("fake image"):f.Png;mime="image/png";}
            else if(uri.AbsolutePath.EndsWith(".pdf")){bytes=f.Pdf;mime="application/pdf";}
            else if(SourceSyncPolicy.Id(url)!=null)bytes=Encoding.UTF8.GetBytes($"""
                <html><head><link rel="canonical" href="https://qldd.mae.gov.vn/Pages/chi-tiet-tin-tuc.aspx?ItemID={f.SourceId}"></head><body>
                <h1 class="tieu-de-tin-tuc">Fixture {(f.Changed?"changed":"news")}</h1><p class="sapo-chi-tiet-tin">Fixture sapo</p>
                <p class="thoi-gian-tao-chi-tiet-tin">14/11/2026</p><a class="chi-tiet-tin-back-icon" href="/chuyen-muc/site-xay-dung-pho-bien-chinh-sach-phap-luat-75.htm">Category</a>
                <section><div id="News-content"><article class="noi-dung-chi-tiet-tin"><h2>Section fixture</h2><p>Body fixture {(f.Changed?"changed":"original")}</p><table><tbody><tr><td>Cell</td></tr></tbody></table>
                <figure><img src="" data-src="https://media-qldd.mae.gov.vn/fixture.png" srcset="https://media-qldd.mae.gov.vn/fixture.png 1x" onerror="alert(1)"><figcaption>Caption fixture</figcaption></figure>
                <a href="https://media-qldd.mae.gov.vn/fixture.pdf">PDF fixture</a><script>alert(1)</script></article></div><p class="text-end"><strong>Fixture author</strong></p></section></body></html>
                """);
            else bytes=Encoding.UTF8.GetBytes($"<div id='KhuVuc_DanhSachTin'><a href='/fixture-{f.SourceId}.htm'>Fixture news</a></div>");
            if(bytes.Length>maximum)throw new IOException("Fixture too large");return new(url,200,mime,bytes,DateTimeOffset.UtcNow);
        }
    }
}
