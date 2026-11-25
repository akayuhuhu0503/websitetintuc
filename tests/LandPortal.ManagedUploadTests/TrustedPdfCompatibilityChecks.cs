using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LandPortal.Infrastructure;

internal static class TrustedPdfCompatibilityChecks
{
    internal static async Task Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"LandPortal-PdfCompatibility-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var passed=0;
        async Task Check(string name,byte[] data,bool expected,bool trusted=true,bool expectWarnings=false,CancellationToken ct=default)
        {
            var path=Path.Combine(root,Guid.NewGuid().ToString("N")+".pdf");await File.WriteAllBytesAsync(path,data);var hash=SHA256.HashData(data);var warnings=new List<string>();
            var accepted=true;
            try {await EditorialDocumentPolicy.ValidateWithWarningsAsync(path,"application/pdf",null!,true,ct,trusted,warnings);}
            catch(Exception e)when(e is InvalidDataException or MediaUploadException or OperationCanceledException){accepted=false;}
            var after=SHA256.HashData(await File.ReadAllBytesAsync(path));
            if(accepted!=expected || !hash.SequenceEqual(after) || expected&&(warnings.Count>0)!=expectWarnings)throw new Exception("FAIL "+name);
            File.Delete(path);Console.WriteLine($"PASS {++passed}: {name}");
        }
        await Check("undefined graphics state and XObject, readable content, unchanged bytes and warnings",Pdf("/Gs1 gs /Missing Do 0 0 20 20 re f"),true,expectWarnings:true);
        await Check("public/ordinary PDF path remains strict",Pdf("/Gs1 gs /Missing Do 0 0 20 20 re f"),false,trusted:false);
        await Check("undefined resource with no remaining readable content denied",Pdf("/Missing Do"),false);
        await Check("declared but invalid XObject denied",Pdf("/Bad Do 0 0 20 20 re f",resources:"/XObject << /Bad 5 0 R >>",extraObject:"42"),false);
        await Check("missing font not skipped",Pdf("BT /Absent 12 Tf (visible) Tj ET /Missing Do 0 0 20 20 re f"),false);
        await Check("normal vector PDF remains accepted without warning",Pdf("0 0 20 20 re f"),true);
        await Check("list numbering is inert structure data",Pdf("0 0 20 20 re f",catalog:"/StructTreeRoot << /Type /StructTreeRoot /K [<< /Type /StructElem /S /L /A << /O /List /ListNumbering /Decimal >> >>] >>"),true);
        await Check("executable key hidden in list attribute denied",Pdf("0 0 20 20 re f",catalog:"/StructTreeRoot << /Type /StructTreeRoot /K [<< /Type /StructElem /S /L /A << /O /List /ListNumbering /Decimal /JS (bad) >> >>] >>"),false);
        await Check("unknown list attribute not broadly allowlisted",Pdf("0 0 20 20 re f",catalog:"/StructTreeRoot << /Type /StructTreeRoot /K [<< /Type /StructElem /S /L /A << /O /List /ListNumbering /Unexpected >> >>] >>"),false);
        await Check("JavaScript still rejected with missing graphics resources",Pdf("/Gs1 gs /Missing Do 0 0 20 20 re f",catalog:"/OpenAction << /S /JavaScript /JS (bad) >>"),false);
        await Check("embedded file still rejected",Pdf("/Missing Do 0 0 20 20 re f",catalog:"/Names << /EmbeddedFiles << >> >>"),false);
        await Check("relative local URI still rejected",Pdf("0 0 20 20 re f",page:"/Annots [<< /Type /Annot /Subtype /Link /Rect [0 0 20 20] /A << /S /URI /URI (../../private/file.docx) >> >>]"),false);
        await Check("launch action still rejected",Pdf("0 0 20 20 re f",page:"/Annots [<< /Type /Annot /Subtype /Link /Rect [0 0 20 20] /A << /S /Launch /F (app.exe) >> >>]"),false);
        await Check("truncated PDF denied",Pdf("0 0 20 20 re f")[..^10],false);
        await Check("cancellation retained",Pdf("0 0 20 20 re f"),false,ct:new CancellationToken(true));
        // Backlinks through a signature and /P lead to the same catalog/tree.
        // The malicious leaf must still be visited after breadth-first dedup.
        await Check("indirect catalog backlinks do not hide dangerous action",Pdf("0 0 20 20 re f",catalog:"/Other 5 0 R",extraObject:"<< /Parent 1 0 R /Child << /A << /S /JavaScript /JS (bad) >> >> >>"),false);
        await Check("benign indirect catalog backlink accepted",Pdf("0 0 20 20 re f",catalog:"/Other 5 0 R",extraObject:"<< /Parent 1 0 R /Child (inert) >>"),true);
        await Check("invalid signature ByteRange remains rejected",Pdf("0 0 20 20 re f",catalog:"/AcroForm << /Fields [<< /FT /Sig /V << /ByteRange [0 1 9999999 10] /Contents <00> >> >>] >>"),false);
        Directory.Delete(root);
        Console.WriteLine($"TRUSTED PDF COMPATIBILITY {passed}/18 PASS; original bytes retained, no SQL or HTTP.");
    }

    internal static byte[] Pdf(string content,string resources="",string catalog="",string page="",string extraObject="")
    {
        var objects=new List<string>{
            $"<< /Type /Catalog /Pages 2 0 R {catalog} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << {resources} >> /Contents 4 0 R {page} >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"};
        if(extraObject!="")objects.Add(extraObject);
        var b=new StringBuilder("%PDF-1.7\n");var offsets=new List<int>();
        for(var i=0;i<objects.Count;i++){offsets.Add(b.Length);b.Append($"{i+1} 0 obj\n{objects[i]}\nendobj\n");}
        var xref=b.Length;b.Append($"xref\n0 {objects.Count+1}\n0000000000 65535 f \n");foreach(var offset in offsets)b.Append($"{offset:D10} 00000 n \n");
        b.Append($"trailer\n<< /Size {objects.Count+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");return Encoding.ASCII.GetBytes(b.ToString());
    }
}
