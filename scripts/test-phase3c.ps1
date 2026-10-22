[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'test-profile.ps1')
$id='phase3c-tests-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$root=Join-Path $script:ProjectRoot ('.local/evidence/phase3c/'+$id);[IO.Directory]::CreateDirectory($root)|Out-Null
$mutex=New-Object Threading.Mutex -ArgumentList $false,'Local\LandPortal_SQL_Test_Run';$held=$false
$files=@('src/Infrastructure/DefenderInquiryScanner.cs','src/MigrationCli/MediaRecovery.cs','src/MigrationCli/RecoveryCommand.cs','src/MigrationCli/RecoveryEvidence.cs','src/MigrationCli/SourceBatch.cs','src/MigrationCli/SqlImporter.Batch.cs','src/MigrationCli/ApprovedBatchExecution.cs','src/MigrationCli/SafeBatchCommand.cs','src/MigrationCli/BatchCommand.cs','src/MigrationCli/BatchModel.cs','src/MigrationCli/Program.cs','tests/LandPortal.Tests/MediaRecoveryTests.cs','tests/LandPortal.Tests/BatchImportTests.cs','tests/LandPortal.Tests/Program.cs','tests/LandPortal.Tests/TestEvidencePaths.cs','scripts/phase3c.ps1','scripts/test-phase3c.ps1')
$code=@($files|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()}})
$record=[ordered]@{runId=$id;startedUtc=[DateTimeOffset]::UtcNow.ToString('O');code=$code;complete=$false;scope='Focused Phase3C only; no full regression; no repository-wide artifact rehash'}
$record|ConvertTo-Json -Depth 6|Set-Content -LiteralPath ($root+'/run.json') -Encoding UTF8
try{
 try{$held=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true};if(-not $held){throw 'Test SQL busy.'}
 Initialize-ProjectTestProfile;$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false';$env:LandPortal__EvidenceRoot=$root;$env:LandPortal__BuildRoot=Join-Path $script:ProjectRoot '.local/build/phase3c/current'
 $prior=$ErrorActionPreference;$ErrorActionPreference='Continue'
 & rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -Command "& { . ./scripts/common.ps1; Invoke-ProjectDotnet @('restore','tests/LandPortal.Tests/LandPortal.Tests.csproj','--locked-mode','--artifacts-path','$env:LandPortal__BuildRoot'); Invoke-ProjectDotnet @('build','tests/LandPortal.Tests/LandPortal.Tests.csproj','--no-restore','--artifacts-path','$env:LandPortal__BuildRoot'); Invoke-ProjectDotnet @((Join-Path '$env:LandPortal__BuildRoot' 'bin/LandPortal.Tests/debug/LandPortal.Tests.dll'),'--phase3c-only') }" 2>&1|Tee-Object -FilePath ($root+'/run.log')
 $exit=$LASTEXITCODE;$ErrorActionPreference=$prior
}catch{$exit=1;$_|Out-String|Add-Content -LiteralPath ($root+'/run.log')}
finally{if($held){$mutex.ReleaseMutex()};$mutex.Dispose()}
$record.codeDrift=@($code|Where-Object{(Get-FileHash -LiteralPath $_.path).Hash.ToLowerInvariant() -cne $_.sha256}|ForEach-Object{$_.path});if($record.codeDrift.Count){$exit=1}
$record.complete=$true;$record.exitCode=$exit;$record.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O');$record|ConvertTo-Json -Depth 6|Set-Content -LiteralPath ($root+'/run.json') -Encoding UTF8
Write-Output ('Phase3C evidence '+$root+'; exit='+$exit);exit $exit
