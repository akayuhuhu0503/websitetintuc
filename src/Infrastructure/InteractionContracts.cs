using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
namespace LandPortal.Infrastructure;
public sealed class InteractionInput
{
    public InteractionKind Kind { get; set; }
    public Guid? ContentId { get; set; }
    public Guid? PublishedRevisionId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? AttachmentId { get; set; }
    [StringLength(120),System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Field { get; set; }
    [Required, StringLength(120)] public string Name { get; set; }="";
    [Required, StringLength(254)] public string Email { get; set; }="";
    [StringLength(30),DisplayFormat(ConvertEmptyStringToNull=false)] public string Phone { get; set; }="";
    [StringLength(200),DisplayFormat(ConvertEmptyStringToNull=false)] public string Title { get; set; }="";
    [Required, StringLength(8000,MinimumLength=3)] public string Text { get; set; }="";
    public bool PublicConsent { get; set; }
    [StringLength(0),DisplayFormat(ConvertEmptyStringToNull=false)] public string Website { get; set; }="";
    public void Validate()
    {
        if(!Enum.IsDefined(Kind))throw new InvalidOperationException("Loại hồ sơ không hợp lệ.");
        if((Kind is not (InteractionKind.Inquiry or InteractionKind.Contact)&&AttachmentId!=null)||(Kind!=InteractionKind.Inquiry&&Field!=null))throw new ValidationException("Tệp chỉ dành cho hỏi đáp/hòm thư; lĩnh vực chỉ dành cho hỏi đáp.");
        if(Field!=null){Field=Field.Trim().Normalize(NormalizationForm.FormC);if(Field.Length<1||Field.Any(char.IsControl))throw new ValidationException("Lĩnh vực không hợp lệ.");}
        Name=Name.Trim().Normalize(NormalizationForm.FormC);Email=Email.Trim();Title=Title.Trim().Normalize(NormalizationForm.FormC);Text=Text.Trim().Normalize(NormalizationForm.FormC);Phone=Phone.Trim();
        Validator.ValidateObject(this,new ValidationContext(this),true);
        if(!MailAddress.TryCreate(Email,out var mail)||mail.Address!=Email||Email.Any(char.IsControl))throw new ValidationException("Email không hợp lệ.");
        // Domain is case-insensitive; preserve local-part identity (no provider-specific dot/plus rewriting).
        var at=Email.LastIndexOf('@');Email=Email[..(at+1)]+Email[(at+1)..].ToLowerInvariant();
        if(Kind is InteractionKind.Inquiry or InteractionKind.Contact && Title.Length<3)throw new ValidationException("Nhập tiêu đề (ít nhất 3 ký tự).");
        if(Name.Any(char.IsControl)||Title.Any(char.IsControl)||Phone.Any(char.IsControl))throw new ValidationException("Trường thông tin chứa ký tự không hợp lệ.");
        if(Kind is InteractionKind.Contact or InteractionKind.Consultation)PublicConsent=false;
    }
    public string Hash()=>TextNormalizer.Hash(JsonSerializer.Serialize(this));
}
public sealed record InteractionReceipt(Guid Id,bool AlreadyApplied)
{
    public string? PublicCode { get; init; }
    public bool MailQueued { get; init; }
}
public sealed record InteractionMail(string Email,string Subject,string Body);
public sealed record PublicInteraction(string DisplayName,string Text,string AgencyAnswer,DateTimeOffset ApprovedUtc);
public static class InteractionPermissions
{
    public static bool CanList(ContentActor actor,InteractionKind kind)=>actor.Has(Read(kind))||(kind==InteractionKind.Inquiry&&actor.Has(Permissions.InquiryList))||(kind==InteractionKind.Contact&&actor.Has(Permissions.ContactList));
    public static string Read(InteractionKind kind)=>kind switch{InteractionKind.Comment=>Permissions.CommentRead,InteractionKind.Inquiry=>Permissions.InquiryRead,InteractionKind.Contact=>Permissions.ContactRead,_=>Permissions.ConsultationRead};
    public static string Process(InteractionKind kind)=>kind switch{InteractionKind.Comment=>Permissions.CommentModerate,InteractionKind.Inquiry=>Permissions.InquiryRespond,InteractionKind.Contact=>Permissions.ContactProcess,_=>Permissions.ConsultationProcess};
}
