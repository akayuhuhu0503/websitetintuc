using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using LandPortal.Domain;

namespace LandPortal.MigrationCli;

public static class InventoryDiscovery
{
    public static InventoryUrl? Classify(string raw, string parent, int? category = null)
    {
        if (raw.Length>4096||!Uri.TryCreate(new Uri(parent), System.Net.WebUtility.HtmlDecode(raw), out var u) || u.Scheme is not ("https" or "http") || !u.IsDefaultPort || u.UserInfo.Length != 0) return null;
        var b = new UriBuilder(u) { Fragment = "" }; u = b.Uri;
        if (Regex.IsMatch(u.AbsolutePath, @"(^|/)(_layouts|_api|_vti[^/]*|admin|login|signin|signout|logout|api)(/|\.|$)", RegexOptions.IgnoreCase)) return null;
        if (Regex.IsMatch(u.Query,@"[?&](delete|remove|save|update|insert|logout|cmd|action)=",RegexOptions.IgnoreCase))return null;
        var file = Regex.IsMatch(u.AbsolutePath, @"\.(pdf|docx?|xlsx?|zip|mp4|webm|m3u8|jpg|jpeg|png|gif|webp|avif)$", RegexOptions.IgnoreCase);
        var n = new InventoryUrl { Url = u.AbsoluteUri };
        if (file) { n.Role = "File"; if (!SafeSourceClient.AllowedHosts.Contains(u.Host)) { n.Status = "OutOfScope"; n.Error = "Unapproved resource host"; } return n; }
        if (!u.Host.Equals("qldd.mae.gov.vn", StringComparison.OrdinalIgnoreCase)) { n.Status = "OutOfScope"; n.Role = "External"; return n; }
        if (Regex.IsMatch(u.AbsolutePath, @"(^|/)(_layouts|_api|_vti[^/]*|admin|login|signin|signout|logout|api)(/|\.|$)", RegexOptions.IgnoreCase)) return null;
        foreach (var part in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2); var key = Uri.UnescapeDataString(kv[0]); var value = kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : "";
            if (!(key.Equals("Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p) && p is >= 1 and <= 10000) &&
                !(key.Equals("ItemID", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(value, @"^\d{1,10}$")) &&
                !(key.Equals("ListId", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(value, out _))) return null;
        }
        var path = u.AbsolutePath.ToLowerInvariant();
        if (path == "/robots.txt") { n.Role = "Robots"; return n; }
        if (path.EndsWith(".xml") || path.Contains("/rss")) { n.Role = "Feed"; return n; }
        if (path == "/") { n.Role = "Home"; return n; }
        var cm = Regex.Match(path, @"/chuyen-muc/[^/]*?-(\d+)(?:-p(\d+))?\.htm$");
        if (cm.Success) { n.Role = "List"; n.CategoryIds.Add(int.Parse(cm.Groups[1].Value)); n.Page = cm.Groups[2].Success ? int.Parse(cm.Groups[2].Value) : 1; return n; }
        n.Kind = Regex.IsMatch(path,@"^/chi-tiet-van-ban-du-thao-\d+\.htm$") || path=="/pages/chi-tiet-van-ban-du-thao.aspx" ? ContentKind.DraftDocument : Regex.IsMatch(path,@"^/van-ban-\d+\.htm$") || path=="/pages/van-ban.aspx" ? ContentKind.LegalDocument : Regex.IsMatch(path,@"^/video(?:-\d+)?\.htm$") || path=="/pages/danh-sach-video.aspx" ? ContentKind.Video : category == 52 || path.StartsWith("/thong-bao/") ? ContentKind.Announcement : ContentKind.Article;
        if (path is "/danh-sach-van-ban.htm" or "/danh-sach-van-ban-du-thao.htm" or "/video.htm")
        { n.Role = "List"; if(path.StartsWith("/danh-sach-van-ban")) n.Kind = path.Contains("du-thao") ? ContentKind.DraftDocument : ContentKind.LegalDocument; var pm = Regex.Match(u.Query, @"[?&]Page=(\d+)",RegexOptions.IgnoreCase);if(pm.Success)n.Page=int.Parse(pm.Groups[1].Value); return n; }
        if (path.StartsWith("/lanh-dao-cuc-") || path.StartsWith("/chuc-nang-nhiem-vu-") || path.Contains("co-cau-to-chuc")) n.Kind = ContentKind.StaticPage;
        var id = Regex.Match(path, @"-(\d+)\.htm$");
        if (path is "/pages/chi-tiet-tin-tuc.aspx" or "/pages/van-ban.aspx" or "/pages/chi-tiet-van-ban-du-thao.aspx" or "/pages/danh-sach-video.aspx") id = Regex.Match(u.Query, @"[?&]ItemID=(\d+)",RegexOptions.IgnoreCase);
        if (id.Success) { n.Role = "Detail"; n.CandidateId = id.Groups[1].Value; if(category.HasValue)n.CategoryIds.Add(category.Value); return n; }
        if (path.EndsWith(".htm") || path.EndsWith(".html")) { n.Role = "Other"; return n; }
        return null;
    }
    public static IEnumerable<string> FeedLinks(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
        var doc = XDocument.Load(reader);
        return doc.Descendants().Where(x => x.Name.LocalName is "loc" or "link").Select(x => x.Attribute("href")?.Value ?? x.Value.Trim()).Where(x => Uri.TryCreate(x, UriKind.Absolute, out _)).Distinct().ToArray();
    }
    public static bool RobotsAllows(string robots, string url)
    {
        var groups = new List<(List<string> Agents,List<(string Path,bool Allow)> Rules)>();
        List<string> agents=[]; List<(string,bool)> rules=[];
        foreach(var raw in robots.Split('\n'))
        {
            var line=raw.Split('#')[0].Trim();var split=line.IndexOf(':');if(split<0)continue;
            var name=line[..split].Trim().ToLowerInvariant();var value=line[(split+1)..].Trim();
            if(name=="user-agent") { if(rules.Count>0){groups.Add((agents,rules));agents=[];rules=[];}agents.Add(value.ToLowerInvariant()); }
            else if(name is "disallow" or "allow" && value.Length>0)rules.Add((value,name=="allow"));
        }
        groups.Add((agents,rules));
        var specific=groups.Where(x=>x.Agents.Any(a=>a!="*"&&"landportalmigration".Contains(a,StringComparison.OrdinalIgnoreCase))).ToArray();
        var selected=specific.Length>0?specific:groups.Where(x=>x.Agents.Contains("*")).ToArray();
        var target=new Uri(url).PathAndQuery; var matches=selected.SelectMany(x=>x.Rules).Where(x=>Regex.IsMatch(target,"^"+Regex.Escape(x.Path).Replace("\\*",".*").Replace("\\$","$"),RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))).OrderByDescending(x=>x.Path.Length).ThenByDescending(x=>x.Allow).ToArray();
        return matches.Length==0||matches[0].Allow;
    }
}
