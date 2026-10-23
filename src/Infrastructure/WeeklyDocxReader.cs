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
// Supports the technical 7/8-column form and the TTDL 5-column leadership form.
// Parsing a sample is separate from approval to publish it or AC-16.1 (3 weeks).
// Does not publish, save a file, fetch relationships, execute Word or change live SQL.
public static class WeeklyDocxReader
{
    public const string Template="Weekly-multiform-3";
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
            var warnings=new List<string>{"Đọc tệp không cấp quyền phát hành. Cần đối chiếu bản xem trước; một tuần không thay thế nghiệm thu ba tuần nghiệp vụ khác nhau.","Tệp gốc, thông tin ẩn và nhận xét giữ riêng tư đến khi có quyền và xác nhận phát hành."};
            if(zip.Entries.Any(x=>x.FullName.StartsWith("docProps/",StringComparison.Ordinal)||x.FullName.Contains("comments",StringComparison.OrdinalIgnoreCase)))warnings.Add("Có metadata/comments trong file gốc; phải rà soát riêng trước phát hành.");
            string Text(XElement c)=>string.Join("\n",c.Elements(W+"p").Select(p=>string.Concat(p.Descendants().Select(t=>t.Name==W+"t"?t.Value:t.Name==W+"br"||t.Name==W+"cr"?"\n":t.Name==W+"tab"?"\t":"")))).Trim();
            string Normalize(string x)=>Regex.Replace(LandPortal.Application.TextNormalizer.Search(x),"[^a-z0-9]+"," ").Trim();
            bool Five(string[] h)=>h.Length==5&&Normalize(h[0])=="thu ngay"&&new[]{"lanh dao trung tam","lanh dao tt"}.Contains(Normalize(h[1]))&&h.Skip(2).Select(Normalize).SequenceEqual(new[]{"thoi gian","noi dung cong viec","dia diem"});
            bool Standard(string[] h)=>h.Select(Normalize).SequenceEqual(Headers.Select(Normalize))||h.Select(Normalize).SequenceEqual(Headers.Take(7).Select(Normalize));
            var matches=document.Descendants(W+"tbl").SelectMany((t,ti)=>t.Elements(W+"tr").Take(5).Select((r,ri)=>new{Table=t,TableIndex=ti+1,Row=ri,Header=r.Elements(W+"tc").Select(Text).ToArray()})).Where(x=>Five(x.Header)||Standard(x.Header)).ToArray();
            if(matches.Length!=1)throw new InvalidOperationException("Không xác định được duy nhất bảng lịch: cần mẫu lãnh đạo Trung tâm 5 cột hoặc mẫu 7/8 cột. Kiểm tra dòng tiêu đề; lịch đang đăng không thay đổi.");
            var matchTable=matches[0];var table=matchTable.Table;var rows=table.Elements(W+"tr").Skip(matchTable.Row).ToArray();if(rows.Length is <2 or >101)throw new InvalidOperationException("Bảng lịch vượt số dòng.");
            var header=matchTable.Header;var five=Five(header);
            if(five)warnings.Add("Đây là lịch lãnh đạo Trung tâm; không tự công bố thành lịch chính thức của Cục. Buổi không có căn cứ được giữ Chưa xác định; ký hiệu giờ giữ nguyên.");
            var sourceText=string.Join("\n",document.Descendants(W+"p").Where(p=>!p.Ancestors().Contains(table)).Select(p=>string.Concat(p.Descendants(W+"t").Select(t=>t.Value))));
            var sourceYear=Regex.Match(Normalize(sourceText),@"\bnam\s+(\d{4})\b");var sourceWeek=Regex.Match(Normalize(sourceText),@"\btuan\s+(?:thu\s+)?(\d{1,2})\b");
            if(sourceYear.Success&&int.Parse(sourceYear.Groups[1].Value,CultureInfo.InvariantCulture)!=year||sourceWeek.Success&&int.Parse(sourceWeek.Groups[1].Value,CultureInfo.InvariantCulture)!=week)throw new InvalidOperationException("Tuần/năm ghi trong Word không khớp hồ sơ đang mở; chưa thay đổi lịch đã đăng.");
            foreach(var line in sourceText.Split('\n').Where(x=>Normalize(x).Contains("cap nhat")||Normalize(x).Contains("phat hanh")))warnings.Add("Thông tin nguyên bản: "+line+". Lần cập nhật trên Word không phải số phiên bản website.");
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
                    {var own=value;if(!inherited.TryGetValue(col,out value))throw new InvalidOperationException($"Dòng{i}: ô gộp dọc không có ô bắt đầu.");if(own.Length>0&&own!=value){value+="\n"+own;warnings.Add($"Dòng{i+matchTable.Row+1}: ô gộp có thêm chữ, đã giữ cả phần tiếp nối để rà soát.");}}
                    for(var j=0;j<span;j++){grid[col+j]=value;if(merge!=null)inherited[col+j]=value;else inherited.Remove(col+j);}if(span>1)ambiguous=true;col+=span;
                }
                if(col!=columns)throw new InvalidOperationException($"Dòng{i}: thiếu hoặc lệch cột.");
                if(ambiguous){warnings.Add($"Bảng {matchTable.TableIndex}, dòng {i+matchTable.Row+1}: gộp ngang cần rà soát; giữ chữ gốc: "+string.Join(" | ",grid.Take(columns)));continue;}
                if(grid.Take(columns).All(string.IsNullOrWhiteSpace))continue;
                var match=Regex.Match(grid[0],@"\b\d{1,2}/\d{1,2}(?:/\d{4})?\b",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
                var dateText=match.Value;if(five&&dateText.Count(x=>x=='/')==1)dateText+="/"+year;
                if(!match.Success||!DateOnly.TryParseExact(dateText,["d/M/yyyy","dd/MM/yyyy","d/MM/yyyy","dd/M/yyyy"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))throw new InvalidOperationException($"Bảng {matchTable.TableIndex}, dòng {i+matchTable.Row+1}: không xác định được ngày; chưa thay đổi lịch công khai.");
                var row=new WeeklyScheduleRow{Date=date,Period=five?"Chưa xác định":grid[1],Time=grid[2],Content=grid[3],Chair=five?grid[1]:grid[4],Attendees=five?"":grid[5],Location=five?grid[4]:grid[6],Note=five?"":grid[7]};
                if(five){var participants=Regex.Matches(row.Content,@"(?:^|\n)\s*TP\s*:\s*([^\n]+)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));row.Attendees=string.Join("\n",participants.Select(x=>x.Groups[1].Value.Trim()));if(row.Time.Contains("*)",StringComparison.Ordinal)||row.Period=="Chưa xác định")warnings.Add($"Dòng {i+matchTable.Row+1}: giữ nguyên giờ ‘{row.Time}’; cán bộ kiểm tra buổi nếu cần.");}
                if(!keys.Add(string.Join('|',date,row.Period,row.Time,row.Content)))warnings.Add($"Dòng{i}: trùng ngày/buổi/giờ/nội dung, cần người biên tập kiểm tra.");result.Add(row);
            }
            if(result.Count==0)throw new InvalidOperationException("Không có dòng lịch rõ ràng; dùng nhập tay.");
            V05ContentPolicy.Validate(new ContentMetadata{WeeklyYear=year,WeeklyWeek=week,WeeklyRowsJson=System.Text.Json.JsonSerializer.Serialize(result)},ContentKind.StaticPage);
            return new(result,warnings,five?"TTDL-leadership-5-columns":Template);
        }
        catch(Exception e)when(e is InvalidDataException or XmlException){throw new InvalidOperationException("DOCX/ZIP/XML không hợp lệ hoặc vượt giới hạn; lịch công khai giữ nguyên.");}
    }
}
