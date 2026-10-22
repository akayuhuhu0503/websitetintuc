using System.Security.Claims;
using System.Security.Cryptography;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.Controllers;
[Authorize,Route("admin/hoi-dap"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None),RequestSizeLimit(262144)]
public sealed class InquiryManagementController(PortalDbContext db,InteractionService interaction,CurrentPermissionService permissions,InquiryAssignmentService assignments,InquiryCalendarService calendars,InquiryExcelService excel,InteractionBatchService batch,InquiryAttachmentService attachments):Controller
{
    private Task<ContentActor> Actor()=>permissions.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private async Task<IActionResult> Execute(Func<Task> action,Guid id)
    {try{if(!ModelState.IsValid)return BadRequest("Thông tin thao tác không hợp lệ.");await action();return Redirect("/admin/tuong-tac/"+id);}catch(UnauthorizedAccessException){return StatusCode(403);}catch(KeyNotFoundException){return NotFound();}catch(DbUpdateConcurrencyException){return Conflict("Hồ sơ đã đổi; tải lại trước khi xử lý.");}catch(Exception e)when(e is InvalidOperationException or FormatException){return BadRequest(e.Message);}}
    [HttpPost("{id:guid}/phan-cong")]
    public Task<IActionResult> Assign(Guid id,InquiryAssignForm form)=>Execute(async()=>await assignments.AssignAsync(await Actor(),id,form.AssigneeId,Convert.FromBase64String(form.RowVersion),form.CommandId,form.Reason),id);
    [HttpPost("{id:guid}/gia-han")]
    public Task<IActionResult> Extend(Guid id,InquiryExtendForm form)=>Execute(async()=>await calendars.ExtendAsync(await Actor(),id,Convert.FromBase64String(form.RowVersion),form.CommandId,new DateTimeOffset(DateTime.SpecifyKind(form.DueVietnam,DateTimeKind.Unspecified),TimeSpan.FromHours(7)).ToUniversalTime(),form.Reason),id);
    [HttpGet("tep/{id:guid}"),HttpHead("tep/{id:guid}")]
    public async Task<IActionResult> FileDownload(Guid id)
    {
        var actor=await Actor();bool Access(ContentActor a,InteractionKind k)=>k==InteractionKind.Inquiry?a.Has(Permissions.InquiryRead)&&a.Has(Permissions.InquiryFilesRead):k==InteractionKind.Contact&&a.Has(Permissions.ContactRead)&&a.Has(Permissions.ContactFilesRead);if(!Access(actor,InteractionKind.Inquiry)&&!Access(actor,InteractionKind.Contact))return StatusCode(403);
        var file=await db.InquiryAttachments.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.SubmissionId!=null);if(file==null)return NotFound();var kind=await db.InteractionSubmissions.Where(x=>x.Id==file.SubmissionId).Select(x=>x.Kind).SingleAsync();if(!Access(actor,kind)||!await interaction.Inbox(actor,kind).AnyAsync(x=>x.Id==file.SubmissionId))return NotFound();
        if(file.ScanState!=InquiryScanState.Clean||file.ScannedSha256!=file.Sha256||file.ScannedUtc==null||string.IsNullOrWhiteSpace(file.EngineVersion))return StatusCode(423,"Tệp chưa có bằng chứng quét sạch; không được tải.");
        var path=attachments.PathFor(file);FileStream? stream=null;
        try
        {
            stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
            if(stream.Length!=file.Bytes||Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,Request.HttpContext.RequestAborted))!=file.Sha256)
            {
                await stream.DisposeAsync();await using var tx=await db.Database.BeginTransactionAsync();
                if(await db.InquiryAttachments.Where(x=>x.Id==id&&x.RowVersion==file.RowVersion).ExecuteUpdateAsync(x=>x.SetProperty(f=>f.ScanState,InquiryScanState.ScanFailed).SetProperty(f=>f.ScanError,"DownloadHashMismatch").SetProperty(f=>f.ScannedSha256,(string?)null).SetProperty(f=>f.ScannedUtc,(DateTimeOffset?)null))==1)
                {db.AuditLogs.Add(new(){Actor=actor.UserId,Action="inquiry.scan.invalidated",Detail=System.Text.Json.JsonSerializer.Serialize(new{AssetId=id,Reason="DownloadHashMismatch"})});await db.SaveChangesAsync();}
                await tx.CommitAsync();return Conflict("Tệp/hash đã thay đổi; phải đối soát và quét lại.");
            }
            actor=await Actor();if(!Access(actor,kind)||!await interaction.Inbox(actor,kind).AnyAsync(x=>x.Id==file.SubmissionId)){await stream.DisposeAsync();return StatusCode(403);}
            if(!await db.InquiryAttachments.AsNoTracking().AnyAsync(x=>x.Id==id&&x.RowVersion==file.RowVersion&&x.ScanState==InquiryScanState.Clean)){await stream.DisposeAsync();return Conflict("Trạng thái tệp đã đổi.");}
            stream.Position=0;return File(stream,file.Mime,attachments.Name(file),enableRangeProcessing:true);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){if(stream!=null)await stream.DisposeAsync();return NotFound();}
        catch{if(stream!=null)await stream.DisposeAsync();throw;}
    }
    [HttpGet("excel")]
    public async Task<IActionResult> Export(InteractionState? state,string? field,bool includePii=false)
    {
        try{var result=await excel.ExportAsync(await Actor(),state,field,includePii);Response.Headers["Cache-Control"]="no-store";Response.Headers["X-Export-Rows"]=result.Rows.ToString();return File(result.Bytes,"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","hoi-dap.xlsx");}catch(UnauthorizedAccessException){return StatusCode(403);}catch(InvalidOperationException e){return BadRequest(e.Message);}
    }
    [HttpPost("batch")]
    public async Task<IActionResult> Batch([FromBody]List<InteractionBatchItem> items)
    {if(!ModelState.IsValid)return BadRequest("Danh sách xử lý không hợp lệ.");try{return Json(await batch.ApplyAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!,items));}catch(InvalidOperationException e){return BadRequest(e.Message);}}
    [HttpGet("lich")]
    public async Task<IActionResult> Calendar()
    {
        var actor=await Actor();if(!actor.Has(Permissions.InquiryCalendarManage)||!actor.Has(Permissions.InquirySupervise))return StatusCode(403);ViewData["Versions"]=await db.InquiryCalendars.AsNoTracking().OrderByDescending(x=>x.CreatedUtc).Take(50).ToListAsync();return View(new InquiryCalendarForm{EffectiveFrom=DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime),Label="Lịch local tạm tính",PolicyVersion="local-v1"});
    }
    [HttpPost("lich")]
    public async Task<IActionResult> SaveCalendar(InquiryCalendarForm form)
    {
        if(!ModelState.IsValid)return BadRequest("Lịch không hợp lệ.");try
        {
            var calendar=new InquiryCalendar{Label=form.Label,PolicyVersion=form.PolicyVersion,EffectiveFrom=form.EffectiveFrom,WorkingDays=form.WorkingDays,Cutoff=form.Cutoff,CountReceiptDay=form.CountReceiptDay,WeekendDays=form.WeekendDays};
            foreach(var line in form.Exceptions.Split('\n',StringSplitOptions.RemoveEmptyEntries)){var parts=line.Trim().Split('|',3);if(parts.Length<2||!DateOnly.TryParseExact(parts[0],"yyyy-MM-dd",out var date)||parts[1] is not("work" or "off"))throw new InvalidOperationException("Ngoại lệ dùng yyyy-MM-dd|work/off|ghi chú.");calendar.Exceptions.Add(new(){Date=date,WorkingDay=parts[1]=="work",Note=parts.Length==3?parts[2]:""});}
            await calendars.CreateAsync(await Actor(),calendar);return RedirectToAction(nameof(Calendar));
        }catch(UnauthorizedAccessException){return StatusCode(403);}catch(InvalidOperationException e){return BadRequest(e.Message);}
    }
}
