using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public static class PublicCaptchaModel
{
    public static void Configure(ModelBuilder b)
    {
        var c=b.Entity<PublicCaptchaChallenge>();c.Property(x=>x.SessionHash).HasMaxLength(64).IsUnicode(false);c.Property(x=>x.AddressHash).HasMaxLength(64).IsUnicode(false);c.Property(x=>x.AnswerHash).HasMaxLength(64).IsUnicode(false);c.Property(x=>x.Purpose).HasMaxLength(40);c.HasIndex(x=>new{x.SessionHash,x.CreatedUtc});c.HasIndex(x=>new{x.AddressHash,x.CreatedUtc});c.HasIndex(x=>x.ExpiresUtc);
        var a=b.Entity<PublicCaptchaAttempt>();a.Property(x=>x.AddressHash).HasMaxLength(64).IsUnicode(false);a.HasIndex(x=>new{x.AddressHash,x.CreatedUtc});
    }
}
public partial class PortalDbContext
{
    public DbSet<PublicCaptchaChallenge> PublicCaptchaChallenges=>Set<PublicCaptchaChallenge>();
    public DbSet<PublicCaptchaAttempt> PublicCaptchaAttempts=>Set<PublicCaptchaAttempt>();
}
