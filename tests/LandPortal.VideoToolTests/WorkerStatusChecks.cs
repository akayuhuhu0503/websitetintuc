using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LandPortal.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

internal static class WorkerStatusChecks
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"LandPortal-WorkerStatus-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks=0;
        void Check(bool value,string name)
        {
            if(!value)throw new InvalidOperationException(name);
            checks++;
            Console.WriteLine("PASS Worker status: "+name);
        }
        var web=Path.Combine(root,"web");
        Directory.CreateDirectory(Path.Combine(web,".runtime","worker"));
        // Resolve only; no credentials, SQL, IIS or real package are used.
        File.WriteAllText(Path.Combine(web,".runtime","manifest.json"),"{}");
        var webLayout=PortablePackageLayout.Resolve(web);
        var workerLayout=PortablePackageLayout.Resolve(Path.Combine(web,".runtime","worker"));
        IConfiguration WebConfiguration()=>new ConfigurationBuilder().AddInMemoryCollection().Build();
        var webConfig=WebConfiguration();var workerConfig=WebConfiguration();
        webLayout.SetRuntimeLocations(webConfig);workerLayout.SetRuntimeLocations(workerConfig);
        Check(webConfig["Storage:TempRoot"]==workerConfig["Storage:TempRoot"]&&webConfig["Storage:KeysRoot"]==workerConfig["Storage:KeysRoot"],"WebBundle Web and separate Worker resolve the same temp/keys");
        var storage=new StorageOptions
        {
            MediaRoot=webConfig["Storage:MediaRoot"]!,SnapshotRoot=webConfig["Storage:SnapshotRoot"]!,
            TempRoot=webConfig["Storage:TempRoot"]!,KeysRoot=webConfig["Storage:KeysRoot"]!
        };
        foreach(var path in new[]{storage.MediaRoot,storage.SnapshotRoot,storage.TempRoot,storage.KeysRoot})Directory.CreateDirectory(path);
        var pathStatus=Path.Combine(storage.TempRoot,"video-worker-status.dat");
        Check(VideoWorkerStatus.Inspect(storage).Code=="VIDEO_WORKER_STATUS_MISSING","Absent heartbeat is distinguished from invalid evidence");
        var tools=new VideoToolHealth(true,"VIDEO_PROCESSOR_CONFIGURED",true,true,"fixture","fixture",new string('1',64),new string('2',64));
        VideoWorkerStatus.Write(storage,tools,false,true);
        var heartbeat=VideoWorkerStatus.Inspect(storage);
        Check(heartbeat.Code=="VIDEO_WORKER_READY"&&heartbeat.Observation?.ManagedVideoWithoutAv==true,"A separately created reader verifies protected heartbeat with same keyring");
        Check(VideoWorkerStatus.Inspect(storage,300,DateTimeOffset.UtcNow.AddHours(1)).Code=="VIDEO_WORKER_STALE","Old heartbeat cannot report a live Worker");
        VideoWorkerStatus.Write(storage,tools,false,false);
        Check(VideoWorkerStatus.Inspect(storage).Code=="VIDEO_WORKER_BLOCKED","Unavailable scanner without approved policy remains blocked");
        VideoWorkerStatus.Write(storage,tools,false,true,"Stopped");
        Check(VideoWorkerStatus.Inspect(storage).Code=="VIDEO_WORKER_STOPPED","Stopped heartbeat is not ready");
        var bytes=File.ReadAllBytes(pathStatus);
        var wrongKeys=new StorageOptions{MediaRoot=storage.MediaRoot,SnapshotRoot=storage.SnapshotRoot,TempRoot=storage.TempRoot,KeysRoot=Path.Combine(root,"other-keys")};
        Directory.CreateDirectory(wrongKeys.KeysRoot);
        Check(VideoWorkerStatus.Inspect(wrongKeys).Code=="VIDEO_WORKER_STATUS_UNVERIFIED","Different keyring is not mislabeled missing or ready");
        File.WriteAllText(pathStatus,"tampered status");
        Check(VideoWorkerStatus.Inspect(storage).Code=="VIDEO_WORKER_STATUS_UNVERIFIED"&&VideoWorkerStatus.Read(storage)==null,"Tampered protected heartbeat is rejected");
        var protector=DataProtectionProvider.Create(new DirectoryInfo(storage.KeysRoot),b=>b.SetApplicationName("LandPortal")).CreateProtector("LandPortal.VideoWorkerStatus.v1");
        File.WriteAllText(pathStatus,protector.Protect("{}"));
        Check(VideoWorkerStatus.Inspect(storage).Code=="VIDEO_WORKER_STATUS_INVALID","Missing required heartbeat fields cannot reach state rendering");
        File.WriteAllText(pathStatus,new string('x',32769));
        Check(VideoWorkerStatus.Inspect(storage).Code=="VIDEO_WORKER_STATUS_INVALID","Oversized heartbeat is bounded and rejected");
        File.WriteAllBytes(pathStatus,bytes);
        if(OperatingSystem.IsWindows())
        {
            var file=new FileInfo(pathStatus);
            var originalSddl=file.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            File.WriteAllText(Path.Combine(root,"fixture-status-original-dacl.sddl"),originalSddl);
            var denied=file.GetAccessControl();
            denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,FileSystemRights.ReadData,AccessControlType.Deny));
            try
            {
                file.SetAccessControl(denied);
                Check(VideoWorkerStatus.Inspect(storage).Code=="VIDEO_WORKER_STATUS_ACCESS_DENIED","Actual Windows read denial reports access error rather than missing Worker");
            }
            finally
            {
                // GetAccessControl returns an unmodified object; setting it back may be a no-op.
                var restore=new FileSecurity();
                restore.SetSecurityDescriptorSddlForm(originalSddl,AccessControlSections.Access);
                file.SetAccessControl(restore);
            }
        }
        Check(VideoWorkerStatus.Inspect(storage).Code=="VIDEO_WORKER_STOPPED","Read works again after isolated fixture ACL restoration");
        Console.WriteLine($"WORKER_STATUS_CHECKS_PASS={checks}; sqlOpened=false; operationalDataChanged=false");
    }
}
