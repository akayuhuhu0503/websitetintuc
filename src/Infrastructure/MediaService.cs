using System.Buffers;
using System.Security.Cryptography;
using LandPortal.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SkiaSharp;
using UglyToad.PdfPig;

namespace LandPortal.Infrastructure;

/// <summary>Validation is deliberately separate from malware scanning and public authorization.</summary>
public sealed class MediaUploadOptions
{
    public long MaxImageBytes { get; init; } = 16 * 1024 * 1024;
    public long MaxPdfBytes { get; init; } = 32 * 1024 * 1024;
    public long MaxImagePixels { get; init; } = 30_000_000;
    public long MaxDecodedFramePixels { get; init; } = 80_000_000;
    public int MaxImageFrames { get; init; } = 100;
    public int MaxPdfPages { get; init; } = 1000;
    public bool AllowUnscannedDevelopmentUploads { get; init; }
    public bool IsDevelopmentLoopback { get; init; }
    public bool DevelopmentExceptionEnabled => IsDevelopmentLoopback && AllowUnscannedDevelopmentUploads;
    public bool NativeUatUploads { get; init; }
    public bool UploadEnabled=>DevelopmentExceptionEnabled||NativeUatUploads;

    public static MediaUploadOptions FromConfiguration(IConfiguration configuration, string environmentName)
    {
        static long Number(IConfiguration c, string key, long fallback, long minimum, long maximum)
        {
            var raw = c["Media:" + key];
            if (raw is null) return fallback;
            if (!long.TryParse(raw, out var value) || value < minimum || value > maximum)
                throw new InvalidOperationException("Giới hạn Media__" + key + " không hợp lệ.");
            return value;
        }
        var explicitException = bool.TryParse(configuration["Media:AllowUnscannedDevelopmentUploads"], out var enabled) && enabled;
        var loopback = Uri.TryCreate(configuration["Public:BaseUrl"], UriKind.Absolute, out var uri) && uri.IsLoopback && uri.Scheme is "http" or "https";
        return new MediaUploadOptions
        {
            MaxImageBytes = Number(configuration, "MaxImageBytes", 16 * 1024 * 1024, 1024, 64 * 1024 * 1024),
            MaxPdfBytes = Number(configuration, "MaxPdfBytes", 32 * 1024 * 1024, 1024, 64 * 1024 * 1024),
            MaxImagePixels = Number(configuration, "MaxImagePixels", 30_000_000, 1, 50_000_000),
            MaxDecodedFramePixels = Number(configuration, "MaxDecodedFramePixels", 80_000_000, 1, 100_000_000),
            MaxImageFrames = (int)Number(configuration, "MaxImageFrames", 100, 1, 100),
            MaxPdfPages = (int)Number(configuration, "MaxPdfPages", 1000, 1, 5000),
            AllowUnscannedDevelopmentUploads = explicitException,
            IsDevelopmentLoopback = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) && loopback
            ,NativeUatUploads=UatDeployment.Validate(configuration,environmentName)
        };
    }

    public bool CanServe(MediaAsset asset) => asset.ValidationStatus == BatchMediaProof.Validation
        ? IsDevelopmentLoopback && asset.Origin == "Source" && !asset.IsQuarantined && asset.ScanStatus == "Clean"
        : asset.Origin == "EditorialDocument"
        ? !asset.IsQuarantined && asset.ScanStatus == "Clean" && asset.ValidationStatus == "Validated" && OfficeDocumentPolicy.IsDocument(asset.MimeType)
        : !asset.IsQuarantined && (asset.Origin is "Source" or "Uploaded")
        && (asset.ScanStatus is "NotScanned" or "Clean")
        && (asset.Origin != "Uploaded" || asset.ValidationStatus == "Validated")
        && (asset.ScanStatus == "Clean" || (asset.Origin == "Source" && IsDevelopmentLoopback) || (asset.Origin == "Uploaded" && DevelopmentExceptionEnabled));
}

public sealed class MediaUploadException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed record MediaUploadResult(MediaAsset Asset, bool Reused);

