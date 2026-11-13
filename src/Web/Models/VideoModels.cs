namespace LandPortal.Web.Models;
public sealed record VideoListItem(Guid Id,string Title,string State,bool IsPublic,DateTimeOffset CreatedUtc)
{
    public string FileState {get;init;}="Chưa có tệp";
    public Guid? AssetId {get;init;}
    public string Uploader {get;init;}="";
}
public sealed class VideoIndexModel
{
    public IReadOnlyList<VideoListItem> Items {get;init;}=[];
    public string? Query {get;init;}
    public string? State {get;init;}
    public int Page {get;init;}
    public int Total {get;init;}
    public bool Enabled {get;init;}
    public bool CanCreate {get;init;}
    public string Processor {get;init;}="";
}
public sealed record VideoUploadModel(Guid ContentId,string Title,string RowVersion,long MaxFileBytes,int ChunkBytes,string Processor);
public sealed record VideoEmbedModel(Guid Id,string Caption,IReadOnlyList<Guid> Tracks);

public static class VideoCmsLabels
{
    public static string Article(string state,bool isPublic) => state switch
    {
        "Approved" => isPublic ? "Bản sửa: Đã duyệt, chưa xuất bản. Bản công khai trước đó vẫn hiển thị." : "Bài viết: Đã duyệt, chưa xuất bản.",
        "PendingReview" => isPublic ? "Bản sửa: Đang chờ duyệt. Bản công khai trước đó vẫn hiển thị." : "Bài viết: Đang chờ duyệt.",
        "Draft" => isPublic ? "Bản sửa: Đang soạn. Bản công khai trước đó vẫn hiển thị." : "Bài viết: Đang soạn nháp.",
        "Rejected" => isPublic ? "Bản sửa: Được trả lại để chỉnh sửa. Bản công khai trước đó vẫn hiển thị." : "Bài viết: Được trả lại để chỉnh sửa.",
        "Published" when isPublic => "Bài viết: Đã xuất bản.",
        _ => isPublic ? "Bài viết: Đang hiển thị công khai." : "Bài viết: Chưa công khai."
    };
    public static string File(string? state) => state switch
    {
        "Uploading" => "Đang tải lên",
        "Completed" or "Pending" => "Đã tải lên, đang chờ xử lý",
        "Scanning" => "Đang kiểm tra tệp",
        "Processing" => "Đang chuyển mã và tạo ảnh đại diện",
        "Ready" => "Đã xử lý, sẵn sàng dùng trong bài",
        "Infected" => "Đã cách ly do phát hiện nguy cơ",
        "ScanFailed" => "Chưa hoàn tất kiểm tra an toàn",
        "Failed" => "Xử lý chưa thành công",
        "Blocked" => "Chưa đủ điều kiện xử lý",
        "PolicyBlocked" => "Tệp chưa đủ điều kiện theo chính sách hiện hành",
        "Cancelled" => "Đã hủy",
        "Expired" => "Lần tải đã hết hạn; chọn tệp để tải lại",
        _ => "Chưa có tệp video"
    };
}
