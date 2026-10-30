using Microsoft.AspNetCore.Authorization;

namespace LandPortal.Web.Services;

public static class FirstLoginPasswordGate
{
    public static bool Restricts(HttpContext context)
    {
        if(context.User.Identity?.IsAuthenticated!=true)return false;
        if(context.Request.Path.Equals("/admin/account/change-password",StringComparison.OrdinalIgnoreCase)||
           context.Request.Path.Equals("/admin/account/logout",StringComparison.OrdinalIgnoreCase))return false;
        var endpoint=context.GetEndpoint();
        return context.Request.Path.StartsWithSegments("/admin")||
            (endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count>0&&endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null);
    }
    public static async Task RejectAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl="no-store, private";
        if(context.Request.Headers["X-Editor-Request"]=="true")
        {
            context.Response.StatusCode=403;
            await context.Response.WriteAsJsonAsync(new{code="password_change_required",message="Bạn phải đổi mật khẩu tạm trước khi sử dụng chức năng quản trị."});
        }
        else context.Response.Redirect("/admin/account/change-password");
    }
}
