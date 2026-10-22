using LandPortal.Domain;
namespace LandPortal.Web.Models;
public static class InteractionLabels
{
    public static string Kind(InteractionKind value)=>value switch{InteractionKind.Comment=>"Bình luận",InteractionKind.Inquiry=>"Hỏi đáp",InteractionKind.Contact=>"Liên hệ",_=>"Góp ý dự thảo"};
    public static string State(InteractionState value)=>value switch{InteractionState.Pending=>"Chờ xử lý",InteractionState.Processing=>"Đang xử lý",InteractionState.Answered=>"Đã trả lời",InteractionState.Closed=>"Đã đóng",_=>"Từ chối"};
    public static string Mail(InteractionMailState value)=>value switch{InteractionMailState.Pending=>"Đang xếp hàng",InteractionMailState.Leased=>"Worker đang xử lý",InteractionMailState.Accepted=>"Máy chủ thư đã chấp nhận",InteractionMailState.Failed=>"Gửi thất bại",InteractionMailState.Uncertain=>"Chưa xác định máy chủ đã nhận; cần đối soát",_=>"Đã hủy"};
}
