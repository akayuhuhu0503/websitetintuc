using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Controllers;

public sealed record BulkSelection(Guid Id, Guid RevisionId, string RowVersion);
public sealed record BulkPreviewRequest(string Action, List<BulkSelection> Items);
public sealed record BulkExecuteRequest(string Action, BulkSelection Item, Guid RequestId);

public sealed partial class AdminController
{
    [HttpPost("admin/bulk/preview"), RequestSizeLimit(65536)]
    public async Task<IActionResult> BulkPreview([FromBody] BulkPreviewRequest request)
    {
        if (!ModelState.IsValid || request == null || !BulkEditorialPolicy.Labels.ContainsKey(request.Action ?? "") ||
            request.Items == null || request.Items.Count is < 1 or > BulkEditorialPolicy.SelectionLimit ||
            request.Items.Any(x => x == null) || request.Items.Select(x => x.Id).Distinct().Count() != request.Items.Count)
            return BadRequest(new { message = "Chọn từ 1 đến 100 bài riêng biệt và một thao tác hợp lệ." });
        var actor = await Actor();
        if (!BulkEditorialPolicy.Allowed(actor, request.Action!)) return Forbid();
        var results = new List<object>();
        foreach (var item in request.Items)
        {
            var c = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == item.Id);
            if (c == null || !await permissions.CanReadAsync(actor, c))
            {
                results.Add(new { item.Id, title = "Bài không còn trong phạm vi truy cập", eligible = false, reason = "Không tìm thấy hoặc không có quyền xem.", url = (string?)null });
                continue;
            }
            var title = await db.ContentDrafts.Where(x => x.ContentId == c.Id).Select(x => x.Title).FirstOrDefaultAsync()
                ?? await db.Revisions.Where(x => x.Id == c.CurrentRevisionId).Select(x => x.Title).FirstOrDefaultAsync() ?? "Bài viết";
            var reason = BulkEditorialPolicy.Reason(actor, c, request.Action!, await db.PublicationJobs.AnyAsync(x => x.ContentId == c.Id && x.IsActive));
            if (reason == null && (item.RevisionId != c.CurrentRevisionId || item.RowVersion != Convert.ToBase64String(c.RowVersion)))
                reason = "Bài đã thay đổi sau khi chọn; tải lại danh sách và chọn lại.";
            if (reason == null)
            {
                try
                {
                    var requirements = await workflow.InspectRequirementsAsync(c.Id);
                    if (requirements.Count != 0) reason = string.Join(" ", requirements.Values);
                }
                catch (Exception error) when (error is InvalidOperationException or MediaUploadException or IOException)
                { reason = "Chưa kiểm tra được nội dung hoặc tệp đính kèm. Mở bài để đối soát; các bài khác vẫn có thể tiếp tục."; }
            }
            results.Add(new { item.Id, title, eligible = reason == null, reason, url = "/admin/edit/" + c.Id });
        }
        return Json(new { action = request.Action, label = BulkEditorialPolicy.Labels[request.Action!], items = results });
    }

    // One independent request/transaction chain per item: bounded concurrency,
    // fresh rights, CSRF and existing commands; no batch-wide SQL transaction.
    [HttpPost("admin/bulk/execute"), RequestSizeLimit(4096)]
    public async Task<IActionResult> BulkExecute([FromBody] BulkExecuteRequest request)
    {
        if (!ModelState.IsValid || request?.Item == null || request.RequestId == Guid.Empty || request.Item.RevisionId == Guid.Empty ||
            !BulkEditorialPolicy.Labels.ContainsKey(request.Action ?? "")) return BadRequest(new { message = "Thiếu thao tác hoặc phiên bản bài viết." });
        if (!BulkEditorialPolicy.Allowed(await Actor(), request.Action!)) return Forbid();
        using var guard = workflow.RestrictBulkToUnpublishedUnscheduled();
        var model = new WorkflowModel { Action = request.Action!, RevisionId = request.Item.RevisionId, RowVersion = request.Item.RowVersion, RequestId = request.RequestId };
        // Reuse both existing manual endpoints, including combined-action replay
        // and the explicit "approved but not published" partial result.
        return request.Action == "ApproveAndPublish" ? await ApproveAndPublish(request.Item.Id, model) : await ChangeWorkflow(request.Item.Id, model);
    }
}
