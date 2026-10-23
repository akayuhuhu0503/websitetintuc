using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Tests;

// Focused on this delivery's template, PDF response and scanner boundary changes.
// Genuine source rows are read only. All writes use labelled SQL Test fixtures.
public static class UatDeliveryTests
{
    static void Check(bool ok,string why)=>V05FinishTests.Check(ok,why);
    static WorkflowService Workflow(PortalDbContext db,IConfiguration config)=>new(db,new(),MediaUploadOptions.FromConfiguration(config,"Development"),StorageOptions.FromConfiguration(config));
    public static async Task Template(DbContextOptions<PortalDbContext> options,IConfiguration config)
    {
        var metadataValidator=typeof(WorkflowService).GetMethod("ValidateMetadata",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        var longTags=string.Join(',',Enumerable.Range(0,20).Select(i=>i.ToString("D2")+new string('a',22)));Check(longTags.Length==499,"Tag boundary fixture changed");
        var denied=false;try{metadataValidator.Invoke(null,[new ContentMetadata{Tags=longTags},ContentKind.Article]);}catch(System.Reflection.TargetInvocationException e)when(e.InnerException is InvalidOperationException){denied=true;}Check(denied,"Normalized tags overflowed SQL500 instead of being rejected before writes");
        await using var db=new PortalDbContext(options);
        var actor=await new CurrentPermissionService(db).GetAsync(await InquiryFinishTests.User(db,Permissions.All,"UAT delivery template technical Test"));
        var cover=await db.MediaAssets.AsNoTracking().FirstAsync(x=>x.ScanStatus=="Clean"&&!x.IsQuarantined&&x.SourceUrl!=""&&x.MimeType.StartsWith("image/"));
        var input=Phase2aSqlTests.Input("UAT delivery technical template "+Guid.NewGuid());input.Summary="";input.Author="";
        var wf=Workflow(db,config);var saved=await wf.SaveAsync(null,input,actor,null,Guid.NewGuid());
        await V05FinishTests.Denied<InvalidOperationException>(()=>wf.SubmitAsync(saved.ContentId,saved.CurrentRevisionId,actor,saved.RowVersion,Guid.NewGuid()));db.ChangeTracker.Clear();
        input.Summary="Technical sapo";saved=await wf.SaveAsync(saved.ContentId,input,actor,saved.RowVersion,Guid.NewGuid());
        await V05FinishTests.Denied<InvalidOperationException>(()=>wf.PublishDirectAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid()));db.ChangeTracker.Clear();
        input.Author="Anonymous technical author";saved=await wf.SaveAsync(saved.ContentId,input,actor,saved.RowVersion,Guid.NewGuid());
        await V05FinishTests.Denied<InvalidOperationException>(()=>wf.SubmitAsync(saved.ContentId,saved.CurrentRevisionId,actor,saved.RowVersion,Guid.NewGuid()));db.ChangeTracker.Clear();
        input.CoverAssetId=cover.Id;input.Assets=[new(){MediaAssetId=cover.Id,Usage="Cover",Caption="Technical caption <script>encoded</script>"}];input.Metadata.Tags="Đất đai; đất đai, tra cứu";input.Metadata.Featured=true;
        var body=input.BodyHtml;input.BodyHtml="<p></p>";saved=await wf.SaveAsync(saved.ContentId,input,actor,saved.RowVersion,Guid.NewGuid());
        await V05FinishTests.Denied<InvalidOperationException>(()=>wf.SubmitAsync(saved.ContentId,saved.CurrentRevisionId,actor,saved.RowVersion,Guid.NewGuid()));db.ChangeTracker.Clear();input.BodyHtml=body;
        saved=await wf.SaveAsync(saved.ContentId,input,actor,saved.RowVersion,Guid.NewGuid());saved=await wf.SubmitAsync(saved.ContentId,saved.CurrentRevisionId,actor,saved.RowVersion,Guid.NewGuid());
        saved=await wf.ApproveAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());saved=await wf.PublishApprovedAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());
        var published=saved.PublishedRevisionId!.Value;var old=await db.Revisions.AsNoTracking().SingleAsync(x=>x.Id==published);
        Check(old.Metadata.Tags=="Đất đai, tra cứu"&&old.Metadata.Featured==true&&old.Author==input.Author,"Template revision metadata lost");
        Check(await db.ContentAssets.AnyAsync(x=>x.RevisionId==published&&x.MediaAssetId==cover.Id&&x.Caption==input.Assets[0].Caption),"Cover caption not bound to reviewed revision");
        Check(await new PublicContentQuery(db).Filter(new PublicContentQuery(db).Published(),"dat dai").AnyAsync(x=>x.Id==saved.ContentId),"Published Vietnamese tags not searchable");
        saved=await wf.BeginEditAsync(saved.ContentId,actor,saved.RowVersion,Guid.NewGuid());input.Metadata.Tags="PRIVATE-TAG-"+Guid.NewGuid();input.Metadata.Featured=false;input.Title+=" private draft";
        saved=await wf.SaveAsync(saved.ContentId,input,actor,saved.RowVersion,Guid.NewGuid());
        Check(saved.PublishedRevisionId==published&&!await new PublicContentQuery(db).Filter(new PublicContentQuery(db).Published(),input.Metadata.Tags).AnyAsync(x=>x.Id==saved.ContentId),"Draft changed public tags or revision");
        await V05FinishTests.Denied<InvalidOperationException>(()=>wf.PublishApprovedAsync(saved.ContentId,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid()));db.ChangeTracker.Clear();
        // Read-only check of the proven source exception: no source edit/import.
        var source=await db.Contents.AsNoTracking().FirstAsync(x=>x.Kind==ContentKind.Article&&x.PublishedRevisionId!=null&&db.SourceRecords.Any(s=>s.ContentId==x.Id));
        var revision=await db.Revisions.AsNoTracking().SingleAsync(x=>x.Id==source.PublishedRevisionId);
        var validate=typeof(WorkflowService).GetMethod("ValidateRequiredAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        await (Task)validate.Invoke(wf,[source,revision.Title,revision.BodyHtml,revision.PrimaryCategoryId,1,revision.Summary,revision.Author,revision.CoverAssetId])!;
        var site=new SiteConfigurationService(db,StorageOptions.FromConfiguration(config),MediaUploadOptions.FromConfiguration(config,"Development"));var header=(await site.ReadAsync(true))!.Header;
        var category=await db.Categories.AsNoTracking().FirstAsync();
        Check(!await site.InitializeAsync("technical-initialization-noop",[new(){Id=category.Id,Name="MUST_NOT_OVERWRITE",Slug="must-not-overwrite"}])&&(await site.ReadAsync(true))!.Header.RowVersion.SequenceEqual(header.RowVersion)&&(await db.Categories.AsNoTracking().SingleAsync(x=>x.Id==category.Id)).Name==category.Name,"Initializer overwrote existing catalog/site");
        await File.WriteAllTextAsync(Path.Combine(TestEvidencePaths.At(PortalConfiguration.FindWorkspace()),"template-readback.json"),JsonSerializer.Serialize(new{fixture=saved.ContentId,published,requiredFields="PASS",tags=true,featured=true,caption=true,draftIsolation=true,oldApprovalDenied=true,sourceExceptionReadOnly=true}));
    }

    public static Task ScannerBoundaries(string root,IConfiguration config)
    {
        foreach(var environment in new[]{"Production","Staging"})
            Check(Rejected(()=>InquiryScannerFactory.Create(ScannerTests.DefenderConfig(config,[new string('0',64)]),environment)),"Test scanner escaped environment guard");
        foreach(var environment in new[]{"Development","Staging","Production"})
        {
            var c=new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?>{{"Scanner:Provider","MicrosoftDefenderOnPrem"}}).Build();
            Check(Rejected(()=>InquiryScannerFactory.Create(c,environment)),"On-prem provider accepted without allocated approved UAT policy");
        }
        Check(!UatDeployment.Enabled(config),"Test fixture accidentally enabled UAT");return Task.CompletedTask;
    }
    static bool Rejected(Func<IInquiryScanner> create){try{create();return false;}catch(InvalidOperationException){return true;}}
    public static async Task TrafficSql(DbContextOptions<PortalDbContext> options)
    {
        var protection=new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider();
        PublicTraffic Service(PortalDbContext db)=>new(db,protection,new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),Microsoft.Extensions.Logging.Abstractions.NullLogger<PublicTraffic>.Instance);
        var now=DateTimeOffset.UtcNow;var hash=ScannerTests.Hash(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString()));
        await using var before=new PortalDbContext(options);var baseline=(await Service(before).ReadAsync())!;await Service(before).RecordAsync(hash,now);
        async Task Record(DateTimeOffset at){await using var other=new PortalDbContext(options);await Service(other).RecordAsync(hash,at);}
        await Task.WhenAll(Record(now.AddMinutes(-2)),Record(now.AddSeconds(1)));
        await using var read=new PortalDbContext(options);var row=await read.Set<PublicVisit>().AsNoTracking().SingleAsync(x=>x.SessionHash==hash);var counts=(await Service(read).ReadAsync())!;
        Check(row.StartedUtc==now&&row.LastSeenUtc==now.AddSeconds(1)&&counts.Total==baseline.Total+1&&counts.Today==baseline.Today+1&&counts.Month==baseline.Month+1,"Cross-connection anonymous session counted twice or last-seen regressed");
        var vn=now.ToOffset(TimeSpan.FromHours(7));var previousDay=new DateTimeOffset(vn.Year,vn.Month,vn.Day,0,0,0,vn.Offset).AddSeconds(-1);
        await Service(read).RecordAsync(ScannerTests.Hash(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString())),previousDay);
        var next=(await Service(read).ReadAsync())!;Check(next.Total==counts.Total+1&&next.Today==counts.Today,"Vietnam calendar boundary differs from persisted sessions");
        Check(typeof(PublicVisit).GetProperties().Select(x=>x.Name).Order().SequenceEqual(new[]{"LastSeenUtc","SessionHash","StartedUtc"}),"Traffic collected unnecessary identity fields");
        await File.WriteAllTextAsync(Path.Combine(TestEvidencePaths.At(PortalConfiguration.FindWorkspace()),"traffic-readback.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",durableAcrossConnections=true,concurrentDedup=true,lastSeenMonotonic=true,vietnamDay=true,noIpAccountEmailOrUrl=true,counts=next}));
    }

    public static async Task SliderHttp(string root,IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        var folder=TestEvidencePaths.At(root,"slider-http");Directory.CreateDirectory(folder);Guid oldLive;ContentActor actor;
        SiteConfigurationService Site(PortalDbContext db)=>new(db,StorageOptions.FromConfiguration(config),MediaUploadOptions.FromConfiguration(config,"Development"));
        await using(var db=new PortalDbContext(options))
        {
            actor=await new CurrentPermissionService(db).GetAsync(await InquiryFinishTests.User(db,Permissions.All,"UAT slider technical Test"));
            var site=Site(db);var state=(await site.ReadAsync(false))!;oldLive=state.Header.LiveRevisionId!.Value;
            var old=(await site.ReadAsync(true))!;Check(SiteConfigurationService.Hash(old.Revision)==old.Revision.ContentHash,"Legacy live configuration hash changed");
            var images=await db.MediaAssets.Where(x=>x.ScanStatus=="Clean"&&!x.IsQuarantined&&x.SourceUrl!=""&&x.MimeType.StartsWith("image/")&&db.ContentAssets.Any(a=>a.MediaAssetId==x.Id&&db.Contents.Any(c=>c.IsPublic&&c.PublishedRevisionId==a.RevisionId))).Select(x=>x.Id).Take(2).ToArrayAsync();Check(images.Length==2,"Two genuine public Test images required");
            state=await site.MutateAsync(actor,state.Header.RowVersion,r=>{r.EnglishName="Technical Test English name";r.SupervisingAgency="Technical Test agency";r.ResponsiblePerson="Technical <script>encoded</script>";r.LicenseNumber="TECHNICAL-NOT-OFFICIAL";r.SeoDescription="Technical Test description";r.Banners.RemoveAll(b=>b.Position=="home");for(var i=0;i<images.Length;i++)r.Banners.Add(new(){MediaAssetId=images[i],Position="home",Label="Technical slider fixture",Alt="Technical image "+i,TargetKind="Route",RouteKey="Home",SortOrder=i});},"Technical Test presentation only");
            Check((await site.ReadAsync(true))!.Revision.EnglishName==old.Revision.EnglishName&&state.Revision.ContentHash!=old.Revision.ContentHash,"Draft presentation changed live or escaped version hash");
            await site.PublishAsync(actor,state.Header.RowVersion,state.Revision.Id);
        }
        const string origin="http://127.0.0.1:5295";var start=new ProcessStartInfo(Path.Combine(root,".local/dotnet/dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(TestRuntimePaths.Assembly(root,"Web"));start.ArgumentList.Add("--contentRoot");start.ArgumentList.Add(Path.Combine(root,"src/Web"));
        foreach(var p in new[]{("Public__BaseUrl",origin),("DOTNET_ENVIRONMENT","Development"),("ASPNETCORE_ENVIRONMENT","Development"),("Mail__Transport","Disabled"),("Media__AllowUnscannedDevelopmentUploads","false")})start.Environment[p.Item1]=p.Item2;
        using var web=Process.Start(start)!;var output=web.StandardOutput.ReadToEndAsync();var error=web.StandardError.ReadToEndAsync();
        try
        {
            using var client=new HttpClient();var ready=false;for(var i=0;i<100;i++){if(web.HasExited)break;try{using var h=await client.GetAsync(origin+"/health");if(h.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(100);}Check(ready,"Slider Test host unavailable");
            var node=new ProcessStartInfo("node"){WorkingDirectory=Path.Combine(root,"tests/browser"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};node.ArgumentList.Add("--input-type=module");node.ArgumentList.Add("-e");node.ArgumentList.Add("""
                import {chromium} from 'playwright';
                const b=await chromium.launch({channel:'msedge',headless:true});const c=await b.newContext({locale:'en-US',timezoneId:'America/New_York'});const p=await c.newPage();const errors=[];p.on('pageerror',e=>errors.push(e.message));
                const check=(v,m)=>{if(!v)throw Error(m)};
                try{
                    for(const width of [1440,390]){
                        await p.setViewportSize({width,height:900});await p.goto('http://127.0.0.1:5295/');await p.locator('[data-slide-controls]').waitFor();
                        check(await p.locator('.public-traffic').count()===1,'SQL statistics missing');
                        const session=(await c.cookies()).find(x=>x.name==='LandPortal.PublicSession');check(session?.httpOnly===true&&session.sameSite==='Lax','Statistics cookie privacy');
                        check(await p.locator('.brand [lang=en]').textContent()==='Technical Test English name','Versioned English name');
                        check((await p.locator('.footer-legal').textContent()).includes('TECHNICAL-NOT-OFFICIAL')&&await p.locator('.footer-legal script').count()===0,'Versioned footer or XSS encoding');
                        check(await p.locator('[data-slide]:visible').count()===1,'Initial slide');await p.locator('[data-slide-next]').click();
                        check(await p.locator('[data-slide-status]').textContent()==='Ảnh 2 / 2','Next slide');await p.locator('[data-slide-next]').click();
                        check(await p.locator('[data-slide-status]').textContent()==='Ảnh 1 / 2','Wrap');await p.locator('[data-slide-prev]').click();
                        check(await p.locator('[data-slide-status]').textContent()==='Ảnh 2 / 2','Previous');
                        const clock=await p.locator('[data-vietnam-clock]').textContent();await p.waitForTimeout(1100);check(await p.locator('[data-vietnam-clock]').textContent()!==clock,'Clock stopped');
                        check(await p.locator('[data-vietnam-clock]').evaluate(e=>e.textContent===new Intl.DateTimeFormat('vi-VN',{timeZone:'Asia/Ho_Chi_Minh',weekday:'long',year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',second:'2-digit',hour12:false}).format(new Date(e.dateTime))),'Browser timezone changed Vietnam clock');
                        check(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Horizontal overflow');
                        if(width===390){await p.locator('.mobile-menu-toggle').click();check(await p.locator('.mobile-menu-toggle').getAttribute('aria-expanded')==='true','Mobile navigation');}
                    }
                    check(errors.length===0,'Browser script error');console.log('PASS actual Test desktop/mobile slider, clock, SQL statistics cookie, versioned English/footer encoding and navigation; no screenshots');
                }finally{await b.close();}
                """);
            using var browser=Process.Start(node)!;var stdout=browser.StandardOutput.ReadToEndAsync();var stderr=browser.StandardError.ReadToEndAsync();await browser.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(folder,"browser.log"),await stdout+"\n"+await stderr);Check(browser.ExitCode==0,"Slider browser failed; inspect unique log");
            await File.WriteAllTextAsync(Path.Combine(folder,"readback.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",widths=new[]{1440,390},realPublicImages=2,sourceRowsEdited=0,slider="PASS",clock="PASS",devConfigWrites=0,screenshots=0}));
        }
        finally
        {
            if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(folder,"web.log"),await output+"\n"+await error);
            await using var db=new PortalDbContext(options);var site=Site(db);var current=(await site.ReadAsync(false))!;var restored=await site.RestoreAsync(actor,current.Header.RowVersion,oldLive);await site.PublishAsync(actor,restored.Header.RowVersion,restored.Revision.Id);
        }
    }

    public static async Task PdfHttp(string root,IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        var folder=TestEvidencePaths.At(root,"pdf-http");Directory.CreateDirectory(folder);Guid id,assetId;byte[] pdf;string pdfHash;
        ContentActor actor;WorkflowResult saved;var checks=new List<string>();
        await using(var db=new PortalDbContext(options))
        {
            actor=await new CurrentPermissionService(db).GetAsync(await InquiryFinishTests.User(db,Permissions.All,"UAT delivery PDF technical Test"));
            var input=Phase2aSqlTests.Input("UAT delivery PDF technical fixture "+Guid.NewGuid());input.Kind=ContentKind.LegalDocument;input.Metadata=new(){Number="TECH-"+Guid.NewGuid(),DocumentType="Quyết định",IssuedDate=new(2026,10,23),IssuingBody="Anonymous technical Test"};
            saved=await Workflow(db,config).SaveAsync(null,input,actor,null,Guid.NewGuid());id=saved.ContentId;
            var source=await db.MediaAssets.AsNoTracking().FirstAsync(x=>x.SourceUrl!=""&&x.MimeType=="application/pdf"&&x.ByteLength<8*1024*1024);
            pdf=(await File.ReadAllBytesAsync(StorageOptions.FromConfiguration(config).ResolveMedia(source.StorageKey))).Concat(Encoding.ASCII.GetBytes("\n% Anonymous technical PDF "+Guid.NewGuid())).ToArray();pdfHash=ScannerTests.Hash(pdf);
            var doc=V05FinishTests.Documents(db,config);assetId=(await doc.UploadAsync(id,new MemoryStream(pdf),"technical.pdf","application/pdf",actor)).Id;
            var eicar=ScannerTests.EicarArchive();var scanner=InquiryScannerFactory.Create(ScannerTests.DefenderConfig(config,[pdfHash,ScannerTests.Hash(eicar)]),"Development");
            Check((await scanner.HealthAsync()).Ready,"Real Defender unavailable: actual scanner verification BLOCKED");
            Check(await doc.ScanOnceAsync(scanner,assetId),"Real PDF scan did not execute");db.ChangeTracker.Clear();
            var proof=await db.EditorialDocuments.AsNoTracking().SingleAsync(x=>x.MediaAssetId==assetId);Check(proof.ScanState==InquiryScanState.Clean&&proof.ScannedSha256==pdfHash&&proof.EngineVersion!.StartsWith("Defender/"),"Genuine Clean PDF proof absent");
            var infected=await scanner.ScanAsync(new MemoryStream(eicar));Check(infected.State==InquiryScanState.Infected,"Real EICAR not classified Infected");
            var failed=await scanner.ScanAsync(new MemoryStream(Encoding.ASCII.GetBytes("unapproved anonymous bytes")));Check(failed.State==InquiryScanState.ScanFailed,"Unapproved bytes failed open");
            await File.WriteAllTextAsync(Path.Combine(folder,"native-scans.json"),JsonSerializer.Serialize(new{clean=new{sha256=pdfHash,proof.EngineVersion,proof.ScannedUtc},infected,failed,uatTargetEngine="BLOCKED: no allocated service identity/privacy policy/host",source53Changed=false}));
            input.Assets=[new(){MediaAssetId=assetId,Usage="Attachment",Caption="Technical PDF"}];saved=await Workflow(db,config).SaveAsync(id,input,actor,saved.RowVersion,Guid.NewGuid());
        }
        const string origin="http://127.0.0.1:5294";
        var start=new ProcessStartInfo(Path.Combine(root,".local/dotnet/dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(TestRuntimePaths.Assembly(root,"Web"));start.ArgumentList.Add("--contentRoot");start.ArgumentList.Add(Path.Combine(root,"src/Web"));
        foreach(var p in new[]{("Public__BaseUrl",origin),("DOTNET_ENVIRONMENT","Development"),("ASPNETCORE_ENVIRONMENT","Development"),("Mail__Transport","Disabled"),("Media__AllowUnscannedDevelopmentUploads","false")})start.Environment[p.Item1]=p.Item2;
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new(origin),Timeout=TimeSpan.FromSeconds(40)};
        async Task Expect(string path,HttpStatusCode status,HttpMethod? method=null,string? range=null){using var req=new HttpRequestMessage(method??HttpMethod.Get,path);if(range!=null)req.Headers.Add("Range",range);using var response=await client.SendAsync(req);Check(response.StatusCode==status,"HTTP "+path+" "+response.StatusCode);}
        try
        {
            var ready=false;for(var n=0;n<100;n++){if(process.HasExited)break;try{using var h=await client.GetAsync("/health");if(h.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(100);}Check(ready,"Focused Test HTTP host unavailable");
            await Expect("/bai-viet/"+id,HttpStatusCode.NotFound);foreach(var method in new[]{HttpMethod.Get,HttpMethod.Head})await Expect("/media/"+assetId,HttpStatusCode.NotFound,method);await Expect("/media/"+assetId,HttpStatusCode.NotFound,range:"bytes=0-31");checks.Add("Draft PDF GET/HEAD/Range private");
            await using(var db=new PortalDbContext(options))saved=await Workflow(db,config).PublishDirectAsync(id,saved.CurrentRevisionId!.Value,actor,saved.RowVersion,Guid.NewGuid());
            var html=await client.GetStringAsync("/bai-viet/"+id);var dom=new HtmlParser().ParseDocument(html);Check(dom.QuerySelector("iframe")?.GetAttribute("src")=="/media/"+assetId,"Published PDF embed missing");checks.Add("Actual document iframe");
            using(var inline=await client.GetAsync("/media/"+assetId)){Check(inline.IsSuccessStatusCode&&inline.Headers.GetValues("X-Frame-Options").Single()=="SAMEORIGIN"&&inline.Headers.GetValues("Content-Security-Policy").Single().Contains("frame-ancestors 'self'")&&ScannerTests.Hash(await inline.Content.ReadAsByteArrayAsync())==pdfHash,"PDF headers or exact bytes differ");}
            await Expect("/media/"+assetId+"?download=true&contentId="+id,HttpStatusCode.OK);
            await Expect("/media/"+assetId+"?download=true&contentId="+id,HttpStatusCode.OK,HttpMethod.Head);
            await Expect("/media/"+assetId+"?download=true&contentId="+id,HttpStatusCode.PartialContent,range:"bytes=0-31");
            await Expect("/media/"+assetId+"?download=true&contentId="+Guid.NewGuid(),HttpStatusCode.OK);
            await using(var db=new PortalDbContext(options))
            {
                Check(await db.AuditLogs.CountAsync(x=>x.ContentId==id&&x.Action=="PublicDocumentView")==1&&await db.AuditLogs.CountAsync(x=>x.ContentId==id&&x.Action=="PublicDocumentDownload")==1,"Counters included HEAD/Range/wrong document or missed valid requests");
                Check(!await db.AuditLogs.AnyAsync(x=>x.ContentId==id&&x.Action.StartsWith("PublicDocument")&&(x.Actor!="Public"||x.Detail!="")),"Counter collected private identity/details");
                saved=await Workflow(db,config).UnpublishAsync(id,actor,saved.RowVersion,Guid.NewGuid());
            }
            await Expect("/bai-viet/"+id,HttpStatusCode.NotFound);await Expect("/media/"+assetId,HttpStatusCode.NotFound);checks.Add("Persistent counters exclude HEAD/Range/unbound; withdrawal revokes file");
            using(var login=await client.GetAsync("/admin/login"))Check(login.Headers.GetValues("X-Frame-Options").Single()=="DENY","PDF exception weakened HTML admin framing");
            await File.WriteAllTextAsync(Path.Combine(folder,"http-readback.json"),JsonSerializer.Serialize(new{database="LandPortal_Test",fixtureContent=id,assetId,pdfHash,checks,devWrites=0,screenshots="NOT_RUN"}));
        }
        finally{if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(folder,"web.log"),await stdout+"\n"+await stderr);}
    }
}
