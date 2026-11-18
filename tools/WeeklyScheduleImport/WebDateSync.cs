using System.Text.Json;
using AngleSharp.Dom;

namespace LandPortal.WeeklyImport;

public sealed record DateSnapshot(string State, Guid RevisionId, Guid? PublicRevisionId, string Fingerprint,
    Dictionary<string, string[]> Fields, JsonElement Metadata);
public sealed record DatePlanRow(int Stt, Guid? ContentId, Guid? MediaId, string? Date, string Status, DateSnapshot? Before);
public sealed record WebDatePlan(string Origin, string UserId, string ZipSha256, string WorkbookSha256, string DatesHash, IReadOnlyList<DatePlanRow> Rows);
public sealed class DateChange
{
    public int Stt { get; set; }
    public string Stage { get; set; } = "Planned";
    public Guid BeginCommand { get; set; } = Guid.NewGuid();
    public Guid SaveCommand { get; set; } = Guid.NewGuid();
    public Dictionary<string, string[]>? PendingSave { get; set; }
    public Guid? SavedRevisionId { get; set; }
}
public sealed record WebDateJournal(string PlanHash, List<DateChange> Rows);

// Uses only existing CMS forms/history/commands. Never uploads, submits or publishes.
public sealed class WebDateSync(WebCmsClient cms)
{
    const string DateField = "Metadata.PublicationDate";
    readonly WebWeeklyImporter importer = new(cms);
    static string Get(Dictionary<string, string[]> fields, string key) => fields.TryGetValue(key, out var v) ? v.FirstOrDefault() ?? "" : "";
    static string DatesHash(IReadOnlyDictionary<int, string> dates) => WeeklyPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(dates.OrderBy(x => x.Key)));
    internal static void ValidateDateChoices(WeeklyPackage source, DateDecisions? decisions, WebDatePlan plan)
    {
        var dates = WeeklyDates.Resolve(source, decisions);
        if (plan.DatesHash != DatesHash(dates) || plan.Rows.Any(x => x.Date != dates.GetValueOrDefault(x.Stt)))
            throw new InvalidDataException("Ngày trong plan không khớp các ngày đã xác nhận; không ghi CMS.");
    }
    static InvalidDataException Conflict(int stt, string message) => new($"STT {stt}: {message}");
    static bool SameFields(Dictionary<string, string[]> a, Dictionary<string, string[]> b) => WebWeeklyImporter.Fingerprint(a) == WebWeeklyImporter.Fingerprint(b);
    static Dictionary<string, string[]> Stable(Dictionary<string, string[]> fields) => fields.Where(x => x.Key is not ("RowVersion" or "CurrentRevisionId" or "RequestId" or "intent") && !x.Key.StartsWith("Metadata.", StringComparison.Ordinal)).ToDictionary(x => x.Key, x => x.Value);
    static Dictionary<string, string[]> MetadataFields(JsonElement metadata) => metadata.EnumerateObject().ToDictionary(
        x => "Metadata." + char.ToUpperInvariant(x.Name[0]) + x.Name[1..], x => new[] { x.Value.ValueKind == JsonValueKind.Null ? "" : x.Value.ToString() });
    static string MetadataHash(JsonElement metadata, bool ignoreDate = false) => WebWeeklyImporter.Fingerprint(MetadataFields(metadata)
        .Where(x => !ignoreDate || x.Key != DateField).ToDictionary(x => x.Key, x => x.Value));

    async Task<(Guid? Public, IDocument History)> HistoryAsync(Guid id, string version)
    {
        var doc = await cms.HtmlAsync($"/admin/history/{id}"); var form = doc.QuerySelector("#history-form");
        if (form?.GetAttribute("data-content-id") != id.ToString() || WebCmsClient.Value(form, "RowVersion") != version)
            throw new InvalidDataException("Phiên bản đổi trong lúc đọc lịch sử; chạy lại kế hoạch chỉ đọc.");
        var live = doc.QuerySelectorAll("table.revision-table tbody tr").Where(x => x.QuerySelector(".public-badge")?.TextContent.Trim() == "Công khai").ToArray();
        if (live.Length > 1) throw new InvalidDataException("Nhiều phiên bản công khai; dừng.");
        Guid? published = live.Length == 0 ? null : Guid.Parse(live[0].QuerySelector("[data-compare]")!.GetAttribute("data-compare")!);
        return (published, doc);
    }
    async Task<JsonElement> MetadataAsync(Guid id, Guid revision)
        => (await cms.JsonAsync($"/admin/history/{id}/compare?revisionId={revision}")).GetProperty("after").GetProperty("metadata").Clone();

    async Task<DateSnapshot> SnapshotAsync(WeeklyRow row, WebJournalRow item)
    {
        var editor = await importer.EditorAsync(item.ContentId!.Value, row);
        if (await importer.AttachedAsync(editor, row) != item.MediaId) throw Conflict(row.Stt, "PDF khác nhật ký; không sửa.");
        var fields = WebWeeklyImporter.Fields(editor.Form);
        if (!fields.ContainsKey(DateField) || !Guid.TryParse(Get(fields, "CurrentRevisionId"), out var revision)) throw Conflict(row.Stt, "CMS thiếu trường ngày hoặc revision.");
        if (editor.State is not ("Draft" or "Rejected" or "Published")
            || editor.State is "Draft" or "Rejected" && !editor.CanSave
            || editor.State == "Published" && editor.Form.QuerySelector("[data-workflow='StartDraft']") == null)
            throw Conflict(row.Stt, "Không có quyền sửa hoặc bài đang duyệt/hẹn giờ; không tự đổi workflow.");
        var (published, history) = await HistoryAsync(editor.Id, Get(fields, "RowVersion"));
        // A revision snapshot cannot describe unsnapshotted autosaves. Do not guess
        // hidden metadata from a stale revision or overwrite a colleague's draft.
        var latest = history.QuerySelector(".history-audit tbody tr")?.QuerySelectorAll("td").ElementAtOrDefault(2)?.TextContent.Trim();
        if (latest is not ("Lưu bản nháp" or "Mở bản sửa" or "Xuất bản bài đã duyệt" or "Xuất bản trực tiếp" or "Trả lại bài"))
            throw Conflict(row.Stt, "Chưa chứng minh bản nháp khớp revision (có thể có tự lưu). Cán bộ lưu nháp rồi lập lại kế hoạch; không tự lưu hộ.");
        var metadata = await MetadataAsync(editor.Id, revision);
        var full = MetadataFields(metadata);
        if (Get(full, "Metadata.MetadataOrigin") != "Editorial" || Get(full, "Metadata.WeeklyYear") != "")
            throw Conflict(row.Stt, "Hồ sơ đã đổi sang nguồn/lịch có cấu trúc; cần đối soát riêng để không đổi metadata hoặc tạo WeeklyScheduleRelease.");
        foreach (var field in fields.Where(x => x.Key.StartsWith("Metadata.", StringComparison.Ordinal)))
            if (Get(full, field.Key) != Get(fields, field.Key)) throw Conflict(row.Stt, "Metadata bản nháp khác revision; không ghi đè.");
        if ((await importer.EditorAsync(editor.Id, row)).Fingerprint != editor.Fingerprint) throw Conflict(row.Stt, "Bài đổi trong lúc lập kế hoạch.");
        return new(editor.State, revision, published, editor.Fingerprint, fields, metadata);
    }

    public async Task<WebDatePlan> PlanAsync(WeeklyPackage source, WebPlan importPlan, WebJournal journal, DateDecisions? decisions)
    {
        importer.Validate(source, importPlan, journal); var dates = WeeklyDates.Resolve(source, decisions); var rows = new List<DatePlanRow>();
        foreach (var row in source.Rows)
        {
            var item = journal.Rows.SingleOrDefault(x => x.Stt == row.Stt);
            if (!dates.TryGetValue(row.Stt, out var date)) { rows.Add(new(row.Stt, item?.ContentId, item?.MediaId, null, "NeedsConfirmation", null)); continue; }
            if (item is not { Stage: "Verified", ContentId: not null, MediaId: not null }) { rows.Add(new(row.Stt, item?.ContentId, item?.MediaId, date, "ImportNotVerified", null)); continue; }
            var before = await SnapshotAsync(row, item);
            rows.Add(new(row.Stt, item.ContentId, item.MediaId, date, Get(before.Fields, DateField) == date ? "AlreadySet" : "Ready", before));
        }
        return new(cms.Origin.AbsoluteUri, cms.UserId, source.ZipSha256, source.WorkbookSha256, DatesHash(dates), rows);
    }

    public async Task ApplyAsync(WeeklyPackage source, WebPlan importPlan, WebJournal imported, DateDecisions? decisions,
        WebDatePlan plan, WebDateJournal journal, string journalPath, int? onlyStt = null)
    {
        importer.Validate(source, importPlan, imported);
        ValidateDateChoices(source, decisions, plan);
        var planHash = WeeklyPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(plan, WeeklyPackage.Json));
        if (plan.Origin != cms.Origin.AbsoluteUri || plan.UserId != cms.UserId || plan.ZipSha256 != source.ZipSha256 || plan.WorkbookSha256 != source.WorkbookSha256
            || plan.DatesHash != DatesHash(WeeklyDates.Resolve(source, decisions)) || planHash != journal.PlanHash || plan.Rows.Count != 43
            || !plan.Rows.Select(x => x.Stt).Order().SequenceEqual(Enumerable.Range(1, 43)) || journal.Rows.GroupBy(x => x.Stt).Any(x => x.Count() != 1)
            || journal.Rows.Any(x => x.Stt is < 1 or > 43) || onlyStt is < 1 or > 43)
            throw new InvalidDataException("Plan ngày/nguồn/xác nhận/tài khoản/nhật ký không khớp; không ghi CMS.");
        foreach (var rowPlan in plan.Rows.Where(x => onlyStt == null || x.Stt == onlyStt))
        {
            if (rowPlan.Status is "NeedsConfirmation" or "ImportNotVerified") continue;
            if (rowPlan.Status is not ("Ready" or "AlreadySet") || rowPlan.Before == null || rowPlan.Date == null) throw Conflict(rowPlan.Stt, "Plan ngày không hợp lệ.");
            var row = source.Rows.Single(x => x.Stt == rowPlan.Stt); var item = imported.Rows.Single(x => x.Stt == row.Stt);
            if (item.Stage != "Verified" || item.ContentId != rowPlan.ContentId || item.MediaId != rowPlan.MediaId) throw Conflict(row.Stt, "Mapping gốc đã đổi.");
            var change = journal.Rows.SingleOrDefault(x => x.Stt == row.Stt);
            if (change == null) { change = new() { Stt = row.Stt }; journal.Rows.Add(change); await Persist(); }
            async Task Persist() => await WeeklyImporter.AtomicJson(journalPath, journal);
            var current = await SnapshotAsync(row, item);
            if (change.Stage == "Verified")
            {
                if (Get(current.Fields, DateField) != rowPlan.Date || current.RevisionId != change.SavedRevisionId) throw Conflict(row.Stt, "Bản đã sửa sau đối soát; không tự áp lại ngày.");
                continue;
            }
            if (change.Stage == "Planned")
            {
                if (current.Fingerprint != rowPlan.Before.Fingerprint || current.State != rowPlan.Before.State || current.PublicRevisionId != rowPlan.Before.PublicRevisionId
                    || MetadataHash(current.Metadata) != MetadataHash(rowPlan.Before.Metadata)) throw Conflict(row.Stt, "Bài thay đổi sau kế hoạch; dừng, giữ sửa đổi của cán bộ.");
                if (rowPlan.Status == "AlreadySet") { change.Stage = "Verified"; change.SavedRevisionId = current.RevisionId; await Persist(); continue; }
                change.Stage = current.State == "Published" ? "Beginning" : "Saving";
                if (change.Stage == "Saving") PrepareSave(current);
                await Persist();
            }
            if (change.Stage == "Beginning")
            {
                var version = Get(rowPlan.Before.Fields, "RowVersion");
                if (Get(current.Fields, "RowVersion") == version)
                    await cms.BeginEditAsync(item.ContentId!.Value, version, change.BeginCommand);
                else if (!await HasCommandAsync(item.ContentId!.Value, change.BeginCommand, current)) throw Conflict(row.Stt, "Bài đã đổi trước lệnh mở bản sửa.");
                current = await SnapshotAsync(row, item);
                if (!await HasCommandAsync(item.ContentId!.Value, change.BeginCommand, current) || current.State != "Draft" || current.RevisionId != rowPlan.Before.RevisionId || current.PublicRevisionId != rowPlan.Before.PublicRevisionId
                    || !SameFields(Stable(current.Fields), Stable(rowPlan.Before.Fields)) || MetadataHash(current.Metadata) != MetadataHash(rowPlan.Before.Metadata))
                    throw Conflict(row.Stt, "Bản sửa không còn khớp sau BeginEdit; không ghi ngày.");
                PrepareSave(current); change.Stage = "Saving"; await Persist();
            }
            if (change.Stage == "Saving")
            {
                var payload = change.PendingSave ?? throw Conflict(row.Stt, "Thiếu payload lưu gốc.");
                if (Get(payload, "Id") != item.ContentId.ToString() || Get(payload, DateField) != rowPlan.Date || Get(payload, "intent") != "Save"
                    || Get(payload, "RequestId") != change.SaveCommand.ToString() || !SameFields(Stable(payload), Stable(rowPlan.Before.Fields)))
                    throw Conflict(row.Stt, "Payload không còn là cập nhật ngày duy nhất.");
                var expectedMetadata = MetadataFields(rowPlan.Before.Metadata); expectedMetadata[DateField] = [rowPlan.Date];
                if (!SameFields(payload.Where(x => x.Key.StartsWith("Metadata.", StringComparison.Ordinal)).ToDictionary(x => x.Key, x => x.Value), expectedMetadata))
                    throw Conflict(row.Stt, "Payload đã đổi trường metadata ngoài ngày.");
                current = await SnapshotAsync(row, item);
                if (Get(current.Fields, "RowVersion") == Get(payload, "RowVersion")) await cms.SaveAsync(payload);
                else if (!await HasCommandAsync(item.ContentId!.Value, change.SaveCommand, current)) throw Conflict(row.Stt, "Bản nháp có sửa đổi mới; không ghi đè hoặc tạo command khác.");
                current = await SnapshotAsync(row, item);
                if (!await HasCommandAsync(item.ContentId!.Value, change.SaveCommand, current) || current.State != "Draft" || current.PublicRevisionId != rowPlan.Before.PublicRevisionId || Get(current.Fields, DateField) != rowPlan.Date
                    || !SameFields(Stable(current.Fields), Stable(rowPlan.Before.Fields)) || MetadataHash(current.Metadata, true) != MetadataHash(rowPlan.Before.Metadata, true))
                    throw Conflict(row.Stt, "Đối soát bản sửa chưa đạt; dừng, không duyệt/xuất bản.");
                change.SavedRevisionId = current.RevisionId; change.Stage = "Verified"; change.PendingSave = null; await Persist();
            }
            if (change.Stage != "Verified") throw Conflict(row.Stt, "Trạng thái nhật ký ngày không hợp lệ.");
            void PrepareSave(DateSnapshot snapshot)
            {
                var fields = new Dictionary<string, string[]>(snapshot.Fields);
                foreach (var field in MetadataFields(snapshot.Metadata)) fields[field.Key] = field.Value;
                fields[DateField] = [rowPlan.Date]; fields["RequestId"] = [change.SaveCommand.ToString()]; fields["intent"] = ["Save"];
                change.PendingSave = fields;
            }
        }
    }
    async Task<bool> HasCommandAsync(Guid id, Guid command, DateSnapshot snapshot)
    {
        var (_, history) = await HistoryAsync(id, Get(snapshot.Fields, "RowVersion"));
        // Only the newest audit command may authorize reconciliation; never treat
        // an older receipt as permission to overwrite subsequent human changes.
        return history.QuerySelector(".history-audit tbody tr")?.QuerySelectorAll("td").LastOrDefault()?.TextContent.Trim() == "command=" + command;
    }

    public async Task<object[]> VerifyPublicAsync(WeeklyPackage source, WebPlan plan, WebJournal journal, DateDecisions? decisions)
    {
        var dates = WeeklyDates.Resolve(source, decisions); var links = await importer.SyncAsync(source, plan, journal); var result = new List<object>();
        var archive = new Dictionary<Guid, string>();
        for (var page = 1; page <= 1000; page++)
        {
            var doc = await cms.HtmlAsync($"/lich-tuan?page={page}", true);
            foreach (var li in doc.QuerySelectorAll(".weekly-articles li"))
                if (Guid.TryParse(li.QuerySelector("h3 a")?.GetAttribute("href")?.Split('/').Last(), out var id))
                    archive[id] = li.QuerySelector(".weekly-article-meta span")?.TextContent.Trim() ?? "";
            if (!doc.QuerySelectorAll(".weekly-navigation a").Any(x => x.TextContent.Contains("Trang sau", StringComparison.Ordinal))) break;
            if (page == 1000) throw new InvalidDataException("Danh sách lịch vượt giới hạn kiểm tra; chưa xác minh hết.");
        }
        foreach (var link in links)
        {
            var status = "Chưa công khai/không đủ điều kiện";
            if (!dates.TryGetValue(link.Stt, out var date)) status = "Ngày chưa được xác nhận";
            else if (link.Url != null)
            {
                var metadata = await MetadataAsync(link.ContentId!.Value, link.PublishedRevisionId!.Value);
                var page = await cms.HtmlAsync("/bai-viet/" + link.ContentId, true); var display = WeeklyDates.ParseIso(date).ToString("dd/MM/yyyy");
                var sameRevision = await importer.PublishedAsync(journal.Rows.Single(x => x.Stt == link.Stt), source.Rows.Single(x => x.Stt == link.Stt)) == link.PublishedRevisionId;
                status = Get(MetadataFields(metadata), DateField) == date && page.QuerySelector(".article-meta")?.TextContent.Trim().StartsWith(display, StringComparison.Ordinal) == true
                    && archive.GetValueOrDefault(link.ContentId.Value) == "Ngày đăng: " + display && sameRevision ? "PublishedDateVerified" : "Chưa công khai đúng ngày ở cả hai trang";
            }
            result.Add(new { link.Stt, link.ContentId, link.PublishedRevisionId, ExpectedDate = date, Status = status });
        }
        return result.ToArray();
    }
}
