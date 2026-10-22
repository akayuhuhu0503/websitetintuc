using Ganss.Xss;
using AngleSharp.Html.Parser;
namespace LandPortal.Infrastructure;
public sealed class HtmlPolicy
{
    public string Clean(string html)
    {
        // Source markup uses harmless wrappers such as article, section and font.
        // Removing a wrapper must retain its text, images and tables. Active subtrees
        // are removed before enabling child retention so script/style text stays absent.
        var input = new HtmlParser().ParseDocument(html);
        foreach (var active in input.QuerySelectorAll("script,style,iframe,object,embed,form,template,svg,math")) active.Remove();
        var sanitizer = new HtmlSanitizer { KeepChildNodes = true };
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "strong", "b", "em", "i", "u", "s", "h2", "h3", "h4", "ul", "ol", "li", "blockquote", "a", "img", "figure", "figcaption", "table", "caption", "colgroup", "col", "thead", "tbody", "tfoot", "tr", "td", "th", "div", "span", "sub", "sup", "hr" }) sanitizer.AllowedTags.Add(tag);
        sanitizer.AllowedAttributes.Clear();
        foreach (var attr in new[] { "href", "src", "alt", "title", "colspan", "rowspan", "scope", "span", "width", "height" }) sanitizer.AllowedAttributes.Add(attr);
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("https"); sanitizer.AllowedSchemes.Add("http"); sanitizer.AllowedSchemes.Add("mailto");
        var document = new HtmlParser().ParseDocument(sanitizer.Sanitize(input.Body?.InnerHtml ?? ""));
        foreach (var img in document.QuerySelectorAll("img"))
        {
            var src = img.GetAttribute("src") ?? "";
            if (!src.StartsWith("/media/", StringComparison.Ordinal) || !Guid.TryParseExact(src[7..], "D", out _)) { img.Remove(); continue; }
            img.SetAttribute("loading", "lazy");
            img.SetAttribute("decoding", "async");
        }
        foreach (var link in document.QuerySelectorAll("a"))
        {
            var href = link.GetAttribute("href") ?? "";
            if (href.StartsWith("//") || href.StartsWith('\\')) { link.RemoveAttribute("href"); continue; }
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") link.SetAttribute("rel", "noopener noreferrer");
        }
        return document.Body?.InnerHtml ?? "";
    }
    public string PlainText(string html) => new HtmlParser().ParseDocument(html).Body?.TextContent ?? "";
}
