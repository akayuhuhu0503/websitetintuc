using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;

static class SourceImageProbe
{
    // Explicit bounded public read, with the production client. No runtime configuration or SQL.
    public static async Task Run(string output)
    {
        output=Path.GetFullPath(output);
        if(!output.StartsWith(Path.Combine(Directory.GetCurrentDirectory(),".local")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(output))throw new ArgumentException("New local probe directory required");
        Directory.CreateDirectory(output);
        var cases=new[]{
            (Id:"3758",Url:"https://qldd.mae.gov.vn/cuc-quan-ly-dat-dai-day-manh-cong-tac-xay-dung-hoan-thien-va-to-chuc-thi-hanh-phap-luat-huong-ung-ngay-phap-luat-viet-nam-nam-2026-3758.htm"),
            (Id:"3767",Url:"https://qldd.mae.gov.vn/cuc-quan-ly-dat-dai-khan-truong-trien-khai-xay-dung-co-so-du-lieu-vi-tri-viec-lam-3767.htm")};
        using var client=new SafeSourceClient(10);using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var db=new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().Options);
        var media=new MediaService(db,new StorageOptions{MediaRoot=output,SnapshotRoot=output,TempRoot=output,KeysRoot=output},new MediaUploadOptions());var facts=new List<object>();
        try
        {
            var robots=await client.FetchAsync("https://qldd.mae.gov.vn/robots.txt",128*1024,deadline.Token,true);
            if(robots.Status is not(200 or 404))throw new IOException("Robots HTTP "+robots.Status);
            var rules=new SourceSyncRobots(robots.Status==200?Encoding.UTF8.GetString(robots.Bytes):"");
            foreach(var item in cases)
            {
                if(!rules.Allows(item.Url))throw new IOException("Robots denied");
                var page=await client.FetchAsync(item.Url,2*1024*1024,deadline.Token,true);
                if(page.Status!=200)throw new IOException("Page HTTP "+page.Status);
                await File.WriteAllBytesAsync(Path.Combine(output,item.Id+".html"),page.Bytes);
                var parsed=SourceParser.Parse(Encoding.UTF8.GetString(page.Bytes),page.Url,ContentKind.Article,item.Id);
                var markup=SourceParser.Document(parsed.BodyHtml).QuerySelectorAll("img").Select(x=>new{src=x.GetAttribute("src"),dataSrc=x.GetAttribute("data-src"),srcset=x.GetAttribute("srcset")}).ToArray();
                facts.Add(new{item.Id,parsed.PrimaryCategoryId,parsed.Warnings,markup,parsed.AssetUrls});
                foreach(var url in parsed.AssetUrls.Take(2))
                {
                    if(new Uri(url).Host=="qldd.mae.gov.vn"&&!rules.Allows(url))throw new IOException("Asset robots denied");
                    var response=await client.FetchAsync(url,10*1024*1024,deadline.Token);
                    var sha=Convert.ToHexStringLower(SHA256.HashData(response.Bytes));var path=Path.Combine(output,sha+".bin");
                    await File.WriteAllBytesAsync(path,response.Bytes);
                    string validation;int? width=null,height=null;
                    try{SourceSyncResourcePolicy.ValidateResponse(response,"image/jpeg");var result=media.ValidateContent(path,"image/jpeg",deadline.Token);validation="PASS";width=result.Width;height=result.Height;}
                    catch(Exception e){validation=e.GetType().Name+": "+e.Message;}
                    facts.Add(new{item.Id,requested=url,final=response.Url,response.Status,response.ContentType,bytes=response.Bytes.Length,sha256=sha,magic=Convert.ToHexString(response.Bytes.Take(12).ToArray()),validation,width,height});
                }
            }
        }
        catch(Exception e){facts.Add(new{error=e.GetType().Name,message=e.Message});Environment.ExitCode=1;}
        finally
        {
            var json=JsonSerializer.Serialize(new{client.RequestsUsed,sqlTouched=false,facts},new JsonSerializerOptions{WriteIndented=true});
            await File.WriteAllTextAsync(Path.Combine(output,"result.json"),json);Console.WriteLine(json);
        }
    }
}
