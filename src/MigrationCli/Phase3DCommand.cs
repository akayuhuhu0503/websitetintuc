using System.Text;
using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.MigrationCli;

public static class Phase3DCommand
{
    public static async Task<int> OfflineAsync(string[] args, string workspace)
    {
        string Opt(string key) { var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:throw new InvalidDataException(key+" required"); }
        var folder=Path.GetFullPath(Opt("--report"),workspace);
        if(!folder.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/evidence/phase3d"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(folder))throw new InvalidDataException("New private Phase3D evidence directory required.");
        SourceInventory.NoReparse(folder);Directory.CreateDirectory(folder);
        if(args[0]=="source-audit")
        {
            var state=await SourceBatch.ReadCheckpointAsync(workspace,Opt("--checkpoint"),Opt("--checkpoint-sha"));
            var report=await CoverageAudit.InspectAsync(workspace,state);
            await MediaRecovery.WriteNewAsync(Path.Combine(folder,"coverage.json"),report);
            static string Cell(string x)=>"\""+("=+-@\t\r".Contains(x.FirstOrDefault())?"'":"")+x.Replace("\"","\"\"")+"\"";
            var csv=new[]{"SourceIdentity,Status,Reason,Categories,URLs,RawHashes,Errors"}.Concat(report.Candidates.Select(x=>string.Join(",",new[]{x.SourceIdentity,x.Status,x.Reason,string.Join(";",x.Categories),string.Join(";",x.Urls),string.Join(";",x.CaptureHashes),string.Join(";",x.Errors)}.Select(Cell))));
            await File.WriteAllLinesAsync(Path.Combine(folder,"candidates.csv"),csv,new UTF8Encoding(true));
            await MediaRecovery.WriteNewAsync(Path.Combine(folder,"url-ledger.json"),state.Urls);
            Console.WriteLine($"Offline audit: {report.Urls} URLs / {report.CandidateIdentities} candidate keys / {report.VerifiedItems} staged items / {report.NewVerified} newly parsed. No network or SQL writes.");
        }
        else if(args[0]=="nukeviet-preview")
        {
            var file=Path.GetFullPath(Opt("--html"),workspace);
            if(!file.StartsWith(Path.Combine(workspace,".local")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Project-private HTML snapshot only.");
            SourceInventory.NoReparse(file);if(new FileInfo(file).Length>8*1024*1024)throw new InvalidDataException("HTML exceeds bounded parser limit.");
            var bytes=await File.ReadAllBytesAsync(file);if(SourceInventory.Hash(bytes)!=Opt("--sha"))throw new InvalidDataException("Raw snapshot SHA differs.");
            var preview=MigrationSourceIdentityPolicy.ParseNukeViet(new UTF8Encoding(false,true).GetString(bytes),Opt("--url"),Opt("--sha"));
            await MediaRecovery.WriteNewAsync(Path.Combine(folder,"nukeviet.json"),preview);
            var state=await SourceBatch.ReadCheckpointAsync(workspace,Opt("--checkpoint"),Opt("--checkpoint-sha"));
            await MediaRecovery.WriteNewAsync(Path.Combine(folder,"duplicate-review.json"),state.Items.Select(x=>new{preview.Identity,ComparedWith="qldd.mae.gov.vn/"+x.Source.Kind+"/"+x.Source.SourceId,Evidence=MigrationSourceIdentityPolicy.DuplicateEvidence(preview,x.Source)}).Where(x=>x.Evidence!="NO_MATCH").ToArray());
        }
        else throw new InvalidDataException("Unknown offline source command.");
        return 0;
    }

    // Applies the explicit name decision only on Test. Does not publish content or menu configuration.
    public static async Task ApplyNameDecisionTestAsync(PortalDbContext db,IConfiguration config,string workspace,string decisionPath,string sha,string backup)
    {
        if(config["DOTNET_ENVIRONMENT"]!="Development"||config["Sql:ExpectedDatabase"]!="LandPortal_Test")throw new UnauthorizedAccessException("Name decision application is restricted to SQL Test; Dev requires a separately approved data change.");
        var file=Path.GetFullPath(decisionPath,workspace);if(!file.StartsWith(Path.GetFullPath(Path.Combine(workspace,".local/evidence/phase3d"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Phase3D decision reference required.");
        SourceInventory.NoReparse(file);var bytes=await File.ReadAllBytesAsync(file);
        if(SourceInventory.Hash(bytes)!=sha||!Encoding.UTF8.GetString(bytes).Contains("Tên chuyên mục thứ hai đã chốt: \"Người nước ngoài định cư ở Việt Nam\"",StringComparison.Ordinal))throw new InvalidDataException("Exact user decision bytes required; no inferred PO decision.");
        await ApprovedBatchExecution.VerifyBackupAsync(db,workspace,"LandPortal_Test",backup);
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var group=await db.DisclosureGroups.SingleAsync(x=>x.Id=="nguoi-nuoc-ngoai-dinh-cu-o-viet-nam");
        if(group.Name!="Người nước ngoài định cư ở Việt Nam")throw new InvalidDataException("Stored name differs; manual review required.");
        if(group.Status=="NameApproved"){await tx.CommitAsync();return;}
        if(group.Status!="NeedsNameApproval")throw new InvalidDataException("Unexpected name approval state.");
        group.Status="NameApproved";
        db.AuditLogs.Add(new(){Actor="phase3d-cli-test",Action="Phase3D.FR17NameDecision",Detail="User Phase3D decision SHA256="+sha+"; Test only; no content/menu auto-publication."});
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
