[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$PlanFile,
 [ValidateSet('Plan','Backup')][string]$Mode='Plan',
 [System.Management.Automation.PSCredential]$DbaCredential,
 [switch]$TemporaryCertificateTrust
)
# Manual DBA operation ONLY. Default Plan never opens SQL. This tool cannot
# run migrations, create roles/import data or restore a database.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function NoReparse([string]$Path){
 for($p=[IO.Path]::GetFullPath($Path);$p;$p=[IO.Path]::GetDirectoryName($p)){
  if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse input/output refused'}
 }
}
NoReparse $PlanFile
$plan=Get-Content -LiteralPath $PlanFile -Raw -Encoding UTF8|ConvertFrom-Json
$pin='664de13e864e69a989e56d17e2a927b7477ca5c038030dbe19bfc62451a6e1d7'
if($plan.manifestSha256 -cne $pin -or $plan.target -cne 'WIN-S1IBJB84OOO/website_tin_tuc' -or $plan.kind -cne 'LandPortal.Sql207.OneBackup.v1'){throw 'Wrong backup-plan scope'}
if((Hash $PSCommandPath) -cne $plan.operatorSha256){throw 'Backup operator differs from reviewed plan'}
$packet=[IO.Path]::GetFullPath([string]$plan.packetDirectory);NoReparse $packet
if((Hash (Join-Path $packet 'manifest.json')) -cne $pin){throw 'Packet manifest changed'}
$manifest=Get-Content -LiteralPath (Join-Path $packet 'manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($manifest.kind -cne 'LandPortal.Sql207.ResumePrefix.v1' -or $manifest.baseline.tables -ne 31 -or $manifest.baseline.migrations -ne 2){throw 'Wrong prefix manifest'}
foreach($f in $manifest.files){
 if($f.path -cnotin @('schema.sqlcmd','verify-prefix.sql','recover-207.ps1','START.md')){throw 'Unexpected packet file'}
 $path=Join-Path $packet $f.path;NoReparse $path
 if((Hash $path) -cne $f.sha256){throw 'Packet file hash changed'}
}
if(@($manifest.files).Count -ne 4){throw 'Packet incomplete'}
NoReparse $plan.inspectReport
if((Hash $plan.inspectReport) -cne $plan.inspectReportSha256){throw 'Prior Inspect receipt changed'}
$inspection=@(Get-Content -LiteralPath $plan.inspectReport -Encoding UTF8|ForEach-Object {$_|ConvertFrom-Json})
if($inspection.Count -ne 2 -or $inspection[0].manifestSha256 -cne $pin -or $inspection[0].target -cne $plan.target -or $inspection[0].mode -cne 'Inspect' -or $inspection[1].stage -cne 'READ_ONLY_STATE_PREFLIGHT_PASS' -or $inspection[1].state -cne 'EXACT_COMMITTED_MIGRATION2_PREFIX' -or $inspection[1].ddlExecuted){throw 'Prior Inspect did not prove exact prefix'}
if($plan.jobId -cnotmatch '^[a-f0-9]{32}$' -or $plan.backupTag -cne ('LP207-'+$plan.jobId+'-'+$pin)){throw 'Invalid fixed backup identity'}
$allowed='C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\website_tin_tuc_prefix2_20261029_'+$plan.jobId+'.bak'
if($plan.backupFile -cne $allowed){throw 'Backup path is not this fixed new SQL-server backup file'}
if($Mode -eq 'Plan'){
 Write-Output ('PLAN PASS; packet unchanged; one fixed backup target='+$plan.backupFile+'; existing matching file will only be verified/reused. No SQL connection.')
 return
}
if(-not $DbaCredential){$DbaCredential=Get-Credential -Message 'SQL DBA for exact website_tin_tuc backup; never enter a runtime application account'}
if(-not $DbaCredential -or $DbaCredential.UserName -ieq 'website_tin_tuc_app'){throw 'DBA credential required, not application login'}
$receipt=[IO.Path]::GetFullPath([string]$plan.receiptFile);NoReparse $receipt
if(-not(Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($receipt)) -PathType Container)){throw 'Private receipt directory missing'}
$security=New-Object Security.AccessControl.FileSecurity;$security.SetAccessRuleProtection($true,$false)
foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'))){$security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','Allow')))}
if(Test-Path -LiteralPath $receipt){
 foreach($line in [IO.File]::ReadAllLines($receipt)){
  $entry=$line|ConvertFrom-Json
  if($entry.jobId -cne $plan.jobId -or $entry.manifestSha256 -cne $pin -or $entry.target -cne $plan.target -or $entry.backupFile -cne $plan.backupFile){throw 'Existing backup receipt scope mismatch'}
 }
 Set-Acl -LiteralPath $receipt -AclObject $security
 $stream=[IO.File]::Open($receipt,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::Read)
}else{$stream=[IO.File]::Create($receipt,4096,[IO.FileOptions]::None,$security)}
$writer=New-Object IO.StreamWriter($stream,([Text.UTF8Encoding]::new($false)));$writer.AutoFlush=$true
function Audit([hashtable]$row){
 $row.utc=[DateTimeOffset]::UtcNow.ToString('O');$row.jobId=$plan.jobId;$row.target=$plan.target;$row.manifestSha256=$pin;$row.backupFile=$plan.backupFile
 $writer.WriteLine(($row|ConvertTo-Json -Depth 8 -Compress))
}
$builder=New-Object Data.SqlClient.SqlConnectionStringBuilder
$builder.DataSource='45.120.229.43,1433';$builder.InitialCatalog='website_tin_tuc';$builder.IntegratedSecurity=$false
$builder.Encrypt=$true;$builder.TrustServerCertificate=[bool]$TemporaryCertificateTrust;$builder.Pooling=$false;$builder.ConnectTimeout=15
$secret=$DbaCredential.Password.Copy();$secret.MakeReadOnly()
$credential=New-Object Data.SqlClient.SqlCredential($DbaCredential.UserName,$secret)
$connection=New-Object Data.SqlClient.SqlConnection($builder.ConnectionString,$credential)
$lease=$false;$stage='CONNECT';$madeBackup=$false;$header=$null
function Command([string]$sql){$c=$connection.CreateCommand();$c.CommandText=$sql;$c.CommandTimeout=120;return $c}
function Execute([string]$sql){$c=Command $sql;try{[void]$c.ExecuteNonQuery()}finally{$c.Dispose()}}
function Assert-Prefix {
 Execute 'IF @@TRANCOUNT<>0 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),''DATABASE'',''CONTROL''),0)<>1 THROW 51000,''Fresh DBA connection with target CONTROL required.'',1;'
 Execute ([IO.File]::ReadAllText((Join-Path $packet 'verify-prefix.sql')))
}
try{
 Audit @{stage='STARTED';priorInspectSha256=$plan.inspectReportSha256;beforeTables=31;beforeMigrations=2}
 $connection.Open();$stage='PREFIX'
 # Exact packet guard checks physical identity, visibility, schema/history,
 # rights, no other user transaction, zero business rows. Never grant rights.
 Assert-Prefix
 Execute "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'LandPortal.Sql207.PrefixRecovery',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; IF @r<0 THROW 51000,'Recovery/backup already active.',1;"
 $lease=$true;Assert-Prefix
 $stage='FILE_STATUS';$c=Command 'SELECT file_exists,file_is_a_directory,parent_directory_exists FROM sys.dm_os_file_exists(@file);'
 [void]$c.Parameters.Add('@file',[Data.SqlDbType]::NVarChar,4000);$c.Parameters['@file'].Value=$plan.backupFile
 try{$reader=$c.ExecuteReader();try{if(-not $reader.Read()){throw 'File status absent'};$exists=[bool]$reader['file_exists'];if([bool]$reader['file_is_a_directory'] -or -not [bool]$reader['parent_directory_exists']){throw 'Backup parent unavailable or path is a directory'}}finally{$reader.Dispose()}}finally{$c.Dispose()}
 if(-not $exists){
  $stage='BACKUP';Audit @{stage='BACKUP_START_RECONCILE_IF_INTERRUPTED';backupTag=$plan.backupTag}
  $c=Command 'BACKUP DATABASE [website_tin_tuc] TO DISK=@file WITH COPY_ONLY,CHECKSUM,NOINIT,NOFORMAT,NOSKIP,NAME=@tag,MEDIANAME=@tag;';$c.CommandTimeout=600
  [void]$c.Parameters.Add('@file',[Data.SqlDbType]::NVarChar,4000);$c.Parameters['@file'].Value=$plan.backupFile
  [void]$c.Parameters.Add('@tag',[Data.SqlDbType]::NVarChar,128);$c.Parameters['@tag'].Value=$plan.backupTag
  try{[void]$c.ExecuteNonQuery();$madeBackup=$true}finally{$c.Dispose()}
 }else{Audit @{stage='EXISTING_FILE_VERIFY_ONLY';backupCreated=$false}}
 $stage='HEADER';$c=Command 'RESTORE HEADERONLY FROM DISK=@file;'
 [void]$c.Parameters.Add('@file',[Data.SqlDbType]::NVarChar,4000);$c.Parameters['@file'].Value=$plan.backupFile
 try{$reader=$c.ExecuteReader();try{
  if(-not $reader.Read()){throw 'Backup header absent'}
  if([string]$reader['BackupName'] -cne $plan.backupTag -or [string]$reader['ServerName'] -cne 'WIN-S1IBJB84OOO' -or [string]$reader['DatabaseName'] -cne 'website_tin_tuc' -or [int]$reader['BackupType'] -ne 1 -or -not [bool]$reader['IsCopyOnly'] -or -not [bool]$reader['HasBackupChecksums'] -or [bool]$reader['IsDamaged']){throw 'Existing backup is not this exact complete tagged COPY_ONLY/checksummed target set; do not overwrite/retry'}
  $header=@{backupSetGuid=[string]$reader['BackupSetGUID'];backupSize=[long]$reader['BackupSize'];compressedBackupSize=[long]$reader['CompressedBackupSize'];finish=[string]$reader['BackupFinishDate'];checkpointLsn=[string]$reader['CheckpointLSN'];backupTag=$plan.backupTag}
  if($reader.Read()){throw 'Multiple appended backup sets refused'}
 }finally{$reader.Dispose()}}finally{$c.Dispose()}
 Assert-Prefix
 # Release cooperative lease before reusing the unchanged recovery operator's
 # dedicated connection and the SAME lease. It rechecks prefix/authority twice.
 Execute "EXEC sys.sp_releaseapplock @Resource=N'LandPortal.Sql207.PrefixRecovery',@LockOwner='Session';";$lease=$false
 $connection.Close();$stage='VERIFY_WITH_UNCHANGED_PACKET'
 $verify=Join-Path ([IO.Path]::GetDirectoryName($receipt)) ('backup-verify-'+[guid]::NewGuid().ToString('N')+'.jsonl')
 & (Join-Path $packet 'recover-207.ps1') -PacketDirectory $packet -Mode Inspect -ManifestSha256 $pin -BackupFile $plan.backupFile -ReportFile $verify -DbaCredential $DbaCredential -TemporaryCertificateTrust:$TemporaryCertificateTrust
 $records=@(Get-Content -LiteralPath $verify -Encoding UTF8|ForEach-Object {$_|ConvertFrom-Json})
 $backupPass=@($records|Where-Object {$_.stage -eq 'BACKUP_READONLY_VERIFY_PASS'})
 $last=$records[-1]
 if($records[0].manifestSha256 -cne $pin -or $records[0].target -cne $plan.target -or $backupPass.Count -ne 1 -or $backupPass[0].backup.path -cne $plan.backupFile -or $backupPass[0].backup.backupSetGuid -cne $header.backupSetGuid -or $last.stage -cne 'READ_ONLY_STATE_PREFLIGHT_PASS' -or -not $last.backupVerified -or $last.ddlExecuted){throw 'Independent backup/prefix verification receipt mismatch'}
 Audit @{stage='BACKUP_VERIFIED';backupCreated=$madeBackup;backupReused=(-not $madeBackup);header=$header;tables=31;migrations=@('20261012055549_InitialPortal','20261012083717_CmsRevisionWorkflow');checksumVerify='PASS';headerVerify='PASS';verificationReceipt=$verify;verificationReceiptSha256=(Hash $verify);restoreTested=$false;ddlExecuted=$false}
 Write-Output ('BACKUP_VERIFIED; receipt='+$receipt+'; backup='+$plan.backupFile+'; newBackup='+$madeBackup+'. No schema/role/import executed; restore NOT RUN.')
}catch{
 $n=$null;$ex=$_.Exception
 while($ex){if($ex -is [Data.SqlClient.SqlException]){$n=$ex.Number;break};$ex=$ex.InnerException}
 Audit @{stage='STOP_RECONCILE_REQUIRED';failedStage=$stage;sqlNumber=$n;backupCreated=$madeBackup;backupOutcomeUncertain=($stage -eq 'BACKUP');autoRetry=$false;ddlExecuted=$false}
 throw ('Backup stopped at '+$stage+'; SQL number='+$n+'. Details omitted; read private receipt. No automatic retry, overwrite, migration or restore.')
}finally{
 if($lease -and $connection.State -eq [Data.ConnectionState]::Open){try{Execute "EXEC sys.sp_releaseapplock @Resource=N'LandPortal.Sql207.PrefixRecovery',@LockOwner='Session';"}catch{}}
 $connection.Dispose();$secret.Dispose();$writer.Dispose()
}
