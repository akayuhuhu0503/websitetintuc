using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Infrastructure;

public sealed class EditorialDocumentService(PortalDbContext db, StorageOptions storage, MediaService media, CurrentPermissionService rights)
{
    public const long Maximum = 10 * 1024 * 1024;
    public async Task EnqueueLegacyPdfAsync(Guid contentId, Guid assetId, ContentActor actor, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource={"document-enqueue-" + assetId},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;IF @r<0 THROW 51000,'Document enqueue lock unavailable',1;", ct);
        actor = await rights.GetAsync(actor.UserId); var content = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contentId, ct);
        if (content == null || content.IsTrashed || !actor.Has(Permissions.ManageMedia) || !await rights.CanEditAsync(actor, content)) throw new UnauthorizedAccessException();
        var old = await db.EditorialDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.MediaAssetId == assetId, ct);
        if (old != null) { if (old.ContentId != contentId) throw new UnauthorizedAccessException(); await tx.CommitAsync(ct); return; }
        var asset = await db.MediaAssets.SingleOrDefaultAsync(x => x.Id == assetId, ct);
        if (asset == null || asset.Origin != "Uploaded" || asset.MimeType != "application/pdf" || asset.ScanStatus != "NotScanned" || asset.ValidationStatus != "Validated" || asset.IsQuarantined || asset.ByteLength > Maximum)
            throw new InvalidOperationException("Chỉ tiếp nhận PDF upload cũ NotScanned, đã kiểm định, tối đa10 MiB; không áp dụng cho tài nguyên nguồn hoặc reset kết luận quét.");
        if (!await db.DraftAssets.AnyAsync(x => x.ContentId == contentId && x.MediaAssetId == assetId, ct) && !await db.ContentAssets.AnyAsync(x => x.Revision.ContentId == contentId && x.MediaAssetId == assetId, ct)) throw new UnauthorizedAccessException();
        if (await db.DraftAssets.AnyAsync(x => x.ContentId != contentId && x.MediaAssetId == assetId, ct) || await db.ContentAssets.AnyAsync(x => x.Revision.ContentId != contentId && x.MediaAssetId == assetId, ct))
            throw new InvalidOperationException("PDF upload đang dùng chung nhiều hồ sơ; cần đối soát phạm vi trước khi chuyển hàng đợi, không tự thu hồi tham chiếu khác.");
        await using var original = new FileStream(MediaService.ResolveMediaPath(storage, asset.StorageKey), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (original.Length != asset.ByteLength || Convert.ToHexStringLower(await SHA256.HashDataAsync(original, ct)) != asset.Sha256) throw new InvalidOperationException("Byte PDF cũ không khớp metadata; chưa chuyển hàng đợi.");
        asset.Origin = "EditorialDocument"; asset.IsQuarantined = true; asset.ValidationStatus = "PendingScan";
        db.EditorialDocuments.Add(new() { MediaAssetId = assetId, ContentId = contentId, OwnerId = actor.UserId });
        db.AuditLogs.Add(new() { Actor = actor.UserId, ContentId = contentId, Action = "document.legacy.queued", Detail = JsonSerializer.Serialize(new { assetId, asset.Sha256, asset.ByteLength, previousOrigin = "Uploaded", state = "NotScanned", bytesMoved = false }) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public static void RequireTestInfrastructure(IConfiguration config, string environment)
    {
        if(UatDeployment.Validate(config,environment))return;
        var files = StorageOptions.FromConfiguration(config);
        var root = Path.GetFullPath(Path.Combine(PortalConfiguration.FindWorkspace(), ".local", "tests")) + Path.DirectorySeparatorChar;
        if (environment != "Development" || new SqlConnectionStringBuilder(DatabaseSettings.Connection(config)).InitialCatalog != "LandPortal_Test"
            || !Path.GetFullPath(files.MediaRoot).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Luồng tài liệu mới chỉ mở trên Development / SQL Test / kho Test; Production cần scanner và policy được phê duyệt.");
    }
    public async Task<MediaAsset> UploadAsync(Guid contentId, Stream input, string name, string declaredMime, ContentActor actor, CancellationToken ct = default)
    {
        actor = await rights.GetAsync(actor.UserId);
        var content = await db.Contents.SingleOrDefaultAsync(x => x.Id == contentId, ct);
        if (content == null || content.IsTrashed || !actor.Has(Permissions.ManageMedia) || !await rights.CanEditAsync(actor, content)) throw new UnauthorizedAccessException();
        name = Path.GetFileName(name.Replace('\\', '/'));
        if (name.Length is < 1 or > 200 || name.Any(char.IsControl)) throw new InvalidOperationException("Tên tệp không hợp lệ.");
        var mime = OfficeDocumentPolicy.Mime(name);
        if (mime == OfficeDocumentPolicy.Docx && (content.Kind != ContentKind.StaticPage || content.PrimaryCategoryId != 76 && !await db.ContentDrafts.AnyAsync(x => x.ContentId == contentId && x.PrimaryCategoryId == 76, ct)))
            throw new InvalidOperationException("DOCX dùng cho hồ sơ lịch tuần. Tạo/lưu nháp mẫu lịch tuần trước khi tải tệp.");
        if (declaredMime.Split(';')[0] is var claimed && claimed != mime && claimed != "application/octet-stream") throw new InvalidOperationException("MIME không khớp loại tài liệu.");
        var asset = new MediaAsset { Origin = "EditorialDocument", ValidationStatus = "PendingScan", IsQuarantined = true, MimeType = mime, OriginalName = name, UploadedByUserId = actor.UserId };
        asset.StorageKey = "editorial-private/" + asset.Id.ToString("N") + ".bin";
        var path = MediaService.ResolveMediaPath(storage, asset.StorageKey); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var saved = false; var attempted = false;
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536]; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int count; while ((count = await input.ReadAsync(buffer, ct)) != 0)
                { asset.ByteLength += count; if (asset.ByteLength > Maximum) throw new InvalidOperationException("Tài liệu tối đa 10 MiB."); hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), ct); }
                if (asset.ByteLength == 0) throw new InvalidOperationException("Tệp rỗng."); asset.Sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            }
            if (await db.MediaAssets.AnyAsync(x => x.Sha256 == asset.Sha256, ct)) throw new InvalidOperationException("Tệp trùng đã có trong kho. Không cấp quyền bằng cách tải lại byte giống nhau.");
            // Fresh resource/category permission immediately before persistence.
            actor = await rights.GetAsync(actor.UserId);
            if (!actor.Has(Permissions.ManageMedia) || !await rights.CanEditAsync(actor, content)) throw new UnauthorizedAccessException();
            db.MediaAssets.Add(asset); db.EditorialDocuments.Add(new() { MediaAssetId = asset.Id, ContentId = contentId, OwnerId = actor.UserId });
            db.AuditLogs.Add(new() { Actor = actor.UserId, ContentId = contentId, Action = "document.uploaded", Detail = JsonSerializer.Serialize(new { asset.Id, asset.Sha256, asset.ByteLength, state = "NotScanned" }) });
            attempted = true; await db.SaveChangesAsync(ct); saved = true; return asset;
        }
        finally
        {
            if (!saved && File.Exists(path))
            {
                var remove = !attempted;
                if (attempted) { try { remove = !await db.MediaAssets.AsNoTracking().AnyAsync(x => x.Id == asset.Id, CancellationToken.None); } catch { remove = false; } }
                if (remove) File.Delete(path); // On an uncertain commit preserve a private orphan for reconciliation.
            }
        }
    }
    public static async Task<FileStream?> OpenVerifiedAsync(PortalDbContext db, StorageOptions storage, MediaAsset asset, bool development, CancellationToken ct = default)
    {
        var proof = await db.EditorialDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.MediaAssetId == asset.Id, ct);
        if (asset.Origin != "EditorialDocument" || !OfficeDocumentPolicy.IsDocument(asset.MimeType) || asset.IsQuarantined || asset.ScanStatus != "Clean" || asset.ValidationStatus != "Validated"
            || proof?.ScanState != InquiryScanState.Clean || proof.ScannedSha256 != asset.Sha256 || proof.ScannedUtc == null || string.IsNullOrWhiteSpace(proof.EngineVersion)
            || !development && proof.EngineVersion.StartsWith("Defender/", StringComparison.Ordinal)) return null;
        FileStream? file = null;
        try
        {
            file = new FileStream(MediaService.ResolveMediaPath(storage, asset.StorageKey), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            if (file.Length != asset.ByteLength || Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct)) != asset.Sha256)
            {
                await file.DisposeAsync();
                await using var tx = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(ct) : null;
                var changed = await db.EditorialDocuments.Where(x => x.MediaAssetId == asset.Id && x.RowVersion == proof.RowVersion && x.ScanState == InquiryScanState.Clean)
                    .ExecuteUpdateAsync(x => x.SetProperty(d => d.ScanState, InquiryScanState.ScanFailed).SetProperty(d => d.Error, "FileHashMismatch"), ct);
                if (changed == 1)
                {
                    await db.MediaAssets.Where(x => x.Id == asset.Id && x.Origin == "EditorialDocument").ExecuteUpdateAsync(x => x.SetProperty(a => a.IsQuarantined, true).SetProperty(a => a.ScanStatus, "ScanFailed"), ct);
                    db.AuditLogs.Add(new() { Actor = "document-integrity", Action = "document.tamper.denied", ContentId = proof.ContentId, Detail = "asset=" + asset.Id + ";expectedSha256=" + asset.Sha256 }); await db.SaveChangesAsync(ct);
                }
                if (tx != null) await tx.CommitAsync(ct); return null;
            }
            // Re-read after hashing: a stale Clean proof or revoked file conclusion cannot grant a download.
            if (!await db.EditorialDocuments.AsNoTracking().AnyAsync(x => x.MediaAssetId == asset.Id && x.RowVersion == proof.RowVersion && x.ScanState == InquiryScanState.Clean, ct)
                || !await db.MediaAssets.AsNoTracking().AnyAsync(x => x.Id == asset.Id && !x.IsQuarantined && x.ScanStatus == "Clean" && x.Sha256 == asset.Sha256, ct)) { await file.DisposeAsync(); return null; }
            file.Position = 0; return file;
        }
        catch (Exception error) when (error is IOException or MediaUploadException or UnauthorizedAccessException) { if (file != null) await file.DisposeAsync(); return null; }
    }
    public async Task<bool> ScanOnceAsync(IInquiryScanner scanner, Guid? only = null, CancellationToken ct = default)
    {
        EditorialDocument? item; var now = DateTimeOffset.UtcNow;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource='editorial-document-scan',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;IF @r<0 THROW 51000,'Document scan lock unavailable',1;", ct);
            var expired = db.EditorialDocuments.Where(x => x.ScanState == InquiryScanState.Scanning && x.LeaseUntilUtc <= now);
            if (only != null) expired = expired.Where(x => x.MediaAssetId == only);
            await expired.ExecuteUpdateAsync(x => x.SetProperty(d => d.ScanState, InquiryScanState.ScanFailed).SetProperty(d => d.Error, "LeaseExpired"), ct);
            var queue = db.EditorialDocuments.Where(x => (x.ScanState == InquiryScanState.NotScanned || x.ScanState == InquiryScanState.ScanFailed) && x.Attempts < 3 && x.AvailableUtc <= now);
            if (only != null) queue = queue.Where(x => x.MediaAssetId == only);
            item = await queue.OrderBy(x => x.AvailableUtc).ThenBy(x => x.MediaAssetId).FirstOrDefaultAsync(ct);
            if (item == null) { await tx.CommitAsync(ct); return false; }
            item.ScanState = InquiryScanState.Scanning; item.LeaseToken = Guid.NewGuid(); item.LeaseUntilUtc = now.AddMinutes(2); item.Attempts++;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        var version = item.RowVersion.ToArray(); var asset = await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == item.MediaAssetId, ct);
        var result = new InquiryScanResult(InquiryScanState.ScanFailed, "Unverified", null);
        try
        {
            var owner = await rights.GetAsync(item.OwnerId); var content = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == item.ContentId, ct);
            if (asset.Origin != "EditorialDocument" || content.IsTrashed || !owner.Has(Permissions.ManageMedia) || !await rights.CanEditAsync(owner, content)) throw new UnauthorizedAccessException();
            await using var file = new FileStream(MediaService.ResolveMediaPath(storage, asset.StorageKey), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            if (file.Length != asset.ByteLength || Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct)) != asset.Sha256) result = new(InquiryScanState.ScanFailed, "FileHashMismatch", null);
            else
            {
                file.Position = 0; result = await scanner.ScanAsync(file, ct);
                file.Position = 0; if (Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct)) != asset.Sha256) result = new(InquiryScanState.ScanFailed, "FileChangedDuringScan", null);
                if (result.State == InquiryScanState.Clean) OfficeDocumentPolicy.Validate(MediaService.ResolveMediaPath(storage, asset.StorageKey), asset.MimeType, media);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or System.Xml.XmlException or OperationCanceledException or MediaUploadException)
        { result = new(InquiryScanState.ScanFailed, "FilePolicyOrFormatFailure", null); }
        if (result.State is not (InquiryScanState.Clean or InquiryScanState.Infected or InquiryScanState.ScanFailed) || result.State is InquiryScanState.Clean or InquiryScanState.Infected && string.IsNullOrWhiteSpace(result.EngineVersion)) result = new(InquiryScanState.ScanFailed, "InvalidScannerConclusion", null);
        db.ChangeTracker.Clear();
        await using var finish = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, CancellationToken.None);
        var terminal = result.State is InquiryScanState.Clean or InquiryScanState.Infected;
        var hash = terminal ? asset.Sha256 : null; var scanned = terminal ? DateTimeOffset.UtcNow : (DateTimeOffset?)null;
        var changed = await db.EditorialDocuments.Where(x => x.MediaAssetId == item.MediaAssetId && x.RowVersion == version && x.LeaseToken == item.LeaseToken && x.ScanState == InquiryScanState.Scanning)
            .ExecuteUpdateAsync(x => x.SetProperty(d => d.ScanState, result.State).SetProperty(d => d.Error, result.Error).SetProperty(d => d.EngineVersion, result.EngineVersion).SetProperty(d => d.ScannedSha256, hash).SetProperty(d => d.ScannedUtc, scanned).SetProperty(d => d.AvailableUtc, DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2, item.Attempts))).SetProperty(d => d.LeaseUntilUtc, (DateTimeOffset?)null));
        if (changed != 1) throw new DbUpdateConcurrencyException("Late scanner result rejected by lease/version fence.");
        var clean = result.State == InquiryScanState.Clean;
        changed = await db.MediaAssets.Where(x => x.Id == asset.Id && x.Origin == "EditorialDocument" && x.Sha256 == asset.Sha256 && x.ByteLength == asset.ByteLength)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.ScanStatus, result.State.ToString()).SetProperty(a => a.IsQuarantined, !clean).SetProperty(a => a.ValidationStatus, clean ? "Validated" : "PendingScan"));
        if (changed != 1) throw new DbUpdateConcurrencyException("Document changed during scan.");
        db.AuditLogs.Add(new() { Actor = "document-scan-worker", ContentId = item.ContentId, Action = "document.scanned", Detail = JsonSerializer.Serialize(new { asset.Id, asset.Sha256, asset.ByteLength, item.LeaseToken, item.Attempts, state = result.State.ToString(), result.Provider, result.EngineVersion, result.SignatureVersion, result.OutputSha256, result.Error, scanned }) });
        await db.SaveChangesAsync(); await finish.CommitAsync(); return true;
    }
}
