using System.Security.Claims;
using LandPortal.Infrastructure;
using LandPortal.Domain;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LandPortal.Web.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OwnAccountController(UserManager<PortalUser> users, SignInManager<PortalUser> signIn, PortalDbContext db, ILogger<OwnAccountController> logger) : Controller
{
    [HttpGet("admin/account/change-password")]
    public async Task<IActionResult> ChangePassword()
    {
        var user = await Current(); if (user is null || user.IsDisabled) return Forbid();
        return View("~/Views/Accounts/ChangePassword.cshtml", new ChangeOwnPasswordModel { RequiredChange = user.MustChangePassword });
    }
    [HttpPost("admin/account/change-password")]
    public async Task<IActionResult> ChangePassword(ChangeOwnPasswordModel model)
    {
        var user = await Current(); if (user is null || user.IsDisabled) return Forbid(); model.RequiredChange = user.MustChangePassword;
        if(!string.IsNullOrEmpty(model.NewPassword)&&model.NewPassword==model.CurrentPassword)ModelState.AddModelError(nameof(model.NewPassword),"Mật khẩu mới phải khác mật khẩu hiện tại.");
        if (ModelState.IsValid)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var result = await users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (result.Succeeded)
            {
                user.MustChangePassword = false; var updated = await users.UpdateAsync(user);
                if (updated.Succeeded)
                {
                    db.AuditLogs.Add(new AuditLog { Actor = user.Id, Action = "AccountPasswordChanged", Detail = System.Text.Json.JsonSerializer.Serialize(new { user.Id, requiredChangeBefore = model.RequiredChange, requiredChangeAfter = false }) });
                    await db.SaveChangesAsync(); await transaction.CommitAsync();
                    await signIn.RefreshSignInAsync(user);
                    logger.LogInformation("Own password change committed; initial-change requirement cleared.");
                    TempData["Success"] = "Đã đổi mật khẩu. Các phiên cũ bị thu hồi.";
                    return Redirect("/admin");
                }
                ModelState.AddModelError("", "Không lưu được trạng thái tài khoản; đăng nhập lại và đổi mật khẩu.");
            }
            else
            {
                foreach (var error in result.Errors) ModelState.AddModelError("", PasswordError(error.Code));
                logger.LogInformation("Own password change rejected by Identity: {ErrorCodes}", string.Join(",", result.Errors.Select(x => x.Code)));
            }
        }
        else logger.LogInformation("Own password form rejected by validation: {ErrorCount} errors", ModelState.ErrorCount);
        model.CurrentPassword = ""; model.NewPassword = ""; model.ConfirmPassword = "";
        // Clear submitted secrets without deleting field validation errors.
        foreach (var key in new[] { nameof(model.CurrentPassword), nameof(model.NewPassword), nameof(model.ConfirmPassword) })
            ModelState.SetModelValue(key, "", "");
        return View("~/Views/Accounts/ChangePassword.cshtml", model);
    }
    private string PasswordError(string code) => code switch
    {
        "PasswordMismatch" => "Mật khẩu hiện tại không đúng. Vui lòng nhập lại.",
        "PasswordTooShort" or "PasswordRequiresUpper" or "PasswordRequiresLower" or "PasswordRequiresDigit" or "PasswordRequiresNonAlphanumeric" => PortalIdentityPolicy.PasswordError(code),
        "ConcurrencyFailure" => "Tài khoản vừa được cập nhật. Vui lòng tải lại trang và thử lại.",
        _ => "Chưa đổi được mật khẩu. Vui lòng kiểm tra thông tin và thử lại."
    };
    [HttpPost("admin/account/logout")]
    public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return Redirect("/admin/login"); }
    private Task<PortalUser?> Current() => users.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
}
