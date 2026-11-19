using LandPortal.MigrationCli;

namespace LandPortal.Infrastructure;

public static class SourceSyncResourcePolicy
{
    public static void ValidateResponse(SourceResponse response,string expectedMime)
    {
        if(response.Status!=200)throw new SourceResourceException("HTTP_SOURCE",response.Status);
        if(response.ContentType.Equals(expectedMime,StringComparison.OrdinalIgnoreCase)||
            response.ContentType.Equals("application/octet-stream",StringComparison.OrdinalIgnoreCase))return;
        // Observed on QLDD articles 3758 and 3767: this host labels JPEG bytes
        // as image/png. Only tolerate this proven header pair. The caller MUST
        // still fully decode as the extension's expected JPEG before acceptance.
        if(new Uri(response.Url).Host.Equals("media-qldd.mae.gov.vn",StringComparison.OrdinalIgnoreCase)&&
            expectedMime=="image/jpeg"&&response.ContentType.Equals("image/png",StringComparison.OrdinalIgnoreCase))return;
        throw new SourceResourceException("CONTENT_TYPE");
    }
}

public sealed class SourceResourceException(string code,int? httpStatus=null):IOException("Source resource rejected: "+code)
{
    public string Code { get; }=code;
    public string SafeSummary => Code switch
    {
        "HTTP_SOURCE"=>$"HTTP nguồn không thành công [HTTP_SOURCE:{httpStatus}].",
        "CONTENT_TYPE"=>"Nguồn trả Content-Type không được chấp nhận [CONTENT_TYPE].",
        "IMAGE_INVALID"=>"Ảnh không giải mã được hoặc định dạng thực không khớp [IMAGE_INVALID].",
        "IMAGE_LIMIT"=>"Ảnh vượt giới hạn kích thước hoặc số khung hình [IMAGE_LIMIT].",
        "FILE_INVALID"=>"Tệp không đạt kiểm định hiện hành [FILE_INVALID].",
        _=>"Tài nguyên chưa đạt kiểm định [RESOURCE_INVALID]."
    };
}
