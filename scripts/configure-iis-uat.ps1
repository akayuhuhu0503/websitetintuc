[CmdletBinding()]
param([string]$PrivateConfig,[string]$SiteName,
 [string]$PublishRoot=(Split-Path -Parent $PSScriptRoot),
 [string]$DataHome,[string]$HostName,[string]$ApprovalFile,[switch]$Apply)
# Target-local preparation only. Default is read-only discovery. No SQL, service
# startup, account creation, certificate import or Defender policy modification.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'uat-common.ps1')

function Resolve-IisUatSettings($Settings,$Target,[string]$HomePath,$Approval) {
 $c=$Settings|ConvertTo-Json -Depth 16|ConvertFrom-Json
 if($Target.HostName -match 'REPLACE|[\r\n]' -or [Uri]::CheckHostName($Target.HostName) -ne 'Dns' -or $Target.Port -lt 1 -or $Target.Port -gt 65535){throw 'Actual named IIS HTTPS binding required'}
 $targetDataHome=Get-UatSafePath $HomePath
 $c.Deployment.ExpectedHost=$Target.HostName
 $c.Public.BaseUrl='https://'+$Target.HostName+$(if($Target.Port -eq 443){'/'}else{':'+$Target.Port+'/'})
 $c.Storage.MediaRoot=Join-Path $targetDataHome 'media';$c.Storage.SnapshotRoot=Join-Path $targetDataHome 'snapshots'
 $c.Storage.TempRoot=Join-Path $targetDataHome 'temp';$c.Storage.KeysRoot=Join-Path $targetDataHome 'keys'
 $c.Scanner.Provider='MicrosoftDefenderOnPrem';$c.Scanner.DefenderBinarySha256=$Target.DefenderSha256
 $c.Scanner.DefenderServiceIdentity=$Target.ServiceAccount
 $c.Scanner.PolicyReference='';$c.Scanner.DefenderPrivacyPolicyReference=''
 $c.Deployment.TargetApproved='false';$c.Deployment.TargetApprovalReference=''
 if($Approval){
  if($Approval.Status -cne 'APPROVED' -or $Approval.MachineName -ine $Target.MachineName -or $Approval.SiteName -cne $Target.SiteName -or
     $Approval.PhysicalSqlServer -cne $c.Deployment.ExpectedSqlServerIdentity -or $Approval.Database -cne $c.Sql.ExpectedDatabase){throw 'Real authority must name this IIS machine/site and exact SQL target'}
  foreach($field in @('DeploymentReference','ScannerPolicyReference','DefenderPrivacyPolicyReference')){
   $reference=[string]$Approval.$field
   if($reference.Length -lt 5 -or $reference -match 'REPLACE|PENDING|FIXTURE|[\r\n]'){throw 'Approved authority reference required; discovery is not approval'}
  }
  $c.Deployment.TargetApproved='true';$c.Deployment.TargetApprovalReference=$Approval.DeploymentReference
  $c.Scanner.PolicyReference=$Approval.ScannerPolicyReference;$c.Scanner.DefenderPrivacyPolicyReference=$Approval.DefenderPrivacyPolicyReference
 }
 if($c.Mail.Transport -cne 'Disabled' -or [string]$c.Mail.DeliveryEnabled -ine 'false'){throw 'SMTP must remain Disabled for this handoff'}
 return $c
}

function Assert-IisUatCertificate($Certificate,[string]$BindingHost) {
 if(-not $Certificate.HasPrivateKey -or $Certificate.NotBefore -gt [DateTime]::Now -or $Certificate.NotAfter -le [DateTime]::Now){throw 'IIS HTTPS certificate needs a current private key'}
 $serverEku=@($Certificate.EnhancedKeyUsageList|Where-Object {$_.ObjectId -eq '1.3.6.1.5.5.7.3.1'})
 if(-not $serverEku.Count){throw 'IIS HTTPS certificate must permit Server Authentication'}
 $match=$false
 foreach($name in @($Certificate.DnsNameList|ForEach-Object {$_.Unicode})){
  if($name -ieq $BindingHost -or ($name.StartsWith('*.') -and $BindingHost -ilike $name -and ($BindingHost -split '\.').Count -eq ($name -split '\.').Count)){$match=$true}
 }
 if(-not $match){throw 'IIS HTTPS CN/SAN does not match actual binding'}
 $chain=New-Object Security.Cryptography.X509Certificates.X509Chain
 try{if(-not $chain.Build($Certificate)){throw 'IIS HTTPS certificate trust chain failed'}}finally{$chain.Dispose()}
}

