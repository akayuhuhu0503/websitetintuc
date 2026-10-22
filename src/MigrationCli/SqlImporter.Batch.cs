using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LandPortal.MigrationCli;
public sealed partial class SqlImporter
{
    public static ContentMetadata BatchSourceMetadata(ParsedSource source)=>SourceMetadata(source);
    // Caller holds the serializable transaction and global import lock. Never used by legacy import.
    internal async Task<(Guid ContentId,Guid RevisionId)> ImportValidatedBatchItemAsync(SourceBatchPrepared p,List<string> aliases,SourceRecord? record,Guid batchId,string auditKey,string inventoryRoot,bool keep)
    {
        var s=p.Item.Source;var content=record?.Content;
        if(keep&&record is not null){
            var last=await db.Revisions.Where(x=>x.ContentId==record.ContentId&&x.IsSourceRevision).OrderByDescending(x=>x.CreatedUtc).ThenByDescending(x=>x.Id).FirstAsync();
            await AliasesAsync(record.ContentId,aliases,CancellationToken.None);
            db.AuditLogs.Add(new(){Actor="phase3b-cli",Action="Phase3B.Import",ContentId=record.ContentId,RevisionId=last.Id,Detail=auditKey});Warnings(record.ContentId,last.Id);return(record.ContentId,last.Id);
        }
        var existingRevision=record is null?null:await db.Revisions.Where(x=>x.ContentId==record.ContentId&&x.IsSourceRevision&&x.ContentHash==p.Fingerprint).FirstOrDefaultAsync();
        if(content is null){content=new(){Kind=s.Kind,State=WorkflowState.Draft,IsPublic=false,CommentsEnabled=false,PrimaryCategoryId=p.Primary,PublishedAtUtc=s.PublishedUtc,SourcePublishedDate=s.PublishedDate,OriginalDateText=s.OriginalDateText,DatePrecision=s.DatePrecision};db.Contents.Add(content);await db.SaveChangesAsync();}
        var resolved=new List<(Guid Id,SourceBatchAsset Asset)>();var body=p.Body;
        foreach(var a in p.Assets){
            var asset=await db.MediaAssets.SingleOrDefaultAsync(x=>x.Sha256==a.Capture.Sha256);
            if(asset is not null){
                if(asset.ScanStatus!="Clean"||asset.IsQuarantined)throw new InvalidDataException("Existing asset cannot be promoted using a staging proof.");
                if(SourceInventory.Hash(await File.ReadAllBytesAsync(MediaService.ResolveMediaPath(storage,asset.StorageKey)))!=a.Capture.Sha256)throw new InvalidDataException("Existing asset bytes changed.");
            }else{
                var name=Uri.UnescapeDataString(Path.GetFileName(new Uri(a.Node.Url).AbsolutePath));
                var key="phase3b/"+a.Capture.Sha256+(a.Node.Mime=="application/pdf"?".pdf":a.Node.Mime=="image/png"?".png":".jpg");
                var target=MediaService.ResolveMediaPath(storage,key);SourceInventory.NoReparse(target);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                // Materialization uses the exact private source capture; no source fetch/hotlink fallback.
                var source=StorageOptions.ResolveWithin(inventoryRoot,a.Capture.Key);
                SourceInventory.NoReparse(source);var bytes=await File.ReadAllBytesAsync(source);
                if(SourceInventory.Hash(bytes)!=a.Capture.Sha256||bytes.LongLength!=a.Capture.Bytes)throw new InvalidDataException("Private source bytes changed before materialize.");
                if(!File.Exists(target)){await using var f=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None);await f.WriteAsync(bytes);await f.FlushAsync();f.Flush(true);}
                if(SourceInventory.Hash(await File.ReadAllBytesAsync(target))!=a.Capture.Sha256)throw new InvalidDataException("Materialized bytes differ.");
                asset=new(){Id=a.Id,Sha256=a.Capture.Sha256,StorageKey=key,ByteLength=a.Capture.Bytes,MimeType=a.Node.Mime!,SourceUrl=a.Node.Url,OriginalName=name,Origin="Source",ValidationStatus=SourceBatch.AssetValidation,ScanStatus="Clean",IsQuarantined=false};db.MediaAssets.Add(asset);await db.SaveChangesAsync();
                db.AuditLogs.Add(new(){Actor="phase3b-cli",Action="Phase3B.ScanProof",Detail=JsonSerializer.Serialize(new{batchId,asset.Id,asset.Sha256,a.Node.ScanEngine,a.Node.ScannedUtc,a.Node.ScannedSha256})});
            }
            body=body.Replace("/media/"+a.Id,"/media/"+asset.Id,StringComparison.OrdinalIgnoreCase);
            if(resolved.All(x=>x.Id!=asset.Id))resolved.Add((asset.Id,a));
        }
        ContentRevision revision;
        if(existingRevision is not null)revision=existingRevision;
        else{
            revision=new(){ContentId=content.Id,Title=s.Title,Summary=s.Summary,BodyHtml=body,Author=s.Author,SourceCredit=SourceBatch.Source,IsSourceRevision=true,ContentHash=p.Fingerprint,Metadata=SourceMetadata(s),PrimaryCategoryId=p.Primary,CoverAssetId=resolved.FirstOrDefault(x=>x.Asset.Node.Mime!.StartsWith("image/")).Id is var cover&&cover!=Guid.Empty?cover:null,SearchText=TextNormalizer.Search(s.Title+" "+s.Summary+" "+new HtmlPolicy().PlainText(body)),Categories=p.Categories.Select(x=>new RevisionCategory{CategoryId=x}).ToList(),Assets=resolved.Select((x,index)=>new ContentAsset{MediaAssetId=x.Id,Usage=x.Asset.Node.Mime!.StartsWith("image/")?"Inline":"Attachment",SortOrder=index}).ToList()};
            db.Revisions.Add(revision);db.Add(SearchProjectionService.Create(revision));await db.SaveChangesAsync();
        }
        if(record is null){
            content.CurrentRevisionId=revision.Id;content.PublishedRevisionId=revision.Id;content.IsPublic=true;content.State=WorkflowState.Published;
            await AddCategoriesAsync(content.Id,p.Categories,CancellationToken.None);
            record=new(){ContentId=content.Id,Kind=s.Kind,SourceId=s.SourceId,CanonicalUrl=s.CanonicalUrl,LastContentHash=p.Fingerprint};db.SourceRecords.Add(record);
        }else{
            // Existing editorial/current/published pointers, approval, working draft and live taxonomy are untouched.
            record.LastContentHash=p.Fingerprint;record.LastSeenUtc=DateTimeOffset.UtcNow;
        }
        await AliasesAsync(content.Id,aliases,CancellationToken.None);
        if(s.Kind==ContentKind.LegalDocument&&!await db.LegalDocuments.AnyAsync(x=>x.ContentId==content.Id))db.LegalDocuments.Add(new(){ContentId=content.Id,Number=revision.Metadata.Number,DocumentType=revision.Metadata.DocumentType,IssuingBody=revision.Metadata.IssuingBody,Signer=revision.Metadata.Signer,Field=revision.Metadata.Field,IssuedDate=revision.Metadata.IssuedDate});
        if(s.Kind==ContentKind.DraftDocument&&!await db.DraftDocuments.AnyAsync(x=>x.ContentId==content.Id))db.DraftDocuments.Add(new(){ContentId=content.Id,ConsultationDateEvidence=revision.Metadata.ConsultationDateEvidence,ConsultationStartUtc=revision.Metadata.ConsultationStartUtc,ConsultationEndUtc=revision.Metadata.ConsultationEndUtc});
        db.AuditLogs.Add(new(){Actor="phase3b-cli",Action="Phase3B.Import",ContentId=content.Id,RevisionId=revision.Id,Detail=auditKey});
        Warnings(content.Id,revision.Id);
        return(content.Id,revision.Id);
        void Warnings(Guid contentId,Guid revisionId){foreach(var finding in p.Findings.Where(x=>x.Severity=="NON-BLOCKING"))db.AuditLogs.Add(new(){Actor="phase3b-cli",Action="Phase3B.ImportWarning",ContentId=contentId,RevisionId=revisionId,Detail=JsonSerializer.Serialize(new{batchId,SourceIdentity=s.Kind+":"+s.SourceId,Finding=finding})});}
    }
}
