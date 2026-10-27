using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandPortal.Domain;
using Microsoft.Extensions.Configuration;
namespace LandPortal.Infrastructure;

public static class InquiryScannerFactory
{
    public static IInquiryScanner Create(IConfiguration config,string environment,long maximumBytes=InquiryFileOptions.MaximumBytes)
    {
        if(maximumBytes<1||maximumBytes>1024L*1024*1024)throw new InvalidOperationException("Scanner byte limit is invalid.");
        return config["Scanner:Provider"] switch{null or "" or "Clamd"=>new ClamdInquiryScanner(config,maximumBytes),"DefenderTest"=>new DefenderInquiryScanner(config,environment,maximumBytes),"MicrosoftDefenderOnPrem"=>DefenderInquiryScanner.ForApprovedUat(config,environment,maximumBytes),LanStatisticsSamples.Provider=>DefenderInquiryScanner.ForApprovedLanSamples(config,environment,maximumBytes),_=>throw new InvalidOperationException("Unsupported scanner provider; no automatic Clean fallback.")};
    }
}

// Existing signed Windows engine only; no install, policy change, exclusions, or implicit production provider.
// Test bytes are explicitly allowlisted PUBLIC/ANONYMOUS copies. The separate
// Staging UAT constructor also requires an approved private-host policy, exact
// service identity, signed binary pin and disabled cloud/sample submission.
public sealed class DefenderInquiryScanner:IInquiryScanner
{
    private readonly string temp;
    private readonly HashSet<string>? approved;
    private readonly bool onPrem;
    private readonly bool lanSample;
    private readonly string? binaryPin;
    private readonly int seconds;
    private readonly long maximumBytes;
    public DefenderInquiryScanner(IConfiguration config,string environment,long maximumBytes=InquiryFileOptions.MaximumBytes):this(config,environment,maximumBytes,false){}
    public static IInquiryScanner ForApprovedUat(IConfiguration config,string environment,long maximumBytes)
    {
        if(config["Scanner:Provider"]!="MicrosoftDefenderOnPrem"||!UatDeployment.Validate(config,environment))throw new InvalidOperationException("Defender on-prem requires the approved isolated Staging UAT profile, never a Test/Production bypass.");
        return new DefenderInquiryScanner(config,environment,maximumBytes,true);
    }
    public static IInquiryScanner ForApprovedLanSamples(IConfiguration config,string environment,long maximumBytes)
    {LanStatisticsSamples.Require(config,environment);if(maximumBytes>LandStatisticsService.MaximumBytes)throw new InvalidOperationException("LAN sample scanner is limited to 10 MiB statistics workbooks.");return new DefenderInquiryScanner(config,environment,maximumBytes,false,true);}
    private DefenderInquiryScanner(IConfiguration config,string environment,long maximumBytes,bool approvedUat,bool approvedLanSample=false)
    {
        onPrem=approvedUat;
        lanSample=approvedLanSample;
        if(maximumBytes<1||maximumBytes>1024L*1024*1024)throw new InvalidOperationException("Scanner byte limit is invalid.");this.maximumBytes=maximumBytes;
        var root=config["WorkspaceRoot"]??throw new InvalidOperationException("Workspace required.");var storage=StorageOptions.FromConfiguration(config);
        var prefix=Path.Combine(root,".local","tests")+Path.DirectorySeparatorChar;
        if(onPrem)
        {
            if(!OperatingSystem.IsWindows()||environment!="Staging"||!UatDeployment.Validate(config,environment)||config["Scanner:DefenderServiceIdentity"] is not string identity||!string.Equals(identity,System.Security.Principal.WindowsIdentity.GetCurrent().Name,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Defender UAT must run under the explicitly approved Windows service identity.");
            binaryPin=config["Scanner:DefenderBinarySha256"]!.ToLowerInvariant();
        }
        else if(lanSample)
        {
            LanStatisticsSamples.Require(config,environment);approved=LanStatisticsSamples.Hashes().ToHashSet(StringComparer.Ordinal);binaryPin=config["Scanner:DefenderBinarySha256"]!.ToLowerInvariant();
        }
        else
        {
            if(!OperatingSystem.IsWindows()||environment!="Development"||config["Sql:ExpectedDatabase"]!="LandPortal_Test"||config["Scanner:DefenderAnonymousTestOnly"]!="true"||new[]{storage.MediaRoot,storage.TempRoot,storage.KeysRoot}.Any(x=>!Path.GetFullPath(x).StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("DefenderTest requires isolated local SQL Test, anonymous/public hash allowlist and explicit Development opt-in.");
            approved=(config["Scanner:DefenderAllowedTestHashes"]??"").Split(',',StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            if(approved.Count==0||approved.Any(x=>x.Length!=64||!x.All(Uri.IsHexDigit)||x!=x.ToLowerInvariant()))throw new InvalidOperationException("Anonymous/public Test hashes must be approved explicitly.");
        }
        temp=StorageOptions.ResolveWithin(storage.TempRoot,"defender-scan");Directory.CreateDirectory(temp);
        if((File.GetAttributes(temp)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Scanner private spool must not be a reparse path.");
        seconds=int.TryParse(config["Scanner:TimeoutSeconds"],out var t)&&t is >=1 and <=120?t:30;
    }
    private sealed record Health(bool Active,string Binary,string Engine,string Signatures,DateTimeOffset Updated,string Signed,bool PrivacyReady=false);
    private static async Task<(int Exit,string Output)> Run(ProcessStartInfo info,int limit,CancellationToken ct)
    {
        info.UseShellExecute=false;info.CreateNoWindow=true;info.RedirectStandardOutput=true;info.RedirectStandardError=true;
        using var process=Process.Start(info)??throw new IOException("ScannerProcessUnavailable");
        async Task<string> Read(StreamReader reader){var chars=new char[1024];var text=new System.Text.StringBuilder();int n;while((n=await reader.ReadAsync(chars,ct))>0){if(text.Length+n>32768)throw new IOException("ScannerOutputLimit");text.Append(chars,0,n);}return text.ToString();}
        var stdout=Read(process.StandardOutput);var stderr=Read(process.StandardError);using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(limit));
        try{await process.WaitForExitAsync(deadline.Token);return(process.ExitCode,(await stdout)+(await stderr));}
        finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync(CancellationToken.None);}}
    }
    private async Task<Health?> Probe(CancellationToken ct)
    {
        var script="& { $ErrorActionPreference='Stop'; $d=Get-MpComputerStatus; $p=Join-Path $env:ProgramData 'Microsoft/Windows Defender/Platform'; $b=if(Test-Path -LiteralPath $p){Get-ChildItem -LiteralPath $p -Directory|Sort-Object Name -Descending|ForEach-Object{Join-Path $_.FullName 'MpCmdRun.exe'}|Where-Object{Test-Path -LiteralPath $_}|Select-Object -First 1}; if(-not $b){$b=Join-Path $env:ProgramFiles 'Windows Defender/MpCmdRun.exe'}; $s=Get-AuthenticodeSignature -LiteralPath $b; @{Active=($d.AMServiceEnabled -and $d.AntivirusEnabled -and $d.AMRunningMode -eq 'Normal'); Binary=$b; Engine=$d.AMEngineVersion; Signatures=$d.AntivirusSignatureVersion; Updated=$d.AntivirusSignatureLastUpdated.ToUniversalTime().ToString('O'); Signed=if($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -match 'Microsoft'){'MicrosoftValid'}else{'Invalid'}}|ConvertTo-Json -Compress }";
        if(onPrem)script=script.Replace("$d=Get-MpComputerStatus;","$d=Get-MpComputerStatus; $privacy=Get-MpPreference;").Replace("@{Active=","@{PrivacyReady=($privacy.SubmitSamplesConsent -eq 2 -and $privacy.MAPSReporting -eq 0); Active=");
        var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"));info.ArgumentList.Add("-NoProfile");info.ArgumentList.Add("-NonInteractive");info.ArgumentList.Add("-Command");info.ArgumentList.Add(script);
        var result=await Run(info,10,ct);if(result.Exit!=0)return null;
        var health=JsonSerializer.Deserialize<Health>(result.Output);
        if((onPrem||lanSample)&&health!=null&&Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(health.Binary,ct)))!=binaryPin)return null;
        return health is {Active:true,Signed:"MicrosoftValid"}&&(!onPrem||health.PrivacyReady)&&health.Updated<=DateTimeOffset.UtcNow.AddMinutes(5)&&health.Updated>DateTimeOffset.UtcNow.AddHours(-72)&&Regex.IsMatch(health.Engine,@"^\d+(\.\d+){2,4}$")&&Regex.IsMatch(health.Signatures,@"^\d+(\.\d+){2,4}$")?health:null;
    }
    public async Task<(bool Ready,string Code)> HealthAsync(CancellationToken cancellation=default)
    {
        try{return await Probe(cancellation)!=null?(true,"DefenderSignedActiveRecentSignatures"):(false,"DefenderUnavailableStaleOrUntrusted");}
        catch(Exception e)when(e is IOException or System.ComponentModel.Win32Exception or OperationCanceledException or JsonException){return(false,"DefenderHealthUnavailable");}
    }
    // Recovery reuse must bind the currently verified engine and definition set, not just a provider name.
    public async Task<string?> CurrentIdentityAsync(CancellationToken ct=default)
    {
        try{var health=await Probe(ct);return health is null?null:$"{(onPrem?"DefenderOnPrem":lanSample?"DefenderLanSample":"Defender")}/{health.Engine} sig/{health.Signatures} updated/{health.Updated:O}";}
        catch(Exception e)when(e is IOException or System.ComponentModel.Win32Exception or OperationCanceledException or JsonException){return null;}
    }
    public async Task<InquiryScanResult> ScanAsync(Stream input,CancellationToken cancellation=default)
    {
        using var sample=lanSample?new MemoryStream():null;
        if(sample!=null)
        {
            var buffer=new byte[65536];int read;while((read=await input.ReadAsync(buffer,cancellation))>0){if(sample.Length+read>maximumBytes)return new(InquiryScanState.ScanFailed,"ScannerFileTooLarge",null);await sample.WriteAsync(buffer.AsMemory(0,read),cancellation);}
            if(!approved!.Contains(LanStatisticsSamples.Sha(sample.ToArray())))return new(InquiryScanState.ScanFailed,"NotApprovedAnonymousLanSample",null);
            sample.Position=0;input=sample;
        }
        var path=StorageOptions.ResolveWithin(temp,Guid.NewGuid().ToString("N")+".bin");
        try
        {
            long bytes=0;using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];
            await using(var copy=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){int n;while((n=await input.ReadAsync(buffer,cancellation))>0){bytes+=n;if(bytes>maximumBytes)return new(InquiryScanState.ScanFailed,"ScannerFileTooLarge",null);digest.AppendData(buffer,0,n);await copy.WriteAsync(buffer.AsMemory(0,n),cancellation);}}
            var hash=Convert.ToHexStringLower(digest.GetHashAndReset());if(bytes==0||approved!=null&&!approved.Contains(hash))return new(InquiryScanState.ScanFailed,"NotApprovedAnonymousTestBytes",null);
            var health=await Probe(cancellation);if(health==null)return new(InquiryScanState.ScanFailed,"DefenderUnavailableStaleOrUntrusted",null);
            var info=new ProcessStartInfo(health.Binary){WorkingDirectory=temp};foreach(var a in new[]{"-Scan","-ScanType","3","-File",path,"-DisableRemediation"})info.ArgumentList.Add(a);info.Environment["TEMP"]=temp;info.Environment["TMP"]=temp;
            var result=await Run(info,seconds,cancellation);var after=await Probe(cancellation);
            if(after==null||after.Signatures!=health.Signatures||after.Engine!=health.Engine)return new(InquiryScanState.ScanFailed,"EngineDefinitionsChangedDuringScan",null);
            await using(var check=File.OpenRead(path)){if(check.Length!=bytes||Convert.ToHexStringLower(await SHA256.HashDataAsync(check,cancellation))!=hash)return new(InquiryScanState.ScanFailed,"ScannerCopyChangedOrRemediated",null);}
            var engine=$"{(onPrem?"DefenderOnPrem":lanSample?"DefenderLanSample":"Defender")}/{health.Engine} sig/{health.Signatures} updated/{health.Updated:O}";
            var noThreat=$"Scanning {path} found no threats.";
            var detected=Regex.Match(result.Output,@"(?m)^Threat\s*:\s*([A-Za-z0-9_:/.-]{1,120})\s*$");
            var finished=result.Output.Split('\n').Any(x=>x.Trim()=="Scan finished.");
            var state=finished&&result.Exit==0&&result.Output.Split('\n').Any(x=>x.Trim()==noThreat)&&!detected.Success?InquiryScanState.Clean:finished&&result.Exit==2&&detected.Success&&Regex.IsMatch(result.Output,"Scanning "+Regex.Escape(path)+@" found [1-9]\d* threats\.")?InquiryScanState.Infected:InquiryScanState.ScanFailed;
            return new(state,state==InquiryScanState.Infected?detected.Groups[1].Value:state==InquiryScanState.ScanFailed?"DefenderOutputNotConclusive":"",engine){Provider=onPrem?"MicrosoftDefenderOnPrem":lanSample?LanStatisticsSamples.Provider:"MicrosoftDefender",SignatureVersion=health.Signatures,SignatureUpdatedUtc=health.Updated,OutputSha256=Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(result.Output.Replace(path,onPrem?"<private-scan>":"<anonymous-test>"))))};
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or OperationCanceledException or JsonException){return new(InquiryScanState.ScanFailed,"DefenderUnavailableTimeoutOrFileChanged",null);}
        finally{try{if(File.Exists(path))File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
