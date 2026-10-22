using System.Text;
using System.Text.RegularExpressions;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;

namespace LandPortal.MigrationCli;

// Source-local keys are never interchangeable, even when titles or numeric ids match.
public sealed record MigrationSourceIdentity(string Source, string Module, ContentKind Kind, string LocalId)
{
    public string Key => $"{Source}/{Module}/{Kind}/{LocalId}";
    public string SqlSourceId => Module == "sharepoint" ? LocalId : Module + ":" + LocalId;
}
public sealed record SecondarySourcePreview(MigrationSourceIdentity Identity, string Url, string CanonicalUrl,
    string RawSha256, string Title, string BodyHtml, string BodyTextSha256, string[] AssetUrls,
    string CategoryMappingStatus = "NEEDS_APPROVAL", string PublicationStatus = "PrivateInventoryOnly",
    IReadOnlyDictionary<string,string>? RawMetadata = null);

public static class MigrationSourceIdentityPolicy
{
    public static MigrationSourceIdentity? FromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || !u.IsDefaultPort || u.UserInfo.Length != 0 || u.Query.Length != 0 || u.Fragment.Length != 0) return null;
        if (!u.Host.Equals("gdla.gov.vn", StringComparison.OrdinalIgnoreCase)) return null;
        // Public news paths only. Other NukeViet modules/export schemas still need real samples.
        var m = Regex.Match(u.AbsolutePath, @"^/(?<language>vi/)?(?<module>news)/[a-z0-9-]+/[a-z0-9-]+-(?<id>[1-9][0-9]{0,9})\.html$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if(m.Success)return new("gdla.gov.vn", (m.Groups["language"].Value + m.Groups["module"].Value).ToLowerInvariant(), ContentKind.Article, m.Groups["id"].Value);
        // The real 22-Oct snapshots configure news as the root module. Category
        // aliases must share its namespace; HTML evidence is required below.
        m=Regex.Match(u.AbsolutePath,@"^/(?<category>[a-z0-9-]+)/[a-z0-9-]+-(?<id>[1-9][0-9]{0,9})\.html$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        return m.Success&&!new[]{"about","laws","users","contact","print","savefile","search","rss","banners"}.Contains(m.Groups["category"].Value.ToLowerInvariant())
            ?new("gdla.gov.vn","news",ContentKind.Article,m.Groups["id"].Value):null;
    }
    public static SecondarySourcePreview ParseNukeViet(string html, string url, string rawSha256)
    {
        var identity = FromUrl(url) ?? throw new InvalidDataException("Unsupported read-only NukeViet detail route; not an import permission.");
        if (!Regex.IsMatch(rawSha256, "^[a-f0-9]{64}$")) throw new InvalidDataException("Exact raw SHA required.");
        var doc = SourceParser.Document(html);
        var rootNews=!new Uri(url).AbsolutePath.StartsWith("/news/",StringComparison.OrdinalIgnoreCase)&&!new Uri(url).AbsolutePath.StartsWith("/vi/news/",StringComparison.OrdinalIgnoreCase);
        if(rootNews&&(!Regex.IsMatch(html,@"\bnv_module_name\s*=\s*['""]news['""]",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))||!Regex.IsMatch(html,@"\bnv_func_name\s*=\s*['""]detail['""]",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))))
            throw new InvalidDataException("Root news route lacks actual NukeViet news/detail module evidence.");
        var rawCanonical = doc.QuerySelector("link[rel=canonical]")?.GetAttribute("href");
        if (rawCanonical is null || !Uri.TryCreate(new Uri(url), rawCanonical, out var canonical) || FromUrl(canonical.AbsoluteUri) != identity)
            throw new InvalidDataException("Canonical source/module/id is missing or conflicts with NukeViet route.");
        var body = doc.QuerySelector("#news-bodyhtml") ?? throw new InvalidDataException("No verified NukeViet body adapter; navigation cannot substitute content.");
        var title = (doc.QuerySelector("h1[itemprop=headline]")??(rootNews?null:doc.QuerySelector("h1")))?.TextContent.Trim() ?? "";
        if(rootNews&&doc.QuerySelector("meta[property='og:title']")?.GetAttribute("content")?.Trim()!=title)throw new InvalidDataException("NukeViet headline and article title metadata conflict.");
        var text = NormalizeText(body.InnerHtml);
        if (title.Length == 0 || text.Length == 0) throw new InvalidDataException("Missing NukeViet title or body.");
        var lead=doc.QuerySelector(".news_column [itemprop=image]")?.TextContent.Trim();
        if(lead?.Contains("/themes/default/images/no_image.gif",StringComparison.OrdinalIgnoreCase)==true)lead=null; // Source's no-image placeholder is not migrated content.
        var assets = body.QuerySelectorAll("img[src],a[href]").Select(x => x.GetAttribute(x.LocalName == "img" ? "src" : "href")).Concat([lead])
            .Where(x => x != null).Select(x => Uri.TryCreate(canonical, x, out var a) ? a : null)
            .Where(x => x != null && x.Scheme is "https" or "http" && Regex.IsMatch(x.AbsolutePath, @"\.(jpe?g|png|gif|webp|pdf|docx?|xlsx?|mp4)$", RegexOptions.IgnoreCase))
            .Select(x => x!.AbsoluteUri).Distinct().Order().ToArray();
        var metadata=new Dictionary<string,string>();
        foreach(var name in new[]{"datePublished","dateModified","mainEntityOfPage"})if(doc.QuerySelector(".news_column [itemprop="+name+"]")?.TextContent.Trim() is{Length:>0}value)metadata[name]=value;
        foreach(var name in new[]{"article:published_time","article:modified_time","article:section","og:description"})if(doc.QuerySelector("meta[property='"+name+"']")?.GetAttribute("content")is{Length:>0}value)metadata[name]=value;
        if(doc.QuerySelector(".news_column [itemprop=author] [itemprop=name]")?.TextContent.Trim() is{Length:>0}author)metadata["author"]=author;
        return new(identity, url, canonical.AbsoluteUri, rawSha256, title, body.InnerHtml, SourceInventory.HashText(text), assets,RawMetadata:metadata);
    }
    public static string NormalizeText(string html) => Regex.Replace(new HtmlPolicy().PlainText(html).Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
    public static string DuplicateEvidence(SecondarySourcePreview a, ParsedSource b)
    {
        // Suggestions only: never merge identities or choose a publication automatically.
        var bodyMatch = a.BodyTextSha256 == SourceInventory.HashText(NormalizeText(b.BodyHtml));
        if (bodyMatch) return "BODY_MATCH_NEEDS_REVIEW";
        return a.Title.Normalize(NormalizationForm.FormC).Equals(b.Title.Normalize(NormalizationForm.FormC), StringComparison.OrdinalIgnoreCase)
            ? "TITLE_ONLY_NOT_DUPLICATE" : "NO_MATCH";
    }
}
