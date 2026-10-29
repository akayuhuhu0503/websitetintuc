[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$PacketDirectory,
 [ValidateSet('Plan','Inspect','Apply')][string]$Mode='Plan',
 [string]$ManifestSha256,[string]$ConfirmTarget,[string]$ReportFile,
 [System.Management.Automation.PSCredential]$DbaCredential,
 [switch]$TemporaryCertificateTrust
)
# Manual operator tool. Default Plan never opens SQL. Inspect never executes DDL.
# Apply is only for an explicitly approved exact manifest and bootstrap state.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
function Assert-NoReparse([string]$Path){
 for($node=[IO.Path]::GetFullPath($Path);$node;$node=[IO.Path]::GetDirectoryName($node)){
  if((Test-Path -LiteralPath $node) -and ((Get-Item -LiteralPath $node).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse input/output refused'}
 }
}
function File-Bytes([string]$Path){
 Assert-NoReparse $Path
 $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 try{$memory=New-Object IO.MemoryStream;$stream.CopyTo($memory);$data=$memory.ToArray();$memory.Dispose();return ,$data}finally{$stream.Dispose()}
}
function Byte-Sha([byte[]]$Bytes){$h=[Security.Cryptography.SHA256]::Create();try{return ([BitConverter]::ToString($h.ComputeHash($Bytes))).Replace('-','').ToLowerInvariant()}finally{$h.Dispose()}}
function Safe-SqlMessage([int]$Number,[string]$Message,[string]$QueryText){
 # Only narrowly recognized engine diagnostics and exact static THROW messages
 # may reach a receipt. Never log SQL text, bound parameters or arbitrary values.
 if($Number -eq 451 -and $Message -match '(?i)^\s*Cannot resolve collation conflict for column (\d+) in SELECT statement\.?\s*$'){
  return ('Cannot resolve collation conflict for column '+$matches[1]+' in SELECT statement.')
 }
 if($Number -in @(451,468) -and $Message -match '(?i)collation conflict'){
  $column=[regex]::Match($Message,'(?i)column\s+(\d+)')
  $suffix=if($column.Success){'; output column='+$column.Groups[1].Value}else{''}
  return ('SQL collation conflict'+$suffix+'. Other error text omitted.')
 }
 if($Number -eq 51000){
  foreach($match in [regex]::Matches($QueryText,"(?i)THROW\s+51000\s*,\s*'((?:[^']|'')*)'")){
   $allowed=$match.Groups[1].Value.Replace("''", "'")
   if($Message.Trim() -ceq $allowed){return $allowed}
  }
 }
 return ('SQL error '+$Number+'; detailed text omitted by secret-safe diagnostic policy.')
}
function Text-Batches([string]$Text){
 $batches=New-Object 'System.Collections.Generic.List[string]';$batch=''
 foreach($line in ($Text -split '\r?\n')){
  if($line -match '^\s*:'){if($line.Trim() -ine ':ON ERROR EXIT'){throw 'Unreviewed SQLCMD directive refused'};continue}
  if($line -match '^\s*GO\s*$'){if($batch.Trim()){$batches.Add($batch)};$batch=''}
  elseif($line -match '^\s*GO\s+\d+') {throw 'Repeated GO batch refused'}
  else{$batch+=$line+"`n"}
 }
 if($batch.Trim()){$batches.Add($batch)}
 return $batches.ToArray()
}
$packet=[IO.Path]::GetFullPath($PacketDirectory).TrimEnd('\');Assert-NoReparse $packet
$manifestBytes=File-Bytes (Join-Path $packet 'manifest.json');$actualSha=Byte-Sha $manifestBytes
if($ManifestSha256 -and $ManifestSha256 -cne $actualSha){throw 'Manifest SHA mismatch; no SQL connection attempted'}
if($Mode -ne 'Plan' -and $ManifestSha256 -notmatch '^[a-f0-9]{64}$'){throw 'Exact reviewed manifest SHA required before any SQL connection'}
$manifest=[Text.Encoding]::UTF8.GetString($manifestBytes)|ConvertFrom-Json
$backup='C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\website_tin_tuc_pre_schema_20261028_074711_584F7E5FF87E4AF79DFCD8D70065B8B9.bak'
if($manifest.kind -cne 'LandPortal.Sql1934.BootstrapOnly.v1' -or $manifest.server -cne 'WIN-S1IBJB84OOO' -or $manifest.database -cne 'website_tin_tuc' -or $manifest.endpoint -cne '45.120.229.43,1433' -or $manifest.backupPath -cne $backup){throw 'Manifest scope is not this exact reviewed target/bootstrap backup'}
$content=@{}
foreach($entry in $manifest.files){
 if($entry.path -notin @('schema.sqlcmd','verify-bootstrap.sql','recover-bootstrap.ps1','START.md') -or $content.ContainsKey([string]$entry.path)){throw 'Unexpected/duplicate packet path refused'}
 $bytes=File-Bytes (Join-Path $packet $entry.path)
 if((Byte-Sha $bytes) -cne $entry.sha256){throw ('Packet SHA mismatch: '+$entry.path)}
 $content[[string]$entry.path]=[Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF)
}
foreach($required in @('schema.sqlcmd','verify-bootstrap.sql','recover-bootstrap.ps1','START.md')){if(-not $content.ContainsKey($required)){throw 'Incomplete packet'}}
$self=Byte-Sha (File-Bytes $PSCommandPath)
if($self -cne (@($manifest.files|Where-Object {$_.path -ceq 'recover-bootstrap.ps1'})[0].sha256)){throw 'Executing helper differs from the reviewed packet'}
$schemaBatches=@(Text-Batches $content['schema.sqlcmd']);$guardBatches=@(Text-Batches $content['verify-bootstrap.sql'])
if($Mode -eq 'Plan'){
 Write-Output ('PLAN PASS; manifest='+$actualSha+'; exact files verified; preserve history/PK, reuse verified backup; no SQL connection or execution.')
 return
}
if($Mode -eq 'Apply' -and $ConfirmTarget -cne 'WIN-S1IBJB84OOO/website_tin_tuc'){throw 'Explicit exact target confirmation required for Apply'}
if(-not $ReportFile){throw 'New private JSONL report path required'}
$report=[IO.Path]::GetFullPath($ReportFile);Assert-NoReparse $report
if($report.StartsWith($packet+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $report)){throw 'Receipt must be new and outside the immutable packet'}
if(-not(Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($report)) -PathType Container)){throw 'Create the private receipt parent directory first'}
if(-not $DbaCredential){$DbaCredential=Get-Credential -Message 'Dedicated SQL DBA account for this exact target; password never goes into source/log'}
if(-not $DbaCredential -or $DbaCredential.UserName -ieq 'website_tin_tuc_app'){throw 'Runtime application login cannot perform recovery'}
$security=New-Object Security.AccessControl.FileSecurity;$security.SetAccessRuleProtection($true,$false)
foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'))){$security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','Allow')))}
$receiptStream=[IO.File]::Create($report,4096,[IO.FileOptions]::None,$security)
$writer=New-Object IO.StreamWriter($receiptStream,([Text.UTF8Encoding]::new($false)));$writer.AutoFlush=$true
function Audit($Record){if(-not $Record.ContainsKey('utc')){$Record.utc=[DateTimeOffset]::UtcNow.ToString('O')};$writer.WriteLine(($Record|ConvertTo-Json -Depth 8 -Compress))}
Audit @{reportSchemaVersion=2;stage='STARTED_RECONCILE_IF_INTERRUPTED';mode=$Mode;manifestSha256=$actualSha;target='WIN-S1IBJB84OOO/website_tin_tuc';operatorLogin=$DbaCredential.UserName;utc=[DateTimeOffset]::UtcNow.ToString('O')}
$builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$builder['Data Source']='45.120.229.43,1433';$builder['Initial Catalog']='website_tin_tuc'
$builder['Integrated Security']=$false;$builder['Encrypt']=$true;$builder['TrustServerCertificate']=[bool]$TemporaryCertificateTrust
$builder['Pooling']=$false;$builder['Connect Timeout']=15
$password=$DbaCredential.Password.Copy();$password.MakeReadOnly()
$credential=New-Object System.Data.SqlClient.SqlCredential($DbaCredential.UserName,$password)
$connection=New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString,$credential)
$script:ActiveSqlQueryId='connection.open';$script:ActiveSqlQuerySha256=$null;$script:ActiveSqlQueryText=''
function Command([string]$Query,[string]$QueryId=''){
 $script:ActiveSqlQuerySha256=Byte-Sha ([Text.Encoding]::UTF8.GetBytes($Query))
 if(-not $QueryId){$QueryId='sql.'+$script:ActiveSqlQuerySha256.Substring(0,16)}
 if($QueryId -notmatch '^[a-z0-9_.-]{1,100}$'){throw 'Invalid internal query identifier'}
 $script:ActiveSqlQueryId=$QueryId;$script:ActiveSqlQueryText=$Query
 $c=$connection.CreateCommand();$c.CommandTimeout=60;$c.CommandText=$Query;return $c
}
function Execute([string]$Query,[string]$QueryId=''){$c=Command $Query $QueryId;try{[void]$c.ExecuteNonQuery()}finally{$c.Dispose()}}
function Rows([string]$Query,[string]$QueryId=''){
 $c=Command $Query $QueryId
 try{$r=$c.ExecuteReader();try{$list=New-Object 'System.Collections.Generic.List[object]';while($r.Read()){$row=[ordered]@{};for($i=0;$i -lt $r.FieldCount;$i++){$row[$r.GetName($i)]=if($r.IsDBNull($i)){$null}else{$r.GetValue($i)}};$list.Add([pscustomobject]$row)};return $list.ToArray()}finally{$r.Dispose()}}finally{$c.Dispose()}
}
function Assert-AuthorityAndNoOtherTransaction {
 $authoritySql=@'
IF DB_NAME()<>N'website_tin_tuc' OR COALESCE(CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),N'')<>N'WIN-S1IBJB84OOO'
 THROW 51000,'Wrong server/database.',1;
