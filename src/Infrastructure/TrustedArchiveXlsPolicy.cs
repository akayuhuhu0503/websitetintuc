using System.Buffers.Binary;
using OpenMcdf;

namespace LandPortal.Infrastructure;

// Only called for XLS inside an administrator-confirmed internal ZIP. This is
// a bounded structural allowlist, not an antivirus verdict or an Excel engine.
internal static class TrustedArchiveXlsPolicy
{
    private static ushort U16(ReadOnlySpan<byte> b, int p) => BinaryPrimitives.ReadUInt16LittleEndian(b[p..]);
    private static uint U32(ReadOnlySpan<byte> b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b[p..]);
    private static InvalidDataException Denied(string reason) => new("XLS nội bộ: " + reason);

    public static void Validate(Stream input, CancellationToken ct)
    {
        try { ValidateOle(input, ct); }
        catch (FileFormatException) { throw Denied("OLE bị lỗi cấu trúc; chưa tiếp nhận."); }
    }
    private static void ValidateOle(Stream input, CancellationToken ct)
    {
        if (input.Length is < 512 or > 10 * 1024 * 1024) throw Denied("vượt giới hạn 10 MiB hoặc thiếu cấu trúc OLE.");
        input.Position = 0;
        using var root = RootStorage.Open(input, StorageModeFlags.StrictValidation | StorageModeFlags.LeaveOpen);
        var entries = root.EnumerateEntries().Take(5).ToArray();
        if (entries.Length is < 1 or > 3 || entries.Any(e => e.Type != EntryType.Stream ||
            e.Name is not ("Workbook" or "\u0005SummaryInformation" or "\u0005DocumentSummaryInformation")) ||
            entries.Count(e => e.Name == "Workbook") != 1)
            throw Denied("chỉ nhận Workbook BIFF8 và metadata; không nhận VBA, macro, đối tượng OLE nhúng hoặc luồng khác.");
        using var workbook = root.OpenStream("Workbook");
        if (workbook.Length is < 24 or > 10 * 1024 * 1024) throw Denied("Workbook không hợp lệ.");
        var bytes = new byte[(int)workbook.Length]; workbook.ReadExactly(bytes);
        ValidateBiff(bytes, ct);
    }

    // Inert formatting/cell/cache records in the supported BIFF8 subset. Unknown
    // extensions fail closed; FILEPASS, VBA, DDE, external names, links, queries,
    // array formulas and embedded controls are deliberately absent.
    private static readonly HashSet<ushort> Inert = [0xE1,0xC1,0xE2,0x5C,0x42,0x161,0x1C0,0x13D,0x9C,0x19,0x12,0x13,
        0x1AF,0x1BC,0x3D,0x40,0x8D,0x22,0xE,0x1B7,0xDA,0x31,0x41E,0xE0,0x87C,0x87D,0x293,0x892,0x88E,
        0x160,0x89A,0x8A3,0x8C,0x1C1,0x866,0xFC,0x3C,0xFF,0x863,0x896,0x89B,0x88C,0x20B,0xD,0xC,0xF,
        0x11,0x10,0x5F,0x2A,0x2B,0x82,0x80,0x225,0x14,0x15,0x83,0x84,0x26,0x27,0x28,0x29,0x4D,0xA1,
        0x89C,0x55,0x7D,0x9B,0x9D,0x9E,0x200,0x208,0xFD,0xBE,0x27E,0xBD,0x203,0x201,0xD7,0x23E,0x88B,
        0xA0,0x1D,0x99,0xE5,0x867,0xEF,0x204,0x205,0x207];

