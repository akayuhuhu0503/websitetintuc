using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

[Authorize(Policy = "EditorAccess")]
public sealed class AdminController(PortalDbContext db, SignInManager<PortalUser> signIn, WorkflowService workflow, HtmlPolicy htmlPolicy, CurrentPermissionService permissions, PublicCaptchaService captcha, StorageOptions storage, IWebHostEnvironment environment) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    private Task<ContentActor> Actor() => permissions.GetAsync(UserId);
    [AllowAnonymous, HttpGet("admin/login")]
    public async Task<IActionResult> Login(string? returnUrl) => User.Identity?.IsAuthenticated == true ? Redirect("/admin") : await LoginView(new LoginModel { ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : "/admin" });
    [AllowAnonymous, HttpPost("admin/login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginModel model)
    {
        try
        {
            await captcha.ConsumeAsync(model.CaptchaId, model.CaptchaAnswer, InteractionBrowserSession.Get(HttpContext), LoginAddress(), "AdminLogin");
        }
        catch (PublicCaptchaException error)
        {
            ModelState.AddModelError("", error.Message);
            return await LoginView(model, error.StatusCode);
        }
        if (!ModelState.IsValid) { ModelState.AddModelError("", "Nhập tên đăng nhập và mật khẩu hợp lệ, rồi trả lời câu hỏi mới."); return await LoginView(model, 400); }
        var loginAccount = await signIn.UserManager.FindByNameAsync(model.UserName);
        var result = loginAccount is null || loginAccount.IsDisabled ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await signIn.PasswordSignInAsync(loginAccount, model.Password, isPersistent: false, lockoutOnFailure: true);
        if(loginAccount!=null&&!DevDemoPolicy.Allowed(await signIn.UserManager.GetClaimsAsync(loginAccount),environment.EnvironmentName,db.Database.GetDbConnection().Database))
        {await signIn.SignOutAsync();result=Microsoft.AspNetCore.Identity.SignInResult.Failed;}
        // Never log credentials, CAPTCHA answers, email/IP or the submitted name.
        db.AuditLogs.Add(new(){Actor=loginAccount?.Id??"anonymous",Action=result.Succeeded?"AdminPasswordLogin":result.IsLockedOut?"AdminPasswordLocked":"AdminPasswordFailed",Detail="Identity password outcome; no credentials recorded"});
        try { await db.SaveChangesAsync(); }
        catch { if(result.Succeeded)await signIn.SignOutAsync();throw; }
        if (result.Succeeded) {
            if(loginAccount!.MustChangePassword)return Redirect("/admin/account/change-password");
            var target=Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/admin";
            if((target.StartsWith("/admin/account/mfa",StringComparison.OrdinalIgnoreCase)||target.StartsWith("/admin/mfa",StringComparison.OrdinalIgnoreCase)))target="/admin";
            if(target == "/admin") { var account=await signIn.UserManager.FindByNameAsync(model.UserName);var current=await permissions.GetAsync(account!.Id);if(!current.Has(Permissions.Create)&&!current.Has(Permissions.ViewOwn)&&!current.Has(Permissions.ViewAll)&&!current.Has(Permissions.Review)&&!current.Has(Permissions.PublishApproved)) target=current.Has(Permissions.ManageUsers)?"/admin/users":current.Has(Permissions.ManageRoles)?"/admin/roles":current.Has(Permissions.ManageSite)||current.Has(Permissions.PublishSite)||current.Has(Permissions.ViewSite)?"/admin/site":"/admin/account/change-password"; }
            return LocalRedirect(target);
        }
        ModelState.AddModelError("", result.IsLockedOut ? "Tài khoản tạm khóa do nhiều lần đăng nhập thất bại. Vui lòng thử lại sau." : "Tên đăng nhập hoặc mật khẩu không hợp lệ.");
        return await LoginView(model);
    }
    private string LoginAddress() => "admin-login:" + (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    private async Task<IActionResult> LoginView(LoginModel model, int status = 200)
    {
        Response.Headers.CacheControl = "no-store, private";
        // Never redisplay credentials or the answer from the previous one-use challenge.
        model.Password = ""; model.CaptchaAnswer = "";
        ModelState.Remove(nameof(model.Password)); ModelState.Remove(nameof(model.CaptchaId)); ModelState.Remove(nameof(model.CaptchaAnswer));
        try
        {
            var prompt = await captcha.IssueAsync(InteractionBrowserSession.Get(HttpContext), LoginAddress(), "AdminLogin");
            model.CaptchaId = prompt.Id; model.CaptchaQuestion = prompt.Question;
        }
        catch (PublicCaptchaException error) { model.CaptchaId = Guid.Empty; model.CaptchaQuestion = ""; ModelState.AddModelError("", error.Message); status = error.StatusCode; }
        Response.StatusCode = status;
        return View("Login", model);
    }
    [HttpPost("admin/logout")]
    public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return Redirect("/admin/login"); }
    [AllowAnonymous, HttpGet("admin/access-denied")]
    public IActionResult AccessDenied() { Response.StatusCode = 403; return View("~/Views/Portal/Status.cshtml", new StatusModel("Không có quyền truy cập", "Tài khoản của bạn không có quyền thực hiện thao tác này. Liên hệ quản trị viên để được phân quyền phù hợp.")); }

    [HttpGet("admin")]
    public async Task<IActionResult> Index(string? q, string? status, int? categoryId, string? owner, int page = 1, string? kind = null)
    {
        if (q?.Length > 200 || owner?.Length > 450) return BadRequest();
        var actor = await Actor();
        var contents = ReadScope(actor);
        if (!string.IsNullOrWhiteSpace(kind)) { if (!Enum.TryParse<ContentKind>(kind, out var k) || !Enum.IsDefined(k)) return BadRequest(); contents = contents.Where(x => x.Kind == k); }
        var rows = from c in contents
                   join revision in db.Revisions.AsNoTracking() on c.CurrentRevisionId equals (Guid?)revision.Id into revisions
                   from revision in revisions.DefaultIfEmpty()
                   join draft in db.ContentDrafts.AsNoTracking() on c.Id equals draft.ContentId into drafts
                   from draft in drafts.DefaultIfEmpty()
                   select new AdminQueryRow { Id = c.Id, Title = draft == null ? (revision == null ? "(Bản nháp chưa đặt tiêu đề)" : revision.Title) : draft.Title, CategoryId = draft == null ? (revision == null ? null : revision.PrimaryCategoryId) : draft.PrimaryCategoryId, OwnerId = c.OwnerUserId, State = c.State, IsPublic = c.IsPublic, IsTrashed = c.IsTrashed, UpdatedUtc = draft == null ? (revision == null ? c.CreatedUtc : revision.CreatedUtc) : draft.UpdatedUtc };
        if (!string.IsNullOrWhiteSpace(q)) { var title = q.Trim(); rows = rows.Where(x => EF.Functions.Collate(x.Title, "Vietnamese_100_CI_AI").Contains(title)); }
        if (!string.IsNullOrWhiteSpace(status)) { if (!Enum.TryParse<WorkflowState>(status, out var state) || !Enum.IsDefined(state)) return BadRequest(); rows = rows.Where(x => x.State == state); }
        if (categoryId is not null) rows = rows.Where(x => x.CategoryId == categoryId || db.DraftCategories.Any(c => c.ContentId == x.Id && c.CategoryId == categoryId));
        if (!string.IsNullOrWhiteSpace(owner)) rows = rows.Where(x => x.OwnerId == owner);
        page = Math.Clamp(page, 1, 10000);
        var total = await rows.CountAsync();
        // Sort/page identifiers and ordering keys only. Materialize titles after
        // SQL paging, preserving both the permission scope and deterministic order.
        var ids = await rows.OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Skip((page - 1) * 15).Take(15).Select(x=>x.Id).ToArrayAsync();
        var byId = await rows.Where(x=>ids.Contains(x.Id)).ToDictionaryAsync(x=>x.Id);
        var items = ids.Where(byId.ContainsKey).Select(id=>byId[id]).ToList();
        var categories = await Categories(actor);
        var owners = await (from u in db.Users.AsNoTracking() where contents.Any(c => c.OwnerUserId == u.Id) orderby u.UserName select new AdminOwner(u.Id, u.UserName ?? u.Id)).ToListAsync();
        return View(new AdminListModel { Kind = kind, Query = q, Status = status, CategoryId = categoryId, Owner = owner, Page = page, Total = total, Categories = categories, Owners = owners, CanCreate = actor.Has(Permissions.Create), Items = items.Select(x => new AdminRow(x.Id, x.Title, StateLabel(x.State), categories.FirstOrDefault(c => c.Id == x.CategoryId)?.Name, x.UpdatedUtc.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm"), x.IsPublic && !x.IsTrashed, x.IsTrashed, owners.FirstOrDefault(o => o.Id == x.OwnerId)?.Name ?? "Dữ liệu nguồn")).ToList() });
    }
    private IQueryable<Content> ReadScope(ContentActor actor)
    {
        var rows = permissions.Scope(db.Contents.AsNoTracking(),actor);
        if (actor.Has(Permissions.ViewAll) || actor.Has(Permissions.EditAll)) return rows;
        return rows.Where(c => (c.OwnerUserId == actor.UserId && (actor.Has(Permissions.ViewOwn) || actor.Has(Permissions.EditOwn) || actor.Has(Permissions.Create))) || (actor.Has(Permissions.Review) && c.SubmittedRevisionId != null) || (actor.Has(Permissions.PublishApproved) && c.ApprovedRevisionId != null));
    }
    [HttpGet("admin/create")]
    public async Task<IActionResult> Create(ContentKind kind = ContentKind.Article,string? template=null)
    {
        var actor = await Actor(); if (!actor.Has(Permissions.Create)) return Forbid();
        if (!Enum.IsDefined(kind)) return BadRequest();
        if(kind==ContentKind.Video&&!actor.Has(Permissions.ManageVideos))return Forbid();
        var model=new EditorModel { Kind = kind };
        if(template=="weekly"){if(kind!=ContentKind.StaticPage)return BadRequest();var date=DateTime.UtcNow.AddHours(7);model.Metadata.WeeklyYear=System.Globalization.ISOWeek.GetYear(date);model.Metadata.WeeklyWeek=System.Globalization.ISOWeek.GetWeekOfYear(date);model.Metadata.WeeklyRowsJson="[]";model.CategoryId=76;model.Title="Lịch tuần "+model.Metadata.WeeklyWeek+"/"+model.Metadata.WeeklyYear;}
        return View("Edit", await Populate(model, actor));
    }
    [HttpGet("admin/edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, Guid? documentId)
    {
        var actor = await Actor(); var content = await Find(id);
        if (content is null) return NotFound(); if (!await permissions.CanReadAsync(actor, content)) return Forbid();
        var model = await LoadEditor(content);
        if (documentId != null)
        {
            if (!await permissions.CanEditAsync(actor, content) || content.State is not (WorkflowState.Draft or WorkflowState.Rejected)) return Forbid();
            var document = await db.EditorialDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.ContentId == id && x.MediaAssetId == documentId);
            var asset = document == null ? null : await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId);
            if (asset == null) return NotFound();
            await using var original = await EditorialDocumentService.OpenVerifiedAsync(db, storage, asset, environment.IsDevelopment());
            if (original == null) return StatusCode(423, "Chưa có kết luận Clean đúng byte; không đọc bản xem trước.");
            model.AttachmentIds = model.AttachmentIds.Append(asset.Id).Distinct().ToArray();
            if (asset.MimeType == OfficeDocumentPolicy.Docx)
            {
                if (model.Metadata.WeeklyYear is not int year || model.Metadata.WeeklyWeek is not int week) return BadRequest("Tệp Word chỉ nhập vào mẫu lịch tuần có năm/tuần hợp lệ.");
                model.Metadata.WeeklyOriginalAssetId = asset.Id; model.Metadata.WeeklyOriginalReviewEvidence = null;
                try
                {
                    var preview = WeeklyDocxReader.Parse(original, asset.OriginalName, year, week);
                    model.Metadata.WeeklyRowsJson = System.Text.Json.JsonSerializer.Serialize(preview.Rows);
                    model.DocumentWarnings = preview.Warnings.ToList();
                }
                catch (InvalidOperationException error) { model.DocumentWarnings = [error.Message, "Tệp gốc Clean vẫn được giữ; nhập hoặc sửa các dòng bên dưới. Chưa lưu nháp, chưa thay lịch công khai."]; }
            }
            else model.DocumentWarnings = ["Tài liệu Clean được chọn trong bản xem trước; bấm Lưu bản nháp để tạo phiên bản. Chưa cấp quyền công khai."];
        }
        return View(await Populate(model, actor, content));
    }
    [HttpPost("admin/save")]
    public async Task<IActionResult> Save(EditorModel model, string intent = "Save", bool autosave = false, string? format = null)
    {
        var json = format == "json";
        if (intent is not ("Save" or "Submit" or "Publish") || (autosave && intent != "Save")) return Error(400, "Thao tác lưu không hợp lệ.", json);
        var actor = await Actor();
        if (intent == "Submit" && !actor.Has(Permissions.Submit) || intent == "Publish" && !actor.Has(Permissions.PublishDirect)) return Error(403, "Bạn không có quyền thực hiện thao tác này.", json);
        Content? content = null;
        if (model.Id is not null)
        {
            content = await Find(model.Id.Value);
            if (content is null) return NotFound();
            if (!await permissions.CanEditAsync(actor, content)) return Error(403, "Bạn không có quyền sửa bài này.", json);
        }
        else if (!actor.Has(Permissions.Create)) return Error(403, "Bạn không có quyền tạo bài.", json);
        // Migrated articles can retain a missing category. New articles use the template requirement.
        if (content is not null && model.CategoryId is null && await db.SourceRecords.AnyAsync(x => x.ContentId == content.Id) && content.PrimaryCategoryId is null) ModelState.Remove(nameof(EditorModel.CategoryId));
        var version = Version(model.RowVersion, model.Id is not null);
        var metadata=model.Metadata;
        foreach(var field in new (string Name,string? Value,int Limit)[] { (nameof(metadata.Number),metadata.Number,200),(nameof(metadata.DocumentType),metadata.DocumentType,300),(nameof(metadata.IssuingBody),metadata.IssuingBody,500),(nameof(metadata.Signer),metadata.Signer,300),(nameof(metadata.Field),metadata.Field,300),(nameof(metadata.Unit),metadata.Unit,500),(nameof(metadata.EffectivenessEvidence),metadata.EffectivenessEvidence,2000),(nameof(metadata.ConsultationDateEvidence),metadata.ConsultationDateEvidence,2000) })
            if(field.Value?.Length>field.Limit) ModelState.AddModelError("Metadata."+field.Name,"Trường này tối đa "+field.Limit+" ký tự.");
        if(metadata.ConsultationStartDate>metadata.ConsultationEndDate) ModelState.AddModelError("Metadata.ConsultationEndDate","Ngày kết thúc phải không trước ngày bắt đầu.");
        if((metadata.EffectiveDate is not null || metadata.EffectivenessStatus is "ConfirmedEffective" or "ConfirmedExpired" or "ConfirmedPartial") && string.IsNullOrWhiteSpace(metadata.EffectivenessEvidence)) ModelState.AddModelError("Metadata.EffectivenessEvidence","Nhập căn cứ xác nhận hoặc để hiệu lực chưa xác định.");
        if (!ModelState.IsValid)
        {
            if (json) return BadRequest(new { message = "Thông tin bài viết chưa hợp lệ.", errors = ModelState.Values.SelectMany(v => v.Errors).Select(x => x.ErrorMessage).ToArray() });
            return View("Edit", await Populate(model, actor, content));
        }
        try
        {
            var input = Input(model); input.Metadata.MetadataOrigin = "Editorial";
            var result = await workflow.SaveAsync(model.Id, input, actor, version, model.RequestId, autosave);
            if (intent == "Submit") result = await workflow.SubmitAsync(result.ContentId, result.CurrentRevisionId, actor, result.RowVersion, FollowupCommand(model.RequestId, "Submit"));
            if (intent == "Publish") result = await workflow.PublishDirectAsync(result.ContentId, result.CurrentRevisionId ?? throw new InvalidOperationException("Chưa có phiên bản để xuất bản."), actor, result.RowVersion, FollowupCommand(model.RequestId, "PublishDirect"));
            if (json) return Json(Result(result));
            TempData["Success"] = intent switch { "Submit" => "Đã lưu và gửi đúng phiên bản đi duyệt; bản đang công khai được giữ nguyên.", "Publish" => "Đã xuất bản phiên bản vừa lưu theo quyền đăng trực tiếp.", _ => "Đã lưu bản nháp. Phiên bản đang công khai được giữ nguyên." };
            return Redirect($"/admin/edit/{result.ContentId}");
        }
        catch (DbUpdateConcurrencyException) { return await SaveError(409, "Bài viết đã thay đổi hoặc đã chuyển trạng thái ở phiên khác. Nội dung bạn nhập vẫn được giữ; tải lại và đối chiếu trước khi lưu tiếp."); }
        catch (UnauthorizedAccessException) { return Error(403, "Quyền thực hiện thao tác đã bị từ chối. Nội dung chưa được lưu.", json); }
        catch (InvalidOperationException ex) { return await SaveError(400, ex.Message); }
        async Task<IActionResult> SaveError(int status, string message)
        {
            if (json) return Error(status, message, true);
            Response.StatusCode = status; ModelState.AddModelError("", message); return View("Edit", await Populate(model, actor, content));
        }
    }
    [HttpPost("admin/workflow/{id:guid}")]
    public async Task<IActionResult> ChangeWorkflow(Guid id, WorkflowModel model)
    {
        var actor = await Actor(); var content = await Find(id);
        if (content is null) return NotFound(); if (!await permissions.CanReadAsync(actor, content)) return Error(403, "Bạn không có quyền xem bài này.", true);
        var version = Version(model.RowVersion, true);
        if (!ModelState.IsValid || model.RequestId == Guid.Empty) return Error(400, "Thiếu phiên bản chống ghi đè hoặc mã thao tác.", true);
        try
        {
            WorkflowResult result = model.Action switch
            {
                "StartDraft" or "BeginEdit" => await workflow.BeginEditAsync(id, actor, version!, model.RequestId),
                "Submit" => await workflow.SubmitAsync(id, model.RevisionId, actor, version!, model.RequestId),
                "Approve" => await workflow.ApproveAsync(id, RequireRevision(), actor, version!, model.RequestId),
                "Reject" => await workflow.RejectAsync(id, RequireRevision(), model.Reason ?? "", actor, version!, model.RequestId),
                "PublishApproved" => await workflow.PublishApprovedAsync(id, RequireRevision(), actor, version!, model.RequestId),
                "PublishDirect" => await workflow.PublishDirectAsync(id, RequireRevision(), actor, version!, model.RequestId),
                "Unpublish" => await workflow.UnpublishAsync(id, actor, version!, model.RequestId),
                "Trash" => await workflow.TrashAsync(id, actor, version!, model.RequestId),
                "RestoreTrash" => await workflow.RestoreTrashAsync(id, actor, version!, model.RequestId),
                "RestoreRevision" => await workflow.RestoreRevisionAsync(id, RequireRevision(), actor, version!, model.RequestId),
                "Clone" => await workflow.CloneAsync(id, model.RevisionId, actor, model.RequestId),
                _ => throw new InvalidOperationException("Thao tác quy trình không hợp lệ.")
            };
            return Json(Result(result));
        }
        catch (DbUpdateConcurrencyException) { return Error(409, "Bài hoặc phiên bản đã thay đổi. Tải lại và đối chiếu trước khi thực hiện thao tác; không tự ghi đè.", true); }
        catch (UnauthorizedAccessException) { return Error(403, "Bạn không có quyền thực hiện thao tác này.", true); }
        catch (InvalidOperationException ex) { return Error(400, ex.Message, true); }
        Guid RequireRevision() => model.RevisionId ?? throw new InvalidOperationException("Thiếu định danh phiên bản cần xử lý.");
    }
    [HttpGet("admin/preview/{id:guid}"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Preview(Guid id, Guid? revisionId)
    {
        var actor = await Actor(); var content = await Find(id);
        if (content is null) return NotFound(); if (!await permissions.CanReadAsync(actor, content)) return Forbid();
        var draft = revisionId is null ? await db.ContentDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.ContentId == id) : null;
        var requested = revisionId ?? content.CurrentRevisionId;
        var revision = requested is null ? null : await permissions.ScopeRevisions(db.Revisions.AsNoTracking(),actor).SingleOrDefaultAsync(x => x.Id == requested && x.ContentId == id);
        if (revisionId is not null && revision is null || revision is null && draft is null) return NotFound();
        var categoryId = draft is null ? revision!.PrimaryCategoryId : draft.PrimaryCategoryId;
        var cover = draft is null ? revision!.CoverAssetId : draft.CoverAssetId;
        var coverCaption=cover is null?null:draft is null?await db.ContentAssets.Where(x=>x.RevisionId==revision!.Id&&x.MediaAssetId==cover).Select(x=>x.Caption).FirstOrDefaultAsync():await db.DraftAssets.Where(x=>x.ContentId==id&&x.MediaAssetId==cover).Select(x=>x.Caption).FirstOrDefaultAsync();
        var attachments = draft is null ? await db.ContentAssets.Where(x => x.RevisionId == revision!.Id && (x.MediaAsset.MimeType == "application/pdf" || x.MediaAsset.Origin == "EditorialDocument")).Select(x => new EditorMedia(x.MediaAssetId, x.MediaAsset.OriginalName, x.MediaAsset.MimeType, x.MediaAsset.ByteLength)).ToListAsync() : await db.DraftAssets.Where(x => x.ContentId == id && (x.MediaAsset.MimeType == "application/pdf" || x.MediaAsset.Origin == "EditorialDocument")).Select(x => new EditorMedia(x.MediaAssetId, x.MediaAsset.OriginalName, x.MediaAsset.MimeType, x.MediaAsset.ByteLength)).ToListAsync();
        var category = await db.Categories.Where(x => x.Id == categoryId).Select(x => x.Name).FirstOrDefaultAsync();
        var body = draft?.BodyHtml ?? revision!.BodyHtml;
        var inlineCover = cover is not null && new HtmlParser().ParseDocument(body).QuerySelectorAll("img[src]").Any(x => string.Equals(x.GetAttribute("src"), "/media/" + cover, StringComparison.OrdinalIgnoreCase));
        return View(new PreviewModel { CoverCaption=coverCaption, Metadata = (draft?.Metadata ?? revision!.Metadata).Copy(), ContentId = id, RevisionId = revisionId, Title = draft?.Title ?? revision!.Title, Summary = draft?.Summary ?? revision!.Summary, BodyHtml = PrivateHtml(body, id), Author = draft?.Author ?? revision!.Author, Source = draft?.SourceCredit ?? revision!.SourceCredit, Category = category, CoverUrl = cover is null || inlineCover ? null : $"/admin/media/{cover}/preview?contentId={id}", Attachments = attachments });
    }
    [HttpGet("admin/history/{id:guid}")]
    public async Task<IActionResult> History(Guid id)
    {
        var actor = await Actor(); var content = await Find(id);
        if (content is null) return NotFound(); if (!await permissions.CanReadAsync(actor, content)) return Forbid();
        var revisions = await permissions.ScopeRevisions(db.Revisions.AsNoTracking(),actor).Where(x => x.ContentId == id).OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id).ToListAsync();
        var editors = await db.Users.AsNoTracking().Where(x => revisions.Select(r => r.EditedByUserId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.UserName ?? x.Id);
        var allowedRevisionIds=revisions.Select(x=>x.Id).ToArray();
        var logs = await db.AuditLogs.AsNoTracking().Where(x => x.ContentId == id && (actor.CategoryIds==null || x.RevisionId==null || allowedRevisionIds.Contains(x.RevisionId.Value))).OrderByDescending(x => x.CreatedUtc).Take(200).Select(x => new HistoryAudit(x.Actor, x.Action, x.RevisionId, x.Detail, x.CreatedUtc)).ToListAsync();
        return View(new HistoryModel { ContentId = id, CurrentState = StateLabel(content.State), RowVersion = Convert.ToBase64String(content.RowVersion), CanRestore = actor.Has(Permissions.Restore) && await permissions.CanEditAsync(actor, content) && !content.IsTrashed, CanClone = actor.Has(Permissions.Create), Revisions = revisions.Select(r => new RevisionRow(r.Id, r.Title, r.Summary, htmlPolicy.PlainText(r.BodyHtml), r.Author, r.SourceCredit, r.EditedByUserId is not null && editors.TryGetValue(r.EditedByUserId, out var name) ? name : r.IsSourceRevision ? "Nhập nguồn" : "(Không xác định)", r.CreatedUtc, r.IsSourceRevision, r.Id == content.CurrentRevisionId, content.IsPublic && !content.IsTrashed && r.Id == content.PublishedRevisionId)).ToList(), Events = logs });
    }
    [HttpGet("admin/history/{id:guid}/compare")]
    public async Task<IActionResult> Compare(Guid id, Guid revisionId)
    {
        var actor = await Actor(); var content = await Find(id);
        if (content is null) return NotFound(); if (!await permissions.CanReadAsync(actor, content)) return Forbid();
        var after = await permissions.ScopeRevisions(db.Revisions.AsNoTracking(),actor).SingleOrDefaultAsync(x => x.Id == revisionId && x.ContentId == id);
        if (after is null) return NotFound();
        var before = await permissions.ScopeRevisions(db.Revisions.AsNoTracking(),actor).Where(x => x.ContentId == id && x.CreatedUtc < after.CreatedUtc).OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id).FirstOrDefaultAsync();
        return Json(new { before = before is null ? null : await CompareData(before), after = await CompareData(after) });
    }
    private async Task<object> CompareData(ContentRevision revision) => new
    {
        title = revision.Title, summary = revision.Summary, author = revision.Author, source = revision.SourceCredit,
        metadata = revision.Metadata, bodyText = htmlPolicy.PlainText(revision.BodyHtml), cover = revision.CoverAssetId?.ToString() ?? "",
        categories = string.Join(", ", await db.RevisionCategories.Where(x => x.RevisionId == revision.Id).OrderBy(x => x.Category.SortOrder).Select(x => x.Category.Name).ToListAsync()),
        attachments = string.Join("; ", await db.ContentAssets.Where(x => x.RevisionId == revision.Id).OrderBy(x => x.SortOrder).Select(x => x.MediaAsset.OriginalName + " [" + x.Usage + "] " + x.Caption).ToListAsync())
    };
    private async Task<EditorModel> LoadEditor(Content content)
    {
        var draft = await db.ContentDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.ContentId == content.Id);
        var revision = content.CurrentRevisionId is null ? null : await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == content.CurrentRevisionId);
        if (draft is null && revision is null) throw new InvalidOperationException("Bài chưa có nội dung biên tập.");
        var categories = draft is null ? await db.RevisionCategories.Where(x => x.RevisionId == revision!.Id).Select(x => x.CategoryId).ToArrayAsync() : await db.DraftCategories.Where(x => x.ContentId == content.Id).Select(x => x.CategoryId).ToArrayAsync();
        var assets = draft is null ? await db.ContentAssets.Where(x => x.RevisionId == revision!.Id).Select(x => new EditorMedia(x.MediaAssetId, x.MediaAsset.OriginalName, x.MediaAsset.MimeType, x.MediaAsset.ByteLength)).ToListAsync() : await db.DraftAssets.Where(x => x.ContentId == content.Id).Select(x => new EditorMedia(x.MediaAssetId, x.MediaAsset.OriginalName, x.MediaAsset.MimeType, x.MediaAsset.ByteLength)).ToListAsync();
        var labels = draft is null ? await db.ContentAssets.Where(x=>x.RevisionId == revision!.Id).ToDictionaryAsync(x=>x.MediaAssetId,x=>x.Caption) : await db.DraftAssets.Where(x=>x.ContentId == content.Id).ToDictionaryAsync(x=>x.MediaAssetId,x=>x.Caption);
        var primary = draft is null ? revision?.PrimaryCategoryId : draft.PrimaryCategoryId;
        var body = draft?.BodyHtml ?? revision!.BodyHtml;
        var cover = draft is null ? revision?.CoverAssetId : draft.CoverAssetId;
        var referenced = new HtmlParser().ParseDocument(body).QuerySelectorAll("img[src],a[href]").Select(x => x.GetAttribute(x.LocalName == "img" ? "src" : "href") ?? "").Select(x => TryMediaId(x, out var id) ? id : Guid.Empty).ToHashSet();
        var extras = assets.Where(x => !referenced.Contains(x.Id) && x.Id != cover && x.MimeType != "application/pdf" && x.MimeType != "text/vtt" && !x.MimeType.StartsWith("video/")).Select(x => x.Id).ToArray();
        var preserved = draft is null ? await db.ContentAssets.Where(x => x.RevisionId == revision!.Id && extras.Contains(x.MediaAssetId)).Select(x => new EditorAssetInput { Id = x.MediaAssetId, Usage = x.Usage, Caption = x.Caption, SortOrder = x.SortOrder }).ToListAsync() : await db.DraftAssets.Where(x => x.ContentId == content.Id && extras.Contains(x.MediaAssetId)).Select(x => new EditorAssetInput { Id = x.MediaAssetId, Usage = x.Usage, Caption = x.Caption, SortOrder = x.SortOrder }).ToListAsync();
        return new EditorModel { Kind = content.Kind, Metadata = (draft?.Metadata ?? revision!.Metadata).Copy(), Id = content.Id, CurrentRevisionId = content.CurrentRevisionId, Title = draft?.Title ?? revision!.Title, Summary = draft?.Summary ?? revision!.Summary, BodyHtml = body, Author = draft?.Author ?? revision!.Author, SourceName = draft?.SourceCredit ?? revision!.SourceCredit, CategoryId = primary, SecondaryCategoryIds = categories.Where(x => x != primary).ToArray(), CoverAssetId = cover, CoverCaption=cover is Guid coverId?labels.GetValueOrDefault(coverId, ""):null, VideoAssetIds = assets.Where(x=>x.MimeType.StartsWith("video/")).Select(x=>x.Id).ToArray(), SubtitleAssetIds = assets.Where(x=>x.MimeType=="text/vtt").Select(x=>x.Id).ToArray(), SelectedMedia = assets, AttachmentLabels = labels, AttachmentIds = assets.Where(x => OfficeDocumentPolicy.IsDocument(x.MimeType)).Select(x => x.Id).ToArray(), PreservedAssets = preserved, RowVersion = Convert.ToBase64String(content.RowVersion) };
    }
    private async Task<EditorModel> Populate(EditorModel model, ContentActor actor, Content? content = null)
    {
        model.BodyHtml = htmlPolicy.Clean(model.BodyHtml ?? ""); ModelState.SetModelValue(nameof(EditorModel.BodyHtml), model.BodyHtml, model.BodyHtml);
        content ??= model.Id is null ? null : await Find(model.Id.Value);
        model.IsPublished = content?.IsPublic == true && !content.IsTrashed;
        model.IsTrashed = content?.IsTrashed == true;
        model.DraftState = content?.State.ToString() ?? "Draft";
        model.CurrentStatus = content is null ? "Bản nháp mới" : StateLabel(content.State);
        model.CurrentRevisionId = content?.CurrentRevisionId;
        var canEdit = content is null ? actor.Has(Permissions.Create) : await permissions.CanEditAsync(actor, content);
        model.CanSave = canEdit && !model.IsTrashed && (content is null || content.State is WorkflowState.Draft or WorkflowState.Rejected);
        model.CanBeginEdit = content is not null && canEdit && !model.IsTrashed && !model.CanSave;
        model.CanSubmit = actor.Has(Permissions.Submit); model.CanPublish = actor.Has(Permissions.PublishDirect);
        model.CanPublishApproved = actor.Has(Permissions.PublishApproved); model.CanReview = actor.Has(Permissions.Review);
        model.CanUnpublish = actor.Has(Permissions.Unpublish); model.CanTrash = actor.Has(Permissions.Trash); model.CanRestore = actor.Has(Permissions.Restore);
        model.CanSchedule = actor.Has(Permissions.Schedule); model.CanManageMedia = actor.Has(Permissions.ManageMedia); model.CanManageVideos=actor.Has(Permissions.ManageVideos); model.CanClone = actor.Has(Permissions.Create);
        model.Categories = await Categories(actor);
        if(model.Metadata.WeeklyYear!=null){model.ScheduleRows=V05ContentPolicy.Rows(model.Metadata);while(model.ScheduleRows.Count<14)model.ScheduleRows.Add(new());}
        if (content is not null) model.Kind = content.Kind;
        var liveId = await db.Set<SiteConfiguration>().AsNoTracking().Where(x => x.Id == 1).Select(x => x.LiveRevisionId).FirstOrDefaultAsync();
        model.Vocabularies = await db.Set<SiteVocabulary>().AsNoTracking().Where(x => x.RevisionId == liveId).ToListAsync();
        if (model.AttachmentIds.Length != 0)
        {
            var assets = db.MediaAssets.AsNoTracking().Where(x => model.AttachmentIds.Contains(x.Id));
            if (actor.CategoryIds is not null || !actor.Has(Permissions.EditAll) && !actor.Has(Permissions.ViewAll))
            {
                var readableId = content is not null && await permissions.CanReadAsync(actor, content) ? content.Id : (Guid?)null;
                var readableRevisions=permissions.ScopeRevisions(db.Revisions.AsNoTracking(),actor).Where(r=>r.ContentId==readableId).Select(r=>r.Id);
                assets = assets.Where(x => x.UploadedByUserId == actor.UserId
                    || db.ContentAssets.Any(a => a.MediaAssetId == x.Id && db.Contents.Any(c => c.IsPublic && !c.IsTrashed && c.PublishedRevisionId == a.RevisionId))
                    || readableId != null && (db.DraftAssets.Any(a => a.ContentId == readableId && a.MediaAssetId == x.Id) || db.ContentAssets.Any(a => a.MediaAssetId == x.Id && readableRevisions.Contains(a.RevisionId))));
            }
            model.SelectedMedia = await assets.Select(x => new EditorMedia(x.Id, x.OriginalName, x.MimeType, x.ByteLength)).ToListAsync();
        }
        if (content?.State == WorkflowState.Rejected) model.RejectionReason = await db.AuditLogs.Where(x => x.ContentId == content.Id && x.Action == "Reject").OrderByDescending(x => x.CreatedUtc).Select(x => x.Detail).FirstOrDefaultAsync();
        return model;
    }
    private Task<List<EditorCategory>> Categories(ContentActor actor) => db.Categories.AsNoTracking().Where(x=>actor.CategoryIds==null||actor.CategoryIds.Contains(x.Id))
        .Where(c=>!db.Set<SiteConfiguration>().Any(s=>s.Id == 1 && s.LiveRevisionId != null) || db.Set<SiteCategoryConfiguration>().Any(x=>x.CategoryId == c.Id && db.Set<SiteConfiguration>().Any(s=>s.Id == 1 && s.LiveRevisionId == x.RevisionId)))
        .OrderBy(x=>x.SortOrder).ThenBy(x=>x.Id).Select(x=>new EditorCategory(x.Id,db.Set<SiteCategoryConfiguration>().Where(v=>v.CategoryId == x.Id && db.Set<SiteConfiguration>().Any(s=>s.Id == 1 && s.LiveRevisionId == v.RevisionId)).Select(v=>v.DisplayName).FirstOrDefault() ?? x.Name)).ToListAsync();
    private Task<Content?> Find(Guid id) => db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
    private byte[]? Version(string? raw, bool required)
    {
        if (!required) return null;
        try { var bytes = Convert.FromBase64String(raw ?? ""); if (bytes.Length != 8) throw new FormatException(); return bytes; }
        catch (FormatException) { ModelState.AddModelError("RowVersion", "Thiếu hoặc sai phiên bản chống ghi đè. Tải lại để đối chiếu."); return null; }
    }
    private DraftInput Input(EditorModel model)
    {
        if(model.Metadata.WeeklyYear!=null){model.Metadata.WeeklyRowsJson=System.Text.Json.JsonSerializer.Serialize(model.ScheduleRows.Where(x=>!string.IsNullOrWhiteSpace(x.Content)||x.Date!=null).ToList());}
        var body = htmlPolicy.Clean(model.BodyHtml ?? "");
        var assets = new Dictionary<Guid, AssetSelection>();
        var dom = new HtmlParser().ParseDocument(body); var order = 0;
        foreach (var element in dom.QuerySelectorAll("img[src],a[href]"))
        {
            var path = element.GetAttribute(element.LocalName == "img" ? "src" : "href") ?? "";
            if (!TryMediaId(path, out var id)) continue;
            assets[id] = new AssetSelection { MediaAssetId = id, Usage = element.LocalName == "img" ? "Inline" : "Attachment", Caption = element.Closest("figure")?.QuerySelector("figcaption")?.TextContent ?? "", SortOrder = order++ };
        }
        foreach (var id in model.AttachmentIds.Distinct()) assets[id] = new AssetSelection { MediaAssetId = id, Usage = "Attachment", Caption = model.AttachmentLabels.GetValueOrDefault(id, "") ?? "", SortOrder = order++ };
        foreach(var id in model.VideoAssetIds.Distinct())assets[id]=new AssetSelection{MediaAssetId=id,Usage="Video",Caption=model.AttachmentLabels.GetValueOrDefault(id,"")??"",SortOrder=order++};
        foreach(var id in model.SubtitleAssetIds.Distinct())assets[id]=new AssetSelection{MediaAssetId=id,Usage="Subtitle",Caption=model.AttachmentLabels.GetValueOrDefault(id,"")??"",SortOrder=order++};
        if (model.CoverAssetId is not null) assets[model.CoverAssetId.Value] = new AssetSelection { MediaAssetId = model.CoverAssetId.Value, Usage = "Cover", Caption=model.CoverCaption??model.AttachmentLabels.GetValueOrDefault(model.CoverAssetId.Value,"")??"", SortOrder = -1 };
        // Historical metadata is not an access grant. Only the current body, cover or
        // explicitly selected attachments may keep a media membership in this revision.
        foreach (var asset in model.PreservedAssets)
        {
            if (!assets.TryGetValue(asset.Id, out var active)) continue;
            if (string.IsNullOrWhiteSpace(active.Caption)) active.Caption = asset.Caption ?? "";
        }
        return new DraftInput { Kind = model.Kind, Metadata = model.Metadata, Title = model.Title, Summary = model.Summary ?? "", BodyHtml = body, PrimaryCategoryId = model.CategoryId, SecondaryCategoryIds = model.SecondaryCategoryIds.Distinct().ToList(), CoverAssetId = model.CoverAssetId, Author = model.Author ?? "", SourceCredit = model.SourceName ?? "", Assets = assets.Values.ToList() };
    }
    private static bool TryMediaId(string path, out Guid id) { id = Guid.Empty; return path.StartsWith("/media/", StringComparison.Ordinal) && Guid.TryParse(path[7..].Split('?', '#')[0], out id); }
    private static string PrivateHtml(string body, Guid contentId)
    {
        var document = new HtmlParser().ParseDocument(body);
        foreach (var element in document.QuerySelectorAll("[src],[href]")) foreach (var attr in new[] { "src", "href" }) if (TryMediaId(element.GetAttribute(attr) ?? "", out var id)) element.SetAttribute(attr, $"/admin/media/{id}/preview?contentId={contentId}");
        return document.Body?.InnerHtml ?? "";
    }
    private IActionResult Error(int status, string message, bool json) => json ? StatusCode(status, new { message }) : status == 403 ? Forbid() : StatusCode(status, message);
    private static object Result(WorkflowResult result) => new { contentId = result.ContentId, currentRevisionId = result.CurrentRevisionId, publishedRevisionId = result.PublishedRevisionId, rowVersion = Convert.ToBase64String(result.RowVersion), state = result.State.ToString(), result.IsPublic, result.IsTrashed, result.AlreadyApplied, savedUtc = result.SavedUtc };
    // Stable identifiers keep the legacy combined form action idempotent on network retry.
    private static Guid FollowupCommand(Guid requestId, string action) => new(SHA256.HashData(Encoding.UTF8.GetBytes(requestId.ToString("D") + ":" + action)).AsSpan(0, 16));
    public static string StateLabel(WorkflowState state) => AdminLabels.State(state);
    public static string ActionLabel(string action) => AdminLabels.Action(action);
    private sealed class AdminQueryRow { public Guid Id { get; init; } public string Title { get; init; } = ""; public int? CategoryId { get; init; } public string? OwnerId { get; init; } public WorkflowState State { get; init; } public bool IsPublic { get; init; } public bool IsTrashed { get; init; } public DateTimeOffset UpdatedUtc { get; init; } }
}
