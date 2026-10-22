using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
namespace LandPortal.MigrationCli;

public static class BatchAliasPolicy
{
    public static void BlockConflicts(IReadOnlyList<SourceBatchRow> rows)
    {
        // Database ownership checks alone cannot see two absent identities competing in the same plan.
        var shared=rows.Where(r=>r.Action!="Blocked").SelectMany(r=>r.Aliases.Select(url=>(Row:r,Url:url)))
            .GroupBy(x=>x.Url,StringComparer.Ordinal).Where(g=>g.Select(x=>x.Row.Kind+":"+x.Row.SourceId).Distinct().Count()>1).ToArray();
        foreach(var group in shared)foreach(var row in group.Select(x=>x.Row).Distinct())
        {
            var code="INTRA_BATCH_ALIAS_COLLISION:"+group.Key;
            if(!row.Validation.Contains(code))row.Validation.Add(code);
            row.Findings.Add(new("NEEDS_REVIEW","INTRA_BATCH_ALIAS_COLLISION",group.Key,"ContentIdentity","Canonical thuộc nhiều danh tính trong cùng plan; đối chiếu loại nguồn và mapping, không tự chọn hoặc ghi đè bài công khai."));
            row.Action="Blocked";
        }
    }
}

public static class BatchReadiness
{
    public static string Role(ParsedSource source,string? url)
    {
        if(url is null)return "Content";
        var doc=SourceParser.Document(source.BodyHtml);
        bool Matches(string? value)=>Uri.TryCreate(new Uri(source.SourceUrl),value,out var parsed)&&parsed.AbsoluteUri==url;
        if(doc.QuerySelectorAll("a[href]").Any(x=>Matches(x.GetAttribute("href"))))return "AttachmentOrLinkedImage";
        if(doc.QuerySelectorAll("img").Any(x=>Matches(x.GetAttribute("src"))||Matches(x.GetAttribute("data-src"))))
            return new HtmlPolicy().PlainText(source.BodyHtml).Trim().Length<200?"EssentialImageOrReview":"InlineImage";
        return "SourceResourceNeedsReview";
    }
    public static bool CanReviewOptional(ParsedSource s,OptionalImageReview review)=>
        s.Kind==ContentKind.Article&&s.AssetUrls.Contains(review.Url)&&Role(s,review.Url)=="InlineImage"&&
        Uri.TryCreate(review.Url,UriKind.Absolute,out var uri)&&System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath,@"\.(jpe?g|png|gif|webp)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)&&
        !string.IsNullOrWhiteSpace(review.Reason)&&review.Reason.Length<=1000&&!string.IsNullOrWhiteSpace(review.Evidence)&&review.Evidence.Length<=2000;
    public static SourceBatchFinding Finding(ParsedSource s,string value,bool warning=false)
    {
        var split=value.IndexOf(':');var code=split<0?value:value[..split];
        var url=split>=0&&Uri.TryCreate(value[(split+1)..],UriKind.Absolute,out _)?value[(split+1)..]:null;
        var unknown=code is "INVALID_OPTIONAL_IMAGE_REVIEW" or "InvalidDataException" or "IOException" or "InvalidOperationException";
        var resolution=code switch{
            "SCAN_NOT_CLEAN"=>"Quét đúng bytes bằng engine được phép; không nâng trạng thái tài nguyên gốc bằng proof bản sao.",
            "UNSUPPORTED_FORMAT"=>"Đối chiếu bytes/header nguồn; không coi đuôi URL là định dạng hợp lệ.",
            "VIDEO_METADATA_ONLY_NO_FILE"=>"Cần tệp video thật và pipeline quét/xử lý; metadata không đủ.",
            "OPTIONAL_IMAGE_OMITTED"=>"Ảnh phụ thiếu đã được đối chiếu; giữ cảnh báo nội bộ, không tạo ảnh hoặc hotlink.",
            "OPTIONAL_METADATA_ABSENT"=>"Giữ trống trường tùy chọn như nguồn; không suy diễn giá trị.",
            _=>"Đối chiếu staging/raw và giải quyết trước khi nhập."
        };
        return new(warning?"NON-BLOCKING":unknown?"NEEDS_REVIEW":"BLOCKING",code,url,Role(s,url),resolution);
    }
}
