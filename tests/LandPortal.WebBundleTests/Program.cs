using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using SkiaSharp;

if(args.Length!=1)throw new ArgumentException("Published WebBundle web path required; no SQL is opened.");
var web=Path.GetFullPath(args[0]);
var layout=PortablePackageLayout.Resolve(web);
if(!layout.WebBundle)throw new Exception("Expected WebBundle");
var checks=new List<string>();
void Check(bool condition,string name){if(!condition)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
var config=layout.LoadBoundSettings();
// Same process-environment values applied by the portable loader; no operational profile change.
layout.SetRuntimeLocations(config);
Check(config["Storage:MediaRoot"]==Path.Combine(Directory.GetParent(web)!.FullName,"private","media"),"19-key env keeps existing sibling media path");
Check(config["Deployment:Profile"]=="UAT","Real published config loads with manifest and Web/Worker hashes");
var workerLayout=PortablePackageLayout.Resolve(Path.Combine(layout.Root,"worker"));
Check(workerLayout==layout,"Worker and Web resolve identical configuration and stable data home");
workerLayout.VerifyBinding();
var iis=XDocument.Load(Path.Combine(web,"web.config"));
Check(iis.Descendants("hiddenSegments").Elements("add").Any(x=>(string?)x.Attribute("segment")==".runtime"),"Published IIS request filter hides embedded runtime");
Check(new VideoToolLocator(web).FixedDirectory==new VideoToolLocator(Path.Combine(layout.Root,"worker")).FixedDirectory,"Web/Video Worker use same external FFmpeg tools directory");
var old=Environment.GetEnvironmentVariable("Media__AllowManagedImageWithoutAv");
try {Environment.SetEnvironmentVariable("Media__AllowManagedImageWithoutAv","false");Check(layout.LoadBoundSettings()["Media:AllowManagedImageWithoutAv"]=="true","Old ambient upload=false cannot override published env");}
finally {Environment.SetEnvironmentVariable("Media__AllowManagedImageWithoutAv",old);}
Check(UatDeployment.TemporaryRuntimeIdentityMatches("WIN-S1IBJB84OOO","website_tin_tuc","website_tin_tuc_app","LandPortalRuntime",70,18,"20261110050000_HomeEventBanner"),"Runtime18 identity guard retained");
var run=Path.Combine(Path.GetTempPath(),"LandPortal-WebBundle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
var poolMetadata=Path.Combine(run,"isolated-iis-metadata.xml");
var oldPoolMetadata=Environment.GetEnvironmentVariable("APP_POOL_CONFIG");
try {
    await File.WriteAllTextAsync(poolMetadata,new XDocument(new XElement("configuration",new XElement("virtualDirectory",new XAttribute("physicalPath",web)))).ToString());
    Environment.SetEnvironmentVariable("APP_POOL_CONFIG",poolMetadata);
    MediaService.ValidateStorage(StorageOptions.FromConfiguration(config));
    Check(true,"Actual package sibling private under publish passes shared storage guard with isolated IIS metadata");
    await File.WriteAllTextAsync(poolMetadata,new XDocument(new XElement("configuration",new XElement("virtualDirectory",new XAttribute("physicalPath",web)),new XElement("virtualDirectory",new XAttribute("physicalPath",Path.Combine(layout.Root,"private"))))).ToString());
    try{MediaService.ValidateStorage(StorageOptions.FromConfiguration(config));throw new Exception("Separate runtime mapping allowed");}catch(MediaUploadException){Check(true,"Separate IIS virtual directory exposing runtime rejected");}
}finally{Environment.SetEnvironmentVariable("APP_POOL_CONFIG",oldPoolMetadata);}
var storage=new StorageOptions{MediaRoot=Path.Combine(run,"media"),TempRoot=Path.Combine(run,"temp"),SnapshotRoot=Path.Combine(run,"snapshots"),KeysRoot=Path.Combine(run,"keys")};
foreach(var pair in new[]{("MediaRoot",storage.MediaRoot),("TempRoot",storage.TempRoot),("SnapshotRoot",storage.SnapshotRoot),("KeysRoot",storage.KeysRoot)}){config["Storage:"+pair.Item1]=pair.Item2;Directory.CreateDirectory(pair.Item2);}
config["WorkspaceRoot"]=run;
PortableIisConfiguration.ValidateExternalStores(config,web);
Check(true,"External data roots validated without touching deployed storage");
var mediaOptions=MediaUploadOptions.FromConfiguration(config,"Staging");
Check(mediaOptions.ApprovedManagedImageWithoutAv,"Published 19-key env activates managed image policy through real UAT validation");
var savedStore=config["Storage:MediaRoot"];config["Storage:MediaRoot"]=Path.Combine(web,".runtime","media");
try {PortableIisConfiguration.ValidateExternalStores(config,web);throw new Exception("Inside-web storage accepted");}catch(InvalidOperationException){Check(true,"Inside-web persistent storage rejected");}finally{config["Storage:MediaRoot"]=savedStore;}

using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
await using var db=new FixtureDb(new DbContextOptionsBuilder<PortalDbContext>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
db.Users.Add(new PortalUser{Id="fixture",UserName="fixture"});
foreach(var permission in new[]{Permissions.ManageMedia,Permissions.ManageSite})db.UserClaims.Add(new IdentityUserClaim<string>{UserId="fixture",ClaimType="permission",ClaimValue=permission});
await db.SaveChangesAsync();
var media=new MediaService(db,storage,mediaOptions,config);
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=web,WebRootPath=Path.Combine(web,"wwwroot"),EnvironmentName="Development",Args=[]});
builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddSingleton<PortalDbContext>(db);builder.Services.AddSingleton(storage);builder.Services.AddSingleton(mediaOptions);builder.Services.AddSingleton(media);builder.Services.AddSingleton(new CurrentPermissionService(db));
builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuthentication>("fixture",null);builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o=>o.HeaderName="X-CSRF-TOKEN");builder.Services.AddControllers().AddApplicationPart(typeof(AdminMediaController).Assembly);
await using var app=builder.Build();app.UsePrivateRuntimeProtection();app.UseStaticFiles();app.UseAuthentication();app.UseAuthorization();app.MapControllers();
app.MapGet("/fixture-token",(HttpContext ctx,IAntiforgery anti)=>anti.GetAndStoreTokens(ctx).RequestToken!);await app.StartAsync();
using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new()}){BaseAddress=new(app.Urls.Single())};
foreach(var path in new[]{"/.runtime/private/uat.json","/.runtime/manifest.json","/.runtime/worker/Worker.dll","/.runtime/ops/ttdl.env.example","/.RUNTIME/private/uat.json","/%2eruntime/private/uat.json","/%252eruntime/private/uat.json","/x/.runtime/private/uat.json","/.runtime%2fprivate%2fuat.json","/.runtime./private/uat.json","/.env","/appsettings.json","/web.config","/Infrastructure.dll","/private/uat.json"}){
 using var response=await http.GetAsync(path);Check(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest,"HTTP secret/artifact blocked: "+path);
}
var token=await http.GetStringAsync("/fixture-token");
using var bitmap=new SKBitmap(12,8);bitmap.Erase(SKColors.DarkRed);using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,90);
using var multipart=new MultipartFormDataContent();var bytes=new ByteArrayContent(png.ToArray());bytes.Headers.ContentType=new("image/png");multipart.Add(bytes,"file","fixture.png");
using var request=new HttpRequestMessage(HttpMethod.Post,"/admin/media/upload?site=true"){Content=multipart};request.Headers.Add("X-CSRF-TOKEN",token);
using var uploaded=await http.SendAsync(request);
Check(uploaded.StatusCode==HttpStatusCode.OK,"Actual banner HTTP upload PNG succeeds without scanner");
var asset=await db.MediaAssets.SingleAsync();Check(asset.ScanStatus=="NotScanned"&&asset.ValidationStatus==MediaUploadOptions.ManagedImageNoAvValidation,"PNG retains truthful NotScanned technical validation");
Check(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(storage.ResolveMedia(asset.StorageKey))))==asset.Sha256,"Uploaded PNG stored hash verified");
await app.StopAsync();

