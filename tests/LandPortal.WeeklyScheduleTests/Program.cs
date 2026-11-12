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
