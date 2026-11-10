using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace LandPortal.Infrastructure;

public sealed class OptionalEmailUserValidator : IUserValidator<PortalUser>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<PortalUser> manager, PortalUser user)
    {
        var email = await manager.GetEmailAsync(user);
        if (string.IsNullOrEmpty(email)) return IdentityResult.Success;
        if (email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
            return IdentityResult.Failed(manager.ErrorDescriber.InvalidEmail(email));
        var owner = await manager.FindByEmailAsync(email);
        if (owner is not null && await manager.GetUserIdAsync(owner) != await manager.GetUserIdAsync(user))
            return IdentityResult.Failed(manager.ErrorDescriber.DuplicateEmail(email));
        return IdentityResult.Success;
    }
}
