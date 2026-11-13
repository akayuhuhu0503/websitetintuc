using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public sealed class VideoWorker(PortalDbContext db,StorageOptions storage,VideoOptions options,IInquiryScanner scanner,VideoProcessor processor,CurrentPermissionService permissions,TimeProvider? clock=null)
{
    private DateTimeOffset Now=>(clock??TimeProvider.System).GetUtcNow();
    public async Task<bool> RunOnceAsync(Guid? only=null,CancellationToken ct=default)
    {
        if(!options.Enabled)throw new InvalidOperationException("Video worker requires an enabled, validated environment.");
        // A no-AV worker must not consume attempts or relabel legacy quarantined jobs
        // when no scanner is available. Only new explicitly enrolled uploads proceed.
        var onlyManaged=options.ApprovedManagedVideoWithoutAv && !(await scanner.HealthAsync(ct)).Ready;
        Guid id=Guid.Empty,lease=Guid.Empty;byte[] version=[];
        try
        {
        await using(var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct))
        {
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'video:worker',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=3000; IF @r<0 THROW 51000,'Video worker busy.',1;",ct);
            var expired=db.Set<VideoFile>().Where(x=>(x.State==VideoState.Scanning||x.State==VideoState.Processing)&&x.LeaseUntilUtc<Now);if(only.HasValue)expired=expired.Where(x=>x.MediaAssetId==only.Value);
            if(onlyManaged)expired=expired.Where(x=>x.Original.ValidationStatus==VideoOptions.NoAvPending);
            else if(!options.ApprovedManagedVideoWithoutAv)expired=expired.Where(x=>x.Original.ValidationStatus!=VideoOptions.NoAvPending);
            foreach(var old in await expired.ToListAsync(ct)){old.State=old.Attempts>=3?VideoState.Failed:VideoState.Pending;old.Error="VIDEO_LEASE_EXPIRED";old.LeaseToken=null;old.LeaseUntilUtc=null;}
            await db.SaveChangesAsync(ct);
            if(await db.Set<VideoFile>().AnyAsync(x=>(x.State==VideoState.Scanning||x.State==VideoState.Processing)&&x.LeaseUntilUtc>=Now,ct)){await tx.CommitAsync(ct);return false;}
            var queue=db.Set<VideoFile>().Where(x=>(x.State==VideoState.Pending||x.State==VideoState.ScanFailed||x.State==VideoState.Failed)&&x.Attempts<3&&x.AvailableUtc<=Now && (x.Original.ValidationStatus!=VideoOptions.NoAvPending || options.ApprovedManagedVideoWithoutAv));
            if(onlyManaged)queue=queue.Where(x=>x.Original.ValidationStatus==VideoOptions.NoAvPending);
            if(only.HasValue)queue=queue.Where(x=>x.MediaAssetId==only.Value);
            var job=await queue.Include(x=>x.Original).OrderBy(x=>x.AvailableUtc).ThenBy(x=>x.MediaAssetId).FirstOrDefaultAsync(ct);if(job==null){await tx.CommitAsync(ct);return false;}
            bool quotaClosed;try{var reserved=await db.Set<VideoFile>().Where(x=>x.State!=VideoState.Ready).SumAsync(x=>(long?)x.Original.ByteLength,ct)??0;quotaClosed=VideoDiskBudget.StoredBytes(storage)+reserved*4>options.QuotaBytes;}catch(MediaUploadException){quotaClosed=true;}
            if(quotaClosed||new DriveInfo(Path.GetPathRoot(Path.GetFullPath(storage.MediaRoot))!).AvailableFreeSpace<options.MinimumFreeBytes+job.Original.ByteLength*4)
            {job.State=VideoState.Blocked;job.Error="VIDEO_DISK_BUDGET_UNAVAILABLE";db.AuditLogs.Add(new(){Actor="video-worker",Action="video.processing.blocked",Detail=JsonSerializer.Serialize(new{job.MediaAssetId,job.Error,Utc=Now})});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return true;}
            job.State=VideoState.Scanning;job.Attempts++;job.LeaseToken=lease=Guid.NewGuid();job.LeaseUntilUtc=Now.AddSeconds(options.ProcessSeconds*2+180);id=job.MediaAssetId;await db.SaveChangesAsync(ct);version=job.RowVersion.ToArray();await tx.CommitAsync(ct);
        }
        return await ProcessClaimAsync(id,lease,version,ct);
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested)
        {
            // A graceful host recycle is not a failed upload. Only this exact lease
            // may be released; a killed process still recovers through lease expiry.
            using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                db.ChangeTracker.Clear();
                if(id!=Guid.Empty&&lease!=Guid.Empty)await db.Set<VideoFile>().Where(x=>x.MediaAssetId==id&&x.LeaseToken==lease&&(x.State==VideoState.Scanning||x.State==VideoState.Processing))
                    .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.State,VideoState.Pending)
                        .SetProperty(x=>x.Error,"VIDEO_HOST_STOPPED")
                        .SetProperty(x=>x.LeaseToken,(Guid?)null).SetProperty(x=>x.LeaseUntilUtc,(DateTimeOffset?)null)
                        .SetProperty(x=>x.AvailableUtc,Now).SetProperty(x=>x.Attempts,x=>x.Attempts>0?x.Attempts-1:0),cleanup.Token);
            }
            catch(Exception e)when(e is OperationCanceledException or Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
            { /* Bounded shutdown: durable lease remains recoverable if SQL is unavailable. */ }
            throw;
        }
    }
    private async Task<bool> ProcessClaimAsync(Guid id,Guid lease,byte[] version,CancellationToken ct)
    {
        var watch=Stopwatch.StartNew();InquiryScanResult? scan=null;VideoOutput? output=null;var state=VideoState.Failed;var error="VIDEO_UNKNOWN_FAILURE";
        var file=await db.Set<VideoFile>().Include(x=>x.Original).AsNoTracking().SingleAsync(x=>x.MediaAssetId==id,ct);var session=await db.Set<VideoUploadSession>().AsNoTracking().SingleAsync(x=>x.Id==file.UploadSessionId,ct);
        var noAv=options.ApprovedManagedVideoWithoutAv && file.Original.ValidationStatus==VideoOptions.NoAvPending && file.Original.ScanStatus=="NotScanned" && file.ScannedUtc==null && file.ScannerProvider==null;
        try
        {
            var uploads=new VideoUploadService(db,storage,options,permissions,clock);await uploads.AuthorizeAsync(session.OwnerUserId,session.ContentId,false,ct);
            var path=MediaService.ResolveMediaPath(storage,file.Original.StorageKey);
            await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
            if(stream.Length!=file.Original.ByteLength||Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,ct))!=file.Original.Sha256)throw new VideoProcessingException("VIDEO_ORIGINAL_HASH_MISMATCH");
            if(stream.Length>options.MaxFileBytes)throw new VideoProcessingException("VIDEO_SCANNER_FILE_LIMIT",true);
            stream.Position=0;if(!noAv)scan=await scanner.ScanAsync(stream,ct);stream.Position=0;
            if(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,ct))!=file.Original.Sha256)throw new VideoProcessingException("VIDEO_SCAN_BYTE_MISMATCH");
            if(scan?.State==InquiryScanState.Infected){state=VideoState.Infected;error=scan.Error;}
            else if(!noAv && (scan?.State!=InquiryScanState.Clean||string.IsNullOrWhiteSpace(scan.EngineVersion))){state=VideoState.ScanFailed;error=scan?.Error??"VIDEO_SCANNER_NOT_READY";}
            else
            {
                var changed=await db.Set<VideoFile>().Where(x=>x.MediaAssetId==id&&x.LeaseToken==lease&&x.RowVersion==version).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.State,VideoState.Processing),ct);
                if(changed!=1)return true;
                version=await db.Set<VideoFile>().AsNoTracking().Where(x=>x.MediaAssetId==id).Select(x=>x.RowVersion).SingleAsync(ct);
                output=await processor.ProcessAsync(id,path,ct,forceTranscode:noAv);await uploads.AuthorizeAsync(session.OwnerUserId,session.ContentId,false,ct);
                stream.Position=0;if(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,ct))!=file.Original.Sha256)throw new VideoProcessingException("VIDEO_PROCESS_BYTE_MISMATCH");state=VideoState.Ready;error="";
            }
        }
        catch(VideoProcessingException e){state=e.Blocked?VideoState.Blocked:VideoState.Failed;error=e.Message;}
        catch(MediaUploadException){state=VideoState.Failed;error="VIDEO_PERMISSION_OR_POLICY_REVOKED";}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or OperationCanceledException or JsonException or InvalidOperationException or FormatException or System.ComponentModel.Win32Exception){state=VideoState.Failed;error="VIDEO_"+e.GetType().Name;}
        ct.ThrowIfCancellationRequested();
        db.ChangeTracker.Clear();await using(var tx=await db.Database.BeginTransactionAsync(CancellationToken.None))
        {
            var current=await db.Set<VideoFile>().Include(x=>x.Original).SingleAsync(x=>x.MediaAssetId==id,CancellationToken.None);
            if(current.LeaseToken!=lease||!current.RowVersion.SequenceEqual(version)||current.Original.Sha256!=file.Original.Sha256||current.Original.ByteLength!=file.Original.ByteLength){await tx.RollbackAsync();return true;}
            current.State=state;current.Error=error.Length>200?error[..200]:error;current.LeaseToken=null;current.LeaseUntilUtc=null;current.AvailableUtc=Now.AddMinutes(Math.Pow(2,current.Attempts));
            if(scan!=null){current.ScannerProvider=scan.Provider;current.ScannerVersion=scan.EngineVersion;current.SignatureVersion=scan.SignatureVersion;current.SignatureUpdatedUtc=scan.SignatureUpdatedUtc;current.ScannedUtc=Now;current.ScannedSha256=scan.State is InquiryScanState.Clean or InquiryScanState.Infected?file.Original.Sha256:null;current.Original.ScanStatus=scan.State.ToString();}
            if(state==VideoState.Ready&&output!=null){current.Mp4Key=output.Mp4;current.Mp4Sha256=output.Mp4Hash;current.Mp4Bytes=output.Mp4Bytes;current.PosterKey=output.Poster;current.PosterSha256=output.PosterHash;current.PosterBytes=output.PosterBytes;current.VideoCodec=output.Probe.Codec;current.AudioCodec=output.Probe.Audio;current.Width=output.Probe.Width;current.Height=output.Probe.Height;current.DurationSeconds=output.Probe.Duration;current.ReadyUtc=Now;current.Original.ValidationStatus=noAv?VideoOptions.NoAvValidated:"VideoValidated";}
            current.Original.IsQuarantined=true; // Originals are never served by generic media routes, even after Clean.
            db.AuditLogs.Add(new(){Actor="video-worker",Action="video.processing.completed",ContentId=session.ContentId,Detail=JsonSerializer.Serialize(new{AssetId=id,Lease=lease,current.Attempts,Bytes=file.Original.ByteLength,file.Original.Sha256,State=state.ToString(),Policy=noAv?VideoOptions.NoAvValidated:"ScannerRequired",ScanStatus=noAv?"NotScanned":scan?.State.ToString(),current.Error,scan?.Provider,scan?.EngineVersion,scan?.SignatureVersion,scan?.SignatureUpdatedUtc,scan?.OutputSha256,Tools=processor.LastHealth,NativeCpuMs=output?.NativeCpuMilliseconds,Reused=output?.Reused,SourceProbe=output?.OriginalProbe,OutputProbe=output?.Probe,OutputHash=output?.Mp4Hash,PosterHash=output?.PosterHash,LatencyMs=watch.ElapsedMilliseconds,Utc=Now})});
            try{await db.SaveChangesAsync();await tx.CommitAsync();}catch(DbUpdateConcurrencyException){await tx.RollbackAsync();}
        }
        return true;
    }
}
