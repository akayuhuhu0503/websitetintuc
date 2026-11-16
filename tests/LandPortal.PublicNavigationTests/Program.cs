using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

if(args.Length!=5||args[0] is not ("before" or "after" or "feedback"))throw new ArgumentException("phase, fixture root, host, dotnet, browser module required");
var root=Directory.GetCurrentDirectory();var fixture=Path.GetFullPath(args[1]);
if(!fixture.StartsWith(Path.Combine(root,".local","tests","nav-"),StringComparison.OrdinalIgnoreCase))throw new Exception("Isolated fixture only");
var statePath=Path.Combine(fixture,"fixture.json");Fixture state;
if(!File.Exists(statePath)){
 var instance="LPNav_"+Guid.NewGuid().ToString("N")[..12];
 var local=new ProcessStartInfo("SqlLocalDB.exe"){UseShellExecute=false,CreateNoWindow=true};local.ArgumentList.Add("create");local.ArgumentList.Add(instance);local.ArgumentList.Add("-s");using(var p=Process.Start(local)!){await p.WaitForExitAsync();if(p.ExitCode!=0)throw new Exception("New LocalDB unavailable");}
 state=new(instance,Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),Guid.Empty);
 var sql=new SqlConnectionStringBuilder{DataSource=@"(localdb)\"+instance,InitialCatalog="master",IntegratedSecurity=true,Encrypt=true,TrustServerCertificate=true,Pooling=false};
 await using(var c=new SqlConnection(sql.ConnectionString)){
 await c.OpenAsync();using var q=c.CreateCommand();q.CommandText="SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB')),DB_ID('LandPortal_Test')";using(var r=await q.ExecuteReaderAsync()){await r.ReadAsync();if(r.GetInt32(0)!=1||!r.IsDBNull(1))throw new Exception("Fresh LocalDB required");}
 var mdf=Path.Combine(fixture,"LandPortal_Test.mdf");var ldf=Path.Combine(fixture,"LandPortal_Test_log.ldf");q.CommandText=$"CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'{mdf.Replace("'","''")}') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'{ldf.Replace("'","''")}')";await q.ExecuteNonQueryAsync();}
 sql.InitialCatalog="LandPortal_Test";var options=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(sql.ConnectionString,o=>o.UseCompatibilityLevel(160)).Options;
 await using var db=new PortalDbContext(options);await db.Database.MigrateAsync();if((await db.Database.GetAppliedMigrationsAsync()).Count()!=18)throw new Exception("Schema18 required");await RuntimeRoleInterceptor.ProvisionAsync(db,state.Role);
 var now=DateTimeOffset.UtcNow;
 for(int i=1;i<=4;i++)db.Categories.Add(new(){Id=i,Name="Chuyên mục thử "+i,Slug="fixture-"+i,SortOrder=i});await db.SaveChangesAsync();
 for(int i=0;i<120;i++){
 var c=new Content{Kind=i<100?ContentKind.Article:i<110?ContentKind.Announcement:ContentKind.LegalDocument,PrimaryCategoryId=i%4+1,IsPublic=true,State=WorkflowState.Published,PublishedAtUtc=now.AddMinutes(-i-1)};
 var r=new ContentRevision{Content=c,Title="Bài thử điều hướng "+i,Summary="Nội dung giả chỉ dùng đo điều hướng trong môi trường cô lập.",BodyHtml="<p>Văn bản giả kiểm thử hiệu năng.</p>",PrimaryCategoryId=c.PrimaryCategoryId,Author="Fixture",ContentHash=new string('a',64),SearchText="bai thu dieu huong "+i};r.Metadata.Featured=i<4;c.Revisions.Add(r);db.Contents.Add(c);await db.SaveChangesAsync();c.PublishedRevisionId=c.CurrentRevisionId=r.Id;if(i==0)state=state with{Article=c.Id};await db.SaveChangesAsync();
 }
 var site=new SiteConfigurationRevision{OrganizationName="CỤC QUẢN LÝ ĐẤT ĐAI",FooterText="Môi trường thử nghiệm cô lập",EnglishName="Department of Land Administration"};
 site.Menus.Add(new(){Label="Trang chủ",RouteKey="Home"});site.Menus.Add(new(){Label="Tin tức",RouteKey="News",SortOrder=1});site.Menus.Add(new(){Label="Chuyên mục thử",TargetKind="Category",CategoryId=1,SortOrder=2});
 for(int i=1;i<=4;i++){site.Categories.Add(new(){CategoryId=i,DisplayName="Chuyên mục thử "+i,Slug="fixture-"+i});site.Blocks.Add(new(){Type="Category",CategoryId=i,Title="Chuyên mục thử "+i,Limit=3,SortOrder=i+2});}
 site.Blocks.Add(new(){Type="Featured",Limit=3,Title="Tin chính"});site.Blocks.Add(new(){Type="Recent",Limit=7,SortOrder=1,Title="Tin mới cập nhật"});site.Blocks.Add(new(){Type="Announcements",Limit=4,SortOrder=8,Title="Thông báo"});site.Blocks.Add(new(){Type="LegalDocuments",Limit=4,SortOrder=9,Title="Văn bản mới cập nhật"});
 db.Add(site);await db.SaveChangesAsync();db.Add(new SiteConfiguration{LiveRevisionId=site.Id});await db.SaveChangesAsync();await File.WriteAllTextAsync(statePath,JsonSerializer.Serialize(state));
}else state=JsonSerializer.Deserialize<Fixture>(await File.ReadAllTextAsync(statePath))!;
if(!Regex.IsMatch(state.Instance,"^LPNav_[a-f0-9]{12}$"))throw new Exception("LocalDB allowlist failed");
var run=Path.Combine(fixture,args[0]+"-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(run);
var cs=new SqlConnectionStringBuilder{DataSource=@"(localdb)\"+state.Instance,InitialCatalog="LandPortal_Test",IntegratedSecurity=true,Encrypt=true,TrustServerCertificate=true,Pooling=false};
// Before any repeat, bind to the exact fixture's SQL file and official schema.
await using(var verify=new SqlConnection(cs.ConnectionString)) {
 await verify.OpenAsync();using var q=verify.CreateCommand();q.CommandText="SELECT DB_NAME(),CONVERT(int,SERVERPROPERTY('IsLocalDB')),(SELECT physical_name FROM sys.database_files WHERE file_id=1),(SELECT COUNT(*) FROM __EFMigrationsHistory)";
 using(var r=await q.ExecuteReaderAsync()){await r.ReadAsync();if(r.GetString(0)!="LandPortal_Test"||r.GetInt32(1)!=1||!Path.GetFullPath(r.GetString(2)).Equals(Path.Combine(fixture,"LandPortal_Test.mdf"),StringComparison.OrdinalIgnoreCase)||r.GetInt32(3)!=18)throw new Exception("Fixture ownership/schema mismatch");}
 // Reset only this disposable database's generated traffic so the configured
 // popular-news components see the same empty starting state in both runs.
 q.CommandText="DELETE FROM AuditLogs WHERE Action='PublicArticleView' AND Actor LIKE 'Reader:%'; DELETE FROM PublicVisit;";await q.ExecuteNonQueryAsync();
}
var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var url="http://127.0.0.1:"+port;
var si=new ProcessStartInfo(args[3]){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};si.ArgumentList.Add(args[2]);si.ArgumentList.Add("--contentRoot");si.ArgumentList.Add(Path.Combine(root,"src","Web"));
foreach(var key in si.Environment.Keys.Where(k=>k.Contains("__")||k.StartsWith("LANDPORTAL_")||k.StartsWith("ASPNETCORE_")||k=="DOTNET_ENVIRONMENT").ToArray())si.Environment.Remove(key);
foreach(var p in new Dictionary<string,string>{{"ConnectionStrings__Portal",cs.ConnectionString},{"Sql__ExpectedDatabase","LandPortal_Test"},{"Runtime__RolePassword",state.Role},{"Public__BaseUrl",url},{"Public__CanonicalHost","127.0.0.1"},{"DOTNET_ENVIRONMENT","Development"},{"ASPNETCORE_ENVIRONMENT","Development"},{"Mail__Transport","Disabled"},{"Scanner__Provider","Disabled"},{"NavigationTestRoot",fixture},{"NavigationMetricsRoot",run},{"WorkspaceRoot",root},{"Logging__LogLevel__Default","Warning"}})si.Environment[p.Key]=p.Value;
foreach(var name in new[]{"Media","Snapshot","Temp","Keys"}){var dir=Path.Combine(fixture,name);Directory.CreateDirectory(dir);si.Environment["Storage__"+name+"Root"]=dir;}
using var web=Process.Start(si)!;var stdout=web.StandardOutput.ReadToEndAsync();var stderr=web.StandardError.ReadToEndAsync();
try{
 using var http=new HttpClient{BaseAddress=new Uri(url),Timeout=TimeSpan.FromSeconds(20)};bool ready=false;for(int i=0;i<100;i++){if(web.HasExited)break;try{if((await http.GetAsync("/health")).IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(150);}if(!ready)throw new Exception("HTTP host not ready");
 if(args[0]!="feedback") {var node=new ProcessStartInfo("node"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true};node.ArgumentList.Add(Path.Combine(root,"tests/browser/public-navigation-check.mjs"));node.ArgumentList.Add(url);node.ArgumentList.Add(run);node.ArgumentList.Add(state.Article.ToString());node.Environment["CMS_PLAYWRIGHT_MODULE"]=args[4];using(var p=Process.Start(node)!){await p.WaitForExitAsync();if(p.ExitCode!=0)throw new Exception("Browser measurement failed");}}
 Console.WriteLine("NAVIGATION_PASS "+run);
 if(args[0] is "after" or "feedback"){
  var feedback=new ProcessStartInfo("node"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true};feedback.ArgumentList.Add(Path.Combine(root,"tests/browser/public-navigation-feedback.mjs"));feedback.ArgumentList.Add(url);feedback.ArgumentList.Add(run);feedback.Environment["CMS_PLAYWRIGHT_MODULE"]=args[4];using(var p=Process.Start(feedback)!){await p.WaitForExitAsync();if(p.ExitCode!=0)throw new Exception("Navigation feedback failed");}
  if(args[0]=="after")await FocusedChecks.Run(cs.ConnectionString,state.Role,fixture,http,state.Article);
 }
}finally{if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(run,"host.log"),await stdout+await stderr);await File.WriteAllTextAsync(Path.Combine(fixture,"latest-"+args[0]+".txt"),run);}
record Fixture(string Instance,string Role,Guid Article);
