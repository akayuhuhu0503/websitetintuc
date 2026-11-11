namespace LandPortal.Web.Services;

public static class PrivateRuntimeProtection
{
    // Defense in depth: IIS hiddenSegments also rejects these before ANCM.
    public static IApplicationBuilder UsePrivateRuntimeProtection(this IApplicationBuilder app)=>app.Use(async(context,next)=>{
        var path=context.Request.Path.Value??"";
        for(var i=0;i<3;i++) {
            if(path.Replace('\\','/').Split('/').Any(s=>s.TrimEnd('.', ' ').Equals(".runtime",StringComparison.OrdinalIgnoreCase)) ||
                new[]{"/.env","/web.config","/appsettings.json","/private/uat.json"}.Contains(path,StringComparer.OrdinalIgnoreCase) ||
                path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)) {
                context.Response.StatusCode=404;
                context.Response.Headers.CacheControl="no-store";
                return;
            }
            var decoded=Uri.UnescapeDataString(path);
            if(decoded==path)break;
            path=decoded;
        }
        await next();
    });
}
