using System.Data;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.UatData;
public sealed record TargetPlan(Guid PackageId,string ManifestSha256,string TargetServer,string TargetDatabase,string TargetMediaRootSha256,string Migration,string Selection,Guid[] Contents,Guid[] Media,int Aliases,string OperatorSid,string TargetSqlServerIdentity);
public sealed record ImportReceipt(Guid PackageId,string ManifestSha256,string PlanSha256,string State,Dictionary<Guid,string> RowVersions,Guid[] Media,string[] Aliases,Guid? SiteDraftId);
public sealed record TargetApproval(Guid PackageId,string ManifestSha256,string PlanSha256,string TargetServer,string TargetDatabase,string OperatorSid,DateTimeOffset ExpiresUtc,string[] Actions,string ApprovalReference,string TargetSqlServerIdentity);

public static class PackageImport
{
    static readonly JsonSerializerOptions Json=DataPackage.Json;
    static string? Arg(string[] args,string key){var n=Array.IndexOf(args,key);return n>=0&&n+1<args.Length?args[n+1]:null;}
    static Guid[] Dependencies(IEnumerable<ContentRow> rows)=>rows.SelectMany(x=>x.Assets.Select(a=>a.MediaAssetId).Concat(x.Revision.CoverAssetId is Guid cover?[cover]:Array.Empty<Guid>())).Distinct().Order().ToArray();
    static List<ContentRow> Select(DataBundle data,string[] args)=>args.Contains("--pending-only")?data.PendingReview.OrderBy(x=>x.Id).ToList():args.Contains("--reviewed-candidates")?data.Ready.Concat(data.PendingReview).OrderBy(x=>x.Id).ToList():data.Ready.OrderBy(x=>x.Id).ToList();
    static string MediaKey(Guid package,MediaRow media)=>"uat-data/"+package.ToString("N")+"/"+Path.GetFileName(media.File);
    static async Task<OwnerDbaAudit?> RequireApproval(string[] args,IConfiguration config,TargetPlan plan,string planSha,string action,PortalDbContext db,DataBundle data)
    {
        if(Arg(args,"--owner-dba-confirmation") is string ownerConfirmation)
        {
            if(Arg(args,"--approval")!=null)throw new UnauthorizedAccessException("Choose one explicit approval method; do not mix owner/DBA confirmation with Authenticode.");
            if(plan.PackageId==Media252Scope.PackageId)return await Media252OwnerDbaApproval.VerifyAsync(ownerConfirmation,Arg(args,"--target-media-receipt")??throw new UnauthorizedAccessException("Thiếu receipt scanner/storage IIS thật."),config["WorkspaceRoot"]!,plan,planSha,action,data,db,Arg(args,"--operator-credential")!=null);
            return await Direct33OwnerDbaApproval.VerifyAsync(ownerConfirmation,config["WorkspaceRoot"]!,plan,planSha,action,db,Arg(args,"--operator-credential")!=null);
        }
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Target Windows signer required.");
        var path=Path.GetFullPath(Arg(args,"--approval")??throw new UnauthorizedAccessException("Exact signed target approval required."));DataPackage.NoReparse(path);
        if(!path.StartsWith(Path.GetFullPath(config["WorkspaceRoot"]!)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Approval must stay in target private data home.");
        var trusted=(config["SafeImport:ApprovedSignerThumbprints"]??"").Split(',',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim().ToUpperInvariant()).ToHashSet();
        if(trusted.Count==0)throw new UnauthorizedAccessException("TTDL approved signer pin missing; do not copy a Development certificate/pin.");
        await using var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);using var bytes=new MemoryStream();await locked.CopyToAsync(bytes);var content=bytes.ToArray();
        var line=Encoding.UTF8.GetString(content).Split('\n').SingleOrDefault(x=>x.StartsWith("# LANDPORTAL_UAT_DATA_APPROVAL ",StringComparison.Ordinal))??throw new InvalidDataException("Signed metadata missing.");
        var approval=JsonSerializer.Deserialize<TargetApproval>(line["# LANDPORTAL_UAT_DATA_APPROVAL ".Length..].Trim(),Json)??throw new InvalidDataException("Approval missing.");
        if(approval.PackageId!=plan.PackageId||approval.ManifestSha256!=plan.ManifestSha256||approval.PlanSha256!=planSha||approval.TargetServer!=plan.TargetServer||approval.TargetSqlServerIdentity!=plan.TargetSqlServerIdentity||approval.TargetDatabase!=plan.TargetDatabase||approval.OperatorSid!=WindowsIdentity.GetCurrent().User?.Value||approval.ExpiresUtc<=DateTimeOffset.UtcNow||approval.ExpiresUtc>DateTimeOffset.UtcNow.AddDays(7)||approval.Actions?.Contains(action)!=true||string.IsNullOrWhiteSpace(approval.ApprovalReference))throw new UnauthorizedAccessException("Approval scope, target, identity, expiry or action differs.");
        await PowerShellSignatureBridge.VerifyAsync(path,DataPackage.Hash(content),trusted);
        return null;
    }
    static async Task<ImportReceipt?> Receipt(PortalDbContext db,Guid id)
    {
        var rows=await db.AuditLogs.AsNoTracking().Where(x=>x.Action=="UatDataReceipt"&&x.Actor==id.ToString()).OrderByDescending(x=>x.CreatedUtc).Take(2).ToListAsync();
        return rows.Count==0?null:JsonSerializer.Deserialize<ImportReceipt>(rows[0].Detail,Json)??throw new InvalidDataException("Receipt unreadable.");
    }
    static object Summary(DataBundle data,List<ContentRow> rows)=>new{contents=rows.Count,revisions=rows.Count,media=Dependencies(rows).Length,aliases=rows.Sum(x=>x.Aliases.Count),categories=data.Categories.Count};
    // UAT backups must be validated against the allocated remote target, never
    // through ApprovedBatchExecution's intentionally local Dev/Test-only guard.
    static async Task VerifyUatBackup(PortalDbContext db,IConfiguration config,string receiptPath,DateTimeOffset? preserveAfterUtc=null)
    {
        await UatPreflightCommand.VerifySqlTargetAsync(db,config);
        var home=Path.GetFullPath(config["WorkspaceRoot"]!);
        var root=Path.Combine(home,".local","backups")+Path.DirectorySeparatorChar;
        var full=Path.GetFullPath(receiptPath);DataPackage.NoReparse(full);
        if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("UAT backup receipt must stay in private backup storage.");
        using var document=JsonDocument.Parse(await File.ReadAllBytesAsync(full));var r=document.RootElement;
        if(r.GetProperty("database").GetString()!=config["Sql:ExpectedDatabase"]||r.GetProperty("serverIdentity").GetString()!=UatDeployment.PhysicalServer(config)||r.GetProperty("verifyOnly").GetString()!="PASS"||r.GetProperty("utc").GetDateTimeOffset()<DateTimeOffset.UtcNow.AddHours(-12)||r.GetProperty("utc").GetDateTimeOffset()>DateTimeOffset.UtcNow.AddMinutes(5))throw new InvalidDataException("Fresh exact-target verified UAT backup required.");
        var folder=Path.GetFullPath(r.GetProperty("path").GetString()!);DataPackage.NoReparse(folder);
        if(!folder.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("UAT backup path escaped private storage.");
        var file=Path.Combine(folder,config["Sql:ExpectedDatabase"]+".bak");DataPackage.NoReparse(file);
        if(await DataPackage.FileHash(file)!=r.GetProperty("databaseBackupSha256").GetString())throw new InvalidDataException("Backup bytes differ.");
        var remote=r.TryGetProperty("serverBackupFile",out var remoteFile);
        var sqlFile=remote?remoteFile.GetString()!:file;
        if(!Path.IsPathFullyQualified(sqlFile)||!sqlFile.EndsWith(".bak",StringComparison.OrdinalIgnoreCase)||sqlFile.Any(char.IsControl))throw new InvalidDataException("SQL backup file path invalid.");
        if(remote&&(!r.TryGetProperty("backupSetGuid",out var set)||!Guid.TryParse(set.GetString(),out _)||r.GetProperty("serverFileSha256").GetString()!=r.GetProperty("databaseBackupSha256").GetString()))throw new InvalidDataException("Remote backup GUID/SHA binding missing.");
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command=db.Database.GetDbConnection().CreateCommand();command.CommandTimeout=120;
            var parameter=command.CreateParameter();parameter.ParameterName="@file";parameter.Value=sqlFile;command.Parameters.Add(parameter);
            if(remote)
            {
                command.CommandText="SELECT LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',BulkColumn),2)) FROM OPENROWSET(BULK '"+sqlFile.Replace("'","''")+"',SINGLE_BLOB) AS b;";
                if(Convert.ToString(await command.ExecuteScalarAsync())!=r.GetProperty("databaseBackupSha256").GetString())throw new InvalidDataException("SQL server backup bytes changed after receipt.");
            }
            command.CommandText="SELECT SYSDATETIME()";var now=Convert.ToDateTime(await command.ExecuteScalarAsync());
            command.CommandText="RESTORE HEADERONLY FROM DISK=@file";
            DateTime backupFinish=default;
            await using(var reader=await command.ExecuteReaderAsync())
            {
                if(!await reader.ReadAsync()||reader["DatabaseName"].ToString()!=config["Sql:ExpectedDatabase"]||reader["ServerName"].ToString()!=UatDeployment.PhysicalServer(config)||Convert.ToInt32(reader["BackupType"])!=1||!Convert.ToBoolean(reader["IsCopyOnly"])||!Convert.ToBoolean(reader["HasBackupChecksums"])||now-Convert.ToDateTime(reader["BackupFinishDate"])>TimeSpan.FromHours(12)||Convert.ToDateTime(reader["BackupFinishDate"])>now.AddMinutes(5))throw new InvalidDataException("UAT backup header, checksum or freshness differs.");
                if(remote&&!string.Equals(Convert.ToString(reader["BackupSetGUID"]),r.GetProperty("backupSetGuid").GetString(),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("SQL backup set changed after receipt.");
                backupFinish=Convert.ToDateTime(reader["BackupFinishDate"]);
                if(await reader.ReadAsync())throw new InvalidDataException("Single-set UAT backup required.");
            }
            if(preserveAfterUtc is DateTimeOffset preserve)
            {
                var finished=command.CreateParameter();finished.ParameterName="@finish";finished.Value=backupFinish;command.Parameters.Add(finished);
                command.CommandText="SELECT TODATETIMEOFFSET(@finish,DATEPART(TZOFFSET,SYSDATETIMEOFFSET()))";
                var finishedUtc=(DateTimeOffset)(await command.ExecuteScalarAsync())!;
                if(finishedUtc<preserve)throw new InvalidDataException("Backup có trước commit 33 bài cần bảo toàn; cần điểm khôi phục phù hợp trước batch252.");
            }
            command.CommandText="RESTORE VERIFYONLY FROM DISK=@file WITH CHECKSUM";await command.ExecuteNonQueryAsync();
        }
        finally{await db.Database.CloseConnectionAsync();}
    }
    static bool SiteReady(DataBundle data,ISet<Guid> selected,ISet<Guid> media)
    {
        if(data.PublishedSite is not JsonElement site)return false;
        var assets=new List<Guid>();if(site.TryGetProperty("LogoAssetId",out var logo)&&logo.ValueKind!=JsonValueKind.Null)assets.Add(logo.GetGuid());
        foreach(var x in site.GetProperty("Banners").EnumerateArray())assets.Add(x.GetProperty("MediaAssetId").GetGuid());
        var refs=site.GetProperty("Pins").EnumerateArray().Select(x=>x.GetProperty("ContentId").GetGuid()).ToList();
        foreach(var x in site.GetProperty("Menus").EnumerateArray().Concat(site.GetProperty("Banners").EnumerateArray()))if(x.GetProperty("ContentId").ValueKind!=JsonValueKind.Null)refs.Add(x.GetProperty("ContentId").GetGuid());
        return assets.All(media.Contains)&&refs.All(selected.Contains);
    }
    public static async Task<int> RunAsync(string command,string root,string[] args,IConfiguration config)
    {
        var(data,sha)=await DataPackage.Load(root,Arg(args,"--package-media-root"));var rows=Select(data,args);var mids=Dependencies(rows);var media=data.Media.Where(x=>mids.Contains(x.Id)).OrderBy(x=>x.Id).ToArray();
        var contentOnly=args.Contains("--content-only");
        if(args.Contains("--pending-only")&&args.Contains("--reviewed-candidates"))throw new UnauthorizedAccessException("Chọn một phạm vi dữ liệu, không gộp selection.");
        TargetMediaReceipt? targetMedia=null;
        if(Arg(args,"--target-media-receipt") is string receiptFile)
        {
            Media252Scope.Require(data,sha);
            if(!args.Contains("--pending-only")||!contentOnly)throw new UnauthorizedAccessException("Receipt IIS chỉ dùng cho đúng delta252 PendingOnlyContentOnly.");
            var pair=await TargetMediaStage.Read(receiptFile,data,sha,false);targetMedia=pair.Receipt;
            if(Arg(args,"--target-media-receipt-sha")!=pair.Sha)throw new UnauthorizedAccessException("Hash receipt IIS chưa được xác nhận hoặc đã thay đổi.");
        }
        if(args.Contains("--pending-only")&&targetMedia==null&&command!="offline-plan")throw new UnauthorizedAccessException("Chưa có receipt quét/chuyển media tại IIS; không mở SQL.");
        if(data.PackageId==Media252Scope.PackageId&&targetMedia==null&&command!="offline-plan")throw new UnauthorizedAccessException("Gói delta252 chỉ dùng receipt IIS thật; không có đường quét local thay thế hoặc chạy SQL trước media.");
        if(targetMedia!=null&&(command is "import" or "rollback")&&Arg(args,"--owner-dba-confirmation")==null)throw new UnauthorizedAccessException("Phạm vi remote media252 cần xác nhận riêng chủ dự án + DBA ràng buộc đúng hash receipt IIS; approval thông thường không thay thế điều kiện này.");
        var siteReady=!contentOnly&&SiteReady(data,rows.Select(x=>x.Id).ToHashSet(),mids.ToHashSet());
        if(command=="offline-plan")
        {
            Console.WriteLine(JsonSerializer.Serialize(new{status="OFFLINE_DRY_RUN_PASS_NO_SQL",manifestSha256=sha,selection=args.Contains("--pending-only")?"PENDING_REQUIRES_REAL_TARGET_MEDIA_RECEIPT_AND_NEW_OWNER_DBA_CONFIRMATION":args.Contains("--reviewed-candidates")?"REQUIRES_SIGNED_REVIEW_AND_NATIVE_TARGET_SCAN":"READY_ONLY",delta=Summary(data,rows),siteDraftImport=siteReady?"TARGET_DRAFT_ONLY":"DEFERRED_DEPENDENCIES_NOT_RELEASED",sql="NOT RUN",writes=0}));return 0;
        }
        if(!UatDeployment.Validate(config,Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??"Production"))throw new UnauthorizedAccessException("Approved allocated Staging UAT required; never Dev/Test/LAN/Production.");
        var operatorFile=Arg(args,"--operator-credential");
        if(operatorFile!=null&&(!((rows.Count==33&&mids.Length==0&&contentOnly&&!args.Contains("--reviewed-candidates"))||(targetMedia!=null&&rows.Count==252&&mids.Length==703&&contentOnly))||command=="backup"))throw new UnauthorizedAccessException("Operator credential chỉ dành cho scope đã xác nhận; không tự mở rộng hoặc tạo backup mới qua lệnh này.");
        await using var session=UatOperatorSession.Create(config,operatorFile,data.PackageId,sha,Arg(args,"--plan-sha"),command is "backup" or "import" or "rollback");
        var db=session.Db;await UatPreflightCommand.VerifySqlTargetAsync(db,config);
        var storage=StorageOptions.FromConfiguration(config);var sid=OperatingSystem.IsWindows()?WindowsIdentity.GetCurrent().User?.Value??"":"";
        var selection=(args.Contains("--pending-only")?"PendingOnly":args.Contains("--reviewed-candidates")?"ReviewedCandidates":"Ready")+(contentOnly?"ContentOnly":"");
        var plan=new TargetPlan(data.PackageId,sha,config["Deployment:ExpectedSqlServer"]!,config["Sql:ExpectedDatabase"]!,targetMedia?.MediaRootSha256??DataPackage.Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(storage.MediaRoot).ToUpperInvariant())),data.SourceMigration,selection,rows.Select(x=>x.Id).ToArray(),mids,rows.Sum(x=>x.Aliases.Count),sid,UatDeployment.PhysicalServer(config));
        var planSha=DataPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(plan,Json));
        if(data.SourceMigration!=db.Database.GetMigrations().Last())throw new InvalidDataException("Source/target schema versions differ.");
        if(command=="verify-backup")
        {
            await VerifyUatBackup(db,config,Arg(args,"--backup-receipt")??throw new InvalidDataException("Existing target backup receipt required."));
            Console.WriteLine("VERIFIED_EXISTING_UAT_BACKUP: no new backup or data writes.");return 0;
        }
        if(command=="backup")
        {
            var folder=Path.Combine(config["WorkspaceRoot"]!,".local","backups","uat-data-"+DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ")+"-"+Guid.NewGuid().ToString("N")[..6]);Directory.CreateDirectory(folder);var bak=Path.Combine(folder,plan.TargetDatabase+".bak");
            await db.Database.OpenConnectionAsync();using var sql=db.Database.GetDbConnection().CreateCommand();sql.CommandTimeout=120;
            var p=sql.CreateParameter();p.ParameterName="@file";p.Value=bak;sql.Parameters.Add(p);
            sql.CommandText="BACKUP DATABASE ["+plan.TargetDatabase+"] TO DISK=@file WITH COPY_ONLY,CHECKSUM";await sql.ExecuteNonQueryAsync();sql.CommandText="RESTORE VERIFYONLY FROM DISK=@file WITH CHECKSUM";await sql.ExecuteNonQueryAsync();
            await DataPackage.Write(Path.Combine(folder,"receipt.json"),new{database=plan.TargetDatabase,serverIdentity=UatDeployment.PhysicalServer(config),utc=DateTimeOffset.UtcNow,verifyOnly="PASS",path=folder,databaseBackupSha256=await DataPackage.FileHash(bak),mediaProtection="Append-only no-overwrite import; back up existing private stores per TTDL runbook"});
            Console.WriteLine("Verified target SQL backup receipt: "+Path.Combine(folder,"receipt.json"));return 0;
        }
        var receipt=await Receipt(db,data.PackageId);
        if(command=="plan")
        {
            await Conflicts(db,data,rows,media,receipt!=null);
            if(siteReady&&!await db.Set<SiteConfiguration>().AnyAsync())throw new InvalidOperationException("Target site configuration is absent. Authorized operator must run site-initialize before planning/import; no automatic initialization.");
            var output=Path.GetFullPath(Arg(args,"--output")??throw new InvalidDataException("New --output directory required."));if(!output.StartsWith(Path.GetFullPath(config["WorkspaceRoot"]!)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(output))throw new InvalidDataException("New plan directory under target private data home required.");DataPackage.NoReparse(output);Directory.CreateDirectory(output);
            await DataPackage.Write(Path.Combine(output,"plan.json"),plan);
            var draft=new{data.PackageId,ManifestSha256=sha,PlanSha256=planSha,plan.TargetServer,plan.TargetDatabase,plan.TargetSqlServerIdentity,OperatorSid=sid,ExpiresUtc=(DateTimeOffset?)null,Actions=new[]{"Import"},ApprovalReference=(string?)null};
            await File.WriteAllTextAsync(Path.Combine(output,"approval-UNSIGNED.ps1"),"# Draft only; complete exact scope/expiry/reference then organization-sign. Do not execute.\n# LANDPORTAL_UAT_DATA_APPROVAL "+JsonSerializer.Serialize(draft)+"\n");
            Console.WriteLine(JsonSerializer.Serialize(new{status="TARGET_READ_ONLY_PLAN_NEEDS_APPROVAL",sha,planSha,delta=Summary(data,rows),siteDraftImport=siteReady,receipt=receipt?.State,sqlWrites=0}));return 0;
        }
        if(command is "verify" or "reconcile")
        {
            if(receipt==null){Console.WriteLine("ABSENT: no target receipt; no automatic retry.");return 2;}
            var errors=await Verify(db,storage,data,rows,receipt,sha,targetMedia);Console.WriteLine(JsonSerializer.Serialize(new{status=errors.Count==0?receipt.State:"MISMATCH",errors,delta=Summary(data,rows),mediaVerification=targetMedia==null?"LOCAL_BYTES":"SQL_AND_ACKNOWLEDGED_TARGET_RECEIPT; VERIFY_LIVE_HTTP_SEPARATELY",sqlWrites=0}));return errors.Count==0?0:2;
        }
        if(command is not("import" or "rollback"))throw new InvalidDataException("Unknown command.");
        if(Arg(args,"--manifest-sha")!=sha||Arg(args,"--plan-sha")!=planSha||Arg(args,"--confirm-target")!=plan.TargetDatabase)throw new UnauthorizedAccessException("Exact package, target and fresh plan hash confirmation required.");
        await RequireApproval(args,config,plan,planSha,command=="import"?"Import":"Rollback",db,data);
        if(receipt!=null&&command=="import")
        {
            var errors=await Verify(db,storage,data,rows,receipt,sha,targetMedia);if(errors.Count>0||receipt.State!="COMMITTED_VERIFIED")throw new InvalidOperationException("Existing batch differs/was rolled back; reconcile before any retry.");
            Console.WriteLine("ALREADY_COMMITTED_VERIFIED: 0 writes, 0 duplicates.");return 0;
        }
        DateTimeOffset? preserveAfterUtc=null;
        if(targetMedia!=null)
        {
            var original=await Receipt(db,Direct33OwnerDbaApproval.PackageId);
            if(original==null||original.State!="COMMITTED_VERIFIED"||original.ManifestSha256!=Direct33OwnerDbaApproval.Manifest||original.RowVersions.Count!=33||original.Aliases.Length!=66||original.Media.Length!=0||original.RowVersions.Keys.Intersect(plan.Contents).Any())throw new InvalidDataException("Receipt 33 bài gốc không đúng trạng thái/phạm vi; dừng trước batch252.");
            preserveAfterUtc=await db.AuditLogs.AsNoTracking().Where(a=>a.Action=="UatDataReceipt"&&a.Actor==Direct33OwnerDbaApproval.PackageId.ToString()).OrderByDescending(a=>a.CreatedUtc).Select(a=>(DateTimeOffset?)a.CreatedUtc).FirstAsync();
        }
        await VerifyUatBackup(db,config,Arg(args,"--backup-receipt")??throw new InvalidDataException("Fresh exact-target backup receipt required."),preserveAfterUtc);
        if(targetMedia!=null&&command=="import")TargetMediaStage.Validate(targetMedia,data,sha,true);
        if(command=="rollback")
        {
            if(receipt==null)throw new InvalidOperationException("No batch receipt to roll back.");
            var errors=await Verify(db,storage,data,rows,receipt,sha,targetMedia);if(errors.Count>0)throw new InvalidOperationException("Readback drift; rollback denied.");
            if(receipt.State=="ROLLED_BACK_UNPUBLISHED"){Console.WriteLine("ALREADY_ROLLED_BACK: 0 writes.");return 0;}
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);await Lock(db);
            var ids=plan.Contents;
            if(receipt.SiteDraftId is Guid draftId){var site=await db.Set<SiteConfiguration>().SingleAsync();if(site.LiveRevisionId==draftId)throw new InvalidOperationException("Imported site configuration was published; restore through the admin workflow before rollback.");if(site.DraftRevisionId==draftId){site.DraftRevisionId=site.LiveRevisionId;site.Generation++;}}
            if(await db.ContentDrafts.AnyAsync(x=>ids.Contains(x.ContentId))||await db.Revisions.CountAsync(x=>ids.Contains(x.ContentId))!=ids.Length)throw new InvalidOperationException("Editor data exists; rollback denied.");
            foreach(var row in await db.Contents.Where(x=>ids.Contains(x.Id)).ToListAsync()){if(Convert.ToBase64String(row.RowVersion)!=receipt.RowVersions[row.Id])throw new InvalidOperationException("Post-import edit/state change; rollback denied.");row.IsPublic=false;row.State=WorkflowState.Unpublished;}
            await db.SourceUrlAliases.Where(x=>ids.Contains(x.ContentId)&&receipt.Aliases.Contains(x.UrlHash)).ExecuteDeleteAsync();
            db.AuditLogs.Add(new(){Actor=data.PackageId.ToString(),Action="UatDataReceipt",Detail=JsonSerializer.Serialize(receipt with{State="ROLLED_BACK_UNPUBLISHED"},Json)});await db.SaveChangesAsync();await tx.CommitAsync();
            Console.WriteLine("ROLLED_BACK_UNPUBLISHED: exact batch hidden/aliases removed; immutable history/media retained, no user data deleted.");return 0;
        }
        await Conflicts(db,data,rows,media,false);
        var releases=new List<(MediaAsset Asset,InquiryScanResult Proof)>();
        IInquiryScanner? scanner=null;if(media.Length>0&&targetMedia==null){scanner=InquiryScannerFactory.Create(config,"Staging",48L*1024*1024);if(!(await scanner.HealthAsync()).Ready)throw new InvalidOperationException("Approved target scanner unhealthy; no SQL import.");}
        foreach(var m in media)
        {
            if(targetMedia!=null)
            {
                var evidence=targetMedia.Entries.Single(e=>e.MediaId==m.Id);
                releases.Add((TargetAsset(m,evidence,targetMedia,Arg(args,"--target-media-receipt-sha")!),evidence.Scan));continue;
            }
            await using var input=new FileStream(DataPackage.PathIn(root,m.File),FileMode.Open,FileAccess.Read,FileShare.Read);if(input.Length!=m.Bytes||Convert.ToHexStringLower(await SHA256.HashDataAsync(input))!=m.Sha256)throw new InvalidDataException("Media changed before native scan.");input.Position=0;
            // OutputSha256 hashes scanner stdout; file bytes are verified independently.
            var proof=await scanner!.ScanAsync(input);if(proof.State!=InquiryScanState.Clean||string.IsNullOrWhiteSpace(proof.EngineVersion))throw new InvalidOperationException("Native target scan not Clean for "+m.Id+"; no SQL import.");
            var key=MediaKey(data.PackageId,m);var destination=MediaService.ResolveMediaPath(storage,key);DataPackage.NoReparse(destination);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if(File.Exists(destination)){if(await DataPackage.FileHash(destination)!=m.Sha256)throw new InvalidDataException("Existing target bytes differ; no overwrite.");}
            else{input.Position=0;await using var target=new FileStream(destination,FileMode.CreateNew);await input.CopyToAsync(target);}
            if(await DataPackage.FileHash(destination)!=m.Sha256)throw new InvalidDataException("Copied target media hash mismatch.");
            releases.Add((new MediaAsset{Id=m.Id,Sha256=m.Sha256,ByteLength=m.Bytes,MimeType=m.MimeType,SourceUrl=m.SourceUrl,OriginalName=m.OriginalName,Width=m.Width,Height=m.Height,CreatedUtc=m.CreatedUtc,StorageKey=key,Origin="Source",ScanStatus="Clean",ValidationStatus="SourceValidated",IsQuarantined=false},proof));
        }
        var ownerDbaAudit=await RequireApproval(args,config,plan,planSha,"Import",db,data);
        await using(var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Lock(db);if(await Receipt(db,data.PackageId)!=null)throw new InvalidOperationException("Concurrent commit found; reconcile, do not reapply.");await Conflicts(db,data,rows,media,false);
            if(ownerDbaAudit!=null)db.AuditLogs.Add(new(){Action="UatDataOwnerDbaAuthorization",Actor=data.PackageId.ToString(),Detail=JsonSerializer.Serialize(new{data.PackageId,ManifestSha256=sha,PlanSha256=planSha,confirmation=ownerDbaAudit},Json)});
            if(targetMedia!=null)db.AuditLogs.Add(new(){Action="UatDataTargetMediaAttestation",Actor=data.PackageId.ToString(),Detail=JsonSerializer.Serialize(new{data.PackageId,ManifestSha256=sha,PlanSha256=planSha,ReceiptSha256=Arg(args,"--target-media-receipt-sha"),targetMedia.MachineName,targetMedia.OperatorSid,targetMedia.MediaRootSha256,targetMedia.EngineIdentity,targetMedia.CompletedUtc},Json)});
            foreach(var c in data.Categories)if(!await db.Categories.AnyAsync(x=>x.Id==c.Id))db.Categories.Add(new(){Id=c.Id,Name=c.Name,Slug=c.Slug,SourceUrl=c.SourceUrl,SortOrder=c.SortOrder,ParentId=c.ParentId});
            db.MediaAssets.AddRange(releases.Select(x=>x.Asset));await db.SaveChangesAsync();
            foreach(var release in releases.Where(x=>InternalMediaExceptionPolicy.Candidate(x.Asset)))db.AuditLogs.Add(new(){Action=InternalMediaExceptionPolicy.GrantAction,Actor=release.Asset.Id.ToString(),Detail=InternalMediaExceptionPolicy.Binding(release.Asset)});
            var imported=rows.Select(x=>new Content{Id=x.Id,Kind=x.Kind,CreatedUtc=x.CreatedUtc,PublishedAtUtc=x.PublishedAtUtc,SourcePublishedDate=x.SourcePublishedDate,OriginalDateText=x.OriginalDateText,DatePrecision=x.DatePrecision,CommentsEnabled=x.CommentsEnabled,PrimaryCategoryId=x.Revision.PrimaryCategoryId,IsPublic=true,State=WorkflowState.Published}).ToArray();db.Contents.AddRange(imported);await db.SaveChangesAsync();
            foreach(var row in rows)
            {
                var r=row.Revision;var revision=new ContentRevision{Id=r.Id,ContentId=row.Id,Title=r.Title,Summary=r.Summary,BodyHtml=r.BodyHtml,SearchText=r.SearchText,Author=r.Author,SourceCredit=r.SourceCredit,CreatedUtc=r.CreatedUtc,IsSourceRevision=true,ContentHash=r.ContentHash,PrimaryCategoryId=r.PrimaryCategoryId,CoverAssetId=r.CoverAssetId,Metadata=r.Metadata.Copy()};db.Revisions.Add(revision);db.Set<RevisionSearchProjection>().Add(SearchProjectionService.Create(revision));
            }
            await db.SaveChangesAsync();
            foreach(var row in rows)
            {
                var r=row.Revision;foreach(var id in row.CategoryIds){db.ContentCategories.Add(new(){ContentId=row.Id,CategoryId=id});db.RevisionCategories.Add(new(){RevisionId=r.Id,CategoryId=id});}foreach(var a in row.Assets)db.ContentAssets.Add(new(){RevisionId=r.Id,MediaAssetId=a.MediaAssetId,Usage=a.Usage,Caption=a.Caption,SortOrder=a.SortOrder});
                foreach(var s in row.Sources)db.SourceRecords.Add(new(){ContentId=row.Id,Source=s.Source,SourceId=s.SourceId,Kind=s.Kind,CanonicalUrl=s.CanonicalUrl,LastContentHash=s.LastContentHash,LastSeenUtc=DateTimeOffset.UtcNow});foreach(var url in row.Aliases)db.SourceUrlAliases.Add(new(){ContentId=row.Id,Url=url,UrlHash=TextNormalizer.Hash(url)});
                if(row.Legal is LegalRow l)db.LegalDocuments.Add(new(){ContentId=row.Id,Number=l.Number,DocumentType=l.DocumentType,IssuingBody=l.IssuingBody,Signer=l.Signer,Field=l.Field,IssuedDate=l.IssuedDate,EffectiveDate=l.EffectiveDate,EffectivenessStatus=l.EffectivenessStatus});if(row.Draft is DraftRow d)db.DraftDocuments.Add(new(){ContentId=row.Id,ConsultationStartUtc=d.Start,ConsultationEndUtc=d.End,ConsultationDateEvidence=d.DateEvidence});
                var content=imported.Single(x=>x.Id==row.Id);content.CurrentRevisionId=r.Id;content.PublishedRevisionId=r.Id;
            }
            Guid? siteDraft=null;
            if(siteReady&&data.PublishedSite is JsonElement site)
            {
                var revision=site.Deserialize<SiteConfigurationRevision>(Json)!;revision.EditedByUserId=sid;revision.CreatedUtc=DateTimeOffset.UtcNow;
                foreach(var x in revision.Categories)x.RevisionId=revision.Id;foreach(var x in revision.Menus)x.RevisionId=revision.Id;foreach(var x in revision.Banners)x.RevisionId=revision.Id;foreach(var x in revision.Blocks)x.RevisionId=revision.Id;foreach(var x in revision.Pins)x.RevisionId=revision.Id;foreach(var x in revision.Vocabularies)x.RevisionId=revision.Id;
                if(await db.Set<SiteConfigurationRevision>().AnyAsync(x=>x.Id==revision.Id))throw new InvalidOperationException("Site revision collision.");db.Add(revision);await db.SaveChangesAsync();var current=await db.Set<SiteConfiguration>().SingleAsync();if(current.DraftRevisionId!=current.LiveRevisionId)throw new InvalidOperationException("Existing operator site draft; preserve it.");current.DraftRevisionId=revision.Id;current.Generation++;siteDraft=revision.Id;
            }
            foreach(var release in releases)db.AuditLogs.Add(new(){Action="UatDataMediaRelease",Actor=release.Asset.Id.ToString(),Detail=JsonSerializer.Serialize(new{data.PackageId,ManifestSha256=sha,release.Asset.Sha256,scan=release.Proof,policy=config["Scanner:PolicyReference"],utc=DateTimeOffset.UtcNow})});
            await db.SaveChangesAsync();var saved=new ImportReceipt(data.PackageId,sha,planSha,"COMMITTED_VERIFIED",imported.ToDictionary(x=>x.Id,x=>Convert.ToBase64String(x.RowVersion)),mids,rows.SelectMany(x=>x.Aliases).Select(TextNormalizer.Hash).ToArray(),siteDraft);
            db.AuditLogs.Add(new(){Action="UatDataReceipt",Actor=data.PackageId.ToString(),Detail=JsonSerializer.Serialize(saved,Json)});await db.SaveChangesAsync();await tx.CommitAsync();
        }
        db.ChangeTracker.Clear();var completed=(await Receipt(db,data.PackageId))!;var mismatch=await Verify(db,storage,data,rows,completed,sha,targetMedia);if(mismatch.Count>0)throw new InvalidOperationException("Commit readback mismatch; reconcile before retry.");
        Console.WriteLine(JsonSerializer.Serialize(new{status="COMMITTED_VERIFIED",delta=Summary(data,rows),mismatches=0,siteConfiguration=siteReady?"IMPORTED_AS_DRAFT_REQUIRES_NORMAL_ADMIN_PUBLICATION":"DEFERRED_MISSING_DEPENDENCIES"}));return 0;
    }
    static async Task Lock(PortalDbContext db)=>await db.Database.ExecuteSqlRawAsync("DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource='LandPortal.UatDataImport',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;IF @r<0 THROW 51000,'UAT import busy; reconcile before retry',1;");
    static async Task Conflicts(PortalDbContext db,DataBundle data,List<ContentRow> rows,MediaRow[] media,bool receipted)
    {
        foreach(var c in data.Categories){var current=await db.Categories.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==c.Id);if(current!=null&&(current.Name!=c.Name||current.Slug!=c.Slug))throw new InvalidOperationException("Category mapping conflict: "+c.Id);}
        if(receipted)return;
        var ids=rows.Select(x=>x.Id).ToArray();var revisions=rows.Select(x=>x.Revision.Id).ToArray();var mediaIds=media.Select(x=>x.Id).ToArray();var hashes=media.Select(x=>x.Sha256).ToArray();var aliases=rows.SelectMany(x=>x.Aliases).Select(TextNormalizer.Hash).ToArray();
        if(await db.Contents.AnyAsync(x=>ids.Contains(x.Id))||await db.Revisions.AnyAsync(x=>revisions.Contains(x.Id))||await db.MediaAssets.AnyAsync(x=>mediaIds.Contains(x.Id)||hashes.Contains(x.Sha256))||await db.SourceUrlAliases.AnyAsync(x=>aliases.Contains(x.UrlHash)))throw new InvalidOperationException("Existing identity/hash/alias without matching receipt; no overwrite.");
        foreach(var s in rows.SelectMany(x=>x.Sources))if(await db.SourceRecords.AnyAsync(x=>x.Source==s.Source&&x.Kind==s.Kind&&x.SourceId==s.SourceId))throw new InvalidOperationException("Source identity already exists.");
    }
    static async Task<List<string>> Verify(PortalDbContext db,StorageOptions storage,DataBundle data,List<ContentRow> rows,ImportReceipt receipt,string sha,TargetMediaReceipt? targetMedia=null)
    {
        var errors=new List<string>();if(receipt.ManifestSha256!=sha||!receipt.RowVersions.Keys.Order().SequenceEqual(rows.Select(x=>x.Id).Order())){errors.Add("Receipt selection/hash mismatch");return errors;}
        foreach(var row in rows)
        {
            var c=await db.Contents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==row.Id);var r=await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==row.Revision.Id);var expected=row.Revision;
            if(c==null||r==null||c.PublishedRevisionId!=expected.Id||c.CurrentRevisionId!=expected.Id||c.SourcePublishedDate!=row.SourcePublishedDate||c.PublishedAtUtc!=row.PublishedAtUtc||c.Kind!=row.Kind||r.Title!=expected.Title||r.BodyHtml!=expected.BodyHtml||r.Author!=expected.Author||r.SourceCredit!=expected.SourceCredit||r.ContentHash!=expected.ContentHash||JsonSerializer.Serialize(r.Metadata,Json)!=JsonSerializer.Serialize(expected.Metadata,Json)||c.IsPublic!=(receipt.State=="COMMITTED_VERIFIED"))errors.Add("Content/revision mismatch "+row.Id);
            var actual=await db.SourceUrlAliases.AsNoTracking().Where(x=>x.ContentId==row.Id).Select(x=>x.Url).ToListAsync();if(receipt.State=="COMMITTED_VERIFIED"&&!actual.Order().SequenceEqual(row.Aliases.Order()))errors.Add("Alias mismatch "+row.Id);
            if(r!=null){var cats=await db.RevisionCategories.AsNoTracking().Where(x=>x.RevisionId==r.Id).Select(x=>x.CategoryId).ToListAsync();var links=await db.ContentAssets.AsNoTracking().Where(x=>x.RevisionId==r.Id).OrderBy(x=>x.SortOrder).ThenBy(x=>x.MediaAssetId).Select(x=>new AssetLink(x.MediaAssetId,x.Usage,x.Caption,x.SortOrder)).ToListAsync();if(!cats.Order().SequenceEqual(row.CategoryIds.Order())||JsonSerializer.Serialize(links,Json)!=JsonSerializer.Serialize(row.Assets.OrderBy(x=>x.SortOrder).ThenBy(x=>x.MediaAssetId),Json)||r.Summary!=expected.Summary||r.CoverAssetId!=expected.CoverAssetId||r.PrimaryCategoryId!=expected.PrimaryCategoryId)errors.Add("Public relation/metadata mismatch "+row.Id);}
            foreach(var s in row.Sources)if(!await db.SourceRecords.AnyAsync(x=>x.ContentId==row.Id&&x.Source==s.Source&&x.SourceId==s.SourceId&&x.Kind==s.Kind&&x.CanonicalUrl==s.CanonicalUrl))errors.Add("Source mismatch "+row.Id);
        }
        foreach(var m in data.Media.Where(x=>receipt.Media.Contains(x.Id))){var asset=await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==m.Id);if(asset==null||asset.Sha256!=m.Sha256||asset.ByteLength!=m.Bytes||(targetMedia?.IntegrityOnly==true?!InternalMediaExceptionPolicy.Candidate(asset)||!await InternalMediaExceptionPolicy.HasGrantAsync(db,asset):asset.ScanStatus!="Clean")||asset.IsQuarantined||!new MediaUploadOptions().CanServe(asset)||
            (targetMedia!=null?asset.StorageKey!=Media252Scope.Key(data.PackageId,m):!File.Exists(storage.ResolveMedia(asset.StorageKey))||await DataPackage.FileHash(storage.ResolveMedia(asset.StorageKey))!=m.Sha256))errors.Add("Media mismatch "+m.Id);}
        return errors;
    }
    public static MediaAsset TargetAsset(MediaRow m,TargetMediaEntry evidence,TargetMediaReceipt receipt,string receiptSha)=>new()
    {
        Id=m.Id,Sha256=m.Sha256,ByteLength=m.Bytes,MimeType=m.MimeType,SourceUrl=m.SourceUrl,OriginalName=m.OriginalName,Width=m.Width,Height=m.Height,CreatedUtc=m.CreatedUtc,
        StorageKey=evidence.StorageKey,Origin="Source",ScanStatus=receipt.IntegrityOnly?InternalMediaExceptionPolicy.Status:"Clean",
        ValidationStatus=receipt.IntegrityOnly?InternalMediaExceptionPolicy.Prefix+receiptSha:"SourceValidated",IsQuarantined=false
    };
}
