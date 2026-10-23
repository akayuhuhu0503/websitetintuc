using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.Controllers;
[Authorize,Route("admin/tuong-tac"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class InteractionAdminController(PortalDbContext db,InteractionService service,CurrentPermissionService permissions):Controller
{
    private Task<ContentActor> Actor()=>permissions.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("")]
    public async Task<IActionResult> Index(InteractionKind kind=InteractionKind.Comment,InteractionState? state=null,string? field=null,string? due=null,int page=1,Guid? contentId=null)
    {
        var actor=await Actor();if(!Enum.IsDefined(kind)||!InteractionPermissions.CanList(actor,kind))return StatusCode(403);
        if(!ModelState.IsValid || state is not null && !Enum.IsDefined(state.Value) || contentId is not null && kind!=InteractionKind.Comment)return BadRequest("Bộ lọc không hợp lệ.");
        ViewData["CanManageReception"]=actor.Has(Permissions.CommentModerate)&&actor.Has(Permissions.ManageSettings);
        ViewData["CanExport"]=kind==InteractionKind.Inquiry&&actor.Has(Permissions.InquiryRead)&&actor.Has(Permissions.InquiryExport);ViewData["CanExportPii"]=actor.Has(Permissions.InquiryExportPii);ViewData["CanManageCalendar"]=actor.Has(Permissions.InquiryCalendarManage)&&actor.Has(Permissions.InquirySupervise);
        ViewData["CanContactExport"]=actor.Has(Permissions.ContactRead)&&actor.Has(Permissions.InteractionExport);ViewData["CanContactExportPii"]=actor.Has(Permissions.ContactExportPii);
        var pending=new Dictionary<InteractionKind,int>();foreach(var k in Enum.GetValues<InteractionKind>())if(InteractionPermissions.CanList(actor,k))pending[k]=await service.Inbox(actor,k).CountAsync(x=>x.State==InteractionState.Pending);
        var rows=service.Inbox(actor,kind);if(state!=null)rows=rows.Where(x=>x.State==state);page=Math.Clamp(page,1,10000);
        // Apply the article filter AFTER the permission/category scope. A guessed
        // article ID must not reveal its title, existence or out-of-scope comments.
        if(contentId is Guid article)rows=rows.Where(x=>x.ContentId==article);
        if(field!=null){if(field.Length>120)return BadRequest("Lĩnh vực quá dài.");rows=rows.Where(x=>x.InquiryField==field);}
        var now=DateTimeOffset.UtcNow;if(due=="late")rows=rows.Where(x=>x.InquiryDueUtc<now&&x.State!=InteractionState.Closed&&x.State!=InteractionState.Rejected);else if(due=="soon")rows=rows.Where(x=>x.InquiryDueUtc>=now&&x.InquiryDueUtc<now.AddDays(2)&&x.State!=InteractionState.Closed&&x.State!=InteractionState.Rejected);
        var total=await rows.CountAsync();var items=await rows.OrderByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).Skip((page-1)*20).Take(20).ToListAsync();
        var names=await db.Users.AsNoTracking().Where(x=>items.Select(i=>i.AssigneeId).Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.DisplayName==""?"Cán bộ xử lý":x.DisplayName);
        return View(new InteractionInbox(kind,total,page,items,pending){ContentId=contentId,Field=field,CanReadOriginal=actor.Has(InteractionPermissions.Read(kind)),Due=due,State=state,Assignees=names});
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id)
    {
        var actor=await Actor();var allowed=Enum.GetValues<InteractionKind>().Where(k=>actor.Has(InteractionPermissions.Read(k))).ToArray();if(allowed.Length==0)return StatusCode(403);
        var kind=await db.InteractionSubmissions.Where(x=>x.Id==id&&allowed.Contains(x.Kind)).Select(x=>(InteractionKind?)x.Kind).SingleOrDefaultAsync();
        if(kind==null)return NotFound();if(!actor.Has(InteractionPermissions.Read(kind.Value)))return StatusCode(403);
        var s=await service.Inbox(actor,kind.Value).SingleOrDefaultAsync(x=>x.Id==id);if(s==null)return NotFound();
        var mail=await db.InteractionOutbox.AsNoTracking().Where(x=>x.SubmissionId==id).ToListAsync();
        var history=new List<InquiryOperationHistory>();
        if(s.Kind==InteractionKind.Inquiry&&(actor.Has(Permissions.InquirySupervise)||actor.Has(Permissions.InquiryAssign)))
        {
            var assigned=await db.InquiryAssignments.AsNoTracking().Where(x=>x.SubmissionId==id).OrderByDescending(x=>x.CreatedUtc).Take(30).ToListAsync();
            history.AddRange(assigned.Select(x=>new InquiryOperationHistory(x.CreatedUtc,"Phân công/chuyển cán bộ",service.Unprotect<string>(x.ProtectedReason))));
            var extended=await db.InquiryExtensions.AsNoTracking().Where(x=>x.SubmissionId==id).OrderByDescending(x=>x.CreatedUtc).Take(30).ToListAsync();
            history.AddRange(extended.Select(x=>new InquiryOperationHistory(x.CreatedUtc,"Gia hạn đến "+x.DueUtc.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm"),service.Unprotect<string>(x.ProtectedReason))));
        }
        var staff=new Dictionary<string,string>();if(s.Kind==InteractionKind.Inquiry&&actor.Has(Permissions.InquiryAssign))
        {
            var responders=from ur in db.UserRoles join claim in db.RoleClaims on ur.RoleId equals claim.RoleId where claim.ClaimType=="permission"&&claim.ClaimValue==Permissions.InquiryRespond select ur.UserId;
            var candidates=await db.Users.AsNoTracking().Where(x=>!x.IsDisabled&&!x.MustChangePassword&&(db.UserClaims.Any(c=>c.UserId==x.Id&&c.ClaimType=="permission"&&c.ClaimValue==Permissions.InquiryRespond)||responders.Contains(x.Id))).OrderBy(x=>x.Id).Take(100).ToListAsync();foreach(var user in candidates){var target=await permissions.GetAsync(user.Id);if(target.Has(Permissions.InquiryRead)&&target.Has(Permissions.InquiryRespond))staff[user.Id]=user.DisplayName==""?"Cán bộ "+user.Id[..Math.Min(8,user.Id.Length)]:user.DisplayName;}
        }
        return View(new InteractionDetail(s,service.Unprotect<InteractionInput>(s.ProtectedOriginal),s.ProtectedAnswer==""?"":service.Unprotect<string>(s.ProtectedAnswer),mail,actor.Has(InteractionPermissions.Process(s.Kind)),s.Kind==InteractionKind.Comment?actor.Has(Permissions.CommentModerate):s.Kind==InteractionKind.Inquiry&&actor.Has(Permissions.InquiryPublish),actor.Has(s.Kind==InteractionKind.Comment?Permissions.CommentReply:Permissions.InquiryRespond))
        {Assignees=staff,History=history.OrderByDescending(x=>x.Utc).Take(30).ToList(),Attachment=await db.InquiryAttachments.AsNoTracking().SingleOrDefaultAsync(x=>x.SubmissionId==s.Id),CanAssign=actor.Has(Permissions.InquiryAssign),CanExtend=actor.Has(Permissions.InquiryExtend)&&actor.Has(Permissions.InquirySupervise),CanReadFile=actor.Has(s.Kind==InteractionKind.Contact?Permissions.ContactFilesRead:Permissions.InquiryFilesRead)});
    }
    [HttpPost("{id:guid}")]
    public async Task<IActionResult> Apply(Guid id,InteractionAction input)
    {
        if(!ModelState.IsValid)return BadRequest("Thông tin xử lý không hợp lệ.");
        try{await service.ModerateAsync(await Actor(),id,Convert.FromBase64String(input.RowVersion),input.CommandId,input.Action,input.DisplayName,input.Text,input.Answer,input.Reason);return RedirectToAction(nameof(Detail),new{id});}
        catch(UnauthorizedAccessException){return StatusCode(403);}catch(KeyNotFoundException){return NotFound();}catch(DbUpdateConcurrencyException){return Conflict("Hồ sơ đã thay đổi. Tải lại trước khi xử lý.");}catch(Exception e)when(e is InvalidOperationException or FormatException){return BadRequest(e.Message);}
    }
    [HttpGet("tiep-nhan-binh-luan")]
    public async Task<IActionResult> Reception()
    {
        var actor=await Actor();if(!actor.Has(Permissions.CommentModerate)||!actor.Has(Permissions.ManageSettings))return StatusCode(403);
        ViewData["Enabled"]=await db.SiteSettings.AnyAsync(x=>x.Key=="Interactions.CommentsEnabled"&&x.Value=="true");
        var contents=await permissions.Scope(db.Contents.AsNoTracking(),actor).Where(c=>c.Kind==ContentKind.Article&&c.IsPublic&&!c.IsTrashed).OrderByDescending(c=>c.PublishedAtUtc).Take(50).Select(c=>new{c.Id,c.CommentsEnabled,c.RowVersion}).ToListAsync();
        return View(contents.Select(c=>new LandPortal.Web.Models.CommentReceptionRow(c.Id,c.CommentsEnabled,Convert.ToBase64String(c.RowVersion))).ToArray());
    }
    [HttpPost("tiep-nhan-binh-luan")]
    public async Task<IActionResult> ReceptionChange(Guid? contentId,bool enabled,bool expectedEnabled,string? rowVersion)
    {
        var actor=await Actor();if(!actor.Has(Permissions.CommentModerate)||!actor.Has(Permissions.ManageSettings))return StatusCode(403);
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        if(contentId is Guid id)
        {
            var c=await db.Contents.SingleOrDefaultAsync(x=>x.Id==id&&x.Kind==ContentKind.Article);if(c==null||!await permissions.CanEditAsync(actor,c))return StatusCode(403);
            if(rowVersion==null||Convert.ToBase64String(c.RowVersion)!=rowVersion||c.CommentsEnabled!=expectedEnabled)return Conflict("Cấu hình bài đã đổi.");c.CommentsEnabled=enabled;
        }
        else
        {
            var setting=await db.SiteSettings.SingleOrDefaultAsync(x=>x.Key=="Interactions.CommentsEnabled");if((setting?.Value=="true")!=expectedEnabled)return Conflict("Cấu hình đã đổi.");
            if(setting==null)db.SiteSettings.Add(new(){Key="Interactions.CommentsEnabled",Value=enabled?"true":"false"});else setting.Value=enabled?"true":"false";
        }
        db.AuditLogs.Add(new(){Actor=actor.UserId,Action="interaction.reception",ContentId=contentId,Detail=System.Text.Json.JsonSerializer.Serialize(new{enabled})});await db.SaveChangesAsync();await tx.CommitAsync();return RedirectToAction(nameof(Reception));
    }
}
