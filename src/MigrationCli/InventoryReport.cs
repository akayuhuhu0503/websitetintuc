using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.MigrationCli;

public sealed partial class SourceInventory
{
    public async Task ScanNewPublicFilesAsync(CancellationToken ct=default)
    {
        RequireTest(config);await SynchronizationGuard.EnsureEnabledAsync(db,config);db.ChangeTracker.Clear();
        var scanner=InquiryScannerFactory.Create(config,"Development",32*1024*1024);
        var allowed=config["Scanner:Provider"]=="DefenderTest"?(config["Scanner:DefenderAllowedTestHashes"]??"").Split(',',StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase):null;
        foreach(var node in state.Urls.Where(x=>x.Role=="File"&&x.Status=="PrivateDownloaded"&&x.FormatStatus=="Validated"&&x.ScanStatus is "NotScanned" or "ScanFailed"))
        {
            // Explicit scan chunks leave unselected public bytes NotScanned; no denied scan is synthesized.
            if(allowed is not null&&!allowed.Contains(node.CaptureSha??""))continue;
            // Existing 53 assets, and their copied baseline bytes, are never enlisted by this command.
            var capture=state.Captures.LastOrDefault(x=>x.Url==node.Url&&x.Sha256==node.CaptureSha&&x.Status==200);
            if(capture is null||capture.Origin!="Network")continue;
            var path=PathFor(capture.Key);NoReparse(path);
            await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            var before=Convert.ToHexStringLower(await SHA256.HashDataAsync(input,ct));input.Position=0;
            if(before!=node.CaptureSha)throw new InvalidDataException("Scan input no longer matches captured bytes.");
            var health=await scanner.HealthAsync(ct);
            var result=health.Ready?await scanner.ScanAsync(input,ct):new InquiryScanResult(InquiryScanState.ScanFailed,health.Code,null);
            input.Position=0;var after=Convert.ToHexStringLower(await SHA256.HashDataAsync(input,ct));
            if(before!=after)throw new InvalidDataException("Scan input changed during scan.");
            if(result.State==InquiryScanState.Clean&&string.IsNullOrWhiteSpace(result.EngineVersion))throw new InvalidDataException("A Clean conclusion without genuine engine proof is refused.");
            node.ScanStatus=result.State.ToString();node.ScannedSha256=before;node.ScannedUtc=DateTimeOffset.UtcNow;node.ScanEngine=result.EngineVersion;
            if(result.State!=InquiryScanState.Clean)Issue(node.Url,"SCAN_"+node.ScanStatus,result.Error);
            await NormalizeAllAsync();await SaveAsync();
            Console.WriteLine($"Public-byte scan {before}: {node.ScanStatus}; engine={node.ScanEngine}");
        }
    }
    public async Task WriteReportAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var sqlSnapshots=await db.SourceSnapshots.CountAsync(x=>x.CrawlRunId==state.Id);
        var sqlUrls=await db.CrawlItems.CountAsync(x=>x.CrawlRunId==state.Id);
        var categories=SourceCatalog.Categories.Select(c=>new{c.Id,c.Name,SourceUrl=SourceCatalog.BaseUrl+c.Path,
            DiscoveredDetailUrls=state.Urls.Count(x=>x.Role=="Detail"&&x.CategoryIds.Contains(c.Id)),
            FetchedListPages=state.Lists.Count(x=>x.CategoryId==c.Id),
            ObservedCandidateIds=state.Lists.Where(x=>x.CategoryId==c.Id).SelectMany(x=>x.Ids).Distinct().Count(),
            VerifiedStagedIdentities=state.Items.Count(x=>x.Source.CategoryIds.Contains(c.Id)),
            Declaration=state.Lists.Where(x=>x.CategoryId==c.Id).Select(x=>new{x.DeclaredPages,x.DeclaredRecords}).Distinct().ToArray(),
            Pagination=state.Lists.Where(x=>x.CategoryId==c.Id).Select(x=>new{x.Url,x.Page,x.Result}).ToArray(),
            WholeSourceCount=(int?)null}).ToArray();
        var files=state.Urls.Where(x=>x.Role=="File").ToArray();
        var report=new{
            SchemaVersion=3,RunId=state.Id,GeneratedUtc=DateTimeOffset.UtcNow,state.Status,state.ApprovalReference,
            Scope="Phase3A private collection/staging; no CMS publication/import; SQL Test only",
            WholeSourceTotal=(int?)null,WholeSourceCoverage="UNKNOWN; exhausted frontier is not 100% coverage",
            Discovery=new{Urls=state.Urls.Count,ByRole=state.Urls.GroupBy(x=>x.Role).Select(g=>new{Role=g.Key,Urls=g.Count()}),
                CandidateIdentities=state.Urls.Where(x=>x.Role=="Detail").Select(x=>(x.Kind,x.CandidateId)).Distinct().Count(),
                VerifiedIdentities=state.Items.Count,AliasUrlsBeyondOne=state.Items.Sum(x=>Math.Max(0,x.Aliases.Count-1))},
            Processing=new{SqlSnapshotRows=sqlSnapshots,SqlUrlRows=sqlUrls,CaptureEntries=state.Captures.Count,ByStatus=state.Urls.GroupBy(x=>x.Status).Select(g=>new{Status=g.Key,Count=g.Count()}),
                CapturedPages=state.Urls.Count(x=>x.Role!="File"&&x.CaptureSha!=null),UnfetchedPages=state.Urls.Count(x=>x.Role!="File"&&x.CaptureSha==null&&x.Status!="OutOfScope"),
                Extracted=state.Items.Count,ByKind=Enum.GetValues<ContentKind>().Select(k=>new{Kind=k.ToString(),UrlCandidates=state.Urls.Count(x=>x.Role=="Detail"&&x.Kind==k),Extracted=state.Items.Count(x=>x.Source.Kind==k)}),Categories=categories},
            Resources=new{DiscoveredUrls=files.Length,TransferredUsable=files.Count(x=>x.FormatStatus=="Validated"&&x.ScanStatus=="Clean"&&x.ScannedSha256==x.CaptureSha),
                PrivateDownloadedUrls=files.Count(x=>x.Status=="PrivateDownloaded"),UniquePrivateFiles=files.Where(x=>x.Status=="PrivateDownloaded").Select(x=>x.CaptureSha).Distinct().Count(),
                ByExtension=files.GroupBy(x=>Path.GetExtension(new Uri(x.Url).AbsolutePath).ToLowerInvariant()).Select(g=>new{Extension=g.Key,Discovered=g.Count(),PrivateDownloaded=g.Count(x=>x.Status=="PrivateDownloaded"),Clean=g.Count(x=>x.ScanStatus=="Clean"&&x.ScannedSha256==x.CaptureSha)}),
                VideoMetadata=state.Items.Count(x=>x.Source.Kind==ContentKind.Video),VideoFiles=files.Count(x=>x.Mime?.StartsWith("video/")==true&&x.FormatStatus=="Validated"&&x.ScanStatus=="Clean"),
                Unavailable=files.Where(x=>x.Status is "HttpError" or "ValidationFailed" or "OutOfScope" or "Failed").Select(x=>new{x.Url,x.Status,x.Error}),
                Policy="All blobs private/quarantine; no MediaAsset/VideoFile created. Original 53 NotScanned assets are untouched."},
            Requests=new{ReservedGetAttempts=state.RequestsReserved,CompletedNetworkResponses=state.Captures.Count(x=>x.Origin=="Network"),state.ReceivedBytes,
                ReusedBaselineCaptures=state.Captures.Count(x=>x.Origin.StartsWith("Legacy")),
                Note="Reservations are persisted before GET (including redirects); an interrupted reservation need not have reached the source. Browser/web inspection counted separately."},
            Pagination=state.Lists,LegacyExceptions=new{HistoryEntries=state.LegacyIssues.Count,OpenEntries=state.LegacyIssues.Count(x=>!x.Resolved),
                DistinctOpen=state.LegacyIssues.Where(x=>!x.Resolved).Select(x=>(x.SourceUrl,x.Stage,x.Code)).Distinct().Count(),Details=state.LegacyIssues},
            NewIssueHistory=state.Issues,CurrentErrorUrls=state.Urls.Count(x=>x.Status is "HttpError" or "ValidationFailed" or "Failed"),Urls=state.Urls.Select(x=>new{x.Url,x.Role,Kind=x.Kind.ToString(),x.CandidateId,x.CategoryIds,x.FoundOn,x.Status,x.Attempts,x.AvailableUtc,x.Error,x.CaptureSha,x.Mime,x.FormatStatus,x.ScanStatus,x.ScannedSha256,x.ScanEngine,x.ScannedUtc}),
            Items=state.Items.Select(x=>new{Kind=x.Source.Kind.ToString(),x.Source.SourceId,x.Source.Title,x.Source.SourceUrl,x.Source.CanonicalUrl,x.Source.OriginalDateText,x.Source.DatePrecision,x.Source.Author,x.Source.CategoryIds,x.Source.AssetUrls,x.ContentHash,x.RawSha256,x.Status,x.Aliases}),
            Next="Review access authority and coverage gaps before broad crawl; Phase3B needs scanned-byte policy, batch comparison, approved source totals/redirect acceptance. No publication from this manifest."};
        await CreateReportFile(folder,"inventory.json",JsonSerializer.Serialize(report,InventoryLimits.Json));
        var csv=new StringBuilder("role,kind,url,source_id_candidate,status,attempts,sha256,mime,format,scan,error\n");
        foreach(var x in state.Urls)csv.AppendLine(string.Join(",",new[]{x.Role,x.Kind.ToString(),x.Url,x.CandidateId,x.Status,x.Attempts.ToString(),x.CaptureSha??"",x.Mime??"",x.FormatStatus??"",x.ScanStatus,x.Error??""}.Select(Csv)));
        await CreateReportFile(folder,"urls.csv",csv.ToString());
        var rows=string.Join("",categories.Select(c=>"<tr><td>"+c.Id+"</td><td>"+WebUtility.HtmlEncode(c.Name)+"</td><td>"+c.DiscoveredDetailUrls+"</td><td>"+c.FetchedListPages+"</td><td>"+c.VerifiedStagedIdentities+"</td><td>Chưa xác định</td></tr>"));
        await CreateReportFile(folder,"inventory.html","<!doctype html><html lang=vi><meta charset=utf-8><meta name=viewport content='width=device-width'><meta http-equiv=Content-Security-Policy content=\"default-src 'none'; style-src 'unsafe-inline'\"><title>Kiểm kê nguồn Phase3A</title><style>body{font:16px system-ui;max-width:1200px;margin:2rem auto;padding:1rem;color:#15374b}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ccd6df;padding:.5rem}main{overflow:auto}</style><h1>Kiểm kê nguồn QLDD — Phase3A</h1><p>SQL Test · staging riêng tư · chưa chuyển toàn bộ · CHƯA PRODUCTION READY</p><p>URL phát hiện: "+state.Urls.Count+" · Định danh đã xác minh: "+state.Items.Count+" · GET đã dành ngân sách: "+state.RequestsReserved+"</p><p>Trạng thái: "+WebUtility.HtmlEncode(state.Status)+". Tổng đối chứng toàn nguồn: CHƯA XÁC ĐỊNH. Đọc inventory.json/urls.csv để đối soát từng URL và lỗi.</p><main><table><tr><th>ID</th><th>Chuyên mục</th><th>URL chi tiết</th><th>Trang danh sách</th><th>Đã bóc tách</th><th>Tổng nguồn</th></tr>"+rows+"</table></main></html>");
    }
    private static string Csv(string v)=>"\""+(v.Length>0&&(v[0] is '=' or '+' or '-' or '@' or '\t' or '\r')?"'":"")+v.Replace("\"","\"\"")+"\"";
    private static async Task CreateReportFile(string folder,string name,string text)
    { await using var f=new FileStream(Path.Combine(folder,name),FileMode.CreateNew,FileAccess.Write,FileShare.None);await f.WriteAsync(Encoding.UTF8.GetBytes(text)); }
}