    private static void ValidateBiff(byte[] bytes, CancellationToken ct)
    {
        var p = 0; var open = false; var globals = false; var sheets = new HashSet<uint>(); var actualSheets = new HashSet<uint>();
        var shared = new HashSet<(int,int)>(); var uses = new List<(int,int)>();
        using var drawing = new MemoryStream(); using var drawingGroup = new MemoryStream();
        ushort previous = 0;
        var lastCell = (-1,-1); int supbooks = 0; bool internalReferences = false;
        while (p < bytes.Length)
        {
            ct.ThrowIfCancellationRequested();
            if (p + 4 > bytes.Length) throw Denied("record BIFF bị cắt.");
            var id = U16(bytes,p); var length = U16(bytes,p+2); var start = p; p += 4;
            if (length > 8224 || p + length > bytes.Length) throw Denied("kích thước record BIFF không hợp lệ.");
            var d = bytes.AsSpan(p,length); p += length;
            if (id == 0x809)
            {
                if (drawing.Length != 0) { Drawing(drawing.ToArray(), 0, ct); drawing.SetLength(0); }
                if (open || length != 16 || U16(d,0) != 0x600 || U16(d,2) is not (5 or 16) || (!globals && (start != 0 || U16(d,2) != 5)) || (globals && U16(d,2) != 16))
                    throw Denied("không nhận macro sheet, dialog hoặc phiên bản ngoài BIFF8.");
                if (globals) actualSheets.Add((uint)start); globals = true; open = true; continue;
            }
            if (!open) throw Denied("dữ liệu ngoài các substream BIFF.");
            switch (id)
            {
                case 0xA: if (length != 0) throw Denied("EOF không hợp lệ."); open=false; break;
                case 0x85:
                    if (length < 8 || d[5] != 0 || !sheets.Add(U32(d,0))) throw Denied("sheet không hợp lệ hoặc có macro."); break;
                case 0x81: if (length != 2 || (U16(d,0) & 0x10) != 0) throw Denied("không nhận dialog sheet."); break;
                case 0x1AE:
                    if (++supbooks != 1 || length != 4 || U16(d,2) != 0x0401) throw Denied("liên kết workbook ngoài/DDE/add-in không được phép.");
                    internalReferences = true; break;
                case 0x17:
                    if (!internalReferences || length < 2 || length != 2 + U16(d,0)*6) throw Denied("bảng tham chiếu không hợp lệ.");
                    for(var i=2;i<length;i+=6) if(U16(d,i)!=0 || U16(d,i+2)>=sheets.Count || U16(d,i+4)>=sheets.Count) throw Denied("tham chiếu ngoài workbook."); break;
                case 0x18:
                    // Only built-in print area/titles/filter range, exactly one internal Area3d.
                    if (!internalReferences || length != 27 || (U16(d,0)&~0x21)!=0 || (U16(d,0)&0x20)==0 || d[3]!=1 || U16(d,4)!=11 || d[14]!=0 || d[15] is not (6 or 7 or 13) || d[16]!=0x3B)
                        throw Denied("tên/công thức có thể chứa macro hoặc không thuộc vùng in/lọc nội bộ."); break;
                case 6:
                    if (length < 22 || U16(d,20)!=length-22) throw Denied("công thức BIFF không hợp lệ.");
                    lastCell=(U16(d,0),U16(d,2)); Formula(d[22..], uses); break;
                case 0x4BC:
                    if(length<10 || U16(d,8)!=length-10 || lastCell.Item1<0) throw Denied("công thức dùng chung không hợp lệ.");
                    shared.Add(lastCell); Formula(d[10..], null); break;
                case 0x5D: ValidateObject(d); break;
                case 0xEB: drawingGroup.Write(d); break;
                case 0xEC: drawing.Write(d); break;
                case 0x3C:
                    if (previous == 0xEB) drawingGroup.Write(d);
                    else if (previous == 0xEC) drawing.Write(d);
                    break;
                default: if (!Inert.Contains(id)) throw Denied($"record 0x{id:X4} chưa thuộc tập cấu trúc được phép."); break;
            }
            if (id != 0x3C) previous = id;
        }
        if(drawing.Length!=0) Drawing(drawing.ToArray(),0,ct);
        if(drawingGroup.Length!=0) Drawing(drawingGroup.ToArray(),0,ct);
        if(open || !globals || sheets.Count==0 || !sheets.SetEquals(actualSheets) || uses.Any(x=>!shared.Contains(x))) throw Denied("thiếu sheet, EOF hoặc công thức dùng chung.");
    }
    private static void Formula(ReadOnlySpan<byte> b, List<(int,int)>? sharedUses)
    {
        for(var p=0;p<b.Length;)
        {
            var t=b[p++]; var normalized=t>=0x20 ? (t&0x1F)|0x20 : t;
            var size=normalized switch { 0x01=>4, >=0x03 and <=0x16=>0, 0x1C or 0x1D=>1, 0x1E=>2, 0x1F=>8, 0x24 or 0x2C=>4, 0x25 or 0x2D=>8, _=>-1 };
            if(size<0 || p+size>b.Length)throw Denied("công thức ngoài phép tính và tham chiếu ô nội bộ được hỗ trợ.");
            if(t==1){if(sharedUses==null || b.Length!=5)throw Denied("tham chiếu công thức dùng chung không hợp lệ.");sharedUses.Add((U16(b,p),U16(b,p+2)));}
            p+=size;
        }
    }
    private static void ValidateObject(ReadOnlySpan<byte> b)
    {
        // BIFF autofilter dropdown: FtCmo + FtSbs + unbound FtLbsData only.
        // No FtMacro, PictFmla, linked range, ActiveX or additional subrecords.
        if (b.Length != 70 || U16(b,0)!=0x15 || U16(b,2)!=0x12 || U16(b,4)!=0x14 ||
            U16(b,22)!=0xC || U16(b,24)!=20 || U16(b,46)!=0x13 || U16(b,48)<20 ||
            U16(b,50)!=0 || U16(b,52)!=0 || (U16(b,56)&0x300)!=0x300)
            throw Denied("đối tượng không phải danh sách lọc nội bộ không gắn macro/công thức.");
    }

