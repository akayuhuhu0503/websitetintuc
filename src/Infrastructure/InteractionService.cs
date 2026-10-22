using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;

public sealed partial class InteractionService(PortalDbContext db,IDataProtectionProvider protection,InteractionMailOptions mailOptions,CurrentPermissionService permissions,TimeProvider? clock=null,InquiryAttachmentService? attachments=null)
{
    private readonly IDataProtector protector=protection.CreateProtector("LandPortal.Interaction.Private.v1");
    private DateTimeOffset Now=>(clock??TimeProvider.System).GetUtcNow();
    public string Protect<T>(T value)=>protector.Protect(JsonSerializer.Serialize(value));
    public T Unprotect<T>(string value)=>JsonSerializer.Deserialize<T>(protector.Unprotect(value))!;
    private async Task LockAsync(string key)=>await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={key},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000; IF @r<0 THROW 51000,'Interaction lock unavailable',1;");
    public async Task<bool> CanReceiveAsync(InteractionKind kind,Guid? id)
    {
        if(kind is InteractionKind.Contact or InteractionKind.Inquiry)return id is null;
        if(id is null)return false;
        var c=await db.Contents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.IsPublic&&!x.IsTrashed&&x.PublishedRevisionId!=null);
        if(c is null)return false;
        if(kind==InteractionKind.Comment)return c.Kind==ContentKind.Article&&c.CommentsEnabled&&!await db.Revisions.AnyAsync(r=>r.Id==c.PublishedRevisionId&&r.Metadata.DisclosureGroupId!=null)&&await db.SiteSettings.AnyAsync(x=>x.Key=="Interactions.CommentsEnabled"&&x.Value=="true");
        if(c.Kind!=ContentKind.DraftDocument)return false;
        var r=await db.Revisions.AsNoTracking().SingleAsync(x=>x.Id==c.PublishedRevisionId);
        return r.Metadata.ConsultationState(Now)=="Đang mở";
    }
    private async Task<DateTimeOffset?> CheckTargetAsync(InteractionInput input)
    {
        if(input.Kind is InteractionKind.Contact or InteractionKind.Inquiry)
        {if(input.ContentId!=null||input.PublishedRevisionId!=null)throw new InvalidOperationException("Hồ sơ này không gắn bài viết.");return null;}
        if(input.ContentId is not Guid id)throw new InvalidOperationException("Thiếu nội dung đích.");
        // Same SQL transaction locks the live pointer, closing the submit/unpublish race.
        var c=await db.Contents.FromSqlInterpolated($"SELECT * FROM dbo.Contents WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync();
        if(c is null||!c.IsPublic||c.IsTrashed||c.PublishedRevisionId!=input.PublishedRevisionId)throw new InvalidOperationException("Bài đã gỡ hoặc phiên bản công khai đã đổi. Mở lại biểu mẫu.");
        if(!await CanReceiveAsync(input.Kind,id))throw new InvalidOperationException("Hiện không tiếp nhận nội dung tại đây.");
        if(input.Kind!=InteractionKind.Consultation)return null;
        var r=await db.Revisions.AsNoTracking().SingleAsync(x=>x.Id==input.PublishedRevisionId);
        return r.Metadata.ConsultationEndDate is DateOnly end?new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(7)).ToUniversalTime():r.Metadata.ConsultationEndUtc;
    }
    public async Task<Guid> RequestAsync(InteractionInput input,string session)
    {
        input.Validate();mailOptions.RequireAvailable();var now=Now;var emailHash=TextNormalizer.Hash(input.Email);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockAsync("interaction-email-"+emailHash);
        await CheckTargetAsync(input);
        var recent=await db.InteractionChallenges.Where(x=>x.EmailHash==emailHash&&x.CreatedUtc>now.AddHours(-1)).ToListAsync();
        if(recent.Count>=5||recent.Any(x=>x.CreatedUtc>now.AddSeconds(-60)))throw new InvalidOperationException("Chờ 60 giây giữa các mã; tối đa 5 mã mỗi email trong một giờ.");
        var hashSession=TextNormalizer.Hash(session);
        foreach(var old in recent.Where(x=>x.Kind==input.Kind&&x.ContentId==input.ContentId&&x.SessionHash==hashSession&&x.SubmissionId==null))old.Revoked=true;
        var code=RandomNumberGenerator.GetInt32(0,1000000).ToString("D6");var key=RandomNumberGenerator.GetBytes(32);
        var challenge=new InteractionChallenge{Kind=input.Kind,ContentId=input.ContentId,PublishedRevisionId=input.PublishedRevisionId,EmailHash=emailHash,SessionHash=hashSession,PayloadHash=input.Hash(),ProtectedKey=protector.Protect(Convert.ToBase64String(key)),CodeHash=CodeHash(key,code),CreatedUtc=now,ExpiresUtc=now.AddMinutes(5)};
        if(input.AttachmentId is Guid file)await (attachments??throw new InvalidOperationException("Kho tệp chưa cấu hình.")).BindAsync(file,session,challenge.Id,null);
        db.InteractionChallenges.Add(challenge);
        db.InteractionOutbox.Add(new(){EventKey="otp:"+challenge.Id,ChallengeId=challenge.Id,ProtectedMail=Protect(new InteractionMail(input.Email,"Mã xác minh gửi nội dung",$"Mã xác minh: {code}\nHết hạn sau 5 phút. Mã chỉ dùng cho nội dung và mục đích vừa yêu cầu.")),AvailableUtc=now});
        await db.SaveChangesAsync();await tx.CommitAsync();return challenge.Id;
    }
    private static string CodeHash(byte[] key,string code)=>Convert.ToHexStringLower(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(code)));
    public async Task<InteractionReceipt> SubmitAsync(Guid challengeId,InteractionInput input,string session,string code)
    {
        input.Validate();await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockAsync("interaction-challenge-"+challengeId);
        var c=await db.InteractionChallenges.SingleOrDefaultAsync(x=>x.Id==challengeId);
        if(c is null||c.SessionHash!=TextNormalizer.Hash(session)||c.PayloadHash!=input.Hash()||c.Kind!=input.Kind||c.EmailHash!=TextNormalizer.Hash(input.Email)||c.ContentId!=input.ContentId||c.PublishedRevisionId!=input.PublishedRevisionId)throw new InvalidOperationException("Mã không gắn với nội dung gửi này. Yêu cầu mã mới khi sửa nội dung.");
        // Idempotent retry is bound to the same verified request and browser session.
        if(c.SubmissionId is Guid prior){await tx.CommitAsync();return new(prior,true);}
        if(c.Revoked||c.ExpiresUtc<=Now||c.FailedAttempts>=5)throw new InvalidOperationException("Mã hết hạn, bị thay thế hoặc đã vượt số lần thử.");
        var key=Convert.FromBase64String(protector.Unprotect(c.ProtectedKey));
        if(code.Length!=6||!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(c.CodeHash),Convert.FromHexString(CodeHash(key,code))))
        {c.FailedAttempts++;await db.SaveChangesAsync();await tx.CommitAsync();throw new InvalidOperationException("Mã xác minh sai.");}
        if(!await db.InteractionOutbox.AnyAsync(x=>x.ChallengeId==c.Id&&x.State==InteractionMailState.Accepted))throw new InvalidOperationException("Thư xác minh chưa được máy chủ thư chấp nhận.");
        var deadline=await CheckTargetAsync(input);var receipt=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var s=new InteractionSubmission{ChallengeId=c.Id,Kind=input.Kind,ContentId=input.ContentId,PublishedRevisionId=input.PublishedRevisionId,ProtectedOriginal=Protect(input),EmailHash=c.EmailHash,ReceiptHash=TextNormalizer.Hash(receipt),PublicConsent=input.PublicConsent,CreatedUtc=Now,DeadlineUtc=deadline};
        if(input.Kind==InteractionKind.Inquiry){s.InquiryField=input.Field??"Chưa phân loại";await InquiryCalendarService.ApplyNewAsync(db,s,Now);}
        if(input.AttachmentId is Guid file)await (attachments??throw new InvalidOperationException("Kho tệp chưa cấu hình.")).BindAsync(file,session,c.Id,s.Id);
        db.InteractionSubmissions.Add(s);c.SubmissionId=s.Id;
        db.InteractionOutbox.Add(new(){EventKey="receipt:"+s.Id,SubmissionId=s.Id,ProtectedMail=Protect(new InteractionMail(input.Email,"Xác nhận đã tiếp nhận",$"Hệ thống đã lưu hồ sơ chờ xử lý/kiểm duyệt. Mã hồ sơ: {receipt}\nMã không cho phép tra cứu thông tin cá nhân công khai.")),AvailableUtc=Now});
        db.AuditLogs.Add(new(){Actor="public",Action="interaction.received",ContentId=s.ContentId,RevisionId=s.PublishedRevisionId,Detail=JsonSerializer.Serialize(new{s.Id,s.Kind})});
        await db.SaveChangesAsync();await tx.CommitAsync();return new(s.Id,false);
    }
    public IQueryable<InteractionSubmission> Inbox(ContentActor actor,InteractionKind kind)
    {
        if(!InteractionPermissions.CanList(actor,kind))throw new UnauthorizedAccessException();
        var rows=db.InteractionSubmissions.AsNoTracking().Where(x=>x.Kind==kind);
        if(kind==InteractionKind.Inquiry&&!actor.Has(Permissions.InquirySupervise))rows=rows.Where(x=>x.AssigneeId==actor.UserId||(x.AssigneeId==null&&actor.Has(Permissions.InquiryAssign)));
        if(actor.CategoryIds!=null){var ids=permissions.ScopeRevisions(db.Revisions,actor).Select(x=>x.Id);var contents=permissions.Scope(db.Contents,actor).Select(x=>x.Id);rows=rows.Where(x=>x.ContentId==null||(contents.Contains(x.ContentId.Value)&&x.PublishedRevisionId!=null&&ids.Contains(x.PublishedRevisionId.Value)));}
        return rows;
    }
    public async Task ModerateAsync(ContentActor actor,Guid id,byte[] version,Guid commandId,string action,string displayName,string text,string answer,string reason)
    {
        actor=await permissions.GetAsync(actor.UserId);
        if(commandId==Guid.Empty)throw new InvalidOperationException("Thiếu mã thao tác.");
        var hash=TextNormalizer.Hash(JsonSerializer.Serialize(new{id,version,action,displayName,text,answer,reason}));
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockAsync("interaction-submission-"+id);
        var s=await db.InteractionSubmissions.SingleOrDefaultAsync(x=>x.Id==id)??throw new KeyNotFoundException();
        if(!actor.Has(InteractionPermissions.Read(s.Kind)))throw new UnauthorizedAccessException();
        if(!await Inbox(actor,s.Kind).AnyAsync(x=>x.Id==id))throw new UnauthorizedAccessException();
        var required=s.Kind==InteractionKind.Inquiry&&action is "publish" or "hide"?Permissions.InquiryPublish:InteractionPermissions.Process(s.Kind);
        if(!actor.Has(required))throw new UnauthorizedAccessException();
        var prior=await db.InteractionCommands.SingleOrDefaultAsync(x=>x.Id==commandId);
        if(prior!=null){if(prior.SubmissionId!=id||prior.ActorId!=actor.UserId||prior.RequestHash!=hash)throw new InvalidOperationException("Mã thao tác đã dùng cho yêu cầu khác.");await tx.CommitAsync();return;}
        if(!s.RowVersion.SequenceEqual(version))throw new DbUpdateConcurrencyException();
        if(action=="publish")
        {
            if(s.Kind is not (InteractionKind.Comment or InteractionKind.Inquiry)||!s.PublicConsent)throw new InvalidOperationException("Hồ sơ riêng tư hoặc chưa đồng ý công khai.");
            if(s.Kind==InteractionKind.Comment&&s.ContentId is Guid content&&(!await db.Contents.AnyAsync(x=>x.Id==content&&x.IsPublic&&!x.IsTrashed)||await db.Contents.AnyAsync(x=>x.Id==content&&db.Revisions.Any(r=>r.Id==x.PublishedRevisionId&&r.Metadata.DisclosureGroupId!=null))))throw new InvalidOperationException("Bài đã gỡ hoặc thuộc nhóm không có bình luận.");
            if(string.IsNullOrWhiteSpace(text)||text.Length>8000||displayName.Length>120||answer.Length>8000)throw new InvalidOperationException("Bản công khai không hợp lệ.");
            if(s.Kind==InteractionKind.Inquiry&&string.IsNullOrWhiteSpace(answer))throw new InvalidOperationException("Hỏi đáp công khai phải có câu trả lời đã duyệt.");
            if(answer.Length>0&&s.Kind==InteractionKind.Comment&&!actor.Has(Permissions.CommentReply))throw new UnauthorizedAccessException();
            // Never copy original PII to the public projection implicitly.
            var snapshot=new InteractionPublicSnapshot{SubmissionId=id,DisplayName=displayName.Trim(),Text=text.Trim(),AgencyAnswer=answer.Trim(),ApprovedBy=actor.UserId,ApprovedUtc=Now};
            db.InteractionPublicSnapshots.Add(snapshot);s.PublicSnapshotId=snapshot.Id;
            if(s.Kind==InteractionKind.Comment)s.State=InteractionState.Closed;
        }
        else if(action=="hide"){if(s.Kind is not (InteractionKind.Comment or InteractionKind.Inquiry))throw new InvalidOperationException("Hồ sơ này không có bản công khai.");if(string.IsNullOrWhiteSpace(reason)||reason.Length>500)throw new InvalidOperationException("Nhập lý do ẩn.");s.PublicSnapshotId=null;}
        else if(action=="reject"){if(string.IsNullOrWhiteSpace(reason)||reason.Length>500)throw new InvalidOperationException("Nhập lý do từ chối.");s.PublicSnapshotId=null;s.State=InteractionState.Rejected;}
        else if(action=="note"&&s.Kind==InteractionKind.Contact){if(string.IsNullOrWhiteSpace(answer)||answer.Length>8000)throw new InvalidOperationException("Nhập ghi chú nội bộ, tối đa8000 ký tự.");s.ProtectedAnswer=Protect(answer);}
        else if(action=="process")s.State=InteractionState.Processing;
        else if(action=="close")s.State=InteractionState.Closed;
        else if(action=="answer"&&s.Kind==InteractionKind.Inquiry)
        {
            if(string.IsNullOrWhiteSpace(answer)||answer.Length>8000)throw new InvalidOperationException("Nhập câu trả lời.");
            s.ProtectedAnswer=Protect(answer);s.State=InteractionState.Answered;
            var original=Unprotect<InteractionInput>(s.ProtectedOriginal);
            db.InteractionOutbox.Add(new(){EventKey="answer:"+commandId,SubmissionId=id,RequestedBy=actor.UserId,ProtectedMail=Protect(new InteractionMail(original.Email,"Trả lời hồ sơ hỏi đáp",answer)),AvailableUtc=Now});
        }
        else throw new InvalidOperationException("Thao tác không hỗ trợ.");
        db.InteractionCommands.Add(new(){Id=commandId,SubmissionId=id,ActorId=actor.UserId,RequestHash=hash,CreatedUtc=Now});
        // Audit stores identifiers/state only, never original text/email, tokens or OTP.
        db.AuditLogs.Add(new(){Actor=actor.UserId,Action="interaction."+action,ContentId=s.ContentId,RevisionId=s.PublishedRevisionId,Detail=JsonSerializer.Serialize(new{s.Id,s.Kind,s.State,ProtectedReason=Protect(reason)})});
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
    public async Task<IReadOnlyList<PublicInteraction>> PublicAsync(InteractionKind kind,Guid? contentId=null,int page=1)
    {
        if(kind is not (InteractionKind.Comment or InteractionKind.Inquiry))return [];
        var rows=from s in db.InteractionSubmissions.AsNoTracking() join p in db.InteractionPublicSnapshots.AsNoTracking() on s.PublicSnapshotId equals p.Id where p.SubmissionId==s.Id&&s.Kind==kind&&s.State!=InteractionState.Rejected&&s.PublicConsent select new{s,p};
        if(kind==InteractionKind.Comment)rows=rows.Where(x=>x.s.ContentId==contentId&&db.Contents.Any(c=>c.Id==contentId&&c.IsPublic&&!c.IsTrashed&&c.Kind==ContentKind.Article&&!db.Revisions.Any(r=>r.Id==c.PublishedRevisionId&&r.Metadata.DisclosureGroupId!=null)));
        return await rows.OrderByDescending(x=>x.p.ApprovedUtc).ThenBy(x=>x.p.Id).Skip((Math.Clamp(page,1,10000)-1)*20).Take(20).Select(x=>new PublicInteraction(x.p.DisplayName,x.p.Text,x.p.AgencyAnswer,x.p.ApprovedUtc)).ToListAsync();
    }
}
