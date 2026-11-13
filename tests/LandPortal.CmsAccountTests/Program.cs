using System.Net;
using System.Security.AccessControl;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

if(args.Length!=2||!Regex.IsMatch(args[0],"^LPCms_[a-f0-9]{12}$"))throw new Exception("Exact new isolated LocalDB instance and run directory required");
var run=Path.GetFullPath(args[1]);
if(!run.StartsWith(Path.Combine(Directory.GetCurrentDirectory(),".local","tests","cms-account-"),StringComparison.OrdinalIgnoreCase)||Directory.Exists(run))throw new Exception("New test run only");
Directory.CreateDirectory(run);
var checks=new List<string>();
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);Console.WriteLine("PASS "+label);}
void Denied(Action action,string label){try{action();}catch(InvalidOperationException){Check(true,label);return;}throw new Exception(label);}
var storage=new StorageOptions{MediaRoot=Path.Combine(run,"media"),SnapshotRoot=Path.Combine(run,"snapshots"),TempRoot=Path.Combine(run,"temp"),KeysRoot=Path.Combine(run,"keys")};
foreach(var dir in new[]{storage.MediaRoot,storage.SnapshotRoot,storage.TempRoot,storage.KeysRoot})Directory.CreateDirectory(dir);
var protection=new EphemeralDataProtectionProvider();var clock=new FixtureClock();
var delivery=new CmsTemporaryPasswordDelivery(storage,protection,clock);
var secret=PortalIdentityPolicy.NewTemporaryPassword();
var receipt=delivery.Write("actor","target","fixture-target",secret,"stamp");
var encrypted=Directory.GetFiles(Path.Combine(storage.TempRoot,"cms-credential-delivery")).Single();
Check(!File.ReadAllText(encrypted).Contains(secret),"Private envelope contains only protected ciphertext");
Denied(()=>delivery.Consume(receipt.DeliveryId,"other","target","stamp"),"Different actor cannot consume delivery");
Denied(()=>delivery.Consume(receipt.DeliveryId,"actor","other","stamp"),"Different target cannot consume delivery");
Denied(()=>delivery.Consume(receipt.DeliveryId,"actor","target","changed"),"Changed security stamp invalidates delivery");
Check(delivery.Consume(receipt.DeliveryId,"actor","target","stamp").Password==secret,"Exact actor and target can consume once");
Denied(()=>delivery.Consume(receipt.DeliveryId,"actor","target","stamp"),"Consumed envelope cannot be replayed");
var expired=delivery.Write("actor","target","fixture-target",secret,"stamp");clock.Utc=clock.Utc.AddMinutes(10);
Denied(()=>delivery.Consume(expired.DeliveryId,"actor","target","stamp"),"Ten-minute deadline enforced inclusively");
var tampered=delivery.Write("actor","target","fixture-target",secret,"stamp");
var tamperPath=Path.Combine(storage.TempRoot,"cms-credential-delivery",tampered.DeliveryId.ToString("N")+".protected");File.AppendAllText(tamperPath,"broken");
Denied(()=>delivery.Consume(tampered.DeliveryId,"actor","target","stamp"),"Ciphertext tamper rejected");
var concurrent=delivery.Write("actor","target","fixture-target",secret,"stamp");
var attempts=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(()=>{try{return delivery.Consume(concurrent.DeliveryId,"actor","target","stamp").Password==secret;}catch(InvalidOperationException){return false;}})));
Check(attempts.Count(x=>x)==1,"Concurrent delivery requests return secret exactly once");
if(OperatingSystem.IsWindows()){
 var broad=delivery.Write("actor","target","fixture-target",secret,"stamp");var path=Path.Combine(storage.TempRoot,"cms-credential-delivery",broad.DeliveryId.ToString("N")+".protected");
 var acl=new FileInfo(path).GetAccessControl();acl.AddAccessRule(new(new SecurityIdentifier("S-1-5-32-545"),FileSystemRights.Read,AccessControlType.Allow));new FileInfo(path).SetAccessControl(acl);
 Denied(()=>delivery.Consume(broad.DeliveryId,"actor","target","stamp"),"Broad credential-file ACL blocks disclosure independently of media policy");
}
Check(!LandPortal.Web.Models.RolePermissionSuggestions.All.Single(x=>x.Name=="Người duyệt").Permissions.Contains(Permissions.PublishApproved),"Reviewer suggestion never implies publication");
Check(LandPortal.Web.Models.RolePermissionSuggestions.All.All(x=>x.Permissions.All(p=>Permissions.All.Contains(p))&&!x.Permissions.Contains(Permissions.ManageUsers)),"Presets use existing content permissions without account grants");

