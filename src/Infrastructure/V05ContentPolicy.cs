using System.Globalization;
using System.Net;
using System.Text.Json;
using LandPortal.Domain;
namespace LandPortal.Infrastructure;
public sealed class WeeklyScheduleRow
{
    public DateOnly? Date { get; set; }
    public string Period { get; set; }="";
    public string Time { get; set; }="";
    public string Content { get; set; }="";
    public string Chair { get; set; }="";
    public string Attendees { get; set; }="";
    public string Location { get; set; }="";
    public string Note { get; set; }="";
}
public static class V05ContentPolicy
{
    public static readonly IReadOnlyDictionary<string,string> Groups=new Dictionary<string,string>{["giay-chung-nhan-sai-hong-huy"]="Giấy chứng nhận sai hỏng, hủy",["nguoi-nuoc-ngoai-dinh-cu-o-viet-nam"]="Người nước ngoài định cư ở Việt Nam",["to-chuc-vi-pham-phap-luat-dat-dai"]="Tổ chức vi phạm pháp luật đất đai"};
    public static void Validate(ContentMetadata m,ContentKind kind)
    {
        if (m.WeeklyOriginalReviewEvidence?.Length > 2000 || m.WeeklyOriginalAssetId != null && m.WeeklyYear == null) throw new InvalidOperationException("Tham chiếu Word hoặc xác nhận rà soát không hợp lệ.");
        if(m.DisclosureGroupId=="")m.DisclosureGroupId=null;
        if(m.DisclosureGroupId!=null)
        {
            if(kind!=ContentKind.Article||!Groups.ContainsKey(m.DisclosureGroupId)||m.WeeklyWeek!=null)throw new InvalidOperationException("Nhóm công khai thông tin không hợp lệ cho mẫu này.");
            if(m.Province?.Length>200||m.ProvinceCode?.Length>20||m.DisclosureYear is <1900 or >2200||m.PublicationSource?.Length>1000||m.LegalBasisReference?.Length>2000||m.PublicationReviewEvidence?.Length>2000)throw new InvalidOperationException("Metadata công khai vượt giới hạn.");
        }
        else if(m.Province!=null||m.ProvinceCode!=null||m.DisclosureYear!=null||m.PublicationSource!=null||m.LegalBasisReference!=null||m.PublicationReviewEvidence!=null)throw new InvalidOperationException("Chọn nhóm công khai thông tin cho metadata này.");
        if(m.WeeklyYear!=null||m.WeeklyWeek!=null||m.WeeklyRowsJson!=null||m.WeeklyChangeNote!=null)
        {
            if(kind!=ContentKind.StaticPage||m.WeeklyYear is not(>=2000 and <=2200)||m.WeeklyWeek is not(>=1 and <=53)||m.WeeklyWeek>ISOWeek.GetWeeksInYear(m.WeeklyYear.Value)||m.WeeklyChangeNote?.Length>1000||m.WeeklyRowsJson?.Length>30000)throw new InvalidOperationException("Nhập đúng năm/tuần ISO và bảng lịch trong mẫu trang tĩnh.");
            var rows=Rows(m);if(rows.Count is <1 or >100)throw new InvalidOperationException("Lịch cần từ1 đến100 dòng.");
            foreach(var r in rows)
                if(r.Date==null||ISOWeek.GetYear(r.Date.Value.ToDateTime(TimeOnly.MinValue))!=m.WeeklyYear||ISOWeek.GetWeekOfYear(r.Date.Value.ToDateTime(TimeOnly.MinValue))!=m.WeeklyWeek||r.Period is not("Sáng" or "Chiều" or "Cả ngày" or "Chưa xác định")||string.IsNullOrWhiteSpace(r.Content)||new[]{r.Time,r.Content,r.Chair,r.Attendees,r.Location,r.Note}.Any(x=>x==null||x.Length>2000||x.Any(c=>char.IsControl(c)&&c is not('\n' or '\r' or '\t'))))throw new InvalidOperationException("Dòng lịch sai tuần, buổi hoặc vượt giới hạn; lịch công khai chưa thay đổi.");
        }
    }
    public static List<WeeklyScheduleRow> Rows(ContentMetadata m)
    {
        try{var rows=JsonSerializer.Deserialize<List<WeeklyScheduleRow>>(m.WeeklyRowsJson??"[]")??[];foreach(var r in rows){r.Period??="";r.Time??="";r.Content??="";r.Chair??="";r.Attendees??="";r.Location??="";r.Note??="";}return rows;}catch(JsonException){throw new InvalidOperationException("Bảng lịch chưa hợp lệ.");}
    }
    public static string Table(ContentMetadata m)
    {
        string E(string? s)=>WebUtility.HtmlEncode(s??"");
        return "<table><thead><tr><th>Ngày</th><th>Buổi</th><th>Giờ</th><th>Nội dung</th><th>Chủ trì</th><th>Thành phần</th><th>Địa điểm</th><th>Ghi chú</th></tr></thead><tbody>"+string.Concat(Rows(m).Select(r=>$"<tr><td>{r.Date:dd/MM/yyyy}</td><td>{E(r.Period)}</td><td>{E(r.Time)}</td><td>{E(r.Content)}</td><td>{E(r.Chair)}</td><td>{E(r.Attendees)}</td><td>{E(r.Location)}</td><td>{E(r.Note)}</td></tr>"))+"</tbody></table>";
    }
}
