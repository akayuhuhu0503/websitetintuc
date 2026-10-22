using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
namespace LandPortal.Tests;
public static class CaptchaHttp
{
    public static Dictionary<string,string> Fields(IDocument document)
    {
        var fields = new Dictionary<string,string> { ["__RequestVerificationToken"] = document.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")! };
        Add(document, fields); return fields;
    }
    public static void Add(IDocument document, Dictionary<string,string> fields)
    {
        if (document.QuerySelector("#captcha-question") == null) return;
        fields["CaptchaId"] = document.QuerySelector("input[name=CaptchaId]")!.GetAttribute("value")!;
        fields["CaptchaAnswer"] = Answer(document);
    }
    public static string Answer(IDocument document)
    {
        var text=document.QuerySelector("#captcha-question")?.TextContent??throw new InvalidOperationException("Actual CAPTCHA missing.");
        return AnswerText(text);
    }
    public static string AnswerText(string text)
    {
        var n=Regex.Matches(text,@"\d+").Select(x=>int.Parse(x.Value,CultureInfo.InvariantCulture)).ToArray();
        if(n.Length!=2||(!text.Contains("cộng")&&!text.Contains("trừ")))throw new InvalidOperationException("Unrecognized real arithmetic CAPTCHA.");
        return(text.Contains("cộng")?n[0]+n[1]:n[0]-n[1]).ToString(CultureInfo.InvariantCulture);
    }
}
