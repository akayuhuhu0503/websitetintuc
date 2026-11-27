using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandPortal.Infrastructure;
using LandPortal.MigrationCli;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

static class SourceResourceTests
{
    public static async Task Run(string output)
    {
        output=Path.GetFullPath(output);
        if(!output.StartsWith(Path.Combine(Directory.GetCurrentDirectory(),".local")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(output))throw new ArgumentException("New local test directory required");
        Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
        await using var db=new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().Options);
        var storage=new StorageOptions{MediaRoot=output,TempRoot=output,SnapshotRoot=output,KeysRoot=output};
        var media=new MediaService(db,storage,new MediaUploadOptions());
        SourceResponse Response(string type,byte[] bytes,string host="media-qldd.mae.gov.vn",int status=200)=>new("https://"+host+"/image.jpg",status,type,bytes,DateTimeOffset.UtcNow);
        byte[] Encode(SKEncodedImageFormat format){using var bmp=new SKBitmap(32,20);bmp.Erase(SKColors.Brown);using var image=SKImage.FromBitmap(bmp);using var data=image.Encode(format,95);return data.ToArray();}
        void Validate(SourceResponse response,string mime)
        {
            SourceSyncResourcePolicy.ValidateResponse(response,mime);
            var path=Path.Combine(output,Convert.ToHexStringLower(SHA256.HashData(response.Bytes))+".bin");File.WriteAllBytes(path,response.Bytes);
            media.ValidateContent(path,mime,default);
        }
        void Reject(Action action,string name,string? code=null)
        {try{action();throw new Exception("Accepted: "+name);}catch(Exception e)when(e is SourceResourceException or MediaUploadException){Check(code==null||e is SourceResourceException r&&r.Code==code,name);}}
        var jpeg=Encode(SKEncodedImageFormat.Jpeg);
        Validate(Response("image/jpeg",jpeg),"image/jpeg");Check(true,"Normal JPEG fully decodes");
        Validate(Response("image/png",jpeg),"image/jpeg");Check(true,"Observed QLDD JPEG labelled PNG fully decodes as JPEG");
        Validate(Response("application/octet-stream",jpeg),"image/jpeg");Check(true,"Existing binary response still requires full decode");
        foreach(var pair in new[]{(SKEncodedImageFormat.Png,"image/png"),(SKEncodedImageFormat.Webp,"image/webp")}){Validate(Response(pair.Item2,Encode(pair.Item1)),pair.Item2);Check(true,"Valid "+pair.Item2+" unchanged");}
        Reject(()=>Validate(Response("image/png",Encoding.UTF8.GetBytes("<html>blocked</html>")),"image/jpeg"),"HTML labelled PNG with JPG name denied by decoder");
        Reject(()=>Validate(Response("image/png",jpeg[..20]),"image/jpeg"),"Truncated JPEG denied");
        Reject(()=>Validate(Response("image/png",Encode(SKEncodedImageFormat.Png)),"image/jpeg"),"Actual PNG under JPG name still denied");
        Reject(()=>Validate(Response("text/html",jpeg),"image/jpeg"),"HTML Content-Type rejected even with JPEG bytes","CONTENT_TYPE");
        Validate(Response("image/png",jpeg,"media.mae.gov.vn"),"image/jpeg");Check(true,"Observed Ministry media host JPEG/PNG mismatch still fully decodes");
        Reject(()=>Validate(Response("image/png",jpeg,"outside.invalid"),"image/jpeg"),"Header exception not expanded to other hosts","CONTENT_TYPE");
        Reject(()=>Validate(Response("image/webp",jpeg),"image/jpeg"),"Unobserved MIME mismatch denied","CONTENT_TYPE");
        Reject(()=>Validate(Response("image/png",jpeg,status:404),"image/jpeg"),"HTTP failure classified before accepting bytes","HTTP_SOURCE");
        var tiny=new MediaService(db,storage,new MediaUploadOptions{MaxImagePixels=10});var file=Path.Combine(output,"limit.jpg");File.WriteAllBytes(file,jpeg);
        Reject(()=>tiny.ValidateContent(file,"image/jpeg",default),"Decoded pixel limit unchanged");
        using var client=new SafeSourceClient(0);
        var guard=typeof(SafeSourceClient).GetMethod("ValidateDestination",BindingFlags.Instance|BindingFlags.NonPublic)!;
        void Destination(string url,bool page,int redirects,string? expected)
        {
            try{guard.Invoke(client,[new Uri(url),page,redirects]);Check(expected==null,"Allowed source redirect "+redirects);}
            catch(TargetInvocationException e)when(e.InnerException is SourceTransportException st){Check(st.Code==expected,"Blocked destination "+expected);}
        }
        Destination("https://media-qldd.mae.gov.vn/a%20b.jpg",false,1,null);
        Destination("https://outside.invalid/a.jpg",false,1,"REDIRECT_BLOCKED");
        Destination("http://127.0.0.1/a.jpg",false,1,"REDIRECT_BLOCKED");
        Destination("https://media-qldd.mae.gov.vn:8443/a.jpg",false,1,"REDIRECT_BLOCKED");
        Destination("https://user:private@media-qldd.mae.gov.vn/a.jpg",false,1,"REDIRECT_BLOCKED");
        Destination("https://media-qldd.mae.gov.vn/a.jpg",true,1,"REDIRECT_BLOCKED");
        Destination("file:///private",false,0,"URL_POLICY");
        Check(!SafeSourceClient.IsPublicAddress(IPAddress.Parse("10.0.0.1"))&&!SafeSourceClient.IsPublicAddress(IPAddress.Parse("::ffff:127.0.0.1")),"DNS pinning still rejects private addresses");
        try{await client.FetchAsync("https://qldd.mae.gov.vn/");throw new Exception("Budget bypass");}catch(RequestBudgetException){Check(client.RequestsUsed==0,"Request budget blocks before network");}
        foreach(var code in new[]{"DNS","TCP","TLS","BYTE_LIMIT","REDIRECT_BLOCKED","TIMEOUT"})
        {
            var error=new SourceTransportException(code,new IOException("private-password-and-path"),"tệp nguồn");
            Check(error.SafeSummary.Contains(code)&&!error.SafeSummary.Contains("private-password"),"Safe diagnostic "+code);
        }
        await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=checks.Count,checks,sqlTouched=false}));
        Console.WriteLine("TOTAL PASS "+checks.Count);
    }
}
