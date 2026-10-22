using Microsoft.AspNetCore.Authentication;
namespace LandPortal.Web.Services;
public static class AdminSessionPolicy
{
    public const string ActivityKey="LandPortal.LastUserActivityUtc";
    public static bool Expired(AuthenticationProperties properties,DateTimeOffset now,TimeSpan idle)
    {
        properties.Items.TryGetValue(ActivityKey,out var raw);
        var last=DateTimeOffset.TryParse(raw,System.Globalization.CultureInfo.InvariantCulture,out var stored)?stored:properties.IssuedUtc;
        return last is null||now-last>=idle;
    }
    public static bool IsUserActivity(HttpRequest request)=>request.Method=="POST" || request.Method=="GET"&&
        (request.Path.StartsWithSegments("/admin/edit")||request.Path=="/admin/create"||request.Path=="/admin");
    public static void Touch(AuthenticationProperties properties,DateTimeOffset now,TimeSpan idle)
    {properties.Items[ActivityKey]=now.ToString("O");properties.IssuedUtc=now;properties.ExpiresUtc=now+idle;}
}
