using System.Data;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public sealed class InquiryScanWorker(PortalDbContext db,InquiryAttachmentService files,IInquiryScanner scanner,TimeProvider? clock=null)
{
    private DateTimeOffset Now=>(clock??TimeProvider.System).GetUtcNow();
    public async Task<bool> RunOnceAsync(Guid? onlyId=null,CancellationToken ct=default)
    {
        InquiryAttachment? row;
        await using(var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct))
        {
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource='inquiry-scan-claim',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;IF @r<0 THROW 51000,'Scan lock unavailable',1;",ct);
            var expired=db.InquiryAttachments.Where(x=>x.ScanState==InquiryScanState.Scanning&&x.LeaseUntilUtc<=Now);if(onlyId!=null)expired=expired.Where(x=>x.Id==onlyId);
            await expired.ExecuteUpdateAsync(x=>x.SetProperty(f=>f.ScanState,InquiryScanState.ScanFailed).SetProperty(f=>f.ScanError,"ScanLeaseExpired"),ct);
            var rows=db.InquiryAttachments.Where(x=>(x.ScanState==InquiryScanState.NotScanned||x.ScanState==InquiryScanState.ScanFailed)&&x.ScanAttempts<3&&x.AvailableUtc<=Now&&(x.SubmissionId!=null||x.ExpiresUtc>Now));if(onlyId!=null)rows=rows.Where(x=>x.Id==onlyId);
            row=await rows.OrderBy(x=>x.AvailableUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(ct);if(row==null){await tx.CommitAsync(ct);return false;}
            row.ScanState=InquiryScanState.Scanning;row.ScanAttempts++;row.LeaseToken=Guid.NewGuid();row.LeaseUntilUtc=Now.AddMinutes(2);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }
        InquiryScanResult result;var token=row.LeaseToken;var claimedVersion=row.RowVersion.ToArray();string? hash=null;var watch=Stopwatch.StartNew();
        try
        {
            var path=files.PathFor(row);await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);hash=Convert.ToHexStringLower(await SHA256.HashDataAsync(file,ct));
            if(file.Length!=row.Bytes||hash!=row.Sha256)result=new(InquiryScanState.ScanFailed,"FileHashMismatch",null);
            else{file.Position=0;result=await scanner.ScanAsync(file,ct);file.Position=0;if(Convert.ToHexStringLower(await SHA256.HashDataAsync(file,ct))!=hash)result=new(InquiryScanState.ScanFailed,"FileChangedDuringScan",null);}
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException){result=new(InquiryScanState.ScanFailed,"FileUnavailableUnsafeOrCancelled",null);}
        if(result.State is not(InquiryScanState.Clean or InquiryScanState.Infected or InquiryScanState.ScanFailed)||result.State is InquiryScanState.Clean or InquiryScanState.Infected&&string.IsNullOrWhiteSpace(result.EngineVersion))result=new(InquiryScanState.ScanFailed,"InvalidScannerConclusion",null);
        var state=result.State;var code=result.Error;var engine=result.EngineVersion;var scanned=state is InquiryScanState.Clean or InquiryScanState.Infected?Now:(DateTimeOffset?)null;var scannedHash=scanned==null?null:hash;var available=Now.AddMinutes(Math.Pow(2,row.ScanAttempts));db.ChangeTracker.Clear();
        await using var completion=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,CancellationToken.None);
        var changed=await db.InquiryAttachments.Where(x=>x.Id==row.Id&&x.ScanState==InquiryScanState.Scanning&&x.LeaseToken==token&&x.RowVersion==claimedVersion&&x.Sha256==row.Sha256&&x.Bytes==row.Bytes).ExecuteUpdateAsync(x=>x.SetProperty(f=>f.ScanState,state).SetProperty(f=>f.ScanError,code).SetProperty(f=>f.EngineVersion,engine).SetProperty(f=>f.ScannedSha256,scannedHash).SetProperty(f=>f.ScannedUtc,scanned).SetProperty(f=>f.AvailableUtc,available).SetProperty(f=>f.LeaseUntilUtc,(DateTimeOffset?)null),CancellationToken.None);
        if(changed!=1)throw new DbUpdateConcurrencyException("Scan lease/version/hash lost; late result ignored.");
        db.AuditLogs.Add(new(){Actor="inquiry-scan-worker",Action="inquiry.scan.completed",Detail=JsonSerializer.Serialize(new{AssetId=row.Id,Lease=token,Attempt=row.ScanAttempts,Bytes=row.Bytes,Sha256=row.Sha256,ScannedSha256=scannedHash,State=state.ToString(),result.Provider,result.EngineVersion,result.SignatureVersion,result.SignatureUpdatedUtc,result.OutputSha256,Error=code,LatencyMs=Math.Round(watch.Elapsed.TotalMilliseconds),ScanUtc=scanned})});
        await db.SaveChangesAsync(CancellationToken.None);await completion.CommitAsync(CancellationToken.None);return true;
    }
}
