using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.MigrationCli;

public sealed partial class MediaRecovery
{
    public const string PublicBatchScope="Phase3H.VerifiedPublicNetworkBatch.v1";
    public static void RequirePublicClean(RecoveryInventory inventory,IReadOnlyList<RecoveryProof> proofs)
    {
        // A fresh non-Clean result must never fall back to historical Clean labels in the checkpoint.
        if(inventory.Scope==PublicBatchScope&&(proofs.Count!=inventory.Resources.Count||proofs.Any(x=>x.State!="Clean"||x.Result.Provider!="MicrosoftDefender"||x.Result.OutputSha256 is not{Length:64})))throw new InvalidDataException("Public batch requires complete native Clean proofs; hold unsafe or failed scans.");
    }
    public static void ValidateShape(RecoveryInventory inventory)
    {
        if(inventory.Policy!=Policy||inventory.Resources.Count>60||inventory.Resources.Select(x=>x.Sha256).Distinct().Count()!=inventory.Resources.Count)throw new InvalidDataException("Bounded unique resources/policy required.");
        if(inventory.Scope is null){if(inventory.Resources.Count<53||inventory.Resources.Count(x=>x.IsProtectedSource)!=53)throw new InvalidDataException("Legacy protected53 plus at most7 required.");}
        else if(inventory.Scope!=PublicBatchScope||inventory.Resources.Any(x=>x.IsProtectedSource)||inventory.ParentCheckpointSha256 is not{Length:64})throw new InvalidDataException("Reviewed public batch scope required; no original Dev promotion.");
    }

    public static void ValidatePublicProjection(InventoryState selected,InventoryState parent)
    {
        if(selected.Id!=parent.Id||selected.Items.Count is <1 or >20)throw new InvalidDataException("Projection requires same source session and1-20 whole contents.");
        var identities=selected.Items.Select(x=>(x.Source.Kind,x.Source.SourceId)).ToList();
        if(identities.Distinct().Count()!=identities.Count)throw new InvalidDataException("Duplicate projected identity.");
        foreach(var item in selected.Items){var original=parent.Items.SingleOrDefault(x=>x.Source.Kind==item.Source.Kind&&x.Source.SourceId==item.Source.SourceId);if(original is null||JsonSerializer.Serialize(item)!=JsonSerializer.Serialize(original))throw new InvalidDataException("Projected source changed or not in original checkpoint.");}
        var save=parent.Items;
        try{parent.Items=selected.Items;if(JsonSerializer.Serialize(parent)!=JsonSerializer.Serialize(selected))throw new InvalidDataException("Projection changed capture/provenance/source metadata.");}finally{parent.Items=save;}
    }

    public async Task<RecoveryInventory> PublicBatchInventoryAsync(InventoryState selected,string selectedSha,InventoryState parent,string parentSha,string sourceProofPath,string sourceProofSha)
    {
        await Guard();ValidatePublicProjection(selected,parent);
        using var proof=JsonDocument.Parse(await File.ReadAllBytesAsync(sourceProofPath));
        if(Hash(await File.ReadAllBytesAsync(sourceProofPath))!=sourceProofSha||proof.RootElement.GetProperty("database").GetString()!="LandPortal_Dev")throw new InvalidDataException("Exact readonly Dev source proof required.");
        var media=proof.RootElement.GetProperty("groups").GetProperty("media").EnumerateArray().ToList();
        if(media.Count(x=>x.GetProperty("ScanStatus").GetString()=="NotScanned")!=53)throw new InvalidDataException("Original53 preservation differs; stop for review.");
        var forbidden=media.Where(x=>x.GetProperty("ScanStatus").GetString()!="Clean"||x.GetProperty("IsQuarantined").GetBoolean()).Select(x=>x.GetProperty("Sha256").GetString()!).ToHashSet();
        var resources=new Dictionary<string,RecoveryResource>();
        foreach(var item in selected.Items)
        {
            var source=item.Source;
            var record=await db.SourceRecords.AsNoTracking().Include(x=>x.Content).SingleOrDefaultAsync(x=>x.Source==SourceBatch.Source&&x.Kind==source.Kind&&x.SourceId==source.SourceId);
            if(record?.Content.PublishedRevisionId is not Guid revision||!record.Content.IsPublic||record.Content.IsTrashed||record.Content.HasEditorialChanges||!await db.Revisions.AnyAsync(x=>x.Id==revision&&x.IsSourceRevision)||!await db.AuditLogs.AnyAsync(x=>x.Action=="Phase3B.Import"&&x.ContentId==record.ContentId&&x.RevisionId==revision))throw new InvalidDataException("Real source item must already have a published Test import receipt; fixtures not in parent cannot enter.");
            var raw=parent.Captures.LastOrDefault(x=>x.Sha256==item.RawSha256&&x.Status==200&&x.Origin=="Network");
            if(raw is null)throw new InvalidDataException("Public network detail provenance required.");
            foreach(var url in source.AssetUrls.Distinct())
            {
                var node=selected.Urls.SingleOrDefault(x=>x.Role=="File"&&x.Url==url);
                var capture=node is null?null:selected.Captures.LastOrDefault(x=>x.Url==url&&x.Sha256==node.CaptureSha&&x.Status==200&&x.Origin=="Network");
                if(node?.FormatStatus!="Validated"||node.Mime is not("image/jpeg" or "image/png" or "image/gif" or "image/webp" or "application/pdf")||capture is null||capture.Bytes<=0||capture.Bytes>(node.Mime=="application/pdf"?SourceResourcePolicy.MediaOptions(config,url).MaxPdfBytes:SourceResourcePolicy.StandardPdfBytes)||forbidden.Contains(capture.Sha256))throw new InvalidDataException("Invalid/missing/unreviewed or protected Dev resource; do not omit or promote.");
                var file=StorageOptions.ResolveWithin(Path.Combine(workspace,".local/phase3a",selected.Id.ToString("N")),capture.Key);SourceInventory.NoReparse(file);
                var bytes=await File.ReadAllBytesAsync(file);if(bytes.LongLength!=capture.Bytes||Hash(bytes)!=capture.Sha256)throw new InvalidDataException("Public resource bytes differ.");
                new MediaService(db,storage,SourceResourcePolicy.MediaOptions(config,url)).ValidateContent(file,node.Mime,CancellationToken.None);
                var existing=await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x=>x.Sha256==capture.Sha256);
                if(existing is null||existing.ByteLength!=capture.Bytes||existing.MimeType!=node.Mime||!await db.ContentAssets.AnyAsync(x=>x.RevisionId==revision&&x.MediaAssetId==existing.Id))throw new InvalidDataException("Resource not linked to exact imported Test revision.");
                var identity=source.Kind+":"+source.SourceId;
                if(resources.TryGetValue(capture.Sha256,out var found)){if(!found.Urls.Contains(url))found.Urls.Add(url);if(!found.Identities.Contains(identity))found.Identities.Add(identity);}
                else resources.Add(capture.Sha256,new(capture.Sha256,capture.Bytes,node.Mime,capture.Key,[url],[identity],false));
            }
        }
        var result=new RecoveryInventory{Scope=PublicBatchScope,ParentCheckpointSha256=parentSha,InventoryId=selected.Id,CheckpointSha256=selectedSha,SourceProofSha256=sourceProofSha,SourceResourcePolicySha256=SourceResourcePolicy.Read(config)?.Sha,Resources=resources.Values.OrderBy(x=>x.Sha256).Select(x=>x with{Urls=x.Urls.Order().ToList(),Identities=x.Identities.Order().ToList()}).ToList()};
        ValidateShape(result);return result;
    }
}
