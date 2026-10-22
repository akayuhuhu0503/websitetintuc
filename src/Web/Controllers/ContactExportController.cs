using System.Security.Claims;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Web.Controllers;
[Authorize,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ContactExportController(PortalDbContext db,InteractionService service,CurrentPermissionService permissions):Controller
{
    [HttpGet("admin/hom-thu/excel")]
    public async Task<IActionResult> Index(InteractionState? state,bool includePii=false)
    {
        var actor=await permissions.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if(!actor.Has(Permissions.ContactRead)||!actor.Has(Permissions.InteractionExport)||(includePii&&!actor.Has(Permissions.ContactExportPii)))return StatusCode(403);
        if(!ModelState.IsValid||state!=null&&!Enum.IsDefined(state.Value))return BadRequest();
        var query=service.Inbox(actor,InteractionKind.Contact);if(state!=null)query=query.Where(x=>x.State==state);
        if(await query.CountAsync()>1000)return BadRequest("Giới hạn1000 hồ sơ; lọc hẹp hơn trước khi xuất.");
        var rows=await query.OrderByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).ToListAsync();
        var data=new List<string[]>{includePii?["Mã nội bộ","Trạng thái","Tiếp nhận UTC+7","Họ tên","Email","Điện thoại","Tiêu đề","Nội dung"]:["Mã nội bộ","Trạng thái","Tiếp nhận UTC+7"]};
        foreach(var row in rows){var cells=new List<string>{row.Id.ToString(),row.State.ToString(),row.CreatedUtc.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm")};if(includePii){var original=service.Unprotect<InteractionInput>(row.ProtectedOriginal);cells.AddRange([original.Name,original.Email,original.Phone,original.Title,original.Text]);}data.Add(cells.ToArray());}
        var current=await permissions.GetAsync(actor.UserId);if(!current.Has(Permissions.ContactRead)||!current.Has(Permissions.InteractionExport)||(includePii&&!current.Has(Permissions.ContactExportPii)))return StatusCode(403);
        db.AuditLogs.Add(new(){Actor=actor.UserId,Action="contact.exported",Detail=JsonSerializer.Serialize(new{state,Rows=rows.Count,includePii})});await db.SaveChangesAsync();
        return File(InquiryExcelService.Workbook(data),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","hom-thu.xlsx");
    }
}
