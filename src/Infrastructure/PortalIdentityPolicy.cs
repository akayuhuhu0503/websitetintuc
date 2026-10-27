using Microsoft.AspNetCore.Identity;

namespace LandPortal.Infrastructure;

// Web changes, first-time operator bootstrap and future account creation must
// use the same password policy. This never changes existing hashes or grants.
public static class PortalIdentityPolicy
{
    public static void Configure(IdentityOptions options)
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 7;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireDigit = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 0;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    }
}
