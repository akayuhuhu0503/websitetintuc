using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

public static class PendingReviewQuery
{
    public static IQueryable<Content> Filter(IQueryable<Content> rows, PortalDbContext db) => rows.Where(c =>
        !c.IsTrashed && c.State == WorkflowState.PendingReview && c.SubmittedRevisionId != null &&
        c.SubmittedRevisionId == c.CurrentRevisionId &&
        db.Revisions.Any(r => r.ContentId == c.Id && r.Id == c.SubmittedRevisionId));

    public static Task<int> CountAsync(PortalDbContext db, CurrentPermissionService permissions, ContentActor actor) =>
        actor.Has(Permissions.Review)
            ? Filter(permissions.Scope(db.Contents.AsNoTracking(), actor), db).CountAsync()
            : Task.FromResult(0);
}
