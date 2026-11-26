using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LandPortal.Infrastructure;

internal static partial class TrustedArchiveOfficePolicy
{
    // Narrow internal-ZIP-only extensions. No Office execution, external names,
    // dynamic references, hyperlinks, DDE, add-ins or VBA functions.
    private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
    { "SUM", "COUNT", "COUNTA", "COUNTIF", "COUNTIFS", "MIN", "MAX", "AVERAGE", "IF", "IFERROR", "AND", "OR", "NOT", "ROUND", "ROUNDUP", "ROUNDDOWN", "ABS", "INT", "MOD", "CEILING", "FLOOR", "SUMIF", "SUMIFS", "TEXT" };
    public static bool Allowed(XElement x, string mime)
    {
        if (mime == OfficeDocumentPolicy.Docx && x.Name.LocalName is "instrText" or "fldSimple")
            return PageField().IsMatch(x.Name.LocalName == "fldSimple" ? x.Attributes().FirstOrDefault(a=>a.Name.LocalName=="instr")?.Value ?? "" : x.Value);
        if (mime != OfficeDocumentPolicy.Xlsx) return false;
        if (x.Name.LocalName == "definedName" && x.Attribute("name")?.Value is not ("_xlnm.Print_Area" or "_xlnm.Print_Titles" or "_xlnm._FilterDatabase")) return false;
        if (x.Name.LocalName is not ("f" or "definedName")) return false;
        if (x.Value.Length == 0) return x.Name.LocalName=="f" && x.Attribute("t")?.Value=="shared" && uint.TryParse(x.Attribute("si")?.Value,out _);
        return InertFormula(x.Value, x.Name.LocalName == "definedName");
    }
    private static bool InertFormula(string formula, bool rangeOnly)
    {
        if (formula.Length > 8192) return false;
        var i=0; var depth=0;
        while(i<formula.Length)
        {
            if(char.IsWhiteSpace(formula[i])){i++;continue;}
            var match=FormulaToken().Match(formula,i);
            if(!match.Success || match.Index!=i)return false;
            var token=match.Value; i+=token.Length;
            if(token=="("){if(rangeOnly || ++depth>32)return false;}
            else if(token==")"){if(--depth<0)return false;}
            else if(match.Groups["function"].Success)
            { if(rangeOnly || !Functions.Contains(match.Groups["function"].Value))return false; depth++; if(depth>32)return false; }
            else if(match.Groups["identifier"].Success)
            {
                // Undefined local names can yield #NAME? but cannot introduce an
                // executable definition: custom definedName records are denied.
                // Never accept a disguised/UDF function call through this branch.
                if(rangeOnly || formula.AsSpan(i).TrimStart().StartsWith("("))return false;
            }
            else if(rangeOnly && match.Groups["operator"].Success)return false;
        }
        return depth==0;
    }
    [GeneratedRegex(@"^\s*(PAGE|NUMPAGES|SECTIONPAGES)\s*(\\\*\s+MERGEFORMAT\s*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex PageField();
    // Sheet-qualified references are local only: no brackets, DDE pipe or URL.
    // Arbitrary identifiers/named functions cannot pass this token allowlist.
    [GeneratedRegex("(?<function>[A-Za-z]+)\\s*\\(|(?:'(?:[^'\\[\\]\\r\\n|:!]|'')+'|[A-Za-z_][A-Za-z0-9_.]*)!(?:\\$?[A-Za-z]{1,3}\\$?[0-9]{1,7}|\\$?[A-Za-z]{1,3}(?=:)|\\$?[0-9]{1,7}|#REF!)|\\$?[A-Za-z]{1,3}\\$?[0-9]{1,7}|\\$[A-Za-z]{1,3}|\\$[0-9]{1,7}|[0-9]+(?:\\.[0-9]+)?(?:[Ee][+-]?[0-9]+)?|\"(?:[^\"]|\"\")*\"|#(?:REF!|DIV/0!|N/A|VALUE!|NAME\\?|NUM!|NULL!)|(?<identifier>[\\p{L}_][\\p{L}\\p{N}_.]*)|(?<operator>[+*/^&%=<>-])|[(),:;]", RegexOptions.CultureInvariant, 100)]
    private static partial Regex FormulaToken();
}
