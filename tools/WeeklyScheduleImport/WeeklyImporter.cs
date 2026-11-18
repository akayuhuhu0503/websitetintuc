using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.WeeklyImport;

public sealed record TargetIdentity(string Server, string Database, string Principal, string[] Migrations);
public sealed record ImportRow(int Stt, string Action, Guid? ContentId, Guid? MediaId, string? Version, string? Reason);
public sealed record ImportPlan(string Kind, string ZipSha256, string WorkbookSha256, TargetIdentity Target, string MediaRoot, string ActorId,
    DateTimeOffset CreatedUtc, IReadOnlyList<ImportRow> Rows);
public sealed record RowReceipt(int Stt, Guid ContentId, Guid? MediaId, string State, string Outcome);
public sealed record ImportReceipt(string ZipSha256, string WorkbookSha256, TargetIdentity Target, string ActorId,
    DateTimeOffset UpdatedUtc, IReadOnlyList<RowReceipt> Rows);

public sealed class WeeklyImporter(PortalDbContext db, StorageOptions storage, MediaUploadOptions policy, string actorId, IConfiguration? configuration = null)
{
    readonly CurrentPermissionService rights = new(db);
    WorkflowService Workflow => new(db, new HtmlPolicy(), policy, storage);
    static DraftInput Draft(WeeklyRow row, Guid? mediaId = null) => new()
    {
        Kind = ContentKind.StaticPage, PrimaryCategoryId = 76, Title = row.Title,
        BodyHtml = "<p>Lịch công tác tuần được đính kèm trong tài liệu PDF.</p>",
        Assets = mediaId is Guid id ? [new() { MediaAssetId = id, Usage = "Attachment", Caption = row.FileName }] : []
        // No dates inferred from Excel B, no WeeklyYear/Week, no synthetic release or table.
    };
    public async Task<TargetIdentity> IdentifyAsync()
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),DB_NAME(),USER_NAME()";
            using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
            var target = new TargetIdentity(reader.GetString(0), reader.GetString(1), reader.GetString(2), []);
            await reader.DisposeAsync();
            if (configuration != null && (configuration["Sql:ExpectedDatabase"] != target.Database
                || UatDeployment.Enabled(configuration) && UatDeployment.PhysicalServer(configuration) != target.Server))
                throw new InvalidDataException("SQL physical server/database khác cấu hình đã cấp; không ghi dữ liệu.");
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            if (applied.Length != 18 || !applied.SequenceEqual(db.Database.GetMigrations())) throw new InvalidDataException("Cần schema đúng 18 migration hiện hành; công cụ không nâng schema.");
            if (target.Principal != "LandPortalRuntime") throw new UnauthorizedAccessException("Công cụ nhập phải dùng Application Role LandPortalRuntime, không dùng quyền DBA để bỏ phân quyền CMS.");
            return target with { Migrations = applied };
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
    public static bool SameTarget(TargetIdentity a, TargetIdentity b) => a.Server == b.Server && a.Database == b.Database && a.Principal == b.Principal && a.Migrations.SequenceEqual(b.Migrations);
    async Task<ContentActor> Actor()
    {
        var actor = await rights.GetAsync(actorId);
        if (!actor.Has(Permissions.Create) || !actor.Has(Permissions.EditOwn) && !actor.Has(Permissions.EditAll)
            || !actor.Has(Permissions.ManageMedia) || !actor.Covers([76])) throw new UnauthorizedAccessException("Tài khoản CMS cần quyền soạn bài, sửa bài, tải tài liệu và phạm vi chuyên mục 76.");
        return actor;
    }
    public async Task<ImportPlan> PlanAsync(WeeklyPackage package)
    {
        var target = await IdentifyAsync(); var actor = await Actor();
        if (!await db.Categories.AnyAsync(x => x.Id == 76 && x.Slug == "lich-tuan")) throw new InvalidDataException("Chuyên mục 76 không phải Lịch tuần; không tự tạo/mapping chuyên mục.");
        var live = await db.Set<SiteConfiguration>().Where(x => x.Id == 1).Select(x => x.LiveRevisionId).FirstOrDefaultAsync();
        if (live != null && !await db.Set<SiteCategoryConfiguration>().AnyAsync(x => x.RevisionId == live && x.CategoryId == 76))
            throw new InvalidDataException("Chuyên mục 76 chưa nằm trong cấu hình CMS đang áp dụng.");
        var rows = new List<ImportRow>();
        foreach (var row in package.Rows) rows.Add(await ResolveAsync(row, actor));
        return new("GDLA.WeeklyImport.v1", package.ZipSha256, package.WorkbookSha256, target, Path.GetFullPath(storage.MediaRoot), actorId, DateTimeOffset.UtcNow, rows);
    }
    async Task<ImportRow> ResolveAsync(WeeklyRow row, ContentActor actor)
    {
        var createId = WeeklyPackage.Command(row, "create"); var attachId = WeeklyPackage.Command(row, "attach");
        var command = await db.ContentCommands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == createId);
        if (command != null)
        {
            var c = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == command.ContentId);
            if (command.ActorUserId != actorId || command.Action != "SaveDraft" || c.IsTrashed || !await rights.CanReadAsync(actor, c))
                return new(row.Stt, "Conflict", c.Id, null, null, "Hồ sơ đã nhập thuộc người khác, bị xóa hoặc ngoài quyền; không ghi đè.");
            var attached = await db.ContentCommands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == attachId);
            if (attached != null)
            {
                var match = await MatchAsync(c, row);
                return match is Guid asset ? new(row.Stt, "AlreadyImported", c.Id, asset, Convert.ToBase64String(c.RowVersion), null)
                    : new(row.Stt, "Conflict", c.Id, null, null, "Hồ sơ đã nhập nhưng tiêu đề/PDF hiện hành đã đổi; không tự gắn lại tài liệu đã gỡ.");
            }
            var draft = await db.ContentDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.ContentId == c.Id);
            var original = Draft(row);
            if (c.State != WorkflowState.Draft || c.IsPublic || c.EditGeneration != 1 || c.CurrentRevisionId != command.ResultRevisionId
                || draft == null || draft.Title != row.Title || draft.BodyHtml != new HtmlPolicy().Clean(original.BodyHtml)
                || draft.PrimaryCategoryId != 76 || draft.Metadata.WeeklyYear != null || await db.DraftAssets.AnyAsync(x => x.ContentId == c.Id))
                return new(row.Stt, "Conflict", c.Id, null, null, "Bản nháp dở dang đã được biên tập; không ghi đè hoặc tự hoàn tất.");
            var assetId = await db.EditorialDocuments.Where(x => x.ContentId == c.Id && x.OwnerId == actorId)
                .Join(db.MediaAssets.Where(x => x.Sha256 == row.PdfSha256 && x.OriginalName == row.FileName), d => d.MediaAssetId, m => m.Id, (d, m) => (Guid?)m.Id).SingleOrDefaultAsync();
            return new(row.Stt, "Resume", c.Id, assetId, Convert.ToBase64String(c.RowVersion), null);
        }
        // Check current and historical titles AND attachment hashes. Ambiguity is never resolved by guessing.
        var candidates = await db.Contents.AsNoTracking().Where(c =>
            db.ContentDrafts.Any(d => d.ContentId == c.Id && d.Title == row.Title)
            || db.Revisions.Any(r => r.ContentId == c.Id && r.Title == row.Title)
            || db.ContentAssets.Any(a => a.Revision.ContentId == c.Id && a.MediaAsset.Sha256 == row.PdfSha256)
            || db.EditorialDocuments.Any(d => d.ContentId == c.Id && db.MediaAssets.Any(m => m.Id == d.MediaAssetId && m.Sha256 == row.PdfSha256))).Take(3).ToListAsync();
        if (candidates.Count == 1 && !candidates[0].IsTrashed && await rights.CanReadAsync(actor, candidates[0])
            && await MatchAsync(candidates[0], row) is Guid matching)
            return new(row.Stt, "Reuse", candidates[0].Id, matching, Convert.ToBase64String(candidates[0].RowVersion), "Tái sử dụng hồ sơ khớp tiêu đề/PDF; không sửa nội dung.");
        if (candidates.Count != 0 || await db.MediaAssets.AnyAsync(x => x.Sha256 == row.PdfSha256))
            return new(row.Stt, "Conflict", null, null, null, "Đã có tiêu đề hoặc PDF tương ứng nhưng chưa khớp duy nhất cả hồ sơ/chuyên mục/tệp/quyền.");
        var period = System.Text.RegularExpressions.Regex.Match(row.Title, @"Tuần\s*(\d{1,2}).*?(\d{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (period.Success && int.TryParse(period.Groups[1].Value, out var week) && int.TryParse(period.Groups[2].Value, out var year)
            && await db.WeeklyScheduleReleases.AnyAsync(x => x.IsoYear == year && x.IsoWeek == week))
            return new(row.Stt, "Conflict", null, null, null, "Năm/tuần đã có lịch phát hành; cán bộ cần đối soát hồ sơ hiện hữu, không tự tạo lịch thay thế.");
        return new(row.Stt, "Create", null, null, null, null);
    }
    async Task<Guid?> MatchAsync(Content c, WeeklyRow row)
    {
        if (c.Kind is not (ContentKind.StaticPage or ContentKind.Article)) return null;
        // Existing live revision takes precedence over an unpublished edit.
        var id = c.IsPublic ? c.PublishedRevisionId : c.CurrentRevisionId;
        var revision = await db.Revisions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.ContentId == c.Id);
        if (revision == null || revision.Title != row.Title || revision.PrimaryCategoryId != 76) return null;
        return await db.ContentAssets.Where(a => a.RevisionId == id && a.Usage == "Attachment" && a.MediaAsset.Sha256 == row.PdfSha256
            && a.MediaAsset.ByteLength == row.Bytes && a.MediaAsset.MimeType == "application/pdf" && a.MediaAsset.OriginalName == row.FileName)
            .Select(a => (Guid?)a.MediaAssetId).SingleOrDefaultAsync();
    }
    public async Task<ImportReceipt> ImportAsync(WeeklyPackage package, ImportPlan plan, string receiptPath,
        Func<int, string, Task>? checkpoint = null)
    {
        if (plan.Kind != "GDLA.WeeklyImport.v1" || plan.ZipSha256 != package.ZipSha256 || plan.WorkbookSha256 != package.WorkbookSha256
            || plan.MediaRoot != Path.GetFullPath(storage.MediaRoot) || plan.ActorId != actorId || plan.Rows.Count != 43 || !plan.Rows.Select(x => x.Stt).SequenceEqual(Enumerable.Range(1, 43)))
            throw new InvalidDataException("Plan không khớp ZIP, workbook, người nhập hoặc 43 STT.");
        if (!SameTarget(plan.Target, await IdentifyAsync())) throw new InvalidDataException("SQL đích đã đổi so với dry-run.");
        await db.Database.OpenConnectionAsync(); // Session lock survives individual WorkflowService transactions.
        var output = new List<RowReceipt>();
        try
        {
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='GDLA.WeeklyImport.v1',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; IF @r<0 THROW 51000,'Weekly import already running',1;");
            var actor = await Actor();
            var fresh = new List<ImportRow>(); foreach (var row in package.Rows) fresh.Add(await ResolveAsync(row, actor));
            foreach (var now in fresh)
            {
                var before = plan.Rows.Single(x => x.Stt == now.Stt);
                if (now.Action == "Conflict" || before.Action == "Conflict") throw new InvalidDataException($"STT {now.Stt}: {now.Reason ?? before.Reason}");
                // Only durable receipts may turn a planned Create into Resume/AlreadyImported.
                if (!(before.Action == "Create" && now.Action is "Create" or "Resume" or "AlreadyImported")
                    && !(before.Action == "Resume" && now.Action is "Resume" or "AlreadyImported" && before.ContentId == now.ContentId)
                    && (before.Action != now.Action || before.ContentId != now.ContentId || before.MediaId != now.MediaId || before.Version != now.Version))
                    throw new InvalidDataException($"STT {now.Stt}: dữ liệu đích thay đổi; chạy lại dry-run trước khi ghi.");
            }
            // Validate every input before creating the first record; no fake scanner result.
            foreach (var row in package.Rows)
            {
                if (row.Title != row.Title.Trim()) throw new InvalidDataException($"STT {row.Stt}: tiêu đề có khoảng trắng ngoài; CMS sẽ Trim nên cần đối soát, không tự sửa.");
                var temp = Path.Combine(storage.TempRoot, "weekly-validate-" + Guid.NewGuid().ToString("N") + ".pdf");
                Directory.CreateDirectory(storage.TempRoot);
                try { await File.WriteAllBytesAsync(temp, package.PdfBytes[row.Stt]); ManagedPdfPolicy.Validate(temp); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            var media = new MediaService(db, storage, policy, configuration);
            // Scanner-required deployments retain the existing scanner gate; this tool never enables no-AV.
            if (!policy.ApprovedManagedDocumentsWithoutAv && fresh.Any(x => x.Action is "Create" or "Resume"))
            {
                if (configuration == null) throw new InvalidDataException("Thiếu cấu hình scanner; không tự mở ngoại lệ cho tài liệu.");
                await media.RequireUploadScannerAsync();
            }
            foreach (var row in package.Rows)
            {
                db.ChangeTracker.Clear(); actor = await Actor(); var state = await ResolveAsync(row, actor);
                if (state.Action == "Conflict") throw new InvalidDataException($"STT {row.Stt}: {state.Reason}");
                var contentId = state.ContentId; var assetId = state.MediaId;
                if (state.Action == "Create")
                {
                    var created = await Workflow.SaveAsync(null, Draft(row), actor, null, WeeklyPackage.Command(row, "create"));
                    contentId = created.ContentId;
                    await checkpointOrSave("Created");
                }
                if (state.Action is "Create" or "Resume")
                {
                    if (assetId == null)
                    {
                        var documents = new EditorialDocumentService(db, storage, media, rights, policy);
                        using var input = new MemoryStream(package.PdfBytes[row.Stt]);
                        var asset = await documents.UploadAsync(contentId!.Value, input, row.FileName, "application/pdf", actor);
                        if (asset.Sha256 != row.PdfSha256 || asset.OriginalName != row.FileName || asset.ByteLength != row.Bytes) throw new InvalidDataException("PDF sau tải không khớp nguồn.");
                        assetId = asset.Id; await checkpointOrSave("Uploaded");
                    }
                    var assetNow = await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == assetId);
                    if (assetNow.IsQuarantined) throw new InvalidDataException($"STT {row.Stt}: PDF chờ bộ quét hiện hành; giữ bản nháp và mapping, chưa gắn hoặc công bố.");
                    await VerifyStoredAsync(assetNow, row);
                    var current = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == contentId);
                    // Do not race an intervening editor save after upload.
                    var resume = await ResolveAsync(row, await Actor());
                    if (resume.Action != "Resume") throw new DbUpdateConcurrencyException("Bản nháp đã đổi trong lúc tải PDF; dừng để đối soát.");
                    await Workflow.SaveAsync(contentId, Draft(row, assetId), await Actor(), current.RowVersion, WeeklyPackage.Command(row, "attach"));
                }
                else if (assetId is Guid existingId)
                    await VerifyStoredAsync(await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == existingId), row);
                var c = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == contentId);
                output.Add(new(row.Stt, c.Id, assetId, c.State.ToString(), state.Action));
                await SaveReceipt();
                async Task checkpointOrSave(string step)
                {
                    // Crash between DB commit and file receipt is recovered from ContentCommands / EditorialDocuments.
                    await AtomicJson(receiptPath, new ImportReceipt(package.ZipSha256, package.WorkbookSha256, plan.Target, actorId, DateTimeOffset.UtcNow,
                        output.Append(new RowReceipt(row.Stt, contentId!.Value, assetId, "Draft", step)).ToArray()));
                    if (checkpoint != null) await checkpoint(row.Stt, step);
                }
            }
            return await SaveReceipt();
        }
        finally
        {
            try { await db.Database.ExecuteSqlRawAsync("EXEC sys.sp_releaseapplock @Resource='GDLA.WeeklyImport.v1',@LockOwner='Session';"); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
        async Task<ImportReceipt> SaveReceipt()
        {
            var receipt = new ImportReceipt(package.ZipSha256, package.WorkbookSha256, plan.Target, actorId, DateTimeOffset.UtcNow, output.ToArray());
            await AtomicJson(receiptPath, receipt); return receipt;
        }
    }
    async Task VerifyStoredAsync(MediaAsset asset, WeeklyRow row)
    {
        if (!policy.CanServe(asset) || asset.Sha256 != row.PdfSha256 || asset.OriginalName != row.FileName || asset.ByteLength != row.Bytes)
            throw new InvalidDataException($"STT {row.Stt}: PDF chưa đạt chính sách hiện hành hoặc metadata không khớp; không tự đánh dấu Clean.");
        await using var file = new FileStream(MediaService.ResolveMediaPath(storage, asset.StorageKey), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length != row.Bytes || Convert.ToHexStringLower(await SHA256.HashDataAsync(file)) != row.PdfSha256)
            throw new InvalidDataException($"STT {row.Stt}: byte/hash PDF trong storage không khớp; không ghi đè tệp.");
    }
    public async Task<ImportReceipt> ReconcileAsync(WeeklyPackage package)
    {
        var plan = await PlanAsync(package);
        var rows = new List<RowReceipt>();
        foreach (var row in plan.Rows.Where(x => x.ContentId != null && x.Action != "Conflict"))
        {
            var c = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == row.ContentId);
            rows.Add(new(row.Stt, c.Id, row.MediaId, c.State.ToString(), row.Action));
        }
        return new(package.ZipSha256, package.WorkbookSha256, plan.Target, actorId, DateTimeOffset.UtcNow, rows);
    }
    public static async Task AtomicJson<T>(string path, T value)
    {
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, WeeklyPackage.Json)); File.Move(temp, full, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
