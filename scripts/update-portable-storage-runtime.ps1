[CmdletBinding()]
param([string]$IisPublishRoot='D:\WWW_GDLA\publish\TTDL-IIS')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($IisPublishRoot).TrimEnd('\')
if($root -ine 'D:\WWW_GDLA\publish\TTDL-IIS'){throw 'Gói sửa này chỉ dành cho đúng TTDL-IIS đã xác nhận.'}
function Assert-NoLink([string]$Path){
 for($p=[IO.Path]::GetFullPath($Path);$p;$p=[IO.Path]::GetDirectoryName($p)){
  if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Không chấp nhận đường dẫn reparse.'}
 }
}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Write-BinaryBytes([string]$Source,[string]$Target){
 # Opening the existing destination preserves its Windows security descriptor;
 # do not copy ACL/security metadata from the extracted delivery file.
 $inputStream=[IO.File]::Open($Source,'Open','Read','Read')
 try{$outputStream=[IO.File]::Open($Target,'Create','Write','None');try{$inputStream.CopyTo($outputStream);$outputStream.Flush($true)}finally{$outputStream.Dispose()}}finally{$inputStream.Dispose()}
}
function Runtime-Path([string]$Relative){
 if($Relative -notmatch '^(web|worker|tools)/' -or $Relative -match '\\|:|(^|/)\.\.?(/|$)' -or $Relative -match '(^|/)(web\.config|appsettings[^/]*\.json|\.env[^/]*)$'){throw 'Đường dẫn cập nhật ngoài phạm vi binary.'}
 $p=[IO.Path]::GetFullPath((Join-Path $root $Relative))
 if(-not $p.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Đường dẫn cập nhật vượt gói IIS.'}
 Assert-NoLink $p
 return $p
}
function Invoke-MediaTool([string]$Operation){
 $modeArgs=@();if($Operation -in @('media-storage-preflight','verify-copied-media')){$modeArgs=@('--integrity-only')}
 $lines=& $script:dotnet $script:tool $Operation --package $script:packet --iis-root $root @modeArgs
 if($LASTEXITCODE -ne 0){throw ('BLOCKED tại '+$Operation+'. Không bỏ guard hoặc tự nhập SQL.')}
 return $lines
}
Assert-NoLink $root;Assert-NoLink $PSScriptRoot
if([IO.Path]::GetFullPath($PSScriptRoot).StartsWith((Join-Path $root 'web')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Đặt gói sửa ngoài webroot.'}
$deliveryPath=Join-Path $PSScriptRoot 'delivery.json'
if((Hash $deliveryPath) -cne '__DELIVERY_SHA__'){throw 'Hash gói sửa không đúng.'}
$delivery=Get-Content -LiteralPath $deliveryPath -Raw -Encoding UTF8|ConvertFrom-Json
foreach($item in $delivery.files){
 if($item.path -match '(^|/)\.\.?(/|$)|\\|:' -or $item.path -notmatch '^(bin|runtime|package)/'){throw 'Đường dẫn delivery không hợp lệ.'}
 $p=Join-Path $PSScriptRoot $item.path;Assert-NoLink $p
 if((Hash $p) -cne $item.sha256){throw ('Hash binary/metadata không khớp: '+$item.path)}
}
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
$tool=Join-Path $PSScriptRoot 'bin/UatDataHandoff.dll';$packet=Join-Path $PSScriptRoot 'package'
$manifestPath=Join-Path $root 'manifest.json';$privatePath=Join-Path $root 'private/uat.json'
Assert-NoLink $manifestPath;Assert-NoLink $privatePath
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
$configHash=Hash $privatePath
if($manifest.privateMode -cne 'Portable' -or $manifest.privateConfiguration.path -cne 'private/uat.json' -or -not $manifest.privateConfiguration.confidential -or $manifest.privateConfiguration.sha256 -cne $configHash){throw 'Cấu hình private/binding không khớp; không sửa cấu hình.'}
$payload=@($delivery.updates)
foreach($item in $payload){
 if($item.path -notlike 'runtime/*'){throw 'Payload không hợp lệ.'}
 [void](Runtime-Path $item.path.Substring(8))
 if(-not @($delivery.files|Where-Object {$_.path -ceq $item.source -and $_.sha256 -ceq $item.sha256}).Count){throw 'Nguồn binary không nằm trong delivery đã kiểm tra.'}
}
$oldDotnet=$env:DOTNET_ENVIRONMENT;$oldAspnet=$env:ASPNETCORE_ENVIRONMENT
$offline=Join-Path $root 'web/app_offline.htm';$ownsOffline=$false
try {
 $env:DOTNET_ENVIRONMENT='Staging';$env:ASPNETCORE_ENVIRONMENT='Staging'
 # Owner policy: ACL composition is not a deployment/media prerequisite.
 # Real file access, webroot/origin, hashes, native scanner and receipts remain guards.
 $identity=((Invoke-MediaTool 'media-tool-identity') -join "`n")|ConvertFrom-Json
 $webLibrary=@($payload|Where-Object {$_.path -ceq 'runtime/web/Infrastructure.dll'})
 if($webLibrary.Count -ne 1 -or $identity.configResolver -cne 'PORTABLE_PRIVATE_STORAGE_V1' -or $identity.toolSha256 -cne (Hash $tool) -or $identity.infrastructureSha256 -cne $webLibrary[0].sha256){throw 'Web và công cụ không dùng cùng thư viện đã kiểm tra.'}
 $preflight=((Invoke-MediaTool 'media-storage-preflight') -join "`n")|ConvertFrom-Json
 if($preflight.status -cne 'PRIVATE_STORAGE_PREFLIGHT_PASS_NOT_SCANNED' -or $preflight.mediaRoot -ine (Join-Path $root 'private/media')){throw 'Storage/scanner chưa sẵn sàng; chưa cập nhật website.'}
 $alreadyUpdated=$true
 foreach($item in $payload){$target=Runtime-Path $item.path.Substring(8);if(-not(Test-Path -LiteralPath $target) -or (Hash $target) -cne $item.sha256){$alreadyUpdated=$false;break}}
 if(-not $alreadyUpdated){
 # A running service/CLI must not be replaced underneath its process.
 foreach($item in $payload|Where-Object {$_.path -notlike 'runtime/web/*'}){
  $target=Runtime-Path $item.path.Substring(8)
  if(Test-Path -LiteralPath $target){try{$h=[IO.File]::Open($target,'Open','ReadWrite','None');$h.Dispose()}catch{throw 'Worker/CLI đang giữ binary. CNTT dừng đúng Worker của gói này trước khi cập nhật; công cụ không tự bật Mail/Worker.'}}
 }
 if(Test-Path -LiteralPath $offline){throw 'Website đang có app_offline.htm; không ghi đè phiên bảo trì khác.'}
 $id=[Guid]::NewGuid().ToString('N');$rollback=Join-Path $root ('runtime-rollback/'+$id)
 Assert-NoLink $rollback
 [void][IO.Directory]::CreateDirectory($rollback)
 [IO.File]::Copy($manifestPath,(Join-Path $rollback 'manifest.json'),$false)
 $installed=@();$manifestChanged=$false;$safeToOpen=$false
 foreach($item in $payload){
  $relative=$item.path.Substring(8);$target=Runtime-Path $relative
  if(Test-Path -LiteralPath $target){$old=Join-Path $rollback $relative;[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($old));[IO.File]::Copy($target,$old,$false)}
 }
 $offlineText='<!doctype html><meta charset="utf-8"><p>Website đang cập nhật thư viện lưu trữ. Vui lòng thử lại sau ít phút.</p>'
 $bytes=[Text.Encoding]::UTF8.GetBytes($offlineText);$stream=[IO.File]::Open($offline,'CreateNew','Write','None');try{$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()};$ownsOffline=$true
 try {
  $deadline=[DateTime]::UtcNow.AddSeconds(45)
  do {
   $unlocked=$true
   foreach($item in $payload|Where-Object {$_.path -like 'runtime/web/*'}){ $target=Runtime-Path $item.path.Substring(8);if(Test-Path -LiteralPath $target){try{$h=[IO.File]::Open($target,'Open','ReadWrite','None');$h.Dispose()}catch{$unlocked=$false;break}} }
   if(-not $unlocked){Start-Sleep -Milliseconds 500}
  }while(-not $unlocked -and [DateTime]::UtcNow -lt $deadline)
  if(-not $unlocked){throw 'IIS chưa nhả binary sau app_offline; chưa thay file.'}
  foreach($item in $payload){
   $relative=$item.path.Substring(8);$target=Runtime-Path $relative
   [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target));$installed+=$relative
   Write-BinaryBytes (Join-Path $PSScriptRoot $item.source) $target
   if((Hash $target) -cne $item.sha256){throw 'Đối soát binary sau copy thất bại.'}
  }
  if((Hash $privatePath) -cne $configHash){throw 'Cấu hình private thay đổi trong lúc cập nhật.'}
  $paths=@($payload|ForEach-Object {$_.path.Substring(8)})
  $records=@($manifest.files|Where-Object {$_.path -notin $paths})
  foreach($item in $payload){$records+=@{path=$item.path.Substring(8);bytes=(Get-Item -LiteralPath (Join-Path $PSScriptRoot $item.source)).Length;sha256=$item.sha256}}
  $manifest.files=$records
  $manifest|Add-Member -NotePropertyName portableStorageUpdate -NotePropertyValue @{id=$id;utc=[DateTimeOffset]::UtcNow.ToString('O');infrastructureSha256=$identity.infrastructureSha256;privateConfigUnchanged=$true} -Force
  $temporary=Join-Path $root ('manifest-'+$id+'.tmp');[IO.File]::WriteAllText($temporary,($manifest|ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false));[IO.File]::Replace($temporary,$manifestPath,(Join-Path $rollback 'manifest-before-replace.json'));$manifestChanged=$true
  $safeToOpen=$true
 }catch {
  foreach($relative in $installed){$target=Runtime-Path $relative;$previous=Join-Path $rollback $relative;if(Test-Path -LiteralPath $previous){Write-BinaryBytes $previous $target}elseif(Test-Path -LiteralPath $target){[IO.File]::Delete($target)}}
  if($manifestChanged){[IO.File]::Copy((Join-Path $rollback 'manifest.json'),$manifestPath,$true)}
  $safeToOpen=$true
  throw
 }finally {
  if($safeToOpen -and $ownsOffline -and (Test-Path -LiteralPath $offline) -and [IO.File]::ReadAllText($offline) -ceq $offlineText){[IO.File]::Delete($offline);$ownsOffline=$false}
 }
 Write-Output ('BINARY UPDATED; giữ nguyên private/config/media; bản binary cũ: '+$rollback)
 }else{Write-Output 'Binary đã đúng hash; không cập nhật/lưu bản binary cũ lần nữa.'}
 # Integrity receipt records NotScanned; publication requires a separate scoped
 # administrator exception during import. No SQL or public upload policy changes.
 Invoke-MediaTool 'verify-copied-media'|Write-Output
 Write-Output 'Đã kiểm toàn vẹn, KHÔNG QUÉT ANTIVIRUS. Gửi KET-QUA-MEDIA-*.json và SHA256 cho Developer; chỉ nhập/phát hành sau xác nhận ngoại lệ quản trị đúng receipt. Chưa ghi SQL.'
}finally{
 $env:DOTNET_ENVIRONMENT=$oldDotnet;$env:ASPNETCORE_ENVIRONMENT=$oldAspnet
 if($ownsOffline){Write-Warning 'Còn file bảo trì của lượt này; cần đối soát binary trước khi mở website.'}
}
