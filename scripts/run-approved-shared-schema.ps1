[CmdletBinding()]
param(
 [switch]$ExecuteApprovedUpgrade,
 [ValidateSet('Both','Fr06')][string]$StartAt='Both',
 [string]$ReportDirectory,
 [System.Management.Automation.PSCredential]$DbaCredential
)
# Exact owner-approved 02-Nov-2026 scope. Plan is offline by default.
# No credential persistence, retries, database restore, role creation or import.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$backupOperator=Join-Path $PSScriptRoot 'backup-shared-sql-207.ps1'
$stages=@(
 @{name='prefix2';packet='shared-sql-207-resume';sha='664de13e864e69a989e56d17e2a927b7477ca5c038030dbe19bfc62451a6e1d7';kind='LandPortal.Sql207.OneBackup.v1';tag='LP207-';file='website_tin_tuc_prefix2_20261029_';tables=70;migrations=16},
 @{name='fr06';packet='fr06-closure-final-20261102';sha='13594b2be96f46b7bd517b5160572800ab63ee4d93ce26ce64b4edf7df8d9a8e';kind='LandPortal.Fr06.OneBackup.v1';tag='LPFR06-';file='website_tin_tuc_prefix16_fr06_';tables=70;migrations=17}
)
if($StartAt -eq 'Fr06'){$stages=@($stages[1])}
foreach($stage in $stages){
 $packet=Join-Path $root ('.local/publish/'+$stage.packet)
 & (Join-Path $packet 'recover-207.ps1') -PacketDirectory $packet -Mode Plan -ManifestSha256 $stage.sha
}
if(-not $ExecuteApprovedUpgrade){Write-Output 'OFFLINE_PLAN_PASS; no connection, backup or DDL. Execution requires a dedicated DBA credential and exact-prefix checks.';return}
if(-not $ReportDirectory){throw 'A new private report directory is required'}
$reports=[IO.Path]::GetFullPath($ReportDirectory).TrimEnd('\')
$privateRoot=[IO.Path]::GetFullPath((Join-Path $env:USERPROFILE 'LandPortal-Private')).TrimEnd('\')
if(-not $reports.StartsWith($privateRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $reports)){throw 'Use a new directory beneath current-user LandPortal-Private'}
for($p=$reports;$p;$p=[IO.Path]::GetDirectoryName($p)){
 if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse report path refused'}
}
$acl=New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false)
foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'))){
 $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))
}
# ACL is applied at creation, before any receipt is written.
[void][IO.Directory]::CreateDirectory($reports,$acl)
$status=Join-Path $reports 'operator-status.json'
$currentStage='CREDENTIAL_PROMPT'
function Status([string]$State){
 [ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('O');state=$State;stage=$currentStage;target='WIN-S1IBJB84OOO/website_tin_tuc';autoRetry=$false;credentialPersisted=$false}|
 ConvertTo-Json|Set-Content -LiteralPath $status -Encoding UTF8
}
try{
 Status 'WAITING_FOR_DBA'
 if(-not $DbaCredential){$DbaCredential=Get-Credential -Message 'Approved website_tin_tuc schema upgrade only. Enter authorized DBA; not website_tin_tuc_app. Password is kept in memory only.'}
 if(-not $DbaCredential -or $DbaCredential.UserName -ieq 'website_tin_tuc_app'){throw 'Dedicated DBA credential required'}
 foreach($stage in $stages){
  $packet=Join-Path $root ('.local/publish/'+$stage.packet)
  $runner=Join-Path $packet 'recover-207.ps1'
  $inspect=Join-Path $reports ($stage.name+'-inspect.jsonl')
  $currentStage=$stage.name+'.inspect';Status 'RUNNING'
  & $runner -PacketDirectory $packet -Mode Inspect -ManifestSha256 $stage.sha -TemporaryCertificateTrust -DbaCredential $DbaCredential -ReportFile $inspect
  $job=[guid]::NewGuid().ToString('N')
  $backup='C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\'+$stage.file+$job+'.bak'
  $backupReceipt=Join-Path $reports ($stage.name+'-backup.jsonl')
  $planFile=Join-Path $reports ($stage.name+'-backup-plan.json')
  [ordered]@{kind=$stage.kind;target='WIN-S1IBJB84OOO/website_tin_tuc';manifestSha256=$stage.sha;
   packetDirectory=$packet;operatorSha256=(Get-FileHash $backupOperator -Algorithm SHA256).Hash.ToLowerInvariant();
   inspectReport=$inspect;inspectReportSha256=(Get-FileHash $inspect -Algorithm SHA256).Hash.ToLowerInvariant();
   jobId=$job;backupTag=$stage.tag+$job+'-'+$stage.sha;backupFile=$backup;receiptFile=$backupReceipt}|
   ConvertTo-Json|Set-Content -LiteralPath $planFile -Encoding UTF8
  $currentStage=$stage.name+'.backup';Status 'RUNNING'
  & $backupOperator -PlanFile $planFile -Mode Backup -DbaCredential $DbaCredential -TemporaryCertificateTrust
  $backupRecords=@(Get-Content -LiteralPath $backupReceipt -Encoding UTF8|ForEach-Object {$_|ConvertFrom-Json})
  if($backupRecords[-1].stage -cne 'BACKUP_VERIFIED'){throw 'Backup did not finish verification'}
  $apply=Join-Path $reports ($stage.name+'-apply.jsonl')
  $currentStage=$stage.name+'.apply';Status 'RUNNING'
  $applyArgs=@{PacketDirectory=$packet;Mode='Apply';ManifestSha256=$stage.sha;ConfirmTarget='WIN-S1IBJB84OOO/website_tin_tuc';
   BackupFile=$backup;ReportFile=$apply;DbaCredential=$DbaCredential;TemporaryCertificateTrust=$true}
  if($stage.name -eq 'fr06'){$applyArgs.PrefixInspectionReport=$inspect}
  & $runner @applyArgs
  $result=@(Get-Content -LiteralPath $apply -Encoding UTF8|ForEach-Object {$_|ConvertFrom-Json})[-1]
  if($result.stage -cne 'SCHEMA_RECOVERY_VERIFIED' -or $result.tables -ne $stage.tables -or $result.migrations -ne $stage.migrations -or -not $result.applicationRightsPreserved -or -not $result.prefixTablesPreserved){throw 'Apply readback mismatch; stop and reconcile'}
 }
 $currentStage='COMPLETE';Status 'SCHEMA_70_TABLES_17_MIGRATIONS_VERIFIED'
 Write-Output ('SCHEMA VERIFIED; private receipts='+$reports+'. No runtime role, import, LAN or production deployment.')
}catch{
 Status 'BLOCKED_STOP_RECONCILE_NO_AUTORETRY'
 # Child recovery/backup receipts contain their own sanitized diagnostics.
 # Do not emit arbitrary exception text, credential objects or SQL parameters.
 Write-Output ('BLOCKED at '+$currentStage+'; inspect private receipts at '+$reports+'. No automatic retry.')
 exit 1
}finally{
 $DbaCredential=$null
}