// Small isolated layout fixture: test tampering and whole-directory rollback, never mutate published input.
var fixture=Path.Combine(run,"package","web");var runtime=Path.Combine(fixture,".runtime");Directory.CreateDirectory(Path.Combine(runtime,"private"));Directory.CreateDirectory(Path.Combine(runtime,"worker"));
var fixtureConfig=Path.Combine(runtime,"private","uat.json");await File.WriteAllTextAsync(fixtureConfig,"{\"Deployment\":{\"Profile\":\"UAT\"}}");
var paths=new[]{"Web.dll","Infrastructure.dll","web.config",".runtime/worker/Worker.dll",".runtime/worker/Infrastructure.dll"};
foreach(var p in paths)await File.WriteAllTextAsync(Path.Combine(fixture,p),"old-release");
string Hash(string file)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
await File.WriteAllTextAsync(Path.Combine(runtime,"manifest.json"),JsonSerializer.Serialize(new{privateMode="WebBundle",privateConfiguration=new{path="private/uat.json",confidential=true,sha256=Hash(fixtureConfig)},files=paths.Select(p=>new{path=p,sha256=Hash(Path.Combine(fixture,p))})}));
var isolated=PortablePackageLayout.Resolve(fixture);isolated.VerifyBinding();
await File.AppendAllTextAsync(fixtureConfig," ");try{isolated.VerifyBinding();throw new Exception("Tampered JSON accepted");}catch(InvalidOperationException){Check(true,"Changed private JSON refused");}
await File.WriteAllTextAsync(fixtureConfig,"{\"Deployment\":{\"Profile\":\"UAT\"}}");
await File.WriteAllTextAsync(Path.Combine(runtime,"worker","Worker.dll"),"mixed-release");try{isolated.VerifyBinding();throw new Exception("Mixed Worker accepted");}catch(InvalidOperationException){Check(true,"Mixed Worker binary refused");}
await File.WriteAllTextAsync(Path.Combine(runtime,"worker","Worker.dll"),"old-release");
Directory.Move(fixture,fixture+"-rollback");Directory.Move(fixture+"-rollback",fixture);isolated.VerifyBinding();Check(true,"Whole-web rollback restores valid bundle without storage changes");
var legacyRoot=Path.Combine(run,"legacy");Directory.CreateDirectory(Path.Combine(legacyRoot,"web"));Directory.CreateDirectory(Path.Combine(legacyRoot,"worker"));Directory.CreateDirectory(Path.Combine(legacyRoot,"private"));
var legacyConfig=Path.Combine(legacyRoot,"private","uat.json");await File.WriteAllTextAsync(legacyConfig,"{}");
await File.WriteAllTextAsync(Path.Combine(legacyRoot,"manifest.json"),JsonSerializer.Serialize(new{privateMode="Portable",privateConfiguration=new{path="private/uat.json",confidential=true,sha256=Hash(legacyConfig)}}));
var legacy=PortablePackageLayout.Resolve(Path.Combine(legacyRoot,"web"));legacy.VerifyBinding();Check(!legacy.WebBundle&&legacy.DefaultDataHome==Path.Combine(legacyRoot,"private"),"Legacy portable layout remains valid for first-transition rollback");
Console.WriteLine($"TOTAL {checks.Count} PASS; isolated HTTP/SQLite; no SQL Server/IIS access; evidence={run}");
await File.WriteAllTextAsync(Path.Combine(run,"result.json"),JsonSerializer.Serialize(new{checks,sqlServerTouched=false,iisTouched=false}));

sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e):AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e){protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"fixture")],"fixture")),"fixture")));}
sealed class FixtureDb(DbContextOptions<PortalDbContext> options):PortalDbContext(options){protected override void OnModelCreating(ModelBuilder b){base.OnModelCreating(b);foreach(var entity in b.Model.GetEntityTypes()){foreach(var index in entity.GetIndexes())index.SetFilter(null);foreach(var p in entity.GetProperties()){p.SetColumnType(null);p.SetDefaultValueSql(null);p.SetComputedColumnSql(null);if(p.ClrType==typeof(byte[])&&p.IsConcurrencyToken)p.ValueGenerated=ValueGenerated.Never;if(p.ClrType==typeof(DateTimeOffset))p.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());}}}}
