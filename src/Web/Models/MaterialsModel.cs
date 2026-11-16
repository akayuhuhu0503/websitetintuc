namespace LandPortal.Web.Models;

public sealed record MaterialItem(ContentCard Content, IReadOnlyList<AttachmentModel> Files);
public sealed record MaterialsModel(IReadOnlyList<MaterialItem> Items, int Total, int Page, string? Query)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / 10d));
}
