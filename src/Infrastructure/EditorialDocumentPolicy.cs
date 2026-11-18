namespace LandPortal.Infrastructure;

// Only authenticated editorial attachments use these bounds. Public/inquiry/media uploads keep their own limits.
public static class EditorialDocumentPolicy
{
    public const long OfficeMaximum = 10 * 1024 * 1024;
    public const long PdfMaximum = 104 * 1024 * 1024;
    public const long RequestMaximum = PdfMaximum + 1024 * 1024; // Multipart overhead, not a larger file limit.
    public const int PdfMaximumPages = 2000;
    public const int PdfValidationSeconds = 60;
    public const string SizeHelp = "PDF tối đa 104 MiB, 2.000 trang; DOCX/XLSX tối đa 10 MiB.";
    private static readonly SemaphoreSlim PdfSlot = new(1, 1);

    public static long MaximumFor(string mime) => mime == "application/pdf" ? PdfMaximum : OfficeMaximum;
    public static void RequireSize(long bytes, string mime)
    {
        if (bytes < 1 || bytes > MaximumFor(mime)) throw new MediaUploadException(SizeHelp, 413);
    }

    public static async Task<int?> ValidateAsync(string path, string mime, MediaService media, bool managedWithoutAv, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        RequireSize(new FileInfo(path).Length, mime);
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
            if (managedWithoutAv) ManagedPdfPolicy.Validate(path, budget.Token);
            var pages = MediaService.ValidatePdfContent(path, PdfMaximumPages, budget.Token);
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
