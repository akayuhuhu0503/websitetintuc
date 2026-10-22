using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace LandPortal.Web.Services;

public sealed class AdminMfaFlow(IDataProtectionProvider protection)
{
    sealed record Flow(string UserId,string Stamp,string ReturnUrl,DateTimeOffset ExpiresUtc);
    readonly IDataProtector protector=protection.CreateProtector("LandPortal.Admin.MfaFlow.v1");
    public void Start(HttpContext context,PortalUser user,string returnUrl,bool enrollment=false)
    {
        var name=enrollment?"LandPortal.MfaEnroll":"LandPortal.MfaPending";
        var path=enrollment?"/admin/account/mfa":"/admin";
        var expires=DateTimeOffset.UtcNow.AddMinutes(5);
        context.Response.Cookies.Append(name,protector.Protect(JsonSerializer.Serialize(new Flow(user.Id,user.SecurityStamp??"",returnUrl,expires))),new(){HttpOnly=true,Secure=context.Request.IsHttps,SameSite=SameSiteMode.Strict,Path=path,Expires=expires,IsEssential=true});
    }
    public string? Read(HttpContext context,PortalUser user,bool enrollment=false)
    {
        try{
            var value=context.Request.Cookies[enrollment?"LandPortal.MfaEnroll":"LandPortal.MfaPending"];if(value==null)return null;
            var flow=JsonSerializer.Deserialize<Flow>(protector.Unprotect(value));
            return flow!=null&&flow.ExpiresUtc>DateTimeOffset.UtcNow&&flow.UserId==user.Id&&flow.Stamp==user.SecurityStamp&&!user.IsDisabled&&(user.LockoutEnd==null||user.LockoutEnd<=DateTimeOffset.UtcNow)?flow.ReturnUrl:null;
        }catch(System.Security.Cryptography.CryptographicException){return null;}catch(JsonException){return null;}
    }
    public void Clear(HttpContext context,bool enrollment=false)=>context.Response.Cookies.Delete(enrollment?"LandPortal.MfaEnroll":"LandPortal.MfaPending",new(){Path=enrollment?"/admin/account/mfa":"/admin",Secure=context.Request.IsHttps,SameSite=SameSiteMode.Strict});
}
