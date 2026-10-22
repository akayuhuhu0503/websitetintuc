using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;

namespace LandPortal.MigrationCli;

public sealed partial class SourceInventory
{
    private async Task ProcessAsync(InventoryUrl node)
    {
        var reservedBefore=state.RequestsReserved;var oldAttempts=node.Attempts;
        try
        {
            var prior=state.Captures.LastOrDefault(x=>x.Url==node.Url&&x.Status==200);
            var capture=prior??state.Captures.LastOrDefault(x=>x.Url==node.Url);
            if(capture is null||node.Refresh||node.Status=="Retry")
            {
                if(limits.Offline)return;
                if(approval is not null&&(state.Urls.Count(x=>x.Role!="File"&&x.Attempts>0)>=approval.MaximumPages||state.Items.Count>=approval.MaximumContents||state.ReceivedBytes>=approval.MaximumBytes))throw new RequestBudgetException();
                if(node.Role!="Robots"&&state.RobotsState is "Blocked" or "Unavailable")throw new InvalidDataException("Robots not available for a safe online run.");
                node.Status="Fetching";node.Attempts++;await SaveAsync();
                var remaining=Math.Min(limits.Bytes-(state.ReceivedBytes-bytesAtStart),approval is null?long.MaxValue:approval.MaximumBytes-state.ReceivedBytes);
                if(remaining<1)throw new RequestBudgetException();
                var max=(int)Math.Min(remaining,node.Role=="File"?32L*1024*1024:8L*1024*1024);
                SourceResponse response;
                if(fixtureTransport is not null){await Reserve(node.Url);response=await fixtureTransport(node.Url,max,operationToken,node.Role!="File",node.ETag,node.LastModified);if(response.Bytes.Length>max)throw new InvalidDataException("Fixture exceeded byte budget.");}
                else response=await network!.FetchAsync(node.Url,max,operationToken,node.Role!="File",node.ETag,node.LastModified);
                state.ReceivedBytes+=response.Bytes.LongLength;
                var key=await Blob(response.Bytes,node.Role=="File"?"quarantine":"responses");
                var received=new InventoryCapture(node.Url,response.Url,response.Status,response.ContentType,key,Hash(response.Bytes),response.Bytes.LongLength,response.FetchedUtc,fixtureTransport is null?"Network":"FixtureTransport");
                state.Captures.Add(received);node.ETag=response.ETag??node.ETag;node.LastModified=response.LastModified??node.LastModified;node.Refresh=false;
                if(response.Status==304)
                {
                    if(prior is null)throw new InvalidDataException("304 without a verified prior response.");
                    capture=prior;
                }
                else capture=received;
                await SaveAsync();
                if(response.Status==429||response.Status>=500)
                {
                    node.AvailableUtc=response.RetryAfterUtc??DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2,node.Attempts));
                    node.Status=node.Attempts>=3?"Failed":"Retry";node.Error="HTTP "+response.Status;Issue(node.Url,"HTTP_RETRY",node.Error);await SaveAsync();return;
                }
            }
            if(capture is null)throw new InvalidDataException("No response captured.");
            NoReparse(PathFor(capture.Key));var bytes=await File.ReadAllBytesAsync(PathFor(capture.Key),operationToken);
            if(Hash(bytes)!=capture.Sha256||bytes.LongLength!=capture.Bytes)throw new InvalidDataException("Snapshot/file hash differs from captured proof.");
            if(node.CaptureSha!=capture.Sha256){node.ScanStatus="NotScanned";node.ScannedSha256=null;node.ScanEngine=null;node.ScannedUtc=null;node.FormatStatus=null;}
            node.CaptureSha=capture.Sha256;
            if(capture.Status!=200)
            {
                node.Status="HttpError";node.Error="HTTP "+capture.Status;Issue(node.Url,"HTTP_ERROR",node.Error);
                if(node.Role=="Robots")state.RobotsState=capture.Status==404?"Missing404UnknownRules":"Unavailable";
                await SaveAsync();return;
            }
            if(node.Role=="File")
            {
                assets++;ValidateFile(node,bytes);node.Status="PrivateDownloaded";
            }
            else
            {
                if(node.Role=="Detail")details++;else pages++;
                var html=new UTF8Encoding(false,true).GetString(bytes);
                if(node.Role=="Robots")
                {
                    if(html.Contains("<html",StringComparison.OrdinalIgnoreCase)||(!html.Contains("User-agent:",StringComparison.OrdinalIgnoreCase)&&!html.TrimStart().StartsWith('#')))throw new InvalidDataException("robots response is not a verified robots text document.");
                    state.RobotsText=html;state.RobotsState=InventoryDiscovery.RobotsAllows(html,SourceCatalog.BaseUrl+"/")?"Valid":"Blocked";
                    foreach(Match m in Regex.Matches(html,@"(?im)^Sitemap:\s*(\S+)"))Add(m.Groups[1].Value,node.Url);
                }
                else if(node.Role=="Feed")foreach(var link in InventoryDiscovery.FeedLinks(html))Add(link,node.Url);
                else
                {
                    var doc=SourceParser.Document(html);
                    foreach(var anchor in doc.QuerySelectorAll("a[href]"))
                    {
                        var raw=anchor.GetAttribute("href")!;
                        if(raw=="#"||raw.Length==0)
                        {
                            if(anchor.TextContent.Trim().Contains("Cơ cấu tổ chức",StringComparison.OrdinalIgnoreCase))Issue(node.Url,"MENU_WITHOUT_TARGET","Cơ cấu tổ chức has no usable target; do not invent its content.");
                            continue;
                        }
                        Add(raw,node.Url);
                    }
                    if(node.Role=="List")ReadList(node,html);
                    else if(node.Role=="Detail")Extract(node,html,capture);
                    else if(node.Role=="Other")Issue(node.Url,"UNSUPPORTED_PAGE_ADAPTER","Page captured for inventory; no verified business-body adapter, not counted as an extracted article.");
                }
                node.Status=node.Role=="Detail"?"PrivateStaged":"Captured";
            }
            node.Error=null;await NormalizeAllAsync();await SaveAsync();
        }
        catch(RequestBudgetException)
        {
            node.Status="Discovered";if(state.RequestsReserved==reservedBefore)node.Attempts=oldAttempts;throw;
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex)when(ex is IOException or HttpRequestException or InvalidDataException or JsonException or System.Xml.XmlException or InvalidOperationException or MediaUploadException)
        {
            if(node.Role=="File")node.FormatStatus="Rejected";
            node.Status=ex is HttpRequestException||ex is IOException&&ex is not InvalidDataException ?(node.Attempts>=3?"Failed":"Retry"):"ValidationFailed";
            node.Error=ex.GetType().Name+": "+ex.Message;node.AvailableUtc=DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2,Math.Min(node.Attempts,3)));
            if(node.Role=="Robots")state.RobotsState="Unavailable";
            Issue(node.Url,"PROCESS_ERROR",node.Error);await SaveAsync();
        }
    }
    private void ReadList(InventoryUrl node,string html)
    {
        var paging=SourceParser.ReadPagination(html);var category=node.CategoryIds.Cast<int?>().FirstOrDefault();
        List<string> ids;
        if(node.Kind is ContentKind.LegalDocument or ContentKind.DraftDocument)
        {
            ids=SourceParser.EmbeddedDocumentIds(html);
            foreach(var id in ids)Add(SourceCatalog.BaseUrl+(node.Kind==ContentKind.DraftDocument?"/chi-tiet-van-ban-du-thao-":"/van-ban-")+id+".htm",node.Url);
        }
        else if(node.Kind==ContentKind.Video)
        {
            var m=Regex.Match(html,@"var\s+lisstData\s*=\s*(\{[^\r\n]+\});");ids=[];
            if(m.Success){using var json=JsonDocument.Parse(m.Groups[1].Value);ids=json.RootElement.GetProperty("data").EnumerateArray().Select(x=>x.GetProperty("ID").GetInt32().ToString()).ToList();}
            foreach(var id in ids)Add(SourceCatalog.BaseUrl+"/video-"+id+".htm",node.Url);
        }
        else
        {
            var links=SourceParser.DiscoverNewsLinks(html,node.Url,category).ToList();ids=links.Select(x=>x.SourceId).Distinct().ToList();
            foreach(var link in links)Add(link.Url,node.Url,category);
        }
        var recordMatch=Regex.Match(html,@"[""']?recordsTotal[""']?\s*:\s*(\d+)");int? records=recordMatch.Success?int.Parse(recordMatch.Groups[1].Value):null;
        var first=node.ListRoot is null?null:state.Lists.LastOrDefault(x=>x.Url==node.ListRoot);
        var result=ids.Count==0?"NoAccessibleRowsUnknown":node.Page==paging.TotalPages&&paging.TotalPages>0?"DeclaredTerminal":"Observed";
        if(node.Page>1&&first is not null&&ids.Count>0&&!ids.Except(first.Ids).Any())result="RepeatedFirstPage";
        state.Lists.RemoveAll(x=>x.Url==node.Url);state.Lists.Add(new(node.Url,category,node.Page,paging.TotalPages,records,ids,result,paging.NextTemplate));
        if(result is "NoAccessibleRowsUnknown" or "RepeatedFirstPage")Issue(node.Url,"PAGINATION_"+result.ToUpperInvariant(),"Enumeration remains incomplete; no source count is inferred.");
        if(result=="RepeatedFirstPage")return;
        if(paging.TotalPages>1&&paging.NextTemplate.Length==0)Issue(node.Url,"PAGINATION_TEMPLATE_UNKNOWN","Declared pages but no verified navigation template.");
        if(paging.TotalPages>10000){Issue(node.Url,"PAGINATION_LIMIT","Source-declared page count exceeds a finite safe bound.");return;}
        if(node.Page<paging.TotalPages&&paging.NextTemplate.Length>0&&ids.Count>0)
        {
            var next=paging.NextTemplate.Replace("{page}",(node.Page+1).ToString());
            var added=Add(next,node.Url,category,node.ListRoot??node.Url);
            if(added is not null&&added.Role!="List")throw new InvalidDataException("Pagination did not produce a recognized read-only list route.");
        }
    }
    private void Extract(InventoryUrl node,string html,InventoryCapture capture)
    {
        var parsed=SourceParser.Parse(html,node.Url,node.Kind,node.CandidateId);
        var canonical=SourceParser.Document(html).QuerySelector("link[rel=canonical]")?.GetAttribute("href");
        if(canonical is null||!Uri.TryCreate(new Uri(node.Url),canonical,out var uri)||uri.Host!="qldd.mae.gov.vn"||!Regex.IsMatch(uri.Query,@"[?&]ItemID=\d+(?:&|$)",RegexOptions.IgnoreCase)||InventoryDiscovery.Classify(uri.AbsoluteUri,node.Url) is null)throw new InvalidDataException("No verified source canonical ItemID; URL suffix remains a candidate, not an identity.");
        if(node.Kind!=parsed.Kind){Issue(node.Url,"CANONICAL_KIND_CORRECTED",$"{node.Kind} -> {parsed.Kind}; rawSHA256={capture.Sha256}");node.Kind=parsed.Kind;}
        foreach(var cat in node.CategoryIds)if(!parsed.CategoryIds.Contains(cat))parsed.CategoryIds.Add(cat);
        foreach(var asset in parsed.AssetUrls)Add(asset,node.Url);
        if(parsed.Kind==ContentKind.Video&&parsed.Fields.TryGetValue("VideoSource",out var video)&&video.Length>0)Add(video,node.Url);
        foreach(var warning in parsed.Warnings)Issue(node.Url,"SOURCE_WARNING",warning);
        var item=state.Items.FirstOrDefault(x=>x.Source.Kind==parsed.Kind&&x.Source.SourceId==parsed.SourceId);
        if(item is null){item=new(){Source=parsed};state.Items.Add(item);}else{foreach(var cat in item.Source.CategoryIds)if(!parsed.CategoryIds.Contains(cat))parsed.CategoryIds.Add(cat);item.Source=parsed;}
        if(!item.Aliases.Contains(node.Url))item.Aliases.Add(node.Url);if(!item.Aliases.Contains(parsed.CanonicalUrl))item.Aliases.Add(parsed.CanonicalUrl);
        item.RawSha256=capture.Sha256;
        item.ContentHash=HashText(string.Join("\n",parsed.Title,parsed.Summary,parsed.BodyHtml,parsed.Author,parsed.OriginalDateText,JsonSerializer.Serialize(parsed.Fields.OrderBy(x=>x.Key).ToArray()),string.Join(",",parsed.CategoryIds.Order())));
        node.CandidateId=parsed.SourceId;
    }
    private void ValidateFile(InventoryUrl node,byte[] bytes)
    {
        var type=SampleCollector.DetectType(bytes);var extension=Path.GetExtension(new Uri(node.Url).AbsolutePath).ToLowerInvariant();
        node.Mime=type?.Mime??(extension==".docx"?OfficeDocumentPolicy.Docx:extension==".xlsx"?OfficeDocumentPolicy.Xlsx:null);
        if(node.Mime is null)throw new InvalidDataException("Unsupported file/container; response retained privately, never declared transferred/usable.");
        var capture=state.Captures.Last(x=>x.Url==node.Url&&x.Sha256==node.CaptureSha);
        var storage=new StorageOptions{MediaRoot=root,SnapshotRoot=root,TempRoot=root,KeysRoot=root};var media=new MediaService(db,storage,new());
        if(OfficeDocumentPolicy.IsDocument(node.Mime))OfficeDocumentPolicy.Validate(PathFor(capture.Key),node.Mime,media);
        else if(node.Mime.StartsWith("image/")&&node.Mime!="image/avif")media.ValidateContent(PathFor(capture.Key),node.Mime,operationToken);
        else if(node.Mime=="image/avif"||node.Mime.StartsWith("video/"))throw new InvalidDataException("File retained; format adapter requires a verified processor before it can be usable.");
        node.FormatStatus="Validated";
    }
    private Task NormalizeAllAsync()
    {
        foreach(var item in state.Items)
        {
            var doc=SourceParser.Document(item.Source.BodyHtml);
            foreach(var el in doc.QuerySelectorAll("[src],[href],[data-src],[srcset]"))
            {
                foreach(var attr in new[]{"src","data-src","href"})
                {
                    var raw=el.GetAttribute(attr);if(raw is null||!Uri.TryCreate(new Uri(item.Source.SourceUrl),raw,out var u))continue;
                    var file=state.Urls.FirstOrDefault(x=>x.Url==u.AbsoluteUri&&x.Role=="File");
                    if(file is not null&&file.CaptureSha is not null&&file.FormatStatus=="Validated")el.SetAttribute(attr=="data-src"?"src":attr,"/media/"+new Guid(Convert.FromHexString(file.CaptureSha).AsSpan(0,16)));
                    else if(attr is "src" or "data-src"||file is not null)el.RemoveAttribute(attr);
                }
                el.RemoveAttribute("srcset");el.RemoveAttribute("data-src");
            }
            item.NormalizedBody=new HtmlPolicy().Clean(doc.Body?.InnerHtml??"");
            item.Status=item.Source.AssetUrls.All(u=>state.Urls.Any(x=>x.Url==u&&x.FormatStatus=="Validated"&&x.ScanStatus=="Clean"&&x.ScannedSha256==x.CaptureSha))?"PrivateValidatedNoPublication":"PrivateStagedNeedsScanOrFile";
        }
        return Task.CompletedTask;
    }
}
