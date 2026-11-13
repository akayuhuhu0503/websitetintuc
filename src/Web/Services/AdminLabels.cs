using LandPortal.Application;

namespace LandPortal.Web.Services;

// Display only. Permission/enum/form/API/SQL values are never translated.
public static class AdminLabels
{
    public static readonly IReadOnlyDictionary<string,string> PermissionNames=new Dictionary<string,string>(StringComparer.Ordinal)
    {
        [Permissions.Create]="Tạo bài viết",[Permissions.ViewOwn]="Xem bài của mình",[Permissions.ViewAll]="Xem tất cả bài viết",
        [Permissions.EditOwn]="Sửa bài của mình",[Permissions.EditAll]="Sửa tất cả bài viết",[Permissions.Submit]="Gửi bài duyệt",
        [Permissions.PublishDirect]="Xuất bản bài viết trực tiếp",[Permissions.PublishApproved]="Xuất bản bài đã duyệt",[Permissions.Review]="Duyệt bài viết",
        [Permissions.Unpublish]="Gỡ bài công khai",[Permissions.Trash]="Chuyển bài vào thùng rác",[Permissions.Restore]="Khôi phục bài viết",
        [Permissions.ManageMedia]="Quản lý tệp đa phương tiện",[Permissions.ManageVideos]="Quản lý video tải trực tiếp",[Permissions.PublishDisclosure]="Xuất bản thông tin công khai",
        [Permissions.Moderate]="Kiểm duyệt tương tác",[Permissions.ManageSettings]="Quản lý thiết lập",[Permissions.ManageUsers]="Quản lý tài khoản",
        [Permissions.ManageRoles]="Quản lý vai trò",[Permissions.GrantPermissions]="Cấp quyền",[Permissions.GrantPrivileged]="Cấp quyền đặc biệt",
        [Permissions.ManageSite]="Biên tập cấu hình website",[Permissions.PublishSite]="Áp dụng cấu hình website",[Permissions.ViewSite]="Xem cấu hình website",
        [Permissions.Schedule]="Hẹn giờ xuất bản",[Permissions.Import]="Chuyển dữ liệu nguồn",
        [Permissions.CommentRead]="Đọc bình luận riêng",[Permissions.CommentModerate]="Kiểm duyệt bình luận",[Permissions.CommentReply]="Trả lời bình luận",
        [Permissions.InquiryRead]="Đọc hồ sơ hỏi đáp",[Permissions.InquiryList]="Xem danh sách hỏi đáp",[Permissions.InquiryAssign]="Phân công xử lý hỏi đáp",
        [Permissions.InquiryRespond]="Trả lời hỏi đáp",[Permissions.InquiryPublish]="Duyệt hỏi đáp công khai",[Permissions.InquirySupervise]="Giám sát xử lý hỏi đáp",
        [Permissions.InquiryFilesRead]="Tải tệp hỏi đáp riêng",[Permissions.InquiryExport]="Xuất bảng hỏi đáp",[Permissions.InquiryExportPii]="Xuất thông tin liên hệ hỏi đáp",
        [Permissions.InquiryCalendarManage]="Quản lý lịch xử lý hỏi đáp",[Permissions.InquiryExtend]="Gia hạn hỏi đáp",
        [Permissions.ContactRead]="Đọc hòm thư góp ý",[Permissions.ContactList]="Xem danh sách góp ý",[Permissions.ContactProcess]="Xử lý hòm thư góp ý",
        [Permissions.ContactFilesRead]="Tải tệp góp ý riêng",[Permissions.ContactExportPii]="Xuất thông tin liên hệ góp ý",
        [Permissions.ConsultationRead]="Đọc góp ý dự thảo",[Permissions.ConsultationProcess]="Xử lý góp ý dự thảo",[Permissions.InteractionExport]="Xuất bảng tương tác",
        [Permissions.OutboxReconcile]="Đối soát thư chưa rõ kết quả"
    };
    public static readonly IReadOnlyDictionary<string,string> States=new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["Draft"]="Bản nháp",["PendingReview"]="Chờ duyệt",["Approved"]="Đã duyệt",["Published"]="Đã đăng",["Rejected"]="Bị trả lại",["Unpublished"]="Đã gỡ",["Archived"]="Trong thùng rác",
        ["VIDEO_PROCESSOR_CONFIGURED"]="Bộ xử lý sẵn sàng: phiên bản và encoder đã kiểm tra",
        ["VIDEO_PROCESSOR_ACCESS_DENIED"]="Tài khoản tiến trình không có quyền đọc/chạy bộ xử lý",
        ["VIDEO_PROCESSOR_IO_ERROR"]="Không đọc được bộ xử lý hoặc kho tạm",
        ["VIDEO_PROCESSOR_VERSION_INVALID"]="Không nhận diện được phiên bản FFmpeg/FFprobe",
        ["VIDEO_PROCESSOR_CHANGED_DURING_CHECK"]="Binary đã thay đổi trong lúc kiểm tra; chưa xử lý video",
        ["VIDEO_TOOL_PATH_UNSAFE"]="Đường dẫn công cụ không an toàn; không dùng thư mục upload, đường dẫn tương đối hoặc liên kết",
        ["VIDEO_TOOL_PACKAGE_INVALID"]="Bộ công cụ video không khớp gói triển khai; cần cập nhật nguyên thư mục web từ bản Publish hợp lệ",
        ["VIDEO_WORKER_UNKNOWN"]="Chưa nhận được trạng thái bộ xử lý trong website; kiểm tra cấu hình video và nhật ký ứng dụng",
        ["VIDEO_WORKER_STATUS_MISSING"]="Chưa có bản ghi trạng thái bộ xử lý; kiểm tra chức năng video đã bật và ứng dụng đang chạy",
        ["VIDEO_WORKER_STATUS_ACCESS_DENIED"]="Ứng dụng không đọc được trạng thái xử lý hoặc khóa bảo vệ; kiểm tra quyền truy cập kho riêng",
        ["VIDEO_WORKER_STATUS_UNVERIFIED"]="Không xác minh được trạng thái xử lý; kiểm tra kho khóa bảo vệ hoặc bản ghi bị thay đổi",
        ["VIDEO_WORKER_STATUS_INVALID"]="Bản ghi trạng thái xử lý không hợp lệ; kiểm tra gói triển khai và nhật ký ứng dụng",
        ["VIDEO_WORKER_STATUS_PATH_UNSAFE"]="Đường dẫn trạng thái xử lý không an toàn; không dùng liên kết thư mục",
        ["VIDEO_WORKER_STATUS_READ_FAILED"]="Chưa đọc được trạng thái xử lý; kiểm tra kho tạm và nhật ký ứng dụng",
        ["VIDEO_WORKER_STALE"]="Bộ xử lý chưa cập nhật; ứng dụng có thể đang nghỉ hoặc khởi động lại",
        ["VIDEO_WORKER_STOPPED"]="Bộ xử lý đã dừng cùng ứng dụng; hàng đợi tiếp tục ở lần khởi động kế tiếp",
        ["VIDEO_WORKER_ERROR"]="Bộ xử lý gặp lỗi; kiểm tra nhật ký ứng dụng",
        ["VIDEO_WORKER_READY"]="Bộ xử lý trong website đang hoạt động và sẵn sàng",
        ["VIDEO_WORKER_BLOCKED"]="Bộ xử lý trong website đang chạy nhưng chưa đủ điều kiện xử lý",
        ["VIDEO_HOST_STOPPED"]="Tạm dừng do ứng dụng khởi động lại; video được giữ trong hàng đợi để xử lý tiếp",
        ["VIDEO_PROCESSOR_NOT_AVAILABLE"]="Chưa tìm thấy FFmpeg/FFprobe trong tools/ffmpeg hoặc PATH của tiến trình",
        ["VIDEO_DISABLED"]="Chức năng video chưa được bật trong môi trường hợp lệ",
        ["VIDEO_PROCESSOR_HASH_MISMATCH"]="SHA256 FFmpeg/FFprobe không khớp cấu hình",
        ["VIDEO_ENCODER_NOT_AVAILABLE"]="FFmpeg thiếu encoder H.264 libx264, AAC hoặc JPEG mjpeg",
        ["VIDEO_PROCESSOR_START_FAILED"]="Không khởi chạy được FFmpeg/FFprobe",
        ["VIDEO_PROCESSOR_REJECTED"]="FFmpeg/FFprobe từ chối tệp",
        ["VIDEO_PROCESSOR_TIMEOUT"]="Xử lý video vượt thời gian cho phép",
        ["VIDEO_PROCESSOR_MEMORY_LIMIT"]="Xử lý video vượt giới hạn bộ nhớ",
        ["VIDEO_PROCESSOR_OUTPUT_LIMIT"]="Thông tin trả về của bộ xử lý vượt giới hạn",
        ["VIDEO_PROCESSOR_CANCELLED"]="Đã dừng xử lý video",
        ["Ready"]="Sẵn sàng",["Disabled"]="Chưa được bật",["NotScanned"]="Chưa quét an toàn",["Clean"]="Đã quét sạch",["Infected"]="Phát hiện mã độc",["ScanFailed"]="Quét chưa thành công",["Scanning"]="Đang quét",["Deleted"]="Đã xóa theo chính sách",
        ["Pending"]="Đang chờ",["Running"]="Đang thực hiện",["Completed"]="Đã hoàn thành",["Cancelled"]="Đã hủy",["Canceled"]="Đã hủy",["Failed"]="Chưa thành công",["Blocked"]="Đang bị chặn",["NeedsReview"]="Cần kiểm tra lại",
        ["Uploading"]="Đang tải tệp",["Finalizing"]="Đang xác nhận tệp",["Uploaded"]="Đã tải đủ tệp",["Processing"]="Đang xử lý",["Queued"]="Đã vào hàng đợi",["Expired"]="Đã hết hạn",["RejectedFile"]="Tệp bị từ chối",
        ["Accepted"]="Máy chủ thư đã tiếp nhận",["Uncertain"]="Chưa rõ kết quả gửi",["Leased"]="Đang gửi",["Answered"]="Đã trả lời",["Closed"]="Đã đóng",
        ["Article"]="Bài viết",["Announcement"]="Thông báo",["StaticPage"]="Trang thông tin",["LegalDocument"]="Văn bản pháp luật",["DraftDocument"]="Văn bản dự thảo",["Video"]="Video",
        ["main"]="Menu chính",["footer"]="Chân trang",["links"]="Liên kết website",["supplementary"]="Thông tin bổ sung",["header"]="Đầu trang",["home"]="Trang chủ",["sidebar"]="Cột bên",["home_event_strip"]="Dải sự kiện sau Tin mới cập nhật",
        ["Route"]="Trang hệ thống",["Category"]="Chuyên mục",["Content"]="Nội dung công khai",["External"]="Liên kết ngoài",["Group"]="Nhóm menu",
        ["Featured"]="Tin nổi bật",["Recent"]="Tin mới",["Announcements"]="Thông báo",["LegalDocuments"]="Văn bản pháp luật",["DraftDocuments"]="Văn bản dự thảo",["Videos"]="Video",["Links"]="Liên kết website",
        ["Authority"]="Cơ quan ban hành",["Type"]="Loại văn bản",["Field"]="Lĩnh vực",["Inline"]="Trong nội dung",["Attachment"]="Tệp đính kèm",["Cover"]="Ảnh đại diện",["Thumbnail"]="Ảnh thu nhỏ",["Subtitle"]="Phụ đề"
    };
    public static string Permission(string code)=>PermissionNames.GetValueOrDefault(code,"Quyền chưa được mô tả");
    public static string State(object? code)=>string.IsNullOrWhiteSpace(code?.ToString())?"Chưa có":States.GetValueOrDefault(code.ToString()!,"Cần kiểm tra trạng thái");
    public static string Role(string value)=>value switch{"Administrator"=>"Quản trị hệ thống","Editor"=>"Người viết","Publisher"=>"Người duyệt và xuất bản",_=>value};
    public static string Action(string value)=>value switch
    {
        "SaveDraft"=>"Lưu bản nháp","Autosave"=>"Tự lưu bản nháp","Submit"=>"Gửi duyệt","Approve"=>"Duyệt bài","Reject"=>"Trả lại bài","PublishDirect"=>"Xuất bản trực tiếp","PublishApproved"=>"Xuất bản bài đã duyệt","Unpublish"=>"Gỡ bài","Trash"=>"Chuyển vào thùng rác","RestoreRevision"=>"Khôi phục phiên bản","RestoreTrash"=>"Khôi phục bài","BeginEdit"=>"Mở bản sửa","Clone"=>"Nhân bản bài",
        "AdminPasswordLogin"=>"Đăng nhập bằng mật khẩu","AdminPasswordFailed"=>"Đăng nhập chưa thành công","AdminPasswordLocked"=>"Đăng nhập bị khóa tạm thời",
        "SiteSaveDraft"=>"Lưu cấu hình nháp","SitePublish"=>"Áp dụng cấu hình","SiteRestoreDraft"=>"Khôi phục cấu hình","DisclosureCatalogConfirmed"=>"Xác nhận danh mục công khai thông tin",
        _=>value.StartsWith("interaction.",StringComparison.Ordinal)?"Xử lý tương tác":value.StartsWith("document.",StringComparison.Ordinal)?"Kiểm tra tài liệu":value.StartsWith("Account",StringComparison.Ordinal)?"Quản lý tài khoản":value.StartsWith("Role",StringComparison.Ordinal)?"Quản lý nhóm quyền":"Thao tác hệ thống"
    };
    public static string Diagnostic(string? code)=>string.IsNullOrWhiteSpace(code)?"Không có cảnh báo":"Cần cán bộ kỹ thuật kiểm tra nhật ký xử lý.";
}
