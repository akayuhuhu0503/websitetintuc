[CmdletBinding()]
param([ValidateSet('Preview','Commit','Verify','Rollback')][string]$Action='Preview',[ValidateSet('LandPortal_Test','LandPortal_Dev')][string]$Database='LandPortal_Test',[Parameter(Mandatory=$true)][string]$Checkpoint,[Parameter(Mandatory=$true)][string]$CheckpointSha,[string]$Plan,[string]$PlanSha,[string]$ReportRoot,[string]$BuildRoot,[string]$OptionalImages,[string]$OptionalImagesSha,[int]$StopAfter=0,[switch]$NoBuild)
. (Join-Path $PSScriptRoot 'common.ps1')
if($Database -eq 'LandPortal_Dev' -and $Action -ne 'Preview'){throw 'Apply Dev requires a separately approved implementation; this entrypoint refuses all Dev writes.'}
$lease=New-Object Threading.Mutex -ArgumentList $false,'Local\LandPortal_SQL_Test_Run';$held=$false
try{
 if($Database -eq 'LandPortal_Test'){try{$held=$lease.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true};if(-not $held){throw 'SQL Test is in use.'};. (Join-Path $PSScriptRoot 'test-profile.ps1');Initialize-ProjectTestProfile}
 $env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false'
 $build=Join-Path $script:ProjectRoot '.local/build/phase3b/current'
 if($BuildRoot){$build=[IO.Path]::GetFullPath($BuildRoot);if(-not $build.StartsWith((Join-Path $script:ProjectRoot '.local/build/phase3b')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Phase3B isolated build required.'}}
 if(-not $NoBuild){Invoke-ProjectDotnet @('restore','LandPortal.sln','--locked-mode','--artifacts-path',$build);Invoke-ProjectDotnet @('build','LandPortal.sln','--no-restore','--artifacts-path',$build)}
 $dll=Join-Path $build 'bin/MigrationCli/debug/MigrationCli.dll';if(-not(Test-Path $dll)){throw 'Phase3B binary missing.'}
 $cli=@($dll,('batch-'+$Action.ToLowerInvariant()),'--checkpoint',$Checkpoint,'--checkpoint-sha',$CheckpointSha,'--stop-after',"$StopAfter")
 foreach($pair in @(@('--plan',$Plan),@('--plan-sha',$PlanSha),@('--report',$ReportRoot),@('--optional-images',$OptionalImages),@('--optional-images-sha',$OptionalImagesSha))){if($pair[1]){$cli+=$pair}}
 Invoke-ProjectDotnet $cli
}finally{if($held){$lease.ReleaseMutex()};$lease.Dispose()}
