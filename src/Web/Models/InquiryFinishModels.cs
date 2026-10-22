using System.ComponentModel.DataAnnotations;
using LandPortal.Domain;
namespace LandPortal.Web.Models;
public sealed class InquiryLookupForm
{
    [Required,StringLength(32,MinimumLength=32)]public string Receipt { get; set; }="";
    [Required,EmailAddress,StringLength(254)]public string Email { get; set; }="";
    [DisplayFormat(ConvertEmptyStringToNull=false),StringLength(6)]public string Code { get; set; }="";
    public Guid? ChallengeId { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull=false),StringLength(0)]public string Website { get; set; }="";
    [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]public bool Available { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]public string Message { get; set; }="";
}
public sealed class InquiryAssignForm
{
    public Guid CommandId { get; set; }
    [Required,StringLength(128)]public string RowVersion { get; set; }="";
    [StringLength(450)]public string? AssigneeId { get; set; }
    [Required,StringLength(500)]public string Reason { get; set; }="";
}
public sealed class InquiryExtendForm
{
    public Guid CommandId { get; set; }
    [Required,StringLength(128)]public string RowVersion { get; set; }="";
    public DateTime DueVietnam { get; set; }
    [Required,StringLength(500)]public string Reason { get; set; }="";
}
public sealed class InquiryCalendarForm
{
    [Required,StringLength(200)]public string Label { get; set; }="";
    [Required,StringLength(80)]public string PolicyVersion { get; set; }="";
    public DateOnly EffectiveFrom { get; set; }
    [Range(1,60)]public int WorkingDays { get; set; }=10;
    public TimeOnly Cutoff { get; set; }=new(17,0);
    public bool CountReceiptDay { get; set; }
    [Required,StringLength(20)]public string WeekendDays { get; set; }="0,6";
    [StringLength(16000),DisplayFormat(ConvertEmptyStringToNull=false)]public string Exceptions { get; set; }="";
}
public sealed class OutboxReconcileForm
{
    public Guid CommandId { get; set; }
    [Required,StringLength(128)]public string RowVersion { get; set; }="";
    [Required,StringLength(40)]public string Decision { get; set; }="";
    [Required,StringLength(1000)]public string Evidence { get; set; }="";
    public bool AcknowledgeDuplicateRisk { get; set; }
}
public sealed record OutboxQueue(IReadOnlyList<InteractionOutbox> Items,int Total,int Page);
