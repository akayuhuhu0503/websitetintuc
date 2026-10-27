using System.Security.Claims;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace LandPortal.Web.Controllers;
public sealed class AdminSessionController(SignInManager<PortalUser> signIn,IAntiforgery antiforgery,PublicCaptchaService captcha):Controller
{
    [Authorize,HttpGet("admin/session")]
    public async Task<IActionResult> Status()
    {
        Response.Headers.CacheControl="no-store";
        var ticket=await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        return Json(new{userId=User.FindFirstValue(ClaimTypes.NameIdentifier),expiresUtc=ticket.Properties?.ExpiresUtc,csrf=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    }
    private string Address() => "admin-reauth:" + (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    [AllowAnonymous,HttpGet("admin/reauthenticate/challenge"),EnableRateLimiting("login")]
    public async Task<IActionResult> ReauthenticationChallenge()
    {
        Response.Headers.CacheControl="no-store, private";
        try { var prompt=await captcha.IssueAsync(InteractionBrowserSession.Get(HttpContext),Address(),"AdminReauth");return Json(new{captchaId=prompt.Id,question=prompt.Question,expiresUtc=prompt.ExpiresUtc,csrf=antiforgery.GetAndStoreTokens(HttpContext).RequestToken}); }
        catch(PublicCaptchaException error){return StatusCode(error.StatusCode,new{message=error.Message});}
    }
    [AllowAnonymous,HttpPost("admin/reauthenticate"),EnableRateLimiting("login")]
    public async Task<IActionResult> Reauthenticate(string? userName,string? password,string? expectedUserId,Guid captchaId,string? captchaAnswer)
    {
        Response.Headers.CacheControl="no-store";
        try { await captcha.ConsumeAsync(captchaId,captchaAnswer,InteractionBrowserSession.Get(HttpContext),Address(),"AdminReauth"); }
        catch(PublicCaptchaException error){return StatusCode(error.StatusCode,new{message=error.Message});}
        if(string.IsNullOrWhiteSpace(userName)||string.IsNullOrEmpty(password)||string.IsNullOrWhiteSpace(expectedUserId)||userName.Length>256||password.Length>256||expectedUserId.Length>450)return BadRequest();
        var user=await signIn.UserManager.FindByNameAsync(userName);
        if(user is null||user.Id!=expectedUserId||user.IsDisabled||user.MustChangePassword)return StatusCode(401,new{message="Xác thực lại bằng đúng tài khoản đã mở bản soạn."});
        var result=await signIn.PasswordSignInAsync(user,password,false,true);
        if(!result.Succeeded)return StatusCode(401,new{message="Không xác thực được. Nội dung đang nhập vẫn giữ trong trang này."});
        return Json(new{userId=user.Id});
    }
}
