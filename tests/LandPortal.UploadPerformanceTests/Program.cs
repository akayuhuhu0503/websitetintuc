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

if(args.Length==2 && args[0]=="--video-health") { await VideoHealthMeasurement.Run(args[1]); return; }
if(args.Length!=6 || !Regex.IsMatch(args[0],"^LPPerf_[a-f0-9]{12}$"))throw new ArgumentException("New isolated LocalDB instance/run/host required");
var root=Directory.GetCurrentDirectory();var run=Path.GetFullPath(args[1]);var host=Path.GetFullPath(args[2]);
if(!run.StartsWith(Path.Combine(root,".local","tests","perf-upload-"),StringComparison.OrdinalIgnoreCase)||!host.StartsWith(run+Path.DirectorySeparatorChar)||!File.Exists(host)||File.Exists(Path.Combine(run,"result.json")))throw new Exception("New exact test run required");
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
 }
 var services=new ServiceCollection();services.AddLogging();services.AddScoped(_=>new PortalDbContext(options));
 services.AddIdentityCore<PortalUser>(PortalIdentityPolicy.Configure).AddRoles<IdentityRole>().AddEntityFrameworkStores<PortalDbContext>();
 using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
 var accounts=new Dictionary<string,(PortalUser User,string Password)>();
 foreach(var entry in new[]{("writer",new[]{Permissions.Create,Permissions.ViewOwn,Permissions.EditOwn,Permissions.Submit,Permissions.ManageMedia,Permissions.ManageVideos}), ("reviewer",new[]{Permissions.Review}), ("publisher",new[]{Permissions.PublishApproved,Permissions.Schedule,Permissions.ViewAll}), ("outsider",new[]{Permissions.ViewOwn})}){
  var user=new PortalUser{UserName="fixture-"+entry.Item1,Email=entry.Item1+"@example.invalid",DisplayName="Isolated "+entry.Item1,EmailConfirmed=true,LockoutEnabled=true};var password="Aa1!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
  Check((await users.CreateAsync(user,password)).Succeeded,"Identity fixture "+entry.Item1);
  Check((await users.AddClaimsAsync(user,entry.Item2.Select(p=>new Claim("permission",p)))).Succeeded,"Scoped claims "+entry.Item1);accounts[entry.Item1]=(user,password);
 }
 if(args[4] is "Ux" or "UxHome"){
 await users.AddClaimsAsync(accounts["writer"].User,new[]{Permissions.ViewAll,Permissions.EditAll,Permissions.Review,Permissions.ManageSettings,Permissions.ManageSite,Permissions.ViewSite}.Select(p=>new Claim("permission",p)));
 await UxMeasurement.Seed(options,accounts["writer"].User.Id);
 }
 stage="start-real-http";
 var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var url="http://127.0.0.1:"+port;
 var start=new ProcessStartInfo(args[3]){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
 start.ArgumentList.Add(host);start.ArgumentList.Add("--contentRoot");start.ArgumentList.Add(Path.Combine(root,"src","Web"));
 // No inherited deployment configuration/credentials enter the isolated test host.
 foreach(var key in start.Environment.Keys.Where(k=>k.Contains("__")||k.StartsWith("LANDPORTAL_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("ASPNETCORE_",StringComparison.OrdinalIgnoreCase)||k.StartsWith("DOTNET_ENVIRONMENT",StringComparison.OrdinalIgnoreCase)).ToArray())start.Environment.Remove(key);
 foreach(var pair in new Dictionary<string,string>{{"ConnectionStrings__Portal",cs.ConnectionString},{"Sql__ExpectedDatabase","LandPortal_Test"},{"Runtime__RolePassword",roleSecret},{"Public__BaseUrl",url},{"Public__CanonicalHost","127.0.0.1"},{"DOTNET_ENVIRONMENT","Development"},{"ASPNETCORE_ENVIRONMENT","Development"},{"Mail__Transport","Disabled"},{"Deployment__Profile",""},{"Scanner__Provider","Disabled"},{"WorkspaceRoot",root},{"ManagedHttpTestRoot",run},{"Logging__LogLevel__Default","Warning"}})start.Environment[pair.Key]=pair.Value;
 foreach(var pair in new[]{("MediaRoot","media"),("SnapshotRoot","snapshots"),("TempRoot","temp"),("KeysRoot","keys")}){var dir=Path.Combine(run,pair.Item2);Directory.CreateDirectory(dir);start.Environment["Storage__"+pair.Item1]=dir;}
 if(args[4]=="Video"){
 var toolConfig=JsonDocument.Parse(await File.ReadAllTextAsync(args[5])).RootElement;start.Environment["PerfVideo"]="true";
 // A real unsupported "Disabled" provider deliberately throws before the video loop.
 // Use a valid but unavailable loopback scanner, as the approved no-AV policy expects.
 start.Environment["Scanner__Provider"]="Clamd";start.Environment["Scanner__ClamdHost"]="127.0.0.1";start.Environment["Scanner__ClamdPort"]="65534";
 foreach(var key in new[]{"FfmpegPath","FfprobePath","FfmpegSha256","FfprobeSha256"})start.Environment["Video__"+key]=toolConfig.GetProperty(char.ToLowerInvariant(key[0])+key[1..]).GetString()!;
}
web=Process.Start(start)!;stdout=web.StandardOutput.ReadToEndAsync();stderr=web.StandardError.ReadToEndAsync();
 HttpClient Client()=>new(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new()}){BaseAddress=new(url),Timeout=TimeSpan.FromSeconds(25)};
 using var guest=Client();bool ready=false;for(int i=0;i<60;i++){if(web.HasExited)break;try{using var r=await guest.GetAsync("/ready");if(r.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(200);}
 Check(ready,"Real Kestrel HTTP and SQL runtime role ready without scanner");
 async Task<AngleSharp.Dom.IDocument> Page(HttpClient c,string path){using var r=await c.GetAsync(path);if(r.StatusCode!=HttpStatusCode.OK)throw new Exception("GET "+path+" status "+(int)r.StatusCode);return new HtmlParser().ParseDocument(await r.Content.ReadAsStringAsync());}
 var tokens=new Dictionary<HttpClient,string>(); async Task<string> Token(HttpClient c,string path="/admin"){if(!tokens.TryGetValue(c,out var t)){t=(await Page(c,path)).QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;tokens[c]=t;}return t;}
 async Task<HttpClient> Login(string who){var c=Client();var p=await Page(c,"/admin/login");var question=p.QuerySelector("#captcha-question")!.TextContent;var numbers=Regex.Matches(question,@"\d+").Select(m=>int.Parse(m.Value)).ToArray();if(numbers.Length!=2||!(question.Contains("cộng")||question.Contains("trừ")))throw new Exception("Unexpected CAPTCHA");
 using var r=await c.PostAsync("/admin/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",p.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!},{"CaptchaId",p.QuerySelector("input[name=CaptchaId]")!.GetAttribute("value")!},{"CaptchaAnswer",(question.Contains("cộng")?numbers[0]+numbers[1]:numbers[0]-numbers[1]).ToString()},{"UserName",accounts[who].User.UserName!},{"Password",accounts[who].Password}}));Check(r.StatusCode==HttpStatusCode.Redirect,"HTTP Identity/CAPTCHA login "+who);return c;}
 using var writer=await Login("writer");using var reviewer=await Login("reviewer");using var publisher=await Login("publisher");using var outsider=await Login("outsider");
 async Task<JsonElement> Post(HttpClient c,string route,Dictionary<string,string> fields,string form="/admin"){
 fields["__RequestVerificationToken"]=await Token(c,form);using var r=await c.PostAsync(route,new FormUrlEncodedContent(fields));var body=await r.Content.ReadAsStringAsync();if(r.StatusCode!=HttpStatusCode.OK){await File.WriteAllTextAsync(Path.Combine(run,"last-response.txt"),body);throw new Exception("POST "+route+" status "+(int)r.StatusCode);}return JsonDocument.Parse(body).RootElement.Clone();}
 Guid Id(JsonElement x)=>x.GetProperty("contentId").GetGuid();Guid Rev(JsonElement x)=>x.GetProperty("currentRevisionId").GetGuid();
 var assets=new List<Guid>();Guid? cover=null;
 async Task<JsonElement> Save(JsonElement? old=null){var f=new Dictionary<string,string>{{"Kind","Article"},{"Title","ISOLATED NOAV HTTP TEST"},{"Summary","Synthetic fixture only"},{"Author","Automated fixture"},{"BodyHtml","<p>Isolated upload workflow verification.</p>"},{"CategoryId","1"},{"RequestId",Guid.NewGuid().ToString()},{"intent","Save"},{"format","json"}};if(cover!=null)f["CoverAssetId"]=cover.ToString()!;if(assets.Count>=2)f["BodyHtml"]+=$"<p><img src='/media/{assets[1]}' alt='Fixture inline PNG'></p>";for(var i=2;i<assets.Count;i++){f[$"AttachmentIds[{i-2}]"]=assets[i].ToString();}if(old is {} s){f["Id"]=Id(s).ToString();f["RowVersion"]=s.GetProperty("rowVersion").GetString()!;}return await Post(writer,"/admin/save",f,old is {} v?"/admin/edit/"+Id(v):"/admin/create");}
 async Task<JsonElement> Act(HttpClient c,JsonElement s,string action)=>await Post(c,"/admin/workflow/"+Id(s),new(){{"Action",action},{"RowVersion",s.GetProperty("rowVersion").GetString()!},{"RevisionId",Rev(s).ToString()},{"RequestId",Guid.NewGuid().ToString()}});
 async Task<HttpResponseMessage> Upload(HttpClient c,string route,string filename,string mime,byte[] bytes,bool csrf=true){using var form=new MultipartFormDataContent();var file=new ByteArrayContent(bytes);file.Headers.ContentType=new(mime);form.Add(file,"file",filename);using var req=new HttpRequestMessage(HttpMethod.Post,route){Content=form};if(csrf)req.Headers.Add("X-CSRF-TOKEN",await Token(c));return await c.SendAsync(req);}
 byte[] Image(SKEncodedImageFormat f){using var bitmap=new SKBitmap(12,8);bitmap.Erase(SKColors.DarkRed);using var image=SKImage.FromBitmap(bitmap);using var bytes=image.Encode(f,90);return bytes.ToArray();}
 byte[] Office(bool word){using var mem=new MemoryStream();using(var zip=new ZipArchive(mem,ZipArchiveMode.Create,true)){void Entry(string n,string s){using var w=new StreamWriter(zip.CreateEntry(n).Open());w.Write(s);}var main=word?"word/document.xml":"xl/workbook.xml";var type=word?OfficeDocumentPolicy.Docx+".main+xml":OfficeDocumentPolicy.Xlsx+".main+xml";Entry("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/"+main+"\" ContentType=\""+type+"\"/>"+(word?"":"<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")+"</Types>");Entry("_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\""+main+"\"/></Relationships>");Entry(main,word?"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Fixture document</w:t></w:r></w:p></w:body></w:document>":"<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Fixture\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");if(!word){Entry("xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");Entry("xl/worksheets/sheet1.xml","<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Fixture</t></is></c></row></sheetData></worksheet>");}}return mem.ToArray();}
if(args[4]=="UxHome"){ await UxMeasurement.ReadPages(guest,run,Check,homeOnly:true); if(args[5]!="baseline")await UxCorrectness.Run(options,guest,run,Check); stage="COMPLETE";return; }
if(args[4]=="Ux"){
 await UxMeasurement.ReadPages(writer,run,Check,guest:guest);
 var draft=await Save();var uxCid=Id(draft);
 for(int i=0;i<4;i++){await Page(writer,"/admin/edit/"+uxCid);draft=await Save(draft);}
 using(var image=await Upload(writer,"/admin/media/upload?contentId="+uxCid,"fixture.png","image/png",Image(SKEncodedImageFormat.Png))){Check(image.IsSuccessStatusCode,"UX PNG upload");cover=JsonDocument.Parse(await image.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();}
 var stale=draft;draft=await Save(draft);
 try{await Save(stale);throw new Exception("Stale rowversion accepted");}catch(Exception e)when(e.Message.Contains("status 409")){Check(true,"Stale editor rejected");}
 draft=await Act(writer,draft,"Submit");draft=await Act(reviewer,draft,"Approve");draft=await Act(publisher,draft,"PublishApproved");
 Check((await Page(guest,"/bai-viet/"+uxCid)).QuerySelector("h1")!=null,"HTTP submit approve publish and public page");
 await PermissionChecks.Run(options,Check);
 if(args[5]!="baseline")await UxCorrectness.Run(options,guest,run,Check);
 stage="COMPLETE";return;
}
if(args[4]=="Video"){await VideoMeasurement.Run(options,writer,args[5],run,c=>Token(c),Check);stage="COMPLETE";return;}
 byte[] LargePdf(){
  using var m=new MemoryStream();void Raw(string text){m.Write(Encoding.ASCII.GetBytes(text));}var offsets=new List<long>{0};
  Raw("%PDF-1.7\n");void Obj(string text){offsets.Add(m.Position);Raw(offsets.Count-1+" 0 obj\n"+text+"\nendobj\n");}
  Obj("<< /Type /Catalog /Pages 2 0 R >>");Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");Obj("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /XObject << /Im1 4 0 R >> >> /Contents 5 0 R >>");
  offsets.Add(m.Position);Raw("4 0 obj\n<< /Type /XObject /Subtype /Image /Width 1024 /Height 3072 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 9437184 >>\nstream\n");m.Write(RandomNumberGenerator.GetBytes(9437184));Raw("\nendstream\nendobj\n");
  Obj("<< /Length 31 >>\nstream\nq 100 0 0 100 0 0 cm /Im1 Do Q\n\nendstream");var xref=m.Position;Raw("xref\n0 6\n0000000000 65535 f \n");foreach(var offset in offsets.Skip(1))Raw(offset.ToString("D10")+" 00000 n \n");Raw("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n"+xref+"\n%%EOF\n");return m.ToArray();
 }
 byte[] LargeOffice(bool word){using var m=new MemoryStream();m.Write(Office(word));using(var zip=new ZipArchive(m,ZipArchiveMode.Update,true)){
  // Real embedded raster images; each part remains below the existing 2MiB ZIP-entry cap.
  for(int i=0;i<12;i++){using var bitmap=new SKBitmap(768,768);var pixels=new byte[bitmap.ByteCount];RandomNumberGenerator.Fill(pixels);System.Runtime.InteropServices.Marshal.Copy(pixels,0,bitmap.GetPixels(),pixels.Length);using var img=SKImage.FromBitmap(bitmap);using var jpg=img.Encode(SKEncodedImageFormat.Jpeg,100);var bytes=jpg.ToArray();using var entry=zip.CreateEntry((word?"word":"xl")+"/media/fixture-"+i+".jpg",CompressionLevel.NoCompression).Open();entry.Write(bytes);}
 }return m.ToArray();}
 byte[] Fixture(string name,Func<byte[]> create){var directory=Path.Combine(root,".local","perf-document-fixtures");Directory.CreateDirectory(directory);var file=Path.Combine(directory,name);if(!File.Exists(file))File.WriteAllBytes(file,create());return File.ReadAllBytes(file);}
 if(args[4]=="Near"){
 var draft=await Save();var target=Id(draft);
 foreach(var f in new[]{("large.pdf","application/pdf",Fixture("large.pdf",LargePdf)),("large.docx",OfficeDocumentPolicy.Docx,Fixture("large.docx",()=>LargeOffice(true))),("large.xlsx",OfficeDocumentPolicy.Xlsx,Fixture("large.xlsx",()=>LargeOffice(false)))}){
  using var response=await Upload(writer,"/admin/documents/"+target+"?format=json",f.Item1,f.Item2,f.Item3);if(!response.IsSuccessStatusCode)throw new Exception("Near-limit upload "+f.Item1+" status "+response.StatusCode);Check(true,"Near-limit "+f.Item1+" bytes="+f.Item3.Length+" sha="+Convert.ToHexStringLower(SHA256.HashData(f.Item3)));
 }stage="COMPLETE";return;}
 stage="performance";
 foreach(var route in new[]{"/admin","/admin?search=fixture","/admin/create","/admin/videos","/"})for(int repeat=0;repeat<3;repeat++){
  using var response=await writer.GetAsync(route);await response.Content.ReadAsByteArrayAsync();Check(response.IsSuccessStatusCode||route=="/admin/videos"&&(response.StatusCode==HttpStatusCode.Forbidden||response.StatusCode==HttpStatusCode.Redirect),"Measure GET "+route);
 }
 var article=await Save();var cid=Id(article);
 for(int repeat=0;repeat<3;repeat++){using var response=await writer.GetAsync("/admin/edit/"+cid);var html=await response.Content.ReadAsStringAsync();Check(response.IsSuccessStatusCode,"Measure editor HTTP");await File.WriteAllTextAsync(Path.Combine(run,"editor.html"),html);}
 for(int repeat=0;repeat<3;repeat++)article=await Save(article);
 foreach(var format in new[]{("jpg","image/jpeg",SKEncodedImageFormat.Jpeg),("png","image/png",SKEncodedImageFormat.Png)}){
  using var response=await Upload(writer,"/admin/media/upload?contentId="+cid,"fixture."+format.Item1,format.Item2,Image(format.Item3));
  var j=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;Check(response.IsSuccessStatusCode&&j.GetProperty("scanStatus").GetString()=="NotScanned","Measure "+format.Item1);var id=j.GetProperty("id").GetGuid();cover??=id;assets.Add(id);
 }
 using var pdf=new PdfDocumentBuilder();pdf.AddPage(100,100);
 var docs=new List<(Guid Id,string Sha,byte[] Bytes)>();
 foreach(var file in new[]{("small.pdf","application/pdf",pdf.Build()),("small.docx",OfficeDocumentPolicy.Docx,Office(true)),("small.xlsx",OfficeDocumentPolicy.Xlsx,Office(false)),("large.pdf","application/pdf",Fixture("large.pdf",LargePdf)),("large.docx",OfficeDocumentPolicy.Docx,Fixture("large.docx",()=>LargeOffice(true))),("large.xlsx",OfficeDocumentPolicy.Xlsx,Fixture("large.xlsx",()=>LargeOffice(false)))}){
  using var response=await Upload(writer,"/admin/documents/"+cid+"?format=json",file.Item1,file.Item2,file.Item3);var text=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode){await File.WriteAllTextAsync(Path.Combine(run,"failed-upload.txt"),text);throw new Exception("Upload "+file.Item1+" "+response.StatusCode);}
  var j=JsonDocument.Parse(text).RootElement;var id=j.GetProperty("id").GetGuid();docs.Add((id,Convert.ToHexStringLower(SHA256.HashData(file.Item3)),file.Item3));assets.Add(id);Check(j.GetProperty("scanStatus").GetString()=="NotScanned","Measure "+file.Item1+" bytes="+file.Item3.Length);
 }
 article=await Save(article);await using(var db=new PortalDbContext(options))Check(await db.DraftAssets.CountAsync(x=>x.ContentId==cid)==8,"All uploads attached in SQL");
 // Baseline repeated request demonstrates receive/hash/validate overhead despite no duplicate SQL row.
 using(var retry=await Upload(writer,"/admin/documents/"+cid+"?format=json","small.pdf","application/pdf",docs[0].Bytes)){var j=JsonDocument.Parse(await retry.Content.ReadAsStringAsync()).RootElement;Check(retry.IsSuccessStatusCode&&j.GetProperty("id").GetGuid()==docs[0].Id,"Duplicate upload reuses own exact asset");}
 using(var recovered=await writer.GetAsync("/admin/documents/"+cid+"/result?sha256="+docs[0].Sha)){
  if(recovered.IsSuccessStatusCode){var j=JsonDocument.Parse(await recovered.Content.ReadAsStringAsync()).RootElement;Check(j.GetProperty("id").GetGuid()==docs[0].Id,"Recovery matches persisted own asset");using var denied=await outsider.GetAsync("/admin/documents/"+cid+"/result?sha256="+docs[0].Sha);Check(denied.StatusCode==HttpStatusCode.NotFound,"Recovery is not cross-user oracle");
   using var wrong=await writer.GetAsync("/admin/documents/"+Guid.NewGuid()+"/result?sha256="+docs[0].Sha);Check(wrong.StatusCode==HttpStatusCode.NotFound,"Recovery denies wrong content");
   using var malformed=await writer.GetAsync("/admin/documents/"+cid+"/result?sha256=invalid");Check(malformed.StatusCode==HttpStatusCode.BadRequest,"Malformed recovery hash rejected");
   await using(var proofDb=new PortalDbContext(options)){var own=await proofDb.MediaAssets.SingleAsync(x=>x.Id==docs[0].Id);own.IsQuarantined=true;await proofDb.SaveChangesAsync();using var blocked=await writer.GetAsync("/admin/documents/"+cid+"/result?sha256="+docs[0].Sha);var state=JsonDocument.Parse(await blocked.Content.ReadAsStringAsync()).RootElement;Check(!state.GetProperty("usable").GetBoolean(),"Recovery never promotes quarantined asset");own.IsQuarantined=false;await proofDb.SaveChangesAsync();}
  }
  else Check(recovered.StatusCode==HttpStatusCode.NotFound,"Baseline has no recovery route");
 }
 using(var denied=await Upload(outsider,"/admin/documents/"+cid+"?format=json","denied.pdf","application/pdf",docs[0].Bytes))Check(denied.StatusCode==HttpStatusCode.Forbidden||denied.StatusCode==HttpStatusCode.Redirect,"Denied uploader remains denied");
 using(var fake=await Upload(writer,"/admin/documents/"+cid+"?format=json","fake.pdf","application/pdf","fake"u8.ToArray()))Check(fake.StatusCode==HttpStatusCode.BadRequest,"Invalid file remains rejected");

 var components=new List<object>();
 await using(var probeDb=new PortalDbContext(options)){
  var probeMedia=new MediaService(probeDb,new StorageOptions{MediaRoot=Path.Combine(run,"media"),TempRoot=Path.Combine(run,"temp"),KeysRoot=Path.Combine(run,"keys"),SnapshotRoot=Path.Combine(run,"snapshots")},new MediaUploadOptions{ApprovedManagedDocumentsWithoutAv=true});
  foreach(var doc in docs){var asset=await probeDb.MediaAssets.AsNoTracking().SingleAsync(x=>x.Id==doc.Id);var file=Path.Combine(run,"media",asset.StorageKey.Replace('/',Path.DirectorySeparatorChar));
   for(int iteration=0;iteration<3;iteration++){
    var watch=Stopwatch.StartNew();await using(var stream=File.OpenRead(file)){await stream.CopyToAsync(Stream.Null);}var readMs=watch.Elapsed.TotalMilliseconds;watch.Restart();
    await using(var stream=File.OpenRead(file)){Check(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream))==doc.Sha,"Microbench SHA matches");}var hashMs=watch.Elapsed.TotalMilliseconds;watch.Restart();
    OfficeDocumentPolicy.Validate(file,asset.MimeType,probeMedia,managedWithoutAv:true);if(asset.MimeType=="application/pdf")ManagedPdfPolicy.Validate(file);var formatMs=watch.Elapsed.TotalMilliseconds;watch.Restart();
    File.Copy(file,Path.Combine(run,"copy-"+doc.Id+"-"+iteration+".bin"));var writeMs=watch.Elapsed.TotalMilliseconds;
    components.Add(new{asset.OriginalName,asset.ByteLength,iteration,readMs,hashMs,formatMs,writeMs});
   }
  }
 }
 await File.WriteAllTextAsync(Path.Combine(run,"file-components.json"),JsonSerializer.Serialize(components));
 stage="workflow";article=await Act(writer,article,"Submit");article=await Act(reviewer,article,"Approve"); await Post(publisher,"/admin/editor-schedule/"+cid,new(){{"ContentId",cid.ToString()},{"RevisionId",Rev(article).ToString()},{"ContentVersion",article.GetProperty("rowVersion").GetString()!},{"DueVietnam",DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).AddMinutes(30).ToString("yyyy-MM-ddTHH:mm")},{"EditorMode","approved"},{"Direct","false"}}); article=await Act(publisher,article,"PublishApproved");
 for(int repeat=0;repeat<3;repeat++){using var response=await guest.GetAsync("/bai-viet/"+cid);await response.Content.ReadAsStringAsync();Check(response.IsSuccessStatusCode,"Measure public article");}
 foreach(var document in docs){using var response=await guest.GetAsync("/media/"+document.Id);Check(response.IsSuccessStatusCode&&(await response.Content.ReadAsByteArrayAsync()).SequenceEqual(document.Bytes),"Published document bytes preserved");}
 await File.WriteAllTextAsync(Path.Combine(run,"fixture-files.json"),JsonSerializer.Serialize(docs.Select(x=>new{x.Id,x.Sha,Bytes=x.Bytes.Length})));
 await PermissionChecks.Run(options,Check); stage="COMPLETE";
}catch(Exception ex){Console.WriteLine("BLOCKED "+stage+" "+ex.GetType().Name+(ex is SqlException s?" SQL="+s.Number:""));await File.WriteAllTextAsync(Path.Combine(run,"failure.txt"),ex.ToString());Environment.ExitCode=1;}
finally{
 if(web!=null){if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(run,"host.log"),(stdout==null?"":await stdout)+(stderr==null?"":await stderr));web.Dispose();}
 await File.WriteAllTextAsync(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{stage,checks,passed=checks.Count,server=cs.DataSource,database=cs.InitialCatalog,operationalSqlTouched=false,accounts="synthetic only",smtp="Disabled",fullRegression=false},new JsonSerializerOptions{WriteIndented=true}));
 Console.WriteLine("RESULT "+stage+" checks="+checks.Count+" evidence="+run);
}