function Get-IisUatDefender {
 $state=Get-MpComputerStatus;$privacy=Get-MpPreference
 $platform=Join-Path $env:ProgramData 'Microsoft/Windows Defender/Platform'
 $binary=Get-ChildItem -LiteralPath $platform -Directory -ErrorAction Stop|Sort-Object Name -Descending|ForEach-Object {Join-Path $_.FullName 'MpCmdRun.exe'}|Where-Object {Test-Path -LiteralPath $_ -PathType Leaf}|Select-Object -First 1
 if(-not $binary){$binary=Join-Path $env:ProgramFiles 'Windows Defender/MpCmdRun.exe'}
 $binary=Get-UatSafePath $binary
 $signature=Get-AuthenticodeSignature -LiteralPath $binary
 $ready=$state.AMServiceEnabled -and $state.AntivirusEnabled -and $state.AMRunningMode -eq 'Normal' -and $signature.Status -eq 'Valid' -and $signature.SignerCertificate.Subject -match 'Microsoft' -and $state.AntivirusSignatureLastUpdated.ToUniversalTime() -gt [DateTime]::UtcNow.AddHours(-72)
 [pscustomobject]@{Sha256=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant();NativeReady=[bool]$ready;PrivacyReady=([int]$privacy.SubmitSamplesConsent -eq 2 -and [int]$privacy.MAPSReporting -eq 0)}
}

