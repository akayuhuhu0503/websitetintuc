using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.Versioning;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace LandPortal.UatData;

public static class Media252Scope
{
    public static readonly Guid PackageId=Guid.Parse("dee778f3-4010-47f5-8981-69b2fdabdaa3");
    public const string ManifestSha="c5bbabdb27d518625f95ac073a7843a30d62a51b297e06838e255c2fea4e0ccb";
    public static void Require(DataBundle data,string sha)
    {
        var rows=data.PendingReview;
        var dependencies=rows.SelectMany(r=>r.Assets.Select(a=>a.MediaAssetId).Concat(r.Revision.CoverAssetId is Guid c?[c]:Array.Empty<Guid>())).Distinct().Order();
        if(data.PackageId!=PackageId||sha!=ManifestSha||data.Ready.Count!=0||rows.Count!=252||data.Media.Count!=703||rows.Sum(r=>r.Aliases.Count)!=504||
            data.PublishedSite!=null||!dependencies.SequenceEqual(data.Media.Select(m=>m.Id).Order()))
            throw new UnauthorizedAccessException("Gói media252 không đúng ID/hash/phạm vi đã chuẩn bị; không dùng lại gói 33 bài.");
    }
    public static string Key(Guid package,MediaRow m)=>"uat-data/"+package.ToString("N")+"/"+Path.GetFileName(m.File);
}

public sealed record TargetMediaEntry(Guid MediaId,string Sha256,long Bytes,string StorageKey,DateTimeOffset ScannedUtc,InquiryScanResult Scan,DateTimeOffset? VerifiedUtc=null);
public sealed record TargetMediaReceipt(string Kind,Guid PackageId,string ManifestSha256,string MachineName,string OperatorSid,string SqlServerIdentity,string Database,string MediaRootSha256,string ToolSha256,string? EngineIdentity,DateTimeOffset CompletedUtc,string Status,List<TargetMediaEntry> Entries,List<Guid> Blocked,bool IntegrityOnly=false);

