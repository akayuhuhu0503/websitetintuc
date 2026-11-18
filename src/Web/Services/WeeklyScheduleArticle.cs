using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LandPortal.Web.Services;

public sealed record WeeklyScheduleKey(int Year, int Week);

// Read-only presentation of the current public revision, never a new release or approval.
public sealed record WeeklyScheduleArticle(Guid Id, string Title, DateOnly? SourceDate,
    DateTimeOffset? PublishedUtc, int? ScheduleYear, int? ScheduleWeek, bool HasStructuredRelease)
{
    public WeeklyScheduleKey? Period => WeeklyScheduleTitle.Period(Title, ScheduleYear, ScheduleWeek);
    public string Url => HasStructuredRelease && Period is { } period
        ? $"/lich-tuan?year={period.Year}&week={period.Week}"
        : "/bai-viet/" + Id;
    public DateOnly? DisplayDate => SourceDate ?? (PublishedUtc is { } date
        ? DateOnly.FromDateTime(date.ToOffset(TimeSpan.FromHours(7)).DateTime) : (DateOnly?)null);
}

public static class WeeklyScheduleTitle
{
    // Only the title's explicit week and range are used here, never the publication date or PDF.
    private static readonly Regex Pair = new(
        @"\btuan\s+(?:thu\s+)?(?<week>[0-9]{1,2})(?![0-9])\s*(?:nam\s+|[/\-]\s*)(?<year>[0-9]{4})(?![0-9])",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Week = new(@"\btuan\s+(?:thu\s+)?(?<week>[0-9]{1,2})(?![0-9])",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Range = new(
        @"\btu\s+ngay\s+(?<start>[0-9]{1,2}/[0-9]{1,2}/[0-9]{4})(?![0-9])\s+den\s+(?:ngay\s+)?(?<end>[0-9]{1,2}/[0-9]{1,2}/[0-9]{4})(?![0-9])",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static WeeklyScheduleKey? Period(string? title, int? year = null, int? week = null)
    {
        // Explicit published metadata takes precedence; incomplete/invalid metadata must not be guessed.
        if (year.HasValue || week.HasValue)
            return year is int y && week is int w && Valid(y, w) ? new(y, w) : null;
        return Inspect(title).Period;
    }

    // Read-only review result. Conflicting titles remain in the archive without a guessed label/filter key.
    public static (WeeklyScheduleKey? Period, string? ReviewReason) Inspect(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 4096) return (null, null);
        var text = new StringBuilder(title.Length);
        foreach (var c in title.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                text.Append(char.ToLowerInvariant(c) == 'đ' ? 'd' : char.ToLowerInvariant(c));
        var normalized = text.ToString();
        var weeks = Week.Matches(normalized); var pairs = Pair.Matches(normalized); var ranges = Range.Matches(normalized);
        if (weeks.Count != 1 || pairs.Count > 1 || ranges.Count > 1) return (null, "Tiêu đề không xác định duy nhất một tuần.");
        var isoWeek = int.Parse(weeks[0].Groups["week"].Value, CultureInfo.InvariantCulture);
        int? isoYear = pairs.Count == 1 ? int.Parse(pairs[0].Groups["year"].Value, CultureInfo.InvariantCulture) : null;
        if (ranges.Count == 1)
        {
            var range = ranges[0];
            if (!DateOnly.TryParseExact(range.Groups["start"].Value, "d/M/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !DateOnly.TryParseExact(range.Groups["end"].Value, "d/M/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
                || end < start || end.DayNumber - start.DayNumber > 6)
                return (null, "Khoảng ngày trong tiêu đề không hợp lệ cho một tuần.");
            var first = start.ToDateTime(TimeOnly.MinValue); var last = end.ToDateTime(TimeOnly.MinValue);
            var rangeYear = ISOWeek.GetYear(first); var rangeWeek = ISOWeek.GetWeekOfYear(first);
            if (ISOWeek.GetYear(last) != rangeYear || ISOWeek.GetWeekOfYear(last) != rangeWeek
                || rangeWeek != isoWeek || isoYear.HasValue && isoYear != rangeYear)
                return (null, $"Tuần {isoWeek}{(isoYear.HasValue ? "/" + isoYear : "")} không khớp khoảng ngày (bắt đầu ở tuần {rangeWeek}/{rangeYear}).");
            isoYear = rangeYear;
        }
        else if (normalized.Contains("tu ngay", StringComparison.Ordinal))
            return (null, "Khoảng ngày trong tiêu đề cần rà soát.");
        return isoYear is int y && Valid(y, isoWeek) ? (new(y, isoWeek), null) : (null, "Chưa xác định năm/tuần hợp lệ.");
    }

    private static bool Valid(int year, int week) => year is >= 2000 and <= 2200
        && week >= 1 && week <= ISOWeek.GetWeeksInYear(year);
}
