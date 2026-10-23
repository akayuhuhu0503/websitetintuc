using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Web.Services;

// Durable, cross-process counts of successful public document requests. No IP,
// cookie, email or reader identity is collected; HEAD/Range are never downloads.
public sealed class DocumentUsage(PortalDbContext db, PublicContentQuery publicContent, ILogger<DocumentUsage> logger)
{
    public const string ViewAction="PublicDocumentView",DownloadAction="PublicDocumentDownload";
    private IQueryable<PublicRow> Documents=>publicContent.Published().Where(x=>x.Kind==ContentKind.LegalDocument||x.Kind==ContentKind.DraftDocument);
    public async Task ViewAsync(Guid contentId,Guid revisionId)
    {
        if(!await Documents.AnyAsync(x=>x.Id==contentId&&x.RevisionId==revisionId))return;
        db.AuditLogs.Add(new(){Actor="Public",Action=ViewAction,ContentId=contentId,RevisionId=revisionId});
        await db.SaveChangesAsync();
    }
    public async Task<(long Views,long Downloads)> CountsAsync(Guid contentId)
    {
        var rows=await db.AuditLogs.AsNoTracking().Where(x=>x.ContentId==contentId&&(x.Action==ViewAction||x.Action==DownloadAction)).GroupBy(x=>x.Action).Select(x=>new{x.Key,Count=x.LongCount()}).ToListAsync();
        return(rows.FirstOrDefault(x=>x.Key==ViewAction)?.Count??0,rows.FirstOrDefault(x=>x.Key==DownloadAction)?.Count??0);
    }
    public void CountDownloadWhenSuccessful(HttpContext context,Guid assetId,Guid? contentId,bool download)
    {
        if(!download||contentId==null||!HttpMethods.IsGet(context.Request.Method)||context.Request.Headers.ContainsKey("Range"))return;
        context.Response.OnStarting(async()=>
        {
            if(context.Response.StatusCode!=200)return;
            try
            {
                var document=await Documents.Where(x=>x.Id==contentId&&db.ContentAssets.Any(a=>a.RevisionId==x.RevisionId&&a.MediaAssetId==assetId&&a.Usage=="Attachment"&&a.MediaAsset.MimeType=="application/pdf")).Select(x=>new{x.Id,x.RevisionId}).SingleOrDefaultAsync();
                if(document==null)return;
                db.AuditLogs.Add(new(){Actor="Public",Action=DownloadAction,ContentId=document.Id,RevisionId=document.RevisionId});await db.SaveChangesAsync();
            }
            catch(Microsoft.Data.SqlClient.SqlException error){logger.LogError("Document download counter unavailable; SQL code {Code}; file authorization unchanged.",error.Number);}
        });
    }
}
