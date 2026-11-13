using LandPortal.Application;

namespace LandPortal.Web.Models;

// Suggestions change only form checkboxes; the service enforces delegation on save.
public static class RolePermissionSuggestions
{
    public sealed record Suggestion(string Name,string Description,string[] Permissions);
    public static readonly Suggestion[] All = [
        new("Người viết", "Soạn bài của mình, quản lý tệp/video và gửi duyệt. Không duyệt hoặc xuất bản.",
            [Permissions.Create,Permissions.ViewOwn,Permissions.EditOwn,Permissions.Submit,Permissions.ManageMedia,Permissions.ManageVideos]),
        new("Người duyệt", "Xem nội dung trong phạm vi chuyên mục được giao và duyệt/trả bài. Không xuất bản hoặc tự nhận hồ sơ hỏi đáp.",
            [Permissions.ViewAll,Permissions.Review]),
        new("Quản lý nội dung", "Soạn, duyệt, xuất bản bài đã duyệt, gỡ bài và hẹn lịch. Không quản trị tài khoản hoặc cấp quyền.",
            [Permissions.Create,Permissions.ViewAll,Permissions.EditAll,Permissions.Submit,Permissions.Review,Permissions.PublishApproved,Permissions.Unpublish,Permissions.Schedule,Permissions.ManageMedia,Permissions.ManageVideos])
    ];
    public static string Group(string key) => key switch
    {
        _ when key.StartsWith("content.",StringComparison.Ordinal) => "Bài viết và xuất bản",
        _ when key.StartsWith("media.",StringComparison.Ordinal)||key.StartsWith("video.",StringComparison.Ordinal) => "Ảnh, tài liệu và video",
        _ when key.StartsWith("site.",StringComparison.Ordinal)||key.StartsWith("settings.",StringComparison.Ordinal) => "Menu, banner và cấu hình",
        _ when key.StartsWith("interaction.",StringComparison.Ordinal) => "Bình luận, hỏi đáp và liên hệ",
        _ when key.StartsWith("outbox.",StringComparison.Ordinal) => "Đối soát gửi thư",
        _ => "Tài khoản và quản trị hệ thống"
    };
}
