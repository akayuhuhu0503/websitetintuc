using System.Buffers;
using System.Buffers.Binary;
using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using LandPortal.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Infrastructure;

public sealed class InquiryFileOptions
{
    public const long MaximumBytes=10*1024*1024;
    public bool Enabled { get; init; }
    public IReadOnlySet<string> MimeTypes { get; init; }=new HashSet<string>{"application/pdf","image/jpeg","image/png"};
    public static InquiryFileOptions FromConfiguration(IConfiguration c,string environment)
    {
        var local=environment=="Development"&&Uri.TryCreate(c["Public:BaseUrl"],UriKind.Absolute,out var uri)&&uri.IsLoopback;
        var allowed=(c["InquiryFiles:MimeTypes"]??"application/pdf,image/jpeg,image/png").Split(',').Select(x=>x.Trim()).ToHashSet(StringComparer.Ordinal);
        if(allowed.Any(x=>x is not("application/pdf" or "image/jpeg" or "image/png")))throw new InvalidOperationException("Chỉ PDF/JPEG/PNG được xác nhận; DOCX và executable không bật.");
        return new(){Enabled=(local||UatDeployment.Validate(c,environment))&&c["InquiryFiles:Enabled"]!="false",MimeTypes=allowed};
    }
}
public sealed record InquiryScanResult(InquiryScanState State,string Error,string? EngineVersion)
{
    public string Provider { get; init; }="Clamd";
    public string? SignatureVersion { get; init; }
    public DateTimeOffset? SignatureUpdatedUtc { get; init; }
    public string? OutputSha256 { get; init; }
}
public interface IInquiryScanner
{
    Task<(bool Ready,string Code)> HealthAsync(CancellationToken cancellation=default);
    Task<InquiryScanResult> ScanAsync(Stream file,CancellationToken cancellation=default);
}
// Genuine clamd INSTREAM transport only. No flag/signature validator can return Clean.
public sealed class ClamdInquiryScanner(IConfiguration configuration,long maximumBytes=InquiryFileOptions.MaximumBytes):IInquiryScanner
{
    private int Port=>int.TryParse(configuration["Scanner:ClamdPort"],out var value)&&value is >=1024 and <=65535?value:0;
    private int TimeoutSeconds=>int.TryParse(configuration["Scanner:TimeoutSeconds"],out var value)&&value is >=1 and <=120?value:30;
    private static async Task<string> Reply(NetworkStream stream,CancellationToken ct)
    {
        var bytes=new List<byte>();var one=new byte[1];while(bytes.Count<1024){if(await stream.ReadAsync(one,ct)==0)throw new IOException("ScannerClosed");if(one[0]==0)return Encoding.UTF8.GetString(bytes.ToArray());bytes.Add(one[0]);}throw new IOException("ScannerReplyTooLong");
    }
    private async Task<string> Command(string command,CancellationToken ct)
    {
        using var client=new TcpClient();await client.ConnectAsync(IPAddress.Loopback,Port,ct);await using var stream=client.GetStream();await stream.WriteAsync(Encoding.ASCII.GetBytes("z"+command+"\0"),ct);return await Reply(stream,ct);
    }
    public async Task<(bool Ready,string Code)> HealthAsync(CancellationToken cancellation=default)
    {
        if(Port==0)return(false,"ScannerNotConfigured");using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try{return (await Command("PING",deadline.Token)=="PONG", "ClamdProbe");}catch(Exception e)when(e is IOException or SocketException or OperationCanceledException){return(false,"ScannerUnavailable");}
    }
    public async Task<InquiryScanResult> ScanAsync(Stream file,CancellationToken cancellation=default)
    {
        if(maximumBytes<1||maximumBytes>1024L*1024*1024)return new(InquiryScanState.ScanFailed,"ScannerConfigurationInvalid",null);
        if(file.CanSeek&&file.Length-file.Position>maximumBytes)return new(InquiryScanState.ScanFailed,"ScannerFileTooLarge",null);
        if(Port==0)return new(InquiryScanState.ScanFailed,"ScannerNotConfigured",null);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));var ct=deadline.Token;
        try
        {
            var version=await Command("VERSIONCOMMANDS",ct);if(!version.StartsWith("ClamAV ",StringComparison.Ordinal)||!version.Contains("INSTREAM",StringComparison.Ordinal))return new(InquiryScanState.ScanFailed,"ScannerProtocolUnsupported",null);
            using var client=new TcpClient();await client.ConnectAsync(IPAddress.Loopback,Port,ct);await using var stream=client.GetStream();await stream.WriteAsync("zINSTREAM\0"u8.ToArray(),ct);
            var buffer=ArrayPool<byte>.Shared.Rent(65536);var size=new byte[4];long total=0;
            try{while(true){var count=await file.ReadAsync(buffer,ct);if(count==0)break;total+=count;if(total>maximumBytes)return new(InquiryScanState.ScanFailed,"ScannerFileTooLarge",null);BinaryPrimitives.WriteInt32BigEndian(size,count);await stream.WriteAsync(size,ct);await stream.WriteAsync(buffer.AsMemory(0,count),ct);}}finally{ArrayPool<byte>.Shared.Return(buffer);}
            await stream.WriteAsync(new byte[4],ct);var reply=await Reply(stream,ct);var engine=version.Split("COMMANDS:")[0].Trim();if(engine.Length>200)engine=engine[..200];
            return reply=="stream: OK"?new(InquiryScanState.Clean,"",engine):reply.StartsWith("stream: ",StringComparison.Ordinal)&&reply.EndsWith(" FOUND",StringComparison.Ordinal)?new(InquiryScanState.Infected,"MalwareDetected",engine):new(InquiryScanState.ScanFailed,"ScannerRejected",engine);
        }
        catch(Exception e)when(e is IOException or SocketException or OperationCanceledException){return new(InquiryScanState.ScanFailed,"ScannerUnavailableOrTimeout",null);}
    }
}
public sealed class InquiryAttachmentService(PortalDbContext db,StorageOptions storage,MediaService validation,IDataProtectionProvider protection,InquiryFileOptions options,TimeProvider? clock=null)
{
    private DateTimeOffset Now=>(clock??TimeProvider.System).GetUtcNow();
    private readonly IDataProtector protector=protection.CreateProtector("LandPortal.InquiryFile.Name.v1");
    public string Name(InquiryAttachment file)=>protector.Unprotect(file.ProtectedName);
    public string PathFor(InquiryAttachment file)
    {
        if(!file.StorageKey.StartsWith("inquiry-private/",StringComparison.Ordinal))throw new InvalidOperationException("Tệp nằm ngoài kho hỏi đáp.");return MediaService.ResolveMediaPath(storage,file.StorageKey);
    }
    public async Task<InquiryAttachment> UploadAsync(Stream input,string name,string claimedMime,string session,CancellationToken ct=default)
    {
        if(!options.Enabled)throw new MediaUploadException("Chưa bật kho tiếp nhận tệp riêng tại môi trường này.",503);
        if(string.IsNullOrWhiteSpace(name)||name.Length>150||name.Any(char.IsControl)||name.Contains('/')||name.Contains('\\')||name.Contains(':')||name.Count(x=>x=='.')!=1)throw new MediaUploadException("Tên tệp không hợp lệ; chỉ tên đơn với một phần mở rộng.");
        var ext=System.IO.Path.GetExtension(name).ToLowerInvariant();var mime=ext switch{".pdf"=>"application/pdf",".jpg" or ".jpeg"=>"image/jpeg",".png"=>"image/png",_=>""};
        if(!options.MimeTypes.Contains(mime)||claimedMime.Split(';')[0].Trim()!=mime)throw new MediaUploadException("Định dạng/MIME không được phép.");
        var owner=LandPortal.Application.TextNormalizer.Hash(session);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource={"inquiry-upload-"+owner},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;IF @r<0 THROW 51000,'Upload lock unavailable',1;",ct);
        if(await db.InquiryAttachments.CountAsync(x=>x.SessionHash==owner&&x.SubmissionId==null&&x.CreatedUtc>Now.AddHours(-1)&&x.ScanState!=InquiryScanState.Deleted,ct)>=4)throw new MediaUploadException("Quá nhiều tệp tạm; chờ hoặc hoàn tất hồ sơ.",429);
        var file=new InquiryAttachment{SessionHash=owner,StorageKey="inquiry-private/"+Guid.NewGuid().ToString("N")+ext,ProtectedName=protector.Protect(name),Mime=mime,CreatedUtc=Now,ExpiresUtc=Now.AddHours(1),AvailableUtc=Now};var path=PathFor(file);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);var committed=false;var sqlAttempted=false;
        try
        {
            var buffer=ArrayPool<byte>.Shared.Rent(65536);using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            try{await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous);while(true){var count=await input.ReadAsync(buffer,ct);if(count==0)break;file.Bytes+=count;if(file.Bytes>InquiryFileOptions.MaximumBytes)throw new MediaUploadException("Tệp vượt10 MiB.",413);digest.AppendData(buffer,0,count);await output.WriteAsync(buffer.AsMemory(0,count),ct);}}finally{ArrayPool<byte>.Shared.Return(buffer);}
            if(file.Bytes==0)throw new MediaUploadException("Tệp rỗng.");validation.ValidateContent(path,mime,ct);file.Sha256=Convert.ToHexStringLower(digest.GetHashAndReset());db.InquiryAttachments.Add(file);sqlAttempted=true;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);committed=true;return file;
        }
        finally
        {
            // An uncertain SQL commit must keep its private file. The bounded orphan sweep reconciles later.
            if(!committed&&!sqlAttempted&&System.IO.File.Exists(path))System.IO.File.Delete(path);
        }
    }
    public async Task<InquiryAttachment> BindAsync(Guid id,string session,Guid? challenge,Guid? submission,CancellationToken ct=default)
    {
        var file=await db.InquiryAttachments.FromSqlInterpolated($"SELECT * FROM dbo.InquiryAttachments WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync(ct);
        if(file==null||file.SessionHash!=LandPortal.Application.TextNormalizer.Hash(session)||file.SubmissionId!=null||file.ExpiresUtc<=Now||file.ScanState is InquiryScanState.Deleted or InquiryScanState.Infected)throw new InvalidOperationException("Tệp không thuộc phiên gửi, đã dùng hoặc hết hạn.");
        if(submission!=null&&file.ChallengeId!=challenge)throw new InvalidOperationException("Tệp không gắn với mã xác minh này.");
        file.ChallengeId=challenge;file.SubmissionId=submission;return file;
    }
    public async Task<int> SweepAsync()
    {
        var rows=await db.InquiryAttachments.Where(x=>x.SubmissionId==null&&x.ExpiresUtc<=Now&&x.ScanState!=InquiryScanState.Deleted&&x.ScanState!=InquiryScanState.Scanning).Take(100).ToListAsync();var count=0;
        foreach(var file in rows)
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);await db.Entry(file).ReloadAsync();if(file.SubmissionId!=null||file.ExpiresUtc>Now||file.ScanState==InquiryScanState.Scanning){await tx.RollbackAsync();continue;}
            var path=PathFor(file);if(System.IO.File.Exists(path))System.IO.File.Delete(path);file.ScanState=InquiryScanState.Deleted;await db.SaveChangesAsync();await tx.CommitAsync();count++;
        }
        // A failed/uncertain SQL commit can leave a file without a row. Reconcile only generated
        // names in this private directory, after 24 hours; never walk source-media directories.
        var directory=MediaService.ResolveMediaPath(storage,"inquiry-private/orphan-boundary");
        directory=System.IO.Path.GetDirectoryName(directory)!;
        if(Directory.Exists(directory))
        {
            foreach(var candidate in Directory.EnumerateFiles(directory).Where(x=>System.IO.File.GetLastWriteTimeUtc(x)<Now.AddHours(-24).UtcDateTime).Take(100))
            {
                var leaf=System.IO.Path.GetFileName(candidate);var stem=System.IO.Path.GetFileNameWithoutExtension(leaf);var extension=System.IO.Path.GetExtension(leaf);
                if(stem.Length!=32||!Guid.TryParseExact(stem,"N",out _)||extension is not(".pdf" or ".jpg" or ".jpeg" or ".png"))continue;
                var key="inquiry-private/"+leaf;var safe=MediaService.ResolveMediaPath(storage,key);
                await using var orphanTx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                if(!await db.InquiryAttachments.AnyAsync(x=>x.StorageKey==key)&&System.IO.File.Exists(safe)&&System.IO.File.GetLastWriteTimeUtc(safe)<Now.AddHours(-24).UtcDateTime){System.IO.File.Delete(safe);count++;}
                await orphanTx.CommitAsync();
            }
        }
        return count;
    }
}
