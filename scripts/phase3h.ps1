[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidateSet('Inventory','Scan','Preview','Preflight','Reconcile','TestVerify')][string]$Action,
 [ValidateSet('LandPortal_Test','LandPortal_Dev')][string]$Database='LandPortal_Test',
 [Parameter(Mandatory=$true)][string]$Cli,[Parameter(Mandatory=$true)][string]$CliSha,
 [Parameter(Mandatory=$true)][string]$ReportRoot,
 [string]$Checkpoint,[string]$CheckpointSha,[string]$ParentCheckpoint,[string]$ParentCheckpointSha,
 [string]$SourceProof,[string]$SourceProofSha,[string]$Inventory,[string]$InventorySha,
 [string]$ProofSet,[string]$ProofSetSha,[string]$Plan,[string]$PlanSha,[int]$StopAfter=0
)
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'test-profile.ps1')
if($Database -ceq 'LandPortal_Dev' -and $Action -notin @('Preview','Preflight','Reconcile')){throw 'Expanded Dev writes/scans/verification of uncommitted batches are not authorized.'}
$tool=if([IO.Path]::IsPathRooted($Cli)){[IO.Path]::GetFullPath($Cli)}else{[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $Cli))}
$allowedBuild=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot '.local/build')).TrimEnd('\')+'\'
if(-not $tool.StartsWith($allowedBuild,[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $tool -Algorithm SHA256).Hash.ToLowerInvariant() -cne $CliSha){throw 'Exact reviewed local build required'}
$lease=New-Object Threading.Mutex -ArgumentList $false,'Local\LandPortal_SQL_Test_Run';$held=$false
try{
 try{$held=$lease.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true};if(-not $held){throw 'SQL Test is in use'}
 if($Database -ceq 'LandPortal_Test'){Initialize-ProjectTestProfile}else{
  $env:ConnectionStrings__Portal=(Get-ProjectConnectionBuilder 'LandPortal_Dev').ConnectionString;$env:Sql__ExpectedDatabase='LandPortal_Dev'
  foreach($pair in @(@('MediaRoot','media'),@('SnapshotRoot','snapshots'),@('TempRoot','temp'),@('KeysRoot','keys'))){[Environment]::SetEnvironmentVariable(('Storage__'+$pair[0]),(Join-Path $script:ProjectRoot ('.local/'+$pair[1])),'Process')}
 }
 $env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false'
 $command=@{Inventory='recovery-public-inventory';Scan='recovery-scan';Preview='batch-preview';Preflight='batch-preflight';Reconcile='batch-reconcile';TestVerify='batch-verify'}[$Action]
 $argv=@($tool,$command,'--report',$ReportRoot,'--stop-after',[string]$StopAfter)
 foreach($pair in @(@('--checkpoint',$Checkpoint),@('--checkpoint-sha',$CheckpointSha),@('--parent-checkpoint',$ParentCheckpoint),@('--parent-checkpoint-sha',$ParentCheckpointSha),@('--source-proof',$SourceProof),@('--source-proof-sha',$SourceProofSha),@('--inventory',$Inventory),@('--inventory-sha',$InventorySha),@('--proof-set',$ProofSet),@('--proof-set-sha',$ProofSetSha),@('--plan',$Plan),@('--plan-sha',$PlanSha))){if($pair[1]){$argv+=$pair}}
 Invoke-ProjectDotnet $argv
}finally{if($held){$lease.ReleaseMutex()};$lease.Dispose()}
