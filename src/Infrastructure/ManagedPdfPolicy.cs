namespace LandPortal.Infrastructure;

// Managed CMS documents retain their own no-AV status, strict page decoder and
// 100k token / 64 depth bounds. Inert signatures, GoTo and HTTP(S) links are not
// executable code. Reuse structural validation, not TrustedInternal provenance
// or its page-recovery mode. Embedded files, scripts and auto-actions still fail.
public static class ManagedPdfPolicy
{
    public static void Validate(string path, CancellationToken ct = default)
    {
        try
        {
            using var input=File.OpenRead(path);
            TrustedInternalPdfPolicy.ValidatePassiveStructure(input,100_000,ct);
        }
        catch(Exception error)when(error is not(OutOfMemoryException or OperationCanceledException))
        {throw new InvalidDataException("PDF chưa đạt cấu trúc an toàn [PDF_STRUCTURE]; không phải kết quả quét antivirus.",error);}
    }
}