IF @@TRANCOUNT<>0 OR ORIGINAL_LOGIN()=N'website_tin_tuc_app'
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CONTROL'),0)<>1
 THROW 51000,'Clean dedicated DBA connection with database CONTROL required.',1;
IF COALESCE((SELECT default_schema_name FROM sys.database_principals WHERE principal_id=DATABASE_PRINCIPAL_ID()),N'dbo')<>N'dbo'
 THROW 51000,'DBA default schema must be dbo for this EF script.',1;
IF COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),0)<>1
 AND COALESCE(HAS_PERMS_BY_NAME(NULL,'SERVER','VIEW SERVER PERFORMANCE STATE'),0)<>1
 AND COALESCE(HAS_PERMS_BY_NAME(NULL,'SERVER','VIEW SERVER STATE'),0)<>1
 THROW 51000,'DBA transaction metadata visibility required; do not grant it to the runtime login.',1;
IF EXISTS(SELECT 1 FROM sys.dm_tran_database_transactions d
 JOIN sys.dm_tran_session_transactions s ON s.transaction_id=d.transaction_id
 WHERE d.database_id=DB_ID() AND s.session_id<>@@SPID AND s.is_user_transaction=1)
 THROW 51000,'Another target user transaction is active; stop and reconcile, never KILL or blind-retry.',1;
