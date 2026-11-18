using System.Text.Json;
using LandPortal.Infrastructure;

namespace LandPortal.WeeklyImport;

// Explicit, hash-bound derivative selection. The original ZIP, workbook and date decisions remain the source of identity/dates.
public sealed record PlanningPdfReplacement(int Stt, string SourceEntry, string SourceSha256, int Pages,
    string RelativePath, string Sha256, long Bytes, bool SourceHasSignatureObjects, string Notice);
public sealed record PlanningPdfReplacementSet(string ZipSha256, string WorkbookSha256, IReadOnlyList<PlanningPdfReplacement> Files);
public sealed class PlanningPdfOverrides
{
    public required string ManifestSha256 { get; init; }
    public required PlanningPdfReplacementSet Set { get; init; }
    public required IReadOnlyDictionary<string,string> Paths { get; init; }
    public static PlanningPdfOverrides Read(string manifest)
    {
        manifest=Path.GetFullPath(manifest);
        if(new FileInfo(manifest).Length>256*1024)throw new InvalidDataException("Manifest PDF quá lớn.");
        var bytes=File.ReadAllBytes(manifest);
        var set=JsonSerializer.Deserialize<PlanningPdfReplacementSet>(bytes) ?? throw new InvalidDataException("Thiếu manifest PDF.");
        if(set.Files.Count is <1 or >33 || set.Files.GroupBy(x=>x.SourceEntry,StringComparer.Ordinal).Any(g=>g.Count()!=1))throw new InvalidDataException("Trùng hoặc sai số PDF thay thế.");
        var root=Path.GetDirectoryName(manifest)!;var paths=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var file in set.Files)
        {
            if(file.Stt is <1 or >32 || file.Pages is <1 or >EditorialDocumentPolicy.PdfMaximumPages || file.Bytes is <1 or >EditorialDocumentPolicy.PdfMaximum
                || file.RelativePath.Contains('\\') || Path.IsPathRooted(file.RelativePath) || file.RelativePath.Split('/').Any(x=>x is "" or "." or "..")
                || !file.RelativePath.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(file.Notice)
                || file.SourceHasSignatureObjects && !file.Notice.Contains("không còn chữ ký số mật mã",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Mapping PDF không hợp lệ hoặc thiếu cảnh báo chữ ký số.");
            var path=Path.GetFullPath(Path.Combine(root,file.RelativePath));
            var cursor=new FileInfo(path) as FileSystemInfo;
            while(cursor!=null && !string.Equals(cursor.FullName,Path.GetPathRoot(path),StringComparison.OrdinalIgnoreCase))
            { if((cursor.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Không đọc PDF qua liên kết thư mục/tệp.");cursor=cursor is FileInfo f?f.Directory:((DirectoryInfo)cursor).Parent; }
            if(new FileInfo(path).Length!=file.Bytes || PlanningPackage.FileHash(path)!=file.Sha256)throw new InvalidDataException("Bản PDF xử lý đổi byte/hash: STT "+file.Stt);
            paths.Add(file.SourceEntry,path);
        }
        return new(){ManifestSha256=WeeklyPackage.Hash(bytes),Set=set,Paths=paths};
    }
    public PlanningPdfReplacement? For(string sourceEntry)=>Set.Files.SingleOrDefault(x=>x.SourceEntry==sourceEntry);
    public byte[] ReadBytes(string sourceEntry)
    {
        var item=For(sourceEntry) ?? throw new InvalidDataException("Thiếu mapping PDF.");
        using var stream=File.OpenRead(Paths[sourceEntry]);
        if(stream.Length!=item.Bytes || stream.Length>EditorialDocumentPolicy.PdfMaximum)throw new InvalidDataException("PDF xử lý đổi dung lượng.");
        var bytes=new byte[checked((int)stream.Length)];stream.ReadExactly(bytes);
        if(WeeklyPackage.Hash(bytes)!=item.Sha256)throw new InvalidDataException("PDF xử lý đổi hash.");
        return bytes;
    }
}
