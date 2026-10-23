using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public sealed class CurrentPermissionService(PortalDbContext db)
{
    public async Task<ContentActor> GetAsync(string userId)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId);
        if (user == null || user.IsDisabled || user.MustChangePassword || user.LockoutEnd > DateTimeOffset.UtcNow) return new(userId, new HashSet<string>(StringComparer.Ordinal));
        var demo=await db.UserClaims.AsNoTracking().Where(x=>x.UserId==userId&&x.ClaimType!=null&&x.ClaimType.StartsWith("dev.demo")).ToListAsync();
        if(!DevDemoPolicy.Allowed(demo.Select(x=>new System.Security.Claims.Claim(x.ClaimType!,x.ClaimValue??"")),Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),db.Database.GetDbConnection().Database))return new(userId,new HashSet<string>(StringComparer.Ordinal));
        var own = await db.UserClaims.AsNoTracking().Where(x => x.UserId == userId && x.ClaimType == "permission").Select(x => x.ClaimValue!).ToListAsync();
        var roles = from ur in db.UserRoles.AsNoTracking() join rc in db.RoleClaims.AsNoTracking() on ur.RoleId equals rc.RoleId where ur.UserId == userId && rc.ClaimType == "permission" select rc.ClaimValue!;
        var permissions = own.Concat(await roles.ToListAsync()).Where(x => Permissions.All.Contains(x, StringComparer.Ordinal)).ToHashSet(StringComparer.Ordinal);
        // Preserve the original edit capability's create/read meaning; no role-name checks
        // or changes to users, role assignments, passwords or existing stored claims.
        if (permissions.Contains(Permissions.EditOwn) || permissions.Contains(Permissions.EditAll)) { permissions.Add(Permissions.Create); permissions.Add(Permissions.ViewOwn); }
        if (permissions.Contains(Permissions.EditAll)) permissions.Add(Permissions.ViewAll);
        if (permissions.Contains(Permissions.PublishDirect)) permissions.Add(Permissions.PublishApproved);
        var scoped=await db.UserClaims.AsNoTracking().Where(x=>x.UserId==userId&&(x.ClaimType=="content.category.scope"||x.ClaimType=="content.category")).Select(x=>new{x.ClaimType,x.ClaimValue}).ToListAsync();
        IReadOnlySet<int>? categoryIds=null;
        if(scoped.Count!=0)categoryIds=scoped.Where(x=>x.ClaimType=="content.category"&&int.TryParse(x.ClaimValue,out _)).Select(x=>int.Parse(x.ClaimValue!)).ToHashSet();
        return new(userId, permissions){CategoryIds=categoryIds};
    }
    public IQueryable<Content> Scope(IQueryable<Content> rows,ContentActor actor)
    {
        if(actor.CategoryIds is null)return rows;
        var ids=actor.CategoryIds.ToArray();
        return rows.Where(c=>
            !db.ContentDrafts.Any(d=>d.ContentId==c.Id&&(d.PrimaryCategoryId==null||!ids.Contains(d.PrimaryCategoryId.Value)))&&
            !db.DraftCategories.Any(d=>d.ContentId==c.Id&&!ids.Contains(d.CategoryId))&&
            !db.Revisions.Any(r=>r.ContentId==c.Id&&(r.Id==c.CurrentRevisionId||r.Id==c.PublishedRevisionId)&&
                (r.PrimaryCategoryId==null||!ids.Contains(r.PrimaryCategoryId.Value)||db.RevisionCategories.Any(rc=>rc.RevisionId==r.Id&&!ids.Contains(rc.CategoryId)))));
    }
    public Task<bool> CanAccessContentAsync(ContentActor actor,Guid id)=>Scope(db.Contents.AsNoTracking(),actor).AnyAsync(x=>x.Id==id);
    public async Task<bool> CanReadAsync(ContentActor actor,Content content)=>CanRead(actor,content)&&await CanAccessContentAsync(actor,content.Id);
    public async Task<bool> CanEditAsync(ContentActor actor,Content content)=>CanEdit(actor,content)&&await CanAccessContentAsync(actor,content.Id);
    public IQueryable<ContentRevision> ScopeRevisions(IQueryable<ContentRevision> rows,ContentActor actor)
    {
        if(actor.CategoryIds is null)return rows;var ids=actor.CategoryIds.ToArray();
        return rows.Where(r=>r.PrimaryCategoryId!=null&&ids.Contains(r.PrimaryCategoryId.Value)&&!db.RevisionCategories.Any(c=>c.RevisionId==r.Id&&!ids.Contains(c.CategoryId)));
    }
    public static bool CanEdit(ContentActor actor, Content content) => actor.Has(Permissions.EditAll) || (content.OwnerUserId == actor.UserId && actor.Has(Permissions.EditOwn));
    public static bool CanRead(ContentActor actor, Content content) => actor.Has(Permissions.ViewAll) || actor.Has(Permissions.EditAll) ||
        (content.OwnerUserId == actor.UserId && (actor.Has(Permissions.ViewOwn) || actor.Has(Permissions.EditOwn) || actor.Has(Permissions.Create))) ||
        (actor.Has(Permissions.Review) && content.SubmittedRevisionId != null) ||
        (actor.Has(Permissions.PublishApproved) && content.ApprovedRevisionId != null);
}
