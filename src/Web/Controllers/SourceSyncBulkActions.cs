using LandPortal.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

public sealed record SourceBulkPreviewRequest(string Action, List<SourceBulkSelection> Items);
public sealed record SourceBulkExecuteRequest(string Action, SourceBulkSelection Item, Guid RequestId);
public sealed partial class SourceSyncController
{
    static bool Valid(SourceBulkSelection? item) => item != null && item.Id != Guid.Empty && item.Stamp?.Length == 64 && item.Stamp.All(Uri.IsHexDigit);
    [HttpPost("/admin/source-sync/bulk/preview"), RequestSizeLimit(65536)]
    public async Task<IActionResult> BulkPreview([FromBody] SourceBulkPreviewRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request == null || !SourceSyncBulk.Actions.Contains(request.Action) || request.Items == null || request.Items.Count is < 1 or > 100 ||
            request.Items.Any(x=>!Valid(x)) || request.Items.Select(x=>x.Id).Distinct().Count()!=request.Items.Count) return BadRequest();
        LandPortal.Application.ContentActor actor;
        try { actor=await store.RequireAsync(UserId); } catch(UnauthorizedAccessException) { return Forbid(); }
        if(!SourceSyncBulk.Allowed(actor,request.Action))return Forbid();
        var bulk=new SourceSyncBulk(db,store,workflow,transfer);var rows=new List<object>();
        foreach(var item in request.Items)
        {
            string? reason;
            try {
                reason=await bulk.ReasonAsync(item,request.Action,actor,ct);
                // A response may be lost after the draft was committed. Let the operator
                // replay only their recorded command; Execute validates its exact request hash.
                if(reason!=null&&item.RequestId.HasValue&&request.Action!="Ignore")
                {
                    var (_,data)=await store.ReadAsync(item.Id,ct);
                    if(data.ContentId.HasValue&&await db.ContentCommands.AnyAsync(x=>x.Id==item.RequestId&&x.ActorUserId==UserId&&x.Action=="SourceSyncBulkDraft"&&x.ContentId==data.ContentId,ct)) reason=null;
                }
            }
            catch(Exception error) when(error is InvalidOperationException or IOException){reason="Chưa đọc được phiên bản nguồn; mở xem trước để kiểm tra.";}
            rows.Add(new{item.Id,eligible=reason==null,reason,url="/admin/source-sync/"+item.Id});
        }
        return Json(new{items=rows});
    }
    [HttpPost("/admin/source-sync/bulk/execute"), RequestSizeLimit(4096)]
    public async Task<IActionResult> BulkExecute([FromBody] SourceBulkExecuteRequest request,CancellationToken ct)
    {
        if(!ModelState.IsValid||request==null||request.RequestId==Guid.Empty||!Valid(request.Item)||!SourceSyncBulk.Actions.Contains(request.Action))return BadRequest();
        try
        {
            var result=await new SourceSyncBulk(db,store,workflow,transfer).ExecuteAsync(request.Item,new(request.RequestId,request.Action,request.Item.Stamp),UserId,ct);
            return Json(new{result.Id,result.Outcome,result.Message,editUrl=result.ContentId.HasValue?"/admin/edit/"+result.ContentId:null,
                publicUrl=result.Outcome=="Published"?"/bai-viet/"+result.ContentId:null});
        }
        catch(UnauthorizedAccessException){return Forbid();}
        catch(Exception error) when(error is InvalidOperationException or IOException or MediaUploadException or DbUpdateConcurrencyException)
        {return Conflict(new{message=SafeMessage(error)});}
    }
}
