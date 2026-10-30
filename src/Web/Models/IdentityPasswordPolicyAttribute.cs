using System.ComponentModel.DataAnnotations;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LandPortal.Web.Models;

public sealed class IdentityPasswordPolicyAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value,ValidationContext context)
    {
        if(value is not string password||password.Length==0)return ValidationResult.Success;
        var options=((IOptions<IdentityOptions>?)context.GetService(typeof(IOptions<IdentityOptions>)))?.Value??new IdentityOptions();
        if(context.GetService(typeof(IOptions<IdentityOptions>))==null)PortalIdentityPolicy.Configure(options);
        var policy=options.Password;var errors=new List<string>();
        if(password.Length<policy.RequiredLength)errors.Add(PortalIdentityPolicy.PasswordError("PasswordTooShort"));
        if(policy.RequireUppercase&&!password.Any(c=>c is >= 'A' and <= 'Z'))errors.Add(PortalIdentityPolicy.PasswordError("PasswordRequiresUpper"));
        if(policy.RequireLowercase&&!password.Any(c=>c is >= 'a' and <= 'z'))errors.Add(PortalIdentityPolicy.PasswordError("PasswordRequiresLower"));
        if(policy.RequireDigit&&!password.Any(c=>c is >= '0' and <= '9'))errors.Add(PortalIdentityPolicy.PasswordError("PasswordRequiresDigit"));
        if(policy.RequireNonAlphanumeric&&password.All(c=>c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'))errors.Add(PortalIdentityPolicy.PasswordError("PasswordRequiresNonAlphanumeric"));
        return errors.Count==0?ValidationResult.Success:new ValidationResult(string.Join(" ",errors));
    }
}
