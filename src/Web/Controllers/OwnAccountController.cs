using System.Security.Claims;
using LandPortal.Infrastructure;
using LandPortal.Domain;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LandPortal.Web.Controllers;

[Authorize]
public sealed class OwnAccountController(UserManager<PortalUser> users, SignInManager<PortalUser> signIn, PortalDbContext db) : Controller
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
                    await signIn.RefreshSignInAsync(user); TempData["Success"] = "Đã đổi mật khẩu. Các phiên cũ bị thu hồi."; return Redirect("/admin");
                }
                ModelState.AddModelError("", "Không lưu được trạng thái tài khoản; đăng nhập lại và đổi mật khẩu.");
            }
            else ModelState.AddModelError("", "Mật khẩu hiện tại không đúng hoặc mật khẩu mới không đáp ứng chính sách Identity.");
        }
        model.CurrentPassword = ""; model.NewPassword = ""; model.ConfirmPassword = "";
        ModelState.Remove(nameof(model.CurrentPassword)); ModelState.Remove(nameof(model.NewPassword)); ModelState.Remove(nameof(model.ConfirmPassword));
        return View("~/Views/Accounts/ChangePassword.cshtml", model);
    }
    [HttpPost("admin/account/logout")]
    public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return Redirect("/admin/login"); }
    private Task<PortalUser?> Current() => users.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
}
