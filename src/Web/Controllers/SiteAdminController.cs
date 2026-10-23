using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

[Authorize]
public sealed class SiteAdminController(PortalDbContext db, SiteConfigurationService sites, PublicSiteService publicSites,
    CurrentPermissionService permissions, PublicContentQuery query, MediaUploadOptions options, StorageOptions storage) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private async Task<ContentActor> ActorAsync() => await permissions.GetAsync(UserId);
    private static bool CanRead(ContentActor actor) => actor.Has(Permissions.ViewSite) || actor.Has(Permissions.ManageSite) || actor.Has(Permissions.PublishSite);
    private static byte[] Version(string? value)
    { try { var bytes = Convert.FromBase64String(value ?? ""); if (bytes.Length == 8) return bytes; } catch (FormatException) { } throw new SiteConfigurationException("Thiếu phiên bản cấu hình hợp lệ.", 409); }

    [HttpGet("admin/site")]
    public async Task<IActionResult> Index(string section = "contact")
    {
        var actor = await ActorAsync(); if (!CanRead(actor)) return Forbid();
        var state = await sites.ReadAsync(false); if (state is null) return StatusCode(503, "Cấu hình SQL chưa khởi tạo. Dùng lệnh vận hành site-initialize sau migration.");
        var history = section == "history" ? await db.Set<SiteConfigurationRevision>().AsNoTracking().OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id).Take(50).Select(x => new SiteHistoryRow(x.Id, x.ContentHash, x.EditedByUserId, x.CreatedUtc)).ToListAsync() : [];
        return View(new SiteAdminModel { State = state, Section = section, CanSave = actor.Has(Permissions.ManageSite), CanPublish = actor.Has(Permissions.PublishSite), History = history, ProtectedCategoryIds = (await db.Categories.Where(x => x.SourceUrl != "").Select(x => x.Id).ToListAsync()).ToHashSet() });
    }

    [HttpGet("admin/site/edit"), Authorize(Policy = Permissions.ManageSite)]
    public async Task<IActionResult> Edit(string section = "contact", Guid? id = null, int? categoryId = null, string contentSearch = "", string mediaSearch = "")
    {
        var state = await sites.ReadAsync(false); if (state is null) return NotFound();
        var r = state.Revision; var model = new SiteEditModel { Section = section, RowVersion = Convert.ToBase64String(state.Header.RowVersion), ItemId = id, CategoryId = categoryId, ContentSearch = contentSearch, MediaSearch = mediaSearch };
        switch (section)
        {
            case "contact": model.Contact = new() { EnglishName=r.EnglishName,SupervisingAgency=r.SupervisingAgency,ResponsiblePerson=r.ResponsiblePerson,LicenseNumber=r.LicenseNumber,SeoDescription=r.SeoDescription,OrganizationName = r.OrganizationName, FooterText = r.FooterText, ContactAddress = r.ContactAddress, ContactPhone = r.ContactPhone, ContactHotline = r.ContactHotline, ContactEmail = r.ContactEmail, LogoAssetId = r.LogoAssetId }; break;
            case "categories":
                if (categoryId.HasValue) { var x = r.Categories.SingleOrDefault(x => x.CategoryId == categoryId); if (x is null) return NotFound(); model.Category = new() { DisplayName = x.DisplayName, Slug = x.Slug, Description = x.Description, ParentCategoryId = x.ParentCategoryId, SortOrder = x.SortOrder, ShowInNavigation = x.ShowInNavigation }; model.SystemProtected = await db.Categories.AnyAsync(c => c.Id == categoryId && c.SourceUrl != ""); } break;
            case "menus":
                if (id.HasValue) { var x = r.Menus.SingleOrDefault(x => x.Id == id); if (x is null) return NotFound(); model.Menu = new() { Label = x.Label, Position = x.Position, ParentId = x.ParentId, TargetKind = x.TargetKind, CategoryId = x.CategoryId, ContentId = x.ContentId, RouteKey = x.RouteKey, ExternalUrl = x.ExternalUrl, OpenNewWindow = x.OpenNewWindow, SortOrder = x.SortOrder, Enabled=x.Enabled }; model.SystemProtected = false; } break;
            case "banners":
                model.Banner.Position = "home";
                if (id.HasValue) { var x = r.Banners.SingleOrDefault(x => x.Id == id); if (x is null) return NotFound(); model.Banner = new() { Label = x.Label, Alt = x.Alt, Position = x.Position, TargetKind = x.TargetKind, CategoryId = x.CategoryId, ContentId = x.ContentId, RouteKey = x.RouteKey, ExternalUrl = x.ExternalUrl, OpenNewWindow = x.OpenNewWindow, SortOrder = x.SortOrder, MediaAssetId = x.MediaAssetId, StartVietnam = x.StartUtc?.ToOffset(TimeSpan.FromHours(7)).DateTime, EndVietnam = x.EndUtc?.ToOffset(TimeSpan.FromHours(7)).DateTime }; } break;
            case "blocks":
                if (id.HasValue) { var x = r.Blocks.SingleOrDefault(x => x.Id == id); if (x is null) return NotFound(); model.Block = new() { Type = x.Type, Title = x.Title, Limit = x.Limit, CategoryId = x.CategoryId, SortOrder = x.SortOrder, Enabled = x.Enabled, PinnedContentIds = r.Pins.Where(p => p.BlockId == id).OrderBy(p => p.SortOrder).Select(p => p.ContentId).ToArray() }; } break;
            case "vocabulary":
                if (id.HasValue) { var x = r.Vocabularies.SingleOrDefault(x => x.Id == id); if (x is null) return NotFound(); model.Vocabulary = new() { Kind = x.Kind, Label = x.Label, SortOrder = x.SortOrder, Active = x.Active }; } break;
            default: return NotFound();
        }
        await PopulateAsync(model, r, await ActorAsync()); return View(model);
    }

    [HttpPost("admin/site/disclosure-menu"), Authorize(Policy = Permissions.ManageSite)]
    public async Task<IActionResult> DisclosureMenu(string rowVersion)
    {
        try
        {
            await sites.MutateAsync(await ActorAsync(), Version(rowVersion), SiteConfigurationService.AddDisclosureMenu, "AddDisclosureMenu");
            TempData["Success"] = "Đã thêm nhóm Công khai thông tin vào cấu hình nháp. Xác nhận danh mục nếu còn thiếu, xem trước rồi áp dụng bản nháp theo quyền để đổi menu công khai.";
            return Redirect("/admin/site?section=menus");
        }
        catch (SiteConfigurationException error) { return StatusCode(error.StatusCode, error.Message); }
        catch (DbUpdateConcurrencyException) { return Conflict("Cấu hình đã đổi. Tải lại trước khi thêm menu."); }
    }
    [HttpPost("admin/site/disclosure-catalog"),Authorize(Policy=Permissions.ManageSite),Authorize(Policy=Permissions.PublishSite)]
    public async Task<IActionResult> DisclosureCatalog(string rowVersion,bool confirmed)
    {
        if(!confirmed)return BadRequest("Cần xác nhận ba tên chuyên mục đúng đặc tả trước khi kích hoạt.");
        try{await sites.ConfirmDisclosureCatalogAsync(await ActorAsync(),Version(rowVersion));TempData["Success"]="Đã xác nhận ba tên danh mục. Chưa áp dụng menu nháp hoặc xuất bản bài viết.";return Redirect("/admin/site?section=menus");}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(SiteConfigurationException e){return StatusCode(e.StatusCode,e.Message);}
        catch(DbUpdateConcurrencyException){return Conflict("Danh mục đã thay đổi; tải lại để kiểm tra.");}
    }
    [HttpPost("admin/site/save"), Authorize(Policy = Permissions.ManageSite)]
    public async Task<IActionResult> Save(SiteEditModel model)
    {
        var actor = await ActorAsync();
        Normalize(model);
        try
        {
            if (!ModelState.IsValid) throw new SiteConfigurationException("Kiểm tra các trường cấu hình.");
            var state = await sites.ReadAsync(false) ?? throw new SiteConfigurationException("Cấu hình chưa khởi tạo.", 503);
            var newCategoryId = Math.Max(await db.Categories.MaxAsync(x => (int?)x.Id) ?? 0, state.Revision.Categories.Select(x => x.CategoryId).DefaultIfEmpty().Max()) + 1;
            var result = await sites.MutateAsync(actor, Version(model.RowVersion), r =>
            {
                switch (model.Section)
                {
                    case "contact": r.EnglishName=Optional(model.Contact.EnglishName);r.SupervisingAgency=Optional(model.Contact.SupervisingAgency);r.ResponsiblePerson=Optional(model.Contact.ResponsiblePerson);r.LicenseNumber=Optional(model.Contact.LicenseNumber);r.SeoDescription=Optional(model.Contact.SeoDescription);r.OrganizationName = model.Contact.OrganizationName.Trim(); r.FooterText = model.Contact.FooterText.Trim(); r.ContactAddress = model.Contact.ContactAddress.Trim(); r.ContactPhone = model.Contact.ContactPhone.Trim(); r.ContactHotline = model.Contact.ContactHotline.Trim(); r.ContactEmail = model.Contact.ContactEmail.Trim(); r.LogoAssetId = model.Contact.LogoAssetId; break;
                    case "categories":
                        var category = model.CategoryId.HasValue ? r.Categories.SingleOrDefault(x => x.CategoryId == model.CategoryId) : null;
                        if (model.CategoryId.HasValue && category is null) throw new SiteConfigurationException("Chuyên mục không có trong cấu hình.", 404);
                        category ??= new SiteCategoryConfiguration { CategoryId = newCategoryId }; if (!r.Categories.Contains(category)) r.Categories.Add(category);
                        category.DisplayName = model.Category.DisplayName.Trim(); category.Slug = model.Category.Slug.Trim(); category.Description = model.Category.Description.Trim(); category.ParentCategoryId = model.Category.ParentCategoryId; category.SortOrder = model.Category.SortOrder; category.ShowInNavigation = model.Category.ShowInNavigation; break;
                    case "menus":
                        var menu = model.ItemId.HasValue ? r.Menus.SingleOrDefault(x => x.Id == model.ItemId) : null;
                        if (model.ItemId.HasValue && menu is null) throw new SiteConfigurationException("Menu không tồn tại.", 404);
                        menu ??= new(); if (!r.Menus.Contains(menu)) r.Menus.Add(menu);
                        menu.Label = model.Menu.Label.Trim(); menu.Position = model.Menu.Position; menu.ParentId = model.Menu.ParentId; menu.TargetKind = model.Menu.TargetKind;
                        menu.CategoryId = model.Menu.TargetKind == "Category" ? model.Menu.CategoryId : null; menu.ContentId = model.Menu.TargetKind == "Content" ? model.Menu.ContentId : null; menu.RouteKey = model.Menu.TargetKind == "Route" ? model.Menu.RouteKey : ""; menu.ExternalUrl = model.Menu.TargetKind is "External" or "Internal" ? model.Menu.ExternalUrl.Trim() : ""; menu.OpenNewWindow = model.Menu.OpenNewWindow; menu.SortOrder = model.Menu.SortOrder; menu.Enabled=model.Menu.Enabled; break;
                    case "banners":
                        var banner = model.ItemId.HasValue ? r.Banners.SingleOrDefault(x => x.Id == model.ItemId) : null;
                        if (model.ItemId.HasValue && banner is null) throw new SiteConfigurationException("Banner không tồn tại.", 404);
                        banner ??= new(); if (!r.Banners.Contains(banner)) r.Banners.Add(banner);
                        banner.Label = model.Banner.Label.Trim(); banner.Alt = model.Banner.Alt.Trim(); banner.Position = model.Banner.Position; banner.MediaAssetId = model.Banner.MediaAssetId ?? Guid.Empty; banner.TargetKind = model.Banner.TargetKind;
                        banner.CategoryId = model.Banner.TargetKind == "Category" ? model.Banner.CategoryId : null; banner.ContentId = model.Banner.TargetKind == "Content" ? model.Banner.ContentId : null; banner.RouteKey = model.Banner.TargetKind == "Route" ? model.Banner.RouteKey : ""; banner.ExternalUrl = model.Banner.TargetKind == "External" ? model.Banner.ExternalUrl.Trim() : ""; banner.OpenNewWindow = model.Banner.OpenNewWindow; banner.SortOrder = model.Banner.SortOrder; banner.StartUtc = Vietnam(model.Banner.StartVietnam); banner.EndUtc = Vietnam(model.Banner.EndVietnam); break;
                    case "blocks":
                        var block = model.ItemId.HasValue ? r.Blocks.SingleOrDefault(x => x.Id == model.ItemId) : null;
                        if (model.ItemId.HasValue && block is null) throw new SiteConfigurationException("Khối không tồn tại.", 404);
                        block ??= new(); if (!r.Blocks.Contains(block)) r.Blocks.Add(block);
                        block.Type = model.Block.Type; block.Title = model.Block.Title.Trim(); block.Limit = model.Block.Limit; block.CategoryId = model.Block.Type == "Category" ? model.Block.CategoryId : null; block.SortOrder = model.Block.SortOrder; block.Enabled = model.Block.Enabled;
                        r.Pins.RemoveAll(x => x.BlockId == block.Id); var order = 0; foreach (var content in model.Block.PinnedContentIds.Distinct().Take(12)) r.Pins.Add(new() { BlockId = block.Id, ContentId = content, SortOrder = order++ }); break;
                    case "vocabulary":
                        var vocabulary = model.ItemId.HasValue ? r.Vocabularies.SingleOrDefault(x => x.Id == model.ItemId) : null;
                        if (model.ItemId.HasValue && vocabulary is null) throw new SiteConfigurationException("Danh mục phụ không tồn tại.", 404);
                        vocabulary ??= new(); if (!r.Vocabularies.Contains(vocabulary)) r.Vocabularies.Add(vocabulary);
                        vocabulary.Kind = model.Vocabulary.Kind; vocabulary.Label = model.Vocabulary.Label.Trim(); vocabulary.SortOrder = model.Vocabulary.SortOrder; vocabulary.Active = model.Vocabulary.Active; break;
                    default: throw new SiteConfigurationException("Mẫu cấu hình không hợp lệ.");
                }
            }, "Lưu mẫu " + model.Section + "; item=" + (model.ItemId?.ToString() ?? model.CategoryId?.ToString() ?? "new"));
            if (Request.Query["format"] == "json") return Json(new { revisionId = result.Revision.Id, rowVersion = Convert.ToBase64String(result.Header.RowVersion) });
            TempData["Success"] = "Đã lưu phiên bản cấu hình nháp trong SQL; bản công khai chưa đổi."; return RedirectToAction(nameof(Index), new { section = model.Section });
        }
        catch (SiteConfigurationException error)
        {
            if (Request.Query["format"] == "json") return StatusCode(error.StatusCode, new { error = error.Message });
            Response.StatusCode = error.StatusCode; ModelState.AddModelError("", error.Message);
            var state = await sites.ReadAsync(false); if (state is not null) await PopulateAsync(model, state.Revision, actor); return View("Edit", model);
        }
    }

    [HttpPost("admin/site/publish"), Authorize(Policy = Permissions.PublishSite)]
    public async Task<IActionResult> Publish(string rowVersion, Guid revisionId) => await Command(async () => await sites.PublishAsync(await ActorAsync(), Version(rowVersion), revisionId), "Đã áp dụng đồng bộ cấu hình công khai.");
    [HttpPost("admin/site/restore"), Authorize(Policy = Permissions.ManageSite)]
    public async Task<IActionResult> Restore(string rowVersion, Guid revisionId) => await Command(async () => await sites.RestoreAsync(await ActorAsync(), Version(rowVersion), revisionId), "Đã tạo bản nháp mới từ phiên bản cũ; chưa công khai.");
    [HttpPost("admin/site/remove"), Authorize(Policy = Permissions.ManageSite)]
    public async Task<IActionResult> Remove(string rowVersion, string section, Guid? id, int? categoryId)
    {
        return await Command(async () =>
        {
            if (section == "categories")
            {
                var state = await sites.ReadAsync(false) ?? throw new SiteConfigurationException("Cấu hình chưa có.");
                var probe = SiteConfigurationService.Clone(state.Revision, UserId, DateTimeOffset.UtcNow); await sites.DeleteCategoryAsync(probe, categoryId ?? 0);
            }
            return await sites.MutateAsync(await ActorAsync(), Version(rowVersion), r =>
            {
                switch (section)
                {
                    case "categories": r.Categories.RemoveAll(x => x.CategoryId == categoryId); break;
                    case "menus": if (r.Menus.Any(x => x.ParentId == id)) throw new SiteConfigurationException("Chuyển menu con trước khi bỏ nhóm."); r.Menus.RemoveAll(x => x.Id == id); break;
                    case "banners": r.Banners.RemoveAll(x => x.Id == id); break;
                    case "blocks": r.Blocks.RemoveAll(x => x.Id == id); r.Pins.RemoveAll(x => x.BlockId == id); break;
                    case "vocabulary": throw new SiteConfigurationException("Danh mục phụ không hard-delete; sửa để ngừng chọn, giữ tham chiếu lịch sử.");
                    default: throw new SiteConfigurationException("Mẫu không hợp lệ.");
                }
            }, "Bỏ mục trong bản nháp " + section);
        }, "Đã bỏ tham chiếu trong bản nháp; bản công khai và tài nguyên dùng chung được giữ.", section);
    }
    [HttpPost("admin/site/move"), Authorize(Policy = Permissions.ManageSite)]
    public async Task<IActionResult> Move(string rowVersion, string section, Guid id, int direction) => await Command(async () => await sites.MutateAsync(await ActorAsync(), Version(rowVersion), r =>
    {
        if (direction is not (-1 or 1)) throw new SiteConfigurationException("Hướng sắp xếp không hợp lệ.");
        if (section == "menus") { var item = r.Menus.Single(x => x.Id == id); var items = r.Menus.Where(x => x.Position == item.Position && x.ParentId == item.ParentId).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList(); Reorder(items, item, direction, x => x.SortOrder, (x, n) => x.SortOrder = n); }
        else if (section == "blocks") { var item = r.Blocks.Single(x => x.Id == id); Reorder(r.Blocks.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList(), item, direction, x => x.SortOrder, (x, n) => x.SortOrder = n); }
        else throw new SiteConfigurationException("Chỉ sắp xếp menu/khối bằng nút này.");
    }, "Sắp xếp " + section), "Đã lưu thứ tự bản nháp.", section);

    [HttpGet("admin/site/preview")]
    public async Task<IActionResult> Preview(Guid? revisionId)
    {
        if (!CanRead(await ActorAsync())) return Forbid();
        var state = await sites.ReadAsync(false, revisionId); if (state is null) return NotFound();
        HttpContext.Items[PublicSiteService.PreviewKey] = state.Revision;
        var site = await publicSites.GetAsync(); return View("~/Views/Home/Configured.cshtml", await publicSites.HomeAsync(site!));
    }
    [HttpGet("admin/site/media/{id:guid}")]
    public async Task<IActionResult> PreviewMedia(Guid id)
    {
        var actor = await ActorAsync(); if (!CanRead(actor)) return NotFound();
        var linked = await db.Set<SiteConfigurationRevision>().AnyAsync(x => x.LogoAssetId == id) || await db.Set<SiteBanner>().AnyAsync(x => x.MediaAssetId == id);
        var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        var publicContent = await db.ContentAssets.AnyAsync(a => a.MediaAssetId == id && db.Contents.Any(c => c.IsPublic && !c.IsTrashed && c.PublishedRevisionId == a.RevisionId));
        if (asset is null || !options.CanServe(asset) || !linked && !publicContent && (!actor.Has(Permissions.ManageSite) || asset.UploadedByUserId != UserId)) return NotFound();
        if (asset.MimeType is not ("image/jpeg" or "image/png" or "image/gif" or "image/webp")) return NotFound();
        string path; try { path = MediaService.ResolveMediaPath(storage, asset.StorageKey); } catch (Exception ex) when (ex is InvalidOperationException or MediaUploadException) { return NotFound(); }
        if (!System.IO.File.Exists(path)) return NotFound(); Response.Headers.CacheControl = "no-store, private";
        Request.Headers.Remove("If-None-Match"); Request.Headers.Remove("If-Modified-Since"); Request.Headers.Remove("If-Range");
        return PhysicalFile(path, asset.MimeType, enableRangeProcessing: true);
    }

    private async Task PopulateAsync(SiteEditModel model, SiteConfigurationRevision r, ContentActor actor)
    {
        model.CanUpload = actor.Has(Permissions.ManageMedia) && options.UploadEnabled;
        model.Categories = r.Categories.OrderBy(x => x.SortOrder).Select(x => new SiteSelectItem(x.CategoryId.ToString(), x.DisplayName)).ToList();
        model.Parents = r.Menus.Where(x => x.TargetKind == "Group" && x.Id != model.ItemId).OrderBy(x => x.SortOrder).Select(x => new SiteSelectItem(x.Id.ToString(), x.Position + " · " + x.Label)).ToList();
        var selected = new[] { model.Menu.ContentId, model.Banner.ContentId }.Where(x => x.HasValue).Select(x => x!.Value).Concat(model.Block.PinnedContentIds).ToArray();
        var rows = query.Published(); if (model.ContentSearch.Length > 0) rows = query.Filter(rows, model.ContentSearch[..Math.Min(200, model.ContentSearch.Length)]);
        // Picker needs identifiers/labels only. Do not materialize thumbnails,
        // summaries and document metadata, or query articles at all for logo/contact.
        var choices = model.Section is "menus" or "banners" or "blocks"
            ? await PublicContentQuery.Recent(rows).Take(60).Select(x=>new{x.Id,x.Kind,x.Title}).ToListAsync()
            : [];
        var chosenIds = choices.Select(x => x.Id).ToArray(); if (selected.Length > 0) choices.AddRange(await query.Published().Where(x => selected.Contains(x.Id) && !chosenIds.Contains(x.Id)).Take(12).Select(x=>new{x.Id,x.Kind,x.Title}).ToListAsync());
        model.Contents = choices.Select(x => new SiteSelectItem(x.Id.ToString(), PublicContentQuery.KindLabel(x.Kind) + " · " + x.Title)).ToList();
        var images = db.MediaAssets.AsNoTracking().Where(x => !x.IsQuarantined && x.MimeType.StartsWith("image/") && (x.UploadedByUserId == actor.UserId || db.ContentAssets.Any(a => a.MediaAssetId == x.Id && db.Contents.Any(c => c.IsPublic && !c.IsTrashed && c.PublishedRevisionId == a.RevisionId)) || db.Set<SiteConfigurationRevision>().Any(v => v.LogoAssetId == x.Id) || db.Set<SiteBanner>().Any(b => b.MediaAssetId == x.Id)));
        if (model.MediaSearch.Length > 0) images = images.Where(x => x.OriginalName.Contains(model.MediaSearch));
        model.Images = model.Section is "contact" or "banners" ? (await images.OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id).Take(60).ToListAsync()).Where(options.CanServe).Select(x => new SiteSelectItem(x.Id.ToString(), x.OriginalName + " · " + x.ScanStatus)).ToList() : [];
        if (model.Section == "categories" && model.CategoryId.HasValue) model.SystemProtected = await db.Categories.AnyAsync(x => x.Id == model.CategoryId && x.SourceUrl != "");
        if (model.Section == "menus" && model.ItemId.HasValue) model.SystemProtected = false;
    }
    private async Task<IActionResult> Command(Func<Task<SiteConfigurationState>> command, string message, string section = "contact")
    {
        try { var result = await command(); if (Request.Query["format"] == "json") return Json(new { revisionId = result.Revision.Id, rowVersion = Convert.ToBase64String(result.Header.RowVersion) }); TempData["Success"] = message; return RedirectToAction(nameof(Index), new { section }); }
        catch (SiteConfigurationException error) { return StatusCode(error.StatusCode, error.Message); }
        catch (InvalidOperationException) { return BadRequest("Mục cấu hình đã đổi hoặc không tồn tại; tải lại."); }
    }
    private static DateTimeOffset? Vietnam(DateTime? value) => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).ToUniversalTime() : null;
    private static string? Optional(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static void Normalize(SiteEditModel m)
    {
        m.Contact.OrganizationName ??= ""; m.Contact.FooterText ??= ""; m.Contact.ContactAddress ??= ""; m.Contact.ContactPhone ??= ""; m.Contact.ContactHotline ??= ""; m.Contact.ContactEmail ??= "";
        m.Category.DisplayName ??= ""; m.Category.Slug ??= ""; m.Category.Description ??= "";
        m.Menu.Label ??= ""; m.Menu.Position ??= ""; m.Menu.TargetKind ??= ""; m.Menu.RouteKey ??= ""; m.Menu.ExternalUrl ??= "";
        m.Banner.Label ??= ""; m.Banner.Alt ??= ""; m.Banner.Position ??= ""; m.Banner.TargetKind ??= ""; m.Banner.RouteKey ??= ""; m.Banner.ExternalUrl ??= "";
        m.Block.Title ??= ""; m.Block.Type ??= ""; m.Block.PinnedContentIds ??= []; m.Vocabulary.Kind ??= ""; m.Vocabulary.Label ??= "";
    }
    private static void Reorder<T>(List<T> items, T item, int direction, Func<T, int> order, Action<T, int> set)
    { var index = items.IndexOf(item); var other = index + direction; if (other < 0 || other >= items.Count) return; (items[index], items[other]) = (items[other], items[index]); for (var i = 0; i < items.Count; i++) set(items[i], i); }
}
