using System.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.UatData;
public static class PackageExport
{
    public static async Task RunAsync(string output,IConfiguration config)
    {
        if(new SqlConnectionStringBuilder(DatabaseSettings.Connection(config)).InitialCatalog!="LandPortal_Dev")throw new InvalidOperationException("Read-only export requires exactly LandPortal_Dev.");
        if(!Path.GetFullPath(output).StartsWith(Path.Combine(PortalConfiguration.FindWorkspace(),".local","handoff")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Private project handoff directory required; never webroot/Git.");
        if(Directory.Exists(output)||File.Exists(output))throw new InvalidOperationException("Unique output directory required; no overwrite.");
        DataPackage.NoReparse(output);Directory.CreateDirectory(output);
        var storage=StorageOptions.FromConfiguration(config);await using var db=new PortalDbContext(DatabaseSettings.Options(config,forMigration:true));
        await SchemaManager.VerifyLocalProjectAsync(db,"LandPortal_Dev");
        var data=new DataBundle();
        List<Content> contents;List<ContentRevision> revisions;List<ContentAsset> links;List<RevisionCategory> categories;List<SourceRecord> sources;List<SourceUrlAlias> aliases;List<MediaAsset> media;List<LegalDocument> legal;List<DraftDocument> drafts;
        await using(var snapshot=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            contents=await db.Contents.AsNoTracking().OrderBy(x=>x.Id).ToListAsync();var ids=contents.Select(x=>x.PublishedRevisionId).OfType<Guid>().ToArray();
            revisions=await db.Revisions.AsNoTracking().Where(x=>ids.Contains(x.Id)).ToListAsync();links=await db.ContentAssets.AsNoTracking().Where(x=>ids.Contains(x.RevisionId)).OrderBy(x=>x.SortOrder).ThenBy(x=>x.MediaAssetId).ToListAsync();categories=await db.RevisionCategories.AsNoTracking().Where(x=>ids.Contains(x.RevisionId)).ToListAsync();
            sources=await db.SourceRecords.AsNoTracking().ToListAsync();aliases=await db.SourceUrlAliases.AsNoTracking().ToListAsync();media=await db.MediaAssets.AsNoTracking().OrderBy(x=>x.Id).ToListAsync();legal=await db.LegalDocuments.AsNoTracking().ToListAsync();drafts=await db.DraftDocuments.AsNoTracking().ToListAsync();
            data.Categories=await db.Categories.AsNoTracking().OrderBy(x=>x.SortOrder).Select(x=>new CategoryRow(x.Id,x.Name,x.Slug,x.SourceUrl,x.SortOrder,x.ParentId)).ToListAsync();
            data.SourceMigration=(await db.Database.GetAppliedMigrationsAsync()).Last();
            data.Inventory=new(){["Contents"]=contents.Count,["Revisions"]=await db.Revisions.CountAsync(),["MediaAssets"]=media.Count,["Aliases"]=aliases.Count,["Categories"]=data.Categories.Count,["NotScanned"]=media.Count(x=>x.ScanStatus=="NotScanned")};
            var live=(await db.Set<SiteConfiguration>().AsNoTracking().SingleAsync()).LiveRevisionId;
            if(live is Guid liveId)
            {
                var s=await db.Set<SiteConfigurationRevision>().AsNoTracking().Include(x=>x.Categories).Include(x=>x.Menus).Include(x=>x.Banners).Include(x=>x.Blocks).Include(x=>x.Pins).Include(x=>x.Vocabularies).SingleAsync(x=>x.Id==liveId);
                data.PublishedSite=JsonSerializer.SerializeToElement(new{s.Id,s.ContentHash,s.OrganizationName,s.FooterText,s.EnglishName,s.SupervisingAgency,s.ResponsiblePerson,s.LicenseNumber,s.SeoDescription,s.ContactAddress,s.ContactPhone,s.ContactHotline,s.ContactEmail,s.LogoAssetId,
                    Categories=s.Categories.Select(x=>new{x.CategoryId,x.DisplayName,x.Slug,x.Description,x.ParentCategoryId,x.SortOrder,x.ShowInNavigation}),
                    Menus=s.Menus.Select(x=>new{x.Id,x.ParentId,x.Position,x.Label,x.TargetKind,x.CategoryId,x.ContentId,x.RouteKey,x.ExternalUrl,x.OpenNewWindow,x.SortOrder,x.SystemProtected,x.Enabled}),
                    Banners=s.Banners.Select(x=>new{x.Id,x.MediaAssetId,x.Label,x.Alt,x.Position,x.TargetKind,x.CategoryId,x.ContentId,x.RouteKey,x.ExternalUrl,x.OpenNewWindow,x.SortOrder,x.StartUtc,x.EndUtc}),
                    Blocks=s.Blocks.Select(x=>new{x.Id,x.Type,x.Title,x.Limit,x.CategoryId,x.SortOrder,x.Enabled}),Pins=s.Pins.Select(x=>new{x.BlockId,x.ContentId,x.SortOrder}),Vocabularies=s.Vocabularies.Select(x=>new{x.Id,x.Kind,x.Label,x.SortOrder,x.Active})},DataPackage.Json);
            }
            await snapshot.CommitAsync();
        }
        var files=new Dictionary<string,string>();
        var candidateMedia=new Dictionary<Guid,MediaRow>();var denied=new Dictionary<Guid,string>();
        var eligibleRevisions=contents.Where(c=>c.IsPublic&&!c.IsTrashed&&c.State==WorkflowState.Published&&!c.HasEditorialChanges&&sources.Count(s=>s.ContentId==c.Id&&s.Source=="qldd.mae.gov.vn")==1).Select(c=>c.PublishedRevisionId).OfType<Guid>().ToHashSet();
        var publicAssets=links.Where(x=>eligibleRevisions.Contains(x.RevisionId)).Select(x=>x.MediaAssetId).Concat(revisions.Where(x=>eligibleRevisions.Contains(x.Id)&&x.CoverAssetId!=null).Select(x=>x.CoverAssetId!.Value)).ToHashSet();
        if(data.PublishedSite is JsonElement publicSite){if(publicSite.GetProperty("LogoAssetId").ValueKind!=JsonValueKind.Null)publicAssets.Add(publicSite.GetProperty("LogoAssetId").GetGuid());foreach(var banner in publicSite.GetProperty("Banners").EnumerateArray())publicAssets.Add(banner.GetProperty("MediaAssetId").GetGuid());}
        foreach(var m in media)
        {
            if(!publicAssets.Contains(m.Id)){denied[m.Id]="NOT_REFERENCED_BY_PUBLIC_SOURCE";continue;}
            if(m.Origin!="Source"||m.ScanStatus!="Clean"||m.IsQuarantined){denied[m.Id]="NOT_CLEAN_OR_NOT_SOURCE";continue;}
            if(m.MimeType is not("application/pdf" or "image/jpeg" or "image/png" or "image/gif" or "image/webp")||m.ByteLength>48L*1024*1024){denied[m.Id]="UNSUPPORTED_TYPE_OR_SIZE";continue;}
            try
            {
                var source=MediaService.ResolveMediaPath(storage,m.StorageKey);DataPackage.NoReparse(source);
                var ext=m.MimeType switch{"application/pdf"=>".pdf","image/jpeg"=>".jpg","image/png"=>".png","image/gif"=>".gif",_=>".webp"};
                var ready=new MediaUploadOptions().CanServe(m);var relative=(ready?"ready/media/":"pending/media/")+m.Sha256+ext;var dest=DataPackage.PathIn(output,relative);
                if(new FileInfo(source).Length!=m.ByteLength||await DataPackage.FileHash(source)!=m.Sha256)throw new InvalidDataException("Hash mismatch.");
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);await using(var input=File.OpenRead(source))await using(var copied=new FileStream(dest,FileMode.CreateNew))await input.CopyToAsync(copied);
                files[relative]=m.Sha256;
                candidateMedia[m.Id]=new(m.Id,m.Sha256,m.ByteLength,m.MimeType,m.SourceUrl,Path.GetFileName(m.OriginalName),m.Width,m.Height,m.CreatedUtc,relative,m.ScanStatus,m.ValidationStatus,ready);
            }
            catch(Exception e)when(e is IOException or InvalidDataException or MediaUploadException){denied[m.Id]="MISSING_OR_CHANGED_BYTES";}
        }
        var parser=new HtmlParser();
        foreach(var c in contents)
        {
            var reason=new List<string>();var r=revisions.SingleOrDefault(x=>x.Id==c.PublishedRevisionId);var origin=sources.Where(x=>x.ContentId==c.Id).ToArray();
            if(!c.IsPublic||c.IsTrashed||c.State!=WorkflowState.Published||c.HasEditorialChanges)reason.Add("NOT_APPROVED_PUBLIC_SOURCE_SNAPSHOT");
            if(origin.Length!=1||origin[0].Source!="qldd.mae.gov.vn"||r?.IsSourceRevision!=true)reason.Add("NOT_VERIFIED_QLDD_SOURCE");
            if(c.Kind==ContentKind.Video)reason.Add("VIDEO_METADATA_ONLY_NO_FILE");
            if(r==null)reason.Add("NO_PUBLISHED_REVISION");
            if(r!=null)
            {
                if(r.Metadata.WeeklyOriginalAssetId!=null)reason.Add("PRIVATE_WORD_DEPENDENCY");
                var deps=links.Where(x=>x.RevisionId==r.Id).Select(x=>x.MediaAssetId).Append(r.CoverAssetId??Guid.Empty).Where(x=>x!=Guid.Empty).Distinct().ToArray();
                foreach(var id in deps.Where(x=>!candidateMedia.ContainsKey(x)))reason.Add("MEDIA_"+id+":"+denied.GetValueOrDefault(id,"MISSING"));
                var doc=parser.ParseDocument(r.BodyHtml);
                if(doc.QuerySelectorAll("img[src],source[src],video[src],iframe[src]").Any(x=>!(x.GetAttribute("src")??"").StartsWith("/media/",StringComparison.OrdinalIgnoreCase)))reason.Add("EXTERNAL_OR_UNRESOLVED_EMBED");
                foreach(Match match in Regex.Matches(r.BodyHtml,@"/media/([a-fA-F0-9-]{36})"))if(Guid.TryParse(match.Groups[1].Value,out var id)&&!deps.Contains(id))reason.Add("UNDECLARED_BODY_MEDIA:"+id);
                if(reason.Count==0)
                {
                    var metadata=r.Metadata.Copy();metadata.WeeklyOriginalReviewEvidence=null;metadata.PublicationReviewEvidence=null;
                    var rd=new RevisionRow(r.Id,r.Title,r.Summary,r.BodyHtml,r.SearchText,r.Author,r.SourceCredit,r.CreatedUtc,r.IsSourceRevision,r.ContentHash,r.PrimaryCategoryId,r.CoverAssetId,metadata);
                    var l=legal.SingleOrDefault(x=>x.ContentId==c.Id);var d=drafts.SingleOrDefault(x=>x.ContentId==c.Id);
                    var row=new ContentRow(c.Id,c.Kind,c.CreatedUtc,c.PublishedAtUtc,c.SourcePublishedDate,c.OriginalDateText,c.DatePrecision,c.CommentsEnabled,rd,categories.Where(x=>x.RevisionId==r.Id).Select(x=>x.CategoryId).Order().ToList(),links.Where(x=>x.RevisionId==r.Id).Select(x=>new AssetLink(x.MediaAssetId,x.Usage,x.Caption,x.SortOrder)).ToList(),origin.Select(x=>new SourceRow(x.Source,x.SourceId,x.Kind,x.CanonicalUrl,x.LastContentHash)).ToList(),aliases.Where(x=>x.ContentId==c.Id).Select(x=>x.Url).Order().ToList(),l==null?null:new(l.Number,l.DocumentType,l.IssuingBody,l.Signer,l.Field,l.IssuedDate,l.EffectiveDate,l.EffectivenessStatus),d==null?null:new(d.ConsultationStartUtc,d.ConsultationEndUtc,d.ConsultationDateEvidence));
                    if(deps.All(x=>candidateMedia[x].TargetPolicyReady))data.Ready.Add(row);else data.PendingReview.Add(row);
                }
            }
            if(reason.Count>0)data.Excluded.Add(new("Content",origin.FirstOrDefault()?.Kind+":"+origin.FirstOrDefault()?.SourceId+":"+c.Id,reason.Distinct().ToArray()));
        }
        foreach(var item in denied)data.Excluded.Add(new("Media",item.Key.ToString(),[item.Value]));
        data.Media=candidateMedia.Values.OrderBy(x=>x.Id).ToList();
        var git=new ProcessStartInfo("rtk"){UseShellExecute=false,RedirectStandardOutput=true};foreach(var argument in new[]{"proxy","git","rev-parse","HEAD"})git.ArgumentList.Add(argument);using(var p=Process.Start(git)!){data.SourceCommit=(await p.StandardOutput.ReadToEndAsync()).Trim();await p.WaitForExitAsync();}
        DataPackage.Validate(data);
        await DataPackage.Write(Path.Combine(output,"data.json"),data);files["data.json"]=await DataPackage.FileHash(Path.Combine(output,"data.json"));
        await DataPackage.Write(Path.Combine(output,"exclusions.json"),data.Excluded);files["exclusions.json"]=await DataPackage.FileHash(Path.Combine(output,"exclusions.json"));
        var repo=PortalConfiguration.FindWorkspace();var ops=Path.Combine(output,"ops","source");Directory.CreateDirectory(ops);
        foreach(var f in Directory.GetFiles(Path.Combine(repo,"tools","UatDataHandoff")).Where(x=>Path.GetExtension(x) is ".cs" or ".csproj"||Path.GetFileName(x)=="packages.lock.json")){var rel="ops/source/"+Path.GetFileName(f);File.Copy(f,DataPackage.PathIn(output,rel));files[rel]=await DataPackage.FileHash(DataPackage.PathIn(output,rel));}
        foreach(var name in new[]{"uat-data.ps1","uat-data-runbook.md","uat-config.example.json","uat-operator-steps.md"}){var rel="ops/"+name;var source=name=="uat-data-runbook.md"?Path.Combine(repo,"docs","uat-data-handoff.md"):Path.Combine(repo,"scripts",name);File.Copy(source,DataPackage.PathIn(output,rel));files[rel]=await DataPackage.FileHash(DataPackage.PathIn(output,rel));}
        await DataPackage.Write(Path.Combine(output,"manifest.json"),new PackageManifest(1,data.PackageId,DateTimeOffset.UtcNow,"data.json",files,"ready excludes Development-only/NotScanned media; pending requires exact signed target approval and native target scan before import"));
        var sha=await DataPackage.FileHash(Path.Combine(output,"manifest.json"));await File.WriteAllTextAsync(Path.Combine(output,"manifest.sha256"),sha+"  manifest.json\n");
        await File.WriteAllLinesAsync(Path.Combine(output,"checksums.sha256"),files.Select(x=>x.Value+"  "+x.Key).Append(sha+"  manifest.json"));
        Console.WriteLine(JsonSerializer.Serialize(new{status="EXPORTED_READ_ONLY",data.PackageId,manifestSha256=sha,data.Inventory,ready=data.Ready.Count,pendingTargetRelease=data.PendingReview.Count,excludedContent=data.Excluded.Count(x=>x.Entity=="Content"),cleanBytesCopied=data.Media.Sum(x=>x.Bytes),notScannedBytesCopied=0,sourceWrites=0}));
    }
}
