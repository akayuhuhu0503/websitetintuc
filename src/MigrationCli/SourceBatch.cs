using System.Data;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.MigrationCli;

// Offline only. A sealed preview is the authority for this batch, never a crawl permission.
public sealed partial class SourceBatch(PortalDbContext db,StorageOptions storage,IConfiguration config,string workspace,InventoryState state,string checkpointHash,string? recoveryProofHash=null,ApprovedBatchExecution? execution=null)
{
    public const string Source="qldd.mae.gov.vn";
    public const string AssetValidation="Phase3BValidated";
    string PrivateRoot=>Path.Combine(workspace,".local","phase3a",state.Id.ToString("N"));
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    static string H(string value)=>SourceInventory.HashText(value);
    public static string MediaRootIdentity(StorageOptions value) => H(Path.GetFullPath(value.MediaRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).ToUpperInvariant());
    void DemandStore(SourceBatchPlan plan)
    {
        if(plan.SourceResourcePolicySha256!=SourceResourcePolicy.Read(config)?.Sha)throw new InvalidDataException("Reviewed source policy differs from exact plan; create and approve a fresh packet.");
        if(plan.SchemaVersion is not(1 or 2 or 3) || plan.SchemaVersion==3&&plan.TargetMediaRootSha256!=MediaRootIdentity(storage))
            throw new InvalidDataException("Preview media storage differs from the exact target; create and approve a fresh preview.");
    }
    public static async Task<InventoryState> ReadCheckpointAsync(string workspace,string path,string hash)
    {
        if(!Regex.IsMatch(hash,"^[a-f0-9]{64}$"))throw new InvalidDataException("Expected exact checkpoint SHA256.");
        var full=Path.GetFullPath(path,workspace);
        if(!full.StartsWith(Path.Combine(workspace,".local","phase3a")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Checkpoint outside project inventory.");
        SourceInventory.NoReparse(full);
        if(new FileInfo(full).Length>128L*1024*1024)throw new InvalidDataException("Checkpoint too large.");
        var bytes=await File.ReadAllBytesAsync(full);if(SourceInventory.Hash(bytes)!=hash)throw new InvalidDataException("Checkpoint hash mismatch.");
        using var source=new MemoryStream(bytes);Stream input=source;
        if(bytes.AsSpan().StartsWith("LP3A-BR1"u8)){source.Position=8;input=new BrotliStream(source,CompressionMode.Decompress);}
        using var expanded=new MemoryStream();var buf=new byte[65536];int read;
        while((read=await input.ReadAsync(buf))>0){if(expanded.Length+read>128L*1024*1024)throw new InvalidDataException("Expanded checkpoint too large.");expanded.Write(buf,0,read);}
        var result=JsonSerializer.Deserialize<InventoryState>(expanded.ToArray(),InventoryLimits.Json)??throw new InvalidDataException("Empty checkpoint.");
        if(result.SchemaVersion!=3||result.Items.GroupBy(x=>(x.Source.Kind,x.Source.SourceId)).Any(x=>x.Count()!=1))throw new InvalidDataException("Duplicate identity/unsupported checkpoint.");
        if(!full.Equals(Path.Combine(workspace,".local","phase3a",result.Id.ToString("N"),"checkpoints",hash+".bin"),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Checkpoint session/path mismatch.");
        return result;
    }
    async Task Guard(bool write)
    {
        var database=config["Sql:ExpectedDatabase"];
        if(config["DOTNET_ENVIRONMENT"]!="Development"||database is not("LandPortal_Test" or "LandPortal_Dev")||write&&database!="LandPortal_Test"&&execution is null)throw new InvalidOperationException("Batch writes require SQL Test or externally signed local Dev authorization. Ordinary Dev preview only.");
        await SchemaManager.VerifyLocalProjectAsync(db,database!);
        if(write){var prefix=Path.GetFullPath(Path.Combine(workspace,".local",database=="LandPortal_Test"?"tests":"media"))+Path.DirectorySeparatorChar;if(!Path.GetFullPath(storage.MediaRoot).StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&Path.GetFullPath(storage.MediaRoot).TrimEnd(Path.DirectorySeparatorChar)!=prefix.TrimEnd(Path.DirectorySeparatorChar))throw new InvalidOperationException("Exact project target media storage required.");}
    }
    static string Text(string html)=>Regex.Replace(new HtmlPolicy().PlainText(html).Normalize(NormalizationForm.FormC),@"\s+"," ").Trim();
    public async Task<SourceBatchPrepared> PrepareAsync(InventoryItem item,IReadOnlyList<OptionalImageReview>? optionalImages=null)
    {
        var errors=new List<string>();var assets=new List<SourceBatchAsset>();var s=item.Source;
        var warnings=new List<string>();var omitted=new HashSet<string>();var unavailable=new HashSet<string>();optionalImages??=[];
        if(optionalImages.GroupBy(x=>x.Url).Any(x=>x.Count()!=1)||optionalImages.Any(x=>!BatchReadiness.CanReviewOptional(s,x)))errors.Add("INVALID_OPTIONAL_IMAGE_REVIEW");
        var raw=state.Captures.LastOrDefault(x=>x.Sha256==item.RawSha256&&x.Status==200&&state.Urls.Any(u=>u.Url==x.Url&&u.Role=="Detail"));
        if(raw is null)throw new InvalidDataException("Missing verified detail raw.");
        var bytes=await ReadBlob(raw);
        var parsed=SourceParser.Parse(Encoding.UTF8.GetString(bytes),s.SourceUrl,s.Kind,s.SourceId);
        if(parsed.SourceId!=s.SourceId||parsed.Kind!=s.Kind||parsed.CanonicalUrl!=s.CanonicalUrl||parsed.Title!=s.Title||Text(parsed.BodyHtml)!=Text(s.BodyHtml)||parsed.Summary!=s.Summary||parsed.Author!=s.Author||parsed.OriginalDateText!=s.OriginalDateText||parsed.PublishedUtc!=s.PublishedUtc||parsed.PublishedDate!=s.PublishedDate||parsed.DatePrecision!=s.DatePrecision||JsonSerializer.Serialize(parsed.Fields.OrderBy(x=>x.Key))!=JsonSerializer.Serialize(s.Fields.OrderBy(x=>x.Key)))throw new InvalidDataException("Parsed source differs from exact raw proof.");
        if(!parsed.AssetUrls.SequenceEqual(s.AssetUrls))throw new InvalidDataException("Required source files cannot be removed from staging.");
        if(parsed.BodyHtml!=s.BodyHtml)throw new InvalidDataException("Source markup differs from raw proof.");
        if(string.IsNullOrWhiteSpace(s.Title))errors.Add("REQUIRED_TITLE_ABSENT");
        if(s.Kind==ContentKind.Video)errors.Add("VIDEO_METADATA_ONLY_NO_FILE");
        foreach(var url in s.AssetUrls.Distinct())
        {
            var node=state.Urls.SingleOrDefault(x=>x.Url==url&&x.Role=="File");
            var unavailableReview=optionalImages.SingleOrDefault(x=>x.Url==url&&x.RecoveryMode=="SourceAttachmentUnavailable");
            if(unavailableReview is not null)
            {
                if(!BatchReadiness.CanReviewOptional(s,unavailableReview)||!unavailableReview.Evidence.Contains(item.RawSha256,StringComparison.Ordinal)||!await IsUnavailableResponseAsync(node,url,unavailableReview))
                    errors.Add("INVALID_SOURCE_UNAVAILABLE_REVIEW:"+url);
                else { unavailable.Add(url);warnings.Add((unavailableReview.UnavailableState=="WITHHELD_VALIDATION"?"SOURCE_ATTACHMENT_WITHHELD_VALIDATION:":"SOURCE_ATTACHMENT_UNAVAILABLE:")+url); }
                continue;
            }
            bool OptionalMissing()=>optionalImages.Any(x=>x.Url==url&&BatchReadiness.CanReviewOptional(s,x))&&
                (node is null||node.ScanStatus=="NotScanned")&&node?.ScannedSha256 is null&&
                !state.Captures.Any(x=>x.Url==url&&x.Status==200);
            void Missing(string code){if(OptionalMissing()){omitted.Add(url);warnings.Add("OPTIONAL_IMAGE_OMITTED:"+url);}else errors.Add(code+":"+url);}
            if(node?.CaptureSha is null){Missing("MISSING_FILE");continue;}
            var capture=state.Captures.LastOrDefault(x=>x.Url==url&&x.Sha256==node.CaptureSha&&x.Status==200);
            if(capture is null){Missing("MISSING_CAPTURE");continue;}
            var data=await ReadBlob(capture);
            // A verified zero-byte response contains no usable photo and no bytes to publish.
            // Nonempty invalid/unsafe captures never enter this omission path.
            if(data.Length==0&&node.ScanStatus=="NotScanned"&&node.ScannedSha256 is null&&optionalImages.Any(x=>x.Url==url&&BatchReadiness.CanReviewOptional(s,x)))
            {omitted.Add(url);warnings.Add("OPTIONAL_IMAGE_OMITTED:"+url);continue;}
            if(node.FormatStatus!="Validated"||node.Mime is not("image/jpeg" or "image/png" or "image/gif" or "image/webp" or "application/pdf")){errors.Add("UNSUPPORTED_FORMAT:"+url);continue;}
            var media=new MediaService(db,storage,SourceResourcePolicy.MediaOptions(config,url));media.ValidateContent(BlobPath(capture.Key),node.Mime,CancellationToken.None);
            if(node.ScanStatus!="Clean"||node.ScannedSha256!=capture.Sha256||string.IsNullOrWhiteSpace(node.ScanEngine)||node.ScannedUtc is null||!(node.ScanEngine.StartsWith("Defender/")||node.ScanEngine.StartsWith("Clam")))errors.Add("SCAN_NOT_CLEAN:"+url);
            assets.Add(new(node,capture,new Guid(Convert.FromHexString(capture.Sha256).AsSpan(0,16))));
        }
        var cats=s.CategoryIds.Append(s.PrimaryCategoryId??-1).Where(x=>x>=0).Distinct().Order().ToList();
        if(unavailable.Count>0&&Text(s.BodyHtml).Length<200&&assets.Count==0)errors.Add("PARTIAL_SOURCE_CONTENT");
        var known=await db.Categories.Where(x=>cats.Contains(x.Id)).Select(x=>x.Id).ToListAsync();
        // Missing legacy category is retained as provenance, never invented or required by new-editor validation.
        var primary=known.Contains(s.PrimaryCategoryId??-1)?s.PrimaryCategoryId:null;
        var document=SourceParser.Document(s.BodyHtml);
        foreach(var img in document.QuerySelectorAll("img").ToArray())
            if(new[]{"src","data-src"}.Any(attr=>Uri.TryCreate(new Uri(s.SourceUrl),img.GetAttribute(attr),out var uri)&&omitted.Contains(uri.AbsoluteUri)))img.Remove();
        foreach(var el in document.QuerySelectorAll("[src],[href],[data-src],[srcset]"))
        {
            foreach(var attr in new[]{"src","href","data-src"})
            {
                var value=el.GetAttribute(attr);if(value is null||!Uri.TryCreate(new Uri(s.SourceUrl),value,out var uri))continue;
                if(attr=="href"&&unavailable.Contains(uri.AbsoluteUri)) {el.SetAttribute("href",SourceUnavailableLinks.Path((int)s.Kind,s.SourceId,uri.AbsoluteUri));el.SetAttribute("title","Tệp đính kèm hiện không khả dụng.");continue;}
                var a=assets.FirstOrDefault(x=>x.Node.Url==uri.AbsoluteUri);
                if(a is not null)el.SetAttribute(attr=="data-src"?"src":attr,"/media/"+a.Id);
                else if(attr is "src" or "data-src")el.RemoveAttribute(attr);
                else if(uri.Host.EndsWith("qldd.mae.gov.vn",StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(uri.AbsolutePath,@"\.(pdf|docx?|xlsx?|jpe?g|png|mp4)$",RegexOptions.IgnoreCase))errors.Add("UNRESOLVED_ATTACHMENT:"+uri.AbsoluteUri);
            }
            el.RemoveAttribute("srcset");el.RemoveAttribute("data-src");
        }
        var body=new HtmlPolicy().Clean(document.Body?.InnerHtml??"");
        if(Text(body)!=Text(s.BodyHtml))errors.Add("MEANINGFUL_TEXT_MISMATCH");
        var fingerprint=H(JsonSerializer.Serialize(new{s.Kind,s.SourceId,s.Title,s.Summary,Text=Text(s.BodyHtml),Markup=H(s.BodyHtml),s.Author,s.OriginalDateText,s.PublishedDate,s.PublishedUtc,s.DatePrecision,Fields=s.Fields.OrderBy(x=>x.Key),Categories=cats,Primary=s.PrimaryCategoryId,Files=assets.Select(x=>x.Capture.Sha256)}));
        if(omitted.Count>0||unavailable.Count>0)fingerprint=H(fingerprint+JsonSerializer.Serialize(optionalImages.OrderBy(x=>x.Url)));
        if(string.IsNullOrWhiteSpace(s.Summary)||string.IsNullOrWhiteSpace(s.Author))warnings.Add("OPTIONAL_METADATA_ABSENT");
        return new(item,fingerprint,body,assets,known.Order().ToList(),primary,errors){Warnings=warnings,Findings=errors.Select(x=>BatchReadiness.Finding(s,x)).Concat(warnings.Select(x=>BatchReadiness.Finding(s,x,true))).ToList()};
    }
    async Task<bool> IsUnavailableResponseAsync(InventoryUrl? node,string url,OptionalImageReview review)
    {
        // Missing/non-file HTTP responses are distinct from quarantined or
        // unscanned actual files. A real PDF/Office/image can never use this path.
        if(node?.ScanStatus is "Infected" or "ScanFailed"||node?.ScannedSha256 is not null||node?.FormatStatus=="Validated")return false;
        var capture=state.Captures.LastOrDefault(x=>x.Url==url&&x.Sha256==node?.CaptureSha);
        if(capture is null)return review.UnavailableState=="NOT_RETRIEVED_BUDGET"&&node?.CaptureSha is null&&node?.Status=="ValidationFailed"&&node.Error?.Contains("Content-Length exceeds response budget",StringComparison.Ordinal)==true;
        var bytes=await ReadBlob(capture);
        if(review.UnavailableState=="WITHHELD_VALIDATION")return capture.Status==200&&bytes.Length>0&&node?.FormatStatus!="Validated"&&node?.ScanStatus=="NotScanned"&&review.Evidence.Contains(capture.Sha256,StringComparison.Ordinal);
        if(review.UnavailableState!="SOURCE_MISSING")return false;
        var header=Encoding.UTF8.GetString(bytes.AsSpan(0,Math.Min(bytes.Length,2048))).TrimStart('\uFEFF',' ','\r','\n','\t');
        if(capture.Status is 404 or 410)return bytes.Length==0||header.StartsWith("<!doctype html",StringComparison.OrdinalIgnoreCase)||header.StartsWith("<html",StringComparison.OrdinalIgnoreCase);
        if(capture.Status!=200||bytes.Length>1024*1024)return false;
        var text=Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF',' ','\r','\n','\t');
        return (text.StartsWith("<!doctype html",StringComparison.OrdinalIgnoreCase)||text.StartsWith("<html",StringComparison.OrdinalIgnoreCase))&&
            (text.Contains("404",StringComparison.Ordinal)||text.Contains("not found",StringComparison.OrdinalIgnoreCase)||text.Contains("không tìm thấy",StringComparison.OrdinalIgnoreCase)||text.Contains("error",StringComparison.OrdinalIgnoreCase));
    }
    string BlobPath(string key){var p=StorageOptions.ResolveWithin(PrivateRoot,key);SourceInventory.NoReparse(p);return p;}
    async Task<byte[]> ReadBlob(InventoryCapture c){var b=await File.ReadAllBytesAsync(BlobPath(c.Key));if(b.LongLength!=c.Bytes||SourceInventory.Hash(b)!=c.Sha256)throw new InvalidDataException("Source blob hash/size differs.");return b;}
    async Task<string> Stamp(SourceRecord? record)
    {
        if(record is null)return "ABSENT";
        var c=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==record.ContentId);
        var aliases=await db.SourceUrlAliases.AsNoTracking().Where(x=>x.ContentId==c.Id).OrderBy(x=>x.UrlHash).Select(x=>x.Url).ToArrayAsync();
        var revisions=await db.Revisions.AsNoTracking().Where(x=>x.ContentId==c.Id).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ContentHash}).ToArrayAsync();
        return H(JsonSerializer.Serialize(new{c.Id,Version=Convert.ToBase64String(c.RowVersion),record.LastContentHash,record.CanonicalUrl,aliases,revisions}));
    }
    async Task<List<string>> Differences(SourceBatchPrepared p,SourceRecord record)
    {
        var latest=await db.Revisions.AsNoTracking().Include(x=>x.Categories).Include(x=>x.Assets).Where(x=>x.ContentId==record.ContentId&&x.IsSourceRevision).OrderByDescending(x=>x.CreatedUtc).ThenByDescending(x=>x.Id).FirstAsync();
        if(latest.ContentHash==p.Fingerprint)return [];
        var s=p.Item.Source;var c=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==record.ContentId);var diff=new List<string>();
        void Compare(string name,object? a,object? b){if(JsonSerializer.Serialize(a)!=JsonSerializer.Serialize(b))diff.Add(name+": "+JsonSerializer.Serialize(a)+" -> "+JsonSerializer.Serialize(b));}
        Compare("Title",latest.Title,s.Title);Compare("Summary",latest.Summary,s.Summary);Compare("BodyTextSha",H(Text(latest.BodyHtml)),H(Text(s.BodyHtml)));Compare("Author",latest.Author,s.Author);
        Compare("SourceDate",new{c.OriginalDateText,c.SourcePublishedDate,c.PublishedAtUtc,c.DatePrecision},new{OriginalDateText=s.OriginalDateText,SourcePublishedDate=s.PublishedDate,PublishedAtUtc=s.PublishedUtc,s.DatePrecision});
        Compare("Categories",latest.Categories.Select(x=>x.CategoryId).Order(),p.Categories);Compare("PrimaryCategory",latest.PrimaryCategoryId,p.Primary);
        var hashes=await db.MediaAssets.Where(x=>latest.Assets.Select(a=>a.MediaAssetId).Contains(x.Id)).Select(x=>x.Sha256).ToListAsync();Compare("Assets",hashes.Order(),p.Assets.Select(x=>x.Capture.Sha256).Order());
        Compare("Metadata",latest.Metadata,SqlImporter.BatchSourceMetadata(s));
        // Compare retained HTML structure/captions after resolving media ids by hash.
        string Shape(string html){var doc=SourceParser.Document(html);foreach(var el in doc.QuerySelectorAll("img"))el.SetAttribute("src","asset");return new HtmlPolicy().Clean(doc.Body?.InnerHtml??"");}
        Compare("BodyMarkupSha",H(Shape(latest.BodyHtml)),H(Shape(p.Body)));
        return diff;
    }
    public async Task<SourceBatchPlan> PreviewAsync(IReadOnlyDictionary<string,List<OptionalImageReview>>? optionalImages=null)
    {
        await Guard(false);var plan=new SourceBatchPlan{InventoryId=state.Id,CheckpointSha256=checkpointHash,Database=config["Sql:ExpectedDatabase"]!,RecoveryProofSha256=recoveryProofHash,SourceResourcePolicySha256=SourceResourcePolicy.Read(config)?.Sha,TargetMediaRootSha256=MediaRootIdentity(storage)};
        foreach(var item in state.Items.OrderBy(x=>x.Source.Kind).ThenBy(x=>x.Source.SourceId,StringComparer.Ordinal))
        {
            var s=item.Source;var row=new SourceBatchRow{Kind=s.Kind,SourceId=s.SourceId,SourceUrl=s.SourceUrl,Aliases=item.Aliases.Append(s.CanonicalUrl).Distinct().Order().ToList()};plan.Rows.Add(row);
            row.OptionalImages=optionalImages?.GetValueOrDefault(s.Kind+":"+s.SourceId)??[];
            var record=await db.SourceRecords.AsNoTracking().SingleOrDefaultAsync(x=>x.Source==Source&&x.Kind==s.Kind&&x.SourceId==s.SourceId);
            row.ExistingCmsId=record?.ContentId;row.ExistingStamp=await Stamp(record);
            try{
                var p=await PrepareAsync(item,row.OptionalImages);row.SourceFingerprint=p.Fingerprint;row.Validation.AddRange(p.Errors);row.Findings=p.Findings;
                if(record is not null)row.Differences=await Differences(p,record);
                row.Classification=record is null?"New":row.Differences.Count==0?"ExistingUnchanged":"ExistingChanged";
                foreach(var alias in row.Aliases){
                    if(InventoryDiscovery.Classify(alias,s.SourceUrl) is not {Role:"Detail"})row.Validation.Add("UNVERIFIED_ALIAS:"+alias);
                    var hash=H(alias);var existing=await db.SourceUrlAliases.AsNoTracking().SingleOrDefaultAsync(x=>x.UrlHash==hash);
                    if(existing is not null&&(existing.Url!=alias||existing.ContentId!=record?.ContentId))row.Validation.Add("ALIAS_COLLISION:"+alias);
                }
                row.Action=row.Validation.Count>0?"Blocked":record is null?(plan.Database=="LandPortal_Test"?"CreatePublishedTest":"CreateNeedsApproval"):row.Classification=="ExistingUnchanged"?"Keep":"SourceComparisonOnly";
                foreach(var error in row.Validation.Where(x=>!p.Errors.Contains(x)))row.Findings.Add(BatchReadiness.Finding(s,error));
            }catch(Exception e)when(e is InvalidDataException or IOException or InvalidOperationException){row.Classification="ManualReview";row.Validation.Add(e.GetType().Name+": "+e.Message);row.Findings.Add(BatchReadiness.Finding(s,e.GetType().Name+": "+e.Message));}
        }
        BatchAliasPolicy.BlockConflicts(plan.Rows);
        return plan;
    }
    public async Task<List<SourceBatchOutcome>> CommitAsync(SourceBatchPlan plan,string planHash,int stopAfter=0,Func<int,Task>? beforeCommit=null,Func<Task>? afterCommit=null)
    {
        await Guard(true);
        DemandStore(plan);
        if(plan.Database!=config["Sql:ExpectedDatabase"]||plan.InventoryId!=state.Id||plan.CheckpointSha256!=checkpointHash||plan.RecoveryProofSha256!=recoveryProofHash||plan.SchemaVersion==1&&plan.Rows.Any(x=>x.OptionalImages.Count>0))throw new InvalidDataException("Preview target/session/proof differs.");
        if(plan.Database=="LandPortal_Dev")await execution!.DemandAsync(db,config,workspace,plan,planHash,"Apply");
        var outcomes=new List<SourceBatchOutcome>();var completed=0;
        if(plan.Rows.Count!=state.Items.Count||plan.Rows.GroupBy(x=>(x.Kind,x.SourceId)).Any(x=>x.Count()!=1))throw new InvalidDataException("Incomplete or duplicate batch rows.");
        var reconciliation=await MigrationDiagnostics.StepAsync("commit.reconcile",()=>ReconcileAsync(plan,planHash),plan.BatchId,plan.Rows.Count(x=>x.Action!="Blocked"));
        if(reconciliation.Status=="NEEDS_RECONCILIATION")return [new("Batch:"+plan.BatchId,"NeedsReconciliation",null,null,string.Join(";",reconciliation.Errors))];
        foreach(var row in plan.Rows)
        {
            var identity=row.Kind+":"+row.SourceId;
            if(row.Action=="Blocked"){outcomes.Add(new(identity,"Blocked",row.ExistingCmsId,null,string.Join(";",row.Validation)));continue;}
            // Source file hashing and source parsing occur before the serializable SQL transaction.
            var item=state.Items.Single(x=>x.Source.Kind==row.Kind&&x.Source.SourceId==row.SourceId);
            var p=await PrepareAsync(item,row.OptionalImages);
            if(p.Errors.Count>0||p.Fingerprint!=row.SourceFingerprint){outcomes.Add(new(identity,"Failed",row.ExistingCmsId,null,"Source/scan differs from validated preview."));break;}
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var commitSent=false;
            try{
                // Global batch import lock serializes competing batches, alias and hash materialization.
                await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'LandPortal.Phase3B.Import',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @result<0 THROW 51001,'Batch import lock unavailable',1;");
                var run=await db.ImportRuns.SingleOrDefaultAsync(x=>x.Id==plan.BatchId);
                // A missing tracking row describes checkpoint replay, not the origin of its bytes.
                // Keep existing acquisition/fixture provenance intact; never infer it from a missing row.
                if(!await db.CrawlRuns.AnyAsync(x=>x.Id==state.Id))db.CrawlRuns.Add(new(){Id=state.Id,Status="Phase3BCheckpointReplay",Checkpoint="Checkpoint replay; inventory="+state.Id+"; sha256="+checkpointHash});
                if(run is null){run=new(){Id=plan.BatchId,CrawlRunId=state.Id,Status="Phase3BRunning"};db.ImportRuns.Add(run);}
                else if(run.CrawlRunId!=state.Id)throw new InvalidDataException("Batch id belongs to another inventory.");
                var key=plan.BatchId+"|"+planHash+"|"+identity;
                var audit=await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(x=>x.Action=="Phase3B.Import"&&x.Detail==key);
                if(audit is not null){outcomes.Add(new(identity,"AlreadyCommitted",audit.ContentId,audit.RevisionId));await tx.CommitAsync();db.ChangeTracker.Clear();continue;}
                if(await db.AuditLogs.AnyAsync(x=>x.Action=="Phase3B.Import"&&x.Detail.StartsWith(plan.BatchId.ToString()+"|")&&!x.Detail.StartsWith(plan.BatchId+"|"+planHash+"|")))throw new InvalidDataException("Batch id already bound to another plan hash.");
                var record=await db.SourceRecords.Include(x=>x.Content).SingleOrDefaultAsync(x=>x.Source==Source&&x.Kind==row.Kind&&x.SourceId==row.SourceId);
                if(await Stamp(record)!=row.ExistingStamp)throw new DBConcurrencyException("CMS changed since preview; make a new preview.");
                var expected=record is null?(plan.Database=="LandPortal_Test"?"CreatePublishedTest":"CreateNeedsApproval"):(await Differences(p,record)).Count==0?"Keep":"SourceComparisonOnly";
                if(row.Action!=expected)throw new InvalidDataException("Preview action does not match current evidence.");
                if(!row.Aliases.SequenceEqual(item.Aliases.Append(item.Source.CanonicalUrl).Distinct().Order()))throw new InvalidDataException("Alias list differs from checkpoint.");
                var result=await new SqlImporter(db,storage).ImportValidatedBatchItemAsync(p,row.Aliases,record,plan.BatchId,key,PrivateRoot,row.Action=="Keep");
                if(expected=="Keep")run.Unchanged++;else run.Imported++;
                run.Status="Phase3BPartial";
                if(beforeCommit is not null)await beforeCommit(completed);
                await db.SaveChangesAsync();MigrationDiagnostics.Record("COMMIT_SENT",new{plan.BatchId,identity});commitSent=true;
                await tx.CommitAsync();if(afterCommit is not null)await afterCommit();MigrationDiagnostics.Record("COMMIT_ACKNOWLEDGED",new{plan.BatchId,identity});outcomes.Add(new(identity,expected,result.ContentId,result.RevisionId));completed++;db.ChangeTracker.Clear();
                if(stopAfter>0&&completed>=stopAfter)break;
            }catch(Exception e){
                var uncertain=commitSent;
                if(!commitSent){try{await tx.RollbackAsync();MigrationDiagnostics.Record("ROLLBACK_CONFIRMED",new{plan.BatchId,identity});}catch{uncertain=true;}}
                MigrationDiagnostics.Record(uncertain?"NEEDS_RECONCILIATION":"FAILED_ROLLED_BACK",new{plan.BatchId,identity,error=e.GetType().Name});
                db.ChangeTracker.Clear();outcomes.Add(new(identity,uncertain?"NeedsReconciliation":"Failed",row.ExistingCmsId,null,e.GetType().Name+"; reconcile by batch id before retry."));break;
            }
        }
        return outcomes;
    }
    public async Task<List<string>> VerifyAsync(SourceBatchPlan plan,string planHash)
    {
        await Guard(false);DemandStore(plan);if(plan.Database!=config["Sql:ExpectedDatabase"]||plan.RecoveryProofSha256!=recoveryProofHash)throw new InvalidDataException("Verify target/proof differs.");var errors=new List<string>();
        var batchPrefix=plan.BatchId+"|"+planHash+"|";
        var receipts=await db.AuditLogs.AsNoTracking().Where(x=>x.Action=="Phase3B.Import"&&x.Detail.StartsWith(batchPrefix)).Select(x=>new{x.Detail,x.ContentId,x.RevisionId}).Take(plan.Rows.Count+1).ToListAsync();
        if(receipts.Count>plan.Rows.Count||receipts.GroupBy(x=>x.Detail).Any(x=>x.Count()!=1))return ["Duplicate or excess batch receipts."];
        foreach(var row in plan.Rows.Where(x=>x.Action!="Blocked")){
            var identity=row.Kind+":"+row.SourceId;var key=plan.BatchId+"|"+planHash+"|"+identity;
            var a=receipts.SingleOrDefault(x=>x.Detail==key);
            if(a is null){errors.Add("NotCommitted:"+identity);continue;}
            var p=await PrepareAsync(state.Items.Single(x=>x.Source.Kind==row.Kind&&x.Source.SourceId==row.SourceId),row.OptionalImages);
            if(p.Errors.Count>0)errors.AddRange(p.Errors);
            var c=await db.Contents.AsNoTracking().SingleAsync(x=>x.Id==a.ContentId);
            if(!await db.SourceRecords.AnyAsync(x=>x.Source==Source&&x.Kind==row.Kind&&x.SourceId==row.SourceId&&x.ContentId==c.Id))errors.Add("SourceIdentity:"+identity);
            var rev=await db.Revisions.AsNoTracking().Include(x=>x.Assets).Include(x=>x.Categories).SingleAsync(x=>x.Id==a.RevisionId);
            if(rev.ContentId!=c.Id)errors.Add("ReceiptRevisionOwner:"+identity);
            if(row.Action!="Keep"&&rev.ContentHash!=row.SourceFingerprint)errors.Add("RevisionFingerprint:"+identity);
            if(Text(rev.BodyHtml)!=Text(p.Item.Source.BodyHtml))errors.Add("TextMismatch:"+identity);
            if(rev.Title!=p.Item.Source.Title||rev.Summary!=p.Item.Source.Summary||rev.Author!=p.Item.Source.Author)errors.Add("TitleSummaryAuthor:"+identity);
            if(!rev.Categories.Select(x=>x.CategoryId).Order().SequenceEqual(p.Categories.Order())||rev.PrimaryCategoryId!=p.Primary)errors.Add("Taxonomy:"+identity);
            if(JsonSerializer.Serialize(rev.Metadata)!=JsonSerializer.Serialize(SqlImporter.BatchSourceMetadata(p.Item.Source)))errors.Add("Metadata:"+identity);
            if(row.Action is "CreatePublishedTest" or "CreateNeedsApproval"&&(c.PublishedRevisionId!=rev.Id||!c.IsPublic))errors.Add("Publication:"+identity);
            foreach(var asset in p.Assets){var m=await db.MediaAssets.AsNoTracking().SingleAsync(x=>x.Sha256==asset.Capture.Sha256);var file=MediaService.ResolveMediaPath(storage,m.StorageKey);if(SourceInventory.Hash(await File.ReadAllBytesAsync(file))!=asset.Capture.Sha256||m.ScanStatus!="Clean"||m.IsQuarantined)errors.Add("MediaMismatch:"+identity);}
            foreach(var alias in row.Aliases)if(!await db.SourceUrlAliases.AnyAsync(x=>x.ContentId==c.Id&&x.Url==alias))errors.Add("AliasMissing:"+alias);
        }
        return errors;
    }
    public async Task<int> RollbackPublicationAsync(SourceBatchPlan plan,string planHash)
    {
        await Guard(true);var count=0;
        DemandStore(plan);
        if(plan.Database!=config["Sql:ExpectedDatabase"]||plan.RecoveryProofSha256!=recoveryProofHash)throw new InvalidDataException("Rollback target/proof differs.");
        if(plan.Database=="LandPortal_Dev")await execution!.DemandAsync(db,config,workspace,plan,planHash,"Rollback");
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        foreach(var row in plan.Rows.Where(x=>x.Action is "CreatePublishedTest" or "CreateNeedsApproval")){
            var key=plan.BatchId+"|"+planHash+"|"+row.Kind+":"+row.SourceId;
            var audit=await db.AuditLogs.SingleOrDefaultAsync(x=>x.Action=="Phase3B.Import"&&x.Detail==key);if(audit is null)continue;
            var c=await db.Contents.SingleAsync(x=>x.Id==audit.ContentId);
            if(!c.IsPublic)continue;
            if(c.HasEditorialChanges||c.CurrentRevisionId!=audit.RevisionId||c.PublishedRevisionId!=audit.RevisionId||c.EditGeneration!=0)throw new DBConcurrencyException("Edited batch content; manual rollback required.");
            c.IsPublic=false;c.State=WorkflowState.Unpublished;c.PublishedRevisionId=null;count++;
            db.AuditLogs.Add(new(){Actor="phase3b-cli",Action="Phase3B.RollbackPublication",ContentId=c.Id,RevisionId=audit.RevisionId,Detail=key});
        }
        await db.SaveChangesAsync();await tx.CommitAsync();db.ChangeTracker.Clear();return count;
    }
}
