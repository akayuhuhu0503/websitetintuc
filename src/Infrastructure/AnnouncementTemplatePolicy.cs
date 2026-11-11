using LandPortal.Domain;

namespace LandPortal.Infrastructure;

// FR-05 uses the review workflow of FR-10, not the news article's image/sapo template.
public static class AnnouncementTemplatePolicy
{
    public static bool RequiresArticleFields(ContentKind kind) => kind == ContentKind.Article;

    public static bool IsPdfCandidate(MediaAsset asset, MediaUploadOptions options) =>
        asset.MimeType == "application/pdf" && asset.ByteLength > 0 &&
        !asset.IsQuarantined && asset.Origin is "Source" or "EditorialDocument" && options.CanServe(asset) &&
        (asset.ScanStatus=="Clean" || options.ApprovedManagedDocumentsWithoutAv && asset.Origin=="EditorialDocument"
            && asset.ScanStatus=="NotScanned" && asset.ValidationStatus==MediaUploadOptions.ManagedDocumentNoAvValidation);

    // The boolean must come from the persisted attachment and byte/proof checks,
    // never a posted form field or the count of selected media.
    public static string? ContentError(string plainText, bool verifiedPdf) =>
        string.IsNullOrWhiteSpace(plainText) && !verifiedPdf
            ? "Thông báo cần nội dung hoặc PDF đính kèm đã quét an toàn, đúng tệp và được phép sử dụng. Có thể lưu nháp khi chưa hoàn chỉnh."
            : null;
}
