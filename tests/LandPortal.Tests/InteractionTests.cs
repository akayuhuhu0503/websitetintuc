using System.Net;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Tests;
public static class InteractionTests
{
    private static void Assert(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    private static async Task Deny(Func<Task> work){try{await work();}catch(Exception e)when(e is InvalidOperationException or UnauthorizedAccessException or DbUpdateConcurrencyException){return;}throw new InvalidOperationException("Expected denial.");}
    private sealed class Clock : TimeProvider{public DateTimeOffset Utc=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Utc;}
    private static string Code(LocalMailSink sink,Guid mail)=>Regex.Match(sink.Bodies[mail],@"Mã xác minh: (\d{6})").Groups[1].Value;
    private static InteractionInput Input(InteractionKind kind,Guid? id=null,Guid? revision=null)=>new(){Kind=kind,ContentId=id,PublishedRevisionId=revision,Name="Người gửi Test",Email="qa-"+Guid.NewGuid().ToString("N")+"@landportal.invalid",Title="Câu hỏi Test riêng",Text="Nội dung gốc riêng tư cần kiểm duyệt <script>test</script>",PublicConsent=kind is InteractionKind.Comment or InteractionKind.Inquiry};
    public static IEnumerable<(string Name,Func<Task> Test)> Cases(string root,DbContextOptions<PortalDbContext> options,IConfiguration config)
    {
        var evidence=TestEvidencePaths.At(root,"interactions-sql");
        yield return("2C OTP real sink binding wrong code attempts expiry resend throttle consume and privacy",async()=>
        {
            Directory.CreateDirectory(evidence);await using var sink=new LocalMailSink(Path.Combine(evidence,"mail"));await using var db=new PortalDbContext(options);var clock=new Clock();
            var protection=DataProtectionProvider.Create(new DirectoryInfo(StorageOptions.FromConfiguration(config).KeysRoot),b=>b.SetApplicationName("LandPortal"));var mail=new InteractionMailOptions{Available=true,SinkPort=sink.Port};var service=new InteractionService(db,protection,mail,new(db),clock);
            var input=Input(InteractionKind.Contact);var session=Guid.NewGuid().ToString();var challenge=await service.RequestAsync(input,session);var message=await db.InteractionOutbox.SingleAsync(x=>x.ChallengeId==challenge);
            await new InteractionOutboxService(db,protection,mail,new(db),clock).RunOnceAsync(message.Id);var code=Code(sink,message.Id);Assert(code.Length==6,"Real sink did not receive an OTP.");
            foreach(var mutation in new[]{"session","email","purpose","payload","target"})
            {
                var changed=JsonSerializer.Deserialize<InteractionInput>(JsonSerializer.Serialize(input))!;
                if(mutation=="email")changed.Email="different@landportal.invalid";if(mutation=="purpose")changed.Kind=InteractionKind.Inquiry;if(mutation=="payload")changed.Text+="changed";if(mutation=="target")changed.ContentId=Guid.NewGuid();
                await Deny(()=>service.SubmitAsync(challenge,changed,mutation=="session"?"other":session,code));db.ChangeTracker.Clear();
            }
            for(var i=0;i<5;i++){await Deny(()=>service.SubmitAsync(challenge,input,session,code=="000000"?"999999":"000000"));db.ChangeTracker.Clear();}
            await Deny(()=>service.SubmitAsync(challenge,input,session,code));db.ChangeTracker.Clear();
            Assert((await db.InteractionChallenges.FindAsync(challenge))!.FailedAttempts==5,"Failed-attempt counter rolled back.");db.ChangeTracker.Clear();
            clock.Utc=clock.Utc.AddSeconds(61);var resent=await service.RequestAsync(input,session);message=await db.InteractionOutbox.SingleAsync(x=>x.ChallengeId==resent);await new InteractionOutboxService(db,protection,mail,new(db),clock).RunOnceAsync(message.Id);code=Code(sink,message.Id);
            await Deny(()=>service.SubmitAsync(challenge,input,session,code));db.ChangeTracker.Clear();
            var result=await service.SubmitAsync(resent,input,session,code);db.ChangeTracker.Clear();var retry=await service.SubmitAsync(resent,input,session,code);
            Assert(result.Id==retry.Id&&retry.AlreadyApplied&&await db.InteractionSubmissions.CountAsync(x=>x.ChallengeId==resent)==1,"Retry duplicated submission.");db.ChangeTracker.Clear();
            var expiredInput=Input(InteractionKind.Contact);var expired=await service.RequestAsync(expiredInput,session);message=await db.InteractionOutbox.SingleAsync(x=>x.ChallengeId==expired);await new InteractionOutboxService(db,protection,mail,new(db),clock).RunOnceAsync(message.Id);var expiredCode=Code(sink,message.Id);clock.Utc=clock.Utc.AddMinutes(5);
            await Deny(()=>service.SubmitAsync(expired,expiredInput,session,expiredCode));db.ChangeTracker.Clear();
            for(var i=0;i<3;i++){clock.Utc=clock.Utc.AddSeconds(61);await service.RequestAsync(input,session);}clock.Utc=clock.Utc.AddSeconds(61);await Deny(()=>service.RequestAsync(input,session));db.ChangeTracker.Clear();
            Assert(!JsonSerializer.Serialize(await db.InteractionSubmissions.SingleAsync(x=>x.Id==result.Id)).Contains(input.Email),"Plaintext PII in SQL submission.");
            Assert(await db.InteractionSubmissions.CountAsync(x=>x.Kind==InteractionKind.Contact&&x.Id==result.Id)==1,"Wrong private inbox kind.");
            await Deny(()=>new InteractionService(db,protection,new(),new(db)).RequestAsync(Input(InteractionKind.Contact),session));
        });
        yield return("2C outbox real TCP failure backoff retry competing claims acceptance uncertainty and restart",async()=>
        {
            var folder=TestEvidencePaths.At(root,"outbox-sql");Directory.CreateDirectory(folder);await using var sink=new LocalMailSink(Path.Combine(folder,"mail"));
            var protection=DataProtectionProvider.Create(new DirectoryInfo(StorageOptions.FromConfiguration(config).KeysRoot),b=>b.SetApplicationName("LandPortal"));var mail=new InteractionMailOptions{Available=true,SinkPort=sink.Port};var clock=new Clock();Guid id;
            await using(var db=new PortalDbContext(options)){var s=new InteractionService(db,protection,mail,new(db));var m=new InteractionOutbox{EventKey="probe:"+Guid.NewGuid(),ProtectedMail=s.Protect(new InteractionMail("qa@landportal.invalid","Test outbox","Test only")),AvailableUtc=clock.Utc};db.InteractionOutbox.Add(m);await db.SaveChangesAsync();id=m.Id;}
            async Task Send(InteractionMailOptions transport){await using var db=new PortalDbContext(options);await new InteractionOutboxService(db,protection,transport,new(db),clock).RunOnceAsync(id);}
            // Refused loopback connection is a real transport failure, not a mocked SMTP result.
            await Send(new(){Available=true,SinkPort=1});await using(var db=new PortalDbContext(options)){var m=await db.InteractionOutbox.FindAsync(id);Assert(m!.State==InteractionMailState.Pending&&m.Attempts==1&&m.AvailableUtc>clock.Utc,"Backoff missing.");}
            clock.Utc=clock.Utc.AddMinutes(1);await Task.WhenAll(Send(mail),Send(mail));await using(var db=new PortalDbContext(options)){var m=await db.InteractionOutbox.FindAsync(id);Assert(m!.State==InteractionMailState.Accepted&&m.Attempts==2,"Competing claims duplicated send.");}
            await Send(mail);Assert(sink.Bodies.Count==1,"Accepted email resent.");
            sink.DropAcceptance=true;await using(var db=new PortalDbContext(options)){var s=new InteractionService(db,protection,mail,new(db));var m=new InteractionOutbox{EventKey="uncertain:"+Guid.NewGuid(),ProtectedMail=s.Protect(new InteractionMail("qa@landportal.invalid","Test","Unknown acceptance")),AvailableUtc=clock.Utc};db.InteractionOutbox.Add(m);await db.SaveChangesAsync();id=m.Id;}
            await Send(mail);await using(var db=new PortalDbContext(options)){var m=await db.InteractionOutbox.FindAsync(id);Assert(m!.State==InteractionMailState.Uncertain,"Ambiguous acceptance retried unsafely.");}
            await Send(mail);Assert(sink.Bodies.Count==2,"Uncertain acceptance was resent.");
            await using(var db=new PortalDbContext(options)){var s=new InteractionService(db,protection,mail,new(db));var m=new InteractionOutbox{EventKey="crash:"+Guid.NewGuid(),ProtectedMail=s.Protect(new InteractionMail("qa@landportal.invalid","Test","Crash")),State=InteractionMailState.Leased,LeaseToken=Guid.NewGuid(),LeaseUntilUtc=clock.Utc.AddSeconds(-1),AvailableUtc=clock.Utc};db.InteractionOutbox.Add(m);await db.SaveChangesAsync();id=m.Id;}
            await Send(mail);await using(var db=new PortalDbContext(options))Assert((await db.InteractionOutbox.FindAsync(id))!.State==InteractionMailState.Uncertain,"Restart reset uncertain lease.");
        });
        yield return("2C SQL comment toggles all-category scope consultation draft isolation boundary and unpublish",async()=>
        {
            var folder=TestEvidencePaths.At(root,"eligibility-sql");await using var sink=new LocalMailSink(Path.Combine(folder,"mail"));await using var db=new PortalDbContext(options);var clock=new Clock();var date=DateOnly.FromDateTime(clock.Utc.ToOffset(TimeSpan.FromHours(7)).DateTime);
            var protection=DataProtectionProvider.Create(new DirectoryInfo(StorageOptions.FromConfiguration(config).KeysRoot),b=>b.SetApplicationName("LandPortal"));var mail=new InteractionMailOptions{Available=true,SinkPort=sink.Port};var service=new InteractionService(db,protection,mail,new(db),clock);var workflow=new WorkflowService(db,new HtmlPolicy(),MediaUploadOptions.FromConfiguration(config,"Development"),StorageOptions.FromConfiguration(config));var actor=Phase2aSqlTests.Actor();
            var categories=await db.Categories.OrderBy(x=>x.Id).Select(x=>x.Id).Take(2).ToArrayAsync();var articleInput=Phase2aSqlTests.Input("2C private scope fixture "+Guid.NewGuid(),categories[0]);articleInput.SecondaryCategoryIds=[categories[1]];
            var saved=await workflow.SaveAsync(null,articleInput,actor,null,Guid.NewGuid());var published=await workflow.PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());
            var c=await db.Contents.SingleAsync(x=>x.Id==saved.ContentId);Assert(!await service.CanReceiveAsync(InteractionKind.Comment,c.Id),"Disabled article accepted comments.");c.CommentsEnabled=true;var setting=await db.SiteSettings.FindAsync("Interactions.CommentsEnabled");if(setting==null){setting=new(){Key="Interactions.CommentsEnabled",Value="true"};db.SiteSettings.Add(setting);}else setting.Value="true";await db.SaveChangesAsync();Assert(await service.CanReceiveAsync(InteractionKind.Comment,c.Id),"Enabled article did not open.");
            var comment=Input(InteractionKind.Comment,c.Id,published.PublishedRevisionId);var session=Guid.NewGuid().ToString();var challenge=await service.RequestAsync(comment,session);var m=await db.InteractionOutbox.SingleAsync(x=>x.ChallengeId==challenge);await new InteractionOutboxService(db,protection,mail,new(db),clock).RunOnceAsync(m.Id);var result=await service.SubmitAsync(challenge,comment,session,Code(sink,m.Id));db.ChangeTracker.Clear();
            var moderator=new ContentActor("qa-scope",new HashSet<string>{Permissions.CommentRead,Permissions.CommentModerate}){CategoryIds=new HashSet<int>{categories[0]}};
            Assert(!await service.Inbox(moderator,InteractionKind.Comment).AnyAsync(x=>x.Id==result.Id),"Secondary category scope was ignored.");await Deny(async()=>{var s=await db.InteractionSubmissions.SingleAsync(x=>x.Id==result.Id);await service.ModerateAsync(moderator,s.Id,s.RowVersion,Guid.NewGuid(),"publish","Test","Public","","");});db.ChangeTracker.Clear();
            setting=await db.SiteSettings.FindAsync("Interactions.CommentsEnabled");setting!.Value="false";await db.SaveChangesAsync();await Deny(()=>service.RequestAsync(Input(InteractionKind.Comment,c.Id,published.PublishedRevisionId),session));db.ChangeTracker.Clear();
            var draftInput=Phase2aSqlTests.Input("2C deadline fixture "+Guid.NewGuid(),categories[0]);draftInput.Kind=ContentKind.DraftDocument;draftInput.Metadata=new(){ConsultationStartDate=date.AddDays(-1),ConsultationEndDate=date,ConsultationDateEvidence="Test only"};
            saved=await workflow.SaveAsync(null,draftInput,actor,null,Guid.NewGuid());published=await workflow.PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());
            var edit=await workflow.BeginEditAsync(saved.ContentId,actor,published.RowVersion,Guid.NewGuid());draftInput.Metadata.ConsultationEndDate=date.AddDays(10);await workflow.SaveAsync(saved.ContentId,draftInput,actor,edit.RowVersion,Guid.NewGuid());
            var close=new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(7)).ToUniversalTime();clock.Utc=close.AddSeconds(-2);var feedback=Input(InteractionKind.Consultation,saved.ContentId,published.PublishedRevisionId);challenge=await service.RequestAsync(feedback,session);m=await db.InteractionOutbox.SingleAsync(x=>x.ChallengeId==challenge);await new InteractionOutboxService(db,protection,mail,new(db),clock).RunOnceAsync(m.Id);var code=Code(sink,m.Id);clock.Utc=close;
            await Deny(()=>service.SubmitAsync(challenge,feedback,session,code));db.ChangeTracker.Clear();Assert(!await service.CanReceiveAsync(InteractionKind.Consultation,saved.ContentId),"Draft deadline replaced published window.");
            var current=await db.Contents.SingleAsync(x=>x.Id==saved.ContentId);await workflow.UnpublishAsync(current.Id,actor,current.RowVersion,Guid.NewGuid());Assert(!await service.CanReceiveAsync(InteractionKind.Consultation,current.Id),"Withdrawn draft accepted feedback.");
        });
        yield return("2C consultation inclusive Vietnam-day boundaries use published metadata and deny unknown",()=>
        {
            var m=new ContentMetadata{ConsultationStartDate=new(2026,10,1),ConsultationEndDate=new(2026,10,14)};var close=new DateTimeOffset(2026,10,15,0,0,0,TimeSpan.FromHours(7));
            Assert(m.ConsultationState(close.AddTicks(-1))=="Đang mở"&&m.ConsultationState(close)=="Đã đóng","Vietnam inclusive end boundary incorrect.");Assert(new ContentMetadata().ConsultationState(close)=="Chưa xác định","Unknown deadline was opened.");return Task.CompletedTask;
        });
        yield return("2C Test sink environment policy rejects Dev Staging Production and unknown transports",()=>
        {
            foreach(var environment in new[]{"Staging","Production"}){try{InteractionMailOptions.FromConfiguration(new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{{"Mail:Transport","TestSink"},{"Mail:SinkPort","2525"}}).Build(),environment);}catch(InvalidOperationException){continue;}throw new InvalidOperationException("Sink bypass outside Development.");}return Task.CompletedTask;
        });
    }
}
