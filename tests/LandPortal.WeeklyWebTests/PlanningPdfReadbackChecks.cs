using System.Text.Json;
using LandPortal.Infrastructure;
using LandPortal.WeeklyImport;

internal static class PlanningPdfReadbackChecks
{
    public static async Task RunAsync(string path,string expectedHash)
    {
        var root=Path.Combine(Directory.GetCurrentDirectory(),".local","tests","planning-readback-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var checks=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
        var info=new FileInfo(path);
        Check(info.Length>100*1024*1024 && info.Length<=EditorialDocumentPolicy.PdfMaximum && PlanningPackage.FileHash(path)==expectedHash,"Largest reviewed PDF is between 100 and 104 MiB, exact SHA");
        var builder=WebApplication.CreateBuilder();builder.Configuration.Sources.Clear();builder.Configuration.AddInMemoryCollection();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app=builder.Build();var assetId=Guid.NewGuid();var contentId=Guid.NewGuid();var requests=0;
        app.MapGet("/admin/documents/file/{id:guid}",(Guid id,Guid contentId)=>{requests++;return id==assetId ? Results.File(path,"application/pdf") : Results.NotFound();});
        await app.StartAsync();
        try
        {
            using var cms=new WebCmsClient(new Uri(app.Urls.Single()+"/"),true);var importer=new WebWeeklyImporter(cms);
            var row=new WeeklyRow(28,34,"fixture","n","", "",info.Name,expectedHash,info.Length,714,714,null,null,[]);
            var asset=JsonSerializer.SerializeToElement(new{id=assetId,originalName=info.Name,mimeType="application/pdf",byteLength=info.Length,usable=true});
            Check(await importer.VerifyAssetAsync(contentId,row,asset,EditorialDocumentPolicy.PdfMaximum)==assetId && requests==1,"QHKHSDD importer HTTP readback accepts actual 100.16 MiB PDF with exact byte/SHA check");
            async Task Denied(Func<Task> action,string name){try{await action();}catch(InvalidDataException){Check(true,name);return;}throw new Exception("NOT DENIED "+name);}
            await Denied(async()=>{await importer.VerifyAssetAsync(contentId,row,asset);},"Legacy weekly default remains 10 MiB");
            Check(requests==1,"Oversized legacy request rejected before download");
            var wrong=JsonSerializer.SerializeToElement(new{id=assetId,originalName="wrong.pdf",mimeType="application/pdf",byteLength=info.Length,usable=true});
            await Denied(async()=>{await importer.VerifyAssetAsync(contentId,row,wrong,EditorialDocumentPolicy.PdfMaximum);},"Wrong original file name still rejected");
            var unusable=JsonSerializer.SerializeToElement(new{id=assetId,originalName=info.Name,mimeType="application/pdf",byteLength=info.Length,usable=false});
            await Denied(async()=>{await importer.VerifyAssetAsync(contentId,row,unusable,EditorialDocumentPolicy.PdfMaximum);},"Unusable asset still rejected");
        }
        finally{await app.StopAsync();}
        File.WriteAllBytes(Path.Combine(root,"result.json"),JsonSerializer.SerializeToUtf8Bytes(new{passed=checks.Count,checks},WeeklyPackage.Json));
        Console.WriteLine($"RESULT {checks.Count}/{checks.Count} PASS; {root}");
    }
}
