using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using LandPortal.Domain;
namespace LandPortal.Infrastructure;
public sealed record WeeklyDocxPreview(IReadOnlyList<WeeklyScheduleRow> Rows,IReadOnlyList<string> Warnings,string Template)
{
    public bool BusinessTemplateApproved=>false;
}
// Technical template only, not an assertion about the office's as-yet unavailable files.
// Does not publish, save a file, fetch relationships, execute Word or change live SQL.
public static class WeeklyDocxReader
{
    public const string Template="Technical-v05-finish-2";
    private static readonly XNamespace W="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly string[] Headers=["Ngày","Buổi","Giờ","Nội dung","Chủ trì","Thành phần","Địa điểm","Ghi chú"];
    private static XDocument Xml(ZipArchiveEntry entry)
    {
        using var input=entry.Open();using var reader=XmlReader.Create(input,new(){DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2*1024*1024});return XDocument.Load(reader);
    }
    public static WeeklyDocxPreview Parse(Stream input,string filename,int year,int week)
    {
        if(Path.GetFileName(filename)!=filename||!filename.EndsWith(".docx",StringComparison.OrdinalIgnoreCase)||!input.CanSeek||input.Length>10*1024*1024||input.Length<22)throw new InvalidOperationException("Chỉ nhận DOCX tối đa10 MiB để đọc kỹ thuật.");
        try
        {
            using var zip=new ZipArchive(input,ZipArchiveMode.Read,true);
            if(zip.Entries.Count>100||zip.Entries.Sum(x=>x.Length)>20*1024*1024)throw new InvalidOperationException("Word vượt giới hạn ZIP.");
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in zip.Entries)
            {
                if(!paths.Add(entry.FullName)||entry.FullName.StartsWith('/')||entry.FullName.Contains('\\')||entry.FullName.Split('/').Any(p=>p is "." or "..")||entry.Length>2*1024*1024||entry.Length>Math.Max(1024,entry.CompressedLength*100))throw new InvalidOperationException("Cấu trúc/nén ZIP không an toàn.");
                if(entry.FullName.Contains("vba",StringComparison.OrdinalIgnoreCase)||entry.FullName.StartsWith("word/embeddings/",StringComparison.OrdinalIgnoreCase)||entry.FullName.Contains("activeX",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Word có nội dung chủ động/nhúng; không tiếp nhận.");
                if(entry.FullName.EndsWith(".rels",StringComparison.OrdinalIgnoreCase)&&Xml(entry).Descendants().Any(x=>x.Attribute("TargetMode")?.Value.Equals("External",StringComparison.OrdinalIgnoreCase)==true))throw new InvalidOperationException("Word có quan hệ bên ngoài; cần làm sạch trước khi đọc.");
            }
            var types=zip.GetEntry("[Content_Types].xml")??throw new InvalidOperationException("Thiếu DOCX content types.");
            if(!Xml(types).Descendants().Any(x=>x.Attribute("ContentType")?.Value=="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"))throw new InvalidOperationException("Không phải tài liệu Word DOCX.");
            var document=Xml(zip.GetEntry("word/document.xml")??throw new InvalidOperationException("Thiếu word/document.xml."));
            if(document.Descendants().Any(x=>x.Name==W+"ins"||x.Name==W+"del"||x.Name==W+"vanish"||x.Name==W+"webHidden"||x.Name==W+"altChunk"||x.Name==W+"object"))throw new InvalidOperationException("Word có thay đổi được theo dõi, chữ ẩn hoặc phần nhúng; cần rà soát/làm sạch hoặc nhập tay.");
            var warnings=new List<string>{"Mẫu kỹ thuật chưa được Văn phòng Cục xác nhận. Không thay thế AC-16.1 với3tuần nghiệp vụ thật.","Tệp gốc/metadata/comments luôn riêng tư; preview không cấp quyền phát hành tệp."};
            if(zip.Entries.Any(x=>x.FullName.StartsWith("docProps/",StringComparison.Ordinal)||x.FullName.Contains("comments",StringComparison.OrdinalIgnoreCase)))warnings.Add("Có metadata/comments trong file gốc; phải rà soát riêng trước phát hành.");
            var tables=document.Descendants(W+"tbl").ToArray();if(tables.Length!=1)throw new InvalidOperationException("Mẫu kỹ thuật cần đúng một bảng lịch7 hoặc8 cột; dùng nhập tay khi mẫu khác.");
            var table=tables[0];var rows=table.Elements(W+"tr").ToArray();if(rows.Length is <2 or >101)throw new InvalidOperationException("Bảng lịch vượt số dòng.");
            string Text(XElement c)=>string.Join("\n",c.Elements(W+"p").Select(p=>string.Concat(p.Descendants(W+"t").Select(t=>t.Value)))).Trim();
            var header=rows[0].Elements(W+"tc").Select(Text).ToArray();if(!header.SequenceEqual(Headers)&&!header.SequenceEqual(Headers.Take(7)))throw new InvalidOperationException("Sai mẫu tại bảng 1, dòng tiêu đề 1: cần 7 cột (Ngày/Buổi/Giờ/Nội dung/Chủ trì/Thành phần/Địa điểm), tùy chọn cột8 Ghi chú.");
            var columns=header.Length;
            var inherited=new Dictionary<int,string>();var result=new List<WeeklyScheduleRow>();var keys=new HashSet<string>();
            for(var i=1;i<rows.Length;i++)
            {
                var grid=Enumerable.Repeat("",8).ToArray();var col=0;var ambiguous=false;
                foreach(var cell in rows[i].Elements(W+"tc"))
                {
                    var spanText=cell.Element(W+"tcPr")?.Element(W+"gridSpan")?.Attribute(W+"val")?.Value??"1";if(!int.TryParse(spanText,out var span)||span<1||span>columns||col+span>columns)throw new InvalidOperationException($"Dòng{i}: ô gộp vượt bảng.");
                    var value=Text(cell);var merge=cell.Element(W+"tcPr")?.Element(W+"vMerge");
                    if(merge!=null&&merge.Attribute(W+"val")?.Value!="restart")
                    {if(!inherited.TryGetValue(col,out value))throw new InvalidOperationException($"Dòng{i}: ô gộp dọc không có ô bắt đầu.");}
                    for(var j=0;j<span;j++){grid[col+j]=value;if(merge!=null)inherited[col+j]=value;else inherited.Remove(col+j);}if(span>1)ambiguous=true;col+=span;
                }
                if(col!=columns)throw new InvalidOperationException($"Dòng{i}: thiếu hoặc lệch cột.");
                if(ambiguous){warnings.Add($"Dòng{i}: gộp ngang làm mơ hồ dữ liệu; chưa tự nhập dòng này, cần nhập tay.");continue;}
                var match=Regex.Match(grid[0],@"\b\d{1,2}/\d{1,2}/\d{4}\b",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
                if(!match.Success||!DateOnly.TryParseExact(match.Value,["d/M/yyyy","dd/MM/yyyy","d/MM/yyyy","dd/M/yyyy"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))throw new InvalidOperationException($"Dòng{i}: không xác định được ngày; chưa thay đổi lịch công khai.");
                var row=new WeeklyScheduleRow{Date=date,Period=grid[1],Time=grid[2],Content=grid[3],Chair=grid[4],Attendees=grid[5],Location=grid[6],Note=grid[7]};
                if(!keys.Add(string.Join('|',date,row.Period,row.Time,row.Content)))warnings.Add($"Dòng{i}: trùng ngày/buổi/giờ/nội dung, cần người biên tập kiểm tra.");result.Add(row);
            }
            if(result.Count==0)throw new InvalidOperationException("Không có dòng lịch rõ ràng; dùng nhập tay.");
            V05ContentPolicy.Validate(new ContentMetadata{WeeklyYear=year,WeeklyWeek=week,WeeklyRowsJson=System.Text.Json.JsonSerializer.Serialize(result)},ContentKind.StaticPage);
            return new(result,warnings,Template);
        }
        catch(Exception e)when(e is InvalidDataException or XmlException){throw new InvalidOperationException("DOCX/ZIP/XML không hợp lệ hoặc vượt giới hạn; lịch công khai giữ nguyên.");}
    }
}
