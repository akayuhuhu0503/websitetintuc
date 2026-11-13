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

if(args.Length!=5 || !Regex.IsMatch(args[0],"^LPCms_[a-f0-9]{12}$") || args[4] is not ("All" or "VideoAndScreens" or "Screens" or "WeeklyOnly" or "Followup" or "VideoFollowup" or "ScheduleOnly"))throw new ArgumentException("New isolated LocalDB instance/run/host and explicit focus required");
var root=Directory.GetCurrentDirectory();var run=Path.GetFullPath(args[1]);var host=Path.GetFullPath(args[2]);
if(!run.StartsWith(Path.Combine(root,".local","tests","cms-editor-"),StringComparison.OrdinalIgnoreCase)||!host.StartsWith(run+Path.DirectorySeparatorChar)||!File.Exists(host)||File.Exists(Path.Combine(run,"result.json")))throw new Exception("New exact test run required");
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
  db.Categories.AddRange(new Category{Id=1,Name="Isolated CMS test",Slug="isolated-cms-test"},new Category{Id=76,Name="Weekly fixture",Slug="weekly-fixture"});await db.SaveChangesAsync();
 }
 var services=new ServiceCollection();services.AddLogging();services.AddScoped(_=>new PortalDbContext(options));
 services.AddIdentityCore<PortalUser>(PortalIdentityPolicy.Configure).AddRoles<IdentityRole>().AddEntityFrameworkStores<PortalDbContext>();
 using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
 var accounts=new Dictionary<string,(PortalUser User,string Password)>();
 foreach(var entry in new[]{("writer",new[]{Permissions.Create,Permissions.ViewOwn,Permissions.EditOwn,Permissions.Submit,Permissions.ManageMedia}), ("reviewer",new[]{Permissions.Review}), ("publisher",new[]{Permissions.PublishApproved}), ("dual",new[]{Permissions.Create,Permissions.ViewAll,Permissions.EditAll,Permissions.ManageMedia,Permissions.Review,Permissions.PublishApproved,Permissions.Schedule,Permissions.ManageSite,Permissions.ViewSite,Permissions.PublishSite,Permissions.CommentRead,Permissions.CommentModerate,Permissions.InquiryList,Permissions.ManageVideos}), ("outsider",new[]{Permissions.ViewOwn})}){
  var user=new PortalUser{UserName="fixture-"+entry.Item1,Email=entry.Item1+"@example.invalid",DisplayName="Isolated "+entry.Item1,EmailConfirmed=true,LockoutEnabled=true};var password="Aa1!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
  Check((await users.CreateAsync(user,password)).Succeeded,"Identity fixture "+entry.Item1);
  Check((await users.AddClaimsAsync(user,entry.Item2.Select(p=>new Claim("permission",p)))).Succeeded,"Scoped claims "+entry.Item1);accounts[entry.Item1]=(user,password);
 }
 stage="start-real-http";
 var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var url="http://127.0.0.1:"+port;
 var start=new ProcessStartInfo(args[3]){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
 start.ArgumentList.Add(host);start.ArgumentList.Add("--contentRoot");start.ArgumentList.Add(Path.Combine(root,"src","Web"));
 // No inherited deployment configuration/credentials enter the isolated test host.
 foreach(var key in start.Environment.Keys.Where(k=>k.Contains("__")||k.StartsWith("LANDPORTAL_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("ASPNETCORE_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("DOTNET_ENVIRONMENT",StringComparison.OrdinalIgnoreCase)).ToArray())start.Environment.Remove(key);
 foreach(var pair in new Dictionary<string,string>{{"ConnectionStrings__Portal",cs.ConnectionString},{"Sql__ExpectedDatabase","LandPortal_Test"},{"Runtime__RolePassword",roleSecret},{"Public__BaseUrl",url},{"Public__CanonicalHost","127.0.0.1"},{"DOTNET_ENVIRONMENT","Development"},{"ASPNETCORE_ENVIRONMENT","Development"},{"Mail__Transport","Disabled"},{"Deployment__Profile",""},{"Scanner__Provider","Disabled"},{"WorkspaceRoot",root},{"ManagedHttpTestRoot",run},{"Logging__LogLevel__Default","Warning"}})start.Environment[pair.Key]=pair.Value;
 foreach(var pair in new[]{("MediaRoot","media"),("SnapshotRoot","snapshots"),("TempRoot","temp"),("KeysRoot","keys")}){var dir=Path.Combine(run,pair.Item2);Directory.CreateDirectory(dir);start.Environment["Storage__"+pair.Item1]=dir;}
 web=Process.Start(start)!;stdout=web.StandardOutput.ReadToEndAsync();stderr=web.StandardError.ReadToEndAsync();
 HttpClient Client()=>new(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new()}){BaseAddress=new(url),Timeout=TimeSpan.FromSeconds(25)};
 using var guest=Client();bool ready=false;for(int i=0;i<60;i++){if(web.HasExited)break;try{using var r=await guest.GetAsync("/ready");if(r.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(200);}
 Check(ready,"Real Kestrel HTTP and SQL runtime role ready without scanner");
 async Task<AngleSharp.Dom.IDocument> Page(HttpClient c,string path){using var r=await c.GetAsync(path);if(r.StatusCode!=HttpStatusCode.OK)throw new Exception("GET "+path+" status "+(int)r.StatusCode);return new HtmlParser().ParseDocument(await r.Content.ReadAsStringAsync());}
 var csrfTokens=new Dictionary<HttpClient,string>();
 async Task<string> Token(HttpClient c,string path="/admin"){if(!csrfTokens.TryGetValue(c,out var token)){token=(await Page(c,path)).QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;csrfTokens[c]=token;}return token;}
 async Task<HttpClient> Login(string who){var c=Client();var p=await Page(c,"/admin/login");var question=p.QuerySelector("#captcha-question")!.TextContent;var numbers=Regex.Matches(question,@"\d+").Select(m=>int.Parse(m.Value)).ToArray();if(numbers.Length!=2||!(question.Contains("cộng")||question.Contains("trừ")))throw new Exception("Unexpected CAPTCHA");
 using var r=await c.PostAsync("/admin/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",p.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!},{"CaptchaId",p.QuerySelector("input[name=CaptchaId]")!.GetAttribute("value")!},{"CaptchaAnswer",(question.Contains("cộng")?numbers[0]+numbers[1]:numbers[0]-numbers[1]).ToString()},{"UserName",accounts[who].User.UserName!},{"Password",accounts[who].Password}}));Check(r.StatusCode==HttpStatusCode.Redirect,"HTTP Identity/CAPTCHA login "+who);return c;}
 using var writer=await Login("writer");using var reviewer=await Login("reviewer");using var publisher=await Login("publisher");using var outsider=await Login("outsider");using var dual=await Login("dual");
 foreach(var client in new[]{writer,reviewer,publisher,outsider,dual})client.DefaultRequestHeaders.Add("X-Editor-Request","true");
 if(args[4] is "Followup" or "VideoFollowup" or "ScheduleOnly"){
  var followupStorage=new StorageOptions{MediaRoot=Path.Combine(run,"media"),TempRoot=Path.Combine(run,"temp"),SnapshotRoot=Path.Combine(run,"snapshots"),KeysRoot=Path.Combine(run,"keys")};
  if(args[4]!="ScheduleOnly"){stage="video-background-followup";await VideoBackgroundAttachmentChecks.RunAsync(options,followupStorage,Check);}
  if(args[4]!="VideoFollowup"){stage="schedule-followup";await ScheduleFollowupChecks.RunAsync(options,followupStorage,dual,writer,accounts["dual"].User.Id,accounts["reviewer"].User.Id,c=>Token(c),Check);}
  stage="COMPLETE";return;
 }
 async Task<JsonElement> Post(HttpClient c,string route,Dictionary<string,string> fields,string form="/admin"){
 fields["__RequestVerificationToken"]=await Token(c,form);using var r=await c.PostAsync(route,new FormUrlEncodedContent(fields));var body=await r.Content.ReadAsStringAsync();if(r.StatusCode!=HttpStatusCode.OK){await File.WriteAllTextAsync(Path.Combine(run,"last-response.txt"),body);throw new Exception("POST "+route+" status "+(int)r.StatusCode);}return JsonDocument.Parse(body).RootElement.Clone();}
 Guid Id(JsonElement x)=>x.GetProperty("contentId").GetGuid();Guid Rev(JsonElement x)=>x.GetProperty("currentRevisionId").GetGuid();
 async Task WeeklyListCheck(){
  await Post(writer,"/admin/save",new(){{"Kind","StaticPage"},{"Title","WEEKLY LIST FIXTURE"},{"CategoryId","76"},{"Metadata.WeeklyYear","2026"},{"Metadata.WeeklyWeek","46"},{"intent","Save"},{"format","json"},{"RequestId",Guid.NewGuid().ToString()}});
  using var page=await dual.GetAsync("/admin?kind=StaticPage&categoryId=76");var html=await page.Content.ReadAsStringAsync();var dom=new HtmlParser().ParseDocument(html);
  var item=dom.QuerySelector(".cms-week-summary");Check(page.IsSuccessStatusCode&&item!=null&&item.TextContent.Contains("Tuần 46/2026")&&item.TextContent.Contains("09/11/2026")&&item.TextContent.Contains("15/11/2026")&&item.TextContent.Contains("Chưa có phiên bản phát hành"),"Authorized weekly list SQL projection renders ISO range and real release state");
  await File.WriteAllTextAsync(Path.Combine(run,"weekly-list.html"),html);
 }
 if(args[4]=="WeeklyOnly"){stage="weekly-list";await WeeklyListCheck();stage="COMPLETE";return;}
 var assets=new List<Guid>();Guid? cover=null;
 async Task<JsonElement> Save(JsonElement? old=null){var f=new Dictionary<string,string>{{"Kind","Article"},{"Title","ISOLATED NOAV HTTP TEST"},{"Summary","Synthetic fixture only"},{"Author","Automated fixture"},{"BodyHtml","<p>Isolated upload workflow verification.</p>"},{"CategoryId","1"},{"RequestId",Guid.NewGuid().ToString()},{"intent","Save"},{"format","json"}};if(cover!=null)f["CoverAssetId"]=cover.ToString()!;if(assets.Count>=2)f["BodyHtml"]+=$"<p><img src='/media/{assets[1]}' alt='Fixture inline PNG'></p>";for(var i=2;i<assets.Count;i++){f[$"AttachmentIds[{i-2}]"]=assets[i].ToString();}if(old is {} s){f["Id"]=Id(s).ToString();f["RowVersion"]=s.GetProperty("rowVersion").GetString()!;}return await Post(writer,"/admin/save",f,old is {} v?"/admin/edit/"+Id(v):"/admin/create");}
 async Task<JsonElement> Act(HttpClient c,JsonElement s,string action)=>await Post(c,"/admin/workflow/"+Id(s),new(){{"Action",action},{"RowVersion",s.GetProperty("rowVersion").GetString()!},{"RevisionId",Rev(s).ToString()},{"RequestId",Guid.NewGuid().ToString()}});
 async Task<HttpResponseMessage> Upload(HttpClient c,string route,string filename,string mime,byte[] bytes,bool csrf=true){using var form=new MultipartFormDataContent();var file=new ByteArrayContent(bytes);file.Headers.ContentType=new(mime);form.Add(file,"file",filename);using var req=new HttpRequestMessage(HttpMethod.Post,route){Content=form};if(csrf)req.Headers.Add("X-CSRF-TOKEN",await Token(c));return await c.SendAsync(req);}
 byte[] Image(SKEncodedImageFormat f){using var bitmap=new SKBitmap(12,8);bitmap.Erase(SKColors.DarkRed);using var image=SKImage.FromBitmap(bitmap);using var bytes=image.Encode(f,90);return bytes.ToArray();}
 byte[] Office(bool word){using var mem=new MemoryStream();using(var zip=new ZipArchive(mem,ZipArchiveMode.Create,true)){void Entry(string n,string s){using var w=new StreamWriter(zip.CreateEntry(n).Open());w.Write(s);}var main=word?"word/document.xml":"xl/workbook.xml";var type=word?OfficeDocumentPolicy.Docx+".main+xml":OfficeDocumentPolicy.Xlsx+".main+xml";Entry("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/"+main+"\" ContentType=\""+type+"\"/>"+(word?"":"<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")+"</Types>");Entry("_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\""+main+"\"/></Relationships>");Entry(main,word?"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Fixture document</w:t></w:r></w:p></w:body></w:document>":"<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Fixture\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");if(!word){Entry("xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");Entry("xl/worksheets/sheet1.xml","<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Fixture</t></is></c></row></sheetData></worksheet>");}}return mem.ToArray();}
 if(args[4]=="All"){
 stage="cms-documents";
 var article=await Save();var cid=Id(article);
 Check(cid!=Guid.Empty,"Editor saves a private first draft over HTTP");
 using(var page=await writer.GetAsync("/admin/edit/"+cid)){var html=await page.Content.ReadAsStringAsync();Check(page.IsSuccessStatusCode&&html.Contains("editor-document-file")&&!html.Contains("href=\"/admin/media\""),"Editor renders inline documents without JSON navigation");await File.WriteAllTextAsync(Path.Combine(run,"editor.html"),html);}
 using(var readiness=await writer.GetAsync("/admin/editor-readiness/"+cid)){var j=JsonDocument.Parse(await readiness.Content.ReadAsStringAsync()).RootElement;Check(readiness.IsSuccessStatusCode&&!j.GetProperty("canPublish").GetBoolean(),"Readiness identifies missing cover; incomplete draft stays saved");}
 using(var r=await Upload(writer,"/admin/media/upload?contentId="+cid,"fixture.png","image/png",Image(SKEncodedImageFormat.Png))){var j=JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;cover=j.GetProperty("id").GetGuid();Check(r.IsSuccessStatusCode&&j.GetProperty("scanStatus").GetString()=="NotScanned","Inline PNG upload retains truthful status");}
 using var pdf=new PdfDocumentBuilder();pdf.AddPage(100,100);var pdfBytes=pdf.Build();var docs=new List<Guid>();
 foreach(var f in new[]{("pdf","application/pdf",pdfBytes),("docx",OfficeDocumentPolicy.Docx,Office(true)),("xlsx",OfficeDocumentPolicy.Xlsx,Office(false))}){
  using var response=await Upload(writer,"/admin/documents/"+cid+"?format=json","fixture."+f.Item1,f.Item2,f.Item3);
  var text=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new Exception("Document "+f.Item1+" status="+(int)response.StatusCode+" "+text);
  var j=JsonDocument.Parse(text).RootElement;var doc=j.GetProperty("id").GetGuid();docs.Add(doc);
  Check(j.GetProperty("usable").GetBoolean()&&j.GetProperty("scanStatus").GetString()=="NotScanned","Editor JSON "+f.Item1+" ready under explicit test policy, never Clean");
  using var repeat=await Upload(writer,"/admin/documents/"+cid+"?format=json","fixture."+f.Item1,f.Item2,f.Item3);
  Check(repeat.IsSuccessStatusCode&&JsonDocument.Parse(await repeat.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid()==doc,"Retry "+f.Item1+" reuses only same article same owner document");
 }
 using(var r=await writer.GetAsync("/admin/documents/"+cid+"/items")){var j=JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;Check(r.IsSuccessStatusCode&&j.GetProperty("items").GetArrayLength()==3,"Same-article document library exposes three authorized documents");}
 using(var r=await outsider.GetAsync("/admin/documents/"+cid+"/items"))Check(!r.IsSuccessStatusCode,"Other actor cannot list private article documents");
 using(var r=await Upload(writer,"/admin/documents/"+cid+"?format=json","fake.pdf","application/pdf","invalid"u8.ToArray()))Check(r.StatusCode==HttpStatusCode.BadRequest,"Inline fake PDF rejected");
 using(var r=await Upload(writer,"/admin/documents/"+cid+"?format=json","csrf.pdf","application/pdf",pdf.Build(),false))Check(r.StatusCode==HttpStatusCode.BadRequest,"Inline upload requires CSRF");
 async Task<JsonElement> SaveContent(JsonElement? old,string kind="Article",string title="CMS EDITOR FIXTURE",Dictionary<string,string>? extra=null){
  var fields=new Dictionary<string,string>{{"Kind",kind},{"Title",title},{"Summary","Synthetic fixture"},{"Author","Fixture"},{"BodyHtml","<p>Test content</p>"},{"CategoryId","1"},{"RequestId",Guid.NewGuid().ToString()},{"intent","Save"},{"format","json"}};
  if(kind=="Article")fields["CoverAssetId"]=cover.ToString()!;
  if(old is {} o){fields["Id"]=Id(o).ToString();fields["RowVersion"]=o.GetProperty("rowVersion").GetString()!;}
  if(extra!=null)foreach(var p in extra)fields[p.Key]=p.Value;
  return await Post(writer,"/admin/save",fields,old is {} v?"/admin/edit/"+Id(v):"/admin/create?kind="+kind);
 }
 article=await SaveContent(article,extra:docs.Select((x,i)=>(x,i)).ToDictionary(x=>"AttachmentIds["+x.i+"]",x=>x.x.ToString()));
 await using(var db=new PortalDbContext(options))Check(await db.DraftAssets.CountAsync(x=>x.ContentId==cid)==4&&await db.MediaAssets.CountAsync()==4,"SQL draft references PNG and three documents with no duplicate assets");
 using(var r=await writer.GetAsync("/admin/editor-readiness/"+cid))Check(JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("canSubmit").GetBoolean(),"Readiness clears after valid attachments");
 var other=await SaveContent(null,title:"OTHER PRIVATE FIXTURE");
 using(var r=await Upload(writer,"/admin/documents/"+Id(other)+"?format=json","same.pdf","application/pdf",pdfBytes))Check(r.StatusCode==HttpStatusCode.BadRequest,"Hash match on another article cannot grant or transfer attachment ownership");
 var announcement=await SaveContent(null,"Announcement","PDF ONLY FIXTURE",new(){{"Summary",""},{"Author",""},{"BodyHtml",""}});
 using(var otherPdf=new PdfDocumentBuilder()){
  otherPdf.AddPage(160,120);
  using var r=await Upload(writer,"/admin/documents/"+Id(announcement)+"?format=json","announcement.pdf","application/pdf",otherPdf.Build());
  var pdfId=JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
  announcement=await SaveContent(announcement,"Announcement","PDF ONLY FIXTURE",new(){{"Summary",""},{"Author",""},{"BodyHtml",""},{"AttachmentIds[0]",pdfId.ToString()}});
  using var announcementReady=await writer.GetAsync("/admin/editor-readiness/"+Id(announcement));
  Check(announcementReady.IsSuccessStatusCode&&JsonDocument.Parse(await announcementReady.Content.ReadAsStringAsync()).RootElement.GetProperty("canSubmit").GetBoolean(),"PDF-only announcement readiness does not invent body/cover/author requirements");
 }
 var weekly=await SaveContent(null,"StaticPage","WEEKLY FIXTURE",new(){{"CategoryId","76"},{"Metadata.WeeklyYear","2026"},{"Metadata.WeeklyWeek","46"}});
 using(var page=await dual.GetAsync("/admin/create?kind=StaticPage&template=weekly")){Check(page.IsSuccessStatusCode,"Real weekly editor renders");await File.WriteAllTextAsync(Path.Combine(run,"weekly.html"),await page.Content.ReadAsStringAsync());}
 byte[] WeeklyOffice(){using var stream=new MemoryStream();stream.Write(Office(true));stream.Position=0;using(var zip=new ZipArchive(stream,ZipArchiveMode.Update,true)){
  zip.GetEntry("word/document.xml")!.Delete();using var w=new StreamWriter(zip.CreateEntry("word/document.xml").Open());
  string Cell(string text)=>"<w:tc><w:p><w:r><w:t>"+text+"</w:t></w:r></w:p></w:tc>";
  w.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:tbl><w:tr>"+string.Concat(new[]{"Ngày","Buổi","Giờ","Nội dung","Chủ trì","Thành phần","Địa điểm","Ghi chú"}.Select(Cell))+"</w:tr><w:tr>"+string.Concat(new[]{"09/11/2026","Sáng","08:00","Synthetic schedule","QA","Fixture","Local","Not official"}.Select(Cell))+"</w:tr></w:tbl></w:body></w:document>");
 }return stream.ToArray();}
 using(var r=await Upload(writer,"/admin/documents/"+Id(weekly)+"?format=json","weekly.docx",OfficeDocumentPolicy.Docx,WeeklyOffice())){
  var asset=JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
  var preview=await Post(writer,"/admin/editor-weekly/"+Id(weekly)+"/"+asset,new());
  Check(preview.GetProperty("rows").GetArrayLength()==1&&preview.GetProperty("rows")[0].GetProperty("date").GetString()=="2026-11-09","Inline weekly Word preview parses current week without saving or publishing");
  await using var db=new PortalDbContext(options);var weeklyId=Id(weekly);Check(await db.Revisions.CountAsync(x=>x.ContentId==weeklyId)==1,"Word preview did not create a revision before editor save");
 }
 stage="cms-combined-workflow";
 article=await Act(writer,article,"Submit");
 Dictionary<string,string> Combined(JsonElement state,Guid request)=>new(){{"RowVersion",state.GetProperty("rowVersion").GetString()!},{"RevisionId",Rev(state).ToString()},{"RequestId",request.ToString()}};
 var command=Guid.NewGuid();var action=Combined(article,command);
 action["__RequestVerificationToken"]=await Token(reviewer);
 using(var r=await reviewer.PostAsync("/admin/approve-publish/"+cid,new FormUrlEncodedContent(action)))Check(r.StatusCode==HttpStatusCode.Forbidden,"Review-only account cannot combine publish");
 var stale=Combined(article,Guid.NewGuid());stale["RowVersion"]=Convert.ToBase64String(new byte[8]);stale["__RequestVerificationToken"]=await Token(dual);
 using(var r=await dual.PostAsync("/admin/approve-publish/"+cid,new FormUrlEncodedContent(stale)))Check(r.StatusCode==HttpStatusCode.Conflict,"Combined approve-publish rejects stale rowversion");
 var published=await Post(dual,"/admin/approve-publish/"+cid,Combined(article,command));
 Check(published.GetProperty("isPublic").GetBoolean()&&Rev(published)==Rev(article),"Dual-right actor approves and publishes exact submitted revision");
 var replay=await Post(dual,"/admin/approve-publish/"+cid,Combined(article,command));
 Check(replay.GetProperty("isPublic").GetBoolean(),"Lost response retry acknowledges existing combined commands");
 var altered=Combined(article,command);altered["RevisionId"]=Rev(other).ToString();altered["__RequestVerificationToken"]=await Token(dual);
 using(var r=await dual.PostAsync("/admin/approve-publish/"+cid,new FormUrlEncodedContent(altered)))Check(r.StatusCode==HttpStatusCode.BadRequest,"Combined retry cannot substitute another revision");
 var editing=await Act(writer,published,"StartDraft");
 var afterEdit=await Post(dual,"/admin/approve-publish/"+cid,Combined(article,command));
 Check(afterEdit.GetProperty("state").GetString()=="Draft"&&afterEdit.GetProperty("isPublic").GetBoolean()&&Rev(afterEdit)==Rev(editing),"Combined retry after new draft returns current state without publishing it");
 await using(var db=new PortalDbContext(options)){Check(await db.AuditLogs.CountAsync(x=>x.ContentId==cid&&x.Action=="Approve")==1&&await db.AuditLogs.CountAsync(x=>x.ContentId==cid&&x.Action=="PublishApproved")==1,"Combined action retains separate audit records once only");}
 using(var r=await guest.GetAsync("/bai-viet/"+cid))Check(r.IsSuccessStatusCode,"Combined-published article opens publicly");
 foreach(var doc in docs){using var r=await guest.GetAsync("/media/"+doc);Check(r.IsSuccessStatusCode&&r.Content.Headers.ContentDisposition?.DispositionType=="attachment","Published attached document download "+doc.ToString("N")[..6]);}
 stage="cms-schedule";
 var scheduled=await SaveContent(null,title:"SCHEDULE FIXTURE");scheduled=await Act(writer,scheduled,"Submit");scheduled=await Act(reviewer,scheduled,"Approve");
 var due=DateTime.UtcNow.AddHours(9).ToString("yyyy-MM-ddTHH:mm");
 Dictionary<string,string> Schedule(JsonElement s)=>new(){{"ContentId",Id(s).ToString()},{"RevisionId",Rev(s).ToString()},{"ContentVersion",s.GetProperty("rowVersion").GetString()!},{"DueVietnam",due},{"Direct","false"},{"EditorMode","approved"}};
 var job=await Post(dual,"/admin/editor-schedule/"+Id(scheduled),Schedule(scheduled));
 var jobAgain=await Post(dual,"/admin/editor-schedule/"+Id(scheduled),Schedule(scheduled));
 Check(job.GetProperty("jobId").GetGuid()==jobAgain.GetProperty("jobId").GetGuid(),"Editor schedule retries one exact approved-revision job");
 using(var r=await guest.GetAsync("/bai-viet/"+Id(scheduled)))Check(r.StatusCode==HttpStatusCode.NotFound,"Future schedule remains private before due time");
 var denySchedule=Schedule(scheduled);denySchedule["__RequestVerificationToken"]=await Token(writer);
 using(var r=await writer.PostAsync("/admin/editor-schedule/"+Id(scheduled),new FormUrlEncodedContent(denySchedule)))Check(r.StatusCode==HttpStatusCode.Forbidden,"Writer cannot schedule without publish/schedule rights");
 }
 // Additional controller/SQL cases supplied by the video task use fake fixtures only.
 var storage=new StorageOptions{MediaRoot=Path.Combine(run,"media"),TempRoot=Path.Combine(run,"temp"),SnapshotRoot=Path.Combine(run,"snapshots"),KeysRoot=Path.Combine(run,"keys")};
 stage="video-editor-scope";
 if(args[4]!="Screens")await VideoEditorChecks.RunAsync(options,storage,Check);
 // Initialize only this new fixture DB through the existing official configuration service.
 await using(var db=new PortalDbContext(options))await new SiteConfigurationService(db,storage,new MediaUploadOptions()).InitializeAsync(accounts["dual"].User.Id);
 stage="cms-render-scope";
 await WeeklyListCheck();
 var emptyVideo=await Post(dual,"/admin/save",new(){{"Kind","Video"},{"Title","EMPTY VIDEO FIXTURE"},{"BodyHtml","<p>Fixture</p>"},{"CategoryId","1"},{"intent","Save"},{"format","json"},{"RequestId",Guid.NewGuid().ToString()}});
 using(var r=await dual.GetAsync("/admin/editor-readiness/"+Id(emptyVideo)))Check(r.IsSuccessStatusCode&&!JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("canPublish").GetBoolean(),"Video without file reports missing upload before publication");
 var renderedArticle=await Post(writer,"/admin/save",new(){{"Kind","Article"},{"Title","RENDER FIXTURE"},{"CategoryId","1"},{"intent","Save"},{"format","json"},{"RequestId",Guid.NewGuid().ToString()}});
 using(var page=await writer.GetAsync("/admin/edit/"+Id(renderedArticle)+"?returnUrl="+Uri.EscapeDataString("/admin?status=Draft&q=fixture"))){var html=await page.Content.ReadAsStringAsync();var parsed=new HtmlParser().ParseDocument(html);Check(page.IsSuccessStatusCode&&parsed.QuerySelector(".back-link")?.GetAttribute("href")=="/admin?status=Draft&q=fixture","Editor preserves authorized list filter in back link");await File.WriteAllTextAsync(Path.Combine(run,"editor.html"),html);}
 using(var page=await writer.GetAsync("/admin/edit/"+Id(renderedArticle)+"?returnUrl="+Uri.EscapeDataString("https://outside.invalid"))){var parsed=new HtmlParser().ParseDocument(await page.Content.ReadAsStringAsync());Check(parsed.QuerySelector(".back-link")?.GetAttribute("href")=="/admin","External return URL rejected");}
 using(var pdf=new PdfDocumentBuilder()){
  pdf.AddPage(105,105);using var upload=await Upload(writer,"/admin/documents/"+Id(renderedArticle)+"?format=json","render.pdf","application/pdf",pdf.Build());
  var doc=JsonDocument.Parse(await upload.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
  renderedArticle=await Post(writer,"/admin/save",new(){{"Id",Id(renderedArticle).ToString()},{"RowVersion",renderedArticle.GetProperty("rowVersion").GetString()!},{"Kind","Article"},{"Title","RENDER FIXTURE"},{"CategoryId","1"},{"AttachmentIds[0]",doc.ToString()},{"intent","Save"},{"format","json"},{"RequestId",Guid.NewGuid().ToString()}});
  using var page=await writer.GetAsync("/admin/edit/"+Id(renderedArticle));var parsed=new HtmlParser().ParseDocument(await page.Content.ReadAsStringAsync());var row=parsed.QuerySelector("#selected-attachments li")!;
  Check(row.TextContent.Contains("PDF")&&row.TextContent.Contains("KB")&&row.QuerySelector("a")!=null,"Reloaded attachment shows type, size and download action");
  using var file=await writer.GetAsync(row.QuerySelector("a")!.GetAttribute("href"));Check(file.StatusCode==HttpStatusCode.Redirect&&file.Headers.Location!.ToString().Contains("/admin/documents/file/"),"Reloaded document link uses authorized private download route");
 }
 foreach(var path in new[]{"/admin?status=Published","/admin?status=PendingReview","/admin?kind=StaticPage&categoryId=76","/admin/site/picker?type=image&q=&page=1","/admin/site/picker?type=content&q=&page=1","/admin/site?section=banners","/admin/site/edit?section=banners","/admin/tuong-tac/articles?q=&page=1","/admin/create?kind=StaticPage&template=weekly"}){
 using var r=await dual.GetAsync(path);if(!r.IsSuccessStatusCode)await File.WriteAllTextAsync(Path.Combine(run,"last-response.txt"),await r.Content.ReadAsStringAsync());Check(r.IsSuccessStatusCode,"CMS rendering/query "+path);
 if(path.Contains("template=weekly"))await File.WriteAllTextAsync(Path.Combine(run,"weekly.html"),await r.Content.ReadAsStringAsync());
 }
 using(var r=await writer.GetAsync("/admin/site/picker?type=image&q=&page=1"))Check(!r.IsSuccessStatusCode,"Site picker denies actor without site management");
 stage="COMPLETE";
}catch(Exception ex){Console.WriteLine("BLOCKED "+stage+" "+ex.GetType().Name+(ex is SqlException s?" SQL="+s.Number:""));await File.WriteAllTextAsync(Path.Combine(run,"failure.txt"),ex.ToString());Environment.ExitCode=1;}
finally{
 if(web!=null){if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(run,"host.log"),(stdout==null?"":await stdout)+(stderr==null?"":await stderr));web.Dispose();}
 await File.WriteAllTextAsync(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{stage,focus=args[4],checks,passed=checks.Count,server=cs.DataSource,database=cs.InitialCatalog,operationalSqlTouched=false,accounts="synthetic only",smtp="Disabled",fullRegression=false},new JsonSerializerOptions{WriteIndented=true}));
 Console.WriteLine("RESULT "+stage+" checks="+checks.Count+" evidence="+run);
}
