using System.Diagnostics;
using System.Net;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LandPortal.Tests;
public static class Phase3DHttpTests
{
    public static async Task RunAsync(string root,IConfiguration config,DbContextOptions<PortalDbContext> options)
    {
        void Check(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
        var f=await BatchImportTests.Fixture(root,2);await using var db=new PortalDbContext(options);
        var service=new SourceBatch(db,StorageOptions.FromConfiguration(config),config,root,f.State,f.Hash);var plan=await service.PreviewAsync();var sha=SourceInventory.HashText(JsonSerializer.Serialize(plan));
        var imported=await service.CommitAsync(plan,sha);Check(imported.Count==2&&imported.All(x=>x.Status=="CreatePublishedTest"),"Technical SQL fixture not committed.");
        var first=imported[0].ContentId!.Value;var second=imported[1].ContentId!.Value;
        var suffix="/vi/news/technical/technical-test-"+Guid.NewGuid().ToString("N")+"-123.html";
        var source="http://gdla.gov.vn"+suffix;var secure="https://gdla.gov.vn"+suffix;
        db.SourceUrlAliases.Add(new(){Url=source,UrlHash=TextNormalizer.Hash(source),ContentId=first});await db.SaveChangesAsync();
        var folder=TestEvidencePaths.At(root,"secondary-http");Directory.CreateDirectory(folder);const string url="http://127.0.0.1:5305";
        var start=new ProcessStartInfo(Path.Combine(root,".local/dotnet/dotnet.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(TestRuntimePaths.Assembly(root,"Web"));start.ArgumentList.Add("--contentRoot");start.ArgumentList.Add(Path.Combine(root,"src/Web"));start.Environment["Public__BaseUrl"]=url;start.Environment["Public__LocalLegacySourceHost"]="gdla.gov.vn";start.Environment["DOTNET_ENVIRONMENT"]="Development";start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";start.Environment["Mail__Transport"]="Disabled";
        using var web=Process.Start(start)!;var stdout=web.StandardOutput.ReadToEndAsync();var stderr=web.StandardError.ReadToEndAsync();using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new(url)};var checks=new List<object>();
        try
        {
            var ready=false;for(var i=0;i<100;i++){try{using var r=await client.GetAsync("/health");if(r.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(200);}Check(ready,"Owned secondary preview unavailable.");
            using(var r=await client.GetAsync(suffix)){Check(r.StatusCode==HttpStatusCode.MovedPermanently&&r.Headers.Location!.OriginalString.Contains(first.ToString()),"HTTP NukeViet .html alias did not redirect to correct publication.");checks.Add(new{stage="http-only",status=(int)r.StatusCode});}
            db.SourceUrlAliases.Add(new(){Url=secure,UrlHash=TextNormalizer.Hash(secure),ContentId=second});await db.SaveChangesAsync();
            using(var r=await client.GetAsync(suffix)){Check(r.StatusCode==HttpStatusCode.NotFound,"Protocol aliases selected an arbitrary article.");checks.Add(new{stage="conflicting-protocols",status=(int)r.StatusCode});}
            var conflicting=await db.SourceUrlAliases.SingleAsync(x=>x.Url==secure);conflicting.ContentId=first;await db.SaveChangesAsync();
            using(var r=await client.GetAsync(suffix))Check(r.StatusCode==HttpStatusCode.MovedPermanently,"Equivalent protocol aliases should redirect once.");
            await service.RollbackPublicationAsync(plan,sha);
            using(var r=await client.GetAsync(suffix)){Check(r.StatusCode==HttpStatusCode.NotFound,"Withdrawn/draft content redirected publicly.");checks.Add(new{stage="withdrawn",status=(int)r.StatusCode});}
            using(var r=await client.GetAsync("/cong-khai-thong-tin/nguoi-nuoc-ngoai-dinh-cu-o-viet-nam"))Check(r.IsSuccessStatusCode,"Approved FR17 group route unavailable on Test.");
        }
        finally
        {
            if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(folder,"web.log"),await stdout+"\n"+await stderr);
            await MediaRecovery.WriteNewAsync(Path.Combine(folder,"http.json"),new{checks,fixture="TECHNICAL TEST ONLY, not real NukeViet source or AC05.1 coverage",ownedWebStopped=true});
            await service.RollbackPublicationAsync(plan,sha);
        }
    }
}
