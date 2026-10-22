using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using LandPortal.Domain;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LandPortal.MigrationCli;

public sealed record DiscoveredLink(string Url, string SourceId, ContentKind Kind, int? CategoryId = null);
public sealed record PaginationEvidence(int StartPage, int TotalPages, string NextTemplate);
public sealed class ParsedSource
{
    public string SourceId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string CanonicalUrl { get; set; } = "";
    public ContentKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public string Author { get; set; } = "";
    public string OriginalDateText { get; set; } = "";
    public DateOnly? PublishedDate { get; set; }
    public DateTimeOffset? PublishedUtc { get; set; }
    public string DatePrecision { get; set; } = "Unknown";
    public int? PrimaryCategoryId { get; set; }
    public List<int> CategoryIds { get; set; } = [];
    public Dictionary<string, string> Fields { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> AssetUrls { get; set; } = [];
}

public static partial class SourceParser
{
    public static IDocument Document(string html) => new HtmlParser().ParseDocument(html);
    public static List<string> EmbeddedDocumentIds(string html)
    {
        var match = Regex.Match(html, @"odata\s*=\s*(\{[^\r\n]+\});");
        if (!match.Success) return [];
        using var json = JsonDocument.Parse(match.Groups[1].Value);
        return json.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("ID").GetInt32().ToString(CultureInfo.InvariantCulture)).ToList();
    }
    public static IEnumerable<DiscoveredLink> DiscoverNewsLinks(string html, string url, int? categoryId = null)
    {
        var document = Document(html);
        var scope = document.QuerySelector("#KhuVuc_DanhSachTin") ?? document.QuerySelector(".boxleft") ?? document.Body;
        if (scope is null) yield break;
        var seen = new HashSet<string>();
        foreach (var anchor in scope.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href") ?? "";
            if (href.Contains("/chuyen-muc/", StringComparison.OrdinalIgnoreCase)) continue;
            var match = ArticleId().Match(href);
            if (!match.Success || !Uri.TryCreate(new Uri(url), href, out var target) || target.Host != "qldd.mae.gov.vn") continue;
            var id = match.Groups[1].Value;
            if (!seen.Add(id)) continue;
            yield return new(target.AbsoluteUri, id, categoryId == 52 ? ContentKind.Announcement : ContentKind.Article, categoryId);
        }
    }
    public static PaginationEvidence ReadPagination(string html)
    {
        var start = Regex.Match(html, @"startPage:\s*(\d+)");
        var total = Regex.Match(html, @"totalPages:\s*(\d+)");
        var template = Regex.Match(html, "location\\.href\\s*=\\s*location\\.origin\\s*\\+\\s*\"([^\"]+)\"\\s*\\+\\s*page\\s*\\+\\s*\"([^\"]*)\"");
        return new(start.Success ? int.Parse(start.Groups[1].Value) : 1,
            total.Success ? int.Parse(total.Groups[1].Value) : 0,
            template.Success ? template.Groups[1].Value + "{page}" + template.Groups[2].Value : html.Contains("location.pathname + \"?Page=\" + page") ? "?Page={page}" : "");
    }
    public static ParsedSource Parse(string html, string url, ContentKind kind, string sourceId)
    {
        var document = Document(html);
        var result = new ParsedSource { SourceId = sourceId, SourceUrl = url, Kind = kind };
        var canonical = document.QuerySelector("link[rel=canonical]")?.GetAttribute("href");
        result.CanonicalUrl = canonical is null ? url : new Uri(new Uri(url), canonical).AbsoluteUri;
        var canonicalId = Regex.Match(result.CanonicalUrl, @"[?&]ItemID=(\d+)", RegexOptions.IgnoreCase);
        if (canonicalId.Success) result.SourceId = canonicalId.Groups[1].Value;
        // Old inventory hints sometimes classified a news title mentioning a draft as a draft document.
        // Correct only from the source's own canonical route AND its nonempty verified news selectors.
        if (kind is ContentKind.DraftDocument or ContentKind.LegalDocument
            && canonical is not null && new Uri(result.CanonicalUrl).Host == "qldd.mae.gov.vn"
            && new Uri(result.CanonicalUrl).AbsolutePath.Equals("/Pages/chi-tiet-tin-tuc.aspx", StringComparison.OrdinalIgnoreCase)
            && canonicalId.Success && document.QuerySelector("table.contentvbdh") is null
            && !string.IsNullOrWhiteSpace(document.QuerySelector("h1.tieu-de-tin-tuc")?.TextContent)
            && !string.IsNullOrWhiteSpace(document.QuerySelector("article.noi-dung-chi-tiet-tin")?.TextContent))
        {
            kind = ContentKind.Article; result.Kind = kind;
            result.Warnings.Add("Legacy type hint corrected from verified QLDD news canonical/title/body; not a draft document.");
        }
        if (kind is ContentKind.Article or ContentKind.Announcement or ContentKind.StaticPage)
        {
            result.Title = document.QuerySelector("h1.tieu-de-tin-tuc")?.TextContent.Trim() ?? document.QuerySelector("h1")?.TextContent.Trim() ?? "";
            result.Summary = document.QuerySelector(".sapo-chi-tiet-tin")?.TextContent.Trim() ?? "";
            var body = document.QuerySelector("article.noi-dung-chi-tiet-tin") ?? document.QuerySelector("#News-content article");
            if (body is null) throw new InvalidDataException("Verified article body selector not found; sidebar cannot substitute content.");
            result.BodyHtml = body.InnerHtml;
            var attachments = document.QuerySelector("#dinhkem");
            if (attachments is not null) result.BodyHtml += "<div><h2>File đính kèm</h2>" + attachments.OuterHtml + "</div>";
            result.Author = document.QuerySelector("#News-content")?.ParentElement?.QuerySelector("p.text-end strong")?.TextContent.Trim() ?? "";
            result.OriginalDateText = document.QuerySelector(".thoi-gian-tao-chi-tiet-tin")?.TextContent.Trim() ?? "";
            SetDate(result, result.OriginalDateText);
            var back = document.QuerySelector(".chi-tiet-tin-back-icon[href]")?.GetAttribute("href") ?? "";
            var category = CategoryId().Match(back);
            if (category.Success) result.PrimaryCategoryId = int.Parse(category.Groups[1].Value);
        }
        else if (kind is ContentKind.LegalDocument or ContentKind.DraftDocument)
        {
            var table = document.QuerySelector("table.contentvbdh") ?? throw new InvalidDataException("Verified document metadata table not found.");
            foreach (var row in table.QuerySelectorAll("tr"))
            {
                var cells = row.QuerySelectorAll("td");
                for (var i = 0; i + 1 < cells.Length; i += 2) result.Fields[cells[i].TextContent.Trim()] = cells[i + 1].TextContent.Trim();
            }
            result.Title = result.Fields.GetValueOrDefault("Trích yếu", "");
            result.Summary = result.Fields.GetValueOrDefault("Số ký hiệu", result.Fields.GetValueOrDefault("Số hiệu", ""));
            result.BodyHtml = table.OuterHtml;
            // Issue dates / consultation dates are preserved as typed metadata, never relabelled as publication dates.
            result.OriginalDateText = "";
        }
        else if (kind == ContentKind.Video)
        {
            var json = Regex.Match(html, @"var\s+lisstData\s*=\s*(\{[^\r\n]+\});");
            if (!json.Success) throw new InvalidDataException("Verified embedded public video metadata not found.");
            using var data = JsonDocument.Parse(json.Groups[1].Value);
            var entry = data.RootElement.GetProperty("data").EnumerateArray().First(x => x.GetProperty("ID").GetInt32().ToString(CultureInfo.InvariantCulture) == result.SourceId);
            result.Title = entry.GetProperty("Title").GetString()?.Trim() ?? "";
            result.Summary = entry.GetProperty("DescriptionNews").GetString()?.Trim() ?? "";
            result.Fields["SourceCreatedDate"] = entry.GetProperty("CreatedDate").GetString() ?? "";
            var displayedDate = Regex.Match(document.QuerySelector(".lit-pubinfo")?.TextContent ?? "", @"\d{1,2}/\d{1,2}/\d{4}");
            result.OriginalDateText = displayedDate.Success ? displayedDate.Value : "";
            SetDate(result, result.OriginalDateText);
            var sourceFile = entry.GetProperty("ContentNews").GetString() ?? "";
            result.Fields["VideoSource"] = sourceFile;
            result.Fields["VideoClassification"] = sourceFile.Length == 0 ? "MissingFile" : sourceFile.Contains("youtube", StringComparison.OrdinalIgnoreCase) ? "ExternalEmbed" : "DirectFile";
            var poster = entry.GetProperty("ImageNew").GetString();
            if (!string.IsNullOrWhiteSpace(poster)) result.BodyHtml = "<figure><img src=\"" + System.Net.WebUtility.HtmlEncode(poster) + "\" alt=\"\"></figure>";
            if (sourceFile.Length > 0 && Uri.TryCreate(sourceFile, UriKind.Absolute, out var file) && SafeSourceClient.AllowedHosts.Contains(file.Host)) result.AssetUrls.Add(sourceFile);
            else result.Warnings.Add("VIDEO_FILE_UNAVAILABLE_OR_OUT_OF_SCOPE: " + (sourceFile.Length == 0 ? "Source supplied no video file" : sourceFile));
        }
        if (string.IsNullOrWhiteSpace(result.Title)) throw new InvalidDataException("Source supplied no verified title.");
        if (result.PrimaryCategoryId is int primary) result.CategoryIds.Add(primary);
        var bodyDocument = Document(result.BodyHtml);
        foreach (var element in bodyDocument.QuerySelectorAll("script,style,iframe,object,embed,form")) element.Remove();
        foreach (var element in bodyDocument.QuerySelectorAll("[src],[href],[data-src],[srcset]"))
        {
            foreach (var attr in new[] { "src", "data-src", "href" })
            {
                var value = element.GetAttribute(attr);
                if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(new Uri(url), value, out var target)) continue;
                if (attr == "href" && !Regex.IsMatch(target.AbsolutePath, @"\.(pdf|docx?|xlsx?|zip|mp4)$", RegexOptions.IgnoreCase)) continue;
                result.AssetUrls.Add(target.AbsoluteUri);
            }
            var srcset = element.GetAttribute("srcset");
            if (!string.IsNullOrEmpty(srcset)) foreach (var candidate in SourceSrcset.Urls(srcset, element.GetAttribute("src"), element.GetAttribute("data-src")))
            {
                if (Uri.TryCreate(new Uri(url), candidate, out var target)) result.AssetUrls.Add(target.AbsoluteUri);
            }
        }
        result.AssetUrls = result.AssetUrls.Distinct(StringComparer.Ordinal).ToList();
        result.BodyHtml = bodyDocument.Body?.InnerHtml ?? "";
        if (result.AssetUrls.Count == 0 && string.IsNullOrWhiteSpace(bodyDocument.Body?.TextContent)) result.Warnings.Add("EMPTY_BODY");
        return result;
    }
    public static void SetDate(ParsedSource result, string text)
    {
        string[] formats = ["HH:mm dd/MM/yyyy", "dd/MM/yyyy HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "dd/MM/yyyy", "d/M/yyyy"];
        if (!DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return;
        result.PublishedDate = DateOnly.FromDateTime(parsed);
        result.DatePrecision = Regex.IsMatch(text, @"\d{2}:\d{2}:\d{2}") ? "Second" : text.Contains(':') ? "Minute" : "Day";
        if (result.DatePrecision is "Minute" or "Second") result.PublishedUtc = new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).ToUniversalTime();
    }
    [GeneratedRegex(@"-(\d+)\.htm(?:\?|$)", RegexOptions.IgnoreCase)] private static partial Regex ArticleId();
    [GeneratedRegex(@"-(\d+)(?:-p\d+)?\.htm", RegexOptions.IgnoreCase)] private static partial Regex CategoryId();
}
