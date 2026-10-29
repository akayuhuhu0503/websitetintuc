[CmdletBinding()]
param([ValidateSet('LandPortal_Dev','LandPortal_Test')][string]$Database='LandPortal_Dev', [switch]$GrantSqlBackupFolderAccess, [ValidateSet('phase2a','phase2b','v04','phase2c','phase2d','phase3b','phase3c')][string]$EvidencePhase='phase2a',[string]$EvidenceDirectory,[Guid]$InventorySession=[Guid]::Empty,[switch]$AllowAdditionalRestorePoint)
. (Join-Path $PSScriptRoot 'common.ps1')
$retention=Join-Path $script:ProjectRoot '.local/cleanup-retention.json'
if((Test-Path -LiteralPath $retention) -and -not $AllowAdditionalRestorePoint){throw 'A verified restore point is retained. Review capacity/retention and explicitly use -AllowAdditionalRestorePoint before creating another. No backup or automatic deletion performed.'}
$builder=Get-ProjectConnectionBuilder $Database
$conn=New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
$stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$backupRoot=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot ('.local/backups/'+$Database+'-'+$stamp)))
$allowed=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot '.local/backups'))+'\'
if(-not $backupRoot.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Backup path escaped the project backup directory.'}
[IO.Directory]::CreateDirectory($backupRoot)|Out-Null
# Backups contain keys and local .env; avoid broad inherited read access.
$operator=[Security.Principal.WindowsIdentity]::GetCurrent().User
& rtk proxy icacls $backupRoot /inheritance:r /grant:r ('*'+$operator.Value+':(OI)(CI)F') '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if($LASTEXITCODE -ne 0){throw 'Could not protect backup directory ACL.'}
function Execute([string]$sql,[string]$path='') { $cmd=$conn.CreateCommand();$cmd.CommandTimeout=120;$cmd.CommandText=$sql;if($path){[void]$cmd.Parameters.AddWithValue('@path',$path)};try{return $cmd.ExecuteNonQuery()}finally{$cmd.Dispose()} }
function CopyPrivate([string]$key,[string]$destination) {
    $src=[Environment]::GetEnvironmentVariable($key,'Process');if(-not $src){throw "Missing $key"};if(-not [IO.Path]::IsPathRooted($src)){$src=Join-Path $script:ProjectRoot $src};$src=[IO.Path]::GetFullPath($src)
    if(-not $src.StartsWith((Join-Path $script:ProjectRoot '.local')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Backup storage source must stay in the project .local directory.'}
    if(-not (Test-Path -LiteralPath $src -PathType Container)){throw "Missing project storage root $key"};Copy-Item -LiteralPath $src -Destination (Join-Path $backupRoot $destination) -Recurse
}
try {
    $conn.Open();$cmd=$conn.CreateCommand();$cmd.CommandText='SELECT DB_NAME(),CAST(SERVERPROPERTY(''MachineName'') AS nvarchar(200))';$reader=$cmd.ExecuteReader();try{if(-not $reader.Read() -or $reader.GetString(0) -cne $Database -or $reader.GetString(1) -ine $env:COMPUTERNAME){throw 'Backup requires verified local named project database.'}}finally{$reader.Dispose();$cmd.Dispose()}
    if($GrantSqlBackupFolderAccess){
        $cmd=$conn.CreateCommand();$cmd.CommandText='SELECT service_account FROM sys.dm_server_services WHERE servicename LIKE N''SQL Server (%''';$service=$cmd.ExecuteScalar();$cmd.Dispose();if(-not $service){throw 'Could not identify current SQL service account.'}
        & rtk proxy icacls $backupRoot /grant (([string]$service)+':(OI)(CI)M') | Out-Null
        if($LASTEXITCODE -ne 0){throw 'Could not grant SQL service access to project backup directory.'}
    }
    $databaseBackup=Join-Path $backupRoot ($Database+'.bak')
    Execute ('BACKUP DATABASE ['+$Database+'] TO DISK=@path WITH COPY_ONLY,CHECKSUM,INIT') $databaseBackup|Out-Null
    Execute 'RESTORE VERIFYONLY FROM DISK=@path WITH CHECKSUM' $databaseBackup|Out-Null
    CopyPrivate 'Storage__MediaRoot' 'media';CopyPrivate 'Storage__SnapshotRoot' 'snapshots';CopyPrivate 'Storage__KeysRoot' 'keys'
    if($InventorySession -ne [Guid]::Empty){
        $inventory=Join-Path $script:ProjectRoot ('.local/phase3a/'+$InventorySession.ToString('N'))
        if(-not(Test-Path -LiteralPath $inventory -PathType Container)){throw 'Missing named project inventory session.'}
        if(@(Get-ChildItem -LiteralPath $inventory -Recurse -Force|Where-Object{$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Inventory backup refuses reparse points.'}
        Copy-Item -LiteralPath $inventory -Destination (Join-Path $backupRoot 'inventory') -Recurse
    }
    Copy-Item -LiteralPath (Join-Path $script:ProjectRoot '.env') -Destination (Join-Path $backupRoot 'project.env')
    $ledger=@();foreach($file in Get-ChildItem -LiteralPath $backupRoot -File -Recurse){$ledger+= [ordered]@{relativePath=$file.FullName.Substring($backupRoot.Length+1);bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}}
    $record=[ordered]@{schemaVersion=1;database=$Database;utc=[DateTimeOffset]::UtcNow.ToString('O');nativeBackup='COPY_ONLY CHECKSUM';verifyOnly='PASS';restoreTested=$false;backupRoot=$backupRoot;files=$ledger}
    [IO.File]::WriteAllText((Join-Path $backupRoot 'manifest.json'),(ConvertTo-Json $record -Depth 12),[Text.UTF8Encoding]::new($false))
    $evidence=Join-Path $script:ProjectRoot ('.local/evidence/'+$EvidencePhase)
    if($EvidenceDirectory){$evidence=if([IO.Path]::IsPathRooted($EvidenceDirectory)){[IO.Path]::GetFullPath($EvidenceDirectory)}else{[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $EvidenceDirectory))};if(-not (@('phase2c','phase2d','phase3b','phase3c')|Where-Object{$evidence.StartsWith((Join-Path $script:ProjectRoot ('.local/evidence/'+$_))+'\',[StringComparison]::OrdinalIgnoreCase)})){throw 'Evidence escaped its run directory.'};if(Test-Path -LiteralPath (Join-Path $evidence ('backup-'+$Database+'.json'))){throw 'Refusing to overwrite a backup artifact.'}}
    [IO.Directory]::CreateDirectory($evidence)|Out-Null
    [IO.File]::WriteAllText((Join-Path $evidence ('backup-'+$Database+'.json')),(ConvertTo-Json ([ordered]@{database=$Database;utc=$record.utc;path=$backupRoot;verifyOnly='PASS';restoreTested=$false;files=$ledger.Count;bytes=($ledger|ForEach-Object {$_.bytes}|Measure-Object -Sum).Sum;databaseBackupSha256=(Get-FileHash -LiteralPath $databaseBackup -Algorithm SHA256).Hash.ToLowerInvariant()}) -Depth 12),[Text.UTF8Encoding]::new($false))
    Write-Output ('PASS '+$Database+' COPY_ONLY/CHECKSUM backup and actual RESTORE VERIFYONLY; '+$ledger.Count+' files protected by private ACL. Restore has NOT been performed.')
} catch { Write-Error ('Project backup failed: '+$_.Exception.GetType().Name+' at script line '+$_.InvocationInfo.ScriptLineNumber+'. No migration performed; secure local backup directory may be partial.');exit 1 } finally {$conn.Dispose()}
