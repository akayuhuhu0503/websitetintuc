using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace LandPortal.MigrationCli;

// No CLI flag or locally generated JSON is sufficient to authorize Dev writes.
public sealed class ApprovedBatchExecution
{
    readonly string approvalPath,approvalHash,backupPath;
    ApprovedBatchExecution(string path,string hash,string backup){approvalPath=path;approvalHash=hash;backupPath=backup;}
    sealed record Approval(string ApprovalId,string PlanSha256,string TargetDatabase,string OperatorSid,DateTimeOffset ExpiresUtc,string[] Actions);
    public static async Task VerifyBackupAsync(PortalDbContext db,string workspace,string target,string receiptPath)
    {
        await SchemaManager.VerifyLocalProjectAsync(db,target);
        var connection=db.Database.GetDbConnection();await db.Database.OpenConnectionAsync();
        await using var permission=connection.CreateCommand();permission.CommandText="SELECT CASE WHEN IS_MEMBER('db_owner')=1 OR IS_SRVROLEMEMBER('sysadmin')=1 THEN 1 ELSE 0 END";
        if(Convert.ToInt32(await permission.ExecuteScalarAsync())!=1)throw new UnauthorizedAccessException("Existing local backup/apply operator permission required; no grant performed.");
        var receiptFull=Path.GetFullPath(receiptPath,workspace);SourceInventory.NoReparse(receiptFull);
        if(!receiptFull.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/evidence"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Backup receipt outside project evidence.");
        using var receipt=JsonDocument.Parse(await File.ReadAllBytesAsync(receiptFull));var r=receipt.RootElement;
        if(r.GetProperty("database").GetString()!=target||r.GetProperty("verifyOnly").GetString()!="PASS"||r.GetProperty("utc").GetDateTimeOffset()<DateTimeOffset.UtcNow.AddHours(-12))throw new InvalidDataException("Fresh exact-target verified backup required.");
        var folder=Path.GetFullPath(r.GetProperty("path").GetString()!);if(!folder.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/backups"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Backup escaped project.");
        var bak=Path.Combine(folder,target+".bak");SourceInventory.NoReparse(bak);await using var file=File.OpenRead(bak);if(Convert.ToHexStringLower(await SHA256.HashDataAsync(file))!=r.GetProperty("databaseBackupSha256").GetString())throw new InvalidDataException("Backup bytes differ.");
        await using var header=connection.CreateCommand();header.CommandText="RESTORE HEADERONLY FROM DISK=@path";var parameter=header.CreateParameter();parameter.ParameterName="@path";parameter.Value=bak;header.Parameters.Add(parameter);await using var reader=await header.ExecuteReaderAsync();if(!await reader.ReadAsync()||reader["DatabaseName"].ToString()!=target)throw new InvalidDataException("SQL backup header target differs.");
    }
    public static async Task<ApprovedBatchExecution> CreateAsync(PortalDbContext db,IConfiguration config,string workspace,SourceBatchPlan plan,string planSha,string action,string approvalPath,string backupPath)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows local signed approval required.");
        if(plan.Database!="LandPortal_Dev"||config["Sql:ExpectedDatabase"]!=plan.Database||config["DOTNET_ENVIRONMENT"]!="Development")throw new UnauthorizedAccessException("Explicit local Dev target required.");
        var full=Path.GetFullPath(approvalPath,workspace);SourceInventory.NoReparse(full);if(!full.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/approvals/phase3c"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("External approval must be under private phase3c approvals.");
        var trusted=(config["SafeImport:ApprovedSignerThumbprints"]??"").Split(',',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim().ToUpperInvariant()).ToHashSet();if(trusted.Count==0)throw new UnauthorizedAccessException("Approved organization signing certificate is not configured. Do not self-configure trust.");
        if(plan.SchemaVersion!=3 || plan.TargetMediaRootSha256!=SourceBatch.MediaRootIdentity(StorageOptions.FromConfiguration(config)))throw new UnauthorizedAccessException("Dev approval requires a fresh schema3 plan bound to the exact target media store.");
        // Keep the same signed bytes locked against write/delete/replacement throughout verification.
        await using var locked=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read);using var content=new MemoryStream();await locked.CopyToAsync(content);var bytes=content.ToArray();
        var line=Encoding.UTF8.GetString(bytes).Split('\n').SingleOrDefault(x=>x.StartsWith("# LANDPORTAL_PHASE3C_APPROVAL ",StringComparison.Ordinal))??throw new InvalidDataException("Signed approval metadata missing.");
        var approval=JsonSerializer.Deserialize<Approval>(line["# LANDPORTAL_PHASE3C_APPROVAL ".Length..].Trim())??throw new InvalidDataException("Invalid approval.");
        if(string.IsNullOrWhiteSpace(approval.ApprovalId)||approval.PlanSha256!=planSha||approval.TargetDatabase!=plan.Database||approval.OperatorSid!=WindowsIdentity.GetCurrent().User?.Value||approval.ExpiresUtc<=DateTimeOffset.UtcNow||approval.ExpiresUtc>DateTimeOffset.UtcNow.AddDays(7)||!approval.Actions.Contains(action))throw new UnauthorizedAccessException("Approval scope/identity/expiry does not authorize this exact operation.");
        await PowerShellSignatureBridge.VerifyAsync(full,Convert.ToHexStringLower(SHA256.HashData(bytes)),trusted);
        await VerifyBackupAsync(db,workspace,plan.Database,backupPath);return new(full,Convert.ToHexStringLower(SHA256.HashData(bytes)),backupPath);
    }
    public async Task DemandAsync(PortalDbContext db,IConfiguration config,string workspace,SourceBatchPlan plan,string sha,string action)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows local signed approval required.");
        if(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(approvalPath)))!=approvalHash)throw new UnauthorizedAccessException("Approval changed after validation.");
        _=await CreateAsync(db,config,workspace,plan,sha,action,approvalPath,backupPath);
        db.AuditLogs.Add(new(){Actor=WindowsIdentity.GetCurrent().User!.Value,Action="Phase3C.DevAuthorization",Detail=JsonSerializer.Serialize(new{plan.BatchId,PlanSha256=sha,ApprovalSha256=approvalHash,Action=action})});
    }
}
