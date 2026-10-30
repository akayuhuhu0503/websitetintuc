using System.Net;
using System.Text.Json;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Controllers;
using LandPortal.Web.Models;
using LandPortal.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;

namespace LandPortal.Tests;

public static class P0ReadyTests
{
    static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    public static async Task BootstrapPolicy(DbContextOptions<PortalDbContext> options)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddDataProtection();services.AddScoped(_=>new PortalDbContext(options));
        services.AddIdentity<PortalUser,IdentityRole>(PortalIdentityPolicy.Configure).AddEntityFrameworkStores<PortalDbContext>().AddDefaultTokenProviders();
        using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        Check(users.Options.Password.RequiredLength==8&&users.Options.Lockout.MaxFailedAccessAttempts==5&&users.Options.Lockout.DefaultLockoutTimeSpan==TimeSpan.FromMinutes(15),"Bootstrap/Web Identity policies diverged.");
        foreach(var validator in users.PasswordValidators){Check(!(await validator.ValidateAsync(users,new(),"abcdefg")).Succeeded,"Weak initial administrator password accepted.");Check((await validator.ValidateAsync(users,new(),PortalIdentityPolicy.NewTemporaryPassword())).Succeeded,"Generated initial password denied.");}
        var absent=new ConfigurationBuilder().Build();var denied=false;try{await BootstrapIdentity.InitializeAsync(provider,absent);}catch(InvalidOperationException){denied=true;}Check(denied,"Missing bootstrap credentials reported false success.");
        await using var beforeDb=new PortalDbContext(options);var roles=await beforeDb.Roles.CountAsync();var accounts=await beforeDb.Users.CountAsync();
        var missingDelivery=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Bootstrap:AdminEmail","p0-policy-"+Guid.NewGuid().ToString("N")+"@landportal.invalid"}}).Build();denied=false;
        try{await BootstrapIdentity.InitializeAsync(provider,missingDelivery);}catch(InvalidOperationException){denied=true;}Check(denied&&await beforeDb.Roles.CountAsync()==roles&&await beforeDb.Users.CountAsync()==accounts,"Bootstrap without private delivery storage changed roles/accounts.");
    }
    public static Task HomeAndAuditPolicy()
    {
        var site=new SitePublicConfiguration(new(),[],[],null,false);
        var card=new ContentCard(Guid.NewGuid(),"technical","Technical fixture",null,null,null,"Tin tức");
        var valid=new SiteHomeBlockResult(new(){Type="Featured"},[card],"/tin-tuc");
        var home=new ConfiguredHomeModel{Site=site,Blocks=[new(new(){Type="Featured"},[card],null,"PRIVATE-CORRELATION"),new(new(){Type="Announcements"},[],null),new(new(){Type="Links"},[],null),valid]};
        Check(home.VisibleBlocks.SequenceEqual([valid]),"BR03 leaked failed/empty blocks or stale cards from a failed block.");
        var admin=new ContentActor("technical",new HashSet<string>{Permissions.ManageUsers,Permissions.ManageRoles});
        Check(AuditController.CanRead(admin)&&!AuditController.CanRead(admin with{CategoryIds=new HashSet<int>{75}})&&!AuditController.CanRead(new("technical",new HashSet<string>{Permissions.ManageUsers})),"Audit global administrator boundary failed.");
        return Task.CompletedTask;
    }

    // Executed on the real Test HTTP host, after CAPTCHA/Identity login. No
    // production accounts or imported source contents are edited by these tests.
    public static async Task InboxAndAudit(HttpClient admin,HttpClient guest,DbContextOptions<PortalDbContext> options,IConfiguration config,string userId)
    {
        var marker="P0 technical "+Guid.NewGuid().ToString("N");Guid articleId,otherId;var expected=new List<Guid>();Guid unrelated;
        await using(var db=new PortalDbContext(options))
        {
            var actor=await new CurrentPermissionService(db).GetAsync(userId);
            var workflow=new WorkflowService(db,new(),MediaUploadOptions.FromConfiguration(config,"Development"),StorageOptions.FromConfiguration(config));
            async Task<WorkflowResult> Article(int category){var r=await workflow.SaveAsync(null,Phase2aSqlTests.Input(marker,category),actor,null,Guid.NewGuid());return await workflow.PublishDirectAsync(r.ContentId,r.CurrentRevisionId!.Value,actor,r.RowVersion,Guid.NewGuid());}
            var article=await Article(75);var other=await Article(76);articleId=article.ContentId;otherId=other.ContentId;
            for(var i=0;i<22;i++)
            {
                var s=new InteractionSubmission{ChallengeId=Guid.NewGuid(),Kind=InteractionKind.Comment,ContentId=i<21?articleId:otherId,PublishedRevisionId=i<21?article.PublishedRevisionId:other.PublishedRevisionId,PublicConsent=true,CreatedUtc=DateTimeOffset.UtcNow.AddSeconds(i),ReceiptHash=TextNormalizer.Hash(Guid.NewGuid().ToString()),EmailHash=TextNormalizer.Hash(Guid.NewGuid().ToString()),ProtectedOriginal="Technical fixture; never decrypted in inbox",State=InteractionState.Pending};
                db.InteractionSubmissions.Add(s);if(i<21)expected.Add(s.Id);
            }
            await db.SaveChangesAsync();unrelated=await db.InteractionSubmissions.Where(x=>x.ContentId==otherId).Select(x=>x.Id).SingleAsync();
            var s0=await db.InteractionSubmissions.SingleAsync(x=>x.Id==expected[0]);var publicRow=new InteractionPublicSnapshot{SubmissionId=s0.Id,DisplayName="Anonymous Test",Text="<script>public-encoded</script>",AgencyAnswer="<script>answer-encoded</script>",ApprovedBy=userId,ApprovedUtc=DateTimeOffset.UtcNow};db.InteractionPublicSnapshots.Add(publicRow);await db.SaveChangesAsync();s0.PublicSnapshotId=publicRow.Id;
            db.AuditLogs.Add(new(){Actor=userId,Action=marker,Detail="PRIVATE-DETAIL-MUST-NOT-RENDER"});await db.SaveChangesAsync();
            Check(await db.AuditLogs.AnyAsync(x=>x.Actor==userId&&x.Action=="AdminPasswordLogin"),"Password login has no actual SQL audit.");
        }
        async Task<string> Html(HttpClient client,string path){using var r=await client.GetAsync(path);Check(r.IsSuccessStatusCode,"HTTP failed "+path+" "+r.StatusCode);return WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());}
        var first=await Html(admin,"/admin/tuong-tac?kind=Comment&contentId="+articleId);var second=await Html(admin,"/admin/tuong-tac?kind=Comment&contentId="+articleId+"&page=2");
        var list1=await Html(admin,"/admin?page=1");var list2=await Html(admin,"/admin?page=2");
        string[] ArticleLinks(string html)=>new HtmlParser().ParseDocument(html).QuerySelectorAll(".admin-table tbody tr td:first-child a").Select(x=>x.GetAttribute("href")!).ToArray();
        Check(ArticleLinks(list1).Length==15&&ArticleLinks(list2).Length==15&&!ArticleLinks(list1).Intersect(ArticleLinks(list2)).Any(),"Admin SQL identifier paging lost/duplicated rows.");
        var filteredList=await Html(admin,"/admin?q="+Uri.EscapeDataString(marker));Check(ArticleLinks(filteredList).Length==2&&ArticleLinks(filteredList).Contains("/admin/edit/"+articleId)&&ArticleLinks(filteredList).Contains("/admin/edit/"+otherId),"Admin title filter differs after materialization.");
        Guid[] Ids(string html)=>new HtmlParser().ParseDocument(html).QuerySelectorAll("[data-batch-row]").Select(x=>Guid.Parse(x.GetAttribute("data-id")!)).ToArray();
        Check(Ids(first).Length==20&&Ids(second).Length==1&&Ids(first).Concat(Ids(second)).ToHashSet().SetEquals(expected)&&!first.Contains(unrelated.ToString()),"Article filter SQL paging mixed contents or duplicated records.");
        var next=new HtmlParser().ParseDocument(first).QuerySelectorAll("a").Single(x=>x.TextContent=="Trang sau").GetAttribute("href")!;Check(next.Contains("contentId="+articleId),"Paging lost article filter.");
        foreach(var path in new[]{"/admin/tuong-tac?contentId=bad-guid","/admin/tuong-tac?state=999","/admin/tuong-tac?kind=Contact&contentId="+articleId}){using var r=await admin.GetAsync(path);Check(r.StatusCode==HttpStatusCode.BadRequest,"Invalid filter did not fail closed.");}
        var detail=await guest.GetStringAsync("/bai-viet/"+articleId);var dom=new HtmlParser().ParseDocument(detail);Check(dom.QuerySelector(".agency-answer strong")?.TextContent=="Trả lời của Cục Quản lý đất đai"&&dom.QuerySelector(".agency-answer script")==null&&dom.QuerySelector(".agency-answer p")?.TextContent=="<script>answer-encoded</script>","Official label or public answer XSS encoding failed.");
        var audit=await Html(admin,"/admin/audit?eventCode="+Uri.EscapeDataString(marker));Check(audit.Contains(marker)&&!audit.Contains("PRIVATE-DETAIL-MUST-NOT-RENDER"),"Audit filter or private detail exclusion failed.");
        foreach(var filter in new[]{"from=0001-01-01","to=9999-12-31","from=2026-12-01&to=2026-01-01","from=invalid"}){using var invalid=await admin.GetAsync("/admin/audit?"+filter);Check(invalid.StatusCode==HttpStatusCode.BadRequest,"Audit invalid date did not fail closed.");}
        using(var r=await guest.GetAsync("/admin/audit"))Check(r.StatusCode==HttpStatusCode.Redirect,"Anonymous audit accepted.");
        // Current permissions must be checked again on each request using the
        // same cookie. Removing a fixture role claim cannot be defeated by URL.
        await using(var db=new PortalDbContext(options))
        {
            var claim=await db.UserClaims.SingleAsync(x=>x.UserId==userId&&x.ClaimType=="permission"&&x.ClaimValue==Permissions.ManageRoles);db.UserClaims.Remove(claim);await db.SaveChangesAsync();
        }
        using(var r=await admin.GetAsync("/admin/audit"))Check(r.StatusCode==HttpStatusCode.Forbidden,"Revoked administrator permission retained audit access.");
        await using(var db=new PortalDbContext(options))
        {
            db.UserClaims.Add(new(){UserId=userId,ClaimType="permission",ClaimValue=Permissions.ManageRoles});
            db.UserClaims.Add(new(){UserId=userId,ClaimType="content.category.scope",ClaimValue="true"});db.UserClaims.Add(new(){UserId=userId,ClaimType="content.category",ClaimValue="75"});await db.SaveChangesAsync();
        }
        var scoped=await Html(admin,"/admin/tuong-tac?kind=Comment&contentId="+otherId);Check(Ids(scoped).Length==0&&!scoped.Contains(unrelated.ToString()),"Article filter bypassed category scope.");
        var scopedAdmin=await Html(admin,"/admin?q="+Uri.EscapeDataString(marker));Check(ArticleLinks(scopedAdmin).SequenceEqual(new[]{"/admin/edit/"+articleId}),"Admin identifier paging bypassed current category scope.");
        using(var r=await admin.GetAsync("/admin/audit"))Check(r.StatusCode==HttpStatusCode.Forbidden,"Scoped administrator read global audit.");
        await using(var db=new PortalDbContext(options)){await db.UserClaims.Where(x=>x.UserId==userId&&(x.ClaimType=="content.category.scope"||x.ClaimType=="content.category")).ExecuteDeleteAsync();}
        var directory=TestEvidencePaths.At(PortalConfiguration.FindWorkspace(),"p0-inbox-audit");Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory,"readback.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",articleId,otherId,comments=expected.Count,firstPage=20,secondPage=1,articleScope="PASS",revocation="PASS",publicLabelEncoding="PASS",auditPrivacy="PASS",fixtureOnly=true}));
    }
}
