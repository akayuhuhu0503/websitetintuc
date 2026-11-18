using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LandPortal.WeeklyImport;
using UglyToad.PdfPig.Writer;

internal static class PlanningPdfOverrideChecks
{
    public static void Run(string sourceZip, string preparedManifest)
    {
        var root=Path.Combine(Directory.GetCurrentDirectory(),".local","tests","planning-overrides-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
        void Denied(Action action,string name){try{action();}catch(InvalidDataException){Check(true,name);return;}throw new Exception("NOT DENIED "+name);}
        // Hash the real prepared copies only. Do not re-run the previous 27 PDF/parser checks.
        var actual=PlanningPdfOverrides.Read(preparedManifest);
        Check(actual.Set.Files.Count==23,"23 prepared derivatives retain exact byte/hash bindings");
        Check(actual.Set.Files.Where(x=>x.SourceHasSignatureObjects).Select(x=>x.Stt).Order().SequenceEqual(new[]{15,17,18,19,20,23,30}),"Seven signed-source derivatives disclose loss of cryptographic signature");
        var before=PlanningPackage.FileHash(sourceZip);
        byte[] workbook;
        using(var source=ZipFile.OpenRead(sourceZip))workbook=WeeklyPackage.ReadEntry(source.GetEntry(WeeklyPackage.WorkbookName)!);
        Check(actual.Set.ZipSha256==before && actual.Set.WorkbookSha256==WeeklyPackage.Hash(workbook),"Prepared manifest bound to unchanged source ZIP/workbook");
        // Small PDFs and a verbatim workbook copy isolate the new mapping logic from source PDF remediation.
        using var writer=new PdfDocumentBuilder();writer.AddPage(100,100);var pdf=writer.Build();
        byte[] Fixture(string label)=>pdf.Concat(Encoding.ASCII.GetBytes("\n% "+label+"\n")).ToArray();
        var folder="2.4_Lich tuan/QH, KH SDĐ/";
        var zipPath=Path.Combine(root,"fixture.zip");var originals=new Dictionary<string,byte[]>();
        using(var zip=ZipFile.Open(zipPath,ZipArchiveMode.Create))
        {
            void Add(string name,byte[] bytes){using var stream=zip.CreateEntry(name).Open();stream.Write(bytes);}
            Add(WeeklyPackage.WorkbookName,workbook);
            for(var stt=1;stt<=32;stt++)
            {
                var entry=folder+stt+"_fixture.pdf";var bytes=Fixture("source-"+stt);originals.Add(entry,bytes);Add(entry,bytes);
                if(stt==24){entry=folder+stt+"_appendix.pdf";bytes=Fixture("source-24-appendix");originals.Add(entry,bytes);Add(entry,bytes);}
            }
        }
        var prepared=Fixture("derived-1");File.WriteAllBytes(Path.Combine(root,"prepared.pdf"),prepared);
        var sourceEntry=folder+"1_fixture.pdf";
        var replacement=new PlanningPdfReplacement(1,sourceEntry,WeeklyPackage.Hash(originals[sourceEntry]),1,"prepared.pdf",WeeklyPackage.Hash(prepared),prepared.Length,false,"Bản sao chuẩn hóa, không phải bản gốc.");
        var set=new PlanningPdfReplacementSet(PlanningPackage.FileHash(zipPath),WeeklyPackage.Hash(workbook),new[]{replacement});
        var manifest=Path.Combine(root,"overrides.json");
        PlanningPdfOverrides Read(PlanningPdfReplacementSet value){File.WriteAllBytes(manifest,JsonSerializer.SerializeToUtf8Bytes(value));return PlanningPdfOverrides.Read(manifest);}
        var baseline=PlanningPackage.Read(zipPath);
        var mapped=PlanningPackage.Read(zipPath,pdfOverrides:Read(set));
        Check(mapped.Rows.Count==32 && mapped.Rows.All(x=>x.CanImport) && mapped.Rows.Sum(x=>x.Files.Count)==33,"32 fixture dossiers / 33 separate source documents, no split or lost appendix");
        Check(mapped.Dates.Workbook.SequenceEqual(workbook) && mapped.Rows.Zip(baseline.Rows).All(x=>x.First.Source.Title==x.Second.Source.Title && x.First.Source.DateValue==x.Second.Source.DateValue && x.First.Source.DateType==x.Second.Source.DateType),"Workbook, all source titles and raw date values/types unchanged");
        var selected=mapped.Rows.Single(x=>x.Stt==1).Files.Single();
        Check(selected.OriginalSha256==replacement.SourceSha256 && selected.Sha256==replacement.Sha256 && mapped.ReadFile(selected).SequenceEqual(prepared),"ReadFile selects the hash-bound derivative and retains source provenance");
        Check(mapped.ScopeSha256!=baseline.ScopeSha256,"Derivative manifest participates in plan/journal scope hash");
        Check(mapped.ReadFile(mapped.Rows.Single(x=>x.Stt==2).Files.Single()).SequenceEqual(originals[folder+"2_fixture.pdf"]),"No override preserves original PDF bytes");
        Denied(()=>Read(set with{Files=new[]{replacement,replacement}}),"Duplicate mapping rejected");
        Denied(()=>Read(set with{Files=new[]{replacement with{RelativePath="../prepared.pdf"}}}),"Path traversal rejected");
        Denied(()=>Read(set with{Files=new[]{replacement with{Sha256=new string('0',64)}}}),"Incorrect derivative SHA rejected");
        Denied(()=>Read(set with{Files=new[]{replacement with{SourceHasSignatureObjects=true}}}),"Missing signature-loss disclosure rejected");
        Denied(()=>PlanningPackage.Read(zipPath,pdfOverrides:Read(set with{ZipSha256=new string('0',64)})),"Wrong source ZIP rejected");
        Denied(()=>PlanningPackage.Read(zipPath,pdfOverrides:Read(set with{WorkbookSha256=new string('0',64)})),"Wrong source workbook rejected");
        Denied(()=>PlanningPackage.Read(zipPath,pdfOverrides:Read(set with{Files=new[]{replacement with{Stt=2}}})),"Wrong dossier mapping rejected");
        Denied(()=>PlanningPackage.Read(zipPath,pdfOverrides:Read(set with{Files=new[]{replacement with{SourceSha256=new string('0',64)}}})),"Wrong source PDF SHA rejected");
        Denied(()=>PlanningPackage.Read(zipPath,pdfOverrides:Read(set with{Files=new[]{replacement with{Pages=2}}})),"Changed page count rejected");
        Denied(()=>PlanningPackage.Read(zipPath,pdfOverrides:Read(set with{Files=new[]{replacement with{SourceEntry=folder+"1_absent.pdf"}}})),"Unused mapping rejected");
        Read(set);File.AppendAllText(Path.Combine(root,"prepared.pdf"),"% changed");
        Denied(()=>mapped.ReadFile(selected),"Derivative mutation after inventory rejected before upload");
        Check(PlanningPackage.FileHash(sourceZip)==before,"Real source ZIP untouched; zero CMS/SQL access");
        File.WriteAllBytes(Path.Combine(root,"result.json"),JsonSerializer.SerializeToUtf8Bytes(new{passed=checks.Count,checks},WeeklyPackage.Json));
        Console.WriteLine($"RESULT {checks.Count}/{checks.Count} PASS; {root}");
    }
}
