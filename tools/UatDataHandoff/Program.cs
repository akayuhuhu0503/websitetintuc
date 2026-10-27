using LandPortal.Infrastructure;
using LandPortal.UatData;
using Microsoft.EntityFrameworkCore;

try
{
    var command=args.FirstOrDefault()??"help";
    string? Arg(string key){var n=Array.IndexOf(args,key);return n>=0&&n+1<args.Length?args[n+1]:null;}
    var package=Path.GetFullPath(Arg("--package")??throw new InvalidDataException("--package directory required."));
    if(command=="inspect")
    {
        var(data,sha)=await DataPackage.Load(package);Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{status="OFFLINE_PACKAGE_VERIFIED",manifestSha256=sha,data.PackageId,ready=new{contents=data.Ready.Count,revisions=data.Ready.Count,media=data.Ready.SelectMany(x=>x.Assets).Select(x=>x.MediaAssetId).Distinct().Count(),aliases=data.Ready.Sum(x=>x.Aliases.Count)},pendingReview=data.PendingReview.Count,mediaPendingTargetRelease=data.Media.Count(x=>!x.TargetPolicyReady),excluded=data.Excluded.Count,sqlWrites=0,sourceTotal="Unknown; this is the current verified Dev subset"}));return 0;
    }
    if(command=="offline-plan")return await PackageImport.RunAsync(command,package,args,new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
    var config=PortalConfiguration.Load();
    if(command=="export"){await PackageExport.RunAsync(package,config);return 0;}
    return await PackageImport.RunAsync(command,package,args,config);
}
catch(Exception error){Console.Error.WriteLine("UAT data operation stopped: "+error.GetType().Name+": "+error.Message);return 1;}
