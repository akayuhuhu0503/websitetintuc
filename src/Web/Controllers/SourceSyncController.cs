using System.Security.Claims;
using LandPortal.Application;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

[Authorize(Policy=Permissions.ManageSite),AutoValidateAntiforgeryToken]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class SourceSyncController(PortalDbContext db,SourceSyncStore store,WorkflowService workflow,SourceSyncMedia transfer):Controller
{
    string UserId=>User.FindFirstValue(ClaimTypes.NameIdentifier)??throw new UnauthorizedAccessException();
    [HttpGet("/admin/source-sync")]
    public async Task<IActionResult> Index(string? q,string? status,int page=1,bool fragment=false,CancellationToken ct=default,string tab="pending")
    {
        if(q?.Length>200||status?.Length>30)return BadRequest();
        ContentActor actor;
        try{actor=await store.RequireAsync(UserId);}catch(UnauthorizedAccessException){return Forbid();}
        var model=await store.PageAsync(q,status,page,ct,tab);
        if(fragment)return PartialView("_Results",model);
        ViewBag.Categories=await db.Categories.AsNoTracking().Where(x=>actor.CategoryIds==null||actor.CategoryIds.Contains(x.Id)).OrderBy(x=>x.SortOrder).ToListAsync(ct);
        return View(model);
    }
    [HttpGet("/admin/source-sync/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id,CancellationToken ct)
    {
        ContentActor actor;
        try{actor=await store.RequireAsync(UserId);}catch(UnauthorizedAccessException){return Forbid();}
        if(!await db.Jobs.AnyAsync(x=>x.Id==id&&x.Type==SourceSyncPolicy.EntryType,ct))return NotFound();
        var (item,data)=await store.ReadAsync(id,ct);
        ViewBag.Item=item;ViewBag.CanCreate=actor.Has(Permissions.Create)&&actor.Has(Permissions.ManageMedia);
        if(data.ContentId is Guid content&&await new CurrentPermissionService(db).CanAccessContentAsync(actor,content))
        {
            var c=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==content,ct);
            if(CurrentPermissionService.CanRead(actor,c))
            {
                ViewBag.Existing=await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==c.CurrentRevisionId,ct);
                ViewBag.ExistingId=content;ViewBag.Version=Convert.ToBase64String(c.RowVersion);
            }
        }
        return View(data);
    }
    [HttpPost("/admin/source-sync/configure")]
    public Task<IActionResult> Configure(bool? enabled,int? sourceCategory,int? targetCategory,CancellationToken ct)=>Command(async()=>{
        await store.ConfigureAsync(UserId,enabled,sourceCategory,targetCategory,ct);return "Đã lưu lựa chọn đồng bộ. Không thay đổi chuyên mục hoặc công bố bài.";
    });
    [HttpPost("/admin/source-sync/check")]
    public Task<IActionResult> Check(CancellationToken ct)=>Command(async()=>{await store.RequestAsync(UserId,ct);return "Đã nhận yêu cầu. Tiến trình nền sẽ kiểm tra nguồn; bạn có thể rời trang.";});
    [HttpPost("/admin/source-sync/{id:guid}/state")]
    public Task<IActionResult> State(Guid id,string action,CancellationToken ct)=>Command(async()=>{await store.SetItemAsync(id,UserId,action,ct);return "Đã cập nhật hàng chờ.";});
    [HttpPost("/admin/source-sync/{id:guid}/draft")]
    public async Task<IActionResult> Draft(Guid id,string hash,CancellationToken ct,string? assetHash=null)
    {
        try
        {
            if(hash?.Length!=64)return BadRequest(new{message="Thiếu phiên bản nguồn. Tải lại trang xem trước."});
            var actor=await store.RequireAsync(UserId,draft:true);
            var result=await workflow.CreateSourceDraftAsync(id,hash,actor,transfer,ct,assetHash);
            return Json(new{message="Đã tạo bản nháp, chưa công khai. Kiểm tra cảnh báo/tệp trước khi gửi duyệt.",redirect="/admin/edit/"+result.ContentId});
        }
        catch(UnauthorizedAccessException){return Forbid();}
        catch(Exception e)when(e is InvalidOperationException or IOException or InvalidDataException or DbUpdateConcurrencyException){return Conflict(new{message="Chưa chuyển được: "+SafeMessage(e)});}
    }
    [HttpPost("/admin/source-sync/{id:guid}/update")]
    public async Task<IActionResult> Update(Guid id,Guid contentId,string hash,string assetHash,string version,Guid commandId,CancellationToken ct)
    {
        try
        {
            if(hash?.Length!=64||assetHash?.Length!=64||version?.Length!=12||commandId==Guid.Empty||contentId==Guid.Empty)return BadRequest();
            var actor=await store.RequireAsync(UserId,draft:true);
            var result=await workflow.UpdateSourceWeeklyDraftAsync(id,contentId,hash,assetHash,Convert.FromBase64String(version),commandId,actor,transfer,ct);
            return Json(new{message="Đã tạo bản sửa nháp lịch tuần; bản công khai được giữ nguyên.",redirect="/admin/edit/"+result.ContentId});
        }
        catch(UnauthorizedAccessException){return Forbid();}
        catch(FormatException){return BadRequest();}
        catch(Exception e)when(e is InvalidOperationException or IOException or DbUpdateConcurrencyException){return Conflict(new{message=SafeMessage(e)});}
    }
    async Task<IActionResult> Command(Func<Task<string>> action)
    {
        try{return Json(new{message=await action()});}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(InvalidOperationException e){return Conflict(new{message=SafeMessage(e)});}
    }
    static string SafeMessage(Exception e)=>e is DbUpdateConcurrencyException?"Dữ liệu đã thay đổi, vui lòng tải lại.":e is IOException or InvalidDataException?"Tệp/định danh nguồn chưa đầy đủ hoặc không còn khớp. Kiểm tra lại hàng chờ.":e.Message;
}
