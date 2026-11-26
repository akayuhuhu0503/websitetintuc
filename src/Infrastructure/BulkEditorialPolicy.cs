using LandPortal.Application;
using LandPortal.Domain;

namespace LandPortal.Infrastructure;

public static class BulkEditorialPolicy
{
    public const int SelectionLimit = 100;
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["Submit"] = "Gửi duyệt", ["Approve"] = "Duyệt", ["PublishApproved"] = "Xuất bản bài đã duyệt",
        ["ApproveAndPublish"] = "Duyệt và xuất bản", ["PublishDirect"] = "Đăng trực tiếp"
    };
    public static bool Allowed(ContentActor actor, string action) => action switch
    {
        "Submit" => actor.Has(Permissions.Submit), "Approve" => actor.Has(Permissions.Review),
        "PublishApproved" => actor.Has(Permissions.PublishApproved),
        "ApproveAndPublish" => actor.Has(Permissions.Review) && actor.Has(Permissions.PublishApproved),
        "PublishDirect" => actor.Has(Permissions.PublishDirect), _ => false
    };
    public static string? Reason(ContentActor actor, Content c, string action, bool scheduled)
    {
        if (!Allowed(actor, action)) return "Không có quyền thực hiện thao tác này.";
        if (c.IsTrashed) return "Bài đang trong thùng rác.";
        if (c.IsPublic) return "Bài đang công khai; mở từng bài để xử lý bản sửa.";
        if (scheduled) return "Bài đang hẹn giờ; lịch hiện có được giữ nguyên.";
        if (c.CurrentRevisionId == null) return "Chưa có phiên bản đã lưu.";
        if (action == "Submit" && !CurrentPermissionService.CanEdit(actor, c)) return "Không có quyền sửa bài này.";
        var valid = action switch
        {
            "Submit" => c.State is WorkflowState.Draft or WorkflowState.Rejected,
            "Approve" or "ApproveAndPublish" => c.State == WorkflowState.PendingReview && c.SubmittedRevisionId == c.CurrentRevisionId,
            "PublishApproved" => c.State == WorkflowState.Approved && c.ApprovedRevisionId == c.CurrentRevisionId,
            "PublishDirect" => c.State is WorkflowState.Draft or WorkflowState.Rejected or WorkflowState.PendingReview or WorkflowState.Approved,
            _ => false
        };
        return valid ? null : "Trạng thái hoặc phiên bản không phù hợp với thao tác đã chọn.";
    }
}
