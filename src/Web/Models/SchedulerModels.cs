using System.ComponentModel.DataAnnotations;
using LandPortal.Domain;
namespace LandPortal.Web.Models;
public sealed class ScheduleForm
{
    public Guid ContentId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid? ReplaceJobId { get; set; }
    [Required, StringLength(100)] public string ContentVersion { get; set; } = "";
    [StringLength(100)] public string? JobVersion { get; set; }
    [Required] public DateTime? DueVietnam { get; set; }
    public bool Direct { get; set; }
    public string Title { get; set; } = "";
    public bool CanDirect { get; set; }
    public bool CanApproved { get; set; }
    public int CatchUpMinutes { get; set; } = 15;
}
public sealed class ScheduleRow
{
    public Guid Id { get; set; }
    public Guid ContentId { get; set; }
    public Guid RevisionId { get; set; }
    public string Title { get; set; } = "";
    public ContentKind Kind { get; set; }
    public DateTimeOffset DueUtc { get; set; }
    public PublicationJobState State { get; set; }
    public string RequestedByUserId { get; set; } = "";
    public bool Direct { get; set; }
    public int Attempts { get; set; }
    public string FailureReason { get; set; } = "";
    public DateTimeOffset? CompletedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public sealed record ScheduleList(IReadOnlyList<ScheduleRow> Rows, int Page, int Total, PublicationJobState? State,
    ContentKind? Kind, DateTimeOffset? WorkerLastSeen, DateTimeOffset Now);
