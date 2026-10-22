using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using LandPortal.Infrastructure;
namespace LandPortal.Tests;
public static class V05WordTests
{
    private static readonly XNamespace W="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static byte[] Fixture(DateOnly monday,bool unsafeContent=false,bool horizontal=false,bool seven=false)
    {
        XElement Cell(string value,string? merge=null,int span=1)=>new(W+"tc",new XElement(W+"tcPr",merge==null?null:new XElement(W+"vMerge",new XAttribute(W+"val",merge)),span==1?null:new XElement(W+"gridSpan",new XAttribute(W+"val",span))),new XElement(W+"p",new XElement(W+"r",new XElement(W+"t",value))));
        var header=new XElement(W+"tr",new[]{"Ngày","Buổi","Giờ","Nội dung","Chủ trì","Thành phần","Địa điểm","Ghi chú"}.Select(x=>Cell(x)));
        var first=new XElement(W+"tr",Cell(monday.ToString("dd/MM/yyyy"),"restart"),Cell("Sáng","restart"),Cell("08:00"),Cell("Anonymous technical fixture"),Cell("QA"),Cell("Test"),Cell("Local"),Cell("Not official"));
        var second=new XElement(W+"tr",Cell("","continue"),Cell("","continue"),Cell("09:00"),Cell("Second technical fixture"),Cell("QA"),Cell("Test"),Cell("Local"),Cell(""));
        if(seven){header.Elements(W+"tc").Last().Remove();first.Elements(W+"tc").Last().Remove();second.Elements(W+"tc").Last().Remove();}
        if(horizontal){second.RemoveNodes();second.Add(Cell(monday.ToString("dd/MM/yyyy"),span:2),Cell("09:00"),Cell("Ambiguous fixture"),Cell("QA"),Cell("Test"),Cell("Local"),Cell(""));}
        var doc=new XDocument(new XElement(W+"document",new XAttribute(XNamespace.Xmlns+"w",W),new XElement(W+"body",new XElement(W+"tbl",header,first,second),unsafeContent?new XElement(W+"ins",new XElement(W+"r",new XElement(W+"t","tracked private"))):null)));
        using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)){
            void Add(string name,string text){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(text);}
            Add("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");Add("word/document.xml",doc.ToString());Add("docProps/core.xml","<properties>Technical QA fixture, not an office schedule</properties>");
        }return memory.ToArray();
    }
    public static async Task RunAsync(string root)
    {
        var evidence=TestEvidencePaths.At(root,"v05-word-technical");Directory.CreateDirectory(evidence);var records=new List<object>();var monday=new DateOnly(2026,10,12);
        for(var i=0;i<3;i++)
        {
            var day=monday.AddDays(7*i);var bytes=Fixture(day);var preview=WeeklyDocxReader.Parse(new MemoryStream(bytes),"technical-fixture.docx",System.Globalization.ISOWeek.GetYear(day.ToDateTime(TimeOnly.MinValue)),System.Globalization.ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue)));
            if(preview.Rows.Count!=2||preview.Rows[1].Date!=day||preview.Rows[1].Period!="Sáng"||preview.BusinessTemplateApproved)throw new InvalidOperationException("Vertical merged cells lost values or fake business approval.");
            var path=Path.Combine(evidence,$"technical-only-week-{i+1}.docx");await File.WriteAllBytesAsync(path,bytes);records.Add(new{file=Path.GetFileName(path),sha256=ScannerTests.Hash(bytes),rows=preview.Rows,preview.Warnings,classification="TECHNICAL FIXTURE, NOT REAL OFFICE SAMPLE"});
        }
        var ambiguous=WeeklyDocxReader.Parse(new MemoryStream(Fixture(monday,horizontal:true)),"technical.docx",2026,42);if(ambiguous.Rows.Count!=1||!ambiguous.Warnings.Any(x=>x.Contains("gộp ngang")))throw new InvalidOperationException("Ambiguous merged row silently published.");
        var seven=WeeklyDocxReader.Parse(new MemoryStream(Fixture(monday,seven:true)),"technical-seven.docx",2026,42);if(seven.Rows.Count!=2||seven.Rows.Any(x=>x.Note!=""))throw new InvalidOperationException("Seven-column schedule lost rows or invented notes.");
        try{WeeklyDocxReader.Parse(new MemoryStream(Fixture(monday,true)),"technical.docx",2026,42);throw new Exception("Tracked changes allowed.");}catch(InvalidOperationException){}
        try{WeeklyDocxReader.Parse(new MemoryStream("not zip"u8.ToArray()),"bad.docx",2026,42);throw new Exception("Invalid DOCX accepted.");}catch(InvalidOperationException){}
        await File.WriteAllTextAsync(Path.Combine(evidence,"report.json"),System.Text.Json.JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,technicalFixtureStatus="PASS",realSampleStatus="BLOCKED:NO_REAL_WORD_SAMPLES",businessTemplateApproved=false,records},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
}
