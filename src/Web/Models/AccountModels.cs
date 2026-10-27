using System.ComponentModel.DataAnnotations;

namespace LandPortal.Web.Models;

public sealed record UserListRow(string Id, string UserName, string Email, string DisplayName, bool Disabled, bool MustChangePassword);
public sealed class UserListModel
{
    public string? Query { get; set; }
    public int Page { get; set; }
    public int Total { get; set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / 20d));
    public IReadOnlyList<UserListRow> Items { get; set; } = [];
}
public sealed class CreateAccountModel
{
    [Required, StringLength(256), Display(Name = "Tên đăng nhập")] public string UserName { get; set; } = "";
    [Required, EmailAddress, StringLength(256), Display(Name = "Email nội bộ")] public string Email { get; set; } = "";
    [StringLength(200), Display(Name = "Tên hiển thị")] public string DisplayName { get; set; } = "";
    [Required, StringLength(256), IdentityPasswordLength, DataType(DataType.Password), Display(Name = "Mật khẩu tạm")] public string TemporaryPassword { get; set; } = "";
}
public sealed class AccountDetailModel
{
    public bool UnrestrictedCategories { get; set; }
    public IReadOnlyList<EditorCategory> Categories { get; set; }=[];
    public IReadOnlySet<int> CategoryIds { get; set; }=new HashSet<int>();
    public string Id { get; set; } = "";
    public string UserName { get; set; } = "";
    public string ConcurrencyStamp { get; set; } = "";
    [Required, EmailAddress, StringLength(256), Display(Name = "Email nội bộ")] public string Email { get; set; } = "";
    [StringLength(200), Display(Name = "Tên hiển thị")] public string DisplayName { get; set; } = "";
    public bool Disabled { get; set; }
    public bool MustChangePassword { get; set; }
    public bool CanGrant { get; set; }
    public bool CanChangeSensitive { get; set; }
    public IReadOnlyList<RoleChoice> Roles { get; set; } = [];
    public List<string> RoleIds { get; set; } = [];
    public IReadOnlyList<string> EffectivePermissions { get; set; } = [];
}
public sealed record RoleChoice(string Id, string Name, bool Sensitive);
public sealed record RoleListRow(string Id, string Name, int MemberCount, IReadOnlyList<string> Permissions);
public sealed class RoleListModel
{
    public string? Query { get; set; }
    public int Page { get; set; }
    public int Total { get; set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / 20d));
    public IReadOnlyList<RoleListRow> Items { get; set; } = [];
}
public sealed class RoleEditModel
{
    public string? Id { get; set; }
    public string? ConcurrencyStamp { get; set; }
    [Required, StringLength(100), Display(Name = "Tên nhóm quyền")] public string Name { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
    public IReadOnlyList<PermissionChoice> Catalog { get; set; } = [];
    public bool CanGrant { get; set; }
    public bool IsOwnRole { get; set; }
}
public sealed record PermissionChoice(string Key, bool Sensitive, bool CanDelegate);
public sealed class ChangeOwnPasswordModel
{
    [Required, StringLength(256), DataType(DataType.Password), Display(Name = "Mật khẩu hiện tại")] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(256), IdentityPasswordLength, DataType(DataType.Password), Display(Name = "Mật khẩu mới")] public string NewPassword { get; set; } = "";
    [Required, StringLength(256), DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu nhập lại không khớp."), Display(Name = "Nhập lại mật khẩu mới")] public string ConfirmPassword { get; set; } = "";
    public bool RequiredChange { get; set; }
}
