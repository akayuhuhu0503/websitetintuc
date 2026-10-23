using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace LandPortal.Web.Models;

public sealed class VietnameseValidationMetadata:IValidationMetadataProvider,IDisplayMetadataProvider
{
    public void CreateDisplayMetadata(DisplayMetadataProviderContext context)
    {
        if(context.Attributes.OfType<DisplayAttribute>().Any())return;
        var label=context.Key.Name switch
        {
            "Name"=>"Tên","Title"=>"Tiêu đề","Summary"=>"Mô tả ngắn","Text"=>"Nội dung","BodyHtml"=>"Nội dung bài","Author"=>"Tác giả","Email"=>"Email","Phone"=>"Điện thoại","Field"=>"Lĩnh vực",
            "DisplayName"=>"Tên hiển thị","UserName"=>"Tên đăng nhập","Password"=>"Mật khẩu","TemporaryPassword"=>"Mật khẩu tạm","Reason"=>"Lý do","Answer"=>"Câu trả lời","Decision"=>"Quyết định","Evidence"=>"Căn cứ xác nhận",
            "CaptchaAnswer"=>"Câu trả lời xác thực","Code"=>"Mã xác thực","DueVietnam"=>"Thời điểm Việt Nam","From"=>"Từ ngày","To"=>"Đến ngày","RowVersion"=>"Phiên bản đang thao tác","CommandId"=>"Mã thao tác","ContentId"=>"Mã nội dung","CategoryId"=>"Chuyên mục","Permissions"=>"Quyền được giao",_=>null
        };
        if(label!=null)context.DisplayMetadata.DisplayName=()=>label;
    }
    public void CreateValidationMetadata(ValidationMetadataProviderContext context)
    {
        foreach(var attribute in context.ValidationMetadata.ValidatorMetadata.OfType<ValidationAttribute>())
        {
            if(attribute.ErrorMessage!=null||attribute.ErrorMessageResourceName!=null)continue;
            attribute.ErrorMessage=attribute switch
            {
                RequiredAttribute=>"{0} là thông tin bắt buộc.",
                StringLengthAttribute s when s.MinimumLength>0=>"{0} cần từ {2} đến {1} ký tự.",
                StringLengthAttribute=>"{0} tối đa {1} ký tự.",
                EmailAddressAttribute=>"{0} chưa đúng định dạng email.",
                RangeAttribute=>"{0} phải nằm trong khoảng {1} đến {2}.",
                RegularExpressionAttribute=>"{0} chưa đúng định dạng yêu cầu.",
                _=>"{0} chưa hợp lệ. Vui lòng kiểm tra lại."
            };
        }
    }
}
