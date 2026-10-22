using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Infrastructure;
public sealed class OutboxReconciliationService(PortalDbContext db,InteractionService interaction,CurrentPermissionService permissions,IConfiguration configuration,TimeProvider? clock=null)
{
    private DateTimeOffset Now=>(clock??TimeProvider.System).GetUtcNow();
    public IQueryable<InteractionOutbox> Scope(ContentActor actor)
    {
        if(!actor.Has(Permissions.OutboxReconcile))throw new UnauthorizedAccessException();
        IQueryable<Guid> allowed=db.InteractionSubmissions.Where(x=>false).Select(x=>x.Id);
        foreach(var kind in Enum.GetValues<InteractionKind>())if(actor.Has(InteractionPermissions.Read(kind)))allowed=allowed.Concat(interaction.Inbox(actor,kind).Select(x=>x.Id));
        return db.InteractionOutbox.AsNoTracking().Where(x=>x.SubmissionId==null||allowed.Contains(x.SubmissionId.Value));
    }
    private async Task<string> ValidateReceipt(InteractionOutbox row,string reference)
    {
        // Only trusted, local Test sink artifacts; never fetch a user-controlled URL/path outside this project.
        if(configuration["Sql:ExpectedDatabase"]!="LandPortal_Test")throw new InvalidOperationException("Hiện chỉ có đối soát receipt SMTP sink Test. SMTP thật chưa triển khai.");
        var root=configuration["WorkspaceRoot"]!;if(Path.IsPathRooted(reference)||reference.Contains("..",StringComparison.Ordinal)||Path.GetExtension(reference)!=".eml")throw new InvalidOperationException("Receipt phải là tệp .eml Test trong project.");
        var path=Path.GetFullPath(reference,root);var tests=Path.Combine(root,".local","tests","phase2c","mail")+Path.DirectorySeparatorChar;var evidence=Path.Combine(root,".local","evidence","phase2c")+Path.DirectorySeparatorChar;
        var videoEvidence=Path.Combine(root,".local","evidence","phase2d")+Path.DirectorySeparatorChar;
        var inventoryEvidence=Path.Combine(root,".local","evidence","phase3a")+Path.DirectorySeparatorChar;
        var batchEvidence=Path.Combine(root,".local","evidence","phase3b")+Path.DirectorySeparatorChar;
        if(!path.StartsWith(tests,StringComparison.OrdinalIgnoreCase)&&!path.StartsWith(evidence,StringComparison.OrdinalIgnoreCase)&&!path.StartsWith(videoEvidence,StringComparison.OrdinalIgnoreCase)&&!path.StartsWith(inventoryEvidence,StringComparison.OrdinalIgnoreCase)&&!path.StartsWith(batchEvidence,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Receipt ngoài kho sink Test.");
        for(var parent=new DirectoryInfo(Path.GetDirectoryName(path)!);parent!=null;parent=parent.Parent)if(parent.Exists&&(parent.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Receipt qua reparse point.");
        var info=new FileInfo(path);if(!info.Exists||info.Length is <1 or >65536||(info.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Receipt thiếu, quá cỡ hoặc không hợp lệ.");
        var raw=await File.ReadAllBytesAsync(path);var text=Encoding.UTF8.GetString(raw);var parts=text.Split("\r\n\r\n",2);if(parts.Length!=2)throw new InvalidOperationException("Receipt không có thư SMTP đầy đủ.");
        var headers=parts[0].Split("\r\n");var expected="Message-ID: <"+row.Id+"@landportal.invalid>";var mail=interaction.Unprotect<InteractionMail>(row.ProtectedMail);
        if(headers.Count(x=>x.StartsWith("Message-ID:",StringComparison.OrdinalIgnoreCase))!=1||!headers.Contains(expected)||!headers.Contains("To: "+mail.Email))throw new InvalidOperationException("Receipt không gắn đúng message/event.");
        try{if(Encoding.UTF8.GetString(Convert.FromBase64String(parts[1].Trim()))!=mail.Body)throw new InvalidOperationException("Receipt khác payload đã gửi.");}catch(FormatException){throw new InvalidOperationException("Receipt body không hợp lệ.");}
        return Convert.ToHexStringLower(SHA256.HashData(raw));
    }
    public async Task<Guid?> ReconcileAsync(ContentActor actor,Guid id,byte[] version,Guid command,string decision,string evidence,bool acknowledgeDuplicateRisk)
    {
        actor=await permissions.GetAsync(actor.UserId);if(!actor.Has(Permissions.OutboxReconcile))throw new UnauthorizedAccessException();
        if(command==Guid.Empty||string.IsNullOrWhiteSpace(evidence)||evidence.Length>1000||decision is not("accepted" or "not-sent" or "unknown" or "resend-risk"))throw new InvalidOperationException("Thiếu quyết định, bằng chứng hoặc mã thao tác.");
        var hash=TextNormalizer.Hash(JsonSerializer.Serialize(new{id,version,decision,evidence,acknowledgeDuplicateRisk}));await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var row=await db.InteractionOutbox.FromSqlInterpolated($"SELECT * FROM dbo.InteractionOutbox WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync()??throw new KeyNotFoundException();
        if(!await Scope(actor).AnyAsync(x=>x.Id==id))throw new UnauthorizedAccessException();
        var previous=await db.OutboxReconciliations.FindAsync(command);if(previous!=null){if(previous.OutboxId!=id||previous.ActorId!=actor.UserId||previous.RequestHash!=hash)throw new InvalidOperationException("Mã đối soát đã dùng.");await tx.CommitAsync();return previous.ResendOutboxId;}
        if(!row.RowVersion.SequenceEqual(version))throw new DbUpdateConcurrencyException();if(row.State!=InteractionMailState.Uncertain)throw new InvalidOperationException("Chỉ đối soát trạng thái Uncertain.");
        string? receipt=null;Guid? resend=null;
        if(decision=="accepted"){receipt=await ValidateReceipt(row,evidence);row.State=InteractionMailState.Accepted;row.AcceptedUtc=Now;row.ErrorCode="ReconciledReceipt";}
        if(decision is "not-sent" or "resend-risk")
        {
            if(decision=="resend-risk"&&!acknowledgeDuplicateRisk)throw new InvalidOperationException("Phải xác nhận nguy cơ người nhận có thư trùng.");
            if(decision=="not-sent"&&!evidence.StartsWith("NOT_SENT_CONFIRMED:",StringComparison.Ordinal))throw new InvalidOperationException("Cần xác nhận chắc chắn chưa gửi kèm căn cứ đối soát.");
            var next=new InteractionOutbox{EventKey="reconcile:"+command,SupersedesId=row.Id,ChallengeId=row.ChallengeId,LookupChallengeId=row.LookupChallengeId,SubmissionId=row.SubmissionId,RequestedBy=row.RequestedBy,ProtectedMail=row.ProtectedMail,AvailableUtc=Now};db.InteractionOutbox.Add(next);resend=next.Id;row.State=InteractionMailState.Cancelled;row.ErrorCode=decision=="not-sent"?"ReconciledNotSent":"ManualDuplicateRiskResend";
        }
        db.OutboxReconciliations.Add(new(){Id=command,OutboxId=id,ResendOutboxId=resend,ActorId=actor.UserId,Decision=decision,ProtectedEvidence=interaction.Protect(evidence),ReceiptSha256=receipt,DuplicateRiskAcknowledged=acknowledgeDuplicateRisk,RequestHash=hash,CreatedUtc=Now});
        db.AuditLogs.Add(new(){Actor=actor.UserId,Action="outbox.reconciled",Detail=JsonSerializer.Serialize(new{id,command,decision,resend,acknowledgeDuplicateRisk,receipt})});await db.SaveChangesAsync();await tx.CommitAsync();return resend;
    }
}
