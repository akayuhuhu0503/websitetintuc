using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;

// Plain WebVTT only: reject active markup/settings rather than silently rewriting immutable bytes.
public sealed class VideoSubtitleService(PortalDbContext db,StorageOptions storage,VideoOptions options,CurrentPermissionService permissions)
{
    public const int MaximumBytes=1024*1024;
    public static double Validate(byte[] bytes)
    {
        if(bytes.Length==0||bytes.Length>MaximumBytes)throw new MediaUploadException("Phụ đề tối đa1 MiB.",413);
        string text;try{text=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF').Replace("\r\n","\n");}catch(DecoderFallbackException){throw new MediaUploadException("Phụ đề phải là UTF-8 hợp lệ.");}
        if(!text.StartsWith("WEBVTT\n\n",StringComparison.Ordinal)||text.Any(c=>char.IsControl(c)&&c is not('\n' or '\t'))||text.Contains('<')||text.Contains('>')&&!text.Contains("-->"))throw new MediaUploadException("Chỉ nhận WebVTT văn bản thuần, không HTML/STYLE/REGION.");
        var blocks=text[8..].Split("\n\n",StringSplitOptions.RemoveEmptyEntries);if(blocks.Length is <1 or >5000)throw new MediaUploadException("Số cue WebVTT không hợp lệ.");
        double last=0,previousStart=-1;
        foreach(var block in blocks)
        {
            var lines=block.TrimEnd('\n').Split('\n');if(lines.Length is <2 or >12||lines.Any(x=>x.Length>2000))throw new MediaUploadException("Cue WebVTT quá dài hoặc thiếu nội dung.");
            var match=Regex.Match(lines[0],@"^(\d{2}:\d{2}:\d{2}\.\d{3}) --> (\d{2}:\d{2}:\d{2}\.\d{3})$",RegexOptions.CultureInvariant);
            if(!match.Success)throw new MediaUploadException("Dùng timestamp HH:mm:ss.fff và cue văn bản thuần; không cue settings.");
            double Time(string value){var p=value.Split(':');var seconds=double.Parse(p[2],CultureInfo.InvariantCulture);var minutes=int.Parse(p[1],CultureInfo.InvariantCulture);if(minutes>=60||seconds>=60)throw new MediaUploadException("Timestamp WebVTT không hợp lệ.");return int.Parse(p[0],CultureInfo.InvariantCulture)*3600+minutes*60+seconds;}
            var start=Time(match.Groups[1].Value);var end=Time(match.Groups[2].Value);if(start<previousStart||end<=start||end>3600||lines.Skip(1).Any(x=>x.Contains("-->",StringComparison.Ordinal)||x.Contains('<')||x.Contains('>')))throw new MediaUploadException("Thời gian hoặc nội dung cue không hợp lệ.");previousStart=start;last=Math.Max(last,end);
        }
        return last;
    }
    public async Task<VideoSubtitle> UploadAsync(Guid videoId,string user,string name,Stream input,CancellationToken ct=default)
    {
        var video=await db.Set<VideoFile>().AsNoTracking().SingleOrDefaultAsync(x=>x.MediaAssetId==videoId)??throw new MediaUploadException("Không tìm thấy video.",404);
        var uploads=new VideoUploadService(db,storage,options,permissions);var session=await uploads.GetAsync(video.UploadSessionId,user,true,ct);
        if(Path.GetExtension(name).ToLowerInvariant()!=".vtt"||name.Length>160||name.IndexOfAny(['/','\\',':'])>=0)throw new MediaUploadException("Chỉ nhận tệp .vtt.");
        // Bounded 1MiB subtitle buffer; large video bodies use the separate streaming chunk pipeline.
        using var memory=new MemoryStream();var buffer=new byte[16384];int n;while((n=await input.ReadAsync(buffer,ct))>0){if(memory.Length+n>MaximumBytes)throw new MediaUploadException("Phụ đề tối đa1 MiB.",413);await memory.WriteAsync(buffer.AsMemory(0,n),ct);}var bytes=memory.ToArray();var end=Validate(bytes);var sha=Convert.ToHexStringLower(SHA256.HashData(bytes));
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'video:quota',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=3000; IF @r<0 THROW 51000,'Video busy.',1;",ct);
        await uploads.AuthorizeAsync(user,session.ContentId,true,ct);
        var existing=await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x=>x.Sha256==sha,ct);if(existing!=null){var own=await db.Set<VideoSubtitle>().SingleOrDefaultAsync(x=>x.MediaAssetId==existing.Id&&x.UploadSessionId==session.Id,ct);if(own!=null&&existing.UploadedByUserId==user){await tx.CommitAsync(ct);return own;}throw new MediaUploadException("Phụ đề trùng nội dung trong phạm vi khác; không tái sử dụng tệp private.",409);}
        if(await db.Set<VideoSubtitle>().CountAsync(x=>x.UploadSessionId==session.Id,ct)>=10)throw new MediaUploadException("Tối đa10 bản phụ đề cho một phiên video.",429);
        var subtitleBytes=await db.Set<VideoSubtitle>().SumAsync(x=>(long?)x.Original.ByteLength,ct)??0;var originalBytes=await db.Set<VideoFile>().SumAsync(x=>(long?)x.Original.ByteLength,ct)??0;var chunks=await db.Set<VideoChunk>().SumAsync(x=>(long?)x.Bytes,ct)??0;var reservations=await db.Set<VideoUploadSession>().Where(x=>x.State==VideoUploadState.Uploading&&x.ExpiresUtc>DateTimeOffset.UtcNow).SumAsync(x=>(long?)x.ExpectedBytes,ct)??0;
        if(subtitleBytes+originalBytes*4+chunks+reservations*4+VideoDiskBudget.StoredBytes(storage)+bytes.Length>options.QuotaBytes||new DriveInfo(Path.GetPathRoot(storage.MediaRoot)!).AvailableFreeSpace<options.MinimumFreeBytes+bytes.Length)throw new MediaUploadException("Không đủ quota hoặc dung lượng dự phòng.",507);
        var asset=new MediaAsset{Sha256=sha,ByteLength=bytes.Length,MimeType="text/vtt",OriginalName="subtitle.vtt",Origin="VideoSubtitle",UploadedByUserId=user,StorageKey="video-subtitle/"+Guid.NewGuid().ToString("N")+".vtt",ValidationStatus="WebVttValidated",ScanStatus="NotScanned",IsQuarantined=true};
        var path=MediaService.ResolveMediaPath(storage,asset.StorageKey);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){await output.WriteAsync(bytes,ct);await output.FlushAsync(ct);}
        var track=new VideoSubtitle{Original=asset,MediaAssetId=asset.Id,VideoMediaAssetId=videoId,UploadSessionId=session.Id,LastCueSeconds=end,AvailableUtc=DateTimeOffset.UtcNow};db.Add(track);db.AuditLogs.Add(new(){Actor=user,Action="video.subtitle.uploaded",ContentId=session.ContentId,Detail=JsonSerializer.Serialize(new{asset.Id,videoId,sha,asset.ByteLength,State="NotScanned"})});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return track;
    }
    public async Task<bool> ScanOnceAsync(IInquiryScanner scanner,Guid? parent=null,CancellationToken ct=default)
    {
        if(!options.Enabled)throw new InvalidOperationException("Subtitle worker only supports explicit SQL Test.");
        VideoSubtitle? claimed;
        await using(var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct))
        {
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'video:subtitle-worker',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=3000; IF @r<0 THROW 51000,'Subtitle worker busy.',1;",ct);
            var expired=db.Set<VideoSubtitle>().Where(x=>x.State==InquiryScanState.Scanning&&x.LeaseUntilUtc<DateTimeOffset.UtcNow);if(parent.HasValue)expired=expired.Where(x=>x.VideoMediaAssetId==parent);
            foreach(var row in await expired.ToListAsync(ct)){row.State=InquiryScanState.ScanFailed;row.LeaseToken=null;row.Error="SUBTITLE_LEASE_EXPIRED";}await db.SaveChangesAsync(ct);
            if(await db.Set<VideoSubtitle>().AnyAsync(x=>x.State==InquiryScanState.Scanning&&x.LeaseUntilUtc>=DateTimeOffset.UtcNow,ct)){await tx.CommitAsync(ct);return false;}
            var queue=db.Set<VideoSubtitle>().Where(x=>(x.State==InquiryScanState.NotScanned||x.State==InquiryScanState.ScanFailed)&&x.Attempts<3&&x.AvailableUtc<=DateTimeOffset.UtcNow);if(parent.HasValue)queue=queue.Where(x=>x.VideoMediaAssetId==parent);
            claimed=await queue.Include(x=>x.Original).OrderBy(x=>x.AvailableUtc).FirstOrDefaultAsync(ct);if(claimed==null){await tx.CommitAsync(ct);return false;}
            claimed.State=InquiryScanState.Scanning;claimed.LeaseToken=Guid.NewGuid();claimed.LeaseUntilUtc=DateTimeOffset.UtcNow.AddMinutes(5);claimed.Attempts++;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }
        var lease=claimed.LeaseToken;var version=claimed.RowVersion.ToArray();var asset=claimed.Original;InquiryScanResult? result=null;var error="SUBTITLE_SCAN_FAILED";
        var session=await db.Set<VideoUploadSession>().AsNoTracking().SingleAsync(x=>x.Id==claimed.UploadSessionId,ct);
        try
        {
            await new VideoUploadService(db,storage,options,permissions).AuthorizeAsync(session.OwnerUserId,session.ContentId,false,ct);
            await using var stream=new FileStream(MediaService.ResolveMediaPath(storage,asset.StorageKey),FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
            if(stream.Length!=asset.ByteLength||stream.Length>MaximumBytes||Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,ct))!=asset.Sha256)throw new InvalidOperationException();stream.Position=0;result=await scanner.ScanAsync(stream,ct);stream.Position=0;if(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,ct))!=asset.Sha256)throw new InvalidOperationException();
            await new VideoUploadService(db,storage,options,permissions).AuthorizeAsync(session.OwnerUserId,session.ContentId,false,ct);error=result.Error;
        }
        catch(Exception e)when(e is IOException or MediaUploadException or InvalidOperationException or OperationCanceledException or UnauthorizedAccessException){result=null;error="SUBTITLE_SCAN_POLICY_OR_BYTE_FAILURE";}
        db.ChangeTracker.Clear();await using(var tx=await db.Database.BeginTransactionAsync(CancellationToken.None))
        {
            var row=await db.Set<VideoSubtitle>().Include(x=>x.Original).SingleAsync(x=>x.MediaAssetId==asset.Id);
            if(row.LeaseToken!=lease||!row.RowVersion.SequenceEqual(version)||row.Original.Sha256!=asset.Sha256){await tx.RollbackAsync();return true;}
            row.State=result?.State is InquiryScanState.Clean or InquiryScanState.Infected&& !string.IsNullOrWhiteSpace(result.EngineVersion)?result.State:InquiryScanState.ScanFailed;row.Error=error.Length>200?error[..200]:error;row.LeaseToken=null;row.LeaseUntilUtc=null;row.AvailableUtc=DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2,row.Attempts));row.ScannerProvider=result?.Provider;row.ScannerVersion=result?.EngineVersion;row.SignatureVersion=result?.SignatureVersion;row.ScannedUtc=DateTimeOffset.UtcNow;row.ScannedSha256=row.State is InquiryScanState.Clean or InquiryScanState.Infected?asset.Sha256:null;row.Original.ScanStatus=row.State.ToString();row.Original.IsQuarantined=true;
            db.AuditLogs.Add(new(){Actor="video-worker",Action="video.subtitle.scanned",ContentId=session.ContentId,Detail=JsonSerializer.Serialize(new{asset.Id,asset.Sha256,State=row.State.ToString(),row.Error,row.ScannerVersion,row.SignatureVersion,row.Attempts,Utc=DateTimeOffset.UtcNow})});try{await db.SaveChangesAsync();await tx.CommitAsync();}catch(DbUpdateConcurrencyException){await tx.RollbackAsync();}
        }
        return true;
    }
    public static async Task<bool> ReadyAsync(PortalDbContext db,StorageOptions storage,Guid id,bool development,CancellationToken ct=default)
    {
        var track=await db.Set<VideoSubtitle>().AsNoTracking().Include(x=>x.Original).SingleOrDefaultAsync(x=>x.MediaAssetId==id,ct);if(track==null||track.State!=InquiryScanState.Clean||track.Original.ScanStatus!="Clean"||track.Original.Origin!="VideoSubtitle"||track.Original.ValidationStatus!="WebVttValidated"||track.ScannedSha256!=track.Original.Sha256||track.ScannedUtc==null||string.IsNullOrWhiteSpace(track.ScannerVersion)||track.ScannerProvider=="MicrosoftDefender"&&!development)return false;
        var parent=await db.Set<VideoFile>().AsNoTracking().Include(x=>x.Original).SingleOrDefaultAsync(x=>x.MediaAssetId==track.VideoMediaAssetId,ct);if(parent==null||track.LastCueSeconds>parent.DurationSeconds+0.5||!await VideoReadiness.ReadyAsync(db,storage,parent.Original,development,ct))return false;
        try{var path=MediaService.ResolveMediaPath(storage,track.Original.StorageKey);return File.Exists(path)&&new FileInfo(path).Length==track.Original.ByteLength&&await VideoUploadService.HashFileAsync(path,ct)==track.Original.Sha256;}catch(Exception e)when(e is IOException or UnauthorizedAccessException or MediaUploadException){return false;}
    }
}
