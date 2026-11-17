using System.Text.Json;
using AngleSharp.Dom;

namespace LandPortal.WeeklyImport;

public sealed record WebPlanRow(int Stt, string Action, Guid? ContentId, Guid? MediaId, string? Fingerprint);
public sealed record WebPlan(string Origin, string UserId, string ZipSha256, string WorkbookSha256, IReadOnlyList<WebPlanRow> Rows);
public sealed class WebJournalRow
{
    public int Stt { get; set; }
    public Guid? ContentId { get; set; }
    public Guid? MediaId { get; set; }
    public string? Fingerprint { get; set; }
    public Dictionary<string, string[]>? PendingSave { get; set; }
    public string Stage { get; set; } = "New";
    public string? ScanStatus { get; set; }
    public bool CreatedByTool { get; set; }
}
public sealed record WebJournal(string Origin, string UserId, string ZipSha256, string WorkbookSha256, List<WebJournalRow> Rows);
public sealed record WebEditor(Guid Id, string State, bool CanSave, string Fingerprint, IElement Form);

public sealed class WebWeeklyImporter(WebCmsClient cms)
{
    const string Body = "<p>Lịch công tác tuần được đính kèm trong tài liệu PDF.</p>";
    public async Task PreflightAsync()
    {
        await cms.SessionAsync();
        var doc = await cms.HtmlAsync("/admin/create?kind=StaticPage");
        var form = doc.QuerySelector("#editor-form");
        if (form == null || form.GetAttribute("data-can-save") != "true" || form.GetAttribute("data-user-id") != cms.UserId
            || !form.QuerySelectorAll("select[name='CategoryId'] option").Any(o => o.GetAttribute("value") == "76")
            || form.QuerySelector("#editor-document-file") == null || WebCmsClient.Value(form, "Kind") != "StaticPage")
            throw new UnauthorizedAccessException("CMS chưa cho phép tạo StaticPage/chuyên mục 76/tải PDF. Kiểm tra quyền tài khoản hoặc phiên bản CMS; không đổi quyền tự động.");
    }
    public async Task<WebPlan> PlanAsync(WeeklyPackage source)
    {
        await PreflightAsync();
        var rows = new List<WebPlanRow>();
        var inventory = await InventoryAsync();
        foreach (var row in source.Rows)
        {
            if (inventory.Any(x => x.Title == row.Title && x.Trashed)) throw Conflict(row, "Có hồ sơ trong thùng rác; cần cán bộ đối soát, không tạo lại hoặc tự khôi phục.");
            var ids = inventory.Where(x => x.Title == row.Title).Select(x => x.Id).Distinct().ToArray();
            if (ids.Length == 0) { rows.Add(new(row.Stt, "Create", null, null, null)); continue; }
            if (ids.Length != 1) throw Conflict(row, "Có nhiều hồ sơ trùng tiêu đề trong phạm vi được đọc.");
            var editor = await EditorAsync(ids[0], row);
            var asset = await AttachedAsync(editor, row);
            rows.Add(new(row.Stt, "Reuse", editor.Id, asset, editor.Fingerprint));
        }
        return new(cms.Origin.AbsoluteUri, cms.UserId, source.ZipSha256, source.WorkbookSha256, rows);
    }
    public async Task<IReadOnlyList<Guid>> CandidatesAsync(WeeklyRow row)
    {
        var found = (await InventoryAsync(row.Title[..Math.Min(200, row.Title.Length)])).Where(x => x.Title == row.Title).ToArray();
        if (found.Any(x => x.Trashed)) throw Conflict(row, "Có hồ sơ trong thùng rác; không tạo lại.");
        return found.Select(x => x.Id).Distinct().ToArray();
    }
    async Task<IReadOnlyList<(Guid Id, string Title, bool Trashed)>> InventoryAsync(string query = "")
    {
        var items = new Dictionary<Guid,(string Title,bool Trashed)>();
        // Search all accessible kinds/categories, including trash; never hide a title collision.
        foreach (var status in new[] { "", "Archived" })
        {
            for (var page = 1; page <= 1000; page++)
            {
                var doc = await cms.HtmlAsync($"/admin?q={Uri.EscapeDataString(query)}&status={status}&page={page}");
                if (doc.QuerySelector("#admin-status") == null) throw new InvalidDataException("Không nhận diện danh sách Admin; chưa xác minh chống trùng.");
                foreach (var a in doc.QuerySelectorAll("table.admin-table tbody tr td:first-child a[href]"))
                    if (Guid.TryParse(cms.Url(a.GetAttribute("href")!).AbsolutePath.Split('/').Last(), out var id))
                    {
                        var title = a.TextContent.Trim();
                        if (items.TryGetValue(id, out var old) && (old.Title != title || old.Trashed != (status == "Archived"))) throw new InvalidDataException("Bài đổi tiêu đề/trạng thái trong khi đọc danh sách; dừng đối soát.");
                        items[id] = (title, status == "Archived");
                    }
                var next = doc.QuerySelectorAll("nav.pagination a").Any(a => a.TextContent.Contains("Tiếp", StringComparison.Ordinal));
                if (!next) break;
                if (page == 1000) throw new InvalidDataException("Danh sách vượt giới hạn đối soát; dừng thay vì bỏ sót.");
            }
        }
        return items.Select(x => (x.Key, x.Value.Title, x.Value.Trashed)).ToArray();
    }
    public async Task<WebEditor> EditorAsync(Guid id, WeeklyRow row)
    {
        var doc = await cms.HtmlAsync("/admin/edit/" + id);
        var form = doc.QuerySelector("#editor-form") ?? throw Conflict(row, "Không tìm thấy form biên tập.");
        if (WebCmsClient.Value(form, "Id") != id.ToString() || WebCmsClient.Value(form, "Title") != row.Title
            || WebCmsClient.Value(form, "Kind") != "StaticPage" || WebCmsClient.Value(form, "CategoryId") != "76"
            || form.GetAttribute("data-user-id") != cms.UserId)
            throw Conflict(row, "ID, tiêu đề, loại nội dung, chuyên mục hoặc phiên CMS không khớp.");
        var fields = Fields(form);
        return new(id, form.GetAttribute("data-state") ?? "", form.GetAttribute("data-can-save") == "true", Fingerprint(fields), form);
    }
    internal static Dictionary<string, string[]> Fields(IElement form)
    {
        var values = new List<KeyValuePair<string, string>>();
        foreach (var element in form.QuerySelectorAll("input[name],textarea[name],select[name]"))
        {
            var name = element.GetAttribute("name")!; var type = element.GetAttribute("type") ?? "";
            if (name is "__RequestVerificationToken" or "RequestId" || type is "file" or "password" or "submit" || type is "checkbox" or "radio" && !element.HasAttribute("checked")) continue;
            if (element.LocalName == "select")
            {
                var options = element.QuerySelectorAll("option[selected]").ToArray();
                if (options.Length == 0 && !element.HasAttribute("multiple")) options = element.QuerySelectorAll("option").Take(1).ToArray();
                foreach (var option in options) values.Add(new(name, option.GetAttribute("value") ?? option.TextContent));
            }
            else values.Add(new(name, element.LocalName == "textarea" ? element.TextContent : element.GetAttribute("value") ?? ""));
        }
        return values.GroupBy(x => x.Key).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(x => x.Value).ToArray());
    }
    internal static string Fingerprint(Dictionary<string, string[]> fields) => WeeklyPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(fields.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value)));
    public async Task<Guid> AttachedAsync(WebEditor editor, WeeklyRow row)
    {
        var attachments = editor.Form.QuerySelectorAll("input[name='AttachmentIds']").Select(x => Guid.Parse(x.GetAttribute("value")!)).ToArray();
        if (attachments.Length != 1) throw Conflict(row, "Cần đúng một PDF đính kèm; không sửa/gắn lại hồ sơ đã bị thay đổi.");
        var selected = editor.Form.QuerySelectorAll("#selected-attachments li[data-asset]").SingleOrDefault(x => x.GetAttribute("data-asset") == attachments[0].ToString());
        if (selected?.QuerySelector("span")?.TextContent != row.FileName) throw Conflict(row, "Tên PDF đính kèm không khớp.");
        // Generic CMS preview also supports existing legacy attachments. The server
        // checks current resource permissions and media policy before returning bytes.
        var bytes = await cms.BytesAsync($"/admin/media/{attachments[0]}/preview?contentId={editor.Id}&download=true", "application/pdf", 10 * 1024 * 1024,
            expectedRedirect: $"/admin/documents/file/{attachments[0]}?contentId={editor.Id}");
        if (bytes.LongLength != row.Bytes || WeeklyPackage.Hash(bytes) != row.PdfSha256) throw Conflict(row, "PDF đính kèm không khớp byte/SHA256 nguồn.");
        return attachments[0];
    }
    async Task<Guid> VerifyAssetAsync(Guid content, WeeklyRow row, JsonElement asset)
    {
        if (asset.GetProperty("originalName").GetString() != row.FileName || asset.GetProperty("mimeType").GetString() != "application/pdf"
            || asset.GetProperty("byteLength").GetInt64() != row.Bytes || !asset.GetProperty("usable").GetBoolean())
            throw Conflict(row, "Tên/định dạng/dung lượng/chính sách PDF chưa hợp lệ; không giả Clean.");
        var id = asset.GetProperty("id").GetGuid();
        var bytes = await cms.BytesAsync($"/admin/documents/file/{id}?contentId={content}", "application/pdf", 10 * 1024 * 1024);
        if (bytes.LongLength != row.Bytes || WeeklyPackage.Hash(bytes) != row.PdfSha256) throw Conflict(row, "PDF IIS tải về không khớp byte/SHA256 nguồn.");
        return id;
    }
    public void Validate(WeeklyPackage source, WebPlan plan, WebJournal journal)
    {
        if (plan.Origin != cms.Origin.AbsoluteUri || plan.UserId != cms.UserId || plan.ZipSha256 != source.ZipSha256 || plan.WorkbookSha256 != source.WorkbookSha256
            || journal.Origin != plan.Origin || journal.UserId != plan.UserId || journal.ZipSha256 != plan.ZipSha256 || journal.WorkbookSha256 != plan.WorkbookSha256
            || plan.Rows.Count != 43 || !plan.Rows.Select(x => x.Stt).Order().SequenceEqual(Enumerable.Range(1,43))
            || journal.Rows.GroupBy(x => x.Stt).Any(g => g.Count() != 1) || journal.Rows.Any(x => x.Stt is < 1 or > 43)
            || journal.Rows.Where(x => x.ContentId != null).GroupBy(x => x.ContentId).Any(g => g.Count() != 1))
            throw new InvalidDataException("Plan/nhật ký không khớp ZIP, Excel, tài khoản, host hoặc STT. Không nhập.");
    }
    public async Task ImportAsync(WeeklyPackage source, WebPlan plan, WebJournal journal, string journalPath, bool all, DateDecisions? dates = null)
    {
        Validate(source, plan, journal); await PreflightAsync();
        if (all && plan.Rows.Any(x => x.Action == "Create") && !journal.Rows.Any(x => x.Stage == "Verified" && x.CreatedByTool)) throw new InvalidDataException("Chạy nhập thử một hồ sơ và Reconcile thành công trước --all.");
        if (all) await ReconcileAsync(source, plan, journal);
        var pilot = plan.Rows.FirstOrDefault(x => x.Action == "Create")?.Stt ?? 1;
        var selection = all ? source.Rows : source.Rows.Where(x => x.Stt == pilot);
        var confirmed = WeeklyDates.Resolve(source, dates);
        foreach (var row in selection.Where(r => plan.Rows.Single(x => x.Stt == r.Stt).Action == "Create"
            && journal.Rows.All(x => x.Stt != r.Stt || x.Stage == "New")))
            if (!confirmed.ContainsKey(row.Stt)) throw Conflict(row, "Ngày cột B chưa được xác nhận; chạy web-date-report và điền date-decisions.json trước khi tạo bài.");
        foreach (var row in selection)
        {
            var item = journal.Rows.SingleOrDefault(x => x.Stt == row.Stt);
            if (item == null) { item = new() { Stt = row.Stt }; journal.Rows.Add(item); }
            async Task Persist() => await WeeklyImporter.AtomicJson(journalPath, journal);
            if (item.Stage == "Verified") { await VerifyRowAsync(item, row); continue; }
            if (item.Stage == "New")
            {
                var planned = plan.Rows.Single(x => x.Stt == row.Stt); var candidates = await CandidatesAsync(row);
                if (planned.Action == "Reuse")
                {
                    if (candidates.Count != 1 || candidates[0] != planned.ContentId) throw Conflict(row, "Hồ sơ tái sử dụng đã đổi so với Dry-run.");
                    var editor = await EditorAsync(candidates[0], row);
                    if (editor.Fingerprint != planned.Fingerprint || await AttachedAsync(editor, row) != planned.MediaId) throw Conflict(row, "Hồ sơ có sẵn đã đổi; dừng.");
                    item.ContentId = editor.Id; item.MediaId = planned.MediaId; item.Stage = "Verified"; await Persist(); continue;
                }
                if (planned.Action != "Create" || candidates.Count != 0) throw Conflict(row, "Có hồ sơ mới trùng tiêu đề sau Dry-run; chạy đối soát trước.");
                item.PendingSave = new() { ["Kind"] = ["StaticPage"], ["Title"] = [row.Title], ["CategoryId"] = ["76"], ["BodyHtml"] = [Body],
                    ["Metadata.PublicationDate"] = [confirmed[row.Stt]],
                    ["RequestId"] = [WeeklyPackage.Command(row, "web-create").ToString()], ["intent"] = ["Save"] };
                item.Stage = "Creating"; await Persist();
            }
            if (item.Stage == "Creating")
            {
                var result = await cms.SaveAsync(item.PendingSave ?? throw Conflict(row, "Thiếu lệnh tạo nháp để đối soát."));
                var id = result.GetProperty("contentId").GetGuid(); var editor = await EditorAsync(id, row);
                var history = await cms.HtmlAsync($"/admin/history/{id}");
                if (!editor.CanSave || editor.State != "Draft" || WebCmsClient.Value(editor.Form,"BodyHtml") != Body
                    || editor.Form.QuerySelector("input[name='AttachmentIds']") != null || result.GetProperty("isPublic").GetBoolean()
                    || result.GetProperty("isTrashed").GetBoolean() || history.QuerySelectorAll("table.revision-table tbody tr").Length != 1)
                    throw Conflict(row, "Bản nháp đã đổi sau lệnh tạo; không ghi đè.");
                item.ContentId = id; item.CreatedByTool = true; item.Fingerprint = editor.Fingerprint; item.PendingSave = null; item.Stage = "Uploading"; await Persist();
            }
            if (item.Stage == "Uploading")
            {
                var editor = await EditorAsync(item.ContentId!.Value, row);
                if (!editor.CanSave || editor.State != "Draft" || editor.Fingerprint != item.Fingerprint) throw Conflict(row, "Bản nháp đã được chỉnh sửa/chuyển trạng thái; dừng trước upload/gắn PDF.");
                // Explicit resume checks the CMS upload receipt before sending any bytes again.
                var asset = await cms.FindUploadAsync(editor.Id, row.PdfSha256) ?? await cms.UploadAsync(editor.Id, row, source.PdfBytes[row.Stt]);
                item.MediaId = await VerifyAssetAsync(editor.Id, row, asset); item.ScanStatus = asset.GetProperty("scanStatus").GetString();
                var fields = Fields(editor.Form); fields["AttachmentIds"] = [item.MediaId.Value.ToString()]; fields[$"AttachmentLabels[{item.MediaId}]"] = [row.FileName];
                fields["RequestId"] = [WeeklyPackage.Command(row, "web-attach").ToString()]; fields["intent"] = ["Save"];
                item.PendingSave = fields; item.Stage = "Attaching"; await Persist();
            }
            if (item.Stage == "Attaching")
            {
                // Keep the exact original row version/payload on replay. A new payload would overwrite later human edits.
                var result = await cms.SaveAsync(item.PendingSave ?? throw Conflict(row, "Thiếu lệnh gắn PDF gốc."));
                if (result.GetProperty("contentId").GetGuid() != item.ContentId) throw Conflict(row, "CMS trả sai hồ sơ.");
                await VerifyRowAsync(item, row); item.PendingSave = null; item.Stage = "Verified"; await Persist();
            }
            if (item.Stage != "Verified") throw Conflict(row, "Nhật ký có trạng thái không được hỗ trợ.");
        }
    }
    async Task VerifyRowAsync(WebJournalRow item, WeeklyRow row)
    {
        if (item.ContentId == null || item.MediaId == null || await AttachedAsync(await EditorAsync(item.ContentId.Value, row), row) != item.MediaId)
            throw Conflict(row, "Mapping hoặc PDF đã đổi/bị gỡ; không tự gắn lại.");
    }
    public async Task ReconcileAsync(WeeklyPackage source, WebPlan plan, WebJournal journal)
    {
        Validate(source, plan, journal);
        foreach (var item in journal.Rows.Where(x => x.Stage == "Verified")) await VerifyRowAsync(item, source.Rows.Single(x => x.Stt == item.Stt));
    }
    internal async Task<Guid?> PublishedAsync(WebJournalRow item, WeeklyRow row)
    {
        var history = await cms.HtmlAsync($"/admin/history/{item.ContentId}");
        if (history.QuerySelector("#history-form")?.GetAttribute("data-content-id") != item.ContentId.ToString()) throw Conflict(row, "Không nhận diện được lịch sử CMS.");
        var live = history.QuerySelectorAll("table.revision-table tbody tr").Where(x => x.QuerySelector(".public-badge")?.TextContent.Trim() == "Công khai").ToArray();
        if (live.Length == 0) return null;
        if (live.Length != 1) throw Conflict(row, "Có nhiều phiên bản công khai trong lịch sử.");
        var raw = live[0].QuerySelector("[data-compare]")?.GetAttribute("data-compare");
        if (!Guid.TryParse(raw, out var revision)) throw Conflict(row, "Không xác định đúng phiên bản đã xuất bản.");
        var preview = await cms.HtmlAsync($"/admin/preview/{item.ContentId}?revisionId={revision}");
        // The editor confirms the category ID; compare the actual database category
        // label on both previews (not a guessed public slug or a configured caption).
        await EditorAsync(item.ContentId!.Value, row);
        var currentPreview = await cms.HtmlAsync($"/admin/preview/{item.ContentId}");
        var category = currentPreview.QuerySelector("article header .eyebrow")?.TextContent.Trim();
        if (preview.QuerySelector("article h1")?.TextContent.Trim() != row.Title
            || string.IsNullOrWhiteSpace(category) || preview.QuerySelector("article header .eyebrow")?.TextContent.Trim() != category
            || !preview.QuerySelectorAll(".attachments a[href]").Any(a => a.TextContent == row.FileName && cms.Url(a.GetAttribute("href")!).AbsolutePath == $"/admin/media/{item.MediaId}/preview")) return null;
        return revision;
    }
    public async Task<IReadOnlyList<LinkResult>> SyncAsync(WeeklyPackage source, WebPlan plan, WebJournal journal)
    {
        Validate(source, plan, journal); var results = new List<LinkResult>();
        foreach (var row in source.Rows)
        {
            var item = journal.Rows.SingleOrDefault(x => x.Stt == row.Stt && x.Stage == "Verified");
            var revision = item == null ? null : await PublishedAsync(item, row);
            if (revision == null) { results.Add(new(row.Stt, item?.ContentId, null, null, "Chưa công khai đúng phiên bản/PDF")); continue; }
            try
            {
                var path = "/bai-viet/" + item!.ContentId; var page = await cms.HtmlAsync(path, true);
                if (page.QuerySelector("h1")?.TextContent.Trim() != row.Title || !page.QuerySelectorAll("a[href]").Any(a =>
                    Uri.TryCreate(cms.Origin, a.GetAttribute("href"), out var u) && u.GetLeftPart(UriPartial.Authority) == cms.Origin.GetLeftPart(UriPartial.Authority) && u.AbsolutePath == "/media/" + item.MediaId))
                    throw Conflict(row, "Trang công khai chưa khớp tiêu đề/PDF.");
                var bytes = await cms.BytesAsync($"/media/{item.MediaId}?download=true&contentId={item.ContentId}", "application/pdf", 10 * 1024 * 1024, true);
                if (bytes.LongLength != row.Bytes || WeeklyPackage.Hash(bytes) != row.PdfSha256 || await PublishedAsync(item, row) != revision)
                    throw Conflict(row, "PDF hoặc phiên bản công khai đã đổi.");
                results.Add(new(row.Stt, item.ContentId, revision, cms.Url(path).AbsoluteUri, "PublishedVerified"));
            }
            catch (Exception e) when (e is InvalidDataException or HttpRequestException or TaskCanceledException)
            { results.Add(new(row.Stt, item!.ContentId, revision, null, "Chưa xác minh được trang/PDF công khai; không ghi link")); }
        }
        for (var i = 0; i < results.Count; i++)
            if (results[i].Url != null && await PublishedAsync(journal.Rows.Single(x => x.Stt == results[i].Stt), source.Rows.Single(x => x.Stt == results[i].Stt)) != results[i].PublishedRevisionId)
                results[i] = results[i] with { Url = null, Status = "Đã đổi phiên bản trước khi ghi Excel" };
        return results;
    }
    static InvalidDataException Conflict(WeeklyRow row, string message) => new($"STT {row.Stt}: {message}");
}
