using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public sealed record InquiryExport(byte[] Bytes,int Rows,bool IncludesPii);
public sealed class InquiryExcelService(PortalDbContext db,InteractionService interaction,CurrentPermissionService permissions)
{
    public async Task<InquiryExport> ExportAsync(ContentActor actor,InteractionState? state,string? field,bool includePii)
    {
        actor=await permissions.GetAsync(actor.UserId);if(!actor.Has(Permissions.InquiryExport)||!actor.Has(Permissions.InquiryRead)||(includePii&&!actor.Has(Permissions.InquiryExportPii)))throw new UnauthorizedAccessException();
        var rows=interaction.Inbox(actor,InteractionKind.Inquiry);if(state!=null)rows=rows.Where(x=>x.State==state);if(!string.IsNullOrWhiteSpace(field)){if(field.Length>120)throw new InvalidOperationException("Lọc lĩnh vực quá dài.");rows=rows.Where(x=>x.InquiryField==field);}
        if(await rows.CountAsync()>1000)throw new InvalidOperationException("Giới hạn1000 hồ sơ; lọc hẹp hơn trước khi xuất.");
        var items=await rows.OrderByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).ToListAsync();var data=new List<string[]>();
        var headers=new List<string>{"Mã nội bộ","Lĩnh vực","Trạng thái","Tiếp nhận (UTC+7)","Hạn (UTC+7)","Chính sách"};if(includePii)headers.AddRange(["Họ tên","Email","Điện thoại","Tiêu đề","Nội dung gốc"]);data.Add(headers.ToArray());
        foreach(var row in items)
        {
            var values=new List<string>{row.Id.ToString(),row.InquiryField??"Chưa phân loại",row.State.ToString(),row.CreatedUtc.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm",CultureInfo.InvariantCulture),row.InquiryDueUtc?.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm",CultureInfo.InvariantCulture)??"Chưa áp dụng",row.SlaIsOfficial?"Phiên bản đã xác nhận":InquiryCalendarService.TemporaryLabel};
            if(includePii){var original=interaction.Unprotect<InteractionInput>(row.ProtectedOriginal);values.AddRange([original.Name,original.Email,original.Phone,original.Title,original.Text]);}data.Add(values.ToArray());
        }
        var bytes=Workbook(data);db.AuditLogs.Add(new(){Actor=actor.UserId,Action="inquiry.exported",Detail=JsonSerializer.Serialize(new{state,field,Rows=items.Count,includePii})});await db.SaveChangesAsync();return new(bytes,items.Count,includePii);
    }
    public static byte[] Workbook(IReadOnlyList<string[]> rows)
    {
        using var memory=new MemoryStream();
        using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
        {
            void Entry(string name,string value){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(value);}
            Entry("[Content_Types].xml","<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            Entry("_rels/.rels","<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Entry("xl/workbook.xml","<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Hỏi đáp\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Entry("xl/_rels/workbook.xml.rels","<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            using var sheet=zip.CreateEntry("xl/worksheets/sheet1.xml").Open();using var xml=XmlWriter.Create(sheet,new(){Encoding=new UTF8Encoding(false)});xml.WriteStartDocument();xml.WriteStartElement("worksheet","http://schemas.openxmlformats.org/spreadsheetml/2006/main");xml.WriteStartElement("sheetData");
            for(var r=0;r<rows.Count;r++)
            {
                xml.WriteStartElement("row");xml.WriteAttributeString("r",(r+1).ToString());
                for(var c=0;c<rows[r].Length;c++)
                {
                    // All untrusted cells are explicit inline strings. No formulas, external links or macros.
                    xml.WriteStartElement("c");xml.WriteAttributeString("r",Column(c+1)+(r+1));xml.WriteAttributeString("t","inlineStr");xml.WriteStartElement("is");xml.WriteStartElement("t");xml.WriteAttributeString("xml","space","http://www.w3.org/XML/1998/namespace","preserve");xml.WriteString(XmlText(rows[r][c]));xml.WriteEndElement();xml.WriteEndElement();xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            xml.WriteEndElement();xml.WriteEndElement();xml.WriteEndDocument();
        }
        return memory.ToArray();
    }
    private static string Column(int number){var result="";while(number>0){number--;result=(char)('A'+number%26)+result;number/=26;}return result;}
    private static string XmlText(string value)
    {
        var text=new StringBuilder();foreach(var rune in value.EnumerateRunes())
            if(rune.Value is 9 or 10 or 13 or >=0x20 and <=0xd7ff or >=0xe000 and <=0xfffd or >=0x10000 and <=0x10ffff)text.Append(rune.ToString());
        return text.ToString();
    }
}