/// <summary>Stores immutable uploaded originals outside static roots. No network downloader or video path.</summary>
public sealed class MediaService(PortalDbContext db, StorageOptions storage, MediaUploadOptions options, IConfiguration? configuration=null)
{
    private static readonly Dictionary<string, string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".pdf"] = "application/pdf"
    };

    public async Task<MediaUploadResult> UploadAsync(Stream input, string originalName, string declaredMimeType, string userId,
        bool canManageAllMedia, CancellationToken cancellationToken = default, Func<Task>? validateEnvelope = null)
    {
        if (!options.UploadEnabled)
            throw new MediaUploadException("Chưa có máy quét mã độc. Upload chỉ mở khi bật ngoại lệ rõ ràng ở Development trên loopback; production vẫn bị chặn.", 503);
        ValidatePrivateStorageRoot(storage.MediaRoot);
        ValidatePrivateStorageRoot(storage.TempRoot);
        var name = SafeDisplayName(originalName);
        var extension = Path.GetExtension(name);
        if (!AllowedExtensions.TryGetValue(extension, out var expectedMime))
            throw new MediaUploadException("Chỉ nhận JPEG, PNG, GIF, WebP hoặc PDF; không nhận video, SVG, HTML hay tệp thực thi.");
        if(options.NativeUatUploads&&expectedMime=="application/pdf")throw new MediaUploadException("PDF dùng mục Tài liệu của bài để lưu cách ly và quét bằng worker.");
        var claimed = declaredMimeType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (claimed != expectedMime && claimed != "application/octet-stream")
            throw new MediaUploadException("MIME của tệp không khớp loại được phép.");
        var maximum = expectedMime == "application/pdf" ? options.MaxPdfBytes : options.MaxImageBytes;
        var incomingRoot = StorageOptions.ResolveWithin(storage.TempRoot, "media-incoming");
        ValidatePrivateStorageRoot(incomingRoot);
        Directory.CreateDirectory(incomingRoot);
        var temporaryPath = Path.Combine(incomingRoot, Guid.NewGuid().ToString("N") + ".upload");
        string? finalPath = null;
        Guid? newAssetId = null;
        var stored = false;
        var sqlSaveAttempted = false;
        try
        {
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            long length = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            try
            {
                await using var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                while (true)
                {
                    var count = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (count == 0) break;
                    length += count;
                    if (length > maximum) throw new MediaUploadException($"Tệp vượt giới hạn {maximum / (1024 * 1024)} MiB.", 413);
                    hash.AppendData(buffer, 0, count);
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
            if (length == 0) throw new MediaUploadException("Tệp rỗng không hợp lệ.");
            if (validateEnvelope is not null) await validateEnvelope();
            var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
            var (mime, width, height) = ValidateContent(temporaryPath, expectedMime, cancellationToken);
            // Equal bytes reuse the existing immutable original only within an authorized visibility scope.
            var duplicate = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Sha256 == digest, cancellationToken);
            if (duplicate is not null)
            {
                await EnsureDuplicateAccessible(duplicate, mime, userId, canManageAllMedia, cancellationToken);
                return new(duplicate, true);
            }
            var asset = new MediaAsset
            {
                Sha256 = digest, OriginalName = name, MimeType = mime, ByteLength = length,
                Width = width, Height = height, UploadedByUserId = userId, Origin = "Uploaded",
                ValidationStatus = "Validated", ScanStatus = "NotScanned", IsQuarantined = false
            };
            InquiryScanResult? scan=null;
            if(options.NativeUatUploads)
            {
                if(configuration==null||!UatDeployment.Validate(configuration,"Staging"))throw new MediaUploadException("Thiếu profile UAT được kiểm chứng.",503);
                var scanner=InquiryScannerFactory.Create(configuration,"Staging",maximum);
                await using var scanning=new FileStream(temporaryPath,FileMode.Open,FileAccess.Read,FileShare.Read);
                scan=await scanner.ScanAsync(scanning,cancellationToken);scanning.Position=0;
                if(Convert.ToHexStringLower(await SHA256.HashDataAsync(scanning,cancellationToken))!=digest||scan.State==LandPortal.Domain.InquiryScanState.Clean&&(scan.Provider!="Clamd"||scan.EngineVersion is not{Length:>0}))scan=new(LandPortal.Domain.InquiryScanState.ScanFailed,"ScannerProofOrBytesInvalid",null);
                asset.ScanStatus=scan.State.ToString();asset.IsQuarantined=scan.State!=LandPortal.Domain.InquiryScanState.Clean;asset.ValidationStatus=asset.IsQuarantined?"PendingScan":"Validated";
                if(validateEnvelope!=null)await validateEnvelope();
            }
            var canonicalExtension = mime == "image/jpeg" ? ".jpg" : extension.ToLowerInvariant();
            asset.StorageKey = (asset.IsQuarantined?"quarantine/uploads/":"uploads/") + asset.Id.ToString("N") + canonicalExtension;
            newAssetId = asset.Id;
            finalPath = ResolveMediaPath(storage, asset.StorageKey);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            System.IO.File.Move(temporaryPath, finalPath, overwrite: false);
            db.MediaAssets.Add(asset);
            db.AuditLogs.Add(new AuditLog
            {
                Actor = userId, Action = "UploadMedia", Detail = scan==null?$"asset={asset.Id};mime={mime};bytes={length};validation=Validated;scan=NotScanned;exception=DevelopmentLoopback":System.Text.Json.JsonSerializer.Serialize(new{asset.Id,asset.Sha256,asset.ByteLength,asset.ScanStatus,asset.ValidationStatus,scan.Provider,scan.EngineVersion,scan.Error,scope="UAT native synchronous image scan; not source promotion"})
            });
            try
            {
                sqlSaveAttempted = true;
                await db.SaveChangesAsync(cancellationToken);
                stored = true;
                if(asset.IsQuarantined)throw new MediaUploadException("Tệp đã được giữ trong kho cách ly, chưa được phép dùng. Mã: "+asset.Id,scan?.State==LandPortal.Domain.InquiryScanState.Infected?400:503);
                return new(asset, false);
            }
            catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 2601 or 2627)
            {
                // Two identical uploads can race the unique SHA256 index. Preserve the winner, remove only our new file.
                sqlSaveAttempted = false; // The failed unique insert was rolled back by SaveChanges' transaction.
                db.ChangeTracker.Clear();
                var winner = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Sha256 == digest, cancellationToken);
                if (winner is null) throw;
                await EnsureDuplicateAccessible(winner, mime, userId, canManageAllMedia, cancellationToken);
                return new(winner, true);
            }
        }
        finally
        {
            if (System.IO.File.Exists(temporaryPath)) System.IO.File.Delete(temporaryPath);
            if (!stored && finalPath is not null && System.IO.File.Exists(finalPath))
            {
                var remove = !sqlSaveAttempted;
                if (sqlSaveAttempted)
                {
                    // A cancelled/disconnected SQL commit can have an unknown result. Never remove a committed original.
                    try { remove = !await db.MediaAssets.AsNoTracking().AnyAsync(x => x.Id == newAssetId, CancellationToken.None); }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    { remove = false; /* Preserve a private orphan for operator reconciliation if SQL is still unavailable. */ }
                }
                if (remove) System.IO.File.Delete(finalPath);
            }
        }
    }

    private async Task EnsureDuplicateAccessible(MediaAsset asset, string actualMime, string userId, bool canManageAllMedia, CancellationToken cancellationToken)
    {
        if (!options.CanServe(asset)) throw new MediaUploadException("Tệp trùng đang bị cách ly hoặc chưa đủ điều kiện dùng.", 409);
        if (asset.MimeType != actualMime) throw new MediaUploadException("MIME của bản ghi tệp trùng không khớp định dạng thực; cần đối soát metadata.", 409);
        var existingPath = ResolveMediaPath(storage, asset.StorageKey);
        if (!System.IO.File.Exists(existingPath)) throw new MediaUploadException("Bản ghi tệp trùng thiếu tệp trong kho hiện tại; chưa xác nhận upload thành công.", 409);
        await using (var original = System.IO.File.OpenRead(existingPath))
        {
            if (original.Length != asset.ByteLength || Convert.ToHexStringLower(await SHA256.HashDataAsync(original, cancellationToken)) != asset.Sha256)
                throw new MediaUploadException("Byte/hash tệp trùng trong kho không khớp bản gốc; cần kiểm tra kho.", 409);
        }
        if (asset.UploadedByUserId == userId || canManageAllMedia) return;
        if (await db.ContentAssets.AnyAsync(x => x.MediaAssetId == asset.Id
            && db.Contents.Any(c => c.IsPublic && !c.IsTrashed && c.PublishedRevisionId == x.RevisionId), cancellationToken)) return;
        throw new MediaUploadException("Tệp trùng thuộc phạm vi riêng tư của người khác. Không cấp quyền bằng cách tải lại byte giống nhau.", 403);
    }

    public (string Mime, int? Width, int? Height) ValidateContent(string path, string expectedMime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedMime == "application/pdf")
        {
            using var file = System.IO.File.OpenRead(path);
            Span<byte> prefix = stackalloc byte[8];
            if (file.Read(prefix) < 8 || !prefix[..5].SequenceEqual("%PDF-"u8))
                throw new MediaUploadException("Tệp không có định dạng PDF hợp lệ.");
            file.Seek(-Math.Min(file.Length, 2048), SeekOrigin.End);
            var trailer = new byte[Math.Min(file.Length, 2048)];
            file.ReadExactly(trailer);
            if (!System.Text.Encoding.ASCII.GetString(trailer).Contains("%%EOF", StringComparison.Ordinal))
                throw new MediaUploadException("PDF bị thiếu phần kết thúc hoặc bị cắt dở.");
            try
            {
                using var pdf = PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = false, MaxStackDepth = 64 });
                if (pdf.NumberOfPages < 1 || pdf.NumberOfPages > options.MaxPdfPages)
                    throw new MediaUploadException("Số trang PDF vượt giới hạn hoặc không có trang hợp lệ.");
                // Resolve every page through the actual parser; a magic prefix alone never passes validation.
                for (var number = 1; number <= pdf.NumberOfPages; number++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var page = pdf.GetPage(number);
                    if (page.Width <= 0 || page.Height <= 0 || page.Width > 144_000 || page.Height > 144_000)
                        throw new MediaUploadException("Kích thước trang PDF không hợp lệ.");
                }
                return ("application/pdf", null, null);
            }
            catch (MediaUploadException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is not OutOfMemoryException)
            { throw new MediaUploadException("PDF không đọc được bằng bộ phân tích; tệp lỗi hoặc cần mật khẩu."); }
        }
        using var codec = SKCodec.Create(path);
        if (codec is null) throw new MediaUploadException("Không giải mã được ảnh; không chỉ kiểm tra đuôi tệp.");
        var actualMime = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => "image/jpeg", SKEncodedImageFormat.Png => "image/png",
            SKEncodedImageFormat.Gif => "image/gif", SKEncodedImageFormat.Webp => "image/webp", _ => ""
        };
        if (actualMime != expectedMime) throw new MediaUploadException("Định dạng ảnh thực không khớp đuôi/MIME.");
        var info = codec.Info;
        var pixels = (long)info.Width * info.Height;
        var frames = Math.Max(1, codec.FrameCount);
        if (info.Width < 1 || info.Height < 1 || pixels > options.MaxImagePixels || frames > options.MaxImageFrames
            || pixels * frames > options.MaxDecodedFramePixels)
            throw new MediaUploadException("Kích thước ảnh hoặc tổng số điểm ảnh/khung ảnh vượt giới hạn.", 413);
        var decodedInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(decodedInfo);
        for (var frame = 0; frame < frames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (codec.GetPixels(decodedInfo, bitmap.GetPixels(), new SKCodecOptions(frame)) != SKCodecResult.Success)
                throw new MediaUploadException("Dữ liệu ảnh/khung ảnh bị lỗi hoặc bị cắt dở.");
        }
        return (actualMime, info.Width, info.Height);
    }

    private static string SafeDisplayName(string value)
    {
        // The client name is display metadata only. It never contributes to a storage path.
        var name = Path.GetFileName(value.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl))
            throw new MediaUploadException("Tên hiển thị của tệp không hợp lệ hoặc quá dài.");
        return name;
    }

    private static void ValidatePrivateStorageRoot(string root)
    {
        var segments = Path.GetFullPath(root).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(x => x.Equals("wwwroot", StringComparison.OrdinalIgnoreCase) || x.Equals("publish", StringComparison.OrdinalIgnoreCase)))
            throw new MediaUploadException("Kho upload/temp phải nằm ngoài wwwroot và thư mục publish.", 503);
        // Do not follow a pre-existing reparse point into an unrelated or publicly served tree.
        for (var directory = new DirectoryInfo(Path.GetFullPath(root)); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new MediaUploadException("Kho upload/temp không được đi qua reparse point.", 503);
    }

    public static string ResolveMediaPath(StorageOptions storage, string key)
    {
        var path = storage.ResolveMedia(key);
        ValidatePrivateStorageRoot(Path.GetDirectoryName(path)!);
        if (System.IO.File.Exists(path) && (System.IO.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new MediaUploadException("Tệp media không được là reparse point.", 503);
        return path;
    }
}
