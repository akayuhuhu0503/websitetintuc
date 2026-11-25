using System.Security.Cryptography;
using System.Text;
using LandPortal.Infrastructure;

internal static class TrustedOutlineChecks
{
    internal static void Run()
    {
        var passed = 0;
        void Check(string name, byte[] bytes, bool accept, CancellationToken ct = default)
        {
            var before = SHA256.HashData(bytes);
            var accepted = true;
            try { using var stream = new MemoryStream(bytes, writable: false); TrustedInternalPdfPolicy.Validate(stream, ct); }
            catch (Exception e) when (e is InvalidDataException or OperationCanceledException) { accepted = false; }
            if (accepted != accept || !before.SequenceEqual(SHA256.HashData(bytes))) throw new Exception("FAIL " + name);
            Console.WriteLine($"PASS {++passed}: {name}");
        }
        Check("120 sibling bookmarks, original bytes preserved", Pdf(120), true);
        Check("ten nested bookmarks", Pdf(10, nested: true), true);
        Check("nested hierarchy still bounded", Pdf(70, nested: true), false);
        Check("sibling cycle rejected", Pdf(120, cycle: true), false);
        Check("wrong parent rejected", Pdf(120, wrongParent: true), false);
        Check("JavaScript at final sibling rejected", Pdf(120, extra: "/A << /S /JavaScript /JS (bad) >>"), false);
        Check("embedded file at final sibling rejected", Pdf(120, extra: "/EmbeddedFiles << >>"), false);
        Check("relative file URI at final sibling rejected", Pdf(120, extra: "/A << /S /URI /URI (../../private/file.docx) >>"), false);
        Check("HTTPS at final sibling accepted", Pdf(120, extra: "/A << /S /URI /URI (https://example.org/document) >>"), true);
        Check("non-outline nested structures still bounded", Pdf(1, extra: "/Other " + string.Concat(Enumerable.Repeat("<< /X ", 70)) + "0" + string.Concat(Enumerable.Repeat(" >>", 70))), false);
        Check("token budget retained", Pdf(1, extra: "/Other [" + string.Concat(Enumerable.Repeat("0 ", TrustedInternalPdfPolicy.MaximumTokens)) + "]"), false);
        Check("cancel retained", Pdf(120), false, new CancellationToken(canceled: true));
        Console.WriteLine($"Trusted outline checks: {passed}/12 PASS; no database or network.");
    }

    private static byte[] Pdf(int count, bool nested = false, bool cycle = false, bool wrongParent = false, string extra = "")
    {
        var objects = new List<string> {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>",
            $"<< /Type /Outlines /First 5 0 R /Last {(nested ? 5 : count + 4)} 0 R /Count {count} >>"
        };
        for (var i = 0; i < count; i++)
        {
            var parent = wrongParent && i == count - 1 ? 2 : nested && i > 0 ? i + 4 : 4;
            var links = i < count - 1 ? nested ? $"/First {i + 6} 0 R /Last {i + 6} 0 R" : $"/Next {i + 6} 0 R" : cycle ? "/Next 5 0 R" : "";
            objects.Add($"<< /Title (Bookmark {i}) /Parent {parent} 0 R /Dest [3 0 R /Fit] {links} {(i == count - 1 ? extra : "")} >>");
        }
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++) { offsets.Add(text.Length); text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = text.Length;
        text.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append($"{offset:D10} 00000 n \n");
        text.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
}
