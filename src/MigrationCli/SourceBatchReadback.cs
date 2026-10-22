using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.MigrationCli;
public sealed partial class SourceBatch
{
    public async Task<object> ReadbackAsync(SourceBatchPlan plan,string hash)
    {
        await Guard(false);DemandStore(plan);if(plan.Database!=config["Sql:ExpectedDatabase"]||plan.InventoryId!=state.Id||plan.CheckpointSha256!=checkpointHash)throw new InvalidDataException("Readback target/session differs.");
        var prefix=plan.BatchId+"|"+hash+"|";
        var receipts=await db.AuditLogs.AsNoTracking().Where(x=>x.Action=="Phase3B.Import"&&x.Detail.StartsWith(prefix)).Select(x=>new{x.ContentId,x.RevisionId,x.Detail}).Take(plan.Rows.Count+1).ToListAsync();
        if(receipts.Count>plan.Rows.Count)throw new InvalidDataException("Excess readback receipts.");
        var ids=receipts.Select(x=>x.ContentId).Distinct().ToArray();var revisions=receipts.Select(x=>x.RevisionId).Distinct().ToArray();
        var sources=await db.SourceRecords.AsNoTracking().Where(x=>ids.Contains(x.ContentId)).OrderBy(x=>x.Kind).ThenBy(x=>x.SourceId).Select(x=>new{x.Source,x.Kind,x.SourceId,x.ContentId,x.CanonicalUrl,x.LastContentHash}).ToListAsync();
        var contents=await db.Contents.AsNoTracking().Where(x=>ids.Contains(x.Id)).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.IsPublic,x.State,x.CurrentRevisionId,x.PublishedRevisionId,x.PrimaryCategoryId,x.PublishedAtUtc,x.SourcePublishedDate,x.OriginalDateText,x.DatePrecision,x.HasEditorialChanges,x.EditGeneration}).ToListAsync();
        var version=await db.Revisions.AsNoTracking().Where(x=>revisions.Contains(x.Id)).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ContentId,x.Title,x.Summary,x.BodyHtml,x.Author,x.ContentHash,x.PrimaryCategoryId,x.Metadata}).ToListAsync();
        var links=await db.ContentAssets.AsNoTracking().Where(x=>revisions.Contains(x.RevisionId)).OrderBy(x=>x.RevisionId).ThenBy(x=>x.MediaAssetId).Select(x=>new{x.RevisionId,x.MediaAssetId,x.Usage,x.SortOrder}).ToListAsync();
        var mediaIds=links.Select(x=>x.MediaAssetId).Distinct().ToArray();
        var media=await db.MediaAssets.AsNoTracking().Where(x=>mediaIds.Contains(x.Id)).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Sha256,x.StorageKey,x.ByteLength,x.MimeType,x.ScanStatus,x.IsQuarantined}).ToListAsync();
        var aliases=await db.SourceUrlAliases.AsNoTracking().Where(x=>ids.Contains(x.ContentId)).OrderBy(x=>x.UrlHash).Select(x=>new{x.Url,x.UrlHash,x.ContentId}).ToListAsync();
        var files=new List<object>();foreach(var m in media){var path=MediaService.ResolveMediaPath(storage,m.StorageKey);SourceInventory.NoReparse(path);var actual=SourceInventory.Hash(await File.ReadAllBytesAsync(path));if(actual!=m.Sha256||new FileInfo(path).Length!=m.ByteLength)throw new InvalidDataException("Readback media bytes differ.");files.Add(new{m.Id,m.Sha256,m.ByteLength,actualSha256=actual});}
        var values=new{sources,contents,version,links,media,aliases};
        return new{plan.BatchId,PlanSha256=hash,Database=plan.Database,ReceiptCount=receipts.Count,SourceCount=sources.Count,RevisionCount=version.Count,MediaCount=media.Count,AliasCount=aliases.Count,DataSha256=SourceInventory.HashText(JsonSerializer.Serialize(values)),Rows=values,Files=files};
    }
}
