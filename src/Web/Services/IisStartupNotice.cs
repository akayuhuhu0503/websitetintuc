namespace LandPortal.Web.Services;

// An unbound UAT package serves no application endpoints and never opens SQL.
// This makes a missing deployment step readable without relaxing activation.
public static class IisStartupNotice
{
    public static bool IsUat(IConfiguration config,string environment)=>environment=="Staging"&&config["Deployment:Profile"]=="UAT";
    public static string? MissingBinding(IConfiguration config,string environment)
    {
        if(!IsUat(config,environment))return null;
        if(LandPortal.Infrastructure.PortableIisConfiguration.Enabled)return null;
        var home=Environment.GetEnvironmentVariable("LANDPORTAL_HOME");
        var file=Environment.GetEnvironmentVariable("LANDPORTAL_CONFIG");
        if(string.IsNullOrWhiteSpace(home)||string.IsNullOrWhiteSpace(file))return "IIS-CONFIG-001";
        if(!Path.IsPathFullyQualified(home)||!Path.IsPathFullyQualified(file)||!File.Exists(file))return "IIS-CONFIG-002";
        return null;
    }
    public static void MapBlocked(WebApplication app,string code)
    {
        var stage=code.StartsWith("IIS-CONFIG-003-",StringComparison.Ordinal)?code[15..]:"TECHNICAL";
        var hint=LandPortal.Infrastructure.PortableIisConfiguration.StartupHint(stage);
        Console.Error.WriteLine(code+": "+hint);
        app.UsePrivateRuntimeProtection();
        app.Run(async context=>{
            context.Response.StatusCode=StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType="text/html; charset=utf-8";
            context.Response.Headers.CacheControl="no-store";
            context.Response.Headers.ContentSecurityPolicy="default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
            await context.Response.WriteAsync("<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Chưa hoàn tất cấu hình website</title><h1>Website chưa đủ điều kiện khởi động</h1><p>"+System.Text.Encodings.Web.HtmlEncoder.Default.Encode(hint)+"</p><p>Người triển khai không cần tạo uat.json. Không tắt kiểm tra bảo mật để chạy website.</p><p>Mã kiểm tra: "+System.Text.Encodings.Web.HtmlEncoder.Default.Encode(code)+". Chức năng quản trị và kết nối cơ sở dữ liệu chưa được mở.</p></html>");
        });
    }
}
