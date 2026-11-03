[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$PacketDirectory,
 [Parameter(Mandatory=$true)][string]$PrivateConfig,
 [string]$VerifiedBackupPath,
 [string]$ReportFile,
 [System.Management.Automation.PSCredential]$DbaCredential,
 [switch]$TemporaryCertificateTrust,
 [switch]$Apply
)
# Manual DBA entrypoint, PLAN by default. Reuses the exact reviewed role SQL.
# No database/login creation, migration, account copying or data import.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$packet=[IO.Path]::GetFullPath($PacketDirectory);$private=[IO.Path]::GetFullPath($PrivateConfig)
foreach($path in @($packet,$private)){
 for($node=$path;$node;$node=[IO.Path]::GetDirectoryName($node)){
  if((Test-Path -LiteralPath $node) -and ((Get-Item -LiteralPath $node).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse input refused'}
 }
}
if($private.StartsWith($packet+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Private config must be outside the handoff packet/webroot'}
$manifest=Join-Path $packet 'deployment-manifest.json';$role=Join-Path $packet 'runtime-role.sqlcmd'
function Read-ReviewedFile([string]$Path,[string]$ExpectedSha){
 $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 try{
  $memory=New-Object IO.MemoryStream;$stream.CopyTo($memory);$bytes=$memory.ToArray();$memory.Dispose()
  $sha=[Security.Cryptography.SHA256]::Create()
  try{$actual=([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
  if($actual -cne $ExpectedSha){throw 'Exact reviewed artifact SHA differs; no connection attempted'}
  return [Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF)
 }finally{$stream.Dispose()}
}
$reviewed=Read-ReviewedFile $manifest 'a1f06b6c631d8e2dc24d71eec556d20f3d4137c0a24a15104398c83cc5403999'|ConvertFrom-Json
$expectedMigrations=@($reviewed.expectedMigrations)+@('20261102042625_DraftConsultationEmail')
$text=Read-ReviewedFile $role 'a34ad38165061782dee62dc2abf073cf0c337defa6794d5d4cb6977af458dd96'
$batches=New-Object 'System.Collections.Generic.List[string]';$batch=''
foreach($line in ($text -split '\r?\n')){
 if($line -match '^\s*:'){
  if($line.Trim() -ine ':ON ERROR EXIT'){throw 'Unrecognized SQLCMD directive refused'}
  continue
 }
 if($line -match '^\s*GO\s*$'){if($batch.Trim()){$batches.Add($batch)};$batch=''}else{$batch+=$line+"`n"}
}
if($batch.Trim()){$batches.Add($batch)}
if(-not $Apply){Write-Output 'PLAN ONLY: exact role SQL verified; Apply requires70tables/exact17migrations, FR06 columns and separate security approval. No private config read, connection, backup or SQL execution.';return}
if([string]::IsNullOrWhiteSpace($VerifiedBackupPath)){throw 'Previously verified backup server path required'}
$c=Get-Content -LiteralPath $private -Raw -Encoding UTF8|ConvertFrom-Json
if(-not($c.PSObject.Properties.Name -contains 'Runtime') -or -not($c.Runtime.PSObject.Properties.Name -contains 'RolePassword')){throw 'Private target Runtime.RolePassword required; SQL Login password is not the role secret'}
$secret=[string]$c.Runtime.RolePassword
if($secret.Length -lt 24 -or $secret.Length -gt 128){throw 'Target role secret must have 24..128 characters; value omitted'}
try{$sql=New-Object System.Data.SqlClient.SqlConnectionStringBuilder ([string]$c.ConnectionStrings.Portal)}catch{throw 'Invalid private target connection; values omitted'}
if($sql['Data Source'] -cne '45.120.229.43,1433' -or $sql['Initial Catalog'] -cne 'website_tin_tuc' -or $sql['User ID'] -cne 'website_tin_tuc_app' -or $sql['Integrated Security'] -or $sql['Persist Security Info']){throw 'Private endpoint/database/runtime login scope mismatch'}
if(-not $DbaCredential){$DbaCredential=Get-Credential -Message 'Dedicated SQL DBA account; never the runtime application login'}
if($DbaCredential.UserName -ieq 'website_tin_tuc_app'){throw 'Runtime login cannot deploy its own role'}
# Use SqlCredential: the DBA password is never placed in a connection string.
[void]$sql.Remove('User ID');[void]$sql.Remove('Password')
$sql['Pooling']=$false;$sql['Encrypt']=$true;$sql['TrustServerCertificate']=[bool]$TemporaryCertificateTrust;$sql['Connect Timeout']=15
$password=$DbaCredential.Password.Copy();$password.MakeReadOnly()
$credential=New-Object System.Data.SqlClient.SqlCredential($DbaCredential.UserName,$password)
$connection=New-Object System.Data.SqlClient.SqlConnection($sql.ConnectionString,$credential)
function Sql-Command([string]$Query){$command=$connection.CreateCommand();$command.CommandTimeout=120;$command.CommandText=$Query;return $command}
function Rows([string]$Query){
 $command=Sql-Command $Query
 try{$reader=$command.ExecuteReader();try{$rows=New-Object 'System.Collections.Generic.List[object]';while($reader.Read()){$row=[ordered]@{};for($i=0;$i -lt $reader.FieldCount;$i++){$row[$reader.GetName($i)]=if($reader.IsDBNull($i)){$null}else{$reader.GetValue($i)}};$rows.Add([pscustomobject]$row)};return $rows.ToArray()}finally{$reader.Dispose()}}finally{$command.Dispose()}
}
function App-RightsFingerprint {
 $facts=@(Rows "SELECT name,type_desc,CONVERT(varchar(170),sid,2) AS Sid FROM sys.database_principals WHERE name=N'website_tin_tuc_app';")
 $facts+=@(Rows "SELECT class,major_id,minor_id,permission_name,state_desc FROM sys.database_permissions WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'website_tin_tuc_app') ORDER BY class,major_id,minor_id,permission_name,state_desc;")
 $facts+=@(Rows "SELECT role_principal_id FROM sys.database_role_members WHERE member_principal_id=DATABASE_PRINCIPAL_ID(N'website_tin_tuc_app') ORDER BY role_principal_id;")
 $hash=[Security.Cryptography.SHA256]::Create()
 try{return ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes(($facts|ConvertTo-Json -Depth 4 -Compress))))).Replace('-','').ToLowerInvariant()}finally{$hash.Dispose()}
}
if(-not $ReportFile){$ReportFile=Join-Path (Split-Path $private) ('runtime-role-'+[guid]::NewGuid().ToString('N')+'.jsonl')}
$report=[IO.Path]::GetFullPath($ReportFile)
if((Test-Path -LiteralPath $report) -or -not $report.StartsWith((Split-Path $private)+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'New receipt beside private config required'}
for($node=$report;$node;$node=[IO.Path]::GetDirectoryName($node)){if((Test-Path -LiteralPath $node) -and ((Get-Item -LiteralPath $node).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse receipt refused'}}
$fileSecurity=New-Object Security.AccessControl.FileSecurity;$fileSecurity.SetAccessRuleProtection($true,$false)
foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'))){$fileSecurity.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','Allow')))}
$receiptStream=[IO.File]::Create($report,4096,[IO.FileOptions]::None,$fileSecurity)
$writer=New-Object IO.StreamWriter($receiptStream,([Text.UTF8Encoding]::new($false)));$writer.AutoFlush=$true
function Audit([hashtable]$row){$row.utc=[DateTimeOffset]::UtcNow.ToString('O');$row.target='WIN-S1IBJB84OOO/website_tin_tuc';$writer.WriteLine(($row|ConvertTo-Json -Depth 5 -Compress))}
$stage='CONNECT';$securityMayHaveCommitted=$false
$contextSet=$false
try{
 Audit @{stage='STARTED';roleSqlSha256='a34ad38165061782dee62dc2abf073cf0c337defa6794d5d4cb6977af458dd96';backupFile=$VerifiedBackupPath}
 $connection.Open()
 $stage='PRECHECK'
 $guard=Sql-Command @'
IF DB_NAME()<>N'website_tin_tuc' OR CAST(SERVERPROPERTY('ServerName') AS nvarchar(128))<>N'WIN-S1IBJB84OOO'
 THROW 51000,'Wrong physical server/database.',1;
IF ORIGINAL_LOGIN()=N'website_tin_tuc_app' OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CONTROL'),0)<>1
 THROW 51000,'Dedicated DBA authority required.',1;
IF @@TRANCOUNT<>0 THROW 51000,'Clean operator session required.',1;
IF (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0)<>70
 OR (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>17
 OR (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory)<>N'20261102042625_DraftConsultationEmail'
 THROW 51000,'Schema readback must pass before role provisioning.',1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id IN(OBJECT_ID(N'dbo.DraftMetadata'),OBJECT_ID(N'dbo.RevisionMetadata'))
 AND name=N'ConsultationEmail' AND system_type_id=231 AND max_length=-1 AND is_nullable=1)<>2
 THROW 51000,'FR06 column readback differs.',1;
IF COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),0)<>1
 AND COALESCE(HAS_PERMS_BY_NAME(NULL,'SERVER','VIEW SERVER PERFORMANCE STATE'),0)<>1
 AND COALESCE(HAS_PERMS_BY_NAME(NULL,'SERVER','VIEW SERVER STATE'),0)<>1
 THROW 51000,'DBA transaction metadata visibility required.',1;
IF EXISTS(SELECT 1 FROM sys.dm_tran_database_transactions d
 JOIN sys.dm_tran_session_transactions s ON s.transaction_id=d.transaction_id
 WHERE d.database_id=DB_ID() AND s.session_id<>@@SPID AND s.is_user_transaction=1)
 THROW 51000,'Active target transaction; stop without changing security.',1;
IF DATABASE_PRINCIPAL_ID(N'LandPortalRuntime') IS NOT NULL THROW 51000,'Role exists: reconcile; do not reset.',1;
IF NOT EXISTS(SELECT 1 FROM sys.database_principals WHERE name=N'website_tin_tuc_app' AND type='S' AND sid=SUSER_SID(N'website_tin_tuc_app'))
 THROW 51000,'Existing SQL login/user SID mismatch.',1;
IF EXISTS(SELECT 1 FROM sys.database_role_members WHERE member_principal_id=DATABASE_PRINCIPAL_ID(N'website_tin_tuc_app'))
 THROW 51000,'Unexpected application user database-role membership.',1;
IF EXISTS(SELECT 1 FROM sys.database_permissions WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'website_tin_tuc_app')
 AND NOT(class=0 AND major_id=0 AND minor_id=0 AND permission_name=N'CONNECT' AND state=N'G'))
 THROW 51000,'Unexpected direct application permissions.',1;
IF EXISTS(SELECT 1 FROM sys.server_role_members WHERE member_principal_id=SUSER_ID(N'website_tin_tuc_app'))
 THROW 51000,'Unexpected application server-role membership.',1;
'@
 try{[void]$guard.ExecuteNonQuery()}finally{$guard.Dispose()}
 $history=Sql-Command 'SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;'
 try{$reader=$history.ExecuteReader();try{$ids=New-Object 'System.Collections.Generic.List[string]';while($reader.Read()){$ids.Add($reader.GetString(0))}}finally{$reader.Dispose()}}finally{$history.Dispose()}
 if(@(Compare-Object @($expectedMigrations|Sort-Object) @($ids.ToArray()|Sort-Object)).Count){throw 'Exact17 migration IDs differ; no security writes'}
 $rightsBefore=App-RightsFingerprint
 Audit @{stage='TARGET_SCHEMA_APP_MAPPING_PASS';tables=70;migrations=17;applicationRightsSha256=$rightsBefore;roleAbsent=$true}
 $stage='BACKUP_VERIFY'
 $header=Sql-Command 'RESTORE HEADERONLY FROM DISK=@file;'
 [void]$header.Parameters.Add('@file',[Data.SqlDbType]::NVarChar,4000);$header.Parameters['@file'].Value=$VerifiedBackupPath
 try{
  $reader=$header.ExecuteReader()
  try{
   if(-not $reader.Read() -or [string]$reader['DatabaseName'] -cne 'website_tin_tuc' -or [string]$reader['ServerName'] -cne 'WIN-S1IBJB84OOO' -or [int]$reader['BackupType'] -ne 1 -or -not [bool]$reader['IsCopyOnly'] -or [bool]$reader['IsDamaged'] -or -not [bool]$reader['HasBackupChecksums']){throw 'Backup scope/checksum/single-set verification failed'}
   $backupGuid=[string]$reader['BackupSetGUID']
   if($reader.Read()){throw 'Multiple backup sets refused'}
  }finally{$reader.Dispose()}
 }finally{$header.Dispose()}
 $verify=Sql-Command 'RESTORE VERIFYONLY FROM DISK=@file WITH CHECKSUM;'
 [void]$verify.Parameters.Add('@file',[Data.SqlDbType]::NVarChar,4000);$verify.Parameters['@file'].Value=$VerifiedBackupPath
 try{[void]$verify.ExecuteNonQuery()}finally{$verify.Dispose()}
 Audit @{stage='BACKUP_READONLY_VERIFY_PASS';backupSetGuid=$backupGuid;newBackupCreated=$false}
 $set=$connection.CreateCommand();$set.CommandType=[Data.CommandType]::StoredProcedure;$set.CommandText='sys.sp_set_session_context'
 [void]$set.Parameters.Add('@key',[Data.SqlDbType]::NVarChar,128);$set.Parameters['@key'].Value='LandPortal.RuntimeRoleSecret'
 [void]$set.Parameters.Add('@value',[Data.SqlDbType]::NVarChar,128);$set.Parameters['@value'].Value=$secret
 try{[void]$set.ExecuteNonQuery();$contextSet=$true}finally{$set.Parameters.Clear();$set.Dispose()}
 $stage='SECURITY_APPLY';$securityMayHaveCommitted=$true
 Audit @{stage='SECURITY_STARTED_RECONCILE_IF_INTERRUPTED'}
 foreach($sqlBatch in $batches){$command=Sql-Command $sqlBatch;try{[void]$command.ExecuteNonQuery()}finally{$command.Dispose()}}
 $stage='READBACK'
 $check=Sql-Command "SELECT COUNT(*) FROM sys.database_principals WHERE name=N'LandPortalRuntime' AND type='A';"
 try{if([int]$check.ExecuteScalar() -ne 1){throw 'Role creation readback failed'}}finally{$check.Dispose()}
 $expectedPermissions=@('DATABASE||0|ALTER|DENY')
 foreach($permission in @('SELECT','INSERT','UPDATE','DELETE')){$expectedPermissions+='SCHEMA|dbo|0|'+$permission+'|GRANT'}
 foreach($table in @('AuditLogs','InteractionPublicSnapshots','InquiryAssignments','InquiryCalendars','InquiryCalendarExceptions','InquiryExtensions','OutboxReconciliations')){foreach($permission in @('UPDATE','DELETE')){$expectedPermissions+='OBJECT_OR_COLUMN|dbo.'+$table+'|0|'+$permission+'|DENY'}}
 $actualPermissions=@(Rows @'
SELECT class_desc,
 CASE class WHEN 0 THEN N'' WHEN 3 THEN SCHEMA_NAME(major_id) WHEN 1 THEN OBJECT_SCHEMA_NAME(major_id)+N'.'+OBJECT_NAME(major_id) ELSE N'UNEXPECTED' END AS Securable,
 minor_id,permission_name,state_desc
FROM sys.database_permissions WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'LandPortalRuntime');
'@)
 $actualKeys=@($actualPermissions|ForEach-Object {$_.class_desc+'|'+$_.Securable+'|'+$_.minor_id+'|'+$_.permission_name+'|'+$_.state_desc})
 if(@(Compare-Object @($expectedPermissions|Sort-Object) @($actualKeys|Sort-Object)).Count){throw 'Application role permissions differ; reconcile without retry'}
 $rightsAfter=App-RightsFingerprint
 if($rightsAfter -cne $rightsBefore){throw 'Application login mapping/permissions changed; reconcile without resetting'}
 Audit @{stage='RUNTIME_ROLE_CREATED_READBACK_PASS';tables=70;migrations=17;rolePermissionCount=$actualKeys.Count;applicationMappingAndRightsPreserved=$true;applicationRightsSha256=$rightsAfter;roleReset=$false;schemaChanged=$false;dataImported=$false}
 Write-Output 'RUNTIME_ROLE_CREATED_READBACK_PASS; exact existing SQL file used; no schema/data/Identity import or login/database creation. Retain operator backup record.'
}catch{
 $number=$null;for($failure=$_.Exception;$failure;$failure=$failure.InnerException){if($failure -is [Data.SqlClient.SqlException]){$number=$failure.Number;break}}
 Audit @{stage='STOP_RECONCILE_REQUIRED';failedStage=$stage;sqlNumber=$number;securityMayHaveCommitted=$securityMayHaveCommitted;autoRetry=$false}
 throw ('Role deployment stopped at '+$stage+'; SQL number='+$number+'. Read private receipt; no automatic retry.')
}
finally{
 if($contextSet -and $connection.State -eq [Data.ConnectionState]::Open){
  try{$clear=Sql-Command "EXEC sys.sp_set_session_context @key=N'LandPortal.RuntimeRoleSecret',@value=NULL;";[void]$clear.ExecuteNonQuery();$clear.Dispose()}catch{}
 }
 $secret=$null;$connection.Dispose();$password.Dispose();$writer.Dispose()
}
