[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$PacketDirectory,
 [Parameter(Mandatory=$true)][string]$PrivateConfig,
 [string]$VerifiedBackupPath,
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
[void](Read-ReviewedFile $manifest 'a1f06b6c631d8e2dc24d71eec556d20f3d4137c0a24a15104398c83cc5403999')
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
if(-not $Apply){Write-Output 'PLAN ONLY: exact role SQL verified. No private config read, connection, backup or SQL execution. Supply -Apply only after separate owner approval.';return}
if([string]::IsNullOrWhiteSpace($VerifiedBackupPath)){throw 'Previously verified backup server path required'}
$c=Get-Content -LiteralPath $private -Raw -Encoding UTF8|ConvertFrom-Json
if(-not($c.PSObject.Properties.Name -contains 'Runtime') -or -not($c.Runtime.PSObject.Properties.Name -contains 'RolePassword')){throw 'Private target Runtime.RolePassword required; SQL Login password is not the role secret'}
$secret=[string]$c.Runtime.RolePassword
if($secret.Length -lt 24 -or $secret.Length -gt 128){throw 'Target role secret must have 24..128 characters; value omitted'}
try{$sql=New-Object System.Data.SqlClient.SqlConnectionStringBuilder ([string]$c.ConnectionStrings.Portal)}catch{throw 'Invalid private target connection; values omitted'}
if($sql.DataSource -cne '45.120.229.43,1433' -or $sql.InitialCatalog -cne 'website_tin_tuc' -or $sql.UserID -cne 'website_tin_tuc_app' -or $sql.IntegratedSecurity -or $sql.PersistSecurityInfo){throw 'Private endpoint/database/runtime login scope mismatch'}
if(-not $DbaCredential){$DbaCredential=Get-Credential -Message 'Dedicated SQL DBA account; never the runtime application login'}
if($DbaCredential.UserName -ieq 'website_tin_tuc_app'){throw 'Runtime login cannot deploy its own role'}
# Use SqlCredential: the DBA password is never placed in a connection string.
[void]$sql.Remove('User ID');[void]$sql.Remove('Password')
$sql['Pooling']=$false;$sql['Encrypt']=$true;$sql['TrustServerCertificate']=[bool]$TemporaryCertificateTrust;$sql['Connect Timeout']=15
$password=$DbaCredential.Password.Copy();$password.MakeReadOnly()
$credential=New-Object System.Data.SqlClient.SqlCredential($DbaCredential.UserName,$password)
$connection=New-Object System.Data.SqlClient.SqlConnection($sql.ConnectionString,$credential)
function Sql-Command([string]$Query){$command=$connection.CreateCommand();$command.CommandTimeout=120;$command.CommandText=$Query;return $command}
$contextSet=$false
try{
 $connection.Open()
 $guard=Sql-Command @'
IF DB_NAME()<>N'website_tin_tuc' OR CAST(SERVERPROPERTY('ServerName') AS nvarchar(128))<>N'WIN-S1IBJB84OOO'
 THROW 51000,'Wrong physical server/database.',1;
IF ORIGINAL_LOGIN()=N'website_tin_tuc_app' OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CONTROL'),0)<>1
 THROW 51000,'Dedicated DBA authority required.',1;
IF @@TRANCOUNT<>0 THROW 51000,'Clean operator session required.',1;
IF (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0)<>70
 OR (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>16
 OR (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory)<>N'20261027025057_AuditRecoveryLookup'
 THROW 51000,'Schema readback must pass before role provisioning.',1;
IF DATABASE_PRINCIPAL_ID(N'LandPortalRuntime') IS NOT NULL THROW 51000,'Role exists: reconcile; do not reset.',1;
'@
 try{[void]$guard.ExecuteNonQuery()}finally{$guard.Dispose()}
 $header=Sql-Command 'RESTORE HEADERONLY FROM DISK=@file;'
 [void]$header.Parameters.Add('@file',[Data.SqlDbType]::NVarChar,4000);$header.Parameters['@file'].Value=$VerifiedBackupPath
 try{
  $reader=$header.ExecuteReader()
  try{if(-not $reader.Read() -or [string]$reader['DatabaseName'] -cne 'website_tin_tuc' -or -not [bool]$reader['HasBackupChecksums'] -or $reader.Read()){throw 'Backup scope/checksum/single-set verification failed'}}finally{$reader.Dispose()}
 }finally{$header.Dispose()}
 $verify=Sql-Command 'RESTORE VERIFYONLY FROM DISK=@file WITH CHECKSUM;'
 [void]$verify.Parameters.Add('@file',[Data.SqlDbType]::NVarChar,4000);$verify.Parameters['@file'].Value=$VerifiedBackupPath
 try{[void]$verify.ExecuteNonQuery()}finally{$verify.Dispose()}
 $set=$connection.CreateCommand();$set.CommandType=[Data.CommandType]::StoredProcedure;$set.CommandText='sys.sp_set_session_context'
 [void]$set.Parameters.Add('@key',[Data.SqlDbType]::NVarChar,128);$set.Parameters['@key'].Value='LandPortal.RuntimeRoleSecret'
 [void]$set.Parameters.Add('@value',[Data.SqlDbType]::NVarChar,128);$set.Parameters['@value'].Value=$secret
 try{[void]$set.ExecuteNonQuery();$contextSet=$true}finally{$set.Parameters.Clear();$set.Dispose()}
 foreach($sqlBatch in $batches){$command=Sql-Command $sqlBatch;try{[void]$command.ExecuteNonQuery()}finally{$command.Dispose()}}
 $check=Sql-Command "SELECT COUNT(*) FROM sys.database_principals WHERE name=N'LandPortalRuntime' AND type='A';"
 try{if([int]$check.ExecuteScalar() -ne 1){throw 'Role creation readback failed'}}finally{$check.Dispose()}
 Write-Output 'RUNTIME_ROLE_CREATED_READBACK_PASS; exact existing SQL file used; no schema/data/Identity import or login/database creation. Retain operator backup record.'
}catch [System.Data.SqlClient.SqlException]{throw ('Role deployment stopped; SQL number='+$_.Exception.Number+'. Reconcile before retry; no automatic reset/rollback outside the role transaction.')}
finally{
 if($contextSet -and $connection.State -eq [Data.ConnectionState]::Open){
  try{$clear=Sql-Command "EXEC sys.sp_set_session_context @key=N'LandPortal.RuntimeRoleSecret',@value=NULL;";[void]$clear.ExecuteNonQuery();$clear.Dispose()}catch{}
 }
 $secret=$null;$connection.Dispose();$password.Dispose()
}
