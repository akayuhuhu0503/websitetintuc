using LandPortal.Domain;

namespace LandPortal.Infrastructure;

public static class MaterialsMenu
{
    // Called only by the existing versioned CMS draft workflow, never at startup.
    public static void Add(SiteConfigurationRevision revision, int categoryId)
    {
        if (revision.Menus.Any(x => x.TargetKind == "Route" && x.RouteKey == "Materials"))
            throw new SiteConfigurationException("Mục Tài liệu đã có trong bản nháp. Chọn Sửa để điều chỉnh.");
        if (revision.Categories.Any(x => x.Slug == "tai-lieu" || x.CategoryId == categoryId))
            throw new SiteConfigurationException("Chuyên mục Tài liệu đã có. Thêm menu Mục hệ thống → Tài liệu và chọn đúng chuyên mục hiện có.");
        var legal = revision.Menus.Where(x => x.Position == "main" && x.TargetKind == "Route" && x.RouteKey is "Legal" or "DraftDocuments").ToArray();
        if (legal.Length != 2 || legal.Select(x => x.ParentId).Distinct().Count() != 1)
            throw new SiteConfigurationException("Chọn nhóm cha Văn bản khi thêm menu Tài liệu; hai mục văn bản hiện có chưa cùng một nhóm.");
        var parent = legal[0].ParentId;
        if (parent is null)
        {
            var group = new SiteMenuItem { Label = "Văn bản", TargetKind = "Group", Position = "main", SortOrder = legal.Min(x => x.SortOrder) };
            revision.Menus.Add(group); parent = group.Id;
            foreach (var item in legal) item.ParentId = parent;
        }
        revision.Categories.Add(new() { CategoryId = categoryId, DisplayName = "Tài liệu", Slug = "tai-lieu",
            Description = "Tài liệu được biên tập, duyệt và công bố.", SortOrder = revision.Categories.Select(x => x.SortOrder).DefaultIfEmpty().Max() + 1 });
        revision.Menus.Add(new() { Label = "Tài liệu", TargetKind = "Route", RouteKey = "Materials", CategoryId = categoryId,
            ParentId = parent, Position = "main", SortOrder = revision.Menus.Where(x => x.ParentId == parent).Select(x => x.SortOrder).DefaultIfEmpty().Max() + 1 });
    }
}
