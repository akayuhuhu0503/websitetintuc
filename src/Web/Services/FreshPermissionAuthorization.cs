using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Authorization;
namespace LandPortal.Web.Services;
public sealed record FreshPermissionRequirement(string? Permission) : IAuthorizationRequirement;
public sealed class FreshPermissionHandler(CurrentPermissionService permissions) : AuthorizationHandler<FreshPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, FreshPermissionRequirement requirement)
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (context.User.Identity?.IsAuthenticated != true || id is null) return;
        var actor = await permissions.GetAsync(id);
        var allowed = requirement.Permission is null
            ? actor.Has(Permissions.Create) || actor.Has(Permissions.ViewOwn) || actor.Has(Permissions.ViewAll) || actor.Has(Permissions.EditOwn) || actor.Has(Permissions.EditAll) || actor.Has(Permissions.Review) || actor.Has(Permissions.PublishApproved) || actor.Has(Permissions.ManageMedia)
            : actor.Has(requirement.Permission);
        if (allowed) context.Succeed(requirement);
    }
}
