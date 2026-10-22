using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.Controllers;
[Route("tuong-tac"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[RequestSizeLimit(65536)]
public sealed class InteractionsController(PortalDbContext db,InteractionService service,InteractionMailOptions mail,InquiryAttachmentService attachments,PublicCaptchaService captcha):Controller
{
    private string Session()
    {
        return LandPortal.Web.Services.InteractionBrowserSession.Get(HttpContext);
    }
    [HttpGet("{kind}/{contentId:guid?}")]
    public async Task<IActionResult> Form(InteractionKind kind,Guid? contentId)
    {
        if(!Enum.IsDefined(kind)||!await service.CanReceiveAsync(kind,contentId))return StatusCode(409,"Hiện không tiếp nhận nội dung tại đây.");
        Session();Guid? revision=contentId==null?null:await db.Contents.Where(x=>x.Id==contentId).Select(x=>x.PublishedRevisionId).SingleAsync();
        var form=new InteractionForm{Input=new(){Kind=kind,ContentId=contentId,PublishedRevisionId=revision},MailAvailable=mail.Available};await Challenge(form);return View(form);
    }
    [HttpPost("ma-xac-minh"),EnableRateLimiting("interaction")]
    public IActionResult RequestCode()=>StatusCode(410,"Gửi biểu mẫu đã chuyển sang CAPTCHA. Mở lại biểu mẫu; không sử dụng OTP cũ để tạo hồ sơ mới.");
    [HttpPost("lam-moi"),EnableRateLimiting("interaction")]
    public async Task<IActionResult> Refresh(InteractionForm form)
    {
        if(!Enum.IsDefined(form.Input.Kind)||!await service.CanReceiveAsync(form.Input.Kind,form.Input.ContentId))return BadRequest();
        await Challenge(form);return View("Form",form);
    }
    [HttpPost("gui"),EnableRateLimiting("public-submit"),RequestSizeLimit(11*1024*1024),RequestFormLimits(MultipartBodyLengthLimit=11*1024*1024)]
    public async Task<IActionResult> Submit(InteractionForm form,IFormFile? attachment)
    {
        form.MailAvailable=mail.Available;
        try
        {
            if(!ModelState.IsValid)throw new ValidationException("Kiểm tra các trường bắt buộc.");
            if(Request.Form.Files.Count>1)throw new ValidationException("Chỉ nhận một tệp tối đa10 MiB.");
            if(form.Input.AttachmentId!=null)throw new ValidationException("Không nhận tham chiếu tệp từ biểu mẫu khác.");
            if(attachment!=null&&form.Input.Kind is not (InteractionKind.Inquiry or InteractionKind.Contact))throw new ValidationException("Tệp chỉ dành cho hỏi đáp và hòm thư góp ý.");
            async Task<Guid?> Upload(){await using var stream=attachment!.OpenReadStream();return (await attachments.UploadAsync(stream,attachment.FileName,attachment.ContentType,Session(),HttpContext.RequestAborted)).Id;}
            var receipt=await service.SubmitCaptchaAsync(captcha,form.CaptchaId,form.CaptchaAnswer,form.Input,Session(),Address,attachment==null?null:Upload);
            return View("Received",receipt);
        }
        catch(PublicCaptchaException e){Response.StatusCode=e.StatusCode;ModelState.AddModelError("",e.Message);}
        catch(Exception e)when(e is ValidationException or InvalidOperationException){Response.StatusCode=400;ModelState.AddModelError("",e.Message);}
        catch(MediaUploadException e){Response.StatusCode=e.StatusCode;ModelState.AddModelError("",e.Message);}
        await Challenge(form);return View("Form",form);
    }
    private string Address=>HttpContext.Connection.RemoteIpAddress?.MapToIPv6().ToString()??"unknown";
    private async Task Challenge(InteractionForm form)
    {
        form.CaptchaAnswer="";ModelState.Remove(nameof(form.CaptchaAnswer));ModelState.Remove(nameof(form.CaptchaId));
        try{var challenge=await captcha.IssueAsync(Session(),Address,form.Input.Kind.ToString(),form.Input.ContentId,form.Input.PublishedRevisionId);form.CaptchaId=challenge.Id;form.CaptchaQuestion=challenge.Question;}
        catch(PublicCaptchaException e){form.CaptchaId=Guid.Empty;form.CaptchaQuestion="";Response.StatusCode=e.StatusCode;ModelState.AddModelError("",e.Message);}
    }
    [HttpGet("thu/{id:guid}")]
    public async Task<IActionResult> Status(Guid id)
    {
        var hash=TextNormalizer.Hash(Session());if(!await db.InteractionChallenges.AnyAsync(x=>x.Id==id&&x.SessionHash==hash))return NotFound();
        var state=await db.InteractionOutbox.Where(x=>x.ChallengeId==id).Select(x=>x.State).SingleAsync();return Json(new{state=state.ToString()});
    }
    [HttpGet("hoi-dap-da-duyet")]
    public async Task<IActionResult> Questions(int page=1,string? q=null,string? field=null){if(q?.Length>200||field?.Length>120)return BadRequest();ViewData["Title"]="Hỏi đáp đã duyệt";return View("Public",await service.PublicInquiryPageAsync(q,field,page));}
}
