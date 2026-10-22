using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public sealed partial class InteractionService
{
    public async Task<InteractionReceipt> SubmitCaptchaAsync(PublicCaptchaService captcha,Guid challenge,string answer,InteractionInput input,string session,string address,Func<Task<Guid?>>? upload=null)
    {
        await captcha.ConsumeAsync(challenge,answer,session,address,input.Kind.ToString(),input.ContentId,input.PublishedRevisionId);
        input.Validate();
        if(input.Kind==InteractionKind.Inquiry&&string.IsNullOrWhiteSpace(input.Field))throw new InvalidOperationException("Chọn hoặc nhập lĩnh vực hỏi đáp.");
        if(upload!=null){if(input.AttachmentId!=null)throw new InvalidOperationException("Chỉ nhận một tệp cho một lần gửi.");input.AttachmentId=await upload();input.Validate();}
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var deadline=await CheckTargetAsync(input);var receipt=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var s=new InteractionSubmission{ChallengeId=challenge,Kind=input.Kind,ContentId=input.ContentId,PublishedRevisionId=input.PublishedRevisionId,ProtectedOriginal=Protect(input),EmailHash=TextNormalizer.Hash(input.Email),ReceiptHash=TextNormalizer.Hash(receipt),PublicConsent=input.PublicConsent,CreatedUtc=Now,DeadlineUtc=deadline};
        if(input.Kind==InteractionKind.Inquiry){s.InquiryField=input.Field;await InquiryCalendarService.ApplyNewAsync(db,s,Now);}
        if(input.AttachmentId is Guid file)await (attachments??throw new InvalidOperationException("Kho tệp chưa cấu hình.")).BindAsync(file,session,null,s.Id);
        db.InteractionSubmissions.Add(s);
        if(mailOptions.Available)db.InteractionOutbox.Add(new(){EventKey="receipt:"+s.Id,SubmissionId=s.Id,ProtectedMail=Protect(new InteractionMail(input.Email,"Xác nhận đã tiếp nhận",$"Hệ thống đã lưu hồ sơ chờ xử lý/kiểm duyệt. Mã hồ sơ: {receipt}\nMã tiếp nhận không cấp quyền đọc hồ sơ riêng tư. Email chưa được xác minh.")),AvailableUtc=Now});
        db.AuditLogs.Add(new(){Actor="public",Action="interaction.received",ContentId=s.ContentId,RevisionId=s.PublishedRevisionId,Detail=JsonSerializer.Serialize(new{s.Id,s.Kind,Verification="CAPTCHA-v05",EmailVerified=false})});
        await db.SaveChangesAsync();await tx.CommitAsync();return new(s.Id,false){PublicCode=receipt,MailQueued=mailOptions.Available};
    }
}
