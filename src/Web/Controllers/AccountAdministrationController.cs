using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

[Authorize(Policy = Permissions.ManageUsers)]
public sealed class AccountAdministrationController(PortalDbContext db, AccountAdministrationService administration, CurrentPermissionService permissions) : Controller
{
    private string ActorId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    [HttpGet("admin/users")]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        if (q?.Length > 200) return BadRequest();
        var rows = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q)) { var query = q.Trim(); rows = rows.Where(x => x.UserName!.Contains(query) || x.Email!.Contains(query) || x.DisplayName.Contains(query)); }
        page = Math.Clamp(page, 1, 10000); var total = await rows.CountAsync();
        var items = await rows.OrderBy(x => x.UserName).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20)
            .Select(x => new UserListRow(x.Id, x.UserName ?? "", x.Email ?? "", x.DisplayName, x.IsDisabled, x.MustChangePassword)).ToListAsync();
        return View("~/Views/Accounts/Index.cshtml", new UserListModel { Query = q, Page = page, Total = total, Items = items });
    }
    [HttpGet("admin/users/create")]
    public IActionResult Create() => View("~/Views/Accounts/Create.cshtml", new CreateAccountModel());
    [HttpPost("admin/users/create")]
    public async Task<IActionResult> Create(CreateAccountModel model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await administration.CreateUserAsync(ActorId, model.UserName, model.Email, model.DisplayName, model.TemporaryPassword);
                TempData["Success"] = "Đã tạo tài khoản nội bộ. Tài khoản phải đổi mật khẩu trước khi thực hiện thao tác quản trị; chưa được gán nhóm quyền.";
                return Redirect("/admin/users/" + Uri.EscapeDataString(id));
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException error) { ModelState.AddModelError("", error.Message); }
        }
        model.TemporaryPassword = ""; ModelState.Remove(nameof(model.TemporaryPassword));
        return View("~/Views/Accounts/Create.cshtml", model);
    }
    [HttpGet("admin/users/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        var model = await DetailModel(id); return model is null ? NotFound() : View("~/Views/Accounts/Detail.cshtml", model);
    }
    [HttpPost("admin/users/{id}/update")]
    public async Task<IActionResult> Update(string id, AccountDetailModel model)
    {
        if (!ModelState.IsValid) return await Failure(id, 400, "Thông tin tài khoản không hợp lệ.");
        return await Operation(id, () => administration.UpdateProfileAsync(ActorId, id, model.ConcurrencyStamp, model.Email, model.DisplayName), "Đã cập nhật thông tin tài khoản.");
    }
    [HttpPost("admin/users/{id}/status")]
    public Task<IActionResult> Status(string id, string concurrencyStamp, bool disabled) => Operation(id,
        () => administration.SetDisabledAsync(ActorId, id, concurrencyStamp, disabled), disabled ? "Đã vô hiệu hóa tài khoản và thu hồi phiên." : "Đã mở lại tài khoản.");
    [HttpPost("admin/users/{id}/revoke-sessions")]
    public Task<IActionResult> Revoke(string id, string concurrencyStamp) => Operation(id,
        () => administration.RevokeSessionsAsync(ActorId, id, concurrencyStamp), "Đã thu hồi các phiên đăng nhập của tài khoản.");
    [Authorize(Policy = Permissions.GrantPermissions), HttpPost("admin/users/{id}/roles")]
    public Task<IActionResult> Roles(string id, string concurrencyStamp, List<string> roleIds) => Operation(id,
        () => administration.AssignRolesAsync(ActorId, id, concurrencyStamp, roleIds), "Đã cập nhật nhóm quyền và thu hồi các phiên đăng nhập cũ.");
    [Authorize(Policy=Permissions.GrantPermissions),HttpPost("admin/users/{id}/categories")]
    public Task<IActionResult> Categories(string id,string concurrencyStamp,bool unrestrictedCategories,List<int> categoryIds)=>Operation(id,
        ()=>administration.SetCategoryScopeAsync(ActorId,id,concurrencyStamp,unrestrictedCategories,categoryIds),"Đã cập nhật phạm vi chuyên mục và thu hồi phiên cũ. Lịch đăng sẽ kiểm tra lại phạm vi khi chạy.");

    private async Task<IActionResult> Operation(string id, Func<Task> operation, string success)
    {
        try { await operation(); TempData["Success"] = success; return Redirect("/admin/users/" + Uri.EscapeDataString(id)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (DbUpdateConcurrencyException) { return await Failure(id, 409, "Tài khoản đã thay đổi ở phiên khác. Tải lại và đối chiếu trước khi thao tác."); }
        catch (InvalidOperationException error) { return await Failure(id, 400, error.Message); }
    }
    private async Task<IActionResult> Failure(string id, int status, string message)
    {
        Response.StatusCode = status; ModelState.AddModelError("", message);
        var model = await DetailModel(id); return model is null ? NotFound() : View("~/Views/Accounts/Detail.cshtml", model);
    }
    private async Task<AccountDetailModel?> DetailModel(string id)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id); if (user is null) return null;
        var actor = await permissions.GetAsync(ActorId);
        var own = await db.UserClaims.AsNoTracking().Where(x => x.UserId == id && x.ClaimType == "permission").Select(x => x.ClaimValue!).ToListAsync();
        var inherited = await (from ur in db.UserRoles.AsNoTracking() join rc in db.RoleClaims.AsNoTracking() on ur.RoleId equals rc.RoleId where ur.UserId == id && rc.ClaimType == "permission" select rc.ClaimValue!).ToListAsync();
        var roleIds = await db.UserRoles.AsNoTracking().Where(x => x.UserId == id).Select(x => x.RoleId).ToListAsync();
        var roles = await db.Roles.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync();
        var roleClaims = await db.RoleClaims.AsNoTracking().Where(x => x.ClaimType == "permission").Select(x => new { x.RoleId, x.ClaimValue }).ToListAsync();
        var effective = own.Concat(inherited).Where(x => Permissions.All.Contains(x)).Distinct().Order().ToArray();
        var targetActor=await permissions.GetAsync(id);
        var scopeClaims=await db.UserClaims.AsNoTracking().Where(x=>x.UserId==id&&(x.ClaimType=="content.category.scope"||x.ClaimType=="content.category")).ToListAsync();
        var assignedCategories=scopeClaims.Where(x=>x.ClaimType=="content.category"&&int.TryParse(x.ClaimValue,out _)).Select(x=>int.Parse(x.ClaimValue!)).ToHashSet();
        var scopeChoices=await db.Categories.AsNoTracking().OrderBy(x=>x.SortOrder).Select(x=>new EditorCategory(x.Id,x.Name)).ToListAsync();
        return new AccountDetailModel { Id = user.Id, UserName = user.UserName ?? "", Email = user.Email ?? "", DisplayName = user.DisplayName,
            ConcurrencyStamp = user.ConcurrencyStamp ?? "", Disabled = user.IsDisabled, MustChangePassword = user.MustChangePassword,
            UnrestrictedCategories=scopeClaims.Count==0,CategoryIds=assignedCategories,Categories=scopeChoices.Where(x=>actor.CategoryIds==null||actor.CategoryIds.Contains(x.Id)).ToArray(),RoleIds = roleIds, EffectivePermissions = effective, CanGrant = actor.Has(Permissions.GrantPermissions) && actor.UserId != id,
            CanChangeSensitive = !effective.Any(AccountAdministrationService.IsSensitive) || actor.Has(Permissions.GrantPrivileged),
            Roles = roles.Select(r => new RoleChoice(r.Id, r.Name ?? "", roleClaims.Any(c => c.RoleId == r.Id && c.ClaimValue is not null && AccountAdministrationService.IsSensitive(c.ClaimValue)))).ToArray() };
    }
}
