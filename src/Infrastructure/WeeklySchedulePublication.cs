using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

// A release is immutable publication evidence, not a draft. History is public
// only while this same weekly dossier still has a live published release.
public static class WeeklySchedulePublication
{
    public static IQueryable<WeeklyScheduleRelease> Releases(PortalDbContext db) =>
        db.WeeklyScheduleReleases.AsNoTracking().Where(r =>
            db.Contents.Any(c => c.Id == r.ContentId && c.IsPublic && !c.IsTrashed &&
                db.WeeklyScheduleReleases.Any(live => live.ContentId == c.Id && live.RevisionId == c.PublishedRevisionId && live.IsoYear == r.IsoYear && live.IsoWeek == r.IsoWeek) &&
                db.Revisions.Any(current => current.Id == c.PublishedRevisionId && (current.Metadata.DisclosureGroupId == null || db.DisclosureGroups.Any(g => g.Id == current.Metadata.DisclosureGroupId && g.Status == "NameApproved")))) &&
            db.Revisions.Any(v => v.Id == r.RevisionId && v.ContentId == r.ContentId &&
                v.Metadata.WeeklyYear == r.IsoYear && v.Metadata.WeeklyWeek == r.IsoWeek &&
                (v.Metadata.DisclosureGroupId == null || db.DisclosureGroups.Any(g => g.Id == v.Metadata.DisclosureGroupId && g.Status == "NameApproved"))));

    public static Task<bool> HasOriginalGrantAsync(PortalDbContext db, Guid assetId) =>
        Releases(db).AnyAsync(release => db.Revisions.Any(r => r.Id == release.RevisionId && r.Metadata.WeeklyOriginalAssetId == assetId &&
            r.Metadata.WeeklyOriginalReviewEvidence != null && r.Metadata.WeeklyOriginalReviewEvidence != "") &&
            db.ContentAssets.Any(a => a.RevisionId == release.RevisionId && a.MediaAssetId == assetId && a.Usage == "Attachment"));
}
