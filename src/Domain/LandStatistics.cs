namespace LandPortal.Domain;

// Immutable import versions; publishing switches a year's current version under a SQL transaction.
public sealed class LandStatisticsVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Year { get; set; }
    public string Unit { get; set; } = "";
    public string Source { get; set; } = "";
    public string RowsJson { get; set; } = "[]";
    public string StorageKey { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string ScanProofJson { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string State { get; set; } = "Preview";
    public bool IsCurrent { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedUtc { get; set; }
}
public sealed record LandAreaRow(string Code,string Province,decimal Total,decimal Agricultural,decimal NonAgricultural,decimal Unused);
public sealed record LandAreaIssue(int Row,string Column,string Message,bool Warning=false);
public sealed record LandAreaPreview(IReadOnlyList<LandAreaRow> Rows,IReadOnlyList<LandAreaIssue> Issues)
{
    public bool Valid => Rows.Count==34 && !Issues.Any(x=>!x.Warning);
}
