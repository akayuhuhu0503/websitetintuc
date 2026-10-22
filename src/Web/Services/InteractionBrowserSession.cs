using System.Security.Cryptography;
namespace LandPortal.Web.Services;
public static class InteractionBrowserSession
{
    public static string Get(HttpContext context)
    {
        var value=context.Request.Cookies["LandPortal.InteractionSession"];if(value!=null&&value.Length==64&&value.All(Uri.IsHexDigit))return value;
        value=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));context.Response.Cookies.Append("LandPortal.InteractionSession",value,new(){HttpOnly=true,SameSite=SameSiteMode.Strict,Secure=context.Request.IsHttps,IsEssential=true,MaxAge=TimeSpan.FromHours(1)});return value;
    }
}
