using LandPortal.Application;
using AngleSharp.Html.Parser;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PortalController(PortalDbContext db, PublicContentQuery query, PublicSiteService site,PublicUrlPolicy urls,DocumentUsage usage) : Controller
{
    [HttpGet("tin-tuc")]
    public Task<IActionResult> News(string? q, int page = 1) => List("Tin tức", query.Published().Where(x => x.Kind == ContentKind.Article), q, page);
    [HttpGet("thong-bao")]
    public Task<IActionResult> Announcements(string? q, int page = 1) => List("Thông báo", query.Published().Where(x => x.Kind == ContentKind.Announcement), q, page);
    [HttpGet("video")]
    public Task<IActionResult> Videos(string? q, int page = 1) => List("Video", query.Published().Where(x => x.Kind == ContentKind.Video), q, page);
    [HttpGet("lich-tuan/nguon")]
    public Task<IActionResult> WeeklySchedule(string? q, int page = 1) => List("Lịch tuần", query.Published().Where(x => db.RevisionCategories.Any(c => c.RevisionId == x.RevisionId && c.CategoryId == 76) || x.CategoryId == 76), q, page);
    [HttpGet("quy-hoach-su-dung-dat")]
    public Task<IActionResult> LandUsePlanning(string? q, int page = 1) => List("Quy hoạch sử dụng đất", query.Published().Where(x => db.RevisionCategories.Any(c => c.RevisionId == x.RevisionId && c.CategoryId == 77) || x.CategoryId == 77), q, page);
    [HttpGet("van-ban-chi-dao-dieu-hanh")]
    public IActionResult DirectiveDocuments() => View("Status", new StatusModel("Văn bản chỉ đạo điều hành", "Hạng mục được xác minh tại nguồn và giữ độc lập với Văn bản quy phạm pháp luật. Chưa có adapter hoặc dữ liệu loại này trong bản chạy Giai đoạn 1.", "https://qldd.mae.gov.vn/van-ban-chi-dao-dieu-hanh.htm", "Đối chiếu hạng mục nguồn"));
    [HttpGet("chuyen-muc/{slug}")]
    public async Task<IActionResult> Category(string slug, string? q, int page = 1)
    {
        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug);
        if (category is null) return NotFoundView();
        var live = await site.GetAsync();
        var configCategory = live?.Revision.Categories.FirstOrDefault(x=>x.CategoryId == category.Id);
        if (live is not null && configCategory is null) return NotFoundView();
        var rows = query.Published().Where(x => db.RevisionCategories.Any(c => c.RevisionId == x.RevisionId && c.CategoryId == category.Id) || x.CategoryId == category.Id);
        return await List(configCategory?.DisplayName ?? category.Name, rows, q, page, slug,configCategory?.Description);
    }
    private async Task<IActionResult> List(string title, IQueryable<PublicRow> rows, string? q, int page, string? slug = null,string? description=null)
    {
        if (q?.Length > 200) return BadRequest();
        rows = query.Filter(rows, q);
        page = Math.Clamp(page, 1, 10000);
        var total = await rows.CountAsync();
        var items = await query.CardsPageAsync(PublicContentQuery.Recent(rows),(page-1)*10,10);
        return View("List", new ListingModel { Title = title, Description=PublicContentQuery.Empty(description),CategorySlug = slug, Query = q, Page = page, Total = total, Items = items.Select(PublicContentQuery.Card).ToList() });
    }
    [HttpGet("bai-viet/{id:guid}/{slug?}")]
    public async Task<IActionResult> Article(Guid id, string? slug)
    {
        var row = await PublicContentQuery.ForDisplay(query.Published()).SingleOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFoundView();
        var revision = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == row.RevisionId);
        var files = await db.ContentAssets.AsNoTracking().Where(x => x.RevisionId == row.RevisionId && !x.MediaAsset.IsQuarantined && !x.MediaAsset.MimeType.StartsWith("image/"))
            .OrderBy(x => x.SortOrder).Select(x => new AttachmentModel(x.MediaAssetId, x.Caption == "" ? x.MediaAsset.OriginalName : x.Caption, x.MediaAsset.MimeType, x.MediaAsset.ByteLength)).ToListAsync();
        var related = await query.CardsPageAsync(PublicContentQuery.Recent(query.Published().Where(x => x.Id != id && x.Kind == row.Kind && (row.CategoryId == null || x.CategoryId == row.CategoryId))),0,4);
        var source = await db.SourceRecords.AsNoTracking().Where(x => x.ContentId == id).Select(x => x.CanonicalUrl).FirstOrDefaultAsync();
        var legal = revision.Metadata;
        var isDocument=row.Kind is ContentKind.LegalDocument or ContentKind.DraftDocument;
        var inlinePdf=isDocument?await db.ContentAssets.Where(x=>x.RevisionId==row.RevisionId&&x.MediaAsset.MimeType=="application/pdf"&&x.MediaAsset.ScanStatus=="Clean"&&!x.MediaAsset.IsQuarantined).OrderBy(x=>x.SortOrder).Select(x=>(Guid?)x.MediaAssetId).FirstOrDefaultAsync():null;
        var counts=(Views:0L,Downloads:0L);
        if(isDocument){await usage.ViewAsync(row.Id,row.RevisionId);counts=await usage.CountsAsync(row.Id);}
        var coverCaption=revision.CoverAssetId is Guid coverId?await db.ContentAssets.Where(x=>x.RevisionId==row.RevisionId&&x.MediaAssetId==coverId).Select(x=>x.Caption).FirstOrDefaultAsync():null;
        ViewData["CanonicalPath"]="/bai-viet/"+id;
        ViewData["OpenGraphImage"]=revision.CoverAssetId is Guid cover?"/media/"+cover:null;
        var inlineCover = revision.CoverAssetId is not null && new HtmlParser().ParseDocument(revision.BodyHtml).QuerySelectorAll("img[src]").Any(x => string.Equals(x.GetAttribute("src"), "/media/" + revision.CoverAssetId, StringComparison.OrdinalIgnoreCase));
        return View(new ArticleModel { CoverCaption=coverCaption, InlinePdfId=inlinePdf, DocumentViews=counts.Views, DocumentDownloads=counts.Downloads, Metadata = revision.Metadata, IsDraftDocument = row.Kind == ContentKind.DraftDocument, ConsultationState = row.Kind == ContentKind.DraftDocument ? revision.Metadata.ConsultationState(DateTimeOffset.UtcNow) : null, Content = PublicContentQuery.Card(row), BodyHtml = revision.BodyHtml, CoverUrl = revision.CoverAssetId is not null && !inlineCover ? $"/media/{revision.CoverAssetId}" : null, Author = PublicContentQuery.Empty(revision.Author), SourceName = PublicContentQuery.Empty(revision.SourceCredit), SourceUrl = source, Attachments = files, Related = related.Select(PublicContentQuery.Card).ToList(), DocumentNumber = PublicContentQuery.Empty(legal?.Number), IssuingAuthority = PublicContentQuery.Empty(legal?.IssuingBody), IssuedDate = legal?.IssuedDate?.ToString("dd/MM/yyyy"), IsVideo = row.Kind == ContentKind.Video, VideoFileId = files.FirstOrDefault(x => x.ContentType == "video/mp4")?.Id });
    }
    [HttpGet("van-ban")]
    public Task<IActionResult> Documents(string? q, string? agency, string? type, int page = 1, string? field = null, [VietnameseFilterDate] DateOnly? issuedFrom = null, [VietnameseFilterDate] DateOnly? issuedTo = null, [VietnameseFilterDate] DateOnly? from = null, [VietnameseFilterDate] DateOnly? to = null, string sort = "relevant",string? signer=null,string? effectiveness=null) => DocumentList(false,q,agency,type,page,field,issuedFrom,issuedTo,from,to,sort,signer,effectiveness);
    [HttpGet("du-thao")]
    public Task<IActionResult> Drafts(string? q, string? agency, string? type, int page = 1, string? field = null, [VietnameseFilterDate] DateOnly? issuedFrom = null, [VietnameseFilterDate] DateOnly? issuedTo = null, [VietnameseFilterDate] DateOnly? from = null, [VietnameseFilterDate] DateOnly? to = null, string sort = "relevant",string? signer=null,string? effectiveness=null) => DocumentList(true,q,agency,type,page,field,issuedFrom,issuedTo,from,to,sort,signer,effectiveness);
    private async Task<IActionResult> DocumentList(bool draft,string? q,string? agency,string? type,int page,string? field,DateOnly? issuedFrom,DateOnly? issuedTo,DateOnly? from,DateOnly? to,string sort,string? signer,string? effectiveness)
    {
        if (!ModelState.IsValid || q?.Length>200 || agency?.Length>500 || type?.Length>300 || field?.Length>300 || signer?.Length>300 || issuedFrom>issuedTo || from>to || sort is not ("relevant" or "recent" or "oldest" or "title") || effectiveness is not (null or "" or "Unknown" or "ConfirmedEffective" or "ConfirmedExpired" or "ConfirmedPartial")) return InvalidFilter();
        var rows = DateFilter(query.Filter(query.Published().Where(x=>x.Kind == (draft ? ContentKind.DraftDocument : ContentKind.LegalDocument)),q),from,to);
        if (!string.IsNullOrWhiteSpace(agency)) { var term=TextNormalizer.Search(agency); rows=rows.Where(x=>x.AuthoritySearch.Contains(term)); }
        if (!string.IsNullOrWhiteSpace(type)) { var term=TextNormalizer.Search(type); rows=rows.Where(x=>x.TypeSearch.Contains(term)); }
        if (!string.IsNullOrWhiteSpace(field)) { var term=TextNormalizer.Search(field); rows=rows.Where(x=>x.FieldSearch.Contains(term)); }
        if (issuedFrom is not null) rows=rows.Where(x=>x.IssuedDate>=issuedFrom);
        if (issuedTo is not null) rows=rows.Where(x=>x.IssuedDate<=issuedTo);
        if(!string.IsNullOrWhiteSpace(signer))rows=rows.Where(x=>x.Signer.Contains(signer.Trim()));
        if(effectiveness=="Unknown")rows=rows.Where(x=>x.EffectivenessStatus==null||x.EffectivenessStatus=="Unknown");
        else if(!string.IsNullOrEmpty(effectiveness))rows=rows.Where(x=>x.EffectivenessStatus==effectiveness);
        page=Math.Clamp(page,1,10000); var total=await rows.CountAsync();
        var ids=await PublicContentQuery.PageRevisionIdsAsync(Ordered(rows,sort,q),(page-1)*10,10);
        var loaded=await query.DocumentRows(ids).Select(x=>new { Row=x,FileId=db.ContentAssets.Where(a=>a.RevisionId == x.RevisionId && a.MediaAsset.MimeType == "application/pdf" && !a.MediaAsset.IsQuarantined).OrderBy(a=>a.SortOrder).Select(a=>(Guid?)a.MediaAssetId).FirstOrDefault() }).ToListAsync();
        var items=loaded.OrderBy(x=>Array.IndexOf(ids,x.Row.RevisionId)).ToList();
        return View("Documents",new DocumentListModel { Title=draft ? "Văn bản dự thảo" : "Văn bản quy phạm pháp luật",IsDraft=draft,Query=q,Agency=agency,Type=type,Field=field,Signer=signer,Effectiveness=effectiveness,IssuedFrom=issuedFrom,IssuedTo=issuedTo,From=from,To=to,Sort=sort,Page=page,Total=total,Items=items.Select(x=>new DocumentRow(PublicContentQuery.Card(x.Row),PublicContentQuery.Empty(x.Row.Number),PublicContentQuery.Empty(x.Row.Authority),PublicContentQuery.Empty(x.Row.DocumentType),x.Row.IssuedDate?.ToString("dd/MM/yyyy"),x.FileId,EffectivenessLabel(x.Row.EffectivenessStatus))).ToList() });
    }
    [HttpGet("tim-kiem")]
    public async Task<IActionResult> Search(string? q,string? kind,int page=1,int? categoryId=null,DateOnly? from=null,DateOnly? to=null,string sort="recent")
    {
        if (!ModelState.IsValid || q?.Length>200 || kind?.Length>30 || from>to || sort is not ("relevant" or "recent" or "oldest" or "title")) return InvalidFilter();
        var search = new GeneralSearchQuery(db, query);
        if (!string.IsNullOrWhiteSpace(kind) && kind != "Inquiry" && (!Enum.TryParse<ContentKind>(kind,out var parsed) || !Enum.IsDefined(parsed))) return BadRequest();
        var rows=search.Rows(q,kind,categoryId,from,to);
        page=Math.Clamp(page,1,10000); var total=await rows.CountAsync();
        var items=await search.PageAsync(rows,sort,q,page);
        return View(new ListingModel { Title="Tìm kiếm",Query=q,Kind=kind,CategoryId=categoryId,From=from,To=to,Sort=sort,Categories=await db.Categories.AsNoTracking().OrderBy(x=>x.SortOrder).Select(x=>new EditorCategory(x.Id,x.Name)).ToListAsync(),Page=page,Total=total,SearchItems=items });
    }
    [HttpGet("hoi-dap/cong-khai/{id:guid}")]
    public async Task<IActionResult> PublicAnswer(Guid id)
    {
        var row = await (from s in db.InteractionSubmissions.AsNoTracking() join p in db.InteractionPublicSnapshots.AsNoTracking() on s.PublicSnapshotId equals p.Id
                         where p.Id == id && p.SubmissionId == s.Id && s.Kind == InteractionKind.Inquiry && s.PublicConsent && s.State != InteractionState.Rejected
                         select new PublicInteraction(p.DisplayName,p.Text,p.AgencyAnswer,p.ApprovedUtc)).SingleOrDefaultAsync();
        return row == null ? NotFoundView() : View("PublicAnswer",row);
    }
    private static IQueryable<PublicRow> DateFilter(IQueryable<PublicRow> rows,DateOnly? from,DateOnly? to)
    {
        if(from is not null) rows=rows.Where(x=>(x.SourceDate ?? (x.PublishedUtc == null ? (DateOnly?)null : DateOnly.FromDateTime(x.PublishedUtc.Value.AddHours(7).Date)))>=from);
        if(to is not null) rows=rows.Where(x=>(x.SourceDate ?? (x.PublishedUtc == null ? (DateOnly?)null : DateOnly.FromDateTime(x.PublishedUtc.Value.AddHours(7).Date)))<=to);
        return rows;
    }
    private IActionResult InvalidFilter(){Response.StatusCode=400;return View("Status",new StatusModel("Bộ lọc chưa hợp lệ","Nhập ngày theo dd/MM/yyyy (ví dụ 23/10/2026), kiểm tra ngày có thật và Từ ngày không sau Đến ngày. Kiểm tra lựa chọn sắp xếp. Quay lại để sửa bộ lọc; chưa có thay đổi dữ liệu."));}
    public static string EffectivenessLabel(string? value)=>value switch {"ConfirmedEffective"=>"Đã xác nhận còn hiệu lực","ConfirmedExpired"=>"Đã xác nhận hết hiệu lực","ConfirmedPartial"=>"Đã xác nhận hiệu lực một phần",_=>"Chưa xác định"};
    private static IOrderedQueryable<PublicRow> Ordered(IQueryable<PublicRow> rows,string sort,string? q=null) => sort switch {
        "relevant"=>PublicContentQuery.Relevant(rows,q),
        "title"=>rows.OrderBy(x=>x.Title).ThenBy(x=>x.Id),
        "oldest"=>rows.OrderBy(x=>x.SourceDate ?? (x.PublishedUtc == null ? (DateOnly?)null : DateOnly.FromDateTime(x.PublishedUtc.Value.AddHours(7).Date))).ThenBy(x=>x.Id),
        _=>PublicContentQuery.Recent(rows)
    };
    [HttpGet("gioi-thieu/{slug}")]
    public async Task<IActionResult> Introduction(string slug)
    {
        var names = new Dictionary<string, string> { ["lanh-dao-cuc"] = "Lãnh đạo cục", ["chuc-nang-nhiem-vu"] = "Chức năng nhiệm vụ", ["co-cau-to-chuc"] = "Cơ cấu tổ chức" };
        if (!names.TryGetValue(slug, out var name)) return NotFoundView();
        var row = await query.Published().Where(x => x.Kind == ContentKind.StaticPage && x.Title.Contains(name)).FirstOrDefaultAsync();
        if (row is not null) return await Article(row.Id, PublicContentQuery.Slug(row.Title));
        return View("Status", new StatusModel(name, "Nội dung giới thiệu chưa được nhập hoặc chưa tiếp cận được trong tập mẫu. Không tự bổ sung thông tin tổ chức khi chưa có bằng chứng nguồn."));
    }
    [HttpGet("hoi-dap")]
    public IActionResult Questions() => Redirect("/tuong-tac/hoi-dap-da-duyet");
    [HttpGet("lien-he")]
    public async Task<IActionResult> Contact()
    {
        var live=await site.GetAsync();
        var contact=live?.Revision;
        var details=contact is null ? "Thông tin cơ quan đã xác minh được giữ tại footer." : $"{contact.OrganizationName}. Địa chỉ: {contact.ContactAddress}. Điện thoại: {contact.ContactPhone}. Đường dây nóng: {contact.ContactHotline}. Email công khai: {contact.ContactEmail}.";
        return Redirect("/tuong-tac/Contact");
    }
    [HttpGet("so-tay")]
    public Task<IActionResult> Handbook(string? q, int page = 1) => List("Sổ tay điện tử đất đai", query.Published().Where(x => db.RevisionCategories.Any(c => c.RevisionId == x.RevisionId && c.CategoryId == 79) || x.CategoryId == 79), q, page);
    [Route("loi"), IgnoreAntiforgeryToken]
    public IActionResult Error() { Response.StatusCode = 500; return View(); }
    [HttpGet("{**legacyPath}", Order = 100)]
    public async Task<IActionResult> Legacy(string? legacyPath)
    {
        if (legacyPath is null || !(legacyPath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) || legacyPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || legacyPath.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))) return NotFoundView();
        var origin=urls.LegacyOrigin(Request.Host.Host);
        var secure="https://"+origin+Request.Path.Value+Request.QueryString.Value;
        var plain="http://"+origin+Request.Path.Value+Request.QueryString.Value;
        var secureHash=TextNormalizer.Hash(secure);var plainHash=TextNormalizer.Hash(plain);
        var ids=await db.SourceUrlAliases.AsNoTracking().Where(x => x.UrlHash==secureHash&&x.Url==secure || x.UrlHash==plainHash&&x.Url==plain).Select(x=>x.ContentId).Distinct().ToListAsync();
        // A protocol alias collision is a review item, never authority to select another article.
        if(ids.Count!=1)return NotFoundView();var id=ids[0];
        var row = await PublicContentQuery.ForDisplay(query.Published()).SingleOrDefaultAsync(x => x.Id == id);
        return row is null ? NotFoundView() : RedirectPermanent(PublicContentQuery.Card(row).Url);
    }
    private IActionResult NotFoundView() { Response.StatusCode = 404; return View("Status", new StatusModel("Không tìm thấy nội dung", "Nội dung không tồn tại hoặc chưa được phép công khai. Vui lòng chọn một chuyên mục khác hoặc tìm kiếm lại.")); }
}
