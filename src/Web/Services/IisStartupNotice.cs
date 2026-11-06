namespace LandPortal.Web.Services;

// An unbound UAT package serves no application endpoints and never opens SQL.
// This makes a missing deployment step readable without relaxing activation.
public static class IisStartupNotice
{
    public static bool IsUat(IConfiguration config,string environment)=>environment=="Staging"&&config["Deployment:Profile"]=="UAT";
    public static string? MissingBinding(IConfiguration config,string environment)
    {
        if(!IsUat(config,environment))return null;
        var home=Environment.GetEnvironmentVariable("LANDPORTAL_HOME");
        var file=Environment.GetEnvironmentVariable("LANDPORTAL_CONFIG");
        if(string.IsNullOrWhiteSpace(home)||string.IsNullOrWhiteSpace(file))return "IIS-CONFIG-001";
        if(!Path.IsPathFullyQualified(home)||!Path.IsPathFullyQualified(file)||!File.Exists(file))return "IIS-CONFIG-002";
        return null;
    }
    public static void MapBlocked(WebApplication app,string code)
    {
        Console.Error.WriteLine(code+": Chưa gắn cấu hình private hợp lệ. Chạy CAU-HINH-IIS.cmd với cùng file .env; không tự tạo uat.json hoặc tắt guard.");
        app.Run(async context=>{
            context.Response.StatusCode=StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType="text/html; charset=utf-8";
            context.Response.Headers.CacheControl="no-store";
            context.Response.Headers.ContentSecurityPolicy="default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
            await context.Response.WriteAsync("<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Chưa hoàn tất cấu hình website</title><h1>Website chưa đủ điều kiện khởi động</h1><p>Cán bộ triển khai cần hoàn tất cấu hình IIS bằng file .env đã được cấp.</p><p>Chạy CAU-HINH-IIS.cmd trong thư mục Publish. Công cụ sẽ tự tạo và gắn cấu hình private, đồng thời báo rõ những điều kiện còn thiếu.</p><p>Mã kiểm tra: "+code+". Chưa mở chức năng quản trị hoặc kết nối cơ sở dữ liệu.</p></html>");
        });
    }
}
