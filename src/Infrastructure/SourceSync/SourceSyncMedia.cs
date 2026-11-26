using System.Security.Cryptography;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Infrastructure;

public sealed class SourceSyncMedia(StorageOptions storage,MediaService media,EditorialDocumentService documents,MediaUploadOptions policy,PortalDbContext db)
{
    public async Task<DraftInput> PrepareAsync(Guid contentId,SourceSyncCheckpoint checkpoint,int category,ContentActor actor,CancellationToken ct)
    {
        var source=checkpoint.Source??throw new InvalidOperationException("Chưa thu thập được nội dung.");
        var input=new DraftInput{Kind=source.Kind,Title=source.Title,Summary=SourceSyncPolicy.Summary(source),Author=source.Author,
            SourceCredit=source.CanonicalUrl,PrimaryCategoryId=category};
        input.Metadata.PublicationDate=source.PublishedDate;
        // PDF calendar articles follow the existing StaticPage workflow, like
        // the Excel imports. WeeklyYear/Week on ContentMetadata selects the
        // structured timetable workflow (rows + release), not a PDF article.
        // Its validated period is kept in the source checkpoint; the public
        // weekly list derives the same period from the unchanged source title.
        var accepted=new Dictionary<string,MediaAsset>(StringComparer.Ordinal);
        foreach(var file in checkpoint.Assets)
        {
            try
            {
                if(!file.Key.StartsWith("source-sync/resource/",StringComparison.Ordinal))throw new InvalidDataException("Sai kho tài nguyên nguồn.");
                var path=storage.ResolveSnapshot(file.Key);
                MediaService.ValidatePrivateStorageRoot(path,storage);
                await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);
                if(stream.Length!=file.Bytes||Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,ct))!=file.Sha256)throw new InvalidDataException("Tệp nguồn không còn khớp SHA256.");
                stream.Position=0;
                var existing=source.PrimaryCategoryId==76?await (from c in db.Contents join a in db.ContentAssets on c.CurrentRevisionId equals a.RevisionId
                    join m in db.MediaAssets on a.MediaAssetId equals m.Id where c.Id==contentId&&m.Sha256==file.Sha256&&m.ByteLength==file.Bytes select m).FirstOrDefaultAsync(ct):null;
                var asset=existing??(file.Mime.StartsWith("image/")?(await media.UploadAsync(stream,file.Name,file.Mime,actor.UserId,false,ct)).Asset:
                    await documents.UploadAsync(contentId,stream,file.Name,file.Mime,actor,ct));
                if(!policy.CanServe(asset)||asset.IsQuarantined){checkpoint.Warnings.Add("Tệp "+file.Name+" đang chờ kiểm định; chưa gắn vào bản nháp.");continue;}
                accepted[file.Url]=asset;
                if(!input.Assets.Any(x=>x.MediaAssetId==asset.Id))input.Assets.Add(new(){MediaAssetId=asset.Id,Usage=file.Mime.StartsWith("image/")?"Inline":"Attachment",SortOrder=input.Assets.Count});
                if(asset.MimeType.StartsWith("image/"))input.CoverAssetId??=asset.Id;
            }
            catch(Exception e)when(e is IOException or InvalidDataException or MediaUploadException or InvalidOperationException or UnauthorizedAccessException)
            {checkpoint.Warnings.Add("Chưa gắn tệp "+file.Name+" ("+e.GetType().Name+"). Cần tải/kiểm định lại trong trình biên tập.");}
        }
        var doc=SourceParser.Document(source.BodyHtml);
        foreach(var img in doc.QuerySelectorAll("img"))
        {
            // Lazy/srcset markup may carry an empty or placeholder src. Select
            // only a candidate actually downloaded and accepted above.
            var candidates=new[]{img.GetAttribute("src"),img.GetAttribute("data-src")}
                .Concat(SourceSrcset.Urls(img.GetAttribute("srcset")??"",img.GetAttribute("src"),img.GetAttribute("data-src")));
            var asset=candidates.Where(x=>!string.IsNullOrWhiteSpace(x))
                .Select(x=>Uri.TryCreate(new Uri(source.SourceUrl),x,out var url)?accepted.GetValueOrDefault(url.AbsoluteUri):null)
                .FirstOrDefault(x=>x?.MimeType.StartsWith("image/")==true);
            if(asset!=null)
            {
                img.SetAttribute("src","/media/"+asset.Id);
                var caption=img.Closest("figure")?.QuerySelector("figcaption")?.TextContent.Trim()??img.GetAttribute("alt")??"";
                foreach(var selection in input.Assets.Where(x=>x.MediaAssetId==asset.Id))selection.Caption=caption[..Math.Min(2000,caption.Length)];
            }
            else img.Remove(); // No hotlinked fallback or unvalidated image.
        }
        foreach(var anchor in doc.QuerySelectorAll("a[href]"))
        {
            var raw=anchor.GetAttribute("href")??"";
            if(!Uri.TryCreate(new Uri(source.SourceUrl),raw,out var url)){anchor.RemoveAttribute("href");continue;}
            if(accepted.TryGetValue(url.AbsoluteUri,out var asset))anchor.SetAttribute("href","/media/"+asset.Id);
            else if(source.AssetUrls.Contains(url.AbsoluteUri)||url.Scheme is not("http" or "https"))anchor.RemoveAttribute("href");
            else anchor.SetAttribute("href",url.AbsoluteUri);
        }
        input.BodyHtml=new HtmlPolicy().Clean(doc.Body?.InnerHtml??"");
        return input;
    }
    public static string Preview(ParsedSource? source)
    {
        if(source==null)return "";
        var doc=SourceParser.Document(source.BodyHtml);
        foreach(var element in doc.QuerySelectorAll("img,video,audio,source"))element.Remove();
        foreach(var anchor in doc.QuerySelectorAll("a"))anchor.RemoveAttribute("href");
        return new HtmlPolicy().Clean(doc.Body?.InnerHtml??"");
    }
}
