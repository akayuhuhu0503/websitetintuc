using Microsoft.AspNetCore.Mvc;
namespace LandPortal.Web.Controllers;
[Route("tra-cuu-ho-so"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class InquiryLookupController:Controller
{
    // Deny old grants too. No existence query until a new ownership mechanism is approved.
    [HttpGet(""),HttpGet("ket-qua")]
    public IActionResult Index()=>View("Unavailable");
    [HttpPost("ma"),HttpPost("xac-minh")]
    public IActionResult Deny()=>StatusCode(403,"Tra cứu riêng tư đang tạm khóa chờ cơ chế xác minh chủ hồ sơ được phê duyệt. Mã tiếp nhận, email và CAPTCHA không cấp quyền truy cập.");
}
