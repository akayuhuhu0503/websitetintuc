using LandPortal.Domain;

namespace LandPortal.MigrationCli;
public sealed class SampleManifest
{
    public int SchemaVersion { get; set; } = 1;
    public Guid CrawlRunId { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedUtc { get; set; }
    public string Status { get; set; } = "Running";
    public int RequestBudget { get; set; }
    public int RequestsUsed { get; set; }
    public int ContentBudget { get; set; }
    public List<SnapshotEntry> Snapshots { get; set; } = [];
    public List<StagedContent> Contents { get; set; } = [];
    public List<AssetEntry> Assets { get; set; } = [];
    public List<DiscoveredLink> Discovered { get; set; } = [];
    public List<IssueEntry> Issues { get; set; } = [];
    public List<ListEvidence> Lists { get; set; } = [];
    public Dictionary<string, PipelineCheckpoint> ItemStages { get; set; } = [];
}
public sealed record PipelineCheckpoint(string Stage, int Attempts, DateTimeOffset UpdatedUtc, string? Error = null);
public sealed record ListEvidence(string Url, int? CategoryId, PaginationEvidence Pagination, List<string> SourceIds);
public sealed record IssueEntry(string SourceUrl, string Stage, string Code, string Message, bool Resolved = false);
public sealed record SnapshotEntry(string Url, string FinalUrl, int Status, string ContentType, string StorageKey, string Sha256, long Bytes, DateTimeOffset FetchedUtc);
public sealed class StagedContent
{
    public required ParsedSource Source { get; set; }
    public string Status { get; set; } = "Staged";
    public string ContentHash { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public List<Guid> AssetIds { get; set; } = [];
    public int Attempts { get; set; } = 1;
}
public sealed record AssetEntry(Guid Id, string SourceUrl, string StorageKey, string Sha256, long Bytes, string MimeType, string OriginalName);
public static class SourceCatalog
{
    public static readonly (int Id, string Name, string Slug, string Path)[] Categories =
    [
        (75,"Xây dựng, phổ biến chính sách, pháp luật","xay-dung-pho-bien-chinh-sach-phap-luat","/chuyen-muc/site-xay-dung-pho-bien-chinh-sach-phap-luat-75.htm"),
        (83,"Đo đạc và Đăng ký đất đai","do-dac-va-dang-ky-dat-dai","/chuyen-muc/site-do-dac-va-dang-ky-dat-dai-83.htm"),
        (82,"Dữ liệu và Thông tin đất đai","du-lieu-va-thong-tin-dat-dai","/chuyen-muc/site-co-so-du-lieu-quoc-gia-ve-dat-dai-82.htm"),
        (81,"Thống kê, kiểm kê đất đai","thong-ke-kiem-ke-dat-dai","/chuyen-muc/site-thong-ke-kiem-ke-dat-dai-81.htm"),
        (55,"Điều tra, đánh giá tài nguyên đất","dieu-tra-danh-gia-tai-nguyen-dat","/chuyen-muc/dieu-tra-danh-gia-tai-nguyen-dat-55.htm"),
        (49,"Giao đất, cho thuê đất, chuyển mục đích sử dụng đất","giao-dat-cho-thue-dat-chuyen-muc-dich-su-dung-dat","/chuyen-muc/giao-dat-cho-thue-dat-chuyen-muc-dich-su-dung-dat-49.htm"),
        (48,"Quy hoạch, kế hoạch sử dụng đất","quy-hoach-ke-hoach-su-dung-dat","/chuyen-muc/quy-hoach-ke-hoach-su-dung-dat-48.htm"),
        (53,"Tin tức hoạt động","tin-tuc-hoat-dong","/chuyen-muc/tin-tuc-hoat-dong-53.htm"),
        (51,"Phát triển tài nguyên đất","phat-trien-tai-nguyen-dat","/chuyen-muc/phat-trien-tai-nguyen-dat-51.htm"),
        (47,"Tin hoạt động địa phương","tin-hoat-dong-dia-phuong","/chuyen-muc/tin-hoat-dong-dia-phuong-47.htm"),
        (50,"Giá đất","gia-dat","/chuyen-muc/gia-dat-50.htm"),
        (57,"Bồi thường, hỗ trợ, tái định cư","boi-thuong-ho-tro-tai-dinh-cu","/chuyen-muc/boi-thuong-ho-tro-tai-dinh-cu-57.htm"),
        (74,"Thanh tra, kiểm tra đất đai","thanh-tra-kiem-tra-dat-dai","/chuyen-muc/thanh-tra-kiem-tra-dat-dai-74.htm"),
        (56,"Công tác hoạt động Đảng, Đoàn thể","cong-tac-hoat-dong-dang-doan-the","/chuyen-muc/cong-tac-hoat-dong-dang-doan-the-56.htm"),
        (52,"Thông báo","thong-bao","/chuyen-muc/thong-bao-52.htm"),
        (79,"Sổ tay điện tử đất đai","so-tay-dien-tu","/chuyen-muc/so-tay-dien-tu-79.htm"),
        (77,"Quy hoạch sử dụng đất","quy-hoach-su-dung-dat","/chuyen-muc/quy-hoach-su-dung-dat-77.htm"),
        (76,"Lịch tuần","lich-tuan","/chuyen-muc/site-lich-tuan-76.htm")
    ];
    public const string BaseUrl = "https://qldd.mae.gov.vn";
}
