using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LandPortal.MigrationCli;

// Candidate tokenization follows HTML's srcset algorithm: a comma inside the
// URL token is not a separator, and only valid descriptors admit a candidate.
public static class SourceSrcset
{
    static bool Space(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    public static IEnumerable<string> Urls(string input, params string?[] sourceFallbacks)
    {
        var position = 0;
        while (position < input.Length)
        {
            while (position < input.Length && (Space(input[position]) || input[position] == ',')) position++;
            if (position == input.Length) yield break;
            var start = position;
            // QLDD's saved markup contains unescaped spaces in filenames. Repair
            // only an exact complete URL independently present in src/data-src;
            // never guess a filename by concatenating arbitrary descriptor text.
            var corroborated = sourceFallbacks.Where(x => !string.IsNullOrEmpty(x) && x.Any(Space))
                .Select(x => x!.Trim()).OrderByDescending(x => x.Length)
                .FirstOrDefault(x => input.AsSpan(start).StartsWith(x, StringComparison.Ordinal)
                    && start + x.Length < input.Length && Space(input[start + x.Length]));
            string url;
            if (corroborated is not null)
            {
                url = corroborated;
                position += url.Length;
            }
            else
            {
                while (position < input.Length && !Space(input[position])) position++;
                url = input[start..position];
                if (url.EndsWith(','))
                {
                    url = url.TrimEnd(',');
                    if (url.Length > 0) yield return url;
                    continue;
                }
            }
            var descriptors = new List<string>();
            var token = new StringBuilder();
            var inParens = false;
            while (position < input.Length)
            {
                var c = input[position++];
                if (c == ',' && !inParens) break;
                if (Space(c) && !inParens)
                {
                    if (token.Length > 0) { descriptors.Add(token.ToString()); token.Clear(); }
                }
                else
                {
                    token.Append(c);
                    if (c == '(') inParens = true;
                    else if (c == ')') inParens = false;
                }
            }
            if (token.Length > 0) descriptors.Add(token.ToString());
            if (url.Length > 0 && Valid(descriptors)) yield return url;
        }
    }

    static bool Valid(List<string> descriptors)
    {
        var width = false; var height = false; var density = false;
        foreach (var descriptor in descriptors)
        {
            if (Regex.IsMatch(descriptor, @"^[0-9]+[wh]$", RegexOptions.CultureInvariant))
            {
                if (!double.TryParse(descriptor[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var size) || !double.IsFinite(size) || size <= 0) return false;
                if (descriptor[^1] == 'w') { if (width || density) return false; width = true; }
                else { if (height || density) return false; height = true; }
            }
            else if (Regex.IsMatch(descriptor, @"^-?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?x$", RegexOptions.CultureInvariant)
                && double.TryParse(descriptor[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && double.IsFinite(value) && value >= 0)
            {
                if (width || height || density) return false;
                density = true;
            }
            else return false;
        }
        return !height || width;
    }
}
