[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$BackupReceipt,
 [Parameter(Mandatory=$true)][ValidatePattern('^[a-f0-9]{64}$')][string]$BackupReceiptSha256,
 [Parameter(Mandatory=$true)][ValidatePattern('^LandPortal_Restore3J_[a-f0-9]{8}$')][string]$RestoreDatabase,
 [Parameter(Mandatory=$true)][string]$ReportDirectory
)
# Explicitly authorized isolated recovery drill only. Never overwrites a database,
# restores active Dev/Test, changes common database guards, or launches a worker.
. (Join-Path $PSScriptRoot 'dev-signing-common.ps1')
$ProgressPreference='SilentlyContinue'
function PrivatePath([string]$path,[string]$scope){
 $full=Resolve-SigningPath $path $scope
 $walk=$full
 while($walk){
  if((Test-Path -LiteralPath $walk) -and ((Get-Item -LiteralPath $walk -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse path refused'}
  $walk=Split-Path -Parent $walk
 }
 return $full
}
$receiptPath=PrivatePath $BackupReceipt '.local/evidence'
if((Get-SigningSha $receiptPath) -cne $BackupReceiptSha256){throw 'Backup receipt SHA mismatch'}
$receipt=Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8|ConvertFrom-Json
if($receipt.status -cne 'PASS' -or $receipt.missingFiles -ne 0 -or $receipt.hashMismatches -ne 0 -or $receipt.dev.database -cne 'LandPortal_Dev'){throw 'Complete Dev backup receipt required'}
$bak=PrivatePath (Join-Path $receipt.dev.path 'LandPortal_Dev.bak') '.local/backups'
$manifest=PrivatePath $receipt.dev.fileManifest '.local/backups'
if((Get-SigningSha $bak) -cne $receipt.dev.databaseBackupSha256 -or (Get-SigningSha $manifest) -cne $receipt.dev.fileManifestSha256){throw 'Backup bytes differ from receipt'}
$report=PrivatePath $ReportDirectory '.local/evidence/phase3j'
if(Test-Path -LiteralPath $report){throw 'New report directory required'}
$data=PrivatePath (Join-Path (Split-Path -Parent $receipt.path) ('j'+$RestoreDatabase.Substring($RestoreDatabase.Length-8))) '.local/backups'
if(Test-Path -LiteralPath $data){throw 'New isolated restore directory required'}
$free=(Get-PSDrive -Name ([IO.Path]::GetPathRoot($data).Substring(0,1))).Free
if($free -lt 4GB){throw 'Less than 4 GiB free; restore blocked'}
[void][IO.Directory]::CreateDirectory($report)
function FileCheck([string]$label,[string[]]$argsList){
 $saved=$ErrorActionPreference
 try{$ErrorActionPreference='Continue';& rtk proxy python (Join-Path $PSScriptRoot 'backup-file-snapshot.py') @argsList *> (Join-Path $report ($label+'.log'));$code=$LASTEXITCODE}finally{$ErrorActionPreference=$saved}
 if($code -ne 0){throw ('File verification failed: '+$label)}
}
function Rows($connection,[string]$sql,[int]$seconds=30){
 $cmd=$connection.CreateCommand();$cmd.CommandText=$sql;$cmd.CommandTimeout=$seconds
 $rd=$cmd.ExecuteReader();$rows=New-Object 'System.Collections.Generic.List[object]'
 try{while($rd.Read()){$row=[ordered]@{};for($i=0;$i -lt $rd.FieldCount;$i++){$v=$rd.GetValue($i);if($v -is [DBNull]){$v=$null};$row[$rd.GetName($i)]=$v};$rows.Add([pscustomobject]$row)}}finally{$rd.Dispose();$cmd.Dispose()}
 return ,$rows.ToArray()
}
function ExportJson($connection,[string]$name,[string]$sql){
 $cmd=$connection.CreateCommand();$cmd.CommandText=$sql+' FOR JSON PATH,INCLUDE_NULL_VALUES'
 $rd=$cmd.ExecuteReader();$buffer=New-Object Text.StringBuilder
 try{while($rd.Read()){[void]$buffer.Append($rd.GetString(0))}}finally{$rd.Dispose();$cmd.Dispose()}
 $path=Join-Path $report ($name+'.json');$stream=[IO.File]::Open($path,[IO.FileMode]::CreateNew)
 try{$bytes=[Text.Encoding]::UTF8.GetBytes($buffer.ToString());$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
}
FileCheck 'verify-backup-files' @('verify','--manifest',$manifest,'--report',(Join-Path $report 'backup-file-integrity.json'))
$builder=Get-ProjectConnectionBuilder LandPortal_Dev
$builder['Initial Catalog']='master'
$conn=New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
$stage='PRE_RESTORE';$created=$false
try{
 $conn.Open()
 $identity=(Rows $conn "SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(200)) serverName,CAST(SERVERPROPERTY('MachineName') AS nvarchar(200)) machineName,CAST(SERVERPROPERTY('InstanceName') AS nvarchar(200)) instanceName,@@SERVERNAME registeredName")[0]
 if($identity.serverName -ine 'MinhAnh\SQLEXPRESS' -or $identity.machineName -ine $env:COMPUTERNAME -or $identity.instanceName -ine 'SQLEXPRESS'){throw 'Unexpected restore instance'}
 if((Rows $conn "SELECT name FROM sys.databases WHERE name=N'$RestoreDatabase'").Count){throw 'Restore DB already exists; no overwrite permitted'}
 $escaped=$bak.Replace("'","''")
 $header=(Rows $conn "RESTORE HEADERONLY FROM DISK=N'$escaped'")[0]
 if($header.DatabaseName -cne 'LandPortal_Dev' -or -not $header.HasBackupChecksums -or -not $header.IsCopyOnly){throw 'Unexpected SQL backup header'}
 $files=Rows $conn "RESTORE FILELISTONLY FROM DISK=N'$escaped'"
 if($files.Count -ne 2 -or @($files|Where-Object {$_.Type -notin @('D','L')}).Count){throw 'Only one data and one log file are supported by this drill'}
 if(@($files|Where-Object Type -EQ 'D').Count -ne 1 -or @($files|Where-Object Type -EQ 'L').Count -ne 1){throw 'Unexpected SQL file layout'}
 $required=($files|Measure-Object Size -Sum).Sum+$receipt.sourceBytes+2GB
 if($required -gt $free){throw 'Insufficient restore space'}
 [void][IO.Directory]::CreateDirectory($data)
 $moves=@();$targets=@()
 foreach($file in $files){
  $ext=if($file.Type -ceq 'D'){'.mdf'}else{'.ldf'}
  $target=Join-Path $data ($RestoreDatabase+$ext)
  if(Test-Path -LiteralPath $target){throw 'Data file collision'}
  $targets+=@{logical=$file.LogicalName;type=$file.Type;path=$target;bytes=$file.Size}
  $moves+="MOVE N'$($file.LogicalName.Replace("'","''"))' TO N'$($target.Replace("'","''"))'"
 }
 Write-SigningJson (Join-Path $report 'restore-plan.json') @{database=$RestoreDatabase;server=$identity;backup=$bak;backupSha=$receipt.dev.databaseBackupSha256;receipt=$receiptPath;receiptSha=$BackupReceiptSha256;backupStart=$header.BackupStartDate.ToString('O');backupFinish=$header.BackupFinishDate.ToString('O');targets=$targets;freeBytes=$free;requiredBytes=$required;overwrite=$false;workersStarted=0;permissionsChanged=$false}
 $stage='RESTORE_DATABASE'
 # A dedicated 120s restore-operation timeout; application/query timeouts unchanged.
 [void](Rows $conn ("RESTORE DATABASE [$RestoreDatabase] FROM DISK=N'$escaped' WITH CHECKSUM, RECOVERY, "+($moves -join ', ')) 120)
 $created=$true;$stage='DBCC_CHECKDB'
 $checks=Rows $conn "DBCC CHECKDB (N'$RestoreDatabase') WITH NO_INFOMSGS, ALL_ERRORMSGS, TABLERESULTS" 120
 Write-SigningJson (Join-Path $report 'checkdb.json') @{database=$RestoreDatabase;errors=$checks;status=if($checks.Count){'FAIL'}else{'PASS'}}
 if($checks.Count){throw 'DBCC CHECKDB returned diagnostic records; manual review required'}
 # A read-only recovery copy stays offline from all application/worker profiles.
 [void](Rows $conn "ALTER DATABASE [$RestoreDatabase] SET READ_ONLY")
 $conn.ChangeDatabase($RestoreDatabase)
 if((Rows $conn 'SELECT DB_NAME() databaseName')[0].databaseName -cne $RestoreDatabase){throw 'Unexpected readback DB'}
 ExportJson $conn 'state' 'SELECT DB_NAME() databaseName,state_desc,is_read_only FROM sys.databases WHERE database_id=DB_ID()'
 foreach($pair in @(@('contents','SELECT c.* FROM Contents c ORDER BY c.Id'),@('revisions','SELECT r.* FROM Revisions r ORDER BY r.Id'),@('source-records','SELECT s.* FROM SourceRecords s ORDER BY s.Id'),@('media','SELECT m.* FROM MediaAssets m ORDER BY m.Id'),@('links','SELECT ca.* FROM ContentAssets ca ORDER BY ca.RevisionId,ca.MediaAssetId'),@('aliases','SELECT a.* FROM SourceUrlAliases a ORDER BY a.Id'),@('categories','SELECT * FROM Categories ORDER BY Id'),@('snapshots','SELECT * FROM SourceSnapshots ORDER BY Id'),@('schema','SELECT TABLE_NAME,COLUMN_NAME,DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS ORDER BY TABLE_NAME,ORDINAL_POSITION'),@('migrations','SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId'))){ExportJson $conn $pair[0] $pair[1]}
 $stage='RESTORE_FILES'
 FileCheck 'restore-files' @('restore-check','--manifest',$manifest,'--dest',(Join-Path $data 'files'),'--report',(Join-Path $report 'file-restore.json'))
 if((Get-SigningSha $bak) -cne $receipt.dev.databaseBackupSha256){throw 'Backup changed during drill'}
 Write-SigningJson (Join-Path $report 'actual-restore.json') @{status='RESTORED_PENDING_INDEPENDENT_READBACK';database=$RestoreDatabase;readOnly=$true;fileRoot=(Join-Path $data 'files');sqlBackupSha=$receipt.dev.databaseBackupSha256;fileManifest=$manifest;checkdb='PASS';backupReceipt=$receiptPath;utc=[DateTimeOffset]::UtcNow.ToString('O');activeDatabasesModified=$false;workersStarted=0;sqlRestoreActuallyExecuted=$true}
 Write-Output ('Actual SQL/file restore completed: '+$RestoreDatabase+'; independent readback required')
}catch{
 Write-SigningJson (Join-Path $report 'restore-stop.json') @{status='STOP';stage=$stage;database=$RestoreDatabase;restoreReturned=$created;error=$_.Exception.Message;automaticRetry=$false;activeDatabasesModified=$false}
 throw
}finally{$conn.Dispose()}
