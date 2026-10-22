using LandPortal.Domain;
using LandPortal.Infrastructure;
namespace LandPortal.Web.Models;
public sealed class InteractionForm
{
    public InteractionInput Input { get; set; }=new();
    public Guid? ChallengeId { get; set; }
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull=false)] public string Code { get; set; }="";
    public bool MailAvailable { get; set; }
    public string Message { get; set; }="";
    public Guid CaptchaId { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(3),System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull=false)] public string CaptchaAnswer { get; set; }="";
    public string CaptchaQuestion { get; set; }="";
}
public sealed record InteractionInbox(InteractionKind Kind,int Total,int Page,IReadOnlyList<InteractionSubmission> Items,IReadOnlyDictionary<InteractionKind,int> Pending)
{
    public string? Field { get; init; }
    public bool CanReadOriginal { get; init; }=true;
    public string? Due { get; init; }
    public InteractionState? State { get; init; }
    public IReadOnlyDictionary<string,string> Assignees { get; init; }=new Dictionary<string,string>();
}
public sealed record CommentReceptionRow(Guid Id,bool Enabled,string RowVersion);
public sealed record InteractionDetail(InteractionSubmission Submission,InteractionInput Original,string Answer,IReadOnlyList<InteractionOutbox> Mail,bool CanProcess,bool CanPublish,bool CanReply)
{
    public IReadOnlyDictionary<string,string> Assignees { get; init; }=new Dictionary<string,string>();
    public LandPortal.Domain.InquiryAttachment? Attachment { get; init; }
    public bool CanAssign { get; init; }
    public bool CanExtend { get; init; }
    public bool CanReadFile { get; init; }
    public IReadOnlyList<InquiryOperationHistory> History { get; init; }=[];
}
public sealed record InquiryOperationHistory(DateTimeOffset Utc,string Action,string Reason);
public sealed class InteractionAction
{
    public Guid CommandId { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(128),System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull=false)] public string RowVersion { get; set; }="";
    [System.ComponentModel.DataAnnotations.StringLength(30),System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull=false)] public string Action { get; set; }="";
    [System.ComponentModel.DataAnnotations.StringLength(120),System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull=false)] public string DisplayName { get; set; }="";
    [System.ComponentModel.DataAnnotations.StringLength(8000),System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull=false)] public string Text { get; set; }="";
    [System.ComponentModel.DataAnnotations.StringLength(8000),System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull=false)] public string Answer { get; set; }="";
    [System.ComponentModel.DataAnnotations.StringLength(500),System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull=false)] public string Reason { get; set; }="";
}