'@
 Execute $authoritySql 'precheck.authority-and-transactions'
}
function Rights-Fingerprint {
 # Catalog names/descriptors and converted literals can carry different implicit
 # collations. Normalize EACH string operand/output inside this SELECT only.
 # Never ALTER database/server collation or weaken binary bootstrap guards.
 $fingerprintSql=@'
SELECT N'principal' COLLATE DATABASE_DEFAULT AS Kind,
 CONVERT(nvarchar(128),principal_id) COLLATE DATABASE_DEFAULT AS Id,
 (name COLLATE DATABASE_DEFAULT+N'|'+type_desc COLLATE DATABASE_DEFAULT+N'|'+CONVERT(nvarchar(170),sid,2) COLLATE DATABASE_DEFAULT) COLLATE DATABASE_DEFAULT AS Detail
FROM sys.database_principals WHERE name=N'website_tin_tuc_app'
UNION ALL SELECT N'role' COLLATE DATABASE_DEFAULT,CONVERT(nvarchar(128),r.role_principal_id) COLLATE DATABASE_DEFAULT,p.name COLLATE DATABASE_DEFAULT
FROM sys.database_role_members r JOIN sys.database_principals p ON p.principal_id=r.role_principal_id
WHERE r.member_principal_id=DATABASE_PRINCIPAL_ID(N'website_tin_tuc_app')
UNION ALL SELECT N'permission' COLLATE DATABASE_DEFAULT,CONCAT(class,N':',major_id,N':',minor_id) COLLATE DATABASE_DEFAULT,
 (state_desc COLLATE DATABASE_DEFAULT+N'|'+permission_name COLLATE DATABASE_DEFAULT) COLLATE DATABASE_DEFAULT
FROM sys.database_permissions WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'website_tin_tuc_app')
UNION ALL SELECT N'loginSid' COLLATE DATABASE_DEFAULT,N'website_tin_tuc_app' COLLATE DATABASE_DEFAULT,CONVERT(nvarchar(170),SUSER_SID(N'website_tin_tuc_app'),2) COLLATE DATABASE_DEFAULT
ORDER BY Kind,Id,Detail;
'@
 $data=@(Rows $fingerprintSql 'metadata.application-rights-fingerprint')
 return Byte-Sha ([Text.Encoding]::UTF8.GetBytes(($data|ConvertTo-Json -Depth 6 -Compress)))
}
$stage='CONNECT';$ddlStarted=$false;$lease=$false
try{
 $connection.Open();$stage='PRECHECK'
 Assert-AuthorityAndNoOtherTransaction
 foreach($batch in $guardBatches){Execute $batch 'precheck.bootstrap-before-backup'}
 Execute "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'LandPortal.Sql1934.BootstrapRecovery',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; IF @r<0 THROW 51000,'Recovery already active; stop.',1;" 'precheck.acquire-recovery-lease'
 $lease=$true;$rightsBefore=Rights-Fingerprint
 $bootstrapBefore=@(Rows "SELECT OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') AS HistoryId,OBJECT_ID(N'dbo.PK___EFMigrationsHistory',N'PK') AS PrimaryKeyId;")[0]
 $stage='BACKUP_HEADER'
 $c=Command 'RESTORE HEADERONLY FROM DISK=@file;' 'backup.header'
 [void]$c.Parameters.Add('@file',[Data.SqlDbType]::NVarChar,4000);$c.Parameters['@file'].Value=$backup
 try{$r=$c.ExecuteReader();try{
  if(-not $r.Read()){throw 'Backup header absent'}
  if([string]$r['DatabaseName'] -cne 'website_tin_tuc' -or [string]$r['ServerName'] -ine 'WIN-S1IBJB84OOO' -or [int]$r['BackupType'] -ne 1 -or -not [bool]$r['HasBackupChecksums'] -or -not [bool]$r['IsCopyOnly']){throw 'Backup is not the expected target COPY_ONLY full/checksummed set'}
  $backupInfo=@{database=[string]$r['DatabaseName'];server=[string]$r['ServerName'];finish=[string]$r['BackupFinishDate'];backupSetGuid=[string]$r['BackupSetGUID'];path=$backup}
  if($r.Read()){throw 'Multiple appended backup sets refused'}
 }finally{$r.Dispose()}}finally{$c.Dispose()}
 $stage='BACKUP_VERIFY'
 $c=Command 'RESTORE VERIFYONLY FROM DISK=@file WITH CHECKSUM;' 'backup.verify';$c.CommandTimeout=120
 [void]$c.Parameters.Add('@file',[Data.SqlDbType]::NVarChar,4000);$c.Parameters['@file'].Value=$backup
 try{[void]$c.ExecuteNonQuery()}finally{$c.Dispose()}
 Audit @{stage='BACKUP_READONLY_VERIFY_PASS';backup=$backupInfo;newBackupCreated=$false;databaseRestored=$false}
 Assert-AuthorityAndNoOtherTransaction
 foreach($batch in $guardBatches){Execute $batch 'precheck.bootstrap-after-backup'}
 if($Mode -eq 'Inspect'){
  Audit @{stage='READ_ONLY_PREFLIGHT_PASS';state='EXACT_HISTORY_PK_ZERO_ROWS';rightsSha256=$rightsBefore;ddlExecuted=$false}
  Write-Output 'READ_ONLY_PREFLIGHT_PASS; history/PK structure, zero rows and existing backup verified. No DDL, role/grant change or data import.'
 }else{
  $stage='DDL';$ddlStarted=$true;Audit @{stage='DDL_STARTED_RECONCILE_IF_INTERRUPTED';migrationCount=16;roleCreationAllowed=$false;importAllowed=$false}
  $batchNumber=0;foreach($batch in $schemaBatches){$batchNumber++;Execute $batch ('schema.batch.'+$batchNumber)}
  $stage='READBACK'
  $tables=@(Rows "SELECT SCHEMA_NAME(schema_id)+N'.'+name AS Name FROM sys.tables WHERE is_ms_shipped=0 ORDER BY Name;")
  $ids=@(Rows 'SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;')
  if(@(Compare-Object @($manifest.expectedTables|Sort-Object) @($tables|ForEach-Object {$_.Name}|Sort-Object)).Count -or @(Compare-Object @($manifest.expectedMigrations|Sort-Object) @($ids|ForEach-Object {$_.MigrationId}|Sort-Object)).Count){throw 'Table/migration readback differs; stop and reconcile'}
  $indices=@(Rows "SELECT t.name AS TableName,i.name AS IndexName,i.filter_definition,i.is_unique,i.is_disabled FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id WHERE t.schema_id=SCHEMA_ID(N'dbo') AND i.has_filter=1;")
  foreach($expected in $manifest.filteredIndexes){
   $actual=@($indices|Where-Object {$_.TableName -ceq $expected.table -and $_.IndexName -ceq $expected.index})
   if($actual.Count -ne 1 -or -not $actual[0].is_unique -or $actual[0].is_disabled -or (([string]$actual[0].filter_definition -replace '[\s\[\]()]','').ToLowerInvariant() -cne ([string]$expected.filter -replace '[\s\[\]()]','').ToLowerInvariant())){throw 'Filtered index readback mismatch; stop and reconcile'}
  }
  Execute "IF DATABASE_PRINCIPAL_ID(N'LandPortalRuntime') IS NOT NULL THROW 51000,'Runtime role unexpectedly present; reconcile.',1; IF EXISTS(SELECT 1 FROM dbo.Contents) OR EXISTS(SELECT 1 FROM dbo.Revisions) OR EXISTS(SELECT 1 FROM dbo.MediaAssets) OR EXISTS(SELECT 1 FROM dbo.SourceRecords) OR EXISTS(SELECT 1 FROM dbo.AspNetUsers) THROW 51000,'Unexpected imported content/Identity data; reconcile.',1;"
  $rightsAfter=Rights-Fingerprint;if($rightsAfter -cne $rightsBefore){throw 'Application mapping/rights drift; reconcile; do not auto-reset'}
  $bootstrapAfter=@(Rows "SELECT OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') AS HistoryId,OBJECT_ID(N'dbo.PK___EFMigrationsHistory',N'PK') AS PrimaryKeyId;")[0]
  if($bootstrapAfter.HistoryId -ne $bootstrapBefore.HistoryId -or $bootstrapAfter.PrimaryKeyId -ne $bootstrapBefore.PrimaryKeyId){throw 'Original history/PK object IDs changed; reconcile'}
  Audit @{stage='SCHEMA_RECOVERY_VERIFIED';tables=$tables.Count;migrations=$ids.Count;filteredIndexesChecked=$manifest.filteredIndexes.Count;applicationRightsPreserved=$true;rightsSha256=$rightsAfter;roleCreated=$false;dataImported=$false;historyTablePreserved=$true}
  Write-Output 'SCHEMA_RECOVERY_VERIFIED: 70 tables/16 migrations; filtered indexes verified; original history/PK and application rights retained; no role creation or data import.'
 }
}catch{
 $failure=$_.Exception;$sqlFailure=$null
 for($errorNode=$failure;$null -ne $errorNode;$errorNode=$errorNode.InnerException){if($errorNode -is [Data.SqlClient.SqlException]){$sqlFailure=$errorNode;break}}
 $base=if($sqlFailure){$sqlFailure}else{$failure.GetBaseException()}
 $number=if($sqlFailure){$sqlFailure.Number}else{$null}
 $safeErrors=@()
 if($sqlFailure){foreach($sqlError in $sqlFailure.Errors){$safeErrors+=@{number=$sqlError.Number;state=$sqlError.State;severity=$sqlError.Class;line=$sqlError.LineNumber;message=(Safe-SqlMessage $sqlError.Number $sqlError.Message $script:ActiveSqlQueryText)}}}
 $safeMessage=if($safeErrors.Count){($safeErrors|ForEach-Object {$_.message}) -join ' | '}else{'Non-SQL failure; details omitted by secret-safe diagnostic policy.'}
 Audit @{stage='STOP_RECONCILE_REQUIRED';failedStage=$stage;queryId=$script:ActiveSqlQueryId;querySha256=$script:ActiveSqlQuerySha256;sqlNumber=$number;errorType=$base.GetType().Name;safeMessage=$safeMessage;sqlErrors=$safeErrors;ddlMayHaveCommitted=$ddlStarted;autoRetry=$false}
 throw ('Recovery stopped at '+$stage+'; query='+$script:ActiveSqlQueryId+'; SQL number='+$number+'. '+$safeMessage+' Read private receipt; no automatic retry/DROP/restore.')
}finally{
 if($lease -and $connection.State -eq [Data.ConnectionState]::Open){try{Execute "EXEC sys.sp_releaseapplock @Resource=N'LandPortal.Sql1934.BootstrapRecovery',@LockOwner='Session';"}catch{}}
 # Closing this dedicated non-pooled session rolls back only its uncommitted
 # work. Earlier EF migration commits, if any, must be reconciled separately.
 $connection.Dispose();$password.Dispose();$writer.Dispose()
}
