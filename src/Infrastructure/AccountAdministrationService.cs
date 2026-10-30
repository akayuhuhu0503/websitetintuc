using System.Data;
using System.Security.Claims;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

// Every identity mutation uses the same database transaction/application lock.
// The final administrator check therefore also covers requests changing shared roles.
public sealed class AccountAdministrationService(PortalDbContext db, UserManager<PortalUser> users,
    RoleManager<IdentityRole> roles, CurrentPermissionService current, TemporaryPasswordDelivery? delivery = null)
{
    public static readonly IReadOnlySet<string> PrivilegedPermissions = new HashSet<string>(StringComparer.Ordinal)
    {
        Permissions.ManageUsers, Permissions.ManageRoles, Permissions.GrantPermissions,
        Permissions.GrantPrivileged, Permissions.ManageSettings, Permissions.Import
    };

    public static bool IsSensitive(string permission) => PrivilegedPermissions.Contains(permission) ||
        permission.StartsWith("outbox.",StringComparison.Ordinal) ||
        (permission!=Permissions.Moderate&&permission.StartsWith("interaction.",StringComparison.Ordinal)) ||
        permission.StartsWith("site.", StringComparison.Ordinal) || permission.StartsWith("schedule.", StringComparison.Ordinal) ||
        permission == Permissions.PublishDirect || permission == Permissions.PublishApproved || permission == Permissions.Schedule;

    public async Task<TemporaryPasswordReceipt> CreateUserAsync(string actorId, string userName, string email, string displayName)
    {
        return await Mutate(actorId, Permissions.ManageUsers, async actor =>
        {
            displayName??="";
            ValidateText(userName, 256, "Tên đăng nhập", true); ValidateText(email, 256, "Email", true);
            ValidateText(displayName, 200, "Tên hiển thị", false);
            var user = new PortalUser { UserName = userName.Trim(), Email = email.Trim(), DisplayName = displayName.Trim(),
                EmailConfirmed = true, LockoutEnabled = true, MustChangePassword = true };
            var temporaryPassword=PortalIdentityPolicy.NewTemporaryPassword();
            var file=(delivery??throw new InvalidOperationException("Chưa cấu hình kênh bàn giao mật khẩu private; chưa tạo tài khoản.")).Write(user.Id,user.UserName!,temporaryPassword);
            Check(await users.CreateAsync(user, temporaryPassword));
            Audit(actorId, "AccountCreated", new { user.Id, user.UserName, user.Email, user.DisplayName, user.MustChangePassword });
            return new TemporaryPasswordReceipt(user.Id,file);
        });
    }

    public Task<TemporaryPasswordReceipt> ResetPasswordAsync(string actorId,string targetId,string concurrencyStamp) =>
        Mutate(actorId,Permissions.ManageUsers,async actor =>
        {
            var user=await User(targetId,concurrencyStamp);await ProtectTarget(actor,user);
            if(user.Id==actorId)throw new UnauthorizedAccessException("Dùng chức năng đổi mật khẩu của bạn; không tự reset mật khẩu quản trị.");
            if(user.IsDisabled)throw new InvalidOperationException("Tài khoản đang vô hiệu hóa; chưa đặt lại mật khẩu.");
            var password=PortalIdentityPolicy.NewTemporaryPassword();
            var file=(delivery??throw new InvalidOperationException("Chưa cấu hình kênh bàn giao mật khẩu private; chưa reset tài khoản.")).Write(user.Id,user.UserName!,password);
            var token=await users.GeneratePasswordResetTokenAsync(user);
            Check(await users.ResetPasswordAsync(user,token,password));
            user.MustChangePassword=true;Check(await users.UpdateAsync(user));
            Audit(actorId,"AccountPasswordReset",new {user.Id,mustChangePassword=true,sessionsRevoked=true});
            return new TemporaryPasswordReceipt(user.Id,file);
        });

    public async Task UpdateProfileAsync(string actorId, string targetId, string concurrencyStamp, string email, string displayName)
    {
        await Mutate(actorId, Permissions.ManageUsers, async actor =>
        {
            var user = await User(targetId, concurrencyStamp); await ProtectTarget(actor, user);
            ValidateText(email, 256, "Email", true); ValidateText(displayName, 200, "Tên hiển thị", false);
            var before = new { user.Id, user.Email, user.DisplayName };
            user.Email = email.Trim(); user.DisplayName = displayName.Trim();
            Check(await users.UpdateAsync(user));
            Audit(actorId, "AccountProfileUpdated", new { before, after = new { user.Id, user.Email, user.DisplayName } });
            return true;
        });
    }

    public async Task SetDisabledAsync(string actorId, string targetId, string concurrencyStamp, bool disabled)
    {
        await Mutate(actorId, Permissions.ManageUsers, async actor =>
        {
            var user = await User(targetId, concurrencyStamp); await ProtectTarget(actor, user);
            if (user.Id == actorId) throw new InvalidOperationException("Không tự khóa/mở khóa tài khoản đang thực hiện thao tác.");
            var before = new { user.Id, user.IsDisabled, user.LockoutEnd };
            user.IsDisabled = disabled; user.LockoutEnd = disabled ? DateTimeOffset.MaxValue : null;
            Check(await users.UpdateSecurityStampAsync(user));
            Audit(actorId, disabled ? "AccountDisabled" : "AccountEnabled", new { before, after = new { user.Id, user.IsDisabled, user.LockoutEnd } });
            return true;
        });
    }

    public async Task RevokeSessionsAsync(string actorId, string targetId, string concurrencyStamp)
    {
        await Mutate(actorId, Permissions.ManageUsers, async actor =>
        {
            var user = await User(targetId, concurrencyStamp); await ProtectTarget(actor, user);
            Check(await users.UpdateSecurityStampAsync(user));
            Audit(actorId, "AccountSessionsRevoked", new { user.Id });
            return true;
        });
    }

    public async Task AssignRolesAsync(string actorId, string targetId, string concurrencyStamp, IReadOnlyList<string> roleIds)
    {
        await Mutate(actorId, Permissions.GrantPermissions, async actor =>
        {
            Require(actor, Permissions.ManageUsers);
            var user = await User(targetId, concurrencyStamp); await ProtectTarget(actor, user);
            if (user.Id == actorId) throw new UnauthorizedAccessException("Không tự thay đổi nhóm quyền của mình.");
            var requested = roleIds.Distinct(StringComparer.Ordinal).ToArray();
            if (requested.Length > 30) throw new InvalidOperationException("Tối đa 30 nhóm quyền cho mỗi tài khoản.");
            var targetRoles = await db.Roles.Where(x => requested.Contains(x.Id)).ToListAsync();
            if (targetRoles.Count != requested.Length) throw new InvalidOperationException("Nhóm quyền không tồn tại.");
            var oldIds = await db.UserRoles.Where(x => x.UserId == targetId).Select(x => x.RoleId).ToListAsync();
            var affected = oldIds.Union(requested).ToArray();
            var affectedPermissions = await db.RoleClaims.Where(x => affected.Contains(x.RoleId) && x.ClaimType == "permission").Select(x => x.ClaimValue!).ToListAsync();
            CheckDelegation(actor, affectedPermissions);
            var before = new { user.Id, roleIds = oldIds.Order().ToArray() };
            var oldNames = await users.GetRolesAsync(user);
            Check(await users.RemoveFromRolesAsync(user, oldNames));
            Check(await users.AddToRolesAsync(user, targetRoles.Select(x => x.Name!)));
            Check(await users.UpdateSecurityStampAsync(user));
            Audit(actorId, "AccountRolesAssigned", new { before, after = new { user.Id, roleIds = requested.Order().ToArray() } });
            return true;
        });
    }

    public async Task<string> SaveRoleAsync(string actorId, string? roleId, string? concurrencyStamp, string name, IReadOnlyList<string> permissions)
    {
        return await Mutate(actorId, Permissions.ManageRoles, async actor =>
        {
            ValidateText(name, 100, "Tên nhóm", true);
            var requested = permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            CheckCatalog(requested);
            var role = roleId is null ? new IdentityRole(name.Trim()) : await db.Roles.SingleOrDefaultAsync(x => x.Id == roleId)
                ?? throw new InvalidOperationException("Nhóm quyền không tồn tại.");
            if (roleId is not null && (string.IsNullOrWhiteSpace(concurrencyStamp) || role.ConcurrencyStamp != concurrencyStamp))
                throw new DbUpdateConcurrencyException("Nhóm đã thay đổi; tải lại trước khi lưu.");
            var old = roleId is null ? [] : await db.RoleClaims.Where(x => x.RoleId == role.Id && x.ClaimType == "permission").Select(x => x.ClaimValue!).ToArrayAsync();
            CheckCatalog(old);
            var changed = !old.Order(StringComparer.Ordinal).SequenceEqual(requested);
            if (changed)
            {
                Require(actor, Permissions.GrantPermissions);
                if (await db.UserRoles.AnyAsync(x => x.UserId == actorId && x.RoleId == role.Id))
                    throw new UnauthorizedAccessException("Không sửa tập quyền của nhóm mà mình đang thuộc.");
                CheckDelegation(actor, old.Union(requested));
            }
            else if (old.Any(IsSensitive)) CheckDelegation(actor, old);
            var before = roleId is null ? null : new { role.Id, role.Name, permissions = old.Order().ToArray() };
            role.Name = name.Trim();
            if (roleId is null) Check(await roles.CreateAsync(role)); else Check(await roles.UpdateAsync(role));
            if (changed)
            {
                var claims = await roles.GetClaimsAsync(role);
                foreach (var claim in claims.Where(x => x.Type == "permission")) Check(await roles.RemoveClaimAsync(role, claim));
                foreach (var permission in requested) Check(await roles.AddClaimAsync(role, new Claim("permission", permission)));
                var members = await (from ur in db.UserRoles join user in db.Users on ur.UserId equals user.Id where ur.RoleId == role.Id select user).ToListAsync();
                foreach (var member in members) Check(await users.UpdateSecurityStampAsync(member));
            }
            Audit(actorId, roleId is null ? "RoleCreated" : "RoleUpdated", new { before, after = new { role.Id, role.Name, permissions = requested } });
            return role.Id;
        });
    }

    public async Task SetCategoryScopeAsync(string actorId,string targetId,string stamp,bool unrestricted,IReadOnlyList<int> categoryIds)
    {
        await Mutate(actorId,Permissions.ManageUsers,async actor=>
        {
            Require(actor,Permissions.GrantPermissions);
            if(actorId==targetId)throw new UnauthorizedAccessException("Không tự thay đổi phạm vi chuyên mục của mình.");
            var target=await User(targetId,stamp);await ProtectTarget(actor,target);
            var selected=categoryIds.Distinct().ToArray();
            if(selected.Length>100||await db.Categories.CountAsync(x=>selected.Contains(x.Id))!=selected.Length)throw new InvalidOperationException("Danh sách chuyên mục không hợp lệ.");
            if(unrestricted&&(!actor.Has(Permissions.GrantPrivileged)||actor.CategoryIds is not null)||!unrestricted&&!actor.Covers(selected))throw new UnauthorizedAccessException("Không cấp phạm vi rộng hơn chính mình; phạm vi toàn bộ cần ủy quyền đặc quyền.");
            var before=await users.GetClaimsAsync(target);
            foreach(var claim in before.Where(x=>x.Type is "content.category.scope" or "content.category"))Check(await users.RemoveClaimAsync(target,claim));
            if(!unrestricted){Check(await users.AddClaimAsync(target,new Claim("content.category.scope","restricted")));foreach(var id in selected)Check(await users.AddClaimAsync(target,new Claim("content.category",id.ToString(System.Globalization.CultureInfo.InvariantCulture))));}
            Check(await users.UpdateSecurityStampAsync(target));
            Audit(actorId,"AccountCategoryScopeChanged",new{target.Id,before=before.Where(x=>x.Type is "content.category.scope" or "content.category").Select(x=>new{x.Type,x.Value}),after=new{unrestricted,categories=selected},sessionsRevoked=true});
            return true;
        });
    }
    private async Task<T> Mutate<T>(string actorId, string permission, Func<ContentActor, Task<T>> action)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'LandPortal.IdentityAdministration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000; IF @result<0 THROW 51001, 'Identity administration is busy. Retry after reloading.', 1;");
            var actor = await current.GetAsync(actorId); Require(actor, permission);
            var hadAdministration = await HasAdministrationPath();
            var result = await action(actor);
            await db.SaveChangesAsync();
            if (hadAdministration && !await HasAdministrationPath())
                throw new InvalidOperationException("Thao tác làm mất đường quản trị người dùng và phân quyền cuối cùng.");
            await transaction.CommitAsync();
            return result;
        }
        catch { await transaction.RollbackAsync(); db.ChangeTracker.Clear(); throw; }
    }

    private async Task<bool> HasAdministrationPath()
    {
        var candidates = await db.Users.AsNoTracking().Where(x => !x.IsDisabled && !x.MustChangePassword && (x.LockoutEnd == null || x.LockoutEnd <= DateTimeOffset.UtcNow)).Select(x => x.Id).ToListAsync();
        var needed = new[] { Permissions.ManageUsers, Permissions.ManageRoles, Permissions.GrantPermissions };
        foreach (var user in candidates)
        {
            var own = await db.UserClaims.AsNoTracking().Where(x => x.UserId == user && x.ClaimType == "permission").Select(x => x.ClaimValue!).ToListAsync();
            var inherited = await (from ur in db.UserRoles.AsNoTracking() join rc in db.RoleClaims.AsNoTracking() on ur.RoleId equals rc.RoleId where ur.UserId == user && rc.ClaimType == "permission" select rc.ClaimValue!).ToListAsync();
            if (needed.All(own.Concat(inherited).Contains)) return true;
        }
        return false;
    }

    private async Task ProtectTarget(ContentActor actor, PortalUser user)
    {
        var own = await db.UserClaims.Where(x => x.UserId == user.Id && x.ClaimType == "permission").Select(x => x.ClaimValue!).ToListAsync();
        var inherited = await (from ur in db.UserRoles join rc in db.RoleClaims on ur.RoleId equals rc.RoleId where ur.UserId == user.Id && rc.ClaimType == "permission" select rc.ClaimValue!).ToListAsync();
        if (own.Concat(inherited).Any(IsSensitive)) CheckDelegation(actor, own.Concat(inherited));
    }
    public static void CheckDelegation(ContentActor actor, IEnumerable<string> permissions)
    {
        Require(actor, Permissions.GrantPermissions);
        var allowed = permissions.Distinct(StringComparer.Ordinal).ToArray(); CheckCatalog(allowed);
        if (allowed.Any(IsSensitive) && !actor.Has(Permissions.GrantPrivileged)) throw new UnauthorizedAccessException("Thiếu quyền ủy quyền đặc quyền.");
        if (allowed.Any(p => !actor.Has(p))) throw new UnauthorizedAccessException("Chỉ được cấp/thu hồi quyền trong tập quyền chính mình đang có.");
    }
    private async Task<PortalUser> User(string id, string stamp)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id) ?? throw new InvalidOperationException("Tài khoản không tồn tại.");
        if (string.IsNullOrWhiteSpace(stamp) || user.ConcurrencyStamp != stamp) throw new DbUpdateConcurrencyException("Tài khoản đã thay đổi; tải lại trước khi lưu.");
        return user;
    }
    private void Audit(string actor, string action, object detail) => db.AuditLogs.Add(new AuditLog { Actor = actor, Action = action, Detail = JsonSerializer.Serialize(detail) });
    private static void Require(ContentActor actor, string permission) { if (!actor.Has(permission)) throw new UnauthorizedAccessException("Thiếu quyền thực hiện thao tác."); }
    private static void CheckCatalog(IEnumerable<string> permissions) { if (permissions.Any(x => !Permissions.All.Contains(x, StringComparer.Ordinal))) throw new InvalidOperationException("Quyền ngoài catalog không được phép."); }
    private static void ValidateText(string value, int max, string label, bool required) { if (value is null || value.Length > max || (required && string.IsNullOrWhiteSpace(value)) || value.Any(char.IsControl)) throw new InvalidOperationException(label + " không hợp lệ."); }
    private static void Check(IdentityResult result)
    {
        if(result.Succeeded)return;
        // Display only: keep Identity validators, error codes and rejection behavior.
        static string Label(string code)=>code switch
        {
            "DuplicateUserName"=>"Tên đăng nhập đã được sử dụng.","DuplicateEmail"=>"Email đã được sử dụng.","DuplicateRoleName"=>"Tên nhóm quyền đã được sử dụng.",
            "InvalidUserName"=>"Tên đăng nhập chưa hợp lệ.","InvalidEmail"=>"Địa chỉ email chưa hợp lệ.","InvalidRoleName"=>"Tên nhóm quyền chưa hợp lệ.",
            "ConcurrencyFailure"=>"Thông tin đã thay đổi; tải lại trước khi thao tác.","UserAlreadyInRole"=>"Tài khoản đã thuộc nhóm quyền này.","UserNotInRole"=>"Tài khoản chưa thuộc nhóm quyền này.",
            _ when code.StartsWith("Password",StringComparison.Ordinal)=>PortalIdentityPolicy.PasswordError(code),
            _=>"Chưa thể hoàn tất thay đổi tài khoản; kiểm tra thông tin và thử lại."
        };
        throw new InvalidOperationException(string.Join(" ",result.Errors.Select(x=>Label(x.Code)).Distinct()));
    }
}
