using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using LandPortal.Domain;

namespace LandPortal.Infrastructure;

public static class LandStatisticsWorkbook
{
    public static readonly string[] Headers=["Mã tỉnh","Tên tỉnh/thành phố","Tổng diện tích","Đất nông nghiệp","Đất phi nông nghiệp","Đất chưa sử dụng"];
    // Decision 19/2025/QD-TTg, effective 01/07/2025. Reference catalog, never land-area data.
    public const string CatalogSource="https://datafiles.chinhphu.vn/cpp/files/vbpq/2025/7/19ttg.signed.pdf";
    public static readonly IReadOnlyDictionary<string,string> Provinces=new Dictionary<string,string>{
        ["01"]="Thành phố Hà Nội",["04"]="Tỉnh Cao Bằng",["08"]="Tỉnh Tuyên Quang",["11"]="Tỉnh Điện Biên",["12"]="Tỉnh Lai Châu",["14"]="Tỉnh Sơn La",["15"]="Tỉnh Lào Cai",["19"]="Tỉnh Thái Nguyên",["20"]="Tỉnh Lạng Sơn",["22"]="Tỉnh Quảng Ninh",["24"]="Tỉnh Bắc Ninh",["25"]="Tỉnh Phú Thọ",["31"]="Thành phố Hải Phòng",["33"]="Tỉnh Hưng Yên",["37"]="Tỉnh Ninh Bình",["38"]="Tỉnh Thanh Hóa",["40"]="Tỉnh Nghệ An",["42"]="Tỉnh Hà Tĩnh",["44"]="Tỉnh Quảng Trị",["46"]="Thành phố Huế",["48"]="Thành phố Đà Nẵng",["51"]="Tỉnh Quảng Ngãi",["52"]="Tỉnh Gia Lai",["56"]="Tỉnh Khánh Hòa",["66"]="Tỉnh Đắk Lắk",["68"]="Tỉnh Lâm Đồng",["75"]="Tỉnh Đồng Nai",["79"]="Thành phố Hồ Chí Minh",["80"]="Tỉnh Tây Ninh",["82"]="Tỉnh Đồng Tháp",["86"]="Tỉnh Vĩnh Long",["91"]="Tỉnh An Giang",["92"]="Thành phố Cần Thơ",["96"]="Tỉnh Cà Mau"};
    public static LandAreaPreview Read(string path,bool blockMismatch)
    {
        using var zip=ZipFile.OpenRead(path);
        if(zip.Entries.Count>100||zip.Entries.Sum(e=>e.Length)>20*1024*1024||zip.Entries.Any(e=>e.Length>2*1024*1024))throw new InvalidDataException("Excel vượt giới hạn cấu trúc.");
        XDocument Xml(ZipArchiveEntry entry){using var r=XmlReader.Create(entry.Open(),new(){DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2*1024*1024});return XDocument.Load(r);}
        var sheets=zip.Entries.Where(e=>e.FullName.StartsWith("xl/worksheets/")&&e.FullName.EndsWith(".xml")).ToArray();
        if(sheets.Length!=1)throw new InvalidDataException("Chỉ nhận một trang tính chứa đúng mẫu 6 cột.");
        XNamespace n="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var strings=zip.GetEntry("xl/sharedStrings.xml") is {} shared?Xml(shared).Descendants(n+"si").Select(e=>string.Concat(e.Descendants(n+"t").Select(t=>t.Value))).ToArray():[];
        var sheet=Xml(sheets[0]);if(sheet.Descendants(n+"mergeCell").Any())throw new InvalidDataException("Mẫu thống kê không dùng ô gộp.");
        var input=sheet.Descendants(n+"row").ToArray();if(input.Length>36)throw new InvalidDataException("Mẫu tối đa 34 tỉnh/thành và dòng tiêu đề.");
        var issues=new List<LandAreaIssue>();var rows=new List<LandAreaRow>();var seen=new HashSet<string>();
        var rowNumbers=input.Select(r=>r.Attribute("r")?.Value??"").ToArray();
        if(rowNumbers.Length==0||rowNumbers[0]!="1"||rowNumbers.Distinct().Count()!=rowNumbers.Length)issues.Add(new(1,"A:F","Thiếu tiêu đề dòng 1 hoặc vị trí dòng bị trùng."));
        foreach(var row in input)
        {
            if(!int.TryParse(row.Attribute("r")?.Value,out var number)||number<1||number>36)throw new InvalidDataException("Thiếu/sai vị trí dòng Excel.");
            var values=new string[6];var occupied=new HashSet<int>();
            foreach(var cell in row.Elements(n+"c"))
            {
                var reference=cell.Attribute("r")?.Value??"";var letters=new string(reference.TakeWhile(char.IsLetter).ToArray());var index=letters.Length==1?letters[0]-'A':-1;
                if(index is <0 or >5||reference!=letters+number||!occupied.Add(index)){issues.Add(new(number,reference,"Cột hoặc vị trí ô không thuộc mẫu A:F."));continue;}
                if(cell.Element(n+"f")!=null){issues.Add(new(number,reference,"Chỉ nhận giá trị thuần, không nhận công thức."));continue;}
                var type=cell.Attribute("t")?.Value;var value=cell.Element(n+"v")?.Value??"";
                if(type=="s"){if(!int.TryParse(value,out var i)||i<0||i>=strings.Length){issues.Add(new(number,reference,"Chuỗi Excel không hợp lệ."));continue;}value=strings[i];}
                else if(type=="inlineStr")value=string.Concat(cell.Descendants(n+"t").Select(t=>t.Value));
                else if(type is "e" or "b"){issues.Add(new(number,reference,"Kiểu ô không hợp lệ."));continue;}
                values[index]=value.Trim();
            }
            if(number==1){for(var i=0;i<6;i++)if(values[i]!=Headers[i])issues.Add(new(1,((char)('A'+i)).ToString(),"Sai tiêu đề; cần: "+Headers[i]));continue;}
            if(values.All(string.IsNullOrEmpty))continue;
            var code=values[0]??"";if(code.Length==1&&char.IsDigit(code[0]))code="0"+code;
            if(!Provinces.TryGetValue(code,out var province)){issues.Add(new(number,"A","Mã tỉnh không thuộc danh mục 34 tỉnh/thành."));continue;}
            static string Name(string? value)=>(value??"").Replace("Thành phố ","",StringComparison.OrdinalIgnoreCase).Replace("Tỉnh ","",StringComparison.OrdinalIgnoreCase).Trim();
            if(!Name(values[1]).Equals(Name(province),StringComparison.OrdinalIgnoreCase))issues.Add(new(number,"B","Tên không khớp mã tỉnh: "+province));
            if(!seen.Add(code))issues.Add(new(number,"A","Mã tỉnh trùng trong tệp."));
            var numbers=new decimal[4];for(var i=0;i<4;i++)if(!decimal.TryParse(values[i+2],NumberStyles.Float,CultureInfo.InvariantCulture,out numbers[i])||numbers[i]<0||numbers[i]>1_000_000_000_000m)issues.Add(new(number,((char)('C'+i)).ToString(),"Cần số không âm, không dấu phân cách nghìn; ô số Excel hoặc dấu chấm thập phân."));
            if(numbers[0]!=numbers[1]+numbers[2]+numbers[3])issues.Add(new(number,"C:F","Tổng diện tích khác tổng ba nhóm đất.",!blockMismatch));
            rows.Add(new(code,province,numbers[0],numbers[1],numbers[2],numbers[3]));
        }
        if(seen.Count!=34)issues.Add(new(0,"A","Cần đủ 34 mã tỉnh/thành, mỗi mã đúng một dòng."));
        return new(rows,issues);
    }
}
