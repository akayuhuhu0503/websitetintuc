using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using UglyToad.PdfPig.Writer;

if(args.Length is not (3 or 4) || !Regex.IsMatch(args[0],"^LPNoAv_[a-f0-9]{12}$") || args.Length==4 && args[3] is not ("trusted" or "managed"))throw new ArgumentException("New isolated LocalDB instance/run/host required");
var trustedMode=args.Length==4 && args[3]=="trusted";
var root=Directory.GetCurrentDirectory();var run=Path.GetFullPath(args[1]);var host=Path.GetFullPath(args[2]);
if(!run.StartsWith(Path.Combine(root,".local","tests","noav-http-"),StringComparison.OrdinalIgnoreCase)||!host.StartsWith(run+Path.DirectorySeparatorChar)||!File.Exists(host)||File.Exists(Path.Combine(run,"result.json")))throw new Exception("New exact test run required");
var cs=new SqlConnectionStringBuilder{DataSource=@"(localdb)\"+args[0],InitialCatalog="LandPortal_Test",IntegratedSecurity=true,Encrypt=true,TrustServerCertificate=true,Pooling=false};
var options=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString,o=>o.UseCompatibilityLevel(160)).Options;
var checks=new List<string>();var stage="create-empty-test";Process? web=null;Task<string>? stdout=null,stderr=null;
void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);Console.WriteLine("PASS "+name);}
try {
 await using(var sql=new SqlConnection(new SqlConnectionStringBuilder(cs.ConnectionString){InitialCatalog="master"}.ConnectionString)){
  await sql.OpenAsync();using var q=sql.CreateCommand();q.CommandText="SELECT CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),CONVERT(int,SERVERPROPERTY('IsLocalDB')),DB_ID('LandPortal_Test')";
  using(var r=await q.ExecuteReaderAsync()){await r.ReadAsync();Check(r.GetString(0).Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase)&&r.GetInt32(1)==1&&r.IsDBNull(2),"Fresh local SQL Server instance with absent test DB");}
  var mdf=Path.Combine(run,"LandPortal_Test.mdf");var ldf=Path.Combine(run,"LandPortal_Test_log.ldf");
  if(File.Exists(mdf)||File.Exists(ldf))throw new Exception("Fixture SQL files already exist; never overwrite");
  q.CommandText="CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'"+mdf.Replace("'","''")+"') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'"+ldf.Replace("'","''")+"')";await q.ExecuteNonQueryAsync();
  q.CommandText="ALTER DATABASE [LandPortal_Test] SET COMPATIBILITY_LEVEL=160";await q.ExecuteNonQueryAsync();
 }
 stage="migrate-new-test-only";
 var roleSecret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
 await using(var db=new PortalDbContext(options)){
  await db.Database.MigrateAsync();var history=(await db.Database.GetAppliedMigrationsAsync()).ToArray();
  Check(history.Length==18&&history.Last()=="20261110050000_HomeEventBanner","SQL Server exact schema18 via official migrations on new DB only");
  await RuntimeRoleInterceptor.ProvisionAsync(db,roleSecret);
  db.Categories.Add(new(){Id=1,Name="Isolated upload test",Slug="isolated-upload-test"});await db.SaveChangesAsync();
  if(trustedMode)
  {
   var site=new SiteConfigurationRevision();
   site.Categories.Add(new(){CategoryId=1,DisplayName="Isolated upload test",Slug="isolated-upload-test",ShowInNavigation=true});
   site.Menus.Add(new(){Label="Tài liệu",TargetKind="Route",RouteKey="Materials",CategoryId=1});
   db.Add(site);await db.SaveChangesAsync();db.Add(new SiteConfiguration{LiveRevisionId=site.Id});await db.SaveChangesAsync();
  }
 }
 var services=new ServiceCollection();services.AddLogging();services.AddScoped(_=>new PortalDbContext(options));
 services.AddIdentityCore<PortalUser>(PortalIdentityPolicy.Configure).AddRoles<IdentityRole>().AddEntityFrameworkStores<PortalDbContext>();
 using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
 var accounts=new Dictionary<string,(PortalUser User,string Password)>();
 foreach(var entry in new[]{("writer",new[]{Permissions.Create,Permissions.ViewOwn,Permissions.EditOwn,Permissions.Submit,Permissions.ManageMedia}), ("reviewer",new[]{Permissions.Review}), ("publisher",new[]{Permissions.PublishApproved}), ("outsider",new[]{Permissions.ViewOwn})}){
  var user=new PortalUser{UserName="fixture-"+entry.Item1,Email=entry.Item1+"@example.invalid",DisplayName="Isolated "+entry.Item1,EmailConfirmed=true,LockoutEnabled=true};var password="Aa1!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
  Check((await users.CreateAsync(user,password)).Succeeded,"Identity fixture "+entry.Item1);
  var permissions=entry.Item2.AsEnumerable();if(trustedMode&&entry.Item1=="writer")permissions=permissions.Append(Permissions.ManageSettings);
  Check((await users.AddClaimsAsync(user,permissions.Select(p=>new Claim("permission",p)))).Succeeded,"Scoped claims "+entry.Item1);accounts[entry.Item1]=(user,password);
 }
 stage="start-real-http";
 var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var url="http://127.0.0.1:"+port;
 var dotnetHost=Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..","dotnet.exe"));
 var start=new ProcessStartInfo(dotnetHost){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
 start.ArgumentList.Add(host);start.ArgumentList.Add("--contentRoot");start.ArgumentList.Add(Path.Combine(root,"src","Web"));
 // No inherited deployment configuration/credentials enter the isolated test host.
 foreach(var key in start.Environment.Keys.Where(k=>k.Contains("__")||k.StartsWith("LANDPORTAL_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("ASPNETCORE_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("DOTNET_ENVIRONMENT",StringComparison.OrdinalIgnoreCase)).ToArray())start.Environment.Remove(key);
 foreach(var pair in new Dictionary<string,string>{{"ConnectionStrings__Portal",cs.ConnectionString},{"Sql__ExpectedDatabase","LandPortal_Test"},{"Runtime__RolePassword",roleSecret},{"Public__BaseUrl",url},{"Public__CanonicalHost","127.0.0.1"},{"DOTNET_ENVIRONMENT","Development"},{"ASPNETCORE_ENVIRONMENT","Development"},{"Mail__Transport","Disabled"},{"Deployment__Profile",""},{"Scanner__Provider","Disabled"},{"WorkspaceRoot",root},{"ManagedHttpTestRoot",run},{"Logging__LogLevel__Default","Warning"}})start.Environment[pair.Key]=pair.Value;
 foreach(var pair in new[]{("MediaRoot","media"),("SnapshotRoot","snapshots"),("TempRoot","temp"),("KeysRoot","keys")}){var dir=Path.Combine(run,pair.Item2);Directory.CreateDirectory(dir);start.Environment["Storage__"+pair.Item1]=dir;}
 start.Environment["TrustedHttpTestMode"]=trustedMode?"true":"false";
 web=Process.Start(start)!;stdout=web.StandardOutput.ReadToEndAsync();stderr=web.StandardError.ReadToEndAsync();
 HttpClient Client()=>new(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new()}){BaseAddress=new(url),Timeout=TimeSpan.FromSeconds(25)};
 using var guest=Client();bool ready=false;for(int i=0;i<60;i++){if(web.HasExited)break;try{using var r=await guest.GetAsync("/ready");if(r.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(200);}
 Check(ready,"Real Kestrel HTTP and SQL runtime role ready without scanner");
 async Task<AngleSharp.Dom.IDocument> Page(HttpClient c,string path){using var r=await c.GetAsync(path);if(r.StatusCode!=HttpStatusCode.OK)throw new Exception("GET "+path+" status "+(int)r.StatusCode);return new HtmlParser().ParseDocument(await r.Content.ReadAsStringAsync());}
 async Task<string> Token(HttpClient c,string path="/admin")=>(await Page(c,path)).QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
 async Task<HttpClient> Login(string who){var c=Client();var p=await Page(c,"/admin/login");var question=p.QuerySelector("#captcha-question")!.TextContent;var numbers=Regex.Matches(question,@"\d+").Select(m=>int.Parse(m.Value)).ToArray();if(numbers.Length!=2||!(question.Contains("cộng")||question.Contains("trừ")))throw new Exception("Unexpected CAPTCHA");
 using var r=await c.PostAsync("/admin/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",p.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!},{"CaptchaId",p.QuerySelector("input[name=CaptchaId]")!.GetAttribute("value")!},{"CaptchaAnswer",(question.Contains("cộng")?numbers[0]+numbers[1]:numbers[0]-numbers[1]).ToString()},{"UserName",accounts[who].User.UserName!},{"Password",accounts[who].Password}}));Check(r.StatusCode==HttpStatusCode.Redirect,"HTTP Identity/CAPTCHA login "+who);return c;}
 using var writer=await Login("writer");using var reviewer=await Login("reviewer");using var publisher=await Login("publisher");using var outsider=await Login("outsider");
 async Task<JsonElement> Post(HttpClient c,string route,Dictionary<string,string> fields,string form="/admin"){
 fields["__RequestVerificationToken"]=await Token(c,form);using var r=await c.PostAsync(route,new FormUrlEncodedContent(fields));var body=await r.Content.ReadAsStringAsync();if(r.StatusCode!=HttpStatusCode.OK){await File.WriteAllTextAsync(Path.Combine(run,"last-response.txt"),body);throw new Exception("POST "+route+" status "+(int)r.StatusCode);}return JsonDocument.Parse(body).RootElement.Clone();}
 Guid Id(JsonElement x)=>x.GetProperty("contentId").GetGuid();Guid Rev(JsonElement x)=>x.GetProperty("currentRevisionId").GetGuid();
 var assets=new List<Guid>();Guid? cover=null;
 async Task<JsonElement> Save(JsonElement? old=null){var f=new Dictionary<string,string>{{"Kind",trustedMode?"StaticPage":"Article"},{"Title","ISOLATED NOAV HTTP TEST"},{"Summary","Synthetic fixture only"},{"Author","Automated fixture"},{"BodyHtml","<p>Isolated upload workflow verification.</p>"},{"CategoryId","1"},{"RequestId",Guid.NewGuid().ToString()},{"intent","Save"},{"format","json"}};if(trustedMode)f["Metadata.PublicationDate"]="2026-01-30";if(cover!=null)f["CoverAssetId"]=cover.ToString()!;if(!trustedMode&&assets.Count>=2)f["BodyHtml"]+=$"<p><img src='/media/{assets[1]}' alt='Fixture inline PNG'></p>";var first=trustedMode?0:2;for(var i=first;i<assets.Count;i++){f[$"AttachmentIds[{i-first}]"]=assets[i].ToString();}if(old is {} s){f["Id"]=Id(s).ToString();f["RowVersion"]=s.GetProperty("rowVersion").GetString()!;}return await Post(writer,"/admin/save",f,old is {} v?"/admin/edit/"+Id(v):"/admin/create");}
 async Task<JsonElement> Act(HttpClient c,JsonElement s,string action)=>await Post(c,"/admin/workflow/"+Id(s),new(){{"Action",action},{"RowVersion",s.GetProperty("rowVersion").GetString()!},{"RevisionId",Rev(s).ToString()},{"RequestId",Guid.NewGuid().ToString()}});
 async Task<HttpResponseMessage> Upload(HttpClient c,string route,string filename,string mime,byte[] bytes,bool csrf=true,bool trusted=false){using var form=new MultipartFormDataContent();var file=new ByteArrayContent(bytes);file.Headers.ContentType=new(mime);form.Add(file,"file",filename);if(trusted){form.Add(new StringContent("true"),"trustedInternal");form.Add(new StringContent("Synthetic administrator-confirmed fixture"),"trustedSource");}using var req=new HttpRequestMessage(HttpMethod.Post,route){Content=form};if(csrf)req.Headers.Add("X-CSRF-TOKEN",await Token(c));return await c.SendAsync(req);}
 byte[] Image(SKEncodedImageFormat f){using var bitmap=new SKBitmap(12,8);bitmap.Erase(SKColors.DarkRed);using var image=SKImage.FromBitmap(bitmap);using var bytes=image.Encode(f,90);return bytes.ToArray();}
 byte[] Office(bool word){using var mem=new MemoryStream();using(var zip=new ZipArchive(mem,ZipArchiveMode.Create,true)){void Entry(string n,string s){using var w=new StreamWriter(zip.CreateEntry(n).Open());w.Write(s);}var main=word?"word/document.xml":"xl/workbook.xml";var type=word?OfficeDocumentPolicy.Docx+".main+xml":OfficeDocumentPolicy.Xlsx+".main+xml";Entry("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/"+main+"\" ContentType=\""+type+"\"/>"+(word?"":"<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")+"</Types>");Entry("_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\""+main+"\"/></Relationships>");Entry(main,word?"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Fixture document</w:t></w:r></w:p></w:body></w:document>":"<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Fixture\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");if(!word){Entry("xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");Entry("xl/worksheets/sheet1.xml","<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Fixture</t></is></c></row></sheetData></worksheet>");}}return mem.ToArray();}
 if(trustedMode)
 {
  Check(EditorialDocumentPolicy.PdfMaximum==104L*1024*1024&&EditorialDocumentPolicy.PdfMaximumPages==2000&&EditorialZipPolicy.Maximum==128L*1024*1024&&EditorialDocumentPolicy.RequestMaximum==129L*1024*1024,"PDF/ZIP limits remain 104 MiB/2000 pages, 128 MiB and request 129 MiB");
  stage="trusted-http-upload";var draft=await Save();var route="/admin/documents/"+Id(draft);
  Check((await Page(writer,route)).QuerySelector("input[name=trustedInternal]")!=null,"Admin sees explicit unchecked internal-source confirmation");
  using var pdfFixture=new PdfDocumentBuilder();pdfFixture.AddPage(100,100);var pdfBytes=pdfFixture.Build();
  using var archiveBytes=new MemoryStream();using(var archive=new ZipArchive(archiveBytes,ZipArchiveMode.Create,true)){using var entry=archive.CreateEntry("signed-original.pdf").Open();entry.Write(pdfBytes);}var zipBytes=archiveBytes.ToArray();
  using(var denied=await Upload(writer,route,"fixture.zip",EditorialZipPolicy.Mime,zipBytes))Check(denied.StatusCode==HttpStatusCode.BadRequest||denied.StatusCode==HttpStatusCode.ServiceUnavailable,"ZIP without internal attestation still denied without scanner");
  using(var denied=await Upload(writer,route,"fixture.zip",EditorialZipPolicy.Mime,zipBytes,csrf:false,trusted:true))Check(denied.StatusCode==HttpStatusCode.BadRequest,"Trusted upload still requires HTTP CSRF");
  using(var denied=await Upload(outsider,route,"fixture.zip",EditorialZipPolicy.Mime,zipBytes,trusted:true))Check(denied.StatusCode==HttpStatusCode.Forbidden||denied.StatusCode==HttpStatusCode.Redirect&&denied.Headers.Location?.ToString().Contains("access-denied")==true,"Unauthorized user cannot attest internal file");
  using(var denied=await Upload(writer,route,"fake.pdf","application/pdf","broken"u8.ToArray(),trusted:true))Check(denied.StatusCode==HttpStatusCode.BadRequest,"Internal attestation does not accept fake PDF");
  var expected=new Dictionary<Guid,byte[]>();
  foreach(var item in new[]{("original.pdf","application/pdf",pdfBytes),("documents.zip",EditorialZipPolicy.Mime,zipBytes)})
  {
   using var response=await Upload(writer,route+"?format=json",item.Item1,item.Item2,item.Item3,trusted:true);
   if(!response.IsSuccessStatusCode){await File.WriteAllTextAsync(Path.Combine(run,"last-response.txt"),await response.Content.ReadAsStringAsync());throw new Exception("Trusted upload HTTP "+(int)response.StatusCode);}
   var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;var id=json.GetProperty("id").GetGuid();assets.Add(id);expected[id]=item.Item3;
   Check(json.GetProperty("scanStatus").GetString()=="NotScanned"&&json.GetProperty("usable").GetBoolean(),item.Item1+" HTTP internal upload truthful NotScanned");
   using var privateFile=await writer.GetAsync("/admin/documents/file/"+id);Check(privateFile.IsSuccessStatusCode&&(await privateFile.Content.ReadAsByteArrayAsync()).SequenceEqual(item.Item3),item.Item1+" private HTTP bytes unchanged");
   using var hidden=await guest.GetAsync("/media/"+id);Check(hidden.StatusCode==HttpStatusCode.NotFound,item.Item1+" not public while draft");
  }
  using(var retry=await Upload(writer,route+"?format=json","documents.zip",EditorialZipPolicy.Mime,zipBytes,trusted:true))Check(retry.IsSuccessStatusCode&&JsonDocument.Parse(await retry.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid()==assets[1],"HTTP retry uses same ZIP Id");
  draft=await Save(draft);
  using(var listing=await guest.GetAsync("/tai-lieu"))Check(listing.StatusCode==HttpStatusCode.OK&&!(await listing.Content.ReadAsStringAsync()).Contains("ISOLATED NOAV HTTP TEST"),"Materials HTTP hides draft internal documents");
  await using(var db=new PortalDbContext(options)){var id=Id(draft);Check(await db.DraftAssets.CountAsync(x=>x.ContentId==id)==2&&await db.MediaAssets.CountAsync()==2,"Exactly two attachments and no duplicate on HTTP retry");Check(await db.AuditLogs.CountAsync(x=>x.Action==TrustedInternalDocumentPolicy.GrantAction)==2&&await db.EditorialDocuments.AllAsync(x=>x.ScanState==InquiryScanState.NotScanned&&x.Attempts==0&&x.EngineVersion==null&&x.ScannedUtc==null),"SQL records real administrator grants, no scanner proof");}
  stage="trusted-http-workflow";draft=await Act(writer,draft,"Submit");Check(true,"HTTP Submit internal attachments");draft=await Act(reviewer,draft,"Approve");Check(true,"HTTP Reviewer approves exact revision");
  using(var hidden=await guest.GetAsync("/bai-viet/"+Id(draft)))Check(hidden.StatusCode==HttpStatusCode.NotFound,"Approved internal collection hidden until publish");
  draft=await Act(publisher,draft,"PublishApproved");Check(true,"HTTP Publisher publishes through unchanged workflow");
  using(var articlePage=await guest.GetAsync("/bai-viet/"+Id(draft)))Check(articlePage.IsSuccessStatusCode&&(await articlePage.Content.ReadAsStringAsync()).Contains("ISOLATED NOAV HTTP TEST"),"Published article HTTP 200");
  foreach(var pair in expected){using var response=await guest.GetAsync("/media/"+pair.Key+"?download=true");Check(response.IsSuccessStatusCode&&response.Content.Headers.ContentDisposition?.DispositionType=="attachment"&&response.Headers.GetValues("X-Content-Type-Options").Single()=="nosniff"&&response.Headers.GetValues("X-Media-Scan-Status").Single()=="NotScanned"&&(await response.Content.ReadAsByteArrayAsync()).SequenceEqual(pair.Value),"Public HTTP PDF/ZIP attachment with exact bytes and truthful headers");}
  // ASP.NET Core omits Content-Disposition when no download filename is supplied; browsers render the PDF.
  using(var preview=await guest.GetAsync("/media/"+assets[0]))Check(preview.StatusCode==HttpStatusCode.OK&&preview.Content.Headers.ContentType?.MediaType=="application/pdf"&&preview.Content.Headers.ContentDisposition?.DispositionType is (null or "inline")&&preview.Headers.GetValues("X-Content-Type-Options").Single()=="nosniff"&&(await preview.Content.ReadAsByteArrayAsync()).SequenceEqual(pdfBytes),"Published internal PDF remains viewable inline with exact bytes");
  using(var zip=await guest.GetAsync("/media/"+assets[1]))Check(zip.StatusCode==HttpStatusCode.OK&&zip.Content.Headers.ContentDisposition?.DispositionType=="attachment","ZIP remains download-only without download query");
  using(var listing=await guest.GetAsync("/tai-lieu"))Check(listing.StatusCode==HttpStatusCode.OK&&(await listing.Content.ReadAsStringAsync()).Contains("/bai-viet/"+Id(draft)),"Materials HTTP lists the published internal collection");
  await using(var db=new PortalDbContext(options)){var revisionId=Rev(draft);var revision=await db.Revisions.SingleAsync(x=>x.Id==revisionId);Check(revision.Metadata.PublicationDate==new DateOnly(2026,1,30),"PublicationDate preserved through review/publication");}
  stage="COMPLETE";return;
 }
 stage="http-create-upload";var article=await Save();Check(Id(article)!=Guid.Empty,"HTTP create private draft");
 foreach(var f in new[]{("jpg","image/jpeg",SKEncodedImageFormat.Jpeg),("png","image/png",SKEncodedImageFormat.Png)}){using var r=await Upload(writer,"/admin/media/upload?contentId="+Id(article),"fixture."+f.Item1,f.Item2,Image(f.Item3));if(!r.IsSuccessStatusCode)throw new Exception("Image upload "+(int)r.StatusCode);var data=JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;var id=data.GetProperty("id").GetGuid();cover??=id;assets.Add(id);Check(data.GetProperty("scanStatus").GetString()=="NotScanned",f.Item1+" HTTP upload truthful NotScanned");}
 using var pdf=new PdfDocumentBuilder();pdf.AddPage(100,100);var documents=new Dictionary<Guid,byte[]>();
 foreach(var f in new[]{("pdf","application/pdf",pdf.Build()),("docx",OfficeDocumentPolicy.Docx,Office(true)),("xlsx",OfficeDocumentPolicy.Xlsx,Office(false))}){using var r=await Upload(writer,"/admin/documents/"+Id(article),"fixture."+f.Item1,f.Item2,f.Item3);if(r.StatusCode!=HttpStatusCode.Redirect){await File.WriteAllTextAsync(Path.Combine(run,"last-response.txt"),await r.Content.ReadAsStringAsync());throw new Exception("Document upload "+f.Item1+" "+(int)r.StatusCode);}await using var db=new PortalDbContext(options);var a=await db.MediaAssets.SingleAsync(x=>x.OriginalName=="fixture."+f.Item1);assets.Add(a.Id);documents[a.Id]=f.Item3;Check(a.ScanStatus=="NotScanned"&&a.ValidationStatus==MediaUploadOptions.ManagedDocumentNoAvValidation,f.Item1+" HTTP upload + SQL truthful state");}
 article=await Save(article);await using(var attached=new PortalDbContext(options)){var cid=Id(article);Check(await attached.DraftAssets.CountAsync(x=>x.ContentId==cid)==5,"HTTP attach all 5 formats verified in SQL draft");}
 using(var p=await writer.GetAsync("/admin/preview/"+Id(article)))Check(p.IsSuccessStatusCode,"Writer HTTP draft preview");
 foreach(var id in documents.Keys){using var hidden=await guest.GetAsync("/media/"+id);Check(hidden.StatusCode==HttpStatusCode.NotFound,"Draft document is not public");using var download=await writer.GetAsync("/admin/documents/file/"+id);Check(download.IsSuccessStatusCode&&(await download.Content.ReadAsByteArrayAsync()).SequenceEqual(documents[id]),"Private HTTP document byte match");}
 stage="http-negative";
 using(var r=await Upload(writer,"/admin/media/upload?contentId="+Id(article),"fake.jpg","image/jpeg","not image"u8.ToArray()))Check(r.StatusCode==HttpStatusCode.BadRequest,"Fake JPG rejected over HTTP");
 using(var r=await Upload(writer,"/admin/documents/"+Id(article),"fake.pdf","application/pdf","broken pdf"u8.ToArray()))Check(r.StatusCode==HttpStatusCode.BadRequest,"Fake PDF rejected over HTTP");
 using(var r=await Upload(writer,"/admin/media/upload","csrf.png","image/png",Image(SKEncodedImageFormat.Png),false))Check(r.StatusCode==HttpStatusCode.BadRequest,"HTTP upload without CSRF rejected");
 using(var r=await Upload(outsider,"/admin/media/upload","denied.png","image/png",Image(SKEncodedImageFormat.Png)))Check(r.StatusCode==HttpStatusCode.Forbidden,"Unauthorized HTTP image upload rejected");
 using(var r=await Upload(outsider,"/admin/documents/"+Id(article),"denied.pdf","application/pdf",pdf.Build()))Check(r.StatusCode==HttpStatusCode.Forbidden||r.Headers.Location?.ToString().Contains("access-denied")==true,"Unauthorized HTTP document upload rejected");
 using(var r=await outsider.GetAsync("/admin/documents/file/"+documents.Keys.First()))Check(r.StatusCode==HttpStatusCode.NotFound,"Unauthorized private document read rejected");
 stage="http-workflow";article=await Act(writer,article,"Submit");Check(true,"HTTP Submit exact revision");article=await Act(reviewer,article,"Approve");Check(true,"HTTP Approve exact revision");
 using(var hidden=await guest.GetAsync("/bai-viet/"+Id(article)))Check(hidden.StatusCode==HttpStatusCode.NotFound,"Approved article hidden before publication");
 var deniedFields=new Dictionary<string,string>{{"Action","PublishApproved"},{"RowVersion",article.GetProperty("rowVersion").GetString()!},{"RevisionId",Rev(article).ToString()},{"RequestId",Guid.NewGuid().ToString()},{"__RequestVerificationToken",await Token(writer)}};
 using(var r=await writer.PostAsync("/admin/workflow/"+Id(article),new FormUrlEncodedContent(deniedFields)))Check(r.StatusCode==HttpStatusCode.Forbidden,"Writer cannot publish approved article");
 article=await Act(publisher,article,"PublishApproved");Check(true,"HTTP Publisher publishes exact approved revision");
 using(var r=await guest.GetAsync("/bai-viet/"+Id(article)))Check(r.IsSuccessStatusCode&&(await r.Content.ReadAsStringAsync()).Contains("ISOLATED NOAV HTTP TEST"),"Public article HTTP 200");
 foreach(var pair in documents){using var r=await guest.GetAsync("/media/"+pair.Key+"?download=true");Check(r.IsSuccessStatusCode&&r.Content.Headers.ContentDisposition?.DispositionType=="attachment"&&r.Headers.GetValues("X-Content-Type-Options").Single()=="nosniff"&&(await r.Content.ReadAsByteArrayAsync()).SequenceEqual(pair.Value),"Published document attachment/nosniff/exact bytes status="+(int)r.StatusCode+" disposition="+r.Content.Headers.ContentDisposition?.DispositionType);}
 foreach(var id in assets.Except(documents.Keys)){using var r=await guest.GetAsync("/media/"+id);Check(r.IsSuccessStatusCode,"Published image HTTP 200");}
 await using(var db=new PortalDbContext(options)){var id=Id(article);var c=await db.Contents.SingleAsync(x=>x.Id==id);Check(c.IsPublic&&c.PublishedRevisionId==Rev(article)&&await db.MediaAssets.CountAsync()==5&&await db.MediaAssets.AllAsync(x=>x.ScanStatus=="NotScanned"),"SQL published revision and exactly five NotScanned assets");Check(await db.Users.CountAsync()==4&&await db.SourceRecords.CountAsync()==0,"Only synthetic users/content; no source imports");}
 stage="COMPLETE";
}catch(Exception ex){Console.WriteLine("BLOCKED "+stage+" "+ex.GetType().Name+(ex is SqlException s?" SQL="+s.Number:""));await File.WriteAllTextAsync(Path.Combine(run,"failure.txt"),ex.ToString());Environment.ExitCode=1;}
finally{
 if(web!=null){if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(run,"host.log"),(stdout==null?"":await stdout)+(stderr==null?"":await stderr));web.Dispose();}
 await File.WriteAllTextAsync(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{stage,checks,passed=checks.Count,server=cs.DataSource,database=cs.InitialCatalog,operationalSqlTouched=false,accounts="synthetic only",smtp="Disabled",fullRegression=false},new JsonSerializerOptions{WriteIndented=true}));
 Console.WriteLine("RESULT "+stage+" checks="+checks.Count+" evidence="+run);
}
