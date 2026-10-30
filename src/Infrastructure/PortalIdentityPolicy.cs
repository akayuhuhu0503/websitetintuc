using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;

namespace LandPortal.Infrastructure;

// Web changes, first-time operator bootstrap and future account creation must
// use the same password policy. This never changes existing hashes or grants.
public static class PortalIdentityPolicy
{
    public static void Configure(IdentityOptions options)
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredUniqueChars = 0;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    }

    public const string Guidance = "Mật khẩu phải có ít nhất 8 ký tự, gồm chữ hoa, chữ thường, chữ số và ký tự đặc biệt.";
    public static string PasswordError(string code) => code switch
    {
        "PasswordTooShort" => "Mật khẩu phải có tối thiểu 8 ký tự.",
        "PasswordRequiresUpper" => "Mật khẩu phải có ít nhất 1 chữ viết hoa (A–Z).",
        "PasswordRequiresLower" => "Mật khẩu phải có ít nhất 1 chữ viết thường (a–z).",
        "PasswordRequiresDigit" => "Mật khẩu phải có ít nhất 1 chữ số (0–9).",
        "PasswordRequiresNonAlphanumeric" => "Mật khẩu phải có ít nhất 1 ký tự đặc biệt.",
        _ => "Mật khẩu chưa đáp ứng chính sách bảo mật."
    };

    public static string NewTemporaryPassword()
    {
        const string upper="ABCDEFGHJKLMNPQRSTUVWXYZ", lower="abcdefghijkmnopqrstuvwxyz", digits="23456789", symbols="!@#$%&*+-=?";
        var all=upper+lower+digits+symbols;
        var result=new char[24];var groups=new[]{upper,lower,digits,symbols};
        for(var i=0;i<groups.Length;i++)result[i]=groups[i][RandomNumberGenerator.GetInt32(groups[i].Length)];
        for(var i=groups.Length;i<result.Length;i++)result[i]=all[RandomNumberGenerator.GetInt32(all.Length)];
        RandomNumberGenerator.Shuffle(result.AsSpan());
        return new string(result);
    }
}
