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
    // Require an explicit adjacent week/year pair. Publication dates and PDF names are NOT week metadata.
    private static readonly Regex Pair = new(
        @"\btuan\s+(?:thu\s+)?(?<week>[0-9]{1,2})(?![0-9])\s*(?:nam\s+|[/\-]\s*)(?<year>[0-9]{4})(?![0-9])",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static WeeklyScheduleKey? Period(string? title, int? year = null, int? week = null)
    {
        // Explicit published metadata takes precedence; incomplete/invalid metadata must not be guessed.
        if (year.HasValue || week.HasValue)
            return year is int y && week is int w && Valid(y, w) ? new(y, w) : null;
        if (string.IsNullOrWhiteSpace(title) || title.Length > 4096) return null;
        var text = new StringBuilder(title.Length);
        foreach (var c in title.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                text.Append(char.ToLowerInvariant(c) == 'đ' ? 'd' : char.ToLowerInvariant(c));
        var matches = Pair.Matches(text.ToString());
        if (matches.Count != 1) return null; // Ambiguous titles remain in the public archive, not a guessed week.
        var match = matches[0];
        if (!int.TryParse(match.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var isoYear)
            || !int.TryParse(match.Groups["week"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var isoWeek)) return null;
        return Valid(isoYear, isoWeek) ? new(isoYear, isoWeek) : null;
    }

    private static bool Valid(int year, int week) => year is >= 2000 and <= 2200
        && week >= 1 && week <= ISOWeek.GetWeeksInYear(year);
}
