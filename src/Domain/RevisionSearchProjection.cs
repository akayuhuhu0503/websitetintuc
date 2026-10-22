namespace LandPortal.Domain;
public sealed class RevisionSearchProjection
{
    public Guid RevisionId { get; set; }
    public string Text { get; set; } = "";
    public string Number { get; set; } = "";
    public string Authority { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string Field { get; set; } = "";
}
