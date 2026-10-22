using System.Data;
using System.Net.Sockets;
using System.Text;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Infrastructure;
public sealed class InteractionMailOptions
{
    public bool Available { get; init; }
    public int SinkPort { get; init; }
    public string? SmtpHost { get; init; }
    public string From { get; init; }="no-reply@landportal.invalid";
    public string? UserName { get; init; }
    public string? Password { get; init; }
    public static InteractionMailOptions FromConfiguration(IConfiguration c,string environment)
    {
        if(c["Mail:Transport"] is null or "Disabled")return new();
        if(c["Mail:Transport"]=="Smtp")
        {
            if(!UatDeployment.Validate(c,environment)||c["Mail:PolicyReference"] is not{Length:>4})throw new InvalidOperationException("SMTP requires validated UAT and approved relay policy.");
            if(c["Mail:DeliveryEnabled"]!="true")return new();
            if(c["Mail:Host"] is not{Length:>0} host||Uri.CheckHostName(host)==UriHostNameType.Unknown||!int.TryParse(c["Mail:Port"],out var smtpPort)||smtpPort is <1 or >65535||!System.Net.Mail.MailAddress.TryCreate(c["Mail:From"],out var from)||from.Address!=c["Mail:From"]||c["Mail:From"]!.Any(char.IsControl)||c["Mail:Tls"]!="StartTls"||string.IsNullOrEmpty(c["Mail:UserName"])!=string.IsNullOrEmpty(c["Mail:Password"]))throw new InvalidOperationException("Explicit SMTP host/port/from/STARTTLS and paired credentials required.");
            return new(){Available=true,SmtpHost=host,SinkPort=smtpPort,From=from.Address,UserName=c["Mail:UserName"],Password=c["Mail:Password"]};
        }
        if(c["Mail:Transport"]!="TestSink"||environment!="Development"||c["Sql:ExpectedDatabase"]!="LandPortal_Test"||!Uri.TryCreate(c["Public:BaseUrl"],UriKind.Absolute,out var uri)||!uri.IsLoopback||!int.TryParse(c["Mail:SinkPort"],out var port)||port is <1024 or >65535)
            throw new InvalidOperationException("TestSink chỉ cho Development local, LandPortal_Test và cổng loopback tường minh. SMTP thật chưa triển khai.");
        var storage=StorageOptions.FromConfiguration(c);var prefix=Path.Combine(c["WorkspaceRoot"]!,".local","tests")+Path.DirectorySeparatorChar;
        if(new[]{storage.MediaRoot,storage.TempRoot,storage.KeysRoot}.Any(x=>!Path.GetFullPath(x).StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Test sink requires isolated project Test storage.");
        return new(){Available=true,SinkPort=port};
    }
    public void RequireAvailable(){if(!Available)throw new InvalidOperationException("Chưa cấu hình dịch vụ gửi thư được phép. Không báo gửi thư thành công khi chưa có kết quả SMTP.");}
}
public sealed class InteractionOutboxService(PortalDbContext db,IDataProtectionProvider protection,InteractionMailOptions options,CurrentPermissionService permissions,TimeProvider? clock=null)
{
    private DateTimeOffset Now=>(clock??TimeProvider.System).GetUtcNow();
    public async Task<bool> RunOnceAsync(Guid? onlyId=null)
    {
        options.RequireAvailable();var now=Now;InteractionOutbox? row;
        await using(var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='interaction-outbox-claim',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000; IF @r<0 THROW 51000,'Outbox lock unavailable',1;");
            // Recovery cannot prove whether a crashed sender obtained SMTP acceptance.
            await db.InteractionOutbox.Where(x=>x.State==InteractionMailState.Leased&&x.LeaseUntilUtc<=now).ExecuteUpdateAsync(x=>x.SetProperty(m=>m.State,InteractionMailState.Uncertain).SetProperty(m=>m.ErrorCode,"LeaseExpiredAcceptanceUnknown"));
            var rows=db.InteractionOutbox.Where(x=>x.State==InteractionMailState.Pending&&x.AvailableUtc<=now);if(onlyId!=null)rows=rows.Where(x=>x.Id==onlyId);
            row=await rows.OrderBy(x=>x.AvailableUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync();
            if(row==null){await tx.CommitAsync();return false;}
            if(row.ChallengeId is Guid challenge&&!await db.InteractionChallenges.AnyAsync(x=>x.Id==challenge&&!x.Revoked&&x.SubmissionId==null&&x.ExpiresUtc>now&&x.FailedAttempts<5))
            {row.State=InteractionMailState.Cancelled;await db.SaveChangesAsync();await tx.CommitAsync();return true;}
            if(row.LookupChallengeId is Guid lookup&&!await db.InquiryLookupChallenges.AnyAsync(x=>x.Id==lookup&&x.Purpose=="INQUIRY_LOOKUP"&&!x.Revoked&&x.ConsumedUtc==null&&x.ExpiresUtc>now&&x.FailedAttempts<5&&x.SubmissionId!=null))
            {row.State=InteractionMailState.Cancelled;await db.SaveChangesAsync();await tx.CommitAsync();return true;}
            if(row.RequestedBy is string actorId&&row.SubmissionId is Guid id)
            {
                var actor=await permissions.GetAsync(actorId);var service=new InteractionService(db,protection,options,permissions,clock);
                if(!actor.Has(Permissions.InquiryRespond)||!actor.Has(Permissions.InquiryRead)||!await service.Inbox(actor,InteractionKind.Inquiry).AnyAsync(x=>x.Id==id))
                {row.State=InteractionMailState.Cancelled;row.ErrorCode="PermissionRevoked";await db.SaveChangesAsync();await tx.CommitAsync();return true;}
            }
            row.State=InteractionMailState.Leased;row.LeaseToken=Guid.NewGuid();row.LeaseUntilUtc=now.AddMinutes(2);row.Attempts++;await db.SaveChangesAsync();await tx.CommitAsync();
        }
        var token=row.LeaseToken;var dataStarted=false;
        try
        {
            var p=protection.CreateProtector("LandPortal.Interaction.Private.v1");var mail=System.Text.Json.JsonSerializer.Deserialize<InteractionMail>(p.Unprotect(row.ProtectedMail))!;
            using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(15));var ct=cancel.Token;
            using var client=new TcpClient();if(options.SmtpHost==null)await client.ConnectAsync(System.Net.IPAddress.Loopback,options.SinkPort,ct);else await client.ConnectAsync(options.SmtpHost,options.SinkPort,ct);
            Stream stream=client.GetStream();var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true);var writer=new StreamWriter(stream,new UTF8Encoding(false),1024,true){NewLine="\r\n",AutoFlush=true};
            try{
            async Task<string> Expect(int status){var lines=new List<string>();for(var i=0;i<50;i++){var line=await reader.ReadLineAsync(ct);if(line==null||line.Length>4096||!line.StartsWith(status.ToString(),StringComparison.Ordinal)||line.Length>3&&line[3] is not(' ' or '-'))throw new IOException("SmtpProtocolRejected");lines.Add(line);if(line.Length<=3||line[3]!='-')return string.Join('\n',lines);}throw new IOException("SmtpReplyLimit");}
            async Task<string> Command(string line,int status){await writer.WriteLineAsync(line.AsMemory(),ct);return await Expect(status);}
            await Expect(220);var capabilities=await Command("EHLO landportal",250);
            if(options.SmtpHost!=null){
                if(!capabilities.Split('\n').Any(x=>x.Length>4&&x[4..].Trim().Equals("STARTTLS",StringComparison.OrdinalIgnoreCase)))throw new IOException("StartTlsRequired");
                await Command("STARTTLS",220);reader.Dispose();await writer.DisposeAsync();
                var tls=new System.Net.Security.SslStream(stream,leaveInnerStreamOpen:false);stream=tls;
                await tls.AuthenticateAsClientAsync(new System.Net.Security.SslClientAuthenticationOptions{TargetHost=options.SmtpHost,EnabledSslProtocols=System.Security.Authentication.SslProtocols.Tls12|System.Security.Authentication.SslProtocols.Tls13,CertificateRevocationCheckMode=System.Security.Cryptography.X509Certificates.X509RevocationMode.Online},ct);
                reader=new StreamReader(stream,Encoding.UTF8,false,1024,true);writer=new StreamWriter(stream,new UTF8Encoding(false),1024,true){NewLine="\r\n",AutoFlush=true};
                capabilities=await Command("EHLO landportal",250);
                if(!string.IsNullOrEmpty(options.UserName)){if(!capabilities.Contains("AUTH",StringComparison.OrdinalIgnoreCase)||!capabilities.Contains("PLAIN",StringComparison.OrdinalIgnoreCase))throw new IOException("ApprovedSmtpAuthUnavailable");await Command("AUTH PLAIN "+Convert.ToBase64String(Encoding.UTF8.GetBytes("\0"+options.UserName+"\0"+options.Password)),235);}
            }
            if(!System.Net.Mail.MailAddress.TryCreate(mail.Email,out var recipient)||recipient.Address!=mail.Email||mail.Email.Any(char.IsControl))throw new IOException("RecipientEnvelopeInvalid");
            await Command("MAIL FROM:<"+options.From+">",250);await Command("RCPT TO:<"+mail.Email+">",250);await Command("DATA",354);
            var subject=Convert.ToBase64String(Encoding.UTF8.GetBytes(mail.Subject));var encodedBody=Convert.ToBase64String(Encoding.UTF8.GetBytes(mail.Body));var body=string.Join("\r\n",Enumerable.Range(0,(encodedBody.Length+75)/76).Select(i=>encodedBody.Substring(i*76,Math.Min(76,encodedBody.Length-i*76))));dataStarted=true;
            await writer.WriteLineAsync(($"Message-ID: <{row.Id}@{new System.Net.Mail.MailAddress(options.From).Host}>\r\nFrom: {options.From}\r\nTo: {mail.Email}\r\nSubject: =?UTF-8?B?{subject}?=\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n{body}\r\n.").AsMemory(),ct);
            await Expect(250);row.State=InteractionMailState.Accepted;row.AcceptedUtc=Now;row.ErrorCode="";
            }finally{reader.Dispose();await writer.DisposeAsync();await stream.DisposeAsync();}
        }
        catch(Exception error) when(error is IOException or SocketException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
        {row.State=dataStarted?InteractionMailState.Uncertain:row.Attempts>=5?InteractionMailState.Failed:InteractionMailState.Pending;row.ErrorCode=dataStarted?"SmtpAcceptanceUnknown":"SinkUnavailable";row.AvailableUtc=Now.AddSeconds(Math.Min(900,Math.Pow(2,row.Attempts)*15));}
        var state=row.State;var accepted=row.AcceptedUtc;var available=row.AvailableUtc;var errorCode=row.ErrorCode;db.ChangeTracker.Clear();
        var changed=await db.InteractionOutbox.Where(x=>x.Id==row.Id&&x.State==InteractionMailState.Leased&&x.LeaseToken==token).ExecuteUpdateAsync(x=>x.SetProperty(m=>m.State,state).SetProperty(m=>m.AcceptedUtc,accepted).SetProperty(m=>m.AvailableUtc,available).SetProperty(m=>m.ErrorCode,errorCode).SetProperty(m=>m.LeaseUntilUtc,(DateTimeOffset?)null));
        if(changed!=1)throw new InvalidOperationException("Outbox lease lost; SMTP acceptance requires reconciliation.");return true;
    }
}
