using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

[Authorize(Policy = Permissions.ManageRoles)]
public sealed class RoleAdministrationController(PortalDbContext db, AccountAdministrationService administration, CurrentPermissionService permissions) : Controller
{
    private string ActorId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    [HttpGet("admin/roles")]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        if (q?.Length > 200) return BadRequest();
        var roles = db.Roles.AsNoTracking(); if (!string.IsNullOrWhiteSpace(q)) { var query = q.Trim(); roles = roles.Where(x => x.Name!.Contains(query)); }
        page = Math.Clamp(page, 1, 10000); var total = await roles.CountAsync();
        var rows = await roles.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20)
            .Select(x => new { x.Id, x.Name, Members = db.UserRoles.Count(r => r.RoleId == x.Id) }).ToListAsync();
        var ids = rows.Select(x => x.Id).ToArray();
        var claims = await db.RoleClaims.AsNoTracking().Where(x => ids.Contains(x.RoleId) && x.ClaimType == "permission").Select(x => new { x.RoleId, x.ClaimValue }).ToListAsync();
        return View("~/Views/Roles/Index.cshtml", new RoleListModel { Query = q, Page = page, Total = total,
            Items = rows.Select(x => new RoleListRow(x.Id, x.Name ?? "", x.Members, claims.Where(c => c.RoleId == x.Id).Select(c => c.ClaimValue ?? "").Distinct().Order().ToArray())).ToArray() });
    }
    [HttpGet("admin/roles/create")]
    public async Task<IActionResult> Create() => View("~/Views/Roles/Edit.cshtml", await Populate(new RoleEditModel()));
    [HttpGet("admin/roles/{id}")]
    public async Task<IActionResult> Edit(string id)
    {
        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id); if (role is null) return NotFound();
        var model = new RoleEditModel { Id = role.Id, Name = role.Name ?? "", ConcurrencyStamp = role.ConcurrencyStamp,
            Permissions = await db.RoleClaims.AsNoTracking().Where(x => x.RoleId == id && x.ClaimType == "permission").Select(x => x.ClaimValue!).ToListAsync() };
        return View("~/Views/Roles/Edit.cshtml", await Populate(model));
    }
    [HttpPost("admin/roles/save")]
    public async Task<IActionResult> Save(RoleEditModel model)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await administration.SaveRoleAsync(ActorId, model.Id, model.ConcurrencyStamp, model.Name, model.Permissions);
                TempData["Success"] = "Đã lưu nhóm quyền. Phiên cũ của các thành viên bị thu hồi khi tập quyền thay đổi.";
                return Redirect("/admin/roles/" + Uri.EscapeDataString(id));
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (DbUpdateConcurrencyException) { Response.StatusCode = 409; ModelState.AddModelError("", "Nhóm quyền đã thay đổi. Tải lại trước khi lưu; không tự ghi đè."); }
            catch (InvalidOperationException error) { Response.StatusCode = 400; ModelState.AddModelError("", error.Message); }
        }
        return View("~/Views/Roles/Edit.cshtml", await Populate(model));
    }
    private async Task<RoleEditModel> Populate(RoleEditModel model)
    {
        var actor = await permissions.GetAsync(ActorId); model.CanGrant = actor.Has(Permissions.GrantPermissions);
        model.IsOwnRole = model.Id is not null && await db.UserRoles.AsNoTracking().AnyAsync(x => x.UserId == ActorId && x.RoleId == model.Id);
        model.Catalog = Permissions.All.Distinct().Order().Select(x => new PermissionChoice(x, AccountAdministrationService.IsSensitive(x),
            !model.IsOwnRole && model.CanGrant && actor.Has(x) && (!AccountAdministrationService.IsSensitive(x) || actor.Has(Permissions.GrantPrivileged)))).ToArray();
        return model;
    }
}