# Dot sourcing exposes only pure helpers for bounded offline tests.
if($MyInvocation.InvocationName -eq '.'){return}
if(-not $PrivateConfig -or -not $SiteName){throw 'PrivateConfig and actual IIS SiteName required; see START.md'}
$code=Get-UatSafePath $PublishRoot;$source=Get-UatSafePath $PrivateConfig
if($source -ieq $code -or $source.StartsWith($code+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Private input must be outside publish/webroot'}
$settings=Get-Content -LiteralPath $source -Raw -Encoding UTF8|ConvertFrom-Json
. (Join-Path $PSScriptRoot 'iis-env.ps1')
Assert-IisPublishConfiguration $settings
$manifestFile=Join-Path $code 'manifest.json'
$manifest=Get-Content -LiteralPath $manifestFile -Raw -Encoding UTF8|ConvertFrom-Json
$marker=Get-Content -LiteralPath (Join-Path $code 'build-only.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($marker.kind -cne 'TTDL-IIS-BUILD-ONLY' -or $marker.sealed -ne $false -or $marker.manifestSha256 -ine (Get-FileHash -LiteralPath $manifestFile -Algorithm SHA256).Hash -or $manifest.status -cne 'PUBLISHED_CONFIG_PREPARED_NOT_DEPLOYED'){throw 'Expected intact one-command publish packet required'}
foreach($required in @('web/Web.dll','web/web.config','worker/Worker.dll','tools/MigrationCli.dll','ops/uat-runtime.ps1','ops/configure-iis-uat.ps1')){
 if($required -cnotin @($manifest.files|ForEach-Object {$_.path})){throw 'Manifest does not cover required runtime files'}
}
foreach($record in $manifest.files){
 if([string]$record.path -match '(^[\\/]|^[A-Za-z]:|(^|[\\/])\.\.([\\/]|$))'){throw 'Invalid manifest relative path'}
 $file=Get-UatSafePath (Join-Path $code $record.path)
 if(-not $file.StartsWith($code+'\',[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $record.sha256){throw 'Publish integrity failed; no target change'}
}
Add-Type -Path (Join-Path $env:windir 'System32/inetsrv/Microsoft.Web.Administration.dll')
$manager=New-Object Microsoft.Web.Administration.ServerManager
try{
 $site=$manager.Sites[$SiteName];if(-not $site){throw 'Configure an allocated IIS site (stopped) first'}
 $web=Get-UatSafePath ([Environment]::ExpandEnvironmentVariables($site.Applications['/'].VirtualDirectories['/'].PhysicalPath))
 if($web -ine (Join-Path $code 'web')){throw 'IIS physical path must be this package web directory'}
 $pool=$manager.ApplicationPools[$site.Applications['/'].ApplicationPoolName]
 if(-not $pool -or $pool.ProcessModel.IdentityType.ToString() -cne 'SpecificUser' -or $pool.ManagedRuntimeVersion -ne '' -or -not $pool.ProcessModel.LoadUserProfile){throw 'Dedicated No Managed Code pool with LoadUserProfile and existing custom service account required for Web + five workers'}
 $account=[string]$pool.ProcessModel.UserName
 if($account -match '^(?:IIS APPPOOL|NT AUTHORITY)\\|(?:^|\\)(?:Administrator|ADMIN|SYSTEM|LocalService|NetworkService)$'){throw 'Use an allocated least-privilege service account, not an administrator/built-in identity'}
 $serviceSid=(New-Object Security.Principal.NTAccount $account).Translate([Security.Principal.SecurityIdentifier])
 $shared=@(foreach($s in $manager.Sites){foreach($app in $s.Applications){if($app.ApplicationPoolName -eq $pool.Name -and ($s.Name -cne $SiteName -or $app.Path -cne '/')){$app.Path}}})
 if($shared.Count){throw 'Dedicated pool required; unrelated IIS applications must not share the runtime identity'}
 $bindings=@($site.Bindings|Where-Object {$_.Protocol -ceq 'https' -and (-not $HostName -or $_.Host -ieq $HostName)})
 if($bindings.Count -ne 1){throw 'Select one actual named HTTPS binding with -HostName when multiple exist'}
 $binding=$bindings[0]
 $thumb=([BitConverter]::ToString($binding.CertificateHash)).Replace('-','')
 $store=if($binding.CertificateStoreName){$binding.CertificateStoreName}else{'My'}
 if($store -notin @('My','WebHosting') -or $thumb -notmatch '^[a-fA-F0-9]{40}$'){throw 'Existing machine HTTPS certificate binding required'}
 $certificate=Get-Item -LiteralPath ('Cert:\LocalMachine\'+$store+'\'+$thumb)
 Assert-IisUatCertificate $certificate $binding.Host
 $siteId=$site.Id;$siteStopped=$site.State.ToString() -ceq 'Stopped';$poolStopped=$pool.State.ToString() -ceq 'Stopped'
 $target=[pscustomobject]@{HostName=$binding.Host;Port=$binding.EndPoint.Port;SiteName=$SiteName;MachineName=$env:COMPUTERNAME;ServiceAccount=$account;DefenderSha256=''}
}finally{$manager.Dispose()}
if(-not $DataHome){$DataHome=Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) ('LandPortal\Site-'+$siteId)}
$targetDataHome=Get-UatSafePath $DataHome;$config=Join-Path $targetDataHome 'uat.json'
Assert-UatSeparatePaths $targetDataHome $config $code
# A cloned source tree is not a private storage location, even outside webroot.
for($node=$targetDataHome;$node;$node=[IO.Path]::GetDirectoryName($node)){if(Test-Path -LiteralPath (Join-Path $node '.git')){throw 'Private storage cannot be inside a Git working tree'}}
$defender=Get-IisUatDefender;$target.DefenderSha256=$defender.Sha256
$approval=if($ApprovalFile){Get-Content -LiteralPath (Get-UatSafePath $ApprovalFile) -Raw -Encoding UTF8|ConvertFrom-Json}else{$null}
$c=Resolve-IisUatSettings $settings $target $targetDataHome $approval
$runtimeReady=$false
try{Assert-UatRuntimeConfiguration $c $targetDataHome $config $code;$runtimeReady=$true}catch{if($Apply){throw};Write-Output 'ACTIVATION BLOCKED: target/scanner authority or runtime prerequisites remain incomplete; no changes made'}
if(-not $defender.NativeReady -or -not $defender.PrivacyReady){$runtimeReady=$false;Write-Output 'ACTIVATION BLOCKED: Defender health/privacy policy requires TTDL action; no AV policy changed'}
Write-Output ('IIS discovery: '+$target.MachineName+'/'+$SiteName+'; '+$c.Public.BaseUrl+'; data home '+$targetDataHome+'; service '+$account)
if(-not $Apply){Write-Output ('PLAN ONLY; activation configuration ready='+$runtimeReady+'; SQL runtime/ACL/E2E NOT RUN');return}
if(-not $runtimeReady -or -not $siteStopped -or -not $poolStopped){throw 'Apply requires all runtime/authority guards and a stopped site/pool; nothing started'}
if(Test-Path -LiteralPath $targetDataHome){throw 'New private home required; existing storage/config/ACL never overwritten'}
$security=New-Object Security.AccessControl.DirectorySecurity
$security.SetAccessRuleProtection($true,$false)
foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'))){$security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))}
$security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($serviceSid,'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')))
[void][IO.Directory]::CreateDirectory($targetDataHome,$security)
foreach($name in @('MediaRoot','SnapshotRoot','TempRoot','KeysRoot')){
 $path=[string]$c.Storage.$name;[void][IO.Directory]::CreateDirectory($path)
 $acl=Get-Acl -LiteralPath $path;$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($serviceSid,'Modify','ContainerInherit,ObjectInherit','None','Allow')));Set-Acl -LiteralPath $path -AclObject $acl
}
$encoding=[Text.UTF8Encoding]::new($false)
# CreateNew avoids overwriting private input; never print settings or secrets.
$stream=[IO.File]::Open($config,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$bytes=$encoding.GetBytes(($c|ConvertTo-Json -Depth 16));$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
$webConfig=Join-Path $code 'web/web.config'
Copy-Item -LiteralPath $webConfig -Destination (Join-Path $targetDataHome 'web.config.before')
[xml]$xml=Get-Content -LiteralPath $webConfig -Raw -Encoding UTF8
foreach($v in @($xml.configuration.location.'system.webServer'.aspNetCore.environmentVariables.environmentVariable)){
 # Remove packaging's fail-closed override; authority now comes from the
 # validated private JSON, so later revocation there cannot be masked by IIS.
 if($v.name -ceq 'Deployment__TargetApproved'){[void]$v.ParentNode.RemoveChild($v);continue}
 if($v.name -ceq 'LANDPORTAL_HOME'){$v.SetAttribute('value',$targetDataHome)}
 if($v.name -ceq 'LANDPORTAL_CONFIG'){$v.SetAttribute('value',$config)}
}
[IO.File]::WriteAllText($webConfig,$xml.OuterXml,$encoding)
$activation=@{utc=[DateTimeOffset]::UtcNow.ToString('O');status='CONFIGURED_NOT_STARTED';site=$SiteName;machine=$env:COMPUTERNAME;inputManifestSha256=(Get-FileHash -LiteralPath $manifestFile -Algorithm SHA256).Hash.ToLowerInvariant();boundWebConfigSha256=(Get-FileHash -LiteralPath $webConfig -Algorithm SHA256).Hash.ToLowerInvariant();runtimeConfig=$config;dataHome=$targetDataHome;sqlExecuted=$false;servicesStarted=0;workerModes=@('Scheduler','Documents','InquiryScan','Video','Mail');authorityReference=$c.Deployment.TargetApprovalReference;preflight='REQUIRED_AS_SERVICE_IDENTITY'}
[IO.File]::WriteAllText((Join-Path $targetDataHome 'activation.json'),($activation|ConvertTo-Json -Depth 6),$encoding)
Write-Output ('TARGET CONFIG PREPARED: '+$config+'; Web + all five worker modes share this file. Run read-only online Preflight as the service account before starting IIS/workers.')
Write-Output 'Original manifest unchanged; web.config target overlay recorded privately in activation.json. Nothing started; no SQL connection or write.'
