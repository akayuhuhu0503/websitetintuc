using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace LandPortal.Infrastructure;

public static class SourceWeeklyEvidence
{
    public static (int Year,int Week)? HeaderPeriod(string text)
    {
        var header=SourceWeeklySchedule.Normalize(text);
        if(!header.Contains("cuc quan ly dat dai")||!header.Contains("lanh dao cuc")||header.Contains("lanh dao trung tam"))return null;
        var match=Regex.Match(header,@"\btuan\s*(?:thu\s*)?(?<w>\d{1,2})\b.{0,160}?\bnam\s*(?<y>20\d{2})\b");
        if(!match.Success)return null;
        return SourceWeeklySchedule.Period($"Tuần {match.Groups["w"].Value} năm {match.Groups["y"].Value}");
    }
    public static async Task<(int Year,int Week)?> ResolveAsync(SourceSyncCheckpoint data,StorageOptions storage,CancellationToken ct)
    {
        if(data.Source is not {PrimaryCategoryId:76} source||!SourceWeeklySchedule.IsDepartment(source.Title))return null;
        if(SourceWeeklySchedule.Period(source.Title) is {} title)return title;
        // Only fill a missing year. Never override a contradictory explicit year/range.
        var normalized=SourceWeeklySchedule.Normalize(source.Title);
        if(Regex.IsMatch(normalized,@"\b20\d{2}\b")||normalized.Contains("tu ngay"))return null;
        var week=Regex.Match(normalized,@"\btuan\s+(?:thu\s+)?(?<w>\d{1,2})(?!\d)");
        if(!week.Success)return null;
        var found=new HashSet<(int Year,int Week)>();
        foreach(var file in data.Assets.Where(x=>x.Mime=="application/pdf"))
        {
            ct.ThrowIfCancellationRequested();
            if(!file.Key.StartsWith("source-sync/resource/",StringComparison.Ordinal))throw new InvalidDataException("Sai kho bằng chứng lịch.");
            var path=storage.ResolveSnapshot(file.Key);
            MediaService.ValidatePrivateStorageRoot(path,storage);
            await using var input=File.OpenRead(path);
            if(input.Length!=file.Bytes||Convert.ToHexStringLower(await SHA256.HashDataAsync(input,ct))!=file.Sha256)throw new InvalidDataException("Bằng chứng PDF lịch không khớp hash.");
            input.Position=0;
            using var pdf=PdfDocument.Open(input,new ParsingOptions{UseLenientParsing=false,MaxStackDepth=64});
            var text=string.Join(" ",pdf.GetPage(1).GetWords().Select(x=>x.Text));
            var period=HeaderPeriod(text[..Math.Min(1800,text.Length)]);
            if(period.HasValue)found.Add(period.Value);
        }
        return found.Count==1&&found.Single().Week==int.Parse(week.Groups["w"].Value)?found.Single():null;
    }
}
