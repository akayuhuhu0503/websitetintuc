using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LandPortal.Web.Models;

// Read the same validated deployment policy as UserManager, never a posted flag.
public sealed class IdentityPasswordLengthAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not string password || password.Length == 0) return ValidationResult.Success; // Required handles missing values.
        var options = (IOptions<IdentityOptions>?)context.GetService(typeof(IOptions<IdentityOptions>));
        var minimum = options?.Value.Password.RequiredLength ?? 7;
        return password.Length >= minimum ? ValidationResult.Success : new ValidationResult($"Mật khẩu phải có tối thiểu {minimum} ký tự.");
    }
}
