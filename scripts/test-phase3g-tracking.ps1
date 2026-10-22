[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BackupReceipt,[string]$ReportRoot)
. (Join-Path $PSScriptRoot 'common.ps1')
if(-not $ReportRoot){$ReportRoot='.local/evidence/phase3f/phase3g-tracking-tests-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)}
$full=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $ReportRoot))
if(-not $full.StartsWith((Join-Path $script:ProjectRoot '.local/evidence/phase3f')+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $full)){throw 'Unique project Phase3F evidence folder required.'}
[IO.Directory]::CreateDirectory($full)|Out-Null
$build=Join-Path $script:ProjectRoot '.local/build/phase3g/stabilization-current'
$files=@('src/MigrationCli/SourceBatch.cs','src/Infrastructure/Configuration.cs','src/Infrastructure/MigrationDiagnostics.cs','src/Infrastructure/RuntimeRoleInterceptor.cs','tests/LandPortal.Tests/BatchTrackingTests.cs','tests/LandPortal.Tests/BatchImportTests.cs','tests/LandPortal.Tests/Program.cs','scripts/test-phase3g-tracking.ps1')
$code=@($files|ForEach-Object{[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()}})
$record=[ordered]@{startedUtc=[DateTimeOffset]::UtcNow.ToString('O');scope='Tracking metadata SQL Test only, existing negative media/Dev guard case; no full regression/HTTP/crawl/scanning/Dev Apply';backupReceipt=$BackupReceipt;code=$code;complete=$false}
$record|ConvertTo-Json -Depth 8|Set-Content -LiteralPath ($full+'/run.json') -Encoding UTF8
$mutex=New-Object Threading.Mutex -ArgumentList $false,'Local\LandPortal_SQL_Test_Run';$held=$false
try{
 try{$held=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true};if(-not $held){throw 'SQL Test busy.'}
 $b=Get-ProjectConnectionBuilder LandPortal_Test;$env:ConnectionStrings__Portal=$b.ConnectionString;$env:Sql__ExpectedDatabase='LandPortal_Test'
 foreach($pair in @(@('MediaRoot','media'),@('SnapshotRoot','snapshots'),@('TempRoot','temp'),@('KeysRoot','keys'))){$path=Join-Path $script:ProjectRoot ('.local/tests/phase2a/'+$pair[1]);if(-not(Test-Path -LiteralPath $path -PathType Container)){throw 'Existing Test storage required; no copying Dev assets.'};[Environment]::SetEnvironmentVariable(('Storage__'+$pair[0]),$path,'Process')}
 $env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false';$env:MigrationDiagnostics__Root=$full+'/trace'
 $env:LandPortal__EvidenceRoot=$full;$env:LandPortal__BuildRoot=$build
 $ErrorActionPreference='Continue'
 & rtk proxy $script:DotnetPath restore tests/LandPortal.Tests/LandPortal.Tests.csproj --locked-mode --artifacts-path $build 2>&1|Tee-Object -FilePath ($full+'/restore.log')
 if($LASTEXITCODE){throw 'Focused restore failed'}
 & rtk proxy $script:DotnetPath build tests/LandPortal.Tests/LandPortal.Tests.csproj --no-restore --artifacts-path $build 2>&1|Tee-Object -FilePath ($full+'/build.log')
 if($LASTEXITCODE){throw 'Focused build failed'}
 & rtk proxy $script:DotnetPath (Join-Path $build 'bin/LandPortal.Tests/debug/LandPortal.Tests.dll') --batch-tracking-only --test-backup-receipt $BackupReceipt 2>&1|Tee-Object -FilePath ($full+'/tests.log')
 $exit=$LASTEXITCODE
}catch{$exit=1;$_|Out-String|Add-Content -LiteralPath ($full+'/error.log')}
finally{if($held){$mutex.ReleaseMutex()};$mutex.Dispose()}
$record.codeDrift=@($code|Where-Object{(Get-FileHash -LiteralPath $_.path).Hash.ToLowerInvariant() -cne $_.sha256}|ForEach-Object{$_.path});if($record.codeDrift.Count){$exit=1}
$record.complete=$true;$record.exitCode=$exit;$record.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O');$record|ConvertTo-Json -Depth 8|Set-Content -LiteralPath ($full+'/run.json') -Encoding UTF8
Write-Output ('Tracking-focused evidence '+$ReportRoot+'; exit='+$exit);exit $exit
