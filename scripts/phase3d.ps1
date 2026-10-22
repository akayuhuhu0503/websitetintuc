[CmdletBinding()]
param(
 [ValidateSet('Audit','NukeVietPreview','Preview','Commit','Verify','SafeApply','SafeVerify','SafeRollback','NameDecisionTest')][string]$Action='Audit',
 [ValidateSet('LandPortal_Test','LandPortal_Dev')][string]$Database='LandPortal_Test',
 [Parameter(Mandatory=$true)][string]$ReportRoot,[string]$Checkpoint,[string]$CheckpointSha,
 [string]$Inventory,[string]$InventorySha,[string]$ProofSet,[string]$ProofSetSha,[string]$Plan,[string]$PlanSha,
 [string]$OptionalImages,[string]$OptionalImagesSha,[string]$Approval,[string]$BackupReceipt,[string]$ConfirmTest,
 [string]$Html,[string]$Url,[string]$Sha,[string]$Decision,[string]$DecisionSha,[switch]$NoBuild)
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'test-profile.ps1')
$lease=New-Object Threading.Mutex -ArgumentList $false,'Local\LandPortal_SQL_Test_Run';$held=$false
try{
 try{$held=$lease.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true};if(-not $held){throw 'SQL Test is in use.'}
 if($Database -eq 'LandPortal_Test'){Initialize-ProjectTestProfile}else{
  if($Action -notin @('Preview','SafeApply','SafeVerify','SafeRollback','Audit','NukeVietPreview')){throw 'Dev ordinary writes forbidden.'}
  $builder=Get-ProjectConnectionBuilder 'LandPortal_Dev';$env:ConnectionStrings__Portal=$builder.ConnectionString;$env:Sql__ExpectedDatabase='LandPortal_Dev'
 }
 $env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false'
 $build=Join-Path $script:ProjectRoot '.local/build/phase3d/current'
 if(-not $NoBuild){Invoke-ProjectDotnet @('restore','src/MigrationCli/MigrationCli.csproj','--locked-mode','--artifacts-path',$build);Invoke-ProjectDotnet @('build','src/MigrationCli/MigrationCli.csproj','--no-restore','--artifacts-path',$build)}
 $command=@{Audit='source-audit';NukeVietPreview='nukeviet-preview';Preview='batch-preview';Commit='batch-commit';Verify='batch-verify';SafeApply='safe-apply';SafeVerify='safe-verify';SafeRollback='safe-rollback';NameDecisionTest='name-decision-test'}[$Action]
 $cli=@((Join-Path $build 'bin/MigrationCli/debug/MigrationCli.dll'),$command,'--report',$ReportRoot)
 foreach($pair in @(@('--checkpoint',$Checkpoint),@('--checkpoint-sha',$CheckpointSha),@('--inventory',$Inventory),@('--inventory-sha',$InventorySha),@('--proof-set',$ProofSet),@('--proof-set-sha',$ProofSetSha),@('--plan',$Plan),@('--plan-sha',$PlanSha),@('--optional-images',$OptionalImages),@('--optional-images-sha',$OptionalImagesSha),@('--approval',$Approval),@('--backup-receipt',$BackupReceipt),@('--confirm-test',$ConfirmTest),@('--html',$Html),@('--url',$Url),@('--sha',$Sha),@('--decision',$Decision),@('--decision-sha',$DecisionSha))){if($pair[1]){$cli+=$pair}}
 Invoke-ProjectDotnet $cli
}finally{if($held){$lease.ReleaseMutex()};$lease.Dispose()}
