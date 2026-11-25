namespace LandPortal.Infrastructure;

// Only authenticated editorial attachments use these bounds. Public/inquiry/media uploads keep their own limits.
public static class EditorialDocumentPolicy
{
    public const long OfficeMaximum = 10 * 1024 * 1024;
    public const long PdfMaximum = 104 * 1024 * 1024;
    public const long RequestMaximum = EditorialZipPolicy.Maximum + 1024 * 1024; // Multipart overhead; PDF stays 104 MiB.
    public const int PdfMaximumPages = 2000;
    public const int PdfValidationSeconds = 60;
    public const string SizeHelp = "PDF tối đa 104 MiB, 2.000 trang; DOCX/XLSX tối đa 10 MiB; ZIP tối đa 128 MiB. ZIP cần quét antivirus hoặc xác nhận nguồn nội bộ riêng của quản trị viên.";
    private static readonly SemaphoreSlim PdfSlot = new(1, 1);

    public static bool IsDocument(string mime) => mime == EditorialZipPolicy.Mime || OfficeDocumentPolicy.IsDocument(mime);
    public static string Mime(string name) => Path.GetExtension(name).Equals(".zip", StringComparison.OrdinalIgnoreCase) ? EditorialZipPolicy.Mime : OfficeDocumentPolicy.Mime(name);
    public static long MaximumFor(string mime) => mime == EditorialZipPolicy.Mime ? EditorialZipPolicy.Maximum : mime == "application/pdf" ? PdfMaximum : OfficeMaximum;
    public static void RequireSize(long bytes, string mime)
    {
        if (bytes < 1 || bytes > MaximumFor(mime)) throw new MediaUploadException(SizeHelp, 413);
    }

    public static Task<int?> ValidateAsync(string path, string mime, MediaService media, bool managedWithoutAv, CancellationToken ct = default, bool trustedInternal = false)
        => ValidateWithWarningsAsync(path, mime, media, managedWithoutAv, ct, trustedInternal, null);

    public static async Task<int?> ValidateWithWarningsAsync(string path, string mime, MediaService media, bool managedWithoutAv, CancellationToken ct, bool trustedInternal, ICollection<string>? warnings)
    {
        ct.ThrowIfCancellationRequested();
        RequireSize(new FileInfo(path).Length, mime);
        if (mime == EditorialZipPolicy.Mime)
        {
            if (managedWithoutAv && !trustedInternal) throw new MediaUploadException("ZIP không được áp dụng chính sách tài liệu không antivirus.");
            await EditorialZipPolicy.ValidateAsync(path, ct, trustedInternal);
            return null;
        }
        if (mime != "application/pdf")
        {
            OfficeDocumentPolicy.Validate(path, mime, media, managedWithoutAv);
            ct.ThrowIfCancellationRequested();
            return null;
        }
        // Do not accumulate concurrent large parsers or an unbounded waiting queue in the IIS process.
        if (!await PdfSlot.WaitAsync(0, ct)) throw new MediaUploadException("Đang kiểm định một PDF khác. Vui lòng thử lại sau khi tệp đó hoàn tất.", 429);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(PdfValidationSeconds));
            if (trustedInternal) { using var file = File.OpenRead(path); TrustedInternalPdfPolicy.Validate(file, budget.Token); }
            else if (managedWithoutAv) ManagedPdfPolicy.Validate(path, budget.Token);
            using var content = File.OpenRead(path);
            var pages = MediaService.ValidateEditorialPdfContent(content, PdfMaximumPages, budget.Token, trustedInternal, warnings);
            budget.Token.ThrowIfCancellationRequested();
            return pages;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new MediaUploadException("PDF vượt thời gian kiểm định 60 giây. Chưa tiếp nhận tệp; cần kiểm tra cấu trúc tài liệu.", 408);
        }
        finally { PdfSlot.Release(); }
    }
}
