using System.Globalization;
using System.Text.Json;
using AngleSharp.Dom;

namespace LandPortal.WeeklyImport;

public sealed record PlanningPlanRow(int Stt, string Action, string? Date, Guid? ContentId, IReadOnlyDictionary<string,Guid> Assets, string? Fingerprint, string[] Reasons);
public sealed record PlanningPlan(string Profile, string SheetName, string Origin, string UserId, string ZipSha256, string WorkbookSha256, IReadOnlyList<PlanningPlanRow> Rows)
{
    public string SourceScopeSha256 { get; init; } = "";
}
public sealed class PlanningJournalRow
{
    public int Stt { get; set; }
    public Guid? ContentId { get; set; }
    public Dictionary<string,Guid> Assets { get; set; } = [];
    public string Stage { get; set; } = "New";
    public bool Created { get; set; }
    public string? Fingerprint { get; set; }
    public Dictionary<string,string[]>? PendingSave { get; set; }
}
public sealed record PlanningJournal(string PlanSha256, List<PlanningJournalRow> Rows);

public sealed class PlanningWebImporter(WebCmsClient cms)
{
    readonly WebWeeklyImporter common = new(cms, PlanningPackage.Category);
    const string Body = "<p>Tài liệu quy hoạch, kế hoạch sử dụng đất được đính kèm bên dưới.</p>";
    public Task PreflightAsync() => common.PreflightAsync();
    public static string PlanHash(PlanningPlan plan) => WeeklyPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(plan, WeeklyPackage.Json));
    static InvalidDataException Conflict(int stt, string reason) => new($"STT {stt}: {reason}");

    public async Task<PlanningPlan> PlanAsync(PlanningPackage source, DateDecisions? decisions)
    {
        await PreflightAsync(); var dates = WeeklyDates.Resolve(source.Dates, decisions); var rows = new List<PlanningPlanRow>();
        foreach (var row in source.Rows)
        {
            var reasons = row.Blocks.Concat(row.Files.SelectMany(x => x.Blocks.Select(b => x.Name + ": " + b))).ToList();
            if (!dates.ContainsKey(row.Stt)) reasons.Add("Ngày cột B cần xác nhận; không tạo bài thiếu ngày.");
            if (reasons.Count != 0) { rows.Add(new(row.Stt, "Blocked", dates.GetValueOrDefault(row.Stt), null, new Dictionary<string,Guid>(), null, reasons.ToArray())); continue; }
            var ids = await common.CandidatesAsync(row.Source);
            if (ids.Count == 0) { rows.Add(new(row.Stt, "Create", dates[row.Stt], null, new Dictionary<string,Guid>(), null, [])); continue; }
            if (ids.Count != 1) { rows.Add(new(row.Stt, "Blocked", dates[row.Stt], null, new Dictionary<string,Guid>(), null, ["Nhiều bài trùng tiêu đề; cần đối soát."])); continue; }
            try
            {
                var editor = await common.EditorAsync(ids[0], row.Source);
                var assets = await AttachedAsync(editor, row);
                if (WebCmsClient.Value(editor.Form, "Metadata.PublicationDate") != dates[row.Stt]) throw Conflict(row.Stt, "Bài có sẵn khác ngày đã xác nhận; không ghi đè hoặc tạo bản sao.");
                rows.Add(new(row.Stt, "Reuse", dates[row.Stt], editor.Id, assets, editor.Fingerprint, []));
            }
            catch (InvalidDataException e) { rows.Add(new(row.Stt, "Blocked", dates[row.Stt], ids[0], new Dictionary<string,Guid>(), null, [e.Message])); }
        }
        return new(PlanningPackage.Profile, source.Dates.SheetName, cms.Origin.AbsoluteUri, cms.UserId, source.Dates.ZipSha256, source.Dates.WorkbookSha256, rows) { SourceScopeSha256 = source.ScopeSha256 };
    }
    public void Validate(PlanningPackage source, PlanningPlan plan, PlanningJournal journal, DateDecisions? decisions)
    {
        if (plan.Profile != PlanningPackage.Profile || plan.SheetName != source.Dates.SheetName || plan.Origin != cms.Origin.AbsoluteUri || plan.UserId != cms.UserId
            || plan.ZipSha256 != source.Dates.ZipSha256 || plan.WorkbookSha256 != source.Dates.WorkbookSha256 || journal.PlanSha256 != PlanHash(plan)
            || plan.SourceScopeSha256 != source.ScopeSha256
            || !plan.Rows.Select(x => x.Stt).Order().SequenceEqual(source.Rows.Select(x => x.Stt).Order())
            || journal.Rows.GroupBy(x => x.Stt).Any(g => g.Count() != 1) || journal.Rows.Any(x => !source.Rows.Any(r => r.Stt == x.Stt))
            || journal.Rows.Where(x => x.ContentId != null).GroupBy(x => x.ContentId).Any(g => g.Count() != 1))
            throw new InvalidDataException("Plan/nhật ký QHKHSDD không khớp sheet, nguồn, host, tài khoản hoặc STT; không dùng lại nhật ký lịch tuần.");
        var dates = WeeklyDates.Resolve(source.Dates, decisions);
        foreach (var item in plan.Rows.Where(x => x.Action != "Blocked"))
            if (item.Action is not ("Create" or "Reuse") || !source.Rows.Single(r => r.Stt == item.Stt).CanImport || item.Date == null || dates.GetValueOrDefault(item.Stt) != item.Date)
                throw Conflict(item.Stt, "Ngày hoặc điều kiện tệp thay đổi sau kế hoạch; dừng, không ghi.");
    }
    async Task<Dictionary<string,Guid>> AttachedAsync(WebEditor editor, PlanningRow row)
    {
        var ids = editor.Form.QuerySelectorAll("input[name='AttachmentIds']").Select(x => Guid.Parse(x.GetAttribute("value")!)).ToArray();
        if (ids.Distinct().Count() != ids.Length) throw Conflict(row.Stt, "Tệp đính kèm bị lặp; không tự sửa mapping.");
        var supplementary = await PlanningZipSupplement.VerifyExtraAsync(cms, editor, row, ids);
        if (ids.Length - (supplementary.HasValue ? 1 : 0) != row.Files.Count) throw Conflict(row.Stt, "Số PDF khác nguồn; không tự gắn lại tệp bị gỡ.");
        var map = new Dictionary<string,Guid>();
        foreach (var file in row.Files)
        {
            var matches = editor.Form.QuerySelectorAll("#selected-attachments li[data-asset]").Where(x => x.QuerySelector("span")?.TextContent == file.Name).ToArray();
            if (matches.Length != 1 || !Guid.TryParse(matches[0].GetAttribute("data-asset"), out var id) || !ids.Contains(id)) throw Conflict(row.Stt, "Tên/ID PDF không khớp " + file.Name);
            var bytes = await cms.BytesAsync($"/admin/media/{id}/preview?contentId={editor.Id}&download=true", "application/pdf", LandPortal.Infrastructure.EditorialDocumentPolicy.PdfMaximum,
                expectedRedirect: $"/admin/documents/file/{id}?contentId={editor.Id}");
            if (bytes.LongLength != file.Bytes || WeeklyPackage.Hash(bytes) != file.Sha256) throw Conflict(row.Stt, "Byte/hash khác nguồn: " + file.Name);
            map.Add(file.Entry, id);
        }
        if (map.Values.Distinct().Count() != row.Files.Count || supplementary is Guid extra && map.Values.Contains(extra)) throw Conflict(row.Stt, "Tệp bị gắn trùng.");
        return map;
    }
    static bool SameAssets(IReadOnlyDictionary<string,Guid> a, IReadOnlyDictionary<string,Guid> b) => a.Count == b.Count && a.All(x => b.TryGetValue(x.Key, out var id) && id == x.Value);
    async Task VerifyAsync(PlanningRow row, PlanningJournalRow item, string date)
    {
        if (item.ContentId == null) throw Conflict(row.Stt, "Thiếu ContentId.");
        var editor = await common.EditorAsync(item.ContentId.Value, row.Source);
        if (WebCmsClient.Value(editor.Form, "Metadata.PublicationDate") != date || !SameAssets(await AttachedAsync(editor, row), item.Assets))
            throw Conflict(row.Stt, "Ngày hoặc mapping đính kèm đã đổi; không ghi đè chỉnh sửa cán bộ.");
    }
    public async Task ImportAsync(PlanningPackage source, PlanningPlan plan, PlanningJournal journal, string journalPath, DateDecisions? decisions, bool all, int? stt = null)
    {
        Validate(source, plan, journal, decisions); await PreflightAsync();
        if (all && stt != null) throw new ArgumentException("Chỉ chọn --all hoặc --stt.");
        if (all && plan.Rows.Any(x => x.Action == "Create") && !journal.Rows.Any(x => x.Created && x.Stage == "Verified")) throw new InvalidDataException("Nhập thử một hồ sơ đủ điều kiện và Reconcile trước --all.");
        await ReconcileAsync(source, plan, journal, decisions);
        var pilot = stt ?? plan.Rows.FirstOrDefault(x => x.Action == "Create")?.Stt ?? plan.Rows.FirstOrDefault(x => x.Action == "Reuse")?.Stt;
        if (!all && (pilot == null || plan.Rows.SingleOrDefault(x => x.Stt == pilot)?.Action is not ("Create" or "Reuse"))) throw new InvalidDataException("STT chọn chưa đủ điều kiện nhập; xem kế hoạch.");
        var selection = plan.Rows.Where(x => x.Action != "Blocked" && (all || x.Stt == pilot));
        foreach (var p in selection)
        {
            var row = source.Rows.Single(x => x.Stt == p.Stt);
            var item = journal.Rows.SingleOrDefault(x => x.Stt == p.Stt);
            if (item == null) { item = new() { Stt = p.Stt }; journal.Rows.Add(item); }
            async Task Persist() => await WeeklyImporter.AtomicJson(journalPath, journal);
            if (item.Stage == "Verified") { await VerifyAsync(row, item, p.Date!); continue; }
            if (item.Stage == "New")
            {
                var candidates = await common.CandidatesAsync(row.Source);
                if (p.Action == "Reuse")
                {
                    if (candidates.Count != 1 || candidates[0] != p.ContentId) throw Conflict(row.Stt, "Bài tái sử dụng khác kế hoạch.");
                    var old = await common.EditorAsync(p.ContentId!.Value, row.Source);
                    if (old.Fingerprint != p.Fingerprint || !SameAssets(await AttachedAsync(old, row), p.Assets)) throw Conflict(row.Stt, "Bài đã đổi sau kế hoạch.");
                    item.ContentId = p.ContentId; item.Assets = p.Assets.ToDictionary(); item.Stage = "Verified"; await VerifyAsync(row, item, p.Date!); await Persist(); continue;
                }
                if (candidates.Count != 0) throw Conflict(row.Stt, "Có bài trùng sau kế hoạch; dừng.");
                item.PendingSave = new() { ["Kind"] = ["StaticPage"], ["CategoryId"] = ["77"], ["Title"] = [row.Source.Title], ["BodyHtml"] = [Body], ["Metadata.PublicationDate"] = [p.Date!], ["RequestId"] = [source.Command(row, "create").ToString()], ["intent"] = ["Save"] };
                item.Stage = "Creating"; await Persist();
            }
            if (item.Stage == "Creating")
            {
                var saved = await cms.SaveAsync(item.PendingSave ?? throw Conflict(row.Stt, "Thiếu lệnh tạo nháp."));
                var id = saved.GetProperty("contentId").GetGuid(); var editor = await common.EditorAsync(id, row.Source);
                var history = await cms.HtmlAsync($"/admin/history/{id}");
                if (saved.GetProperty("isPublic").GetBoolean() || saved.GetProperty("isTrashed").GetBoolean() || !editor.CanSave || editor.State != "Draft"
                    || editor.Form.QuerySelector("input[name='AttachmentIds']") != null || WebCmsClient.Value(editor.Form, "BodyHtml") != Body
                    || WebCmsClient.Value(editor.Form, "Metadata.PublicationDate") != p.Date || history.QuerySelectorAll("table.revision-table tbody tr").Length != 1)
                    throw Conflict(row.Stt, "Bản nháp không đúng lệnh tạo hoặc đã có chỉnh sửa; dừng.");
                item.ContentId = id; item.Created = true; item.Fingerprint = editor.Fingerprint; item.PendingSave = null; item.Stage = "Uploading"; await Persist();
            }
            if (item.Stage == "Uploading")
            {
                var editor = await common.EditorAsync(item.ContentId!.Value, row.Source);
                if (!editor.CanSave || editor.State != "Draft" || editor.Fingerprint != item.Fingerprint) throw Conflict(row.Stt, "Bản nháp đã đổi; không upload/gắn đè.");
                foreach (var file in row.Files)
                {
                    var asset = await cms.FindUploadAsync(editor.Id, file.Sha256);
                    if (asset == null && item.Assets.ContainsKey(file.Entry)) throw Conflict(row.Stt, "Tệp đã nhận trước đó không còn; không tự upload lại.");
                    asset ??= await cms.UploadAsync(editor.Id, row.Attachment(file), source.ReadFile(file));
                    var id = await common.VerifyAssetAsync(editor.Id, row.Attachment(file), asset.Value, LandPortal.Infrastructure.EditorialDocumentPolicy.PdfMaximum);
                    if (item.Assets.TryGetValue(file.Entry, out var old) && old != id) throw Conflict(row.Stt, "ID media thay đổi.");
                    item.Assets[file.Entry] = id; await Persist();
                }
                // Retain the exact original rowversion; concurrent editor changes cannot be overwritten by the attach command.
                var fields = WebWeeklyImporter.Fields(editor.Form); fields["AttachmentIds"] = row.Files.Select(x => item.Assets[x.Entry].ToString()).ToArray();
                foreach (var file in row.Files) fields[$"AttachmentLabels[{item.Assets[file.Entry]}]"] = [file.Name];
                fields["RequestId"] = [source.Command(row, "attach").ToString()]; fields["intent"] = ["Save"];
                item.PendingSave = fields; item.Stage = "Attaching"; await Persist();
            }
            if (item.Stage == "Attaching")
            {
                var saved = await cms.SaveAsync(item.PendingSave ?? throw Conflict(row.Stt, "Thiếu lệnh gắn PDF để đối soát."));
                if (saved.GetProperty("contentId").GetGuid() != item.ContentId) throw Conflict(row.Stt, "CMS trả sai ContentId.");
                await VerifyAsync(row, item, p.Date!); item.PendingSave = null; item.Stage = "Verified"; await Persist();
            }
            if (item.Stage != "Verified") throw Conflict(row.Stt, "Nhật ký có trạng thái không hỗ trợ.");
        }
    }
    public async Task ReconcileAsync(PlanningPackage source, PlanningPlan plan, PlanningJournal journal, DateDecisions? decisions)
    {
        Validate(source, plan, journal, decisions);
        foreach (var item in journal.Rows.Where(x => x.Stage == "Verified")) await VerifyAsync(source.Rows.Single(x => x.Stt == item.Stt), item, plan.Rows.Single(x => x.Stt == item.Stt).Date!);
    }
    async Task<Guid?> PublishedAsync(PlanningRow row, PlanningJournalRow item, string date)
    {
        var history = await cms.HtmlAsync($"/admin/history/{item.ContentId}");
        if (history.QuerySelector("#history-form")?.GetAttribute("data-content-id") != item.ContentId.ToString()) throw Conflict(row.Stt, "Lịch sử CMS không khớp.");
        var live = history.QuerySelectorAll("table.revision-table tbody tr").Where(x => x.QuerySelector(".public-badge")?.TextContent.Trim() == "Công khai").ToArray();
        if (live.Length == 0) return null;
        if (live.Length != 1 || !Guid.TryParse(live[0].QuerySelector("[data-compare]")?.GetAttribute("data-compare"), out var revision)) throw Conflict(row.Stt, "Không xác định đúng revision công khai.");
        var preview = await cms.HtmlAsync($"/admin/preview/{item.ContentId}?revisionId={revision}");
        await common.EditorAsync(item.ContentId!.Value, row.Source);
        var current = await cms.HtmlAsync($"/admin/preview/{item.ContentId}");
        var category = current.QuerySelector("article header .eyebrow")?.TextContent.Trim();
        if (string.IsNullOrWhiteSpace(category) || preview.QuerySelector("article header .eyebrow")?.TextContent.Trim() != category || preview.QuerySelector("article h1")?.TextContent.Trim() != row.Source.Title) return null;
        foreach (var file in row.Files)
            if (!preview.QuerySelectorAll(".attachments a[href]").Any(a => a.TextContent == file.Name && cms.Url(a.GetAttribute("href")!).AbsolutePath == $"/admin/media/{item.Assets[file.Entry]}/preview")) return null;
        return revision;
    }
    public async Task<IReadOnlyList<LinkResult>> SyncAsync(PlanningPackage source, PlanningPlan plan, PlanningJournal journal, DateDecisions? decisions)
    {
        Validate(source, plan, journal, decisions); var result = new List<LinkResult>();
        foreach (var row in source.Rows)
        {
            var item = journal.Rows.SingleOrDefault(x => x.Stt == row.Stt && x.Stage == "Verified"); var p = plan.Rows.Single(x => x.Stt == row.Stt);
            if (item == null || p.Date == null) { result.Add(new(row.Stt, item?.ContentId, null, null, "Chưa nhập/đối soát hoặc đang bị chặn")); continue; }
            var revision = await PublishedAsync(row, item, p.Date);
            if (revision == null) { result.Add(new(row.Stt, item.ContentId, null, null, "Chưa công khai đúng revision/tài liệu")); continue; }
            try
            {
                var path = "/bai-viet/" + item.ContentId; var page = await cms.HtmlAsync(path, true);
                var expectedDate = WeeklyDates.ParseIso(p.Date).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                var displayedDate = page.QuerySelector(".article-meta")?.ChildNodes.FirstOrDefault(x => x.NodeType == NodeType.Text)?.TextContent.Trim();
                if (page.QuerySelector("h1")?.TextContent.Trim() != row.Source.Title || displayedDate != expectedDate) throw Conflict(row.Stt, "Ngày công khai chưa đúng cột B hoặc tiêu đề khác nguồn.");
                foreach (var file in row.Files)
                {
                    var id = item.Assets[file.Entry];
                    if (!page.QuerySelectorAll(".attachments li").Any(li => li.QuerySelector("strong")?.TextContent == file.Name
                        && li.QuerySelectorAll("a[href]").Any(a => cms.Url(a.GetAttribute("href")!).AbsolutePath == "/media/" + id))) throw Conflict(row.Stt, "Thiếu liên kết PDF công khai.");
                    var bytes = await cms.BytesAsync($"/media/{id}?download=true&contentId={item.ContentId}", "application/pdf", LandPortal.Infrastructure.EditorialDocumentPolicy.PdfMaximum, true);
                    if (bytes.LongLength != file.Bytes || WeeklyPackage.Hash(bytes) != file.Sha256) throw Conflict(row.Stt, "Hash PDF công khai không khớp.");
                }
                if (await PublishedAsync(row, item, p.Date) != revision) throw Conflict(row.Stt, "Revision đổi trong lúc xác minh.");
                result.Add(new(row.Stt, item.ContentId, revision, cms.Url(path).AbsoluteUri, "PublishedDateAndFilesVerified"));
            }
            catch (Exception e) when (e is InvalidDataException or HttpRequestException or TaskCanceledException)
            { result.Add(new(row.Stt, item.ContentId, revision, null, "Chưa xác minh được ngày/trang/PDF công khai: " + (e is InvalidDataException ? e.Message : "HTTP không thành công"))); }
        }
        for (var i = 0; i < result.Count; i++)
            if (result[i].Url != null && await PublishedAsync(source.Rows.Single(x => x.Stt == result[i].Stt), journal.Rows.Single(x => x.Stt == result[i].Stt), plan.Rows.Single(x => x.Stt == result[i].Stt).Date!) != result[i].PublishedRevisionId)
                result[i] = result[i] with { Url = null, Status = "Revision đã đổi trước khi ghi Excel" };
        return result;
    }
}
