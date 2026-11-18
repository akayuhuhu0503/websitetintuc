using System.Text;
using LandPortal.Web.Services;

// Pure read-only presentation checks: no SQL, IIS, credentials, files or external requests.
var checks=0;
void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks++;}
void Period(string title,int year,int week){Check(WeeklyScheduleTitle.Period(title)==new WeeklyScheduleKey(year,week),title);}
void Unknown(string title){Check(WeeklyScheduleTitle.Period(title)==null,"Must not infer: "+title);}
Period("Lịch công tác tuần 42 năm 2026 của Lãnh đạo Cục Quản lý đất đai",2026,42);
Period("Lịch công tác tuần thứ 42 năm 2026 (cập nhật lần 04)",2026,42);
Period("Lịch tuần 42/2026",2026,42);
Period("LỊCH CÔNG TÁC TUẦN THỨ 04 NĂM 2026",2026,4);
Period("Lich cong tac tuan 42-2026",2026,42);
Period("Lịch tuần 42 năm 2026".Normalize(NormalizationForm.FormD),2026,42);
Period("Lịch tuần 53 năm 2020",2020,53);
Period("Lịch tuần 53 năm 2026",2026,53);
Period("Lịch tuần 1 năm 2027",2027,1);
Period("Tuần 45. Từ ngày 02/11/2026 đến ngày 08/11/2026",2026,45);
Period("Lịch công tác tuần thứ Tuần 3. Từ ngày 12/01/2026 đến ngày 18/01/2026",2026,3);
Period("TUẦN 4. TỪ NGÀY 19/01/2026 ĐẾN NGÀY 25/01/2026".Normalize(NormalizationForm.FormD),2026,4);
Period("Tuần 1. Từ ngày 29/12/2025 đến ngày 04/01/2026",2026,1);
Period("Tuần 53. Từ ngày 28/12/2026 đến ngày 03/01/2027",2026,53);
Period("Tuần 1. Từ ngày 29/12/2025 đến ngày 31/12/2025",2026,1);
Period("Tuần 45 năm 2026. Từ ngày 2/11/2026 đến 8/11/2026",2026,45);
Unknown("Tuần 41. Từ ngày 12/10/2026 đến ngày 18/10/2026");
Unknown("Tuần 42. Từ ngày 19/10/2026 đến ngày 25/10/2026");
Unknown("Tuần 43. Từ ngày 26/10/2026 đến ngày 01/11/2026");
Unknown("Tuần 44. Từ ngày 02/11/2026 đến ngày 08/11/2026");
Unknown("Tuần 45. Từ ngày 08/11/2026 đến ngày 02/11/2026");
Unknown("Tuần 45. Từ ngày 02/11/2026 đến ngày 09/11/2026");
Unknown("Tuần 45. Từ ngày 02/11/2026 đến ngày 31/11/2026");
Unknown("Tuần 45 năm 2026. Từ ngày 2/11/2026 đến ngày không rõ");
Unknown("Tuần 45 năm 2025. Từ ngày 02/11/2026 đến ngày 08/11/2026");
Unknown("Tuần 45. Từ ngày 02/11/2026 đến ngày 08/11/2026 và tuần 46");
Unknown("Tuần 45. Từ ngày 02/11/2026 đến ngày 08/11/2026; từ ngày 02/11/2026 đến ngày 08/11/2026");
Check(WeeklyScheduleTitle.Period("Tuần 44. Từ ngày 02/11/2026 đến ngày 08/11/2026",2026,45)==new WeeklyScheduleKey(2026,45),"Validated metadata wins over conflicting title");
Unknown("Lịch tuần 53 năm 2021");
Unknown("Lịch tuần 0 năm 2026");
Unknown("Lịch tuần 54 năm 2026");
Unknown("Lịch tuần 420 năm 2026");
Unknown("Lịch tuần 42 năm 20260");
Unknown("Lịch tuần 42 năm 1999");
Unknown("Lịch tuần 42 năm 2201");
Unknown("Lịch công tác tuần 42");
Unknown("Lịch công tác ngày 18/10/2026");
Unknown("Lịch công tác tuần từ ngày 12/10/2026 đến 18/10/2026");
Unknown("Lịch tuần 42 năm 2026 và tuần 43 năm 2026");
Unknown("Lịch tuần 42-43 năm 2026");
Unknown("");
Check(WeeklyScheduleTitle.Period(null)==null,"Missing title");
Check(WeeklyScheduleTitle.Period("Lịch tuần 42 năm 2026",2026,43)==new WeeklyScheduleKey(2026,43),"Published metadata wins");
Check(WeeklyScheduleTitle.Period("Lịch tuần 42 năm 2026",2026,null)==null,"Do not repair incomplete metadata from title");
Check(WeeklyScheduleTitle.Period("Lịch tuần 42 năm 2026",2026,99)==null,"Invalid metadata remains unmatched");
var id=Guid.Parse("11111111-1111-1111-1111-111111111111");
var article=new WeeklyScheduleArticle(id,"Lịch tuần 42 năm 2026",new DateOnly(2026,11,12),null,null,null,false);
Check(article.Period==new WeeklyScheduleKey(2026,42),"Publication date does not reassign the covered week");
Check(article.Url=="/bai-viet/"+id,"Legacy URL preserves public article/attachment authorization");
Check((article with{HasStructuredRelease=true,ScheduleYear=2026,ScheduleWeek=42}).Url=="/lich-tuan?year=2026&week=42","Structured release keeps existing week route");
Check((article with{Title="Lịch công tác",SourceDate=null}).Period==null,"Unknown period stays discoverable without guessing");
var vietnamDate=article with{SourceDate=null,PublishedUtc=new DateTimeOffset(2026,10,18,18,0,0,TimeSpan.Zero)};
Check(vietnamDate.DisplayDate==new DateOnly(2026,10,19),"Display date uses Vietnam timezone");
Console.WriteLine($"PASS: {checks} weekly schedule title/link checks (no database or IIS).");
if(args.Length==2)
{
    var rows=System.Text.Json.JsonSerializer.Deserialize<SourceTitle[]>(File.ReadAllText(args[0]),new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    var report=rows.Select(row=>new{row.Stt,row.Title,period=WeeklyScheduleTitle.Inspect(row.Title).Period,review=WeeklyScheduleTitle.Inspect(row.Title).ReviewReason}).ToArray();
    File.WriteAllText(args[1],System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"SOURCE REVIEW: {report.Length} titles; {report.Count(x=>x.review!=null)} need review; read-only, no publication dates changed.");
}
record SourceTitle(int Stt,string Title);
