[CmdletBinding()]
param([ValidateSet('Preflight','Reconcile','Commit','Verify')][string]$Action='Preflight',
 [Parameter(Mandatory=$true)][string]$ReportRoot,[Parameter(Mandatory=$true)][string]$Plan,[Parameter(Mandatory=$true)][string]$PlanSha,
 [Parameter(Mandatory=$true)][string]$Checkpoint,[Parameter(Mandatory=$true)][string]$CheckpointSha,
 [string]$Inventory,[string]$InventorySha,[string]$ProofSet,[string]$ProofSetSha,[switch]$NoBuild)
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'test-profile.ps1')
$root=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $ReportRoot));$prefix=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot '.local/evidence/phase3e'))+'\'
if(-not $root.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path $root)){throw 'New private Phase3E report required.'}
$mutex=New-Object Threading.Mutex -ArgumentList $false,'Local\LandPortal_SQL_Test_Run';$held=$false
try{
 try{$held=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true};if(-not $held){throw 'SQL Test is in use.'}
 Initialize-ProjectTestProfile;$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false'
 $env:MigrationDiagnostics__Root=$root+'-trace'
 $build=Join-Path $script:ProjectRoot '.local/build/phase3e/current'
 if(-not $NoBuild){Invoke-ProjectDotnet @('restore','src/MigrationCli/MigrationCli.csproj','--locked-mode','--artifacts-path',$build);Invoke-ProjectDotnet @('build','src/MigrationCli/MigrationCli.csproj','--no-restore','--artifacts-path',$build)}
 $cli=@((Join-Path $build 'bin/MigrationCli/debug/MigrationCli.dll'),('batch-'+$Action.ToLowerInvariant()),'--report',$root,'--plan',$Plan,'--plan-sha',$PlanSha,'--checkpoint',$Checkpoint,'--checkpoint-sha',$CheckpointSha)
 foreach($pair in @(@('--inventory',$Inventory),@('--inventory-sha',$InventorySha),@('--proof-set',$ProofSet),@('--proof-set-sha',$ProofSetSha))){if($pair[1]){$cli+=$pair}}
 Invoke-ProjectDotnet $cli
}finally{if($held){$mutex.ReleaseMutex()};$mutex.Dispose()}
