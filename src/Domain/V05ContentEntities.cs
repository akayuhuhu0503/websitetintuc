namespace LandPortal.Domain;
public sealed class DisclosureGroup
{
    public string Id { get; set; }="";
    public string Name { get; set; }="";
    public string Status { get; set; }="NeedsNameApproval";
}
public sealed class WeeklyScheduleRelease
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public Guid ContentId { get; set; }
    public Guid RevisionId { get; set; }
    public int IsoYear { get; set; }
    public int IsoWeek { get; set; }
    public int Version { get; set; }
    public DateTimeOffset PublishedUtc { get; set; }
    public string PublisherId { get; set; }="";
    public string ChangeNote { get; set; }="";
}
