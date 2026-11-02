using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Explicit, fresh local fixture database only. Never reuse an existing database,
// read private deployment configuration, migrate LAN/Test/Dev, or delete fixtures.
if(args.Length!=2)throw new ArgumentException("Existing private run directory and compatible Web.dll required.");
var root=Directory.GetCurrentDirectory();
var run=Path.GetFullPath(args[0]);var webDll=Path.GetFullPath(args[1]);
if(!run.StartsWith(Path.Combine(root,".local","closure-"),StringComparison.OrdinalIgnoreCase)||!Directory.Exists(run)||File.Exists(Path.Combine(run,"result.json"))||!File.Exists(webDll))throw new InvalidOperationException("New scoped run required.");
var database="LandPortal_ClosureTest_"+Guid.NewGuid().ToString("N")[..12];
var cs=new SqlConnectionStringBuilder{DataSource=@".\SQLEXPRESS",InitialCatalog=database,IntegratedSecurity=true,Encrypt=true,TrustServerCertificate=true,Pooling=false};
var checks=new List<string>();string stage="PRECHECK";Process? web=null;Task<string>? stdout=null,stderr=null;
void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks.Add(label);Console.WriteLine("PASS "+label);}
try
{
    var admin=new SqlConnectionStringBuilder(cs.ConnectionString){InitialCatalog="master"};
    await using(var connection=new SqlConnection(admin.ConnectionString))
    {
        await connection.OpenAsync();
        using(var cmd=connection.CreateCommand())
        {
            cmd.CommandText="SELECT CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),CONVERT(nvarchar(128),SERVERPROPERTY('InstanceName')),DB_ID(@name)";
            cmd.Parameters.AddWithValue("@name",database);
            await using var r=await cmd.ExecuteReaderAsync();await r.ReadAsync();
            Check(r.GetString(0).Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase)&&r.GetString(1)=="SQLEXPRESS"&&r.IsDBNull(2),"New local isolated database target verified");
        }
        stage="CREATE_NEW_FIXTURE_DATABASE";
        // Identifier is generated internally, never supplied by an operator/config file.
        using var create=connection.CreateCommand();create.CommandText=$"CREATE DATABASE [{database}]";await create.ExecuteNonQueryAsync();
    }
    await File.WriteAllTextAsync(Path.Combine(run,"target.json"),JsonSerializer.Serialize(new{database,server=@".\SQLEXPRESS",createdFor="isolated technical FR06/FR10 only",preserve=true}));
    var options=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString,o=>o.UseCompatibilityLevel(160)).Options;
    await using(var db=new PortalDbContext(options))
    {
        stage="MIGRATE_NEW_FIXTURE_DATABASE";
        Check(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.tables WHERE is_ms_shipped=0").SingleAsync()==0,"Fixture database initially empty");
        await db.Database.MigrateAsync();
        Check((await db.Database.GetAppliedMigrationsAsync()).Count()==17,"All 17 migrations applied through supported EF migrator");
        await db.Database.MigrateAsync();
        Check(!(await db.Database.GetPendingMigrationsAsync()).Any()&&await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.tables WHERE is_ms_shipped=0").SingleAsync()==70,"Migration idempotency 70 tables / 17 history");
    }
    var roleSecret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    await using(var db=new PortalDbContext(options))await RuntimeRoleInterceptor.ProvisionAsync(db,roleSecret);
    var services=new ServiceCollection();services.AddLogging();services.AddScoped(_=>new PortalDbContext(options));
    services.AddIdentityCore<PortalUser>(PortalIdentityPolicy.Configure).AddRoles<IdentityRole>().AddEntityFrameworkStores<PortalDbContext>();
    using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();
    var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
    var accounts=new Dictionary<string,(PortalUser User,string Password)>();
    foreach(var entry in new[]{("writer",new[]{Permissions.Create,Permissions.ViewOwn,Permissions.EditOwn,Permissions.Submit}), ("reviewer",new[]{Permissions.Review}), ("publisher",new[]{Permissions.PublishApproved})})
    {
        var user=new PortalUser{UserName="closure-"+entry.Item1,Email="closure-"+entry.Item1+"@example.invalid",DisplayName="Isolated technical "+entry.Item1,EmailConfirmed=true,LockoutEnabled=true};
        var password="Aa1!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        Check((await users.CreateAsync(user,password)).Succeeded,"Isolated Identity account created: "+entry.Item1);
        Check((await users.AddClaimsAsync(user,entry.Item2.Select(p=>new Claim("permission",p)))).Succeeded,"Exact permission fixture: "+entry.Item1);
        accounts.Add(entry.Item1,(user,password));
    }
    var image=Path.Combine(run,"media","cover.jpg");
    using var proof=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run,"scan.json")));
    Check(proof.RootElement.GetProperty("status").GetString()=="Clean"&&proof.RootElement.GetProperty("nativeExit").GetInt32()==0&&DateTimeOffset.UtcNow-proof.RootElement.GetProperty("utc").GetDateTimeOffset()<TimeSpan.FromHours(1)&&Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(image)))==proof.RootElement.GetProperty("sha256").GetString(),"Native Defender proof bound to fixture bytes and time");
    using(var decoded=SkiaSharp.SKBitmap.Decode(image))Check(decoded!=null,"Real image format decoded");
    var cover=new MediaAsset{StorageKey="cover.jpg",OriginalName="public-image-fixture.jpg",MimeType="image/jpeg",ByteLength=new FileInfo(image).Length,Sha256=proof.RootElement.GetProperty("sha256").GetString()!,ScanStatus="Clean",ValidationStatus="Validated",Origin="Uploaded",UploadedByUserId=accounts["writer"].User.Id};
    await using(var db=new PortalDbContext(options))
    {
        db.Categories.Add(new(){Id=1,Name="Isolated technical fixture",Slug="closure-fixture"});
        db.MediaAssets.Add(cover);await db.SaveChangesAsync();
    }
    stage="START_LOOPBACK_TEST_WEB";
    const string url="http://127.0.0.1:5496";
    using var guest=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new(url)};
    var start=new ProcessStartInfo(Path.Combine(root,".local","dotnet","dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
    start.ArgumentList.Add(webDll);start.ArgumentList.Add("--contentRoot");start.ArgumentList.Add(Path.Combine(root,"src","Web"));
    foreach(var key in start.Environment.Keys.Where(k=>k.StartsWith("LANDPORTAL_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("Captcha__",StringComparison.OrdinalIgnoreCase)).ToArray())start.Environment.Remove(key);
    var environment=new Dictionary<string,string>{{"ConnectionStrings__Portal",cs.ConnectionString},{"Sql__ExpectedDatabase",database},{"Runtime__RolePassword",roleSecret},{"Public__BaseUrl",url},{"DOTNET_ENVIRONMENT","Development"},{"ASPNETCORE_ENVIRONMENT","Development"},{"Mail__Transport","Disabled"},{"Deployment__Profile",""},{"Media__AllowUnscannedDevelopmentUploads","false"},{"Scanner__Provider","Disabled"}};
    foreach(var pair in new[]{("MediaRoot","media"),("SnapshotRoot","snapshots"),("TempRoot","temp"),("KeysRoot","keys")}){var folder=Path.Combine(run,pair.Item2);Directory.CreateDirectory(folder);environment["Storage__"+pair.Item1]=folder;}
    foreach(var pair in environment)start.Environment[pair.Key]=pair.Value;
    web=Process.Start(start)!;stdout=web.StandardOutput.ReadToEndAsync();stderr=web.StandardError.ReadToEndAsync();
    bool ready=false;for(int i=0;i<80;i++){if(web.HasExited)break;try{using var response=await guest.GetAsync("/health");if(response.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(200);}
    Check(ready,"Loopback Web with isolated SQL and runtime role ready");
    async Task<AngleSharp.Dom.IDocument> Page(HttpClient client,string path){using var r=await client.GetAsync(path);if(r.StatusCode!=HttpStatusCode.OK)throw new InvalidOperationException("GET "+path+" status "+(int)r.StatusCode);return new HtmlParser().ParseDocument(await r.Content.ReadAsStringAsync());}
    async Task<HttpClient> Login(string who)
    {
        var c=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new()}){BaseAddress=new(url)};
        var page=await Page(c,"/admin/login");var question=page.QuerySelector("#captcha-question")!.TextContent;
        var numbers=Regex.Matches(question,@"\d+").Select(m=>int.Parse(m.Value)).ToArray();
        if(numbers.Length!=2||!(question.Contains("cộng")||question.Contains("trừ")))throw new InvalidOperationException("Unexpected CAPTCHA");
        var fields=new Dictionary<string,string>{{"__RequestVerificationToken",page.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!},{"CaptchaId",page.QuerySelector("input[name=CaptchaId]")!.GetAttribute("value")!},{"CaptchaAnswer",(question.Contains("cộng")?numbers[0]+numbers[1]:numbers[0]-numbers[1]).ToString()},{"UserName",accounts[who].User.UserName!},{"Password",accounts[who].Password}};
        using var response=await c.PostAsync("/admin/login",new FormUrlEncodedContent(fields));Check(response.StatusCode==HttpStatusCode.Redirect,"Identity + actual CAPTCHA login: "+who);return c;
    }
    using var writer=await Login("writer");using var reviewer=await Login("reviewer");using var publisher=await Login("publisher");
    async Task<JsonElement> Post(HttpClient c,string route,Dictionary<string,string> fields,string formPath)
    {
        var form=await Page(c,formPath);fields["__RequestVerificationToken"]=form.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
        using var response=await c.PostAsync(route,new FormUrlEncodedContent(fields));
        if(response.StatusCode!=HttpStatusCode.OK)throw new InvalidOperationException("POST "+route+" status "+(int)response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.Clone();
    }
    Guid Id(JsonElement s)=>s.GetProperty("contentId").GetGuid();Guid Revision(JsonElement s)=>s.GetProperty("currentRevisionId").GetGuid();
    async Task<JsonElement> Save(JsonElement? previous,ContentKind kind,bool featured,string? email,string title)
    {
        var fields=new Dictionary<string,string>{{"Kind",kind.ToString()},{"Title",title},{"Summary","Isolated technical test only"},{"Author","Technical fixture"},{"BodyHtml","<p>Isolated workflow validation; never LAN or Production.</p>"},{"CategoryId","1"},{"Metadata.Featured",featured.ToString()},{"RequestId",Guid.NewGuid().ToString()},{"intent","Save"},{"format","json"}};
        if(kind==ContentKind.Article)fields["CoverAssetId"]=cover.Id.ToString();
        if(email!=null)fields["Metadata.ConsultationEmail"]=email;
        if(previous is JsonElement p){fields["Id"]=Id(p).ToString();fields["RowVersion"]=p.GetProperty("rowVersion").GetString()!;}
        return await Post(writer,"/admin/save",fields,previous is JsonElement x?"/admin/edit/"+Id(x):"/admin/create?kind="+kind);
    }
    async Task<JsonElement> Act(HttpClient client,JsonElement s,string action,string reason="")=>await Post(client,"/admin/workflow/"+Id(s),new(){{"Action",action},{"RowVersion",s.GetProperty("rowVersion").GetString()!},{"RevisionId",Revision(s).ToString()},{"RequestId",Guid.NewGuid().ToString()},{"Reason",reason}},"/admin");
    stage="FR10_HTTP_SQL";
    var article=await Save(null,ContentKind.Article,false,null,"TECHNICAL FR10");var first=Revision(article);
    article=await Save(article,ContentKind.Article,true,null,"TECHNICAL FR10");var second=Revision(article);Check(first!=second,"FR10 enable creates revision");
    article=await Save(article,ContentKind.Article,false,null,"TECHNICAL FR10");var third=Revision(article);Check(second!=third,"FR10 disable creates revision");
    article=await Save(article,ContentKind.Article,false,null,"TECHNICAL FR10");Check(Revision(article)==third,"FR10 unchanged save reuses revision");
    article=await Act(writer,article,"Submit");article=await Act(reviewer,article,"Reject","Technical revision review");
    article=await Save(article,ContentKind.Article,true,null,"TECHNICAL FR10 revised");article=await Act(writer,article,"Submit");article=await Act(reviewer,article,"Approve");
    using(var hidden=await guest.GetAsync("/bai-viet/"+Id(article)))Check(hidden.StatusCode==HttpStatusCode.NotFound,"FR10 approved draft hidden before publish");
    article=await Act(publisher,article,"PublishApproved");var published=Revision(article);
    article=await Act(writer,article,"BeginEdit");article=await Save(article,ContentKind.Article,false,null,"TECHNICAL FR10 private edit");
    article=await Act(writer,article,"Submit");article=await Act(reviewer,article,"Approve");
    var articleId=Id(article);
    using(var db=new PortalDbContext(options))Check((await db.Contents.AsNoTracking().SingleAsync(c=>c.Id==articleId)).PublishedRevisionId==published&&(await db.Revisions.AsNoTracking().SingleAsync(r=>r.Id==published)).Metadata.Featured==true,"FR10 current approval preserves published featured revision");
    var articlePage=await Page(guest,"/bai-viet/"+Id(article));Check(articlePage.Title?.Contains("TECHNICAL FR10 revised")==true,"FR10 public HTTP still shows original approved title");
    stage="FR06_HTTP_SQL";
    var draft=await Save(null,ContentKind.DraftDocument,false,"first@example.invalid","TECHNICAL FR06");
    var preview=await Page(writer,"/admin/preview/"+Id(draft));Check(preview.Body!.TextContent.Contains("first@example.invalid"),"FR06 preview renders email");
    draft=await Act(writer,draft,"Submit");draft=await Act(reviewer,draft,"Approve");draft=await Act(publisher,draft,"PublishApproved");var publishedDraft=Revision(draft);
    draft=await Act(writer,draft,"BeginEdit");draft=await Save(draft,ContentKind.DraftDocument,false,"second@example.invalid","TECHNICAL FR06");
    Check(Revision(draft)!=publishedDraft,"FR06 email-only change creates revision");
    using(var compare=await writer.GetAsync("/admin/history/"+Id(draft)+"/compare?revisionId="+Revision(draft))){var text=await compare.Content.ReadAsStringAsync();Check(compare.IsSuccessStatusCode&&text.Contains("first@example.invalid")&&text.Contains("second@example.invalid"),"FR06 history comparison shows each revision email");}
    draft=await Act(writer,draft,"Submit");draft=await Act(reviewer,draft,"Approve");
    var oldPage=await Page(guest,"/bai-viet/"+Id(draft));Check(oldPage.Body!.TextContent.Contains("first@example.invalid")&&!oldPage.Body.TextContent.Contains("second@example.invalid"),"FR06 draft and approval do not mutate public email");
    draft=await Act(publisher,draft,"PublishApproved");var nextPage=await Page(guest,"/bai-viet/"+Id(draft));Check(nextPage.Body!.TextContent.Contains("second@example.invalid"),"FR06 publish renders exact approved email");
    await using(var db=new PortalDbContext(options)){Check((await db.Revisions.AsNoTracking().SingleAsync(r=>r.Id==publishedDraft)).Metadata.ConsultationEmail=="first@example.invalid","FR06 old published revision preserved in SQL");Check(await db.SourceRecords.CountAsync()==0,"No source content imported into technical database");}
    stage="COMPLETE";
}
catch(Exception ex){Console.WriteLine("BLOCKED "+stage+" / "+ex.GetType().Name+(ex is SqlException s?" SQL "+s.Number:""));await File.WriteAllTextAsync(Path.Combine(run,"failure.txt"),stage+"\n"+ex.GetType().Name+"\n"+ex.StackTrace);Environment.ExitCode=1;}
finally
{
    if(web is not null){if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(run,"web-private.log"),(stdout==null?"":await stdout)+"\n"+(stderr==null?"":await stderr));web.Dispose();}
    await File.WriteAllTextAsync(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{stage,database,checks,sqlFixtureRetained=true,lanWrites=false,sharedSqlAccess=false,smtp="Disabled",baAcceptance="PENDING",backup="NOT RUN"},new JsonSerializerOptions{WriteIndented=true}));
}