var cs=new SqlConnectionStringBuilder{DataSource=@"(localdb)\"+args[0],InitialCatalog="LandPortal_Test",IntegratedSecurity=true,Encrypt=true,TrustServerCertificate=true,Pooling=false};
var options=new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(cs.ConnectionString).Options;
await using(var sql=new SqlConnection(new SqlConnectionStringBuilder(cs.ConnectionString){InitialCatalog="master"}.ConnectionString)){
 await sql.OpenAsync();using var q=sql.CreateCommand();q.CommandText="SELECT CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),CONVERT(int,SERVERPROPERTY('IsLocalDB')),DB_ID('LandPortal_Test')";
 using(var r=await q.ExecuteReaderAsync()){await r.ReadAsync();Check(r.GetString(0).Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase)&&r.GetInt32(1)==1&&r.IsDBNull(2),"New isolated LocalDB with no existing test database");}
 q.CommandText="CREATE DATABASE [LandPortal_Test] ON (NAME=N'LandPortal_Test',FILENAME=N'"+Path.Combine(run,"fixture.mdf").Replace("'","''")+"') LOG ON (NAME=N'LandPortal_Test_log',FILENAME=N'"+Path.Combine(run,"fixture_log.ldf").Replace("'","''")+"')";await q.ExecuteNonQueryAsync();
}
await using(var db=new PortalDbContext(options)){await db.Database.MigrateAsync();Check((await db.Database.GetAppliedMigrationsAsync()).Count()==18,"Only fresh fixture receives official schema18");}

