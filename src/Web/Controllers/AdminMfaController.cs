using System.Security.Claims;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LandPortal.Web.Controllers;
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class AdminMfaController(SignInManager<PortalUser> signIn,UserManager<PortalUser> users,PortalDbContext db,AdminMfaService mfa,AdminMfaFlow flow,IConfiguration config):Controller
{
    async Task<PortalUser?> Current()=>await users.GetUserAsync(User);
    [AllowAnonymous,HttpGet("admin/mfa")]
    public async Task<IActionResult> ChallengeView()
    {
        var user=await signIn.GetTwoFactorAuthenticationUserAsync();
        if(user==null||flow.Read(HttpContext,user)==null)return Redirect("/admin/login");
        return View("~/Views/Accounts/MfaChallenge.cshtml",new MfaCodeModel());
    }
    [AllowAnonymous,HttpPost("admin/mfa"),EnableRateLimiting("login")]
    public async Task<IActionResult> Challenge(MfaCodeModel model)
    {
        var user=await signIn.GetTwoFactorAuthenticationUserAsync();
        var target=user==null?null:flow.Read(HttpContext,user);
        if(user==null||target==null||user.MustChangePassword||await users.IsLockedOutAsync(user))return Redirect("/admin/login");
        var accepted=false;
        if(ModelState.IsValid)
        {
            if(model.Recovery)accepted=(await signIn.TwoFactorRecoveryCodeSignInAsync(model.Code.Replace(" ",""))).Succeeded;
            else if(await mfa.ConsumeAuthenticatorAsync(user,model.Code))accepted=(await signIn.TwoFactorAuthenticatorSignInAsync(AdminMfaService.Normalize(model.Code),false,false)).Succeeded;
            else await users.AccessFailedAsync(user);
        }
        if(accepted){flow.Clear(HttpContext);db.AuditLogs.Add(new(){Actor=user.Id,Action="AdminMfaLogin",Detail=model.Recovery?"RecoveryCode":"Authenticator"});await db.SaveChangesAsync();return LocalRedirect(Url.IsLocalUrl(target)?target:"/admin");}
        if(ModelState.IsValid&&model.Recovery)await users.AccessFailedAsync(user);
        model.Code="";ModelState.Remove(nameof(model.Code));ModelState.AddModelError("","Mã không hợp lệ, đã dùng hoặc phiên xác thực đã hết hạn.");Response.StatusCode=400;
        return View("~/Views/Accounts/MfaChallenge.cshtml",model);
    }
    [Authorize,HttpGet("admin/account/mfa")]
    public async Task<IActionResult> Manage()
    {
        var user=await Current();if(user==null||user.IsDisabled||user.MustChangePassword)return Forbid();
        return ManageView(user,flow.Read(HttpContext,user,true)!=null&&!user.TwoFactorEnabled?await users.GetAuthenticatorKeyAsync(user):null);
    }
    IActionResult ManageView(PortalUser user,string? key=null,string[]? codes=null)=>View("~/Views/Accounts/MfaManage.cshtml",new MfaManageModel{Enabled=user.TwoFactorEnabled,SharedKey=key,RecoveryCodes=codes??[],Required=UatDeployment.RequireMfa(config)});
    [Authorize,HttpPost("admin/account/mfa/start"),EnableRateLimiting("login")]
    public async Task<IActionResult> Start(string? password)
    {
        var user=await Current();if(user==null||user.IsDisabled||user.MustChangePassword||user.TwoFactorEnabled)return Forbid();
        if(string.IsNullOrEmpty(password)||password.Length>256||!(await signIn.CheckPasswordSignInAsync(user,password,true)).Succeeded)return BadRequest("Không xác thực được mật khẩu hiện tại.");
        if(string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(user))){var result=await users.ResetAuthenticatorKeyAsync(user);if(!result.Succeeded)return Conflict();await signIn.RefreshSignInAsync(user);}
        flow.Start(HttpContext,user,"/admin/account/mfa",true);
        return ManageView(user,await users.GetAuthenticatorKeyAsync(user));
    }
    [Authorize,HttpPost("admin/account/mfa/enable"),EnableRateLimiting("login")]
    public async Task<IActionResult> Enable(string? code)
    {
        var user=await Current();if(user==null||user.IsDisabled||user.TwoFactorEnabled||flow.Read(HttpContext,user,true)==null)return Forbid();
        if(!await mfa.ConsumeAuthenticatorAsync(user,code)){await users.AccessFailedAsync(user);return BadRequest("Mã xác thực không hợp lệ hoặc đã dùng.");}
        if(!(await users.SetTwoFactorEnabledAsync(user,true)).Succeeded)return Conflict();
        var codes=(await users.GenerateNewTwoFactorRecoveryCodesAsync(user,10))?.ToArray()??[];
        flow.Clear(HttpContext,true);await signIn.SignInWithClaimsAsync(user,false,new[]{new Claim("amr","mfa")});
        db.AuditLogs.Add(new(){Actor=user.Id,Action="AdminMfaEnabled",Detail="Authenticator; recovery codes generated (not logged)"});await db.SaveChangesAsync();return ManageView(user,codes:codes);
    }
    [Authorize,HttpPost("admin/account/mfa/recovery-codes"),EnableRateLimiting("login")]
    public async Task<IActionResult> RecoveryCodes(string? password,string? code)
    {
        var user=await Current();if(user==null||!user.TwoFactorEnabled||string.IsNullOrEmpty(password)||password.Length>256||!(await signIn.CheckPasswordSignInAsync(user,password,true)).Succeeded||!await mfa.ConsumeAuthenticatorAsync(user,code))return Forbid();
        var codes=(await users.GenerateNewTwoFactorRecoveryCodesAsync(user,10))?.ToArray()??[];
        db.AuditLogs.Add(new(){Actor=user.Id,Action="AdminMfaRecoveryRegenerated",Detail="Previous recovery codes invalidated"});await db.SaveChangesAsync();return ManageView(user,codes:codes);
    }
    [Authorize,HttpPost("admin/account/mfa/reset"),EnableRateLimiting("login")]
    public async Task<IActionResult> Reset(string? password,string? recoveryCode)
    {
        var user=await Current();if(user==null||!user.TwoFactorEnabled||user.IsDisabled||string.IsNullOrEmpty(password)||password.Length>256||!(await signIn.CheckPasswordSignInAsync(user,password,true)).Succeeded||string.IsNullOrEmpty(recoveryCode)||recoveryCode.Length>32)return Forbid();
        if(!(await users.RedeemTwoFactorRecoveryCodeAsync(user,recoveryCode.Replace(" ",""))).Succeeded)return Forbid();
        if(!(await users.SetTwoFactorEnabledAsync(user,false)).Succeeded||!(await users.ResetAuthenticatorKeyAsync(user)).Succeeded||!(await users.UpdateSecurityStampAsync(user)).Succeeded)return Conflict();
        db.AuditLogs.Add(new(){Actor=user.Id,Action="AdminMfaReset",Detail="Own password+one-use recovery; all sessions revoked; enrollment required again"});await db.SaveChangesAsync();await signIn.SignOutAsync();return Redirect("/admin/login");
    }
}