// This is an operator attestation, NOT an organizational signature. Its exact
// hash must be acknowledged in the separately authorized owner + DBA scope.
// Target-only cached scan evidence is DPAPI-protected; no key/SQL credential is
// included in the exported receipt or in the handoff package.
public static class TargetMediaStage
{
    public const string Kind="LandPortal.TargetMedia.NativeScan.v1";
    public const string IntegrityKind="LandPortal.TargetMedia.IntegrityOnly.v1";
    public const string IntegrityStatus="TARGET_MEDIA_INTEGRITY_VERIFIED_NOT_SCANNED";
    public static string ToolSha=>DataPackage.FileHash(typeof(TargetMediaStage).Assembly.Location).GetAwaiter().GetResult();
    public static async Task<int> ValidateFiles(string root)
    {
        var(data,sha)=await DataPackage.Load(root);Media252Scope.Require(data,sha);
        // The parser does not access DbContext or storage. This is format
        // validation only; it can never authorize Clean or publication.
        var validator=new MediaService(null!,null!,new MediaUploadOptions{MaxPdfBytes=48L*1024*1024});
        var errors=new List<object>();var valid=0;
        foreach(var m in data.Media)
        {
            try{validator.ValidateContent(DataPackage.PathIn(root,m.File),m.MimeType,CancellationToken.None);valid++;}
            catch(MediaUploadException){errors.Add(new{mediaId=m.Id,mime=m.MimeType,code="FORMAT_INVALID"});}
        }
        Console.WriteLine(JsonSerializer.Serialize(new{status=errors.Count==0?"MEDIA_FORMATS_VALIDATED_NOT_SCANNED":"FORMAT_EXCEPTIONS",manifestSha256=sha,files=data.Media.Count,valid,errors,nativeScans=0,sqlWrites=0}));return errors.Count==0?0:2;
    }
    public static bool Fresh(TargetMediaEntry entry,string? engine,DateTimeOffset now)
    {
        var scan=entry.Scan;
        if(scan.State!=InquiryScanState.Clean||!string.IsNullOrEmpty(scan.Error)||string.IsNullOrWhiteSpace(engine)||scan.EngineVersion!=engine||
            entry.ScannedUtc<now.AddHours(-24)||entry.ScannedUtc>now.AddMinutes(5))return false;
        if(scan.Provider=="MicrosoftDefenderOnPrem")return scan.SignatureUpdatedUtc is DateTimeOffset updated&&updated>now.AddHours(-72)&&updated<=now.AddMinutes(5)&&
            !string.IsNullOrWhiteSpace(scan.SignatureVersion)&&scan.OutputSha256 is {Length:64} output&&output.All(Uri.IsHexDigit)&&engine.StartsWith("DefenderOnPrem/",StringComparison.Ordinal);
        if(scan.Provider=="Clamd")
        {
            var m=Regex.Match(engine,@"^ClamAV [^/]+/\d+/(.+)$");
            return m.Success&&DateTimeOffset.TryParse(m.Groups[1].Value.Trim().TrimEnd('|').Trim(),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var updated)&&updated>now.AddHours(-72)&&updated<=now.AddMinutes(5);
        }
        return false;
    }
    public static void Validate(TargetMediaReceipt r,DataBundle data,string sha,bool fresh)
    {
        Media252Scope.Require(data,sha);
        if(r.Kind!=(r.IntegrityOnly?IntegrityKind:Kind)||r.PackageId!=data.PackageId||r.ManifestSha256!=sha||r.Status!=(r.IntegrityOnly?IntegrityStatus:"TARGET_MEDIA_READY_NOT_SQL_IMPORTED")||r.SqlServerIdentity!="WIN-S1IBJB84OOO"||r.Database!="website_tin_tuc"||
            string.IsNullOrWhiteSpace(r.MachineName)||!r.OperatorSid.StartsWith("S-1-",StringComparison.Ordinal)||r.MediaRootSha256.Length!=64||!r.MediaRootSha256.All(Uri.IsHexDigit)||
            r.ToolSha256!=ToolSha||r.Blocked.Count!=0||r.Entries.Count!=703||r.Entries.Select(e=>e.MediaId).Distinct().Count()!=703||r.CompletedUtc>DateTimeOffset.UtcNow.AddMinutes(5))
            throw new InvalidDataException("Receipt IIS chưa READY hoặc không khớp phạm vi/định danh.");
        var entries=r.Entries.ToDictionary(e=>e.MediaId);
        foreach(var m in data.Media)
        {
            if(!entries.TryGetValue(m.Id,out var e)||e.Sha256!=m.Sha256||e.Bytes!=m.Bytes||e.StorageKey!=Media252Scope.Key(data.PackageId,m)||
                (r.IntegrityOnly?!IntegrityEntryValid(e,r,fresh):e.Scan.State!=InquiryScanState.Clean||e.ScannedUtc>r.CompletedUtc||fresh&&!Fresh(e,r.EngineIdentity,DateTimeOffset.UtcNow)))throw new InvalidDataException("Media receipt không đúng trạng thái/byte/hash/storage key: "+m.Id);
        }
    }
    static bool IntegrityEntryValid(TargetMediaEntry e,TargetMediaReceipt r,bool fresh)=>r.EngineIdentity==null&&e.ScannedUtc==DateTimeOffset.MinValue&&
        e.Scan.State==InquiryScanState.NotScanned&&e.Scan.Provider=="ScannerSkipped"&&e.Scan.Error=="ANTIVIRUS_NOT_PERFORMED"&&e.Scan.EngineVersion==null&&
        e.Scan.SignatureVersion==null&&e.Scan.SignatureUpdatedUtc==null&&e.Scan.OutputSha256==null&&e.VerifiedUtc is DateTimeOffset utc&&utc<=r.CompletedUtc&&
        utc<=DateTimeOffset.UtcNow.AddMinutes(5)&&(!fresh||utc>=DateTimeOffset.UtcNow.AddHours(-24));
    public static async Task<(TargetMediaReceipt Receipt,string Sha)> Read(string path,DataBundle data,string sha,bool fresh)
    {
        DataPackage.NoReparse(path);var info=new FileInfo(path);if(info.Length is <=0 or >8*1024*1024)throw new InvalidDataException("Media receipt size invalid.");
        var bytes=await File.ReadAllBytesAsync(path);var r=JsonSerializer.Deserialize<TargetMediaReceipt>(bytes,DataPackage.Json)??throw new InvalidDataException("Media receipt missing.");Validate(r,data,sha,fresh);return(r,DataPackage.Hash(bytes));
    }
    static async Task<string?> Identity(IInquiryScanner scanner,IConfiguration config)
    {
        if(scanner is DefenderInquiryScanner defender)return await defender.CurrentIdentityAsync();
        if(scanner is not ClamdInquiryScanner||!int.TryParse(config["Scanner:ClamdPort"],out var port))return null;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));using var client=new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback,port,timeout.Token);await using var stream=client.GetStream();await stream.WriteAsync("zVERSIONCOMMANDS\0"u8.ToArray(),timeout.Token);
        var bytes=new List<byte>();var one=new byte[1];while(bytes.Count<1024&&await stream.ReadAsync(one,timeout.Token)>0){if(one[0]==0)break;bytes.Add(one[0]);}
        var text=Encoding.ASCII.GetString(bytes.ToArray());return text.StartsWith("ClamAV ",StringComparison.Ordinal)&&text.Contains("INSTREAM",StringComparison.Ordinal)?text.Split("COMMANDS:")[0].Trim():null;
    }
    static string Entropy(Guid id,string sha)=>Kind+"|"+id+"|"+sha;
    [SupportedOSPlatform("windows")]
    static TargetMediaEntry? Cached(string path,string entropy)
    {
        if(!File.Exists(path))return null;DataPackage.NoReparse(path);
        var bytes=ProtectedData.Unprotect(File.ReadAllBytes(path),Encoding.UTF8.GetBytes(entropy),DataProtectionScope.CurrentUser);
        try{return JsonSerializer.Deserialize<TargetMediaEntry>(bytes,DataPackage.Json);}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    [SupportedOSPlatform("windows")]
    static void SaveCache(string path,TargetMediaEntry entry,string entropy)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(entry,DataPackage.Json);
        try
        {
            var encrypted=ProtectedData.Protect(bytes,Encoding.UTF8.GetBytes(entropy),DataProtectionScope.CurrentUser);
            // Cache revisions are append-only. Existing evidence is never rewritten.
            using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);output.Write(encrypted);output.Flush(true);
        }
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public static async Task<int> ShowCopyDestination(string root,string iisRoot,IConfiguration config)
    {
        if(!OperatingSystem.IsWindows()||!UatDeployment.Validate(config,"Staging"))throw new UnauthorizedAccessException("Cần cấu hình IIS UAT thật; không suy đoán đường dẫn storage.");
        var(data,sha)=await DataPackage.LoadMetadata(root);Media252Scope.Require(data,sha);
        RequirePrivateStorage(iisRoot,config);
        var storage=StorageOptions.FromConfiguration(config);
        Console.WriteLine(JsonSerializer.Serialize(new{status="COPY_DESTINATION_RESOLVED_NOT_SCANNED",mediaRoot=Path.GetFullPath(storage.MediaRoot),packageDirectory=StorageOptions.ResolveWithin(storage.MediaRoot,"uat-data/"+data.PackageId.ToString("N")),files=703,bytes=data.Media.Sum(m=>m.Bytes),sqlWrites=0}));return 0;
    }
    static void RequirePrivateStorage(string iisRoot,IConfiguration config)
    {
        var storage=StorageOptions.FromConfiguration(config);var web=Path.Combine(Path.GetFullPath(iisRoot),"web").TrimEnd(Path.DirectorySeparatorChar);var media=Path.GetFullPath(storage.MediaRoot).TrimEnd(Path.DirectorySeparatorChar);
        if(media.Equals(web,StringComparison.OrdinalIgnoreCase)||media.StartsWith(web+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Kho media nằm trong webroot; không copy file chưa quét vào vị trí công khai.");
        if(config["Sql:ExpectedDatabase"]!="website_tin_tuc"||UatDeployment.PhysicalServer(config)!="WIN-S1IBJB84OOO"||config["Deployment:ExpectedSqlServer"]!="45.120.229.43,1433")throw new UnauthorizedAccessException("Cấu hình không đúng SQL đích; chưa kết nối SQL.");
    }
    // All prerequisites before the operator replaces runtime binaries. This is
    // read-only verification, never a native Clean receipt or a SQL connection.
    public static async Task<int> InspectCopiedStorage(string root,string iisRoot,IConfiguration config,bool integrityOnly=false)
    {
        if(!OperatingSystem.IsWindows()||!UatDeployment.Validate(config,"Staging"))throw new UnauthorizedAccessException("Windows IIS UAT required.");
        RequirePrivateStorage(iisRoot,config);
        var storage=StorageOptions.FromConfiguration(config);
        var(data,sha)=await DataPackage.Load(root,storage.MediaRoot);Media252Scope.Require(data,sha);
        var folder=StorageOptions.ResolveWithin(storage.MediaRoot,"uat-data/"+data.PackageId.ToString("N"));
        if(!Directory.Exists(storage.TempRoot)||Directory.EnumerateDirectories(folder).Any()||Directory.EnumerateFiles(folder).Count()!=703)throw new InvalidDataException("Kho media/temp hoặc số lượng file không đúng phạm vi.");
        var validator=new MediaService(null!,storage,new MediaUploadOptions{MaxPdfBytes=48L*1024*1024});
        foreach(var m in data.Media)validator.ValidateContent(MediaService.ResolveMediaPath(storage,Media252Scope.Key(data.PackageId,m)),m.MimeType,default);
        if(integrityOnly){Console.WriteLine(JsonSerializer.Serialize(new{status="PRIVATE_STORAGE_PREFLIGHT_PASS_NOT_SCANNED",mediaRoot=storage.MediaRoot,files=703,manifestSha256=sha,scannerHealth="ScannerSkipped",sqlWrites=0,nativeScans=0}));return 0;}
        if(config["Scanner:Provider"]=="MicrosoftDefenderOnPrem"&&!string.Equals(config["Scanner:DefenderServiceIdentity"],WindowsIdentity.GetCurrent().Name,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("SCANNER_IDENTITY_MISMATCH: chạy bằng đúng tài khoản scanner đã cấu hình.");
        var scanner=InquiryScannerFactory.Create(config,"Staging",48L*1024*1024);var health=await scanner.HealthAsync();
        if(!health.Ready)throw new InvalidOperationException("SCANNER_NOT_READY: "+health.Code+"; chưa sửa binary, chưa quét, chưa ghi SQL.");
        Console.WriteLine(JsonSerializer.Serialize(new{status="PRIVATE_STORAGE_PREFLIGHT_PASS_NOT_SCANNED",mediaRoot=storage.MediaRoot,files=703,manifestSha256=sha,scannerHealth=health.Code,sqlWrites=0,nativeScans=0}));
        return 0;
    }
    public static async Task<int> Run(string root,string iisRoot,IConfiguration config,bool verifyCopied=false,bool integrityOnly=false)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Chạy gói media trên máy Windows IIS đích.");
        if(!UatDeployment.Validate(config,"Staging"))throw new UnauthorizedAccessException("Gói này chỉ chạy với cấu hình IIS UAT đã cấp; không thao tác trên Production hoặc cấu hình chưa hợp lệ.");
        RequirePrivateStorage(iisRoot,config);var storage=StorageOptions.FromConfiguration(config);
        var(data,sha)=await DataPackage.Load(root,verifyCopied?storage.MediaRoot:null);Media252Scope.Require(data,sha);
        if(integrityOnly)
        {
            if(!verifyCopied)throw new InvalidOperationException("Integrity-only only verifies existing files; never copies or publishes.");
            return await VerifyIntegrityOnly(root,config,storage,data,sha);
        }
        if(verifyCopied)
        {
            var folder=StorageOptions.ResolveWithin(storage.MediaRoot,"uat-data/"+data.PackageId.ToString("N"));DataPackage.NoReparse(folder);
            if(!Directory.Exists(folder)||Directory.EnumerateDirectories(folder).Any()||Directory.EnumerateFiles(folder).Count()!=703)throw new InvalidDataException("Thư mục package sau copy phải chứa đúng 703 file, không thêm file hoặc thư mục ngoài manifest.");
        }
        if(Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetFullPath(iisRoot),"web")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Đặt gói bàn giao ngoài webroot.");
        if(config["Sql:ExpectedDatabase"]!="website_tin_tuc"||UatDeployment.PhysicalServer(config)!="WIN-S1IBJB84OOO"||config["Deployment:ExpectedSqlServer"]!="45.120.229.43,1433")throw new UnauthorizedAccessException("Cấu hình runtime không đúng SQL đích; chưa đọc/ghi SQL.");
        if(!Directory.Exists(storage.MediaRoot)||!Directory.Exists(storage.TempRoot))throw new IOException("Kho media/temp runtime chưa tồn tại hoặc chưa cấp quyền hợp lệ.");
        var missingBytes=data.Media.Where(m=>!File.Exists(MediaService.ResolveMediaPath(storage,Media252Scope.Key(data.PackageId,m)))).Sum(m=>m.Bytes);
        var largest=data.Media.Max(m=>m.Bytes);var mediaDrive=Path.GetPathRoot(storage.MediaRoot)!;var tempDrive=Path.GetPathRoot(storage.TempRoot)!;
        if(new DriveInfo(mediaDrive).AvailableFreeSpace<missingBytes+largest+64L*1024*1024||new DriveInfo(tempDrive).AvailableFreeSpace<largest+64L*1024*1024)throw new IOException("Không đủ dung lượng trống cho media và spool scanner; chưa chuyển file.");
        if(config["Scanner:Provider"]=="MicrosoftDefenderOnPrem"&&!string.Equals(config["Scanner:DefenderServiceIdentity"],WindowsIdentity.GetCurrent().Name,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("SCANNER_IDENTITY_MISMATCH: cần chạy bằng tài khoản Windows scanner đã được cấu hình trên IIS. Không tự đổi identity hoặc cấp quyền.");
        var scanner=InquiryScannerFactory.Create(config,"Staging",48L*1024*1024);var health=await scanner.HealthAsync();
        if(!health.Ready)throw new InvalidOperationException("SCANNER_NOT_READY: "+health.Code+". Chạy bằng đúng tài khoản scanner đã cấu hình; không sửa policy/ACL tự động.");
        var identity=await Identity(scanner,config);if(identity==null)throw new InvalidOperationException("Không xác minh được engine/definitions thật tại IIS.");
        var state=Path.Combine(config["WorkspaceRoot"]!,".local","media-stage",data.PackageId.ToString("N"),ToolSha[..16]);DataPackage.NoReparse(state);var existingState=Directory.Exists(state);Directory.CreateDirectory(state);
        // Existing Windows permissions are untouched. Cached scan evidence
        // remains DPAPI CurrentUser protected and hash/engine/TTL validated.
        var entries=new List<TargetMediaEntry>();var blocked=new List<Guid>();var reused=0;var copied=0;
        var validator=new MediaService(null!,storage,new MediaUploadOptions{MaxPdfBytes=48L*1024*1024});
        foreach(var m in data.Media.OrderBy(m=>m.Id))
        {
            var key=Media252Scope.Key(data.PackageId,m);var dest=MediaService.ResolveMediaPath(storage,key);DataPackage.NoReparse(dest);var inputPath=verifyCopied?dest:DataPackage.PathIn(root,m.File);
            var cacheFiles=Directory.GetFiles(state,m.Id.ToString("N")+"-*.dpapi").OrderByDescending(f=>f,StringComparer.Ordinal).ToArray();
            TargetMediaEntry? cached=cacheFiles.Length==0?null:Cached(cacheFiles[0],Entropy(m.Id,sha));
            if(cached!=null&&cached.MediaId==m.Id&&cached.Sha256==m.Sha256&&cached.Bytes==m.Bytes&&cached.StorageKey==key&&Fresh(cached,identity,DateTimeOffset.UtcNow)&&File.Exists(dest)&&new FileInfo(dest).Length==m.Bytes&&await DataPackage.FileHash(dest)==m.Sha256){entries.Add(cached);reused++;continue;}
            try
            {
                await using var input=new FileStream(inputPath,FileMode.Open,FileAccess.Read,FileShare.Read);
                if(input.Length!=m.Bytes||Convert.ToHexStringLower(await SHA256.HashDataAsync(input))!=m.Sha256)throw new InvalidDataException("Source bytes changed.");
                validator.ValidateContent(inputPath,m.MimeType,CancellationToken.None);input.Position=0;
                var proof=await scanner.ScanAsync(input);var entry=new TargetMediaEntry(m.Id,m.Sha256,m.Bytes,key,DateTimeOffset.UtcNow,proof);
                if(!Fresh(entry,identity,DateTimeOffset.UtcNow)){blocked.Add(m.Id);Console.WriteLine(JsonSerializer.Serialize(new{mediaId=m.Id,status="BLOCKED_SCAN_OR_PROOF_METADATA",nativeScanState=proof.State.ToString(),copied=false}));continue;}
                if(!verifyCopied)Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if(verifyCopied&&!File.Exists(dest))throw new IOException("File đã copy bị thiếu; không tự thay thế hoặc công bố.");
                if(File.Exists(dest)){if(new FileInfo(dest).Length!=m.Bytes||await DataPackage.FileHash(dest)!=m.Sha256)throw new InvalidDataException("Existing destination differs; never overwrite.");}
                else
                {
                    var temporary=MediaService.ResolveMediaPath(storage,key+".partial-"+Guid.NewGuid().ToString("N"));DataPackage.NoReparse(temporary);input.Position=0;
                    await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){await input.CopyToAsync(output);await output.FlushAsync();}
                    if(new FileInfo(temporary).Length!=m.Bytes||await DataPackage.FileHash(temporary)!=m.Sha256)throw new InvalidDataException("Temporary copy hash differs; no final file created.");
                    var prefix=Path.GetFullPath(storage.MediaRoot).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                    if(!Path.GetFullPath(temporary).StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!Path.GetFullPath(dest).StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Atomic copy path escaped media storage.");
                    File.Move(temporary,dest,false);copied++;
                }
                if(new FileInfo(dest).Length!=m.Bytes||await DataPackage.FileHash(dest)!=m.Sha256)throw new InvalidDataException("Destination hash differs.");
                SaveCache(Path.Combine(state,m.Id.ToString("N")+"-"+DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")+".dpapi"),entry,Entropy(m.Id,sha));entries.Add(entry);
                if((entries.Count+blocked.Count)%10==0)Console.WriteLine(JsonSerializer.Serialize(new{checkedFiles=entries.Count+blocked.Count,total=703,clean=entries.Count,blocked=blocked.Count,copied,reused,sqlWrites=0}));
            }
            catch(Exception error)when(error is IOException or InvalidDataException or MediaUploadException or UnauthorizedAccessException)
            {blocked.Add(m.Id);Console.WriteLine(JsonSerializer.Serialize(new{mediaId=m.Id,status="BLOCKED",errorType=error.GetType().Name,sqlWrites=0}));}
        }
        var finalHealth=await scanner.HealthAsync();var finalIdentity=await Identity(scanner,config);
        foreach(var entry in entries.Where(e=>!Fresh(e,finalIdentity,DateTimeOffset.UtcNow)).ToArray()){entries.Remove(entry);blocked.Add(entry.MediaId);}
        var receipt=new TargetMediaReceipt(Kind,data.PackageId,sha,Environment.MachineName,WindowsIdentity.GetCurrent().User!.Value,"WIN-S1IBJB84OOO","website_tin_tuc",DataPackage.Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(storage.MediaRoot).ToUpperInvariant())),ToolSha,finalIdentity,DateTimeOffset.UtcNow,blocked.Count==0&&entries.Count==703&&finalHealth.Ready?"TARGET_MEDIA_READY_NOT_SQL_IMPORTED":"BLOCKED_PARTIAL_NO_SQL",entries,blocked.Distinct().ToList());
        var outFile=Path.Combine(Path.GetDirectoryName(root)!,"KET-QUA-MEDIA-"+DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ")+"-"+Guid.NewGuid().ToString("N")[..6]+".json");await DataPackage.Write(outFile,receipt);
        Console.WriteLine(JsonSerializer.Serialize(new{status=receipt.Status,clean=entries.Count,blocked=receipt.Blocked.Count,copied,reused,receipt=outFile,receiptSha256=await DataPackage.FileHash(outFile),sqlWrites=0,publication="NOT_RELEASED_UNTIL_SEPARATELY_APPROVED_SQL_IMPORT"}));return receipt.Status=="TARGET_MEDIA_READY_NOT_SQL_IMPORTED"?0:2;
    }
    static async Task<int> VerifyIntegrityOnly(string root,IConfiguration config,StorageOptions storage,DataBundle data,string sha)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows IIS required.");
        var folder=StorageOptions.ResolveWithin(storage.MediaRoot,"uat-data/"+data.PackageId.ToString("N"));DataPackage.NoReparse(folder);
        if(Directory.EnumerateDirectories(folder).Any()||Directory.EnumerateFiles(folder).Count()!=703)throw new InvalidDataException("Cần đúng 703 file, không có file/thư mục bổ sung.");
        var validator=new MediaService(null!,storage,new MediaUploadOptions{MaxPdfBytes=48L*1024*1024});var entries=new List<TargetMediaEntry>();
        foreach(var m in data.Media.OrderBy(m=>m.Id))
        {
            var key=Media252Scope.Key(data.PackageId,m);var path=MediaService.ResolveMediaPath(storage,key);
            await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(input.Length!=m.Bytes||Convert.ToHexStringLower(await SHA256.HashDataAsync(input))!=m.Sha256)throw new InvalidDataException("Byte/hash mismatch: "+path);
            validator.ValidateContent(path,m.MimeType,default);
            entries.Add(new(m.Id,m.Sha256,m.Bytes,key,DateTimeOffset.MinValue,new(InquiryScanState.NotScanned,"ANTIVIRUS_NOT_PERFORMED",null){Provider="ScannerSkipped"},DateTimeOffset.UtcNow));
        }
        var receipt=new TargetMediaReceipt(IntegrityKind,data.PackageId,sha,Environment.MachineName,WindowsIdentity.GetCurrent().User!.Value,"WIN-S1IBJB84OOO","website_tin_tuc",DataPackage.Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(storage.MediaRoot).ToUpperInvariant())),ToolSha,null,DateTimeOffset.UtcNow,IntegrityStatus,entries,[],true);
        Validate(receipt,data,sha,true);
        var file=Path.Combine(Path.GetDirectoryName(root)!,"KET-QUA-MEDIA-"+DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ")+"-"+Guid.NewGuid().ToString("N")[..6]+".json");await DataPackage.Write(file,receipt);
        Console.WriteLine(JsonSerializer.Serialize(new{status=receipt.Status,verified=703,scannerSkipped=703,clean=0,receipt=file,receiptSha256=await DataPackage.FileHash(file),sqlWrites=0,publication="ADMIN_EXCEPTION_AND_SCOPED_IMPORT_REQUIRED"}));return 0;
    }
}