using var rsa=RSA.Create(2048);var certRequest=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
var san=new SubjectAlternativeNameBuilder();san.AddIpAddress(IPAddress.Loopback);san.AddDnsName("localhost");certRequest.CertificateExtensions.Add(san.Build());
using var generatedCert=certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
// Schannel requires a normal user key handle. This import creates no trusted
// certificate-store entry and the client pins this exact synthetic certificate.
using var cert=X509CertificateLoader.LoadPkcs12(generatedCert.Export(X509ContentType.Pfx),null,X509KeyStorageFlags.UserKeySet|X509KeyStorageFlags.Exportable);
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(AccountAdministrationController).Assembly.FullName,ContentRootPath=Path.Combine(Directory.GetCurrentDirectory(),"src","Web"),EnvironmentName="Development",Args=[]});
builder.Configuration.Sources.Clear();builder.Logging.ClearProviders();builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(k=>{k.Listen(IPAddress.Loopback,0,o=>o.UseHttps(cert));k.Listen(IPAddress.Loopback,0);});
builder.Services.AddSingleton(storage);builder.Services.AddSingleton<IDataProtectionProvider>(protection);builder.Services.AddScoped(_=>new PortalDbContext(options));
builder.Services.AddIdentity<PortalUser,IdentityRole>(PortalIdentityPolicy.Configure).AddEntityFrameworkStores<PortalDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o=>{o.Events.OnRedirectToAccessDenied=c=>{c.Response.StatusCode=403;return Task.CompletedTask;};o.Events.OnRedirectToLogin=c=>{c.Response.StatusCode=401;return Task.CompletedTask;};});
builder.Services.AddSingleton(new PublicCaptchaOptions{AddressKey=RandomNumberGenerator.GetBytes(32),AttemptsPerWindow=20});
builder.Services.AddScoped<PublicCaptchaService>();builder.Services.AddScoped<CurrentPermissionService>();builder.Services.AddScoped<AccountAdministrationService>();builder.Services.AddSingleton<CmsTemporaryPasswordDelivery>();
builder.Services.AddScoped<IAuthorizationHandler,FreshPermissionHandler>();
builder.Services.AddAuthorization(o=>{foreach(var p in Permissions.All)o.AddPolicy(p,b=>b.Requirements.Add(new FreshPermissionRequirement(p)));});
builder.Services.AddAntiforgery();builder.Services.AddRateLimiter(o=>o.AddFixedWindowLimiter("login",l=>{l.PermitLimit=50;l.Window=TimeSpan.FromMinutes(1);}));
builder.Services.AddControllersWithViews(o=>o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddApplicationPart(typeof(AccountAdministrationController).Assembly).ConfigureApplicationPartManager(m=>m.FeatureProviders.Add(new AccountControllersOnly()));
await using var app=builder.Build();app.UseRouting();app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();app.MapControllers();
// Test-only login bypasses the unrelated login screen, but uses real Identity
// cookies. The production credential controller always rechecks current password.
app.MapGet("/fixture/login/{who}",async(string who,SignInManager<PortalUser> signIn)=>{var u=await signIn.UserManager.FindByNameAsync("fixture-"+who);if(u==null)return Results.NotFound();await signIn.SignInAsync(u,false);return Results.Ok();});
app.MapGet("/fixture/csrf",(HttpContext c,IAntiforgery a)=>Results.Text(a.GetAndStoreTokens(c).RequestToken!));
var passwords=new Dictionary<string,string>();var ids=new Dictionary<string,string>();
using(var scope=app.Services.CreateScope()){
 var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
 foreach(var who in new[]{"admin","limited","outsider"}){
  var u=new PortalUser{UserName="fixture-"+who,LockoutEnabled=true,EmailConfirmed=true};var pw=PortalIdentityPolicy.NewTemporaryPassword();
  if(!(await users.CreateAsync(u,pw)).Succeeded)throw new Exception("Fixture Identity creation failed");
  var rights=who=="admin"?Permissions.All:who=="limited"?new[]{Permissions.ManageUsers,Permissions.ManageRoles,Permissions.GrantPermissions,Permissions.Create,Permissions.ViewOwn,Permissions.EditOwn,Permissions.Submit,Permissions.ManageMedia}:new[]{Permissions.ViewOwn};
  if(!(await users.AddClaimsAsync(u,rights.Select(p=>new Claim("permission",p)))).Succeeded)throw new Exception("Fixture claims failed");passwords[who]=pw;ids[who]=u.Id;
 }
}
await app.StartAsync();
var https=app.Urls.Single(x=>x.StartsWith("https:"));var http=app.Urls.Single(x=>x.StartsWith("http:"));
HttpClient Client(string origin)=>new(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new(),ServerCertificateCustomValidationCallback=(_,c,_,_)=>c?.GetCertHashString()==cert.GetCertHashString()}){BaseAddress=new(origin)};
using var admin=Client(https);using var outsider=Client(https);using var insecure=Client(http);
await admin.GetAsync("/fixture/login/admin");await outsider.GetAsync("/fixture/login/outsider");await insecure.GetAsync("/fixture/login/admin");
async Task<Dictionary<string,string>> Fields(HttpClient c,string path){var r=await c.GetAsync(path);if(!r.IsSuccessStatusCode)throw new Exception("Fixture form status "+(int)r.StatusCode);var dom=new HtmlParser().ParseDocument(await r.Content.ReadAsStringAsync());var f=dom.QuerySelectorAll("form[action$='/temporary-credential'] input").Where(e=>e.GetAttribute("name")!=null).ToDictionary(e=>e.GetAttribute("name")!,e=>e.GetAttribute("value")??"");var label=dom.QuerySelector("label[for='credential-captcha']");if(label==null)throw new Exception("Missing CAPTCHA form; handoff="+(dom.QuerySelector(".credential-handoff")!=null)+" validation="+dom.QuerySelector(".validation-summary")?.TextContent);var q=label.TextContent;var n=Regex.Matches(q,@"\d+").Select(m=>int.Parse(m.Value)).ToArray();f["CaptchaAnswer"]=(q.Contains("cộng")?n[0]+n[1]:n[0]-n[1]).ToString();f["CurrentPassword"]=passwords["admin"];return f;}
var create=new Dictionary<string,string>{{"__RequestVerificationToken",await admin.GetStringAsync("/fixture/csrf")},{"UserName","fixture-recipient"},{"DisplayName","Synthetic account only"}};
using var created=await admin.PostAsync("/admin/users/create",new FormUrlEncodedContent(create));Check(created.StatusCode==HttpStatusCode.Redirect,"CMS create uses encrypted handoff without operator server file");
var detail=created.Headers.Location!.OriginalString;var target=Uri.UnescapeDataString(detail.Split('/').Last());var reveal=detail+"/temporary-credential";
using(var r=await admin.GetAsync(reveal))Check(r.StatusCode==HttpStatusCode.MethodNotAllowed,"Credential delivery has no GET endpoint");
Check(!Directory.Exists(Path.Combine(storage.TempRoot,"identity-credentials")),"CMS never creates legacy plaintext credential files");
var fields=await Fields(admin,detail);var noCsrf=new Dictionary<string,string>(fields);noCsrf.Remove("__RequestVerificationToken");
using(var r=await admin.PostAsync(reveal,new FormUrlEncodedContent(noCsrf)))Check(r.StatusCode==HttpStatusCode.BadRequest,"Missing CSRF rejects credential delivery");
using(var r=await outsider.PostAsync(reveal,new FormUrlEncodedContent(fields)))Check(r.StatusCode==HttpStatusCode.Forbidden,"User without account management cannot reveal credential");
var insecureFields=new Dictionary<string,string>(fields){["__RequestVerificationToken"]=await insecure.GetStringAsync("/fixture/csrf")};
using(var r=await insecure.PostAsync(reveal,new FormUrlEncodedContent(insecureFields)))Check(r.StatusCode==HttpStatusCode.BadRequest&&(await r.Content.ReadAsStringAsync()).Contains("HTTPS"),"HTTP cannot disclose credential even with authenticated admin and CSRF");
fields=await Fields(admin,detail);fields["CurrentPassword"]="WrongFixtureOnly!9";
using(var r=await admin.PostAsync(reveal,new FormUrlEncodedContent(fields)))Check(r.StatusCode==HttpStatusCode.BadRequest&&!(await r.Content.ReadAsStringAsync()).Contains("WrongFixtureOnly!9"),"Wrong reauthentication rejected without password reflection");
fields=await Fields(admin,detail);string delivered;
using(var r=await admin.PostAsync(reveal,new FormUrlEncodedContent(fields))){var html=await r.Content.ReadAsStringAsync();var dom=new HtmlParser().ParseDocument(html);delivered=dom.QuerySelector("#handoff-password")?.GetAttribute("value")??"";Check(r.IsSuccessStatusCode&&delivered.Length==24,"Explicit HTTPS reauthentication reveals temporary password once");Check(r.Headers.CacheControl?.NoStore==true&&r.Headers.GetValues("Referrer-Policy").Single()=="no-referrer","Sensitive response is no-store and no-referrer");}
using(var r=await admin.PostAsync(reveal,new FormUrlEncodedContent(fields)))Check(!r.IsSuccessStatusCode&&!(await r.Content.ReadAsStringAsync()).Contains(delivered),"Repeated HTTP POST cannot reveal password again");
using(var scope=app.Services.CreateScope()){
 var db=scope.ServiceProvider.GetRequiredService<PortalDbContext>();var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();var u=(await users.FindByIdAsync(target))!;
 Check(u.MustChangePassword&&await users.CheckPasswordAsync(u,delivered),"Delivered password matches Identity and first-change remains required");
 Check(await db.UserRoles.CountAsync(x=>x.UserId==target)==0&&await db.Users.CountAsync()==4,"Create does not grant roles or affect other accounts");
 var audit=await db.AuditLogs.Select(x=>x.Detail).ToListAsync();Check(audit.All(x=>!x.Contains(delivered)&&passwords.Values.All(p=>!x.Contains(p)))&&await db.AuditLogs.CountAsync(x=>x.Action=="AccountTemporaryCredentialDelivered")==1,"Audit records one delivery without secrets");
 var service=scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
 var reset=await service.ResetPasswordAsync(ids["admin"],target,u.ConcurrencyStamp!,true);db.ChangeTracker.Clear();var before=(await users.FindByIdAsync(target))!;
 await users.UpdateSecurityStampAsync(before);
 try{await service.RevealTemporaryPasswordAsync(ids["admin"],target,reset.CmsDelivery!.DeliveryId,(await users.FindByIdAsync(ids["admin"]))!.SecurityStamp!);throw new Exception("Stamp change bypassed");}catch(InvalidOperationException){Check(true,"Target stamp change after reset invalidates encrypted handoff");}
 db.ChangeTracker.Clear();var actor=(await users.FindByIdAsync(ids["admin"]))!;var stale=actor.SecurityStamp!;await users.UpdateSecurityStampAsync(actor);
 try{await service.RevealTemporaryPasswordAsync(actor.Id,target,reset.CmsDelivery!.DeliveryId,stale);throw new Exception("Actor stamp bypassed");}catch(UnauthorizedAccessException){Check(true,"Actor password/stamp change after reauth cannot reveal");}
 try{await service.SaveRoleAsync(ids["limited"],null,null,"forged-preset",new[]{Permissions.PublishApproved});throw new Exception("Delegation bypassed");}catch(UnauthorizedAccessException){Check(true,"Forged role preset cannot bypass server delegation");}
}
await app.StopAsync();
await File.WriteAllTextAsync(Path.Combine(run,"result.json"),System.Text.Json.JsonSerializer.Serialize(new{passed=checks.Count,checks,realHttps=true,isolatedLocalDb=cs.DataSource,operationalSql=false,realAccounts=false,smtp=false},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("CMS ACCOUNT PASS "+checks.Count+" checks; isolated HTTPS/LocalDB only");
sealed class FixtureClock:TimeProvider{public DateTimeOffset Utc=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Utc;}
sealed class AccountControllersOnly:Microsoft.AspNetCore.Mvc.ApplicationParts.IApplicationFeatureProvider<Microsoft.AspNetCore.Mvc.Controllers.ControllerFeature>
{
 public void PopulateFeature(IEnumerable<Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPart> parts,Microsoft.AspNetCore.Mvc.Controllers.ControllerFeature feature)
 {for(var i=feature.Controllers.Count-1;i>=0;i--)if(feature.Controllers[i].AsType()!=typeof(AccountAdministrationController)&&feature.Controllers[i].AsType()!=typeof(RoleAdministrationController))feature.Controllers.RemoveAt(i);}
}
