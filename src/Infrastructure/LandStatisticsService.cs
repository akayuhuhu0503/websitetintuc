using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Infrastructure;

public sealed class LandStatisticsService(PortalDbContext db,StorageOptions storage,MediaService media,CurrentPermissionService rights,IConfiguration config)
{
    public const long MaximumBytes=10*1024*1024;
    async Task<ContentActor> Require(string userId,string permission){var actor=await rights.GetAsync(userId);if(!actor.Has(permission))throw new UnauthorizedAccessException();return actor;}
    public async Task<(LandStatisticsVersion? Version,LandAreaPreview Preview)> PreviewAsync(Stream input,string filename,int year,string unit,string source,string actorId,string environment,CancellationToken ct)
    {
        await Require(actorId,Permissions.ManageSite);
        var lan=LanDemoDeployment.Enabled(config);
        if(lan)LanStatisticsSamples.Require(config,environment);else EditorialDocumentService.RequireTestInfrastructure(config,environment);
        if(Path.GetExtension(filename).ToLowerInvariant()!=".xlsx"||year<2025||year>DateTime.UtcNow.Year+1||unit is not("ha" or "km²")||string.IsNullOrWhiteSpace(source)||source.Length>1000)throw new InvalidDataException("Cần XLSX, năm phù hợp danh mục 34 tỉnh (từ 2025), đơn vị ha/km² và nguồn số liệu.");
        await using var anonymous=lan?new MemoryStream():null;
        var accepted=input;
        if(anonymous!=null)
        {
            // Hash in memory before writing: Windows real-time AV must never see arbitrary LAN/private bytes.
            var chunk=new byte[65536];int size;while((size=await input.ReadAsync(chunk,ct))>0){if(anonymous.Length+size>MaximumBytes)throw new InvalidDataException("XLSX vượt 10 MiB.");await anonymous.WriteAsync(chunk.AsMemory(0,size),ct);}
            if(!LanStatisticsSamples.Hashes().Contains(LanStatisticsSamples.Sha(anonymous.ToArray())))
            {db.AuditLogs.Add(new(){Actor=actorId,Action="land-statistics.sample.denied",Detail="UnapprovedSampleBytes;no disk/native scan/dataset write"});await db.SaveChangesAsync(ct);throw new InvalidDataException("LAN chỉ nhận đúng tệp SAMPLE/TEST tải từ trang quản trị; tệp khác chưa được phép quét.");}
            anonymous.Position=0;accepted=anonymous;
        }
        var id=Guid.NewGuid();var key="land-statistics/"+id.ToString("N")+".xlsx";var path=MediaService.ResolveMediaPath(storage,key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Private file: no MediaAsset/public route is created. Keep unsafe bytes quarantined for operator handling.
        await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
        {var buffer=new byte[65536];long length=0;int read;while((read=await accepted.ReadAsync(buffer,ct))>0){length+=read;if(length>MaximumBytes)throw new InvalidDataException("XLSX vượt 10 MiB.");await output.WriteAsync(buffer.AsMemory(0,read),ct);}if(length==0)throw new InvalidDataException("Tệp rỗng.");}
        if(lan&&!LanStatisticsSamples.Hashes().Contains(await Hash(path,ct)))
        {db.AuditLogs.Add(new(){Actor=actorId,Action="land-statistics.sample.denied",Detail="UnapprovedSampleBytes;no native scan/no dataset write"});await db.SaveChangesAsync(ct);throw new InvalidDataException("LAN chỉ nhận đúng tệp SAMPLE/TEST tải từ trang quản trị. Tệp khác chưa được phép gửi vào scanner.");}
        var proof=await ScanAsync(path,environment,ct);
        await using(var signature=File.OpenRead(path)){var magic=new byte[4];if(await signature.ReadAsync(magic,ct)!=4||!magic.SequenceEqual(new byte[]{80,75,3,4}))throw new InvalidDataException("XLSX không đúng chữ ký ZIP.");}
        OfficeDocumentPolicy.Validate(path,OfficeDocumentPolicy.Xlsx,media);
        var mode=config["LandStatistics:TotalMismatch"]??"Block";if(mode is not("Block" or "Warn"))throw new InvalidOperationException("LandStatistics:TotalMismatch chỉ Block hoặc Warn.");
        var preview=LandStatisticsWorkbook.Read(path,mode=="Block");if(!preview.Valid)return(null,preview);
        var label=lan?"SAMPLE/TEST LAN — "+source.Trim():source.Trim();if(label.Length>1000)throw new InvalidDataException("Nguồn số liệu quá dài.");
        var version=new LandStatisticsVersion{Id=id,Year=year,Unit=unit,Source=label,RowsJson=JsonSerializer.Serialize(preview.Rows),StorageKey=key,Sha256=await Hash(path,ct),ScanProofJson=JsonSerializer.Serialize(proof),OwnerId=actorId};
        await Require(actorId,Permissions.ManageSite);
        db.Add(version);db.AuditLogs.Add(new(){Actor=actorId,Action="land-statistics.preview",Detail=JsonSerializer.Serialize(new{id,year,version.Sha256,Warnings=preview.Issues.Count})});await db.SaveChangesAsync(ct);return(version,preview);
    }
    public async Task ConfirmAsync(Guid id,string actorId,bool confirmed,CancellationToken ct)
    {
        await Require(actorId,Permissions.ManageSite);if(!confirmed)throw new InvalidDataException("Cần xác nhận dữ liệu xem trước.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var changed=await db.Set<LandStatisticsVersion>().Where(x=>x.Id==id&&x.OwnerId==actorId&&x.State=="Preview").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.State,"Draft"),ct);
        if(changed!=1)throw new InvalidOperationException("Bản xem trước không còn hợp lệ hoặc không thuộc người nhập.");
        db.AuditLogs.Add(new(){Actor=actorId,Action="land-statistics.confirm",Detail=id.ToString()});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task PublishAsync(Guid id,Guid? expectedLive,bool replace,string actorId,string environment,CancellationToken ct)
    {
        await Require(actorId,Permissions.PublishSite);
        if(LanDemoDeployment.Enabled(config))throw new InvalidOperationException("LAN SAMPLE/TEST chỉ được lưu nháp, không xuất bản số liệu thử.");
        EditorialDocumentService.RequireTestInfrastructure(config,environment);
        var version=await db.Set<LandStatisticsVersion>().AsNoTracking().SingleAsync(x=>x.Id==id,ct);
        if(version.State!="Draft")throw new InvalidOperationException("Chỉ xuất bản phiên bản đã xác nhận.");
        var path=MediaService.ResolveMediaPath(storage,version.StorageKey);if(await Hash(path,ct)!=version.Sha256)throw new InvalidDataException("Hash tệp gốc thay đổi.");
        var proof=await ScanAsync(path,environment,ct); // Fresh real conclusion on the same bytes, no old Test proof promotion.
        OfficeDocumentPolicy.Validate(path,OfficeDocumentPolicy.Xlsx,media);
        var mode=config["LandStatistics:TotalMismatch"]??"Block";if(mode is not("Block" or "Warn"))throw new InvalidOperationException("Invalid total policy.");
        var reread=LandStatisticsWorkbook.Read(path,mode=="Block");
        if(!reread.Valid||JsonSerializer.Serialize(reread.Rows)!=version.RowsJson||await Hash(path,ct)!=version.Sha256)throw new InvalidDataException("Dữ liệu xem trước hoặc chính sách đã thay đổi.");
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await Require(actorId,Permissions.PublishSite);
        var live=await db.Set<LandStatisticsVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Year==version.Year&&x.IsCurrent,ct);
        if(live?.Id!=expectedLive||live!=null&&!replace)throw new InvalidOperationException("Số liệu công khai đã đổi hoặc chưa xác nhận thay thế kỳ hiện có.");
        if(live!=null)await db.Set<LandStatisticsVersion>().Where(x=>x.Id==live.Id&&x.IsCurrent).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsCurrent,false),ct);
        var changed=await db.Set<LandStatisticsVersion>().Where(x=>x.Id==id&&x.State=="Draft").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsCurrent,true).SetProperty(x=>x.State,"Published").SetProperty(x=>x.PublishedUtc,DateTimeOffset.UtcNow).SetProperty(x=>x.ScanProofJson,JsonSerializer.Serialize(proof)),ct);
        if(changed!=1)throw new InvalidOperationException("Phiên bản đã thay đổi; tải lại trước khi xuất bản.");
        db.AuditLogs.Add(new(){Actor=actorId,Action="land-statistics.publish",Detail=JsonSerializer.Serialize(new{id,version.Year,version.Sha256,Previous=live?.Id})});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    async Task<InquiryScanResult> ScanAsync(string path,string environment,CancellationToken ct)
    {
        var scanner=InquiryScannerFactory.Create(config,environment,MaximumBytes);if(!(await scanner.HealthAsync(ct)).Ready)throw new InvalidOperationException("Scanner chưa sẵn sàng; chưa nhập/công bố.");
        await using var stream=File.OpenRead(path);var result=await scanner.ScanAsync(stream,ct);if(result.State!=InquiryScanState.Clean||string.IsNullOrWhiteSpace(result.EngineVersion))throw new InvalidDataException("Scanner từ chối tệp: "+result.State);return result;
    }
    static async Task<string> Hash(string path,CancellationToken ct){await using var stream=File.OpenRead(path);return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,ct));}
}
