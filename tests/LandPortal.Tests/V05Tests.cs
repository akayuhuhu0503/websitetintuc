using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;
public static class V05Tests
{
    private sealed class Clock:TimeProvider{public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
    private static void Check(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static async Task Denied<T>(Func<Task> action)where T:Exception{try{await action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    private static string Answer(PublicCaptchaPrompt c){var n=System.Text.RegularExpressions.Regex.Matches(c.Question,@"\d+").Select(x=>int.Parse(x.Value,CultureInfo.InvariantCulture)).ToArray();return(c.Question.Contains("cộng")?n[0]+n[1]:n[0]-n[1]).ToString(CultureInfo.InvariantCulture);}
    private static IDataProtectionProvider Protection(IConfiguration c)=>DataProtectionProvider.Create(new DirectoryInfo(StorageOptions.FromConfiguration(c).KeysRoot),x=>x.SetApplicationName("LandPortal"));
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(string root,DbContextOptions<PortalDbContext> options,IConfiguration config)
    {
        foreach (var test in V05FinishTests.Cases(root, options, config)) yield return test;
        yield return ("V05-Finish actual HTTP login CAPTCHA documents Word revision ACL and responsive admin", () => V05FinishHttpTests.RunAsync(root, config, options));
        yield return("V05-01 trusted canonical origins host-qualified aliases unknown origin and no open redirect",async()=>
        {
            var local=new PublicUrlPolicy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Public:BaseUrl"]="http://127.0.0.1:5269"}).Build());
            var live=new PublicUrlPolicy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Public:BaseUrl"]="https://gdla.gov.vn/",["Public:CanonicalHost"]="gdla.gov.vn"}).Build());
            Check(local.Absolute("/bai-viet/test")=="http://127.0.0.1:5269/bai-viet/test"&&live.Absolute("/bai-viet/test")=="https://gdla.gov.vn/bai-viet/test","Canonical not configuration-bound.");
            foreach(var path in new[]{"//attacker.invalid/a","https://attacker.invalid/","/\\attacker.invalid"})await Denied<ArgumentException>(()=>Task.FromResult(local.Absolute(path)));
            Check(!live.AllowsHost("attacker.invalid")&&live.LegacyOrigin("qldd.mae.gov.vn")=="qldd.mae.gov.vn"&&live.LegacyOrigin("gdla.gov.vn")=="gdla.gov.vn","Host mapping conflated.");
            await using var db=new PortalDbContext(options);var ids=await db.Contents.OrderBy(x=>x.Id).Select(x=>x.Id).Take(2).ToArrayAsync();var pathId="/v05-alias-"+Guid.NewGuid().ToString("N")+".htm";
            foreach(var (host,id) in new[]{("qldd.mae.gov.vn",ids[0]),("gdla.gov.vn",ids[1])}){var url="https://"+host+pathId;db.SourceUrlAliases.Add(new(){ContentId=id,Url=url,UrlHash=TextNormalizer.Hash(url)});}await db.SaveChangesAsync();
            foreach(var (host,id) in new[]{("qldd.mae.gov.vn",ids[0]),("gdla.gov.vn",ids[1])}){var url="https://"+host+pathId;Check(await db.SourceUrlAliases.Where(x=>x.UrlHash==TextNormalizer.Hash(url)&&x.Url==url).Select(x=>x.ContentId).SingleAsync()==id,"Same path collided across origins.");}
        });
        yield return("V05-02 SQL CAPTCHA one-use wrong expired missing session purpose binding and pending without SMTP",async()=>
        {
            await using var db=new PortalDbContext(options);var clock=new Clock();var opts=new PublicCaptchaOptions{AddressKey=RandomNumberGenerator.GetBytes(32),AttemptsPerWindow=20};var p=Protection(config);var captcha=new PublicCaptchaService(db,p,opts,clock);var service=new InteractionService(db,p,new(),new(db),clock);var session=Guid.NewGuid().ToString();const string address="anonymous-unit-address";
            var input=new InteractionInput{Kind=InteractionKind.Contact,Name="V05 Test",Email="qa@landportal.invalid",Title="=Test formula",Text="Private V05 fixture"};var before=await db.InteractionSubmissions.CountAsync();
            var c=await captcha.IssueAsync(session,address,"Contact");await Denied<PublicCaptchaException>(()=>service.SubmitCaptchaAsync(captcha,c.Id,"999",input,session,address));await Denied<PublicCaptchaException>(()=>service.SubmitCaptchaAsync(captcha,c.Id,Answer(c),input,session,address));
            c=await captcha.IssueAsync(session,address,"Contact");await Denied<PublicCaptchaException>(()=>captcha.ConsumeAsync(c.Id,Answer(c),"foreign-session",address,"Contact"));await Denied<PublicCaptchaException>(()=>captcha.ConsumeAsync(c.Id,Answer(c),session,address,"Inquiry"));
            Check(await db.InteractionSubmissions.CountAsync()==before,"Invalid CAPTCHA created SQL submission.");
            c=await captcha.IssueAsync(session,address,"Contact");clock.Now=clock.Now.AddMinutes(6);await Denied<PublicCaptchaException>(()=>captcha.ConsumeAsync(c.Id,Answer(c),session,address,"Contact"));await Denied<PublicCaptchaException>(()=>captcha.ConsumeAsync(Guid.Empty,"",session,address,"Contact"));
            c=await captcha.IssueAsync(session,address,"Contact");var receipt=await service.SubmitCaptchaAsync(captcha,c.Id,Answer(c),input,session,address);Check(receipt.PublicCode?.Length==32&&!receipt.MailQueued,"Immediate receipt/mail honesty invalid.");await Denied<PublicCaptchaException>(()=>service.SubmitCaptchaAsync(captcha,c.Id,Answer(c),input,session,address));
            var row=await db.InteractionSubmissions.SingleAsync(x=>x.Id==receipt.Id);Check(row.State==InteractionState.Pending&&row.PublicSnapshotId==null&&await db.InteractionSubmissions.CountAsync()==before+1&&!await db.InteractionOutbox.AnyAsync(x=>x.SubmissionId==row.Id)&&!await db.InteractionChallenges.AnyAsync(x=>x.Id==c.Id),"CAPTCHA created OTP, public output, duplicates or fake mail.");
        });
        yield return("V05-02 SQL default five attempts ten minutes and competing challenge consumption across contexts",async()=>
        {
            var p=Protection(config);var opts=new PublicCaptchaOptions{AddressKey=RandomNumberGenerator.GetBytes(32)};var session=Guid.NewGuid().ToString();var address=Guid.NewGuid().ToString();var clock=new Clock();
            await using(var db=new PortalDbContext(options)){var s=new PublicCaptchaService(db,p,opts,clock);for(var i=0;i<5;i++){var c=await s.IssueAsync(session,address,"Contact");await Denied<PublicCaptchaException>(()=>s.ConsumeAsync(c.Id,"999",session,address,"Contact"));}await Denied<PublicCaptchaException>(()=>s.IssueAsync(session,address,"Contact"));clock.Now=clock.Now.AddMinutes(10).AddSeconds(1);var c2=await s.IssueAsync(session,address,"Contact");await s.ConsumeAsync(c2.Id,Answer(c2),session,address,"Contact");}
            address=Guid.NewGuid().ToString();PublicCaptchaPrompt prompt;await using(var db=new PortalDbContext(options))prompt=await new PublicCaptchaService(db,p,opts).IssueAsync(session,address,"Contact");
            async Task<bool> Consume(){await using var db=new PortalDbContext(options);try{await new PublicCaptchaService(db,p,opts).ConsumeAsync(prompt.Id,Answer(prompt),session,address,"Contact");return true;}catch(PublicCaptchaException){return false;}}
            var results=await Task.WhenAll(Consume(),Consume());Check(results.Count(x=>x)==1,"CAPTCHA race produced more than one consumption.");
        });
        yield return("V05-05 FR17 additive groups exact approval draft metadata isolation denied direct and no comments",async()=>
        {
            await using var db=new PortalDbContext(options);Check(await db.DisclosureGroups.CountAsync()==3&&await db.DisclosureGroups.CountAsync(x=>x.Status=="NeedsNameApproval")==1,"FR17 seed duplicated or assumption approved.");
            var user=await InquiryFinishTests.User(db,Permissions.All,"V05 FR17 Test");var actor=await new CurrentPermissionService(db).GetAsync(user);var wf=new WorkflowService(db,new HtmlPolicy(),MediaUploadOptions.FromConfiguration(config,"Development"),StorageOptions.FromConfiguration(config));
            var input=Phase2aSqlTests.Input("V05 FR17 fixture "+Guid.NewGuid().ToString("N"));input.Metadata=new(){DisclosureGroupId="giay-chung-nhan-sai-hong-huy",Province="Tỉnh thử nghiệm (fixture)",ProvinceCode="TEST",DisclosureYear=2026,PublicationSource="SQL Test only; no official personal data",LegalBasisReference="Fixture test evidence, not an actual legal determination",PublicationReviewEvidence="QA anonymous fixture review"};
            var saved=await wf.SaveAsync(null,input,actor,null,Guid.NewGuid());await Denied<UnauthorizedAccessException>(()=>wf.PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid()));
            saved=await wf.SubmitAsync(saved.ContentId,saved.CurrentRevisionId,actor,saved.RowVersion,Guid.NewGuid());saved=await wf.ApproveAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());saved=await wf.PublishApprovedAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());var old=saved.PublishedRevisionId;
            var interactions=new InteractionService(db,Protection(config),new(),new(db));Check(!await interactions.CanReceiveAsync(InteractionKind.Comment,saved.ContentId),"FR17 accepted comments.");
            saved=await wf.BeginEditAsync(saved.ContentId,actor,saved.RowVersion,Guid.NewGuid());input.Metadata.Province="Private draft province";saved=await wf.SaveAsync(saved.ContentId,input,actor,saved.RowVersion,Guid.NewGuid());
            Check((await db.Revisions.AsNoTracking().SingleAsync(x=>x.Id==old)).Metadata.Province=="Tỉnh thử nghiệm (fixture)"&&!await new PublicContentQuery(db).Filter(new PublicContentQuery(db).Published(),"Private draft province").AnyAsync(x=>x.Id==saved.ContentId),"Draft FR17 metadata leaked public.");
            input.Metadata.DisclosureGroupId="nguoi-nuoc-ngoai-dinh-cu-o-viet-nam";saved=await wf.SaveAsync(saved.ContentId,input,actor,saved.RowVersion,Guid.NewGuid());saved=await wf.SubmitAsync(saved.ContentId,saved.CurrentRevisionId,actor,saved.RowVersion,Guid.NewGuid());saved=await wf.ApproveAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());await Denied<InvalidOperationException>(()=>wf.PublishApprovedAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid()));
        });
        yield return("V05-06 manual weekly immutable v1 v2 restore v3 duplicate week conflict and invalid dates keep live",async()=>
        {
            await using var db=new PortalDbContext(options);var actor=await new CurrentPermissionService(db).GetAsync(await InquiryFinishTests.User(db,Permissions.All,"V05 weekly Test"));var wf=new WorkflowService(db,new HtmlPolicy(),MediaUploadOptions.FromConfiguration(config,"Development"),StorageOptions.FromConfiguration(config));var year=2100;var week=1;
            while(await db.WeeklyScheduleReleases.AnyAsync(x=>x.IsoYear==year&&x.IsoWeek==week)){week++;if(week>52){week=1;year++;}}
            var monday=DateOnly.FromDateTime(ISOWeek.ToDateTime(year,week,DayOfWeek.Monday));var input=Phase2aSqlTests.Input("V05 weekly fixture");input.Kind=ContentKind.StaticPage;input.PrimaryCategoryId=76;input.Metadata=new(){WeeklyYear=year,WeeklyWeek=week,WeeklyChangeNote="Fixture v1",WeeklyRowsJson=JsonSerializer.Serialize(new[]{new WeeklyScheduleRow{Date=monday,Period="Sáng",Time="08:00",Content="Anonymous fixture v1",Chair="QA",Location="Local Test"}})};
            var r=await wf.SaveAsync(null,input,actor,null,Guid.NewGuid());r=await wf.PublishDirectAsync(r.ContentId,r.CurrentRevisionId!.Value,actor,r.RowVersion,Guid.NewGuid());var v1=r.PublishedRevisionId!.Value;
            r=await wf.BeginEditAsync(r.ContentId,actor,r.RowVersion,Guid.NewGuid());input.Metadata.WeeklyChangeNote="Fixture v2";input.Metadata.WeeklyRowsJson=input.Metadata.WeeklyRowsJson.Replace("fixture v1","fixture v2");r=await wf.SaveAsync(r.ContentId,input,actor,r.RowVersion,Guid.NewGuid());Check((await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==r.ContentId)).PublishedRevisionId==v1,"Weekly draft changed public.");r=await wf.PublishDirectAsync(r.ContentId,r.CurrentRevisionId!.Value,actor,r.RowVersion,Guid.NewGuid());
            r=await wf.RestoreRevisionAsync(r.ContentId,v1,actor,r.RowVersion,Guid.NewGuid());r=await wf.PublishDirectAsync(r.ContentId,r.CurrentRevisionId!.Value,actor,r.RowVersion,Guid.NewGuid());var releases=await db.WeeklyScheduleReleases.AsNoTracking().Where(x=>x.ContentId==r.ContentId).OrderBy(x=>x.Version).ToArrayAsync();Check(releases.Select(x=>x.Version).SequenceEqual(new[]{1,2,3})&&releases[2].RevisionId!=v1&&(await db.Revisions.AsNoTracking().SingleAsync(x=>x.Id==v1)).BodyHtml.Contains("fixture v1"),"Weekly restore overwrote immutable release.");
            var duplicate=await wf.SaveAsync(null,input,actor,null,Guid.NewGuid());await Denied<InvalidOperationException>(()=>wf.PublishDirectAsync(duplicate.ContentId,duplicate.CurrentRevisionId!.Value,actor,duplicate.RowVersion,Guid.NewGuid()));
            var invalid=input.Metadata.Copy();invalid.WeeklyRowsJson=JsonSerializer.Serialize(new[]{new WeeklyScheduleRow{Date=monday.AddDays(8),Period="Sáng",Content="wrong week"}});await Denied<InvalidOperationException>(()=>Task.Run(()=>V05ContentPolicy.Validate(invalid,ContentKind.StaticPage)));Check((await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==r.ContentId)).PublishedRevisionId==r.PublishedRevisionId,"Invalid week altered live.");
        });
        yield return("V05-04 public inquiry keyword field SQL paging excludes pending hidden and private originals",async()=>
        {
            await using var db=new PortalDbContext(options);var service=new InteractionService(db,Protection(config),new(),new(db));var field="V05 public fixture "+Guid.NewGuid().ToString("N");var submissions=Enumerable.Range(0,24).Select(i=>new InteractionSubmission{ChallengeId=Guid.NewGuid(),Kind=InteractionKind.Inquiry,InquiryField=field,EmailHash=TextNormalizer.Hash(Guid.NewGuid().ToString()),ReceiptHash=TextNormalizer.Hash(Guid.NewGuid().ToString()),ProtectedOriginal=service.Protect("Never expose original private fixture"),PublicConsent=true,CreatedUtc=DateTimeOffset.UtcNow}).ToArray();db.InteractionSubmissions.AddRange(submissions);await db.SaveChangesAsync();
            var snapshots=submissions.Take(23).Select((s,i)=>new InteractionPublicSnapshot{SubmissionId=s.Id,DisplayName="QA public fixture",Text="Approved public V05 question "+i,AgencyAnswer="Approved answer",ApprovedBy="QA fixture",ApprovedUtc=DateTimeOffset.UtcNow.AddSeconds(-i)}).ToArray();db.InteractionPublicSnapshots.AddRange(snapshots);await db.SaveChangesAsync();foreach(var snap in snapshots)submissions.Single(s=>s.Id==snap.SubmissionId).PublicSnapshotId=snap.Id;await db.SaveChangesAsync();
            var first=await service.PublicInquiryPageAsync("public V05 question",field);var next=await service.PublicInquiryPageAsync("public V05 question",field,2);Check(first.Total==23&&first.Items.Count==20&&next.Items.Count==3&&first.Items.Select(x=>x.Text).Intersect(next.Items.Select(x=>x.Text)).Count()==0,"Public question SQL paging or filters incorrect.");Check((await service.PublicInquiryPageAsync("Never expose original",field)).Total==0,"Search leaked private original.");submissions[0].PublicSnapshotId=null;await db.SaveChangesAsync();Check((await service.PublicInquiryPageAsync("public V05 question",field)).Total==22,"Hidden answer remained public.");
        });
        yield return("V05-11 native short-process exit monitoring race keeps memory limit and verified output",async()=>
        {
            var c=VideoTests.Config(config);var vo=VideoOptions.FromConfiguration(c,"Development");var storage=StorageOptions.FromConfiguration(c);var input=Path.Combine(root,"tests","fixtures","video","variable-fps-anonymous.webm");var rows=new List<object>();var processor=new VideoProcessor(vo,storage);Check((await processor.HealthAsync()).Ready,"Native tools unavailable; not PASS.");
            for(var i=0;i<6;i++){var output=await processor.ProcessAsync(Guid.NewGuid(),input);Check(VideoProcessor.BrowserCompatible(output.Probe),"Native output verification failed.");rows.Add(new{iteration=i,output.Mp4Hash,output.NativeCpuMilliseconds,output.Probe.Codec});}
            var dir=TestEvidencePaths.At(root,"v05-native-exit-race");Directory.CreateDirectory(dir);await File.WriteAllTextAsync(Path.Combine(dir,"proof.json"),JsonSerializer.Serialize(new{nativeOnly=true,sourceInput="committed anonymous technical fixture",publicReadyNotClaimed=true,rows}));
        });
    }
}
