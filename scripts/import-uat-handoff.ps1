[CmdletBinding()]
param(
 [ValidateSet('Inspect','Plan','Backup','Import','Verify','Reconcile')][string]$Mode='Inspect',
 [string]$IisPublishRoot='D:\WWW_GDLA\publish\TTDL-IIS',
 [string]$OperatorConfig,[string]$Plan,[string]$Approval,[string]$BackupReceipt,
 [switch]$ReviewedCandidates
)
# Runs beside package/, bin/ and ops/. No SQL text or credential is accepted here.
# Backup/import still require the separately allocated operator and signed scope.
$ErrorActionPreference='Stop'
$package=Join-Path $PSScriptRoot 'package'
$tool=Join-Path $PSScriptRoot 'bin/UatDataHandoff.dll'
$helper=Join-Path $PSScriptRoot 'ops/uat-data.ps1'
foreach($path in @($package,$tool,$helper)){if(-not(Test-Path -LiteralPath $path)){throw 'Thiếu gói dữ liệu/công cụ. Copy nguyên thư mục bàn giao ngoài webroot.'}}
$base=@{Package=$package;ToolPath=$tool;ContentOnly=$true;ReviewedCandidates=[bool]$ReviewedCandidates}
if($Mode -eq 'Inspect'){
 & $helper @base -Mode inspect
 & $helper @base -Mode offline-plan
 return
}
$configFile=Join-Path ([IO.Path]::GetFullPath($IisPublishRoot)) 'private/uat.json'
if(-not(Test-Path -LiteralPath $configFile)){throw 'Không thấy gói IIS đã cấu hình. Kiểm tra IisPublishRoot; không tạo JSON hoặc secret giả.'}
$c=Get-Content -LiteralPath $configFile -Raw -Encoding UTF8|ConvertFrom-Json
$dataHomeRoot=Join-Path ([IO.Path]::GetFullPath($IisPublishRoot)) 'private'
if($c.PSObject.Properties.Name -contains 'PortableRuntime' -and $c.PortableRuntime.DataHome){$dataHomeRoot=[IO.Path]::GetFullPath([string]$c.PortableRuntime.DataHome)}
if($OperatorConfig){
 if(-not(Test-Path -LiteralPath $OperatorConfig)){throw 'Không thấy cấu hình operator được cấp; không dùng sa trong cấu hình Web.'}
 $base.DataHome=$dataHomeRoot;$base.Config=[IO.Path]::GetFullPath($OperatorConfig)
}else{$base.IisPublishRoot=[IO.Path]::GetFullPath($IisPublishRoot)}
if($Mode -in @('Backup','Import') -and -not $OperatorConfig){throw 'BLOCKED: cần cấu hình operator riêng được cấp quyền backup/import. Không thay login hoặc role của Web.'}
switch($Mode){
 'Plan'{
  $selection=if($ReviewedCandidates){'reviewed285'}else{'ready33'}
  $output=Join-Path $dataHomeRoot ('.local/data-plans/'+$selection+'-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
  & $helper @base -Mode plan -Output $output
  Write-Output ('PLAN: '+(Join-Path $output 'plan.json')+'; approval-UNSIGNED.ps1 chưa phải phê duyệt. Không ký bằng certificate Dev.')
 }
 'Backup'{
  if($BackupReceipt){& $helper @base -Mode verify-backup -BackupReceipt $BackupReceipt}
  else{& $helper @base -Mode backup}
 }
 'Import'{
  if(-not $Plan -or -not $Approval -or -not $BackupReceipt){throw 'BLOCKED: cần plan thực tế, approval UAT đã ký đúng scope và receipt backup hợp lệ. Chưa ghi dữ liệu.'}
  foreach($path in @($Plan,$Approval,$BackupReceipt)){if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw 'Thiếu file plan/approval/backup receipt; chưa import.'}}
  $manifestSha=(Get-FileHash -LiteralPath (Join-Path $package 'manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
  $planSha=(Get-FileHash -LiteralPath $Plan -Algorithm SHA256).Hash.ToLowerInvariant()
  & $helper @base -Mode import -ManifestSha $manifestSha -PlanSha $planSha -ConfirmTarget website_tin_tuc -Approval $Approval -BackupReceipt $BackupReceipt
  & $helper @base -Mode verify
  & $helper @base -Mode reconcile
 }
 'Verify'{& $helper @base -Mode verify}
 'Reconcile'{& $helper @base -Mode reconcile}
}
