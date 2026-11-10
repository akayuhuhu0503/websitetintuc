[CmdletBinding()]
param([string]$IisPublishRoot='D:\WWW_GDLA\publish\TTDL-IIS')
# Exact schema18 runtime hotfix. No SQL writes, migration, private config or media copy.
$ErrorActionPreference='Stop'
function FileHash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function NoLink([string]$Path){for($p=[IO.Path]::GetFullPath($Path);$p;$p=[IO.Path]::GetDirectoryName($p)){if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse path refused'}}}
function CopyBytes([string]$Source,[string]$Destination){
 $inputFile=[IO.File]::OpenRead($Source)
 try{$outputFile=[IO.File]::Open($Destination,'Create','Write','None');try{$inputFile.CopyTo($outputFile);$outputFile.Flush($true)}finally{$outputFile.Dispose()}}finally{$inputFile.Dispose()}
 if((FileHash $Source) -cne (FileHash $Destination)){throw 'Binary copy hash mismatch'}
}
function RestoreFiles([string]$Root,[string]$Backup,[string[]]$Paths){foreach($relative in $Paths){CopyBytes (Join-Path $Backup $relative) (Join-Path $Root $relative)}}
function CheckHttp {
 Add-Type -AssemblyName System.Net.Http
 $handler=[Net.Http.HttpClientHandler]::new();$handler.AllowAutoRedirect=$false
 $client=[Net.Http.HttpClient]::new($handler);$client.Timeout=[TimeSpan]::FromSeconds(20)
 try{foreach($path in @('/','/tin-tuc','/admin/login','/health')){
  $response=$client.GetAsync('https://gdla.gov.vn'+$path).GetAwaiter().GetResult()
  try{[pscustomobject]@{Path=$path;Status=[int]$response.StatusCode}}finally{$response.Dispose()}
 }}finally{$client.Dispose()}
}
$root=[IO.Path]::GetFullPath($IisPublishRoot).TrimEnd('\')
if($root -ine 'D:\WWW_GDLA\publish\TTDL-IIS'){throw 'Exact reviewed IIS root required'}
NoLink $root;NoLink $PSScriptRoot
if($PSScriptRoot.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Extract hotfix outside the running publish directory'}
$deliveryPath=Join-Path $PSScriptRoot 'delivery.json'
if((FileHash $deliveryPath) -cne '__DELIVERY_SHA__'){throw 'Delivery manifest mismatch'}
$delivery=Get-Content -LiteralPath $deliveryPath -Raw|ConvertFrom-Json
foreach($item in $delivery.files){
 if($item.path -notmatch '^(payload|probe)/' -or $item.path -match '\\|:|(^|/)\.\.?(/|$)'){throw 'Unsafe delivery path'}
 $file=Join-Path $PSScriptRoot $item.path;NoLink $file
 if((FileHash $file) -cne $item.sha256){throw 'Delivery file hash mismatch'}
}
Add-Type -Path (Join-Path $env:windir 'System32/inetsrv/Microsoft.Web.Administration.dll')
$manager=[Microsoft.Web.Administration.ServerManager]::new()
try{
 $site=$manager.Sites['WWW_GDLA'];if(-not $site){throw 'WWW_GDLA site missing'}
 $physical=[Environment]::ExpandEnvironmentVariables($site.Applications['/'].VirtualDirectories['/'].PhysicalPath)
 if([IO.Path]::GetFullPath($physical).TrimEnd('\') -ine (Join-Path $root 'web')){throw 'IIS webroot mismatch'}
 if(-not @($site.Bindings|Where-Object {$_.Protocol -eq 'https' -and $_.Host -ceq 'gdla.gov.vn' -and $_.EndPoint.Port -eq 443}).Count){throw 'Exact HTTPS binding missing'}
 $poolName=$site.Applications['/'].ApplicationPoolName
 if(@($manager.Sites|ForEach-Object {$_.Applications}|Where-Object {$_.ApplicationPoolName -eq $poolName}).Count -ne 1){throw 'Shared app pool refused'}
 $pool=$manager.ApplicationPools[$poolName]
 if([string]$pool.State -ne 'Started'){throw 'App pool must be running before controlled update'}
}finally{$manager.Dispose()}
$manifestPath=Join-Path $root 'manifest.json';$config=Join-Path $root 'private/uat.json'
NoLink $manifestPath;NoLink $config
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
$configHash=FileHash $config
if($manifest.privateMode -cne 'Portable' -or $manifest.privateConfiguration.path -cne 'private/uat.json' -or $manifest.privateConfiguration.sha256 -cne $configHash){throw 'Portable private configuration binding mismatch'}
$paths=@('web/Infrastructure.dll','worker/Infrastructure.dll')
foreach($relative in $paths){
 $path=Join-Path $root $relative;NoLink $path
 $record=@($manifest.files|Where-Object {$_.path -ceq $relative})
 if($record.Count -ne 1 -or (FileHash $path) -cne $record[0].sha256){throw ('Current binary differs from manifest: '+$relative)}
 $deps=Join-Path $root $(if($relative.StartsWith('web/')){'web/Web.deps.json'}else{'worker/Worker.deps.json'})
 NoLink $deps;$metadata=Get-Content -LiteralPath $deps -Raw|ConvertFrom-Json
 $libraries=@($metadata.libraries.PSObject.Properties.Name)
 foreach($required in $delivery.requiredLibraries){if($required -notin $libraries){throw ('Incompatible dependency: '+$required)}}
}
if(Test-Path -LiteralPath (Join-Path $root 'web/app_offline.htm')){throw 'Existing maintenance session; no changes'}
$services=@(Get-CimInstance Win32_Service|Where-Object {$_.PathName -and $_.PathName.IndexOf((Join-Path $root 'worker'),[StringComparison]::OrdinalIgnoreCase) -ge 0})
$running=@($services|Where-Object {$_.State -eq 'Running'})
foreach($proc in Get-CimInstance Win32_Process){if($proc.CommandLine -and $proc.CommandLine.IndexOf((Join-Path $root 'worker'),[StringComparison]::OrdinalIgnoreCase) -ge 0 -and $proc.ProcessId -notin @($running.ProcessId)){throw 'Unmanaged Worker process; no automatic termination'}}
$id=[Guid]::NewGuid().ToString('N');$backup=Join-Path $root ('runtime-rollback/runtime18-'+$id)
NoLink $backup;[void][IO.Directory]::CreateDirectory($backup)
$oldEnv=$env:DOTNET_ENVIRONMENT;$oldAsp=$env:ASPNETCORE_ENVIRONMENT
$manager=$null;$changed=$false;$stopped=$false;$safeToRestart=$true
try{
 $env:DOTNET_ENVIRONMENT='Staging';$env:ASPNETCORE_ENVIRONMENT='Staging'
 $dotnet=(Get-Command dotnet -ErrorAction Stop).Source
 # Same interceptor as Web/Worker, SELECT-only; never calls migration/bootstrap.
 & $dotnet (Join-Path $PSScriptRoot 'probe/MigrationCli.dll') uat-sql-runtime-probe --home (Join-Path $root 'private') --config $config --report (Join-Path $root ('private/runtime18-probe-'+$id+'.json'))
 if($LASTEXITCODE -ne 0){throw 'Runtime18 SQL-only probe failed; no binary changed'}
 foreach($relative in ($paths+@('manifest.json'))){$to=Join-Path $backup $relative;[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to));CopyBytes (Join-Path $root $relative) $to}
 foreach($service in $running){Stop-Service -Name $service.Name -ErrorAction Stop;(Get-Service $service.Name).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}
 $manager=[Microsoft.Web.Administration.ServerManager]::new();$pool=$manager.ApplicationPools[$poolName]
 [void]$pool.Stop();$stopped=$true
 $limit=[DateTime]::UtcNow.AddSeconds(40)
 do{$unlocked=$true;foreach($relative in $paths){try{$stream=[IO.File]::Open((Join-Path $root $relative),'Open','ReadWrite','None');$stream.Dispose()}catch{$unlocked=$false}};if(-not $unlocked){Start-Sleep -Milliseconds 500}}while(-not $unlocked -and [DateTime]::UtcNow -lt $limit)
 if(-not $unlocked){throw 'Runtime files still locked'}
 $changed=$true;$safeToRestart=$false
 foreach($relative in $paths){CopyBytes (Join-Path $PSScriptRoot 'payload/Infrastructure.dll') (Join-Path $root $relative)}
 foreach($record in $manifest.files){if($record.path -in $paths){$record.sha256=FileHash (Join-Path $root $record.path);$record.bytes=(Get-Item -LiteralPath (Join-Path $root $record.path)).Length}}
 $manifest|Add-Member -NotePropertyName runtime18Hotfix -NotePropertyValue @{id=$id;utc=[DateTimeOffset]::UtcNow.ToString('O');rollback=$backup} -Force
 [IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 40),[Text.UTF8Encoding]::new($false))
 if((FileHash $config) -cne $configHash){throw 'Private configuration changed unexpectedly'}
 [void]$pool.Start();$stopped=$false
 $http=@();for($attempt=0;$attempt -lt 3;$attempt++){
  try{$http=@(CheckHttp)}catch{$http=@()}
  if($http.Count -eq 4 -and @($http|Where-Object {$_.Status -ne 200}).Count -eq 0){break}
  if($attempt -lt 2){Start-Sleep -Seconds 3}
 }
 if($http.Count -ne 4 -or @($http|Where-Object {$_.Status -ne 200}).Count){throw 'HTTP verification failed; restoring pre-update binary, not SQL'}
 foreach($service in $running){Start-Service -Name $service.Name -ErrorAction Stop}
 $safeToRestart=$true
 @{status='UPDATED_HTTP_4_PASS';http=$http;rollback=$backup;sqlWrites=0;configUnchanged=$true}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $backup 'result.json') -Encoding UTF8
 Write-Output ('PASS: / /tin-tuc /admin/login /health HTTP 200. Rollback files: '+$backup)
}catch{
 if($changed){
  foreach($service in $running){if((Get-Service $service.Name).Status -ne 'Stopped'){Stop-Service $service.Name;(Get-Service $service.Name).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}}
  if($manager){$pool=$manager.ApplicationPools[$poolName];if([string]$pool.State -eq 'Started'){[void]$pool.Stop();$stopped=$true}}
  Start-Sleep -Seconds 3
  RestoreFiles $root $backup ($paths+@('manifest.json'))
  $safeToRestart=$true
 }
 @{status='BLOCKED';binaryRestored=$changed;sqlWrites=0;exceptionType=$_.Exception.GetType().Name}|ConvertTo-Json|Set-Content (Join-Path $backup 'result.json') -Encoding UTF8
 Write-Output ('BLOCKED. Previous binary preserved/restored; this does not guarantee old binary supports schema18. Report: '+$backup)
 throw
}finally{
 if($safeToRestart){
  if($stopped -and $manager){[void]$manager.ApplicationPools[$poolName].Start()}
  foreach($service in $running){if((Get-Service $service.Name).Status -eq 'Stopped'){Start-Service $service.Name}}
 }else{Write-Warning 'Rollback incomplete. Keep pool/workers stopped; reconcile saved binary before restart.'}
 if($manager){$manager.Dispose()};$env:DOTNET_ENVIRONMENT=$oldEnv;$env:ASPNETCORE_ENVIRONMENT=$oldAsp
}
