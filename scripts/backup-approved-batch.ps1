[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$BackupRoot,
 [Parameter(Mandatory=$true)][ValidatePattern('^[a-z0-9]{1,12}$')][string]$DirectoryId,
 [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
 [Parameter(Mandatory=$true)][string]$DependencyManifest,
 [Parameter(Mandatory=$true)][string]$DependencyManifestSha,
 [Parameter(Mandatory=$true)][string[]]$AdditionalFiles,
 [switch]$AllowAdditionalRestorePoint
)
# A complete backup receipt is emitted only after all SQL/files/dependencies verify.
# No ACL or application guard changes. The configured root must already be private
# and service-accessible, within the existing Apply guard's project backup scope.
. (Join-Path $PSScriptRoot 'dev-signing-common.ps1')
$ProgressPreference='SilentlyContinue'
Assert-DevSigningScope Development
$retention=Join-Path $script:ProjectRoot '.local/cleanup-retention.json'
if((Test-Path -LiteralPath $retention) -and -not $AllowAdditionalRestorePoint){throw 'A verified restore point is retained. Review capacity/retention and explicitly use -AllowAdditionalRestorePoint for this approved operation. No automatic backup deletion.'}
$parent=Resolve-SigningPath $BackupRoot '.local/backups'
if(-not (Test-Path -LiteralPath $parent -PathType Container)){throw 'Existing private backup root required; no automatic ACL grants'}
$parentAcl=Get-Acl -LiteralPath $parent
$beforeSddl=$parentAcl.Sddl
$operator=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$service=([Security.Principal.NTAccount]'NT SERVICE\MSSQL$SQLEXPRESS').Translate([Security.Principal.SecurityIdentifier]).Value
$allowed=@($operator,$service,'S-1-5-18','S-1-5-32-544')
$hasService=$false
foreach($ace in $parentAcl.Access){
 if($ace.AccessControlType -eq 'Allow'){
  $sid=$ace.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
  if($sid -notin $allowed){throw 'Existing root ACL permits unexpected principal; no ACL changes performed'}
  if($sid -eq $service){$hasService=$true}
 }
}
if(-not $hasService){throw 'SQL service access absent; no permission changes permitted'}
$report=Resolve-SigningPath $EvidenceDirectory '.local/evidence/phase3f'
$base=Join-Path $parent $DirectoryId
if(Test-Path -LiteralPath $base){throw 'Unique backup directory required'}
if((Get-SigningSha $DependencyManifest) -cne $DependencyManifestSha){throw 'Dependency allowlist changed'}
$dependency=Get-Content -LiteralPath $DependencyManifest -Raw -Encoding UTF8|ConvertFrom-Json
$dependencies=@($dependency.files)
foreach($path in $AdditionalFiles){$full=Resolve-SigningPath $path;$dependencies+=@{sourcePath=$path;sha256=Get-SigningSha $full;bytes=(Get-Item $full).Length}}
foreach($file in $dependencies){$full=Resolve-SigningPath $file.sourcePath;if((Get-SigningSha $full) -cne $file.sha256 -or (Get-Item $full).Length -ne $file.bytes){throw 'Dependency exact-byte check failed'}}
$drive=Get-PSDrive -Name ([IO.Path]::GetPathRoot($base).Substring(0,1))
if($drive.Free -lt 4GB){throw 'Insufficient conservative free disk reserve for project SQL/file backups'}
[void][IO.Directory]::CreateDirectory($base)
Write-SigningJson ($report+'/backup-location-audit.json') @{configuredRoot=$parent;directoryId=$DirectoryId;path=$base;parentSddl=$beforeSddl;permissionsChanged=$false;freeBytes=$drive.Free;guardScope='Unchanged project .local/backups prefix';signedPlanChanged=$false;utc=[DateTimeOffset]::UtcNow.ToString('O')}
function ReadRows($conn,[string]$sql){$cmd=$conn.CreateCommand();$cmd.CommandText=$sql;$reader=$cmd.ExecuteReader();$rows=New-Object System.Collections.Generic.List[object];try{while($reader.Read()){$row=[ordered]@{};for($i=0;$i -lt $reader.FieldCount;$i++){$row[$reader.GetName($i)]=$reader.GetValue($i)};$rows.Add([pscustomobject]$row)}}finally{$reader.Dispose();$cmd.Dispose()};return ,$rows.ToArray()}
function SqlBackup([string]$db,[string]$shortName){
 $folder=Join-Path $base $shortName;[void][IO.Directory]::CreateDirectory($folder)
 $builder=Get-ProjectConnectionBuilder $db
 $conn=New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
 try{
  $conn.Open();$identity=(ReadRows $conn "SELECT DB_NAME() databaseName,CAST(SERVERPROPERTY('ServerName') AS nvarchar(200)) serverName")[0]
  if($identity.databaseName -cne $db -or $identity.serverName -ine 'MinhAnh\SQLEXPRESS'){throw 'Backup SQL target differs'}
  $bak=Join-Path $folder ($db+'.bak')
  if(Test-Path $bak){throw 'Backup overwrite refused'}
  foreach($sql in @(('BACKUP DATABASE ['+$db+'] TO DISK=@path WITH COPY_ONLY,CHECKSUM,INIT'),'RESTORE VERIFYONLY FROM DISK=@path WITH CHECKSUM')){
   $cmd=$conn.CreateCommand();$cmd.CommandTimeout=120;$cmd.CommandText=$sql;[void]$cmd.Parameters.AddWithValue('@path',$bak)
   try{[void]$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}
  }
  $cmd=$conn.CreateCommand();$cmd.CommandText='RESTORE HEADERONLY FROM DISK=@path';[void]$cmd.Parameters.AddWithValue('@path',$bak);$rd=$cmd.ExecuteReader()
  try{if(-not $rd.Read() -or $rd['DatabaseName'] -cne $db -or -not $rd['HasBackupChecksums'] -or -not $rd['IsCopyOnly']){throw 'Backup header mismatch'};$header=@{database=$db;isCopyOnly=[bool]$rd['IsCopyOnly'];hasBackupChecksums=[bool]$rd['HasBackupChecksums'];startLocal=$rd['BackupStartDate'].ToString('O');finishLocal=$rd['BackupFinishDate'].ToString('O')}}finally{$rd.Dispose();$cmd.Dispose()}
  return @{database=$db;utc=[DateTimeOffset]::UtcNow.ToString('O');path=$folder;verifyOnly='PASS';restoreTested=$false;databaseBackupSha256=Get-SigningSha $bak;sqlBackupBytes=(Get-Item $bak).Length;header=$header}
 }finally{$conn.Dispose()}
}
function FileTool([string]$label,[string[]]$arguments){
 $log=$report+'/'+$label+'.log';if(Test-Path $log){throw 'File-tool log already exists'}
 $saved=$ErrorActionPreference
 try{$ErrorActionPreference='Continue'; & rtk proxy python (Join-Path $PSScriptRoot 'backup-file-snapshot.py') @arguments *> $log;$code=$LASTEXITCODE}finally{$ErrorActionPreference=$saved}
 if($code -ne 0){throw ('File snapshot '+$label+' failed; inspect private log; STOP')}
 Get-Content $log -Tail 1
}
try{
 $dev=SqlBackup 'LandPortal_Dev' 'd'
 $test=SqlBackup 'LandPortal_Test' 't'
 Write-SigningJson ($report+'/sql-backups-verified.json') @{dev=$dev;test=$test}
 $spec=@()
 foreach($pair in @(@('Storage__MediaRoot','media'),@('Storage__SnapshotRoot','snapshots'),@('Storage__KeysRoot','keys'))){
  $src=[Environment]::GetEnvironmentVariable($pair[0],'Process');if(-not $src){throw 'Missing explicit Dev storage root'}
  $spec+=@{source=Resolve-SigningPath $src;key=$pair[1]}
 }
 $spec+=@{source=Join-Path $script:ProjectRoot '.env';key='project.env'}
 Write-SigningJson ($report+'/file-spec.json') $spec
 $filesManifest=Join-Path $dev.path 'file-manifest.json'
 FileTool 'copy-dev' @('capture','--spec',($report+'/file-spec.json'),'--dest',$dev.path,'--report',$filesManifest)
 FileTool 'verify-dev' @('verify','--manifest',$filesManifest,'--verify-sources','--report',($report+'/file-readback.json'))
 FileTool 'restore-files' @('restore-check','--manifest',$filesManifest,'--dest',(Join-Path $base 'r'),'--report',($report+'/file-restore-check.json'))
 $depSpec=@(foreach($entry in $dependencies){@{source=Resolve-SigningPath $entry.sourcePath;key=('dependency/'+$entry.sourcePath.Replace('\','/'))}})
 Write-SigningJson ($report+'/dependency-spec.json') $depSpec
 $depManifest=Join-Path $base 'dependency-manifest.json'
 FileTool 'copy-dependencies' @('capture','--spec',($report+'/dependency-spec.json'),'--dest',(Join-Path $base 'p'),'--report',$depManifest)
 FileTool 'verify-dependencies' @('verify','--manifest',$depManifest,'--verify-sources','--report',($report+'/dependency-readback.json'))
 $files=Get-Content -LiteralPath $filesManifest -Raw -Encoding UTF8|ConvertFrom-Json
 $depFiles=Get-Content -LiteralPath $depManifest -Raw -Encoding UTF8|ConvertFrom-Json
 if((Get-SigningSha (Join-Path $dev.path 'LandPortal_Dev.bak')) -cne $dev.databaseBackupSha256 -or (Get-SigningSha (Join-Path $test.path 'LandPortal_Test.bak')) -cne $test.databaseBackupSha256){throw 'SQL backup hash changed'}
 if((Get-Acl -LiteralPath $parent).Sddl -cne $beforeSddl){throw 'Backup parent ACL changed'}
 $restoreText=@'
# Recovery instructions - Development project backup only
SQL d/LandPortal_Dev.bak and t/LandPortal_Test.bak are COPY_ONLY/CHECKSUM,
verified by actual RESTORE VERIFYONLY and HEADERONLY. SQL restore drill NOT RUN.
Do not restore over Dev automatically. Before any SQL restore, obtain explicit
restore target authorization, inspect RESTORE FILELISTONLY, and use MOVE to new
isolated data/log files on an approved Test restore target. Never overwrite Test
or Dev data as an incidental verification step.
d/file-manifest.json records original absolute sources, unchanged relative names,
SHA256 and byte lengths for all media/snapshots/keys and private project.env.
r/ is a verified isolated file restoration rehearsal, not active storage.
Restore to new private roots using original relative names, verify all hashes,
then coordinate SQL/file recovery at this backup checkpoint before switching roots.
dependency-manifest.json maps every backed-up checkpoint/proof/raw resource and
signed approval to its original project path. SQL Test backup retains scan receipts.
Never execute the signed approval PS1. Backups contain private keys/env; keep
the inherited private ACL. No secrets may be printed or placed in Git.
'@
 $textPath=Join-Path $base 'RESTORE.md';$stream=[IO.File]::Open($textPath,[IO.FileMode]::CreateNew);try{$bytes=[Text.Encoding]::UTF8.GetBytes($restoreText);$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
 $dev.files=$files.fileCount+1;$dev.bytes=$files.bytes+$dev.sqlBackupBytes;$dev.fileManifest=$filesManifest;$dev.fileManifestSha256=Get-SigningSha $filesManifest;$dev.readbackMismatches=0;$dev.fileRestoreCheck='PASS';$dev.fullBackup='PASS'
 $test.files=1;$test.bytes=$test.sqlBackupBytes;$test.coverage='SQL recovery receipts/job; exact required byte dependencies in p/'
 Write-SigningJson ($report+'/backup-LandPortal_Dev.json') $dev
 Write-SigningJson ($report+'/backup-LandPortal_Test.json') $test
 Write-SigningJson ($report+'/backup-complete.json') @{status='PASS';path=$base;dev=$dev;test=$test;sourceFiles=$files.fileCount;sourceBytes=$files.bytes;dependencyFiles=$depFiles.fileCount;dependencyBytes=$depFiles.bytes;fileRestore='PASS';sqlRestore='NOT_RUN';missingFiles=0;hashMismatches=0;permissionsChanged=$false;parentSddlUnchanged=$true;utc=[DateTimeOffset]::UtcNow.ToString('O')}
 Write-Output ('BACKUP PASS; '+$files.fileCount+' Dev files + 2 SQL + '+$depFiles.fileCount+' dependencies; path='+$base)
}catch{Write-SigningJson ($report+'/backup-failure.json') @{status='STOP_NO_APPLY';errorType=$_.Exception.GetType().Name;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber};throw}
