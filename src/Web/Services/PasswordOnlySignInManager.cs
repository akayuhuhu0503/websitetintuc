using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LandPortal.Web.Services;

// Keep Identity password/lockout/cookie checks even for legacy rows whose
// unused TwoFactorEnabled flag is still set. No data migration or token creation.
public sealed class PasswordOnlySignInManager(
    UserManager<PortalUser> users, IHttpContextAccessor context,
    IUserClaimsPrincipalFactory<PortalUser> claims, IOptions<IdentityOptions> options,
    ILogger<SignInManager<PortalUser>> logger, IAuthenticationSchemeProvider schemes,
    IUserConfirmation<PortalUser> confirmation)
    : SignInManager<PortalUser>(users, context, claims, options, logger, schemes, confirmation)
{
    public override Task<bool> IsTwoFactorEnabledAsync(PortalUser user) => Task.FromResult(false);
}
