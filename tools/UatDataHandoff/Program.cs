using LandPortal.Infrastructure;
using LandPortal.UatData;
using Microsoft.EntityFrameworkCore;

try
{
    var command=args.FirstOrDefault()??"help";
    string? Arg(string key){var n=Array.IndexOf(args,key);return n>=0&&n+1<args.Length?args[n+1]:null;}
    var package=Path.GetFullPath(Arg("--package")??throw new InvalidDataException("--package directory required."));
    var internalIntegrityOnly=!args.Contains("--require-native-antivirus");
    if(!internalIntegrityOnly&&args.Contains("--integrity-only"))throw new InvalidDataException("Conflicting media verification modes.");
    if(command=="private-acl-inventory")return PrivateAclInventory.Run(Arg("--iis-root")??throw new InvalidDataException("IIS root required."),Path.GetDirectoryName(package)!);
    if(command=="media-tool-identity")
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{status="MEDIA_TOOL_IDENTITY",assemblyName=typeof(TargetMediaStage).Assembly.GetName().Name,toolSha256=TargetMediaStage.ToolSha,infrastructureSha256=await DataPackage.FileHash(typeof(MediaService).Assembly.Location),configResolver="PORTABLE_PRIVATE_STORAGE_V1"}));
        return 0;
    }
    if(command=="inspect")
    {
        var(data,sha)=await DataPackage.Load(package,Arg("--package-media-root"));Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{status="OFFLINE_PACKAGE_VERIFIED",manifestSha256=sha,data.PackageId,ready=new{contents=data.Ready.Count,revisions=data.Ready.Count,media=data.Ready.SelectMany(x=>x.Assets).Select(x=>x.MediaAssetId).Distinct().Count(),aliases=data.Ready.Sum(x=>x.Aliases.Count)},pendingReview=data.PendingReview.Count,mediaPendingTargetRelease=data.Media.Count(x=>!x.TargetPolicyReady),excluded=data.Excluded.Count,sqlWrites=0,sourceTotal="Unknown; verified published QLDD subset, see source inventory"}));return 0;
    }
    if(command=="offline-plan")return await PackageImport.RunAsync(command,package,args,new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
    if(command=="validate-media")return await TargetMediaStage.ValidateFiles(package);
    var config=Arg("--iis-root") is string iisRoot?IisDataConfiguration.Load(iisRoot):PortalConfiguration.Load();
    if(command=="stage-media")return await TargetMediaStage.Run(package,Arg("--iis-root")??throw new InvalidDataException("--iis-root runtime binding required; no standalone configuration."),config,internalIntegrityOnly,internalIntegrityOnly);
    if(command=="media-storage-preflight")return await TargetMediaStage.InspectCopiedStorage(package,Arg("--iis-root")??throw new InvalidDataException("IIS root required."),config,internalIntegrityOnly);
    if(command=="media-copy-destination")return await TargetMediaStage.ShowCopyDestination(package,Arg("--iis-root")??throw new InvalidDataException("IIS root required."),config);
    if(command=="verify-copied-media")return await TargetMediaStage.Run(package,Arg("--iis-root")??throw new InvalidDataException("IIS root required."),config,true,internalIntegrityOnly);
    if(command is "export" or "export-lan"){await PackageExport.RunAsync(package,config,command=="export-lan");return 0;}
    return await PackageImport.RunAsync(command,package,args,config);
}
catch(Exception error){Console.Error.WriteLine("UAT data operation stopped: "+error.GetType().Name+": "+error.Message);return 1;}
