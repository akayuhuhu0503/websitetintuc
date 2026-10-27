using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Application;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;

// Explicit isolated bootstrap only. Never imported into Web startup or the Dev importer.
var root=PortalConfiguration.FindWorkspace();
var home=Environment.GetEnvironmentVariable("LAN_DEMO_HOME")??throw new InvalidOperationException("Private demo home required");
var configPath=Path.Combine(home,"config.json");
var c=new ConfigurationBuilder().AddJsonFile(configPath).AddInMemoryCollection(new Dictionary<string,string?>{{"WorkspaceRoot",home}}).Build();
if(!args.Contains("disable"))LanDemoDeployment.Validate(c,"Development");
else if(c["Deployment:Profile"]!="LAN_DEMO"||c["Sql:ExpectedDatabase"]!=LanDemoDeployment.Database||new SqlConnectionStringBuilder(DatabaseSettings.Connection(c)).DataSource!=@".\SQLEXPRESS")throw new InvalidOperationException("Exact isolated demo target required even after demo expiry.");
var opts=DatabaseSettings.Options(c,forMigration:true);
await using var dest=new PortalDbContext(opts);
if(args.FirstOrDefault()=="statistics-sample-info"){
    Console.WriteLine(JsonSerializer.Serialize(new{hashes=LanStatisticsSamples.Hashes().Order().ToArray(),provider=LanStatisticsSamples.Provider}));return;
}
if(args.FirstOrDefault()=="statistics-sample-check"){
    await LanStatisticsCheck.RunAsync(root,c,dest,args);return;
}
if(args.FirstOrDefault()=="upgrade-statistics-schema"){
    await LanSchemaUpgrade.RunAsync(root,c,dest,args);
    return;
}
if(args.Contains("sync-introductions")){
    await IntroductionSync.RunAsync(root,c,dest);
    return;
}
if(args.Contains("sync-recovered-source")){
    await IntroductionSync.RunAsync(root,c,dest,true);
    return;
}
if(args.Contains("finalize-seed")){
    if(await dest.AuditLogs.AnyAsync(x=>x.Action.StartsWith("Site")||x.Action=="AdminPasswordLogin"))throw new InvalidOperationException("Demo already edited/used; seed finalization refused.");
    var header=await dest.Set<SiteConfiguration>().SingleAsync();if(header.DraftRevisionId!=header.LiveRevisionId)throw new InvalidOperationException("Demo config already edited");
    var site=await dest.Set<SiteConfigurationRevision>().AsNoTracking().Include(x=>x.Categories).Include(x=>x.Menus).Include(x=>x.Blocks).Include(x=>x.Vocabularies).SingleAsync(x=>x.Id==header.LiveRevisionId);
    var replacement=SiteConfigurationService.Clone(site,site.EditedByUserId,DateTimeOffset.UtcNow);
    foreach(var row in replacement.Categories)row.RevisionId=replacement.Id;foreach(var row in replacement.Menus)row.RevisionId=replacement.Id;foreach(var row in replacement.Blocks)row.RevisionId=replacement.Id;foreach(var row in replacement.Vocabularies)row.RevisionId=replacement.Id;
    replacement.ContentHash=SiteConfigurationService.Hash(replacement);dest.Add(replacement);header.LiveRevisionId=replacement.Id;header.DraftRevisionId=replacement.Id;header.Generation++;
    var readonlyConfig=PortalConfiguration.Load(root);if(new SqlConnectionStringBuilder(DatabaseSettings.Connection(readonlyConfig)).InitialCatalog!="LandPortal_Dev")throw new InvalidOperationException("Readonly source must be Dev");await using var readonlyDb=new PortalDbContext(DatabaseSettings.Options(readonlyConfig,forMigration:true));
    var draftIds=await dest.Contents.Where(x=>x.Kind==ContentKind.DraftDocument).Select(x=>x.Id).ToListAsync();var current=await dest.DraftDocuments.Select(x=>x.ContentId).ToListAsync();
    foreach(var item in await readonlyDb.DraftDocuments.AsNoTracking().Where(x=>draftIds.Contains(x.ContentId)).ToListAsync())if(!current.Contains(item.ContentId))dest.DraftDocuments.Add(new(){ContentId=item.ContentId,ConsultationStartUtc=item.ConsultationStartUtc,ConsultationEndUtc=item.ConsultationEndUtc,ConsultationDateEvidence=item.ConsultationDateEvidence});
    await dest.SaveChangesAsync();Console.WriteLine("Demo seed site hash and public consultation metadata finalized; no source writes.");return;
}
async Task<ContentActor> StoredGrants(PortalUser user)
{
    var blocked=await new CurrentPermissionService(dest).GetAsync(user.Id);
    if(blocked.Has(Permissions.Submit)||blocked.Has(Permissions.ManageRoles))throw new InvalidOperationException("Initial password gate must deny operational permissions.");
    var roleIds=await dest.UserRoles.Where(x=>x.UserId==user.Id).Select(x=>x.RoleId).ToListAsync();
    var claims=await dest.RoleClaims.Where(x=>roleIds.Contains(x.RoleId)&&x.ClaimType=="permission").Select(x=>x.ClaimValue!).ToListAsync();
    return new(user.Id,claims.ToHashSet(StringComparer.Ordinal)); // Inspect stored grants; do not alter the password gate.
}
if(args.Contains("verify")){
    var servicesCheck=new ServiceCollection();servicesCheck.AddLogging();servicesCheck.AddDataProtection();servicesCheck.AddScoped(_=>new PortalDbContext(opts));servicesCheck.AddIdentity<PortalUser,IdentityRole>(PortalIdentityPolicy.Configure).AddEntityFrameworkStores<PortalDbContext>().AddDefaultTokenProviders();using var checkProvider=servicesCheck.BuildServiceProvider();using var checkScope=checkProvider.CreateScope();var manager=checkScope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
    using var secrets=JsonDocument.Parse(await File.ReadAllBytesAsync(Environment.GetEnvironmentVariable("LAN_DEMO_CREDENTIALS")!));
    var checks=new List<object>();foreach(var entry in secrets.RootElement.GetProperty("accounts").EnumerateArray()){var name=entry.GetProperty("userName").GetString()!;var user=(await manager.FindByNameAsync(name))!;if(user==null||!await manager.CheckPasswordAsync(user,entry.GetProperty("password").GetString()!)||user.IsDisabled||!user.LockoutEnabled||!user.MustChangePassword)throw new InvalidOperationException("LAN Identity credentials/state check failed; no credentials logged");var actor=await StoredGrants(user);if(name.EndsWith("writer")&&(actor.Has(Permissions.PublishDirect)||actor.Has(Permissions.PublishApproved)||!actor.Has(Permissions.Submit)))throw new InvalidOperationException("Writer permissions differ");if(name.EndsWith("reviewer")&&(!actor.Has(Permissions.Review)||!actor.Has(Permissions.PublishApproved)||actor.Has(Permissions.ManageRoles)))throw new InvalidOperationException("Reviewer permissions differ");if(name.EndsWith("admin")&&(!actor.Has(Permissions.ManageUsers)||!actor.Has(Permissions.ManageRoles)||!actor.Has(Permissions.PublishSite)))throw new InvalidOperationException("Admin permissions differ");checks.Add(new{name,roles=await manager.GetRolesAsync(user),passwordValid=true,mustChangePassword=true,lockout=true,mfaRequired=c["AdminSession:RequireMfa"],enrolled=user.TwoFactorEnabled});}
    var negative=0;foreach(var change in new[]{new KeyValuePair<string,string?>("Sql:ExpectedDatabase","LandPortal_Dev"),new("Mail:Transport","Smtp"),new("Media:AllowUnscannedDevelopmentUploads","true"),new("AdminSession:RequireMfa","false")}){try{LanDemoDeployment.Validate(new ConfigurationBuilder().AddConfiguration(c).AddInMemoryCollection([change]).Build(),"Development");throw new Exception("LAN guard bypass");}catch(InvalidOperationException){negative++;}}
    if(MediaUploadOptions.FromConfiguration(c,"Development").CanServe(new(){ValidationStatus=BatchMediaProof.Validation,Origin="Source",ScanStatus="NotScanned"}))throw new Exception("Unsafe media exposed");
    var result=new{database=dest.Database.GetDbConnection().Database,accounts=checks,negativeGuards=negative,notScannedDenied=true,contents=await dest.Contents.CountAsync(),assets=await dest.MediaAssets.CountAsync(),unsafeAssets=await dest.MediaAssets.CountAsync(x=>x.ScanStatus!="Clean"||x.IsQuarantined),interactions=await dest.Set<InteractionSubmission>().CountAsync(),outbox=await dest.Set<InteractionOutbox>().CountAsync(),captcha=await dest.Set<PublicCaptchaChallenge>().CountAsync(),sourceIdentityCopied=false,fullRegression="NOT_RUN"};await File.WriteAllTextAsync(Path.Combine(home,"verify-result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PASS isolated DB, three real Identity password/role boundaries, mandatory MFA/lockout/CAPTCHA config, four LAN deployment negatives and NotScanned denial; no credentials logged.");return;
}
if(args.Contains("disable")){
    await dest.Database.OpenConnectionAsync();using(var cmd=dest.Database.GetDbConnection().CreateCommand()){cmd.CommandText="SELECT DB_NAME(),CAST(SERVERPROPERTY('MachineName') AS nvarchar(200))";using var rd=await cmd.ExecuteReaderAsync();if(!await rd.ReadAsync()||rd.GetString(0)!=LanDemoDeployment.Database||!string.Equals(rd.GetString(1),Environment.MachineName,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Wrong demo target");}
    await dest.Users.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsDisabled,true).SetProperty(x=>x.SecurityStamp,Guid.NewGuid().ToString()));
    Console.WriteLine("Only LAN demo accounts disabled; existing databases untouched.");return;
}
await using(var conn=new SqlConnection(DatabaseSettings.Connection(c))){var master=new SqlConnectionStringBuilder(conn.ConnectionString){InitialCatalog="master"};await using var probe=new SqlConnection(master.ConnectionString);await probe.OpenAsync();using var cmd=probe.CreateCommand();cmd.CommandText="SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(200)),DB_ID(N'LandPortal_LanDemo')";using var rd=await cmd.ExecuteReaderAsync();if(!await rd.ReadAsync()||!string.Equals(rd.GetString(0),Environment.MachineName,StringComparison.OrdinalIgnoreCase)||!rd.IsDBNull(1))throw new InvalidOperationException("Demo creation requires exact local instance and absent LandPortal_LanDemo; never reset/reseed existing database.");}
await dest.Database.MigrateAsync();
await RuntimeRoleInterceptor.ProvisionAsync(dest,c["Runtime:RolePassword"]!);
var services=new ServiceCollection();services.AddLogging();services.AddDataProtection();services.AddScoped(_=>new PortalDbContext(opts));services.AddIdentity<PortalUser,IdentityRole>(PortalIdentityPolicy.Configure).AddEntityFrameworkStores<PortalDbContext>().AddDefaultTokenProviders();
using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
void Ok(IdentityResult result){if(!result.Succeeded)throw new InvalidOperationException("Demo Identity operation rejected: "+string.Join(',',result.Errors.Select(x=>x.Code)));}
var specs=new[]{("writer","Người viết",new[]{Permissions.Create,Permissions.ViewOwn,Permissions.EditOwn,Permissions.Submit,Permissions.ManageMedia}),("reviewer","Người duyệt",new[]{Permissions.ViewAll,Permissions.EditAll,Permissions.Review,Permissions.PublishApproved,Permissions.Unpublish,Permissions.ManageMedia}),("admin","Quản trị",Permissions.All)};
var accounts=new List<object>();string? admin=null;
foreach(var(suffix,label,permissions)in specs){var role="LanDemo."+suffix;Ok(await roles.CreateAsync(new(role)));foreach(var permission in permissions)Ok(await roles.AddClaimAsync((await roles.FindByNameAsync(role))!,new("permission",permission)));var password="Aa7!"+Convert.ToBase64String(RandomNumberGenerator.GetBytes(27));var user=new PortalUser{UserName="lan-ba-"+suffix,DisplayName=label+" (LAN demo)",Email="lan-ba-"+suffix+"@example.invalid",EmailConfirmed=true,LockoutEnabled=true,MustChangePassword=true};Ok(await users.CreateAsync(user,password));Ok(await users.AddToRoleAsync(user,role));Ok(await users.AddClaimAsync(user,new("lan.demo","isolated-v1")));if(suffix=="admin")admin=user.Id;accounts.Add(new{userName=user.UserName,password,role=label,changePasswordAtFirstLogin=true,mfa="Enroll authenticator via existing /admin/account/mfa flow after changing initial password"});}
var credentials=Environment.GetEnvironmentVariable("LAN_DEMO_CREDENTIALS")??throw new InvalidOperationException("External secured credentials file required");
if(Path.GetFullPath(credentials).StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Credentials must be outside repository");
await using(var file=new FileStream(credentials,FileMode.CreateNew,FileAccess.Write,FileShare.None)){await JsonSerializer.SerializeAsync(file,new{url=c["Public:BaseUrl"],database=LanDemoDeployment.Database,expiresUtc=c["LanDemo:ExpiresUtc"],accounts},new JsonSerializerOptions{WriteIndented=true});}

// Source is strictly read-only. Whitelist public tables, never restore/copy a whole database.
var original=PortalConfiguration.Load(root);var sourceOpts=DatabaseSettings.Options(original,forMigration:true);
if(new SqlConnectionStringBuilder(DatabaseSettings.Connection(original)).InitialCatalog!="LandPortal_Dev")throw new InvalidOperationException("Expected readonly source Dev");
await using var source=new PortalDbContext(sourceOpts);
var eligible=await source.Contents.AsNoTracking().Where(x=>x.IsPublic&&!x.IsTrashed&&!x.HasEditorialChanges&&x.PublishedRevisionId!=null&&x.Kind!=ContentKind.Video&&source.SourceRecords.Any(s=>s.ContentId==x.Id&&s.Source=="qldd.mae.gov.vn")&&source.Revisions.Any(r=>r.Id==x.PublishedRevisionId&&r.IsSourceRevision)&&!source.ContentAssets.Any(a=>a.RevisionId==x.PublishedRevisionId&&(!a.MediaAsset.IsQuarantined? a.MediaAsset.ScanStatus!="Clean":true))).ToListAsync();
var ids=eligible.Select(x=>x.Id).ToArray();var revisionIds=eligible.Select(x=>x.PublishedRevisionId!.Value).ToArray();
var revisions=await source.Revisions.AsNoTracking().Where(x=>revisionIds.Contains(x.Id)).ToListAsync();var links=await source.ContentAssets.AsNoTracking().Where(x=>revisionIds.Contains(x.RevisionId)).ToListAsync();var mids=links.Select(x=>x.MediaAssetId).Distinct().ToArray();
var assets=await source.MediaAssets.AsNoTracking().Where(x=>mids.Contains(x.Id)&&x.ScanStatus=="Clean"&&!x.IsQuarantined).ToListAsync();if(assets.Count!=mids.Length)throw new InvalidOperationException("Demo clean asset set incomplete");
foreach(var m in assets){var src=original["Storage:MediaRoot"]!;src=Path.GetFullPath(src,root);var path=StorageOptions.ResolveWithin(src,m.StorageKey);var target=StorageOptions.ResolveWithin(c["Storage:MediaRoot"]!,m.StorageKey);for(string? n=path;n!=null;n=Path.GetDirectoryName(n))if((File.Exists(n)||Directory.Exists(n))&&(File.GetAttributes(n)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Reparse media refused");var bytes=await File.ReadAllBytesAsync(path);if(bytes.LongLength!=m.ByteLength||Convert.ToHexStringLower(SHA256.HashData(bytes))!=m.Sha256)throw new InvalidOperationException("Source media hash mismatch");Directory.CreateDirectory(Path.GetDirectoryName(target)!);await using(var file=new FileStream(target,FileMode.CreateNew))await file.WriteAsync(bytes);if(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(target)))!=m.Sha256)throw new InvalidOperationException("Copied media mismatch");m.UploadedByUserId=null;}
dest.MediaAssets.AddRange(assets);dest.Categories.AddRange(await source.Categories.AsNoTracking().ToListAsync());await dest.SaveChangesAsync();
foreach(var content in eligible){content.CurrentRevisionId=null;content.PublishedRevisionId=null;content.SubmittedRevisionId=null;content.ApprovedRevisionId=null;content.ApprovedByUserId=null;content.ApprovalHash=null;content.OwnerUserId=admin;content.RowVersion=[];content.Revisions=[];content.Categories=[];}
dest.Contents.AddRange(eligible);await dest.SaveChangesAsync();
foreach(var revision in revisions){revision.EditedByUserId=null;revision.Content=null!;revision.Assets=[];revision.Categories=[];}
dest.Revisions.AddRange(revisions);await dest.SaveChangesAsync();
dest.ContentAssets.AddRange(links.Select(x=>new ContentAsset{RevisionId=x.RevisionId,MediaAssetId=x.MediaAssetId,Usage=x.Usage,Caption=x.Caption,SortOrder=x.SortOrder}));
dest.RevisionCategories.AddRange((await source.RevisionCategories.AsNoTracking().Where(x=>revisionIds.Contains(x.RevisionId)).ToListAsync()).Select(x=>new RevisionCategory{RevisionId=x.RevisionId,CategoryId=x.CategoryId}));
dest.ContentCategories.AddRange((await source.ContentCategories.AsNoTracking().Where(x=>ids.Contains(x.ContentId)).ToListAsync()).Select(x=>new ContentCategory{ContentId=x.ContentId,CategoryId=x.CategoryId}));
foreach(var content in eligible){var rev=revisions.Single(x=>x.ContentId==content.Id);content.CurrentRevisionId=rev.Id;content.PublishedRevisionId=rev.Id;}
dest.LegalDocuments.AddRange((await source.LegalDocuments.AsNoTracking().Where(x=>ids.Contains(x.ContentId)).ToListAsync()).Select(x=>new LegalDocument{ContentId=x.ContentId,Number=x.Number,DocumentType=x.DocumentType,IssuingBody=x.IssuingBody,Signer=x.Signer,Field=x.Field,IssuedDate=x.IssuedDate,EffectiveDate=x.EffectiveDate,EffectivenessStatus=x.EffectivenessStatus}));
dest.SourceRecords.AddRange((await source.SourceRecords.AsNoTracking().Where(x=>ids.Contains(x.ContentId)).ToListAsync()).Select(x=>new SourceRecord{ContentId=x.ContentId,Source=x.Source,SourceId=x.SourceId,Kind=x.Kind,CanonicalUrl=x.CanonicalUrl,LastContentHash=x.LastContentHash,LastSeenUtc=x.LastSeenUtc}));
dest.SourceUrlAliases.AddRange((await source.SourceUrlAliases.AsNoTracking().Where(x=>ids.Contains(x.ContentId)).ToListAsync()).Select(x=>new SourceUrlAlias{ContentId=x.ContentId,Url=x.Url,UrlHash=x.UrlHash}));
await dest.SaveChangesAsync();
var liveId=(await source.Set<SiteConfiguration>().AsNoTracking().SingleAsync()).LiveRevisionId;
if(liveId is Guid live){var site=await source.Set<SiteConfigurationRevision>().AsNoTracking().Include(x=>x.Categories).Include(x=>x.Menus).Include(x=>x.Blocks).Include(x=>x.Vocabularies).SingleAsync(x=>x.Id==live);site.EditedByUserId=admin!;site.LogoAssetId=null;site.FooterText="BẢN THỬ NGHIỆM LAN DÀNH CHO BA — chỉ dữ liệu công khai đủ điều kiện. Không dùng thông tin cá nhân; không phải website vận hành chính thức.";site.EnglishName="Department of Land Administration";site.Banners=[];site.Pins=[];foreach(var menu in site.Menus.Where(x=>x.ContentId!=null&&!ids.Contains(x.ContentId.Value)))menu.Enabled=false;site.ContentHash=SiteConfigurationService.Hash(site);dest.Add(site);await dest.SaveChangesAsync();dest.Add(new SiteConfiguration{Id=1,LiveRevisionId=site.Id,DraftRevisionId=site.Id});await dest.SaveChangesAsync();}
await SearchProjectionService.BackfillAsync(dest);
dest.AuditLogs.Add(new(){Actor="owner-authorized-lan-demo",Action="LanDemoPrepared",Detail="Whitelisted public source content/Clean files only; no source Identity/interactions/drafts/captcha/private media/keys copied; MFA required; SMTP and workers disabled"});await dest.SaveChangesAsync();
var verify=new{database=LanDemoDeployment.Database,contents=await dest.Contents.CountAsync(),revisions=await dest.Revisions.CountAsync(),assets=await dest.MediaAssets.CountAsync(),aliases=await dest.SourceUrlAliases.CountAsync(),accounts=await dest.Users.CountAsync(),unsafeAssets=await dest.MediaAssets.CountAsync(x=>x.ScanStatus!="Clean"||x.IsQuarantined),interactions=await dest.Set<InteractionSubmission>().CountAsync(),roles=specs.Select(x=>new{name=x.Item1,permissions=x.Item3}),sourceUnchanged=true};
await File.WriteAllTextAsync(Path.Combine(home,"setup-result.json"),JsonSerializer.Serialize(verify,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"LAN demo created: {verify.contents} public contents, {verify.assets} Clean assets, 3 new accounts. Credentials stored outside repository; no passwords logged.");
