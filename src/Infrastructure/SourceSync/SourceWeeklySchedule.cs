using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LandPortal.MigrationCli;

namespace LandPortal.Infrastructure;

public static class SourceWeeklySchedule
{
    public static string Normalize(string value)
    {
        var s=new StringBuilder();
        foreach(var c in value.Normalize(NormalizationForm.FormD))
            if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)s.Append(char.ToLowerInvariant(c)=='đ'?'d':char.ToLowerInvariant(c));
        return Regex.Replace(s.ToString(),@"\s+"," ").Trim();
    }
    public static (int Year,int Week)? Period(string title)
    {
        var t=Normalize(title);
        var weeks=Regex.Matches(t,@"\btuan\s+(?:thu\s+)?(?<w>\d{1,2})(?!\d)");
        if(weeks.Count!=1)return null;
        var week=int.Parse(weeks[0].Groups["w"].Value);
        var yearMatch=Regex.Match(t,@"\btuan\s+(?:thu\s+)?\d{1,2}\s*(?:nam|/)\s*(?<y>20\d{2})\b");
        int? year=yearMatch.Success?int.Parse(yearMatch.Groups["y"].Value):null;
        var range=Regex.Match(t,@"tu ngay\s+(?<a>\d{1,2}/\d{1,2}/\d{4})\s+den\s+(?:ngay\s+)?(?<b>\d{1,2}/\d{1,2}/\d{4})");
        if(range.Success)
        {
            if(!DateTime.TryParseExact(range.Groups["a"].Value,"d/M/yyyy",CultureInfo.InvariantCulture,DateTimeStyles.None,out var a)||
               !DateTime.TryParseExact(range.Groups["b"].Value,"d/M/yyyy",CultureInfo.InvariantCulture,DateTimeStyles.None,out var b)||b<a||(b-a).Days>6||
               ISOWeek.GetWeekOfYear(a)!=week||ISOWeek.GetWeekOfYear(b)!=week||ISOWeek.GetYear(a)!=ISOWeek.GetYear(b)||year.HasValue&&year!=ISOWeek.GetYear(a))return null;
            year=ISOWeek.GetYear(a);
        }
        else if(t.Contains("tu ngay"))return null;
        return year is >=2000 and <=2099&&week>=1&&week<=ISOWeek.GetWeeksInYear(year.Value)?(year.Value,week):null;
    }
    public static bool IsDepartment(string title)
    {
        var text=Normalize(title);
        return text.Contains("lanh dao cuc")&&!text.Contains("lanh dao trung tam");
    }
    // Legacy Excel titles can have a conflicting week/date range. Such a match
    // is a review candidate only, never evidence for binding or publication.
    public static bool CouldMatch(string title,(int Year,int Week) period)
    {
        if(!IsDepartment(title))return false;
        var t=Normalize(title);
        var years=Regex.Matches(t,@"\b20\d{2}\b").Select(x=>int.Parse(x.Value)).Distinct().ToArray();
        if(!years.Contains(period.Year))return false;
        if(Regex.Matches(t,@"\btuan\s+(?:thu\s+)?(\d{1,2})(?!\d)").Any(x=>int.Parse(x.Groups[1].Value)==period.Week))return true;
        return Regex.Matches(t,@"\d{1,2}/\d{1,2}/20\d{2}").Any(x=>DateTime.TryParseExact(x.Value,"d/M/yyyy",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)&&ISOWeek.GetYear(d)==period.Year&&ISOWeek.GetWeekOfYear(d)==period.Week);
    }
    public static string AssetHash(IEnumerable<SourceSyncAsset> assets)=>LandPortal.Application.TextNormalizer.Hash(
        string.Join("\n",assets.OrderBy(x=>x.Url,StringComparer.Ordinal).Select(x=>x.Url+"|"+x.Sha256)));
}
