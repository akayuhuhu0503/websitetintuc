using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace LandPortal.Infrastructure;

public sealed class SiteConfigurationException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
public sealed record SiteConfigurationState(SiteConfiguration Header, SiteConfigurationRevision Revision);

public sealed class SiteConfigurationService(PortalDbContext db, StorageOptions storage, MediaUploadOptions mediaOptions, TimeProvider? clock = null)
{
    public static readonly IReadOnlyDictionary<string, (string Label, string Url)> Routes = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        ["Home"] = ("Trang chủ", "/"), ["News"] = ("Tin tức", "/tin-tuc"),
        ["Leadership"] = ("Lãnh đạo cục", "/gioi-thieu/lanh-dao-cuc"),
        ["Functions"] = ("Chức năng nhiệm vụ", "/gioi-thieu/chuc-nang-nhiem-vu"),
        ["Organization"] = ("Cơ cấu tổ chức", "/gioi-thieu/co-cau-to-chuc"),
        ["Announcements"] = ("Thông báo", "/thong-bao"), ["Legal"] = ("Văn bản pháp luật", "/van-ban"),
        ["DraftDocuments"] = ("Văn bản dự thảo", "/du-thao"), ["Videos"] = ("Video", "/video"),
        ["Questions"] = ("Hỏi đáp", "/hoi-dap"), ["Contact"] = ("Liên hệ", "/lien-he"),
        ["Handbook"] = ("Sổ tay đất đai", "/so-tay"), ["ExecutiveDocuments"] = ("Văn bản chỉ đạo điều hành", "/van-ban-chi-dao-dieu-hanh"),
        ["LandPlanning"] = ("Quy hoạch sử dụng đất", "/quy-hoach-su-dung-dat"), ["Weekly"] = ("Lịch tuần", "/lich-tuan"),
        ["Disclosure"] = ("Công khai thông tin", "/cong-khai-thong-tin"),
        ["DisclosureCertificates"] = ("Giấy chứng nhận sai hỏng, hủy", "/cong-khai-thong-tin/giay-chung-nhan-sai-hong-huy"),
        ["DisclosureForeignResidents"] = ("Người nước ngoài định cư ở Việt Nam (chờ PO/pháp chế xác nhận)", "/cong-khai-thong-tin/nguoi-nuoc-ngoai-dinh-cu-o-viet-nam"),
        ["DisclosureViolations"] = ("Tổ chức vi phạm pháp luật đất đai", "/cong-khai-thong-tin/to-chuc-vi-pham-phap-luat-dat-dai")
    };
    public static void AddDisclosureMenu(SiteConfigurationRevision revision)
    {
        var parent = revision.Menus.FirstOrDefault(x => x.TargetKind == "Group" && x.Label == Routes["Disclosure"].Label);
        if (parent == null) { parent = new() { Label = Routes["Disclosure"].Label, TargetKind = "Group", Position = "main", SortOrder = revision.Menus.Where(x => x.Position == "main").Select(x => x.SortOrder).DefaultIfEmpty().Max() + 1 }; revision.Menus.Add(parent); }
        var order = 0;
        foreach (var key in new[] { "Disclosure", "DisclosureCertificates", "DisclosureForeignResidents", "DisclosureViolations" })
        { if (!revision.Menus.Any(x => x.TargetKind == "Route" && x.RouteKey == key)) revision.Menus.Add(new() { Label = Routes[key].Label, TargetKind = "Route", RouteKey = key, Position = parent.Position, ParentId = parent.Id, SortOrder = order }); order++; }
    }
    public static readonly string[] BlockTypes = ["Featured", "Recent", "Announcements", "Category", "LegalDocuments", "DraftDocuments", "Videos", "Links"];
    public static readonly string[] MenuPositions = ["main", "footer", "links", "supplementary"];
    public static readonly string[] BannerPositions = ["header", "home", "sidebar"];
    public static readonly string[] VocabularyKinds = ["Authority", "Type", "Field"];
    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();

    public async Task<SiteConfigurationState?> ReadAsync(bool live, Guid? revisionId = null)
    {
        var header = await db.Set<SiteConfiguration>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1);
        var selected = revisionId ?? (live ? header?.LiveRevisionId : header?.DraftRevisionId ?? header?.LiveRevisionId);
        if (header is null || selected is null) return null;
        var revision = await Revisions().AsNoTracking().SingleOrDefaultAsync(x => x.Id == selected);
        return revision is null ? null : new(header, revision);
    }

    // Invoked only by the explicit operator CLI, never from a web startup/request.
    public async Task<bool> InitializeAsync(string actor)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        if (await db.Set<SiteConfiguration>().AnyAsync()) return false;
        var revision = new SiteConfigurationRevision
        {
            EditedByUserId = actor, CreatedUtc = Now, OrganizationName = "CỤC QUẢN LÝ ĐẤT ĐAI",
            ContactAddress = "Số 10 Tôn Thất Thuyết, Phường Cầu Giấy, Hà Nội", ContactPhone = "024.3629.0211",
            ContactHotline = "024.3629.0196", ContactEmail = "vanphongcqldd@mae.gov.vn",
            FooterText = "Bản chạy thử. Thông tin nguồn và ngoại lệ chuyển dữ liệu được lưu trong hồ sơ dự án."
        };
        var categories = await db.Categories.AsNoTracking().Where(x => x.Id > 0).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
        foreach (var category in categories) revision.Categories.Add(new() { CategoryId = category.Id, DisplayName = category.Name, Slug = category.Slug, ParentCategoryId = category.ParentId, SortOrder = category.SortOrder, ShowInNavigation = true });
        var introduction = Menu(revision, "Giới thiệu", "Group", "", 1);
        Menu(revision, "Lãnh đạo cục", "Route", "Leadership", 0, introduction.Id);
        Menu(revision, "Chức năng nhiệm vụ", "Route", "Functions", 1, introduction.Id);
        Menu(revision, "Cơ cấu tổ chức", "Route", "Organization", 2, introduction.Id);
        Menu(revision, "Trang chủ", "Route", "Home", 0);
        var news = Menu(revision, "Tin tức", "Group", "", 2);
        foreach (var category in categories.Where(x => x.SortOrder < 14))
            revision.Menus.Add(new() { Label = category.Name, TargetKind = "Category", CategoryId = category.Id, ParentId = news.Id, SortOrder = category.SortOrder, SystemProtected = true });
        Menu(revision, "Tất cả tin tức", "Route", "News", 99, news.Id);
        var i = 3;
        foreach (var key in new[] { "Announcements", "Legal", "DraftDocuments", "Videos", "Questions", "Contact", "Handbook" }) Menu(revision, Routes[key].Label, "Route", key, i++);
        i = 0;
        foreach (var key in new[] { "ExecutiveDocuments", "LandPlanning", "Weekly" }) Menu(revision, Routes[key].Label, "Route", key, i++, position: "supplementary");
        i = 0;
        foreach (var key in new[] { "Legal", "DraftDocuments", "Announcements", "Handbook", "Questions", "Contact" }) Menu(revision, Routes[key].Label, "Route", key, i++, position: "footer");
        foreach (var (label, url) in new[] { ("Cổng thông tin nguồn", "https://qldd.mae.gov.vn/"), ("Bộ Nông nghiệp và Môi trường", "https://www.mae.gov.vn/"), ("Báo Nông nghiệp và Môi trường", "https://nongnghiepmoitruong.vn/"), ("Cục Trồng trọt và Bảo vệ thực vật", "https://www.ppd.gov.vn/"), ("Cục Lâm nghiệp và Kiểm lâm", "https://vnforest.gov.vn/"), ("Cục Chuyển đổi số", "https://dinre.mae.gov.vn/"), ("Cục quản lý tài nguyên nước", "https://dwrm.monre.gov.vn/") })
            revision.Menus.Add(new() { Label = label, TargetKind = "External", ExternalUrl = url, Position = "links", OpenNewWindow = true, SortOrder = i++ });
        revision.Blocks.Add(new() { Type = "Featured", Title = "Thông tin mới nhất", Limit = 4, SortOrder = 0 });
        revision.Blocks.Add(new() { Type = "Announcements", Title = "Thông báo", Limit = 5, SortOrder = 1 });
        i = 2;
        // These are existing homepage category groups, not copies of their content.
        foreach (var category in categories.Where(x => x.SortOrder < 6)) revision.Blocks.Add(new() { Type = "Category", Title = category.Name, CategoryId = category.Id, Limit = 3, SortOrder = i++ });
        revision.Blocks.Add(new() { Type = "LegalDocuments", Title = "Văn bản mới cập nhật", Limit = 4, SortOrder = i++ });
        revision.Blocks.Add(new() { Type = "Videos", Title = "Video", Limit = 2, SortOrder = i });
        var legal = await db.LegalDocuments.AsNoTracking().ToListAsync();
        foreach (var (kind, values) in new[] { ("Authority", legal.Select(x => x.IssuingBody)), ("Type", legal.Select(x => x.DocumentType)), ("Field", legal.Select(x => x.Field)) })
            foreach (var label in values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)) revision.Vocabularies.Add(new() { Kind = kind, Label = label, SortOrder = revision.Vocabularies.Count });
        Attach(revision); revision.ContentHash = Hash(revision);
        db.Add(revision); await db.SaveChangesAsync();
        db.Add(new SiteConfiguration { Id = 1, DraftRevisionId = revision.Id, LiveRevisionId = revision.Id, Generation = 1 });
        Audit(actor, "SiteInitialize", revision, "Khởi tạo một lần từ giao diện và hạng mục hiện có; không sửa payload nguồn.");
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return true;
    }

    public async Task<SiteConfigurationState> MutateAsync(ContentActor actor, byte[] version, Action<SiteConfigurationRevision> mutate, string detail)
    {
        Require(actor, Permissions.ManageSite);
        return await ChangeAsync(actor, version, async header =>
        {
            var source = await Revisions().AsNoTracking().SingleAsync(x => x.Id == (header.DraftRevisionId ?? header.LiveRevisionId));
            var revision = Clone(source, actor.UserId, Now); mutate(revision);
            await ValidateAsync(revision, actor, false); Attach(revision); revision.ContentHash = Hash(revision);
            db.Add(revision); await db.SaveChangesAsync(); header.DraftRevisionId = revision.Id;
            Audit(actor.UserId, "SiteSaveDraft", revision, detail); return revision;
        });
    }

    public async Task<SiteConfigurationState> RestoreAsync(ContentActor actor, byte[] version, Guid revisionId)
    {
        Require(actor, Permissions.ManageSite);
        return await ChangeAsync(actor, version, async header =>
        {
            var original = await Revisions().AsNoTracking().SingleOrDefaultAsync(x => x.Id == revisionId) ?? throw new SiteConfigurationException("Không tìm thấy phiên bản cấu hình.", 404);
            var revision = Clone(original, actor.UserId, Now);
            // Keep stable vocabulary identities introduced since the restored version, inactive.
            var liveVocabulary = await db.Set<SiteVocabulary>().AsNoTracking().Where(x => x.RevisionId == header.LiveRevisionId).ToListAsync();
            foreach (var value in liveVocabulary.Where(x => revision.Vocabularies.All(y => y.Id != x.Id))) revision.Vocabularies.Add(new() { Id = value.Id, Kind = value.Kind, Label = value.Label, SortOrder = value.SortOrder, Active = false });
            await ValidateAsync(revision, actor, false); Attach(revision); revision.ContentHash = Hash(revision);
            db.Add(revision); await db.SaveChangesAsync(); header.DraftRevisionId = revision.Id;
            Audit(actor.UserId, "SiteRestoreDraft", revision, "Khôi phục thành bản nháp từ " + revisionId); return revision;
        });
    }

    public async Task<SiteConfigurationState> PublishAsync(ContentActor actor, byte[] version, Guid revisionId)
    {
        Require(actor, Permissions.PublishSite);
        return await ChangeAsync(actor, version, async header =>
        {
            if (header.DraftRevisionId != revisionId) throw new SiteConfigurationException("Bản nháp đã thay đổi; tải lại trước khi áp dụng.", 409);
            var revision = await Revisions().AsNoTracking().SingleAsync(x => x.Id == revisionId);
            if (Hash(revision) != revision.ContentHash) throw new SiteConfigurationException("Hash cấu hình không hợp lệ.", 409);
            await ValidateAsync(revision, actor, true);
            var categoryIds = await db.Categories.Select(x => x.Id).ToListAsync();
            foreach (var category in revision.Categories.Where(x => !categoryIds.Contains(x.CategoryId)))
                db.Categories.Add(new() { Id = category.CategoryId, Name = category.DisplayName, Slug = category.Slug, SortOrder = category.SortOrder, ParentId = category.ParentCategoryId });
            // Existing Category rows are stable source identities. Display fields remain versioned here.
            var previous = header.LiveRevisionId; header.LiveRevisionId = revision.Id;
            Audit(actor.UserId, "SitePublish", revision, "Live: " + previous + " → " + revision.Id); return revision;
        });
    }

    private async Task<SiteConfigurationState> ChangeAsync(ContentActor actor, byte[] version, Func<SiteConfiguration, Task<SiteConfigurationRevision>> change)
    {
        if (version.Length != 8) throw new SiteConfigurationException("Thiếu phiên bản cấu hình hợp lệ.", 409);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var header = await db.Set<SiteConfiguration>().SingleOrDefaultAsync(x => x.Id == 1) ?? throw new SiteConfigurationException("Cấu hình SQL chưa được khởi tạo bằng lệnh vận hành.", 503);
            if (!header.RowVersion.SequenceEqual(version)) throw new SiteConfigurationException("Người khác đã sửa cấu hình; bản đang áp dụng được giữ nguyên.", 409);
            db.Entry(header).Property(x => x.RowVersion).OriginalValue = version;
            var revision = await change(header); header.Generation++;
            await db.SaveChangesAsync(); await transaction.CommitAsync(); return new(header, revision);
        }
        catch (DbUpdateConcurrencyException) { await transaction.RollbackAsync(); db.ChangeTracker.Clear(); throw new SiteConfigurationException("Xung đột cấu hình; tải lại bản mới nhất.", 409); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 }) { await transaction.RollbackAsync(); db.ChangeTracker.Clear(); throw new SiteConfigurationException("Định danh/slug đã được phiên khác dùng; tải lại cấu hình.", 409); }
        catch { await transaction.RollbackAsync(); db.ChangeTracker.Clear(); throw; }
    }

    public async Task ValidateAsync(SiteConfigurationRevision revision, ContentActor actor, bool publishing)
    {
        Text(revision.OrganizationName, 300, true); Text(revision.FooterText, 5000); Text(revision.ContactAddress, 1000);
        Text(revision.ContactPhone, 100); Text(revision.ContactHotline, 100); Text(revision.ContactEmail, 254);
        if (revision.ContactEmail.Length > 0 && (!System.Net.Mail.MailAddress.TryCreate(revision.ContactEmail, out var email) || email.Address != revision.ContactEmail)) throw new SiteConfigurationException("Email liên hệ công khai không hợp lệ.");
        if (revision.Categories.Count > 100 || revision.Menus.Count > 180 || revision.Banners.Count > 30 || revision.Blocks.Count > 30 || revision.Pins.Count > 100 || revision.Vocabularies.Count > 300) throw new SiteConfigurationException("Cấu hình vượt giới hạn số mục.");
        if (revision.Categories.Any(x => x.SortOrder is < 0 or > 10000) || revision.Menus.Any(x => x.SortOrder is < 0 or > 10000) || revision.Banners.Any(x => x.SortOrder is < 0 or > 10000) || revision.Blocks.Any(x => x.SortOrder is < 0 or > 10000) || revision.Vocabularies.Any(x => x.SortOrder is < 0 or > 10000)) throw new SiteConfigurationException("Thứ tự cần nằm trong khoảng 0–10000.");
        if (revision.Menus.GroupBy(x => x.Id).Any(x => x.Key == Guid.Empty || x.Count() > 1) || revision.Banners.GroupBy(x => x.Id).Any(x => x.Key == Guid.Empty || x.Count() > 1) || revision.Blocks.GroupBy(x => x.Id).Any(x => x.Key == Guid.Empty || x.Count() > 1) || revision.Vocabularies.GroupBy(x => x.Id).Any(x => x.Key == Guid.Empty || x.Count() > 1)) throw new SiteConfigurationException("ID cấu hình thiếu hoặc trùng.");
        var stable = await db.Categories.AsNoTracking().ToListAsync();
        var configuredStableIds = await db.Set<SiteCategoryConfiguration>().Where(x => db.Set<SiteConfiguration>().Any(c => c.Id == 1 && c.LiveRevisionId == x.RevisionId)).Select(x => x.CategoryId).ToArrayAsync();
        var removedIds = stable.Where(x => configuredStableIds.Contains(x.Id) && revision.Categories.All(y => y.CategoryId != x.Id)).Select(x => x.Id).ToArray();
        if (removedIds.Length > 0 && (await db.RevisionCategories.AnyAsync(x => removedIds.Contains(x.CategoryId)) || await db.Contents.AnyAsync(x => x.PrimaryCategoryId.HasValue && removedIds.Contains(x.PrimaryCategoryId.Value)))) throw new SiteConfigurationException("Không bỏ chuyên mục đã được nội dung/lịch sử dùng.");
        foreach (var category in stable.Where(x => x.SourceUrl.Length > 0))
        {
            var setting = revision.Categories.SingleOrDefault(x => x.CategoryId == category.Id);
            if (setting is null || setting.DisplayName != category.Name || setting.Slug != category.Slug || setting.ParentCategoryId != category.ParentId || !setting.ShowInNavigation)
                throw new SiteConfigurationException("Không được xóa/đổi/gộp tên, slug hoặc cấu trúc chuyên mục nguồn " + category.Id + ".");
        }
        foreach (var category in revision.Categories)
        {
            Text(category.DisplayName, 300, true); Text(category.Description, 2000);
            if (category.CategoryId < 1 && stable.All(x => x.Id != category.CategoryId) || !Regex.IsMatch(category.Slug, "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant) || category.Slug.Length > 180) throw new SiteConfigurationException("Slug chuyên mục không hợp lệ.");
            if (revision.Categories.Any(x => x != category && (x.CategoryId == category.CategoryId || x.Slug.Equals(category.Slug, StringComparison.OrdinalIgnoreCase)))) throw new SiteConfigurationException("ID/slug chuyên mục bị trùng.");
            var original = stable.SingleOrDefault(x => x.Id == category.CategoryId);
            if (original is not null && original.Slug != category.Slug) throw new SiteConfigurationException("Giữ slug tuyến đã dùng; đổi slug cần mapping ngoài mốc 2B.");
            if (category.ParentCategoryId.HasValue && !revision.Categories.Any(x => x.CategoryId == category.ParentCategoryId)) throw new SiteConfigurationException("Chuyên mục cha không tồn tại.");
            var visited = new HashSet<int> { category.CategoryId }; var parent = category.ParentCategoryId;
            while (parent.HasValue) { if (!visited.Add(parent.Value) || visited.Count > 4) throw new SiteConfigurationException("Cây chuyên mục bị chu trình hoặc vượt ba cấp."); parent = revision.Categories.Single(x => x.CategoryId == parent.Value).ParentCategoryId; }
        }
        // Source items cannot be silently removed or retargeted by posted fields.
        var liveId = await db.Set<SiteConfiguration>().Select(x => x.LiveRevisionId).SingleOrDefaultAsync();
        if (liveId.HasValue)
        {
            var originalVocabulary = await db.Set<SiteVocabulary>().AsNoTracking().Where(x => x.RevisionId == liveId).ToListAsync();
            foreach (var value in originalVocabulary)
                if (!revision.Vocabularies.Any(x => x.Id == value.Id && x.Kind == value.Kind)) throw new SiteConfigurationException("Giữ ID/loại danh mục phụ; ngừng chọn thay vì xóa hoặc đổi loại.");
            var protectedItems = await db.Set<SiteMenuItem>().AsNoTracking().Where(x => x.RevisionId == liveId && x.SystemProtected).ToListAsync();
            foreach (var item in protectedItems)
            {
                var candidate = revision.Menus.SingleOrDefault(x => x.Id == item.Id);
                if (candidate is null || !candidate.SystemProtected || candidate.Label != item.Label || candidate.TargetKind != item.TargetKind || candidate.RouteKey != item.RouteKey || candidate.CategoryId != item.CategoryId || candidate.ContentId != item.ContentId || candidate.ExternalUrl != item.ExternalUrl || candidate.Position != item.Position || candidate.ParentId != item.ParentId)
                    throw new SiteConfigurationException("Giữ đầy đủ mục điều hướng nguồn; mục hệ thống chỉ đổi thứ tự.");
            }
        }
        foreach (var item in revision.Menus)
        {
            Text(item.Label, 300, true);
            if (!MenuPositions.Contains(item.Position) || revision.Menus.Any(x => x != item && x.Id == item.Id)) throw new SiteConfigurationException("Vị trí hoặc ID menu không hợp lệ.");
            if (item.ParentId.HasValue && !revision.Menus.Any(x => x.Id == item.ParentId && x.Position == item.Position && x.TargetKind == "Group")) throw new SiteConfigurationException("Menu cha phải là nhóm cùng vị trí.");
            var visited = new HashSet<Guid> { item.Id }; var parent = item.ParentId;
            while (parent.HasValue) { if (!visited.Add(parent.Value) || visited.Count > 4) throw new SiteConfigurationException("Cây menu bị chu trình hoặc vượt ba cấp."); parent = revision.Menus.Single(x => x.Id == parent).ParentId; }
            await TargetAsync(item.TargetKind, item.CategoryId, item.ContentId, item.RouteKey, item.ExternalUrl, revision, publishing, true);
        }
        foreach (var item in revision.Banners)
        {
            Text(item.Label, 300, true); Text(item.Alt, 500, true);
            if (!BannerPositions.Contains(item.Position) || item.StartUtc.HasValue && item.EndUtc.HasValue && item.StartUtc >= item.EndUtc) throw new SiteConfigurationException("Vị trí/khoảng hiển thị banner không hợp lệ.");
            await TargetAsync(item.TargetKind, item.CategoryId, item.ContentId, item.RouteKey, item.ExternalUrl, revision, publishing, false);
        }
        foreach (var item in revision.Blocks)
        {
            Text(item.Title, 300, true);
            if (!BlockTypes.Contains(item.Type) || item.Limit is < 1 or > 12 || item.Type == "Category" && !revision.Categories.Any(x => x.CategoryId == item.CategoryId)) throw new SiteConfigurationException("Khối trang chủ cần đúng mẫu/số lượng/chuyên mục.");
            if (item.Type != "Category" && item.CategoryId.HasValue) throw new SiteConfigurationException("Chuyên mục chỉ dành cho khối Chuyên mục.");
        }
        foreach (var pin in revision.Pins)
        {
            var block = revision.Blocks.SingleOrDefault(x => x.Id == pin.BlockId) ?? throw new SiteConfigurationException("Khối ghim không tồn tại.");
            var content = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == pin.ContentId) ?? throw new SiteConfigurationException("Nội dung ghim không tồn tại.");
            if (!Accepts(block, content.Kind)) throw new SiteConfigurationException("Nội dung ghim không đúng loại của khối.");
            if (publishing && (!content.IsPublic || content.IsTrashed || !content.PublishedRevisionId.HasValue)) throw new SiteConfigurationException("Nội dung ghim cần đang công khai khi áp dụng.");
        }
        foreach (var value in revision.Vocabularies) { Text(value.Label, 300, true); if (!VocabularyKinds.Contains(value.Kind)) throw new SiteConfigurationException("Loại danh mục phụ không hợp lệ."); }
        if (revision.Vocabularies.GroupBy(x => (x.Kind, x.Label.Trim().ToUpperInvariant())).Any(x => x.Count() > 1)) throw new SiteConfigurationException("Danh mục phụ bị trùng tên.");
        var assetIds = revision.Banners.Select(x => x.MediaAssetId).Concat(revision.LogoAssetId.HasValue ? [revision.LogoAssetId.Value] : []).Distinct().ToArray();
        foreach (var id in assetIds)
        {
            var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (asset is null || !mediaOptions.CanServe(asset) || asset.MimeType is not ("image/jpeg" or "image/png" or "image/gif" or "image/webp")) throw new SiteConfigurationException("Ảnh banner/logo không đạt policy định dạng/quét/quyền.");
            if (!publishing && asset.UploadedByUserId != actor.UserId)
            {
                var linked = await db.ContentAssets.AnyAsync(a => a.MediaAssetId == id && db.Contents.Any(c => c.IsPublic && !c.IsTrashed && c.PublishedRevisionId == a.RevisionId))
                    || await db.Set<SiteConfigurationRevision>().AnyAsync(x => x.LogoAssetId == id) || await db.Set<SiteBanner>().AnyAsync(x => x.MediaAssetId == id);
                if (!linked) throw new SiteConfigurationException("Không được dùng tài nguyên riêng của người khác trong cấu hình.", 403);
            }
            var path = MediaService.ResolveMediaPath(storage, asset.StorageKey);
            if (!File.Exists(path) || new FileInfo(path).Length != asset.ByteLength) throw new SiteConfigurationException("Tệp banner/logo chưa có đủ bytes trong kho riêng.");
        }
    }

    public async Task DeleteCategoryAsync(SiteConfigurationRevision revision, int categoryId)
    {
        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(x => x.Id == categoryId);
        if (category?.SourceUrl.Length > 0 || await db.RevisionCategories.AnyAsync(x => x.CategoryId == categoryId) || await db.Contents.AnyAsync(x => x.PrimaryCategoryId == categoryId)) throw new SiteConfigurationException("Không xóa chuyên mục nguồn hoặc đang được nội dung/lịch sử dùng.");
        if (revision.Categories.Any(x => x.ParentCategoryId == categoryId) || revision.Menus.Any(x => x.CategoryId == categoryId) || revision.Blocks.Any(x => x.CategoryId == categoryId)) throw new SiteConfigurationException("Gỡ liên kết/khối và chuyển chuyên mục con trước khi bỏ mục bổ sung.");
        revision.Categories.RemoveAll(x => x.CategoryId == categoryId);
    }

    public static bool Accepts(SiteHomeBlock block, ContentKind kind) => block.Type switch
    {
        "Featured" or "Recent" or "Category" => kind == ContentKind.Article,
        "Announcements" => kind == ContentKind.Announcement, "LegalDocuments" => kind == ContentKind.LegalDocument,
        "DraftDocuments" => kind == ContentKind.DraftDocument, "Videos" => kind == ContentKind.Video, _ => false
    };
    public static bool ExternalUrlValid(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || url.Contains('\\') || url.Any(char.IsControl) || url.Length > 2000) return false;
        var path = Uri.UnescapeDataString(uri.AbsolutePath).Replace('\\', '/');
        return !new[] { "/admin", "/preview", "/private", "/snapshots", "/.local", "/.env" }.Any(prefix => path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
    }
    public static SiteConfigurationRevision Clone(SiteConfigurationRevision source, string actor, DateTimeOffset now) => new()
    {
        EditedByUserId = actor, CreatedUtc = now, OrganizationName = source.OrganizationName, FooterText = source.FooterText,
        ContactAddress = source.ContactAddress, ContactPhone = source.ContactPhone, ContactHotline = source.ContactHotline, ContactEmail = source.ContactEmail, LogoAssetId = source.LogoAssetId,
        Categories = source.Categories.Select(x => new SiteCategoryConfiguration { CategoryId = x.CategoryId, DisplayName = x.DisplayName, Slug = x.Slug, Description = x.Description, ParentCategoryId = x.ParentCategoryId, SortOrder = x.SortOrder, ShowInNavigation = x.ShowInNavigation }).ToList(),
        Menus = source.Menus.Select(x => new SiteMenuItem { Id = x.Id, ParentId = x.ParentId, Position = x.Position, Label = x.Label, TargetKind = x.TargetKind, CategoryId = x.CategoryId, ContentId = x.ContentId, RouteKey = x.RouteKey, ExternalUrl = x.ExternalUrl, OpenNewWindow = x.OpenNewWindow, SortOrder = x.SortOrder, SystemProtected = x.SystemProtected }).ToList(),
        Banners = source.Banners.Select(x => new SiteBanner { Id = x.Id, MediaAssetId = x.MediaAssetId, Label = x.Label, Alt = x.Alt, Position = x.Position, TargetKind = x.TargetKind, CategoryId = x.CategoryId, ContentId = x.ContentId, RouteKey = x.RouteKey, ExternalUrl = x.ExternalUrl, OpenNewWindow = x.OpenNewWindow, SortOrder = x.SortOrder, StartUtc = x.StartUtc, EndUtc = x.EndUtc }).ToList(),
        Blocks = source.Blocks.Select(x => new SiteHomeBlock { Id = x.Id, Type = x.Type, Title = x.Title, Limit = x.Limit, CategoryId = x.CategoryId, SortOrder = x.SortOrder, Enabled = x.Enabled }).ToList(),
        Pins = source.Pins.Select(x => new SiteHomePin { BlockId = x.BlockId, ContentId = x.ContentId, SortOrder = x.SortOrder }).ToList(),
        Vocabularies = source.Vocabularies.Select(x => new SiteVocabulary { Id = x.Id, Kind = x.Kind, Label = x.Label, SortOrder = x.SortOrder, Active = x.Active }).ToList()
    };
    public static string Hash(SiteConfigurationRevision r) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        r.OrganizationName, r.FooterText, r.ContactAddress, r.ContactPhone, r.ContactHotline, r.ContactEmail, r.LogoAssetId,
        Categories = r.Categories.OrderBy(x => x.CategoryId).Select(x => new { x.CategoryId, x.DisplayName, x.Slug, x.Description, x.ParentCategoryId, x.SortOrder, x.ShowInNavigation }),
        Menus = r.Menus.OrderBy(x => x.Id).Select(x => new { x.Id, x.ParentId, x.Position, x.Label, x.TargetKind, x.CategoryId, x.ContentId, x.RouteKey, x.ExternalUrl, x.OpenNewWindow, x.SortOrder, x.SystemProtected }),
        Banners = r.Banners.OrderBy(x => x.Id).Select(x => new { x.Id, x.MediaAssetId, x.Label, x.Alt, x.Position, x.TargetKind, x.CategoryId, x.ContentId, x.RouteKey, x.ExternalUrl, x.OpenNewWindow, x.SortOrder, x.StartUtc, x.EndUtc }),
        Blocks = r.Blocks.OrderBy(x => x.Id).Select(x => new { x.Id, x.Type, x.Title, x.Limit, x.CategoryId, x.SortOrder, x.Enabled }),
        Pins = r.Pins.OrderBy(x => x.BlockId).ThenBy(x => x.ContentId).Select(x => new { x.BlockId, x.ContentId, x.SortOrder }),
        Vocabularies = r.Vocabularies.OrderBy(x => x.Id).Select(x => new { x.Id, x.Kind, x.Label, x.SortOrder, x.Active })
    }))));
    private IQueryable<SiteConfigurationRevision> Revisions() => db.Set<SiteConfigurationRevision>().Include(x => x.Categories).Include(x => x.Menus).Include(x => x.Banners).Include(x => x.Blocks).Include(x => x.Pins).Include(x => x.Vocabularies).AsSplitQuery();
    private static void Attach(SiteConfigurationRevision r)
    {
        foreach (var x in r.Categories) x.RevisionId = r.Id;
        foreach (var x in r.Menus) x.RevisionId = r.Id;
        foreach (var x in r.Banners) x.RevisionId = r.Id;
        foreach (var x in r.Blocks) x.RevisionId = r.Id;
        foreach (var x in r.Pins) x.RevisionId = r.Id;
        foreach (var x in r.Vocabularies) x.RevisionId = r.Id;
    }
    private async Task TargetAsync(string kind, int? categoryId, Guid? contentId, string route, string external, SiteConfigurationRevision r, bool publishing, bool groupAllowed)
    {
        switch (kind)
        {
            case "Group" when groupAllowed: if (categoryId.HasValue || contentId.HasValue || route.Length > 0 || external.Length > 0) throw new SiteConfigurationException("Nhóm menu không có đích liên kết."); break;
            case "Category": if (!categoryId.HasValue || !r.Categories.Any(x => x.CategoryId == categoryId)) throw new SiteConfigurationException("Chọn chuyên mục đích hợp lệ."); break;
            case "Content":
                var content = contentId.HasValue ? await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contentId) : null;
                if (content is null || publishing && (!content.IsPublic || content.IsTrashed || !content.PublishedRevisionId.HasValue)) throw new SiteConfigurationException("Đích nội dung phải đang công khai khi áp dụng."); break;
            case "Route": if (!Routes.ContainsKey(route)) throw new SiteConfigurationException("Chọn mục hệ thống từ danh sách; không nhận tuyến admin/private."); break;
            case "External": if (!ExternalUrlValid(external)) throw new SiteConfigurationException("Liên kết ngoài phải là HTTP(S), không credential/backslash/control."); break;
            default: throw new SiteConfigurationException("Loại đích liên kết không hợp lệ.");
        }
        if (kind != "Category" && categoryId.HasValue || kind != "Content" && contentId.HasValue || kind != "Route" && route.Length > 0 || kind != "External" && external.Length > 0) throw new SiteConfigurationException("Các trường đích không khớp loại đã chọn.");
    }
    private static SiteMenuItem Menu(SiteConfigurationRevision r, string label, string kind, string route, int order, Guid? parent = null, string position = "main")
    { var item = new SiteMenuItem { Label = label, TargetKind = kind, RouteKey = route, SortOrder = order, ParentId = parent, Position = position, SystemProtected = true }; r.Menus.Add(item); return item; }
    private static void Text(string value, int maximum, bool required = false)
    { if (value.Length > maximum || required && string.IsNullOrWhiteSpace(value) || value.Any(x => char.IsControl(x) && x is not ('\r' or '\n' or '\t'))) throw new SiteConfigurationException("Trường cấu hình thiếu hoặc vượt giới hạn."); }
    private static void Require(ContentActor actor, string permission) { if (!actor.Has(permission)) throw new SiteConfigurationException("Thiếu quyền hiện thời: " + permission, 403); }
    private void Audit(string actor, string action, SiteConfigurationRevision revision, string detail) => db.AuditLogs.Add(new() { Actor = actor, Action = action, RevisionId = null, Detail = detail + "; siteRevision=" + revision.Id + "; hash=" + revision.ContentHash, CreatedUtc = Now });
}
