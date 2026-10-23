using System.ComponentModel.DataAnnotations;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
namespace LandPortal.Web.Controllers;
public sealed class InquiryLookupForm
{
    [Required,StringLength(32)] public string Receipt {get;set;}="";
    [Required,StringLength(254)] public string Email {get;set;}="";
    public Guid CaptchaId {get;set;}
    [Required,StringLength(3)] public string CaptchaAnswer {get;set;}="";
    [BindNever] public string Question {get;set;}="";
    [BindNever] public InquiryStatusResult? Result {get;set;}
    [BindNever] public string? Message {get;set;}
}
[Route("tra-cuu-ho-so"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None),EnableRateLimiting("interaction"),RequestSizeLimit(8192)]
public sealed class InquiryLookupController(InquiryCaptchaLookup lookup,PublicCaptchaService captcha):Controller
{
    private string Session()=>InteractionBrowserSession.Get(HttpContext);
    private string Address=>HttpContext.Connection.RemoteIpAddress?.MapToIPv6().ToString()??"unknown";
    [HttpGet("")]
    public async Task<IActionResult> Index(){var form=new InquiryLookupForm();await Challenge(form);return View("Index",form);}
    [HttpPost("")]
    public async Task<IActionResult> Search(InquiryLookupForm form)
    {
        form.Result=null;form.Message=null;
        try
        {
            if(!ModelState.IsValid){await captcha.ConsumeAsync(form.CaptchaId,form.CaptchaAnswer,Session(),Address,InquiryCaptchaLookup.Purpose);form.Message="Không tìm thấy thông tin phù hợp";}
            else{form.Result=await lookup.ReadAsync(form.Receipt,form.Email,form.CaptchaId,form.CaptchaAnswer,Session(),Address);if(form.Result==null)form.Message="Không tìm thấy thông tin phù hợp";}
        }
        catch(PublicCaptchaException e){Response.StatusCode=e.StatusCode;form.Message=e.Message;}
        ModelState.Clear();form.Receipt="";form.Email="";await Challenge(form);return View("Index",form);
    }
    private async Task Challenge(InquiryLookupForm form)
    {
        form.CaptchaAnswer="";
        try{var challenge=await captcha.IssueAsync(Session(),Address,InquiryCaptchaLookup.Purpose);form.CaptchaId=challenge.Id;form.Question=challenge.Question;}
        catch(PublicCaptchaException e){form.CaptchaId=Guid.Empty;form.Question="";form.Message=e.Message;Response.StatusCode=e.StatusCode;}
    }
    [HttpGet("ket-qua"),HttpPost("ma"),HttpPost("xac-minh")]
    public IActionResult Retired()=>StatusCode(410,"Đường dẫn cũ đã ngừng. Mở trang tra cứu và nhập mã tiếp nhận, email, CAPTCHA mới.");
}
