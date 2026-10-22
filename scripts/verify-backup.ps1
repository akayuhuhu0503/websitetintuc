[CmdletBinding()]
param([ValidateSet('LandPortal_Dev','LandPortal_Test')][string]$Database='LandPortal_Dev', [ValidateSet('phase2a','phase2b','v04')][string]$EvidencePhase='phase2a')
. (Join-Path $PSScriptRoot 'common.ps1')
$summaryPath=Join-Path $script:ProjectRoot ('.local/evidence/'+$EvidencePhase+'/backup-'+$Database+'.json')
$summary=Get-Content -LiteralPath $summaryPath -Encoding UTF8 -Raw|ConvertFrom-Json
$backupRoot=[IO.Path]::GetFullPath($summary.path)
if(-not $backupRoot.StartsWith((Join-Path $script:ProjectRoot '.local/backups')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Backup verification target escaped project backups.'}
$manifest=Get-Content -LiteralPath (Join-Path $backupRoot 'manifest.json') -Encoding UTF8 -Raw|ConvertFrom-Json
if($manifest.database -cne $Database){throw 'Backup manifest database differs from explicit named target.'}
$checks=@();foreach($file in $manifest.files){$path=[IO.Path]::GetFullPath((Join-Path $backupRoot $file.relativePath));if(-not $path.StartsWith($backupRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe backup manifest key.'};$exists=Test-Path -LiteralPath $path -PathType Leaf;$matches=$exists -and (Get-Item -LiteralPath $path).Length -eq $file.bytes -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $file.sha256;$checks+=[ordered]@{file=$file.relativePath;matches=$matches}}
$failed=@($checks|Where-Object {-not $_.matches})
$envUnchanged=(Get-FileHash -LiteralPath (Join-Path $script:ProjectRoot '.env') -Algorithm SHA256).Hash -ceq (Get-FileHash -LiteralPath (Join-Path $backupRoot 'project.env') -Algorithm SHA256).Hash
$record=[ordered]@{schemaVersion=1;database=$Database;utc=[DateTimeOffset]::UtcNow.ToString('O');nativeBackupWasVerified=$manifest.verifyOnly;restoreTested=$manifest.restoreTested;backupFiles=$checks.Count;backupHashFailures=$failed.Count;liveEnvByteHashUnchanged=$envUnchanged;checks=$checks;status=if($failed.Count -eq 0 -and $envUnchanged){'PASS'}else{'FAIL'}}
[IO.File]::WriteAllText((Join-Path $script:ProjectRoot ('.local/evidence/'+$EvidencePhase+'/backup-verify-'+$Database+'.json')),(ConvertTo-Json $record -Depth 10),[Text.UTF8Encoding]::new($false))
Write-Output ($record.status+' backup readback: '+$checks.Count+' file hashes checked, failures='+$failed.Count+'; project .env unchanged='+$envUnchanged+'; RESTORE tested='+$manifest.restoreTested)
if($record.status -ne 'PASS'){exit 1}
