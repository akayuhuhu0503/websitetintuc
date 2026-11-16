using System.Text.Json;
using System.Text.RegularExpressions;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.MigrationCli;

namespace LandPortal.Infrastructure;

public sealed class SourceSyncSettings
{
    public bool Enabled { get; set; }
    public Dictionary<int,int> Categories { get; set; } = [];
    public int Cursor { get; set; }
    public DateTimeOffset? LastCheckedUtc { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }
}
public sealed class SourceSyncCheckpoint
{
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "";
    public string Hash { get; set; } = "";
    public string BaselineHash { get; set; } = "";
    public bool BaselineObservedOnly { get; set; }
    public ParsedSource? Source { get; set; }
    public List<SourceSyncAsset> Assets { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public Guid? ContentId { get; set; }
    public DateTimeOffset? CheckedUtc { get; set; }
}
public sealed record SourceSyncAsset(string Url, string Key, string Sha256, long Bytes, string Mime, string Name);
public static class SourceSyncPolicy
{
    public const string SettingsKey = "SourceSync.QLDD";
    public const string SweepKey = "SourceSync.QLDD.Sweep";
    public const string EntryType = "QlddSyncEntry";
    public const string SweepType = "QlddSyncSweep";
    public const string RunStatus = "QlddSyncCheckpoint";
    public const int RecentWindow = 20, DetailsPerRun = 3, RequestsPerRun = 32, MaxAssets = 12, QueueLimit = 200;
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value);
    public static T Read<T>(string json) where T:new() => string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException("Checkpoint trống.");
    public static string Key(string id) => "SourceSync.QLDD.news."+id;
    public static string? Id(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var u)||u.Host!="qldd.mae.gov.vn"||u.UserInfo!=""||!u.IsDefaultPort||u.Scheme is not ("https" or "http")) return null;
        if(u.AbsolutePath.Equals("/Pages/chi-tiet-tin-tuc.aspx",StringComparison.OrdinalIgnoreCase))
        {
            var q=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(u.Query);
            return q.TryGetValue("ItemID",out var ids)&&ids.Count==1&&Regex.IsMatch(ids[0]??"","^[1-9][0-9]{0,9}$")?ids[0]:null;
        }
        var m=Regex.Match(u.AbsolutePath,@"^/(?!chuyen-muc/)[a-z0-9-]+-([1-9][0-9]{0,9})\.htm$",RegexOptions.IgnoreCase);
        return m.Success?m.Groups[1].Value:null;
    }
    public static string Hash(ParsedSource source) => TextNormalizer.Hash(Serialize(new{source.Title,source.Summary,source.BodyHtml,source.Author,source.OriginalDateText,source.PrimaryCategoryId,source.AssetUrls}));
    public static string Label(string status) => status switch {
        "Queued"=>"Mới phát hiện", "Ready"=>"Chờ biên tập", "Review"=>"Cần kiểm tra", "Changed"=>"Nguồn đã cập nhật",
        "Drafted"=>"Đã chuyển vào CMS", "Existing"=>"Đã có trên GDLA", "Ignored"=>"Đã bỏ qua", "Error"=>"Lỗi thu thập", _=>"Đang kiểm tra"
    };
}
