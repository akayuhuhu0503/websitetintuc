using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;

public static class SearchProjectionService
{
    public static RevisionSearchProjection Create(ContentRevision revision) => new()
    {
        RevisionId = revision.Id,
        Text = TextNormalizer.Search(revision.Title + " " + revision.Summary + " " + new HtmlPolicy().PlainText(revision.BodyHtml) + " " + revision.Metadata.SearchFields()),
        Number = TextNormalizer.Search(revision.Metadata.Number), Authority = TextNormalizer.Search(revision.Metadata.IssuingBody),
        DocumentType = TextNormalizer.Search(revision.Metadata.DocumentType), Field = TextNormalizer.Search(revision.Metadata.Field)
    };
    // Explicit deployment command, not a web startup seed. Bounded batches, no network.
    public static async Task<int> BackfillAsync(PortalDbContext db)
    {
        var count = 0;
        while (true)
        {
            var revisions = await db.Revisions.AsNoTracking().Where(x => !db.Set<RevisionSearchProjection>().Any(p => p.RevisionId == x.Id)).OrderBy(x => x.Id).Take(50).ToListAsync();
            if (revisions.Count == 0) break;
            db.AddRange(revisions.Select(Create)); await db.SaveChangesAsync(); count += revisions.Count; db.ChangeTracker.Clear();
        }
        return count;
    }
}
