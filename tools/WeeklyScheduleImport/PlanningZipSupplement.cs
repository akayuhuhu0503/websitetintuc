using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Infrastructure;

namespace LandPortal.WeeklyImport;

// A supplemental review, not a rewrite/replay of the completed 32/33 PDF import.
public static class PlanningZipSupplement
{
    public static readonly Guid ApprovedContentId = Guid.Parse("fbb093da-f241-4783-a0b2-e3521a01c5c8");
    public const string ApprovedName = "21_2025_NQ_H_ND.zip";
    public const string ApprovedSha256 = "8df1b096400344996af1c84841e1c56d5638b1573967436e90eea512151f5eaf";

    // Read-only reconciliation of the explicitly requested extra attachment. Never adds it to the PDF count/journal.
    public static async Task<Guid?> VerifyExtraAsync(WebCmsClient cms, WebEditor editor, PlanningRow row, Guid[] ids)
    {
        if (ids.Length == row.Files.Count) return null;
        if (row.Stt != 21 || editor.Id != ApprovedContentId || ids.Length != row.Files.Count + 1)
            throw new InvalidDataException("Tệp bổ sung nằm ngoài phạm vi STT 21; không thay mapping PDF.");
        var nodes = editor.Form.QuerySelectorAll("#selected-attachments li[data-asset]").Where(x => x.QuerySelector("span")?.TextContent == ApprovedName).ToArray();
        if (nodes.Length != 1 || !Guid.TryParse(nodes[0].GetAttribute("data-asset"), out var id) || !ids.Contains(id))
            throw new InvalidDataException("Không xác định đúng ZIP bổ sung STT 21.");
        var items = await cms.JsonAsync($"/admin/documents/{editor.Id}/items");
        var matches = items.GetProperty("items").EnumerateArray().Where(a => a.GetProperty("id").GetGuid() == id).ToArray();
        if (matches.Length != 1 || matches[0].GetProperty("scanStatus").GetString() != "Clean" || !matches[0].GetProperty("usable").GetBoolean()
            || matches[0].GetProperty("mimeType").GetString() != EditorialZipPolicy.Mime || matches[0].GetProperty("byteLength").GetInt64() != 124514760
            || matches[0].GetProperty("originalName").GetString() != ApprovedName)
            throw new InvalidDataException("ZIP bổ sung chưa có kết quả quét thật hoặc khác tệp đã chấp thuận.");
        var bytes = await cms.BytesAsync($"/admin/documents/file/{id}?contentId={editor.Id}", EditorialZipPolicy.Mime, EditorialZipPolicy.Maximum);
        if (bytes.LongLength != 124514760 || WeeklyPackage.Hash(bytes) != ApprovedSha256)
            throw new InvalidDataException("ZIP bổ sung khác byte/hash đã chấp thuận.");
        return id;
    }
    public static async Task RunAsync(string[] args)
    {
        var values = new Dictionary<string, string>();
        for (var i = 1; i < args.Length; i++)
        {
            var key = args[i];
            if (key is not ("--file" or "--journal" or "--out") || i + 1 >= args.Length || !values.TryAdd(key, args[++i]))
                throw new ArgumentException("Dùng planning-zip-check --file ZIP --journal planning-web-journal.json --out thư-mục-mới.");
        }
        string Required(string key) => values.GetValueOrDefault(key) ?? throw new ArgumentException("Thiếu " + key);
        var source = Path.GetFullPath(Required("--file"));
        var journalPath = Path.GetFullPath(Required("--journal"));
        var output = Path.GetFullPath(Required("--out"));
        if (Directory.Exists(output)) throw new IOException("Cần thư mục mới; không ghi đè lịch sử bổ sung ZIP.");
        if (Path.GetFileName(source) != ApprovedName || new FileInfo(source).Length != 124514760)
            throw new InvalidDataException("Không đúng ZIP STT 21 đã được chủ dự án yêu cầu bổ sung.");
        await using var file = File.OpenRead(source);
        var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(file));
        if (sha != ApprovedSha256) throw new InvalidDataException("ZIP khác byte/hash phạm vi đã xác nhận; không thay nguồn âm thầm.");
        var journalBytes = await File.ReadAllBytesAsync(journalPath);
        var journal = JsonSerializer.Deserialize<PlanningJournal>(journalBytes) ?? throw new InvalidDataException("Nhật ký không hợp lệ.");
        var plan = JsonSerializer.Deserialize<PlanningPlan>(await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(journalPath)!, "planning-web-plan.json")))
            ?? throw new InvalidDataException("Thiếu kế hoạch gốc.");
        if (journal.PlanSha256 != PlanningWebImporter.PlanHash(plan) || plan.Profile != PlanningPackage.Profile || plan.Rows.Count != 32
            || plan.Origin != "https://gdla.gov.vn/" || journal.Rows.Select(r => r.Stt).Distinct().Count() != 32)
            throw new InvalidDataException("Nhật ký/kế hoạch không khớp đợt QHKHSDD gốc.");
        var row = journal.Rows.Single(r => r.Stt == 21);
        if (row.ContentId != ApprovedContentId || row.Stage != "Verified" || row.Assets.Count != 1)
            throw new InvalidDataException("STT 21 chưa có mapping PDF đã đối soát; không đoán ContentId.");
        IReadOnlyList<EditorialZipPolicy.Entry> entries = [];
        string? blocker = null;
        try { entries = await EditorialZipPolicy.ValidateAsync(source); }
        catch (MediaUploadException e) { blocker = e.Message; }
        var report = new
        {
            version = 1, kind = "QHKHSDD-supplemental-zip", createdUtc = DateTimeOffset.UtcNow,
            stt = 21, row.ContentId, categoryId = 77, publicationDate = plan.Rows.Single(r => r.Stt == 21).Date,
            fileName = ApprovedName, bytes = file.Length, sha256 = sha,
            ownerScope = "Requested supplemental ZIP for existing STT 21; no antivirus exemption or publication approval inferred",
            originalPlanSha256 = journal.PlanSha256, originalJournalSha256 = WeeklyPackage.Hash(journalBytes),
            preservedPdfAssets = row.Assets, expectedScope = new { dossiers = 32, pdfs = 33, supplementalZips = 1 },
            structure = blocker == null ? "PASS" : "BLOCKED", blocker, entries,
            scanStatus = "NotScanned", publicEligible = false, cmsWrites = 0,
            next = "CMS must support ZIP and a real scanner. Reconcile existing article and PDF before upload; attach only after Clean through an editable revision. No automatic approval/publication.",
            oldExclusion = "Historical exclusion remains in the original import manifest; this separate receipt records the new supplemental request only."
        };
        Directory.CreateDirectory(output);
        await WeeklyImporter.AtomicJson(Path.Combine(output, "zip-supplement-review.json"), report);
        Console.WriteLine($"STT 21 → {row.ContentId}; 32 hồ sơ / 33 PDF + 1 ZIP bổ sung. Structure={report.structure}; NotScanned; 0 ghi CMS.");
        if (blocker != null) Console.WriteLine(blocker);
    }
}