    private static void Drawing(ReadOnlySpan<byte> b, int depth, CancellationToken ct)
    {
        if(depth>8)throw Denied("OfficeArt lồng quá sâu.");
        for(var p=0;p<b.Length;)
        {
            ct.ThrowIfCancellationRequested();
            if(b.Length-p<8)throw Denied("OfficeArt bị cắt.");
            var flags=U16(b,p);var type=U16(b,p+2);var length=U32(b,p+4);p+=8;
            if(length>(uint)(b.Length-p))throw Denied("OfficeArt sai kích thước.");
            var d=b.Slice(p,(int)length);p+=(int)length;
            if((flags&15)==15)
            { if(type is not (0xF000 or 0xF002 or 0xF003 or 0xF004))throw Denied("container OfficeArt chưa được phép."); Drawing(d,depth+1,ct); continue; }
            if(type==0xF00B)
            {
                // Fixed scalar presentation properties only. Reject complex
                // properties (strings/macros/hyperlinks), blip references, etc.
                if((flags&15)!=3 || d.Length!=(flags>>4)*6)throw Denied("OfficeArt có thuộc tính phức hợp.");
                for(var j=0;j<d.Length;j+=6)
                    if(U16(d,j) is not (0x7F or 0xBF or 0x181 or 0x1C0 or 0x1FF or 0x3BF))throw Denied("OfficeArt có thuộc tính ngoài định dạng hiển thị.");
            }
            else if(type==0xF00A)
            { if(d.Length!=8 || (flags>>4) is not (0 or 201) || (U32(d,4)&0x10)!=0)throw Denied("OfficeArt không phải khung/nhóm lọc, hoặc có OLE."); }
            else if(type switch { 0xF006=>d.Length>=16&&(d.Length-16)%8==0, 0xF008=>d.Length==8,0xF009=>d.Length==16,0xF010=>d.Length==18,0xF011=>d.Length==0,0xF11E=>d.Length==16,_=>false } is false)
                throw Denied($"OfficeArt 0x{type:X4} chưa được phép.");
        }
    }
}
