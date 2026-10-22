using System.Buffers;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;

public sealed class VideoUploadService(PortalDbContext db,StorageOptions storage,VideoOptions options,CurrentPermissionService permissions,TimeProvider? clock=null)
{
    private DateTimeOffset Now=>(clock??TimeProvider.System).GetUtcNow();
    public string ChunkPath(Guid session,int index)=>MediaService.ResolveMediaPath(storage,$"video-upload/{session:N}/{index:D6}.chunk");
    public async Task<ContentActor> AuthorizeAsync(string userId,Guid contentId,bool write,CancellationToken ct=default)
    {
        if(!options.Enabled)throw new MediaUploadException("Upload video chưa mở tại môi trường này; dùng cấu hình SQL Test/kho Test đã xác minh.",503);
        var actor=await permissions.GetAsync(userId);
        if(!actor.Has(Permissions.ManageVideos)||!actor.Has(Permissions.ManageMedia))throw new MediaUploadException("Thiếu quyền quản lý video và media hiện thời.",403);
        var content=await db.Contents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==contentId,ct);
        if(content==null||content.IsTrashed||!(write?await permissions.CanEditAsync(actor,content):await permissions.CanReadAsync(actor,content)))throw new MediaUploadException("Không tìm thấy nội dung trong phạm vi được giao.",404);
        if(write&&content.State is not(WorkflowState.Draft or WorkflowState.Rejected))throw new MediaUploadException("Bản nháp đã đóng; mở bản sửa mới trước khi tải hoặc gắn video.",409);
        return actor;
    }
    public async Task<VideoUploadSession> GetAsync(Guid id,string user,bool write=false,CancellationToken ct=default)
    {
        var s=await db.Set<VideoUploadSession>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.OwnerUserId==user,ct)??throw new MediaUploadException("Không tìm thấy phiên tải.",404);
        await AuthorizeAsync(user,s.ContentId,write,ct);return s;
    }
    private void Disk(long needed)
    {
        var drive=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(storage.MediaRoot))!);
        if(drive.AvailableFreeSpace<needed+options.MinimumFreeBytes)throw new MediaUploadException("Kho video không đủ dung lượng dự phòng.",507);
    }
    private async Task Lock(string resource,CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=3000; IF @r<0 THROW 51000,'Video busy; retry safely.',1;",ct);
    }
    public async Task<VideoUploadSession> CreateAsync(Guid contentId,string user,string name,string mime,long bytes,string chunkManifestHash,CancellationToken ct=default)
    {
        await AuthorizeAsync(user,contentId,true,ct);
        if(!IsHash(chunkManifestHash))throw new MediaUploadException("Cần SHA-256 của manifest chunk trước khi tạo phiên.");
        if(bytes<1||bytes>options.MaxFileBytes)throw new MediaUploadException("Dung lượng video vượt giới hạn cấu hình hoặc tệp rỗng.",413);
        if((bytes+options.ChunkBytes-1)/options.ChunkBytes>8192)throw new MediaUploadException("Quá nhiều chunk; tăng kích thước chunk trong cấu hình trước khi nhận tệp lớn.",413);
        if(string.IsNullOrWhiteSpace(name)||name.Length>160||name.Any(char.IsControl)||name.IndexOfAny(['/','\\',':'])>=0||name.Count(x=>x=='.')!=1)throw new MediaUploadException("Tên tệp video không hợp lệ.");
        var ext=Path.GetExtension(name).ToLowerInvariant();var expected=ext switch{".mp4"=>"video/mp4",".mov"=>"video/quicktime",".webm"=>"video/webm",_=>""};
        if(expected==""||mime!=expected)throw new MediaUploadException("Chỉ nhận MP4, MOV, WebM đúng MIME; định dạng thực còn phải qua scanner/probe.");
        Disk(checked(bytes*5));
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);await Lock("video:quota",ct);
        var active=db.Set<VideoUploadSession>().Where(x=>x.State==VideoUploadState.Uploading&&x.ExpiresUtc>Now);
        if(await active.CountAsync(x=>x.OwnerUserId==user,ct)>=options.MaxSessions)throw new MediaUploadException("Đã đạt số phiên tải đang hoạt động.",429);
        var reserved=await active.SumAsync(x=>(long?)x.ExpectedBytes,ct)??0;var originals=await db.Set<VideoFile>().SumAsync(x=>(long?)x.Original.ByteLength,ct)??0;
        var storedChunks=await db.Set<VideoChunk>().SumAsync(x=>(long?)x.Bytes,ct)??0;
        if(reserved*4+originals*4+storedChunks+VideoDiskBudget.StoredBytes(storage)+bytes*5>options.QuotaBytes)throw new MediaUploadException("Đã đạt quota video gồm bản gốc, output và vùng tạm; chunk đã hủy vẫn tính đến khi dọn TTL.",507);
        var session=new VideoUploadSession{ContentId=contentId,OwnerUserId=user,Extension=ext,Mime=mime,ExpectedBytes=bytes,ChunkBytes=options.ChunkBytes,ChunkCount=(int)((bytes+options.ChunkBytes-1)/options.ChunkBytes),CreatedUtc=Now,UpdatedUtc=Now,ExpiresUtc=Now.AddHours(options.SessionHours)};
        session.ChunkManifestSha256=chunkManifestHash;
        db.Add(session);db.AuditLogs.Add(new(){Actor=user,Action="video.upload.created",ContentId=contentId,Detail=JsonSerializer.Serialize(new{session.Id,bytes,session.ChunkBytes,chunkManifestHash})});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return session;
    }
    private async Task<VideoUploadSession> Locked(Guid id,string user,CancellationToken ct)
    {
        await Lock("video:session:"+id,ct);db.ChangeTracker.Clear();var s=await db.Set<VideoUploadSession>().SingleOrDefaultAsync(x=>x.Id==id&&x.OwnerUserId==user,ct)??throw new MediaUploadException("Không tìm thấy phiên tải.",404);
        await AuthorizeAsync(user,s.ContentId,true,ct);
        if(s.State!=VideoUploadState.Uploading||s.ExpiresUtc<=Now)throw new MediaUploadException("Phiên tải đã đóng, hủy hoặc hết hạn.",410);return s;
    }
    public async Task<bool> ChunkAsync(Guid id,string user,int index,string digest,Stream input,CancellationToken ct=default)
    {
        if(!IsHash(digest))throw new MediaUploadException("Chunk cần SHA-256 hợp lệ.");
        await GetAsync(id,user,true,ct);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(60));ct=timeout.Token;
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);var s=await Locked(id,user,ct);
        if(index<0||index>=s.ChunkCount)throw new MediaUploadException("Chỉ số chunk không hợp lệ.");
        var prior=await db.Set<VideoChunk>().SingleOrDefaultAsync(x=>x.SessionId==id&&x.Index==index,ct);
        var count=await db.Set<VideoChunk>().CountAsync(x=>x.SessionId==id,ct);
        if(prior==null&&index!=count)throw new MediaUploadException("Tải chunk theo thứ tự đã xác nhận; dùng trạng thái phiên để tiếp tục.",409);
        var expected=Math.Min(s.ChunkBytes,s.ExpectedBytes-(long)index*s.ChunkBytes);Disk(expected);
        var final=ChunkPath(id,index);Directory.CreateDirectory(Path.GetDirectoryName(final)!);var temporary=final+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            var actual=await CopyBoundedAsync(input,temporary,expected,ct);
            if(actual.Bytes!=expected||actual.Hash!=digest)throw new MediaUploadException("Byte/hash chunk không khớp; không xác nhận tải.",409);
            await AuthorizeAsync(user,s.ContentId,true,ct); // Rights may change while the request body streams.
            if(prior!=null)
            {
                if(prior.Sha256!=digest||prior.Bytes!=expected)throw new MediaUploadException("Chunk đã có byte khác; không ghi đè.",409);
                if(!File.Exists(final)||await HashFileAsync(final,ct)!=digest)throw new MediaUploadException("Chunk đã xác nhận bị mất hoặc đổi byte; cần đối soát kho.",409);
                await tx.CommitAsync(ct);return true;
            }
            // Reconcile a crash after atomic rename but before SQL commit. Never overwrite different bytes.
            if(File.Exists(final)){if(new FileInfo(final).Length!=expected||await HashFileAsync(final,ct)!=digest)throw new MediaUploadException("Chunk mồ côi có byte khác; cần đối soát, không ghi đè.",409);}
            else File.Move(temporary,final,false);
            db.Add(new VideoChunk{SessionId=id,Index=index,Bytes=expected,Sha256=digest});s.ReceivedBytes+=expected;s.UpdatedUtc=Now;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return false;
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public async Task<VideoUploadSession> CompleteAsync(Guid id,string user,CancellationToken ct=default)
    {
        var previous=await GetAsync(id,user,false,ct);if(previous.State==VideoUploadState.Completed)return previous;
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);await Lock("video:complete",ct);
        // Another complete may have committed while this request waited.
        var check=await db.Set<VideoUploadSession>().AsNoTracking().SingleAsync(x=>x.Id==id,ct);if(check.State==VideoUploadState.Completed){await tx.CommitAsync(ct);return check;}
        var s=await Locked(id,user,ct);var chunks=await db.Set<VideoChunk>().Where(x=>x.SessionId==id).OrderBy(x=>x.Index).ToListAsync(ct);
        if(chunks.Count!=s.ChunkCount||s.ReceivedBytes!=s.ExpectedBytes)throw new MediaUploadException("Chưa đủ chunk đã xác nhận.",409);
        if(TextNormalizer.Hash(string.Join("\n",chunks.Select(x=>$"{x.Index}:{x.Bytes}:{x.Sha256}")))!=s.ChunkManifestSha256)throw new MediaUploadException("Manifest toàn bộ chunk khác tệp đã chọn ban đầu; không complete.",409);
        Disk(s.ExpectedBytes);
        var key=$"video-original/{s.Id:N}{s.Extension}";var output=MediaService.ResolveMediaPath(storage,key);Directory.CreateDirectory(Path.GetDirectoryName(output)!);var temporary=output+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using var full=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long bytes=0;var buffer=ArrayPool<byte>.Shared.Rent(65536);
            try{await using var destination=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous);
                foreach(var part in chunks){await using var input=File.OpenRead(ChunkPath(id,part.Index));using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long size=0;int n;while((n=await input.ReadAsync(buffer,ct))>0){size+=n;bytes+=n;if(size>part.Bytes||bytes>s.ExpectedBytes)throw new MediaUploadException("Chunk vượt byte đã xác nhận.",409);hash.AppendData(buffer,0,n);full.AppendData(buffer,0,n);await destination.WriteAsync(buffer.AsMemory(0,n),ct);}if(size!=part.Bytes||Convert.ToHexStringLower(hash.GetHashAndReset())!=part.Sha256)throw new MediaUploadException("Chunk thay byte sau upload.",409);}}
            finally{ArrayPool<byte>.Shared.Return(buffer);}
            if(bytes!=s.ExpectedBytes)throw new MediaUploadException("Tổng byte không khớp.",409);
            var digest=Convert.ToHexStringLower(full.GetHashAndReset());await ValidateEnvelopeAsync(temporary,s.Extension,ct);await AuthorizeAsync(user,s.ContentId,true,ct);
            var duplicate=await db.MediaAssets.SingleOrDefaultAsync(x=>x.Sha256==digest,ct);
            if(duplicate!=null)
            {
                if(duplicate.Origin!="VideoUpload"||duplicate.UploadedByUserId!=user||!await db.Set<VideoFile>().AnyAsync(x=>x.MediaAssetId==duplicate.Id,ct))throw new MediaUploadException("Byte trùng thuộc tài nguyên khác hoặc phạm vi riêng tư khác.",409);
                if(!File.Exists(MediaService.ResolveMediaPath(storage,duplicate.StorageKey))||await HashFileAsync(MediaService.ResolveMediaPath(storage,duplicate.StorageKey),ct)!=digest)throw new MediaUploadException("Bản trùng không còn khớp byte.",409);
                s.MediaAssetId=duplicate.Id;
            }
            else
            {
                if(File.Exists(output)){if(new FileInfo(output).Length!=bytes||await HashFileAsync(output,ct)!=digest)throw new MediaUploadException("Bản gốc mồ côi khác byte; không ghi đè.",409);}else File.Move(temporary,output,false);
                var asset=new MediaAsset{StorageKey=key,Sha256=digest,ByteLength=bytes,MimeType=s.Mime,OriginalName="video"+s.Extension,Origin="VideoUpload",UploadedByUserId=user,ValidationStatus="VideoEnvelopeOnly",ScanStatus="NotScanned",IsQuarantined=true};db.MediaAssets.Add(asset);s.MediaAssetId=asset.Id;db.Add(new VideoFile{MediaAssetId=asset.Id,Original=asset,UploadSessionId=s.Id,AvailableUtc=Now,State=VideoState.Pending});
            }
            s.Sha256=digest;s.State=VideoUploadState.Completed;s.UpdatedUtc=Now;db.AuditLogs.Add(new(){Actor=user,Action="video.upload.completed",ContentId=s.ContentId,Detail=JsonSerializer.Serialize(new{s.Id,s.MediaAssetId,bytes,digest})});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return s;
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);} // Unknown commit keeps its private immutable original for reconciliation.
    }
    public async Task CancelAsync(Guid id,string user,CancellationToken ct=default)
    {
        await GetAsync(id,user,false,ct);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);await Lock("video:session:"+id,ct);db.ChangeTracker.Clear();var s=await db.Set<VideoUploadSession>().SingleAsync(x=>x.Id==id&&x.OwnerUserId==user,ct);await AuthorizeAsync(user,s.ContentId,true,ct);
        if(s.State==VideoUploadState.Completed)throw new MediaUploadException("Bản gốc đã vào hàng đợi; hủy upload không được xóa tài nguyên hoặc revision đang dùng.",409);
        if(s.State!=VideoUploadState.Cancelled){s.State=VideoUploadState.Cancelled;s.UpdatedUtc=Now;db.AuditLogs.Add(new(){Actor=user,Action="video.upload.cancelled",ContentId=s.ContentId,Detail=s.Id.ToString()});await db.SaveChangesAsync(ct);}await tx.CommitAsync(ct);
    }
    public async Task<int> SweepAsync(CancellationToken ct=default)
    {
        var ids=await db.Set<VideoUploadSession>().AsNoTracking().Where(x=>x.State==VideoUploadState.Uploading&&x.ExpiresUtc<Now||x.State==VideoUploadState.Cancelled&&x.UpdatedUtc<Now.AddHours(-1)||(x.State==VideoUploadState.Completed||x.State==VideoUploadState.Expired)&&x.UpdatedUtc<Now.AddHours(-24)).OrderBy(x=>x.UpdatedUtc).Select(x=>x.Id).Take(50).ToListAsync(ct);var removed=0;
        foreach(var id in ids){await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);await Lock("video:session:"+id,ct);db.ChangeTracker.Clear();var s=await db.Set<VideoUploadSession>().SingleAsync(x=>x.Id==id,ct);if(s.State==VideoUploadState.Uploading&&s.ExpiresUtc>=Now){await tx.CommitAsync(ct);continue;}if(s.State==VideoUploadState.Uploading)s.State=VideoUploadState.Expired;
            var directory=Path.GetDirectoryName(ChunkPath(id,0))!;if(Directory.Exists(directory))foreach(var file in Directory.EnumerateFiles(directory)){if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)continue;var name=Path.GetFileName(file);if(System.Text.RegularExpressions.Regex.IsMatch(name,@"^\d{6}\.chunk(\.[a-f0-9]{32}\.tmp)?$")){File.Delete(file);removed++;}}
            db.RemoveRange(await db.Set<VideoChunk>().Where(x=>x.SessionId==id).ToListAsync(ct));s.UpdatedUtc=Now;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
        // Only generated attempt directories, after24h and outside every current lease/reference.
        var outputRoot=Path.GetDirectoryName(MediaService.ResolveMediaPath(storage,"video-output/boundary"))!;
        if(Directory.Exists(outputRoot))foreach(var assetDirectory in Directory.EnumerateDirectories(outputRoot).Take(100))
        {
            if(!Guid.TryParseExact(Path.GetFileName(assetDirectory),"N",out var assetId)||(File.GetAttributes(assetDirectory)&FileAttributes.ReparsePoint)!=0)continue;
            var job=await db.Set<VideoFile>().AsNoTracking().SingleOrDefaultAsync(x=>x.MediaAssetId==assetId,ct);if(job!=null&&job.State is VideoState.Scanning or VideoState.Processing)continue;
            foreach(var attempt in Directory.EnumerateDirectories(assetDirectory).Take(20))
            {
                if(!Guid.TryParseExact(Path.GetFileName(attempt),"N",out _)||(File.GetAttributes(attempt)&FileAttributes.ReparsePoint)!=0)continue;
                var prefix=Path.GetRelativePath(storage.MediaRoot,attempt).Replace('\\','/')+"/";if(job?.Mp4Key?.StartsWith(prefix,StringComparison.Ordinal)==true||job?.PosterKey?.StartsWith(prefix,StringComparison.Ordinal)==true)continue;
                foreach(var name in new[]{"play.mp4","poster.jpg","result.dat"}){var candidate=Path.Combine(attempt,name);var key=Path.GetRelativePath(storage.MediaRoot,candidate).Replace('\\','/');var safe=MediaService.ResolveMediaPath(storage,key);if(File.Exists(safe)&&File.GetLastWriteTimeUtc(safe)<Now.AddHours(-24).UtcDateTime){File.Delete(safe);removed++;}}
            }
        }
        return removed;
    }
    public static bool IsHash(string? value)=>value?.Length==64&&value.All(x=>char.IsAsciiHexDigit(x)&&!char.IsUpper(x));
    public static async Task<string> HashFileAsync(string path,CancellationToken ct=default){await using var stream=File.OpenRead(path);return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,ct));}
    public static async Task<(long Bytes,string Hash)> CopyBoundedAsync(Stream input,string path,long maximum,CancellationToken ct)
    {
        var buffer=ArrayPool<byte>.Shared.Rent(65536);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long bytes=0;
        try{await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous);int n;while((n=await input.ReadAsync(buffer,ct))>0){bytes+=n;if(bytes>maximum)throw new MediaUploadException("Chunk vượt giới hạn request.",413);hash.AppendData(buffer,0,n);await output.WriteAsync(buffer.AsMemory(0,n),ct);}return(bytes,Convert.ToHexStringLower(hash.GetHashAndReset()));}finally{ArrayPool<byte>.Shared.Return(buffer);}
    }
    private static async Task ValidateEnvelopeAsync(string path,string extension,CancellationToken ct)
    {
        await using var f=File.OpenRead(path);var header=new byte[32];var n=await f.ReadAsync(header,ct);
        var valid=extension==".webm"?n>=4&&header.AsSpan(0,4).SequenceEqual(new byte[]{0x1a,0x45,0xdf,0xa3}):n>=12&&header.AsSpan(4,4).SequenceEqual("ftyp"u8);
        if(!valid)throw new MediaUploadException("Container không khớp định dạng khai báo. Chữ ký tệp chỉ là kiểm tra đầu vào, chưa phải probe codec.");
    }
}
