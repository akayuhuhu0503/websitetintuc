using System.Data;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public sealed class InquiryAssignmentService(PortalDbContext db,InteractionService interaction,CurrentPermissionService permissions)
{
    public async Task AssignAsync(ContentActor actor,Guid id,string? assignee,byte[] version,Guid command,string reason)
    {
        actor=await permissions.GetAsync(actor.UserId);if(!actor.Has(Permissions.InquiryAssign)||!actor.Has(Permissions.InquiryRead))throw new UnauthorizedAccessException();
        if(command==Guid.Empty||string.IsNullOrWhiteSpace(reason)||reason.Length>500)throw new InvalidOperationException("Nhập lý do và mã phân công.");
        assignee=string.IsNullOrWhiteSpace(assignee)?null:assignee;var hash=TextNormalizer.Hash(JsonSerializer.Serialize(new{id,assignee,version,reason}));
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var row=await db.InteractionSubmissions.FromSqlInterpolated($"SELECT * FROM dbo.InteractionSubmissions WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync()??throw new KeyNotFoundException();
        if(row.Kind!=InteractionKind.Inquiry||!await interaction.Inbox(actor,InteractionKind.Inquiry).AnyAsync(x=>x.Id==id))throw new UnauthorizedAccessException();
        var previous=await db.InquiryAssignments.FindAsync(command);if(previous!=null){if(previous.SubmissionId!=id||previous.ActorId!=actor.UserId||previous.RequestHash!=hash)throw new InvalidOperationException("Mã phân công đã dùng.");await tx.CommitAsync();return;}
        if(!row.RowVersion.SequenceEqual(version))throw new DbUpdateConcurrencyException();
        if(assignee!=null)
        {
            var target=await permissions.GetAsync(assignee);if(!target.Has(Permissions.InquiryRead)||!target.Has(Permissions.InquiryRespond))throw new InvalidOperationException("Người nhận không hoạt động hoặc thiếu quyền đọc/trả lời.");
            if(row.ContentId is Guid content&&!await permissions.CanAccessContentAsync(target,content))throw new InvalidOperationException("Người nhận ngoài phạm vi hồ sơ.");
        }
        db.InquiryAssignments.Add(new(){Id=command,SubmissionId=id,PreviousAssigneeId=row.AssigneeId,AssigneeId=assignee,ActorId=actor.UserId,ProtectedReason=interaction.Protect(reason),RequestHash=hash,CreatedUtc=DateTimeOffset.UtcNow});row.AssigneeId=assignee;
        if(assignee!=null&&row.State==InteractionState.Pending)row.State=InteractionState.Processing;
        if(assignee==null&&row.State==InteractionState.Processing)row.State=InteractionState.Pending;
        db.AuditLogs.Add(new(){Actor=actor.UserId,Action="inquiry.assigned",Detail=JsonSerializer.Serialize(new{id,command})});await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
