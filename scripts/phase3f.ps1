[CmdletBinding()]
param([ValidateSet('Plan','Collect','Scan','NukeVietBundle','Preview','Preflight','Commit','Verify')][string]$Action='Plan',[string]$ReportRoot,[string]$Checkpoint,[string]$CheckpointSha,[string]$Batch,[string]$BatchSha,[string]$ApprovalFile,[string]$Bundle,[string]$BundleSha,[string]$AllowedPublicHashes,[string]$Inventory,[string]$InventorySha,[string]$ProofSet,[string]$ProofSetSha,[string]$Plan,[string]$PlanSha,[string]$OptionalImages,[string]$OptionalImagesSha,[ValidateSet('LandPortal_Test','LandPortal_Dev')][string]$Database='LandPortal_Test',[switch]$NoBuild)
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'test-profile.ps1')
if(-not $ReportRoot){$ReportRoot='.local/evidence/phase3f/phase3f-operation-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)}
$build=Join-Path $script:ProjectRoot '.local/build/phase3f/current'
if($Action -eq 'Collect' -and (-not $Batch -or -not $BatchSha -or -not $ApprovalFile)){throw 'Exact detail work order/SHA and recorded user collection authority required. A work order alone is no authority.'}
$mutex=New-Object Threading.Mutex -ArgumentList $false,'Local\LandPortal_SQL_Test_Run';$held=$false
try{
 try{$held=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true};if(-not $held){throw 'SQL Test busy.'}
 if($Database -eq 'LandPortal_Test'){Initialize-ProjectTestProfile}else{
  if($Action -ne 'Preview'){throw 'Only readonly Dev preview; no Apply/grants here.'}
  $builder=Get-ProjectConnectionBuilder 'LandPortal_Dev';$env:ConnectionStrings__Portal=$builder.ConnectionString;$env:Sql__ExpectedDatabase='LandPortal_Dev'
  $env:MigrationDiagnostics__Root=$null
 }
 $env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false'
 if(-not $NoBuild){Invoke-ProjectDotnet @('restore','src/MigrationCli/MigrationCli.csproj','--locked-mode','--artifacts-path',$build);Invoke-ProjectDotnet @('build','src/MigrationCli/MigrationCli.csproj','--no-restore','--artifacts-path',$build)}
 $dll=Join-Path $build 'bin/MigrationCli/debug/MigrationCli.dll'
 if($Action -eq 'Collect'){
  $b=Get-Content -LiteralPath $Batch -Raw -Encoding UTF8|ConvertFrom-Json
  # Inventory reports keep the existing Phase3A path guard. This references a new run, not an old artifact.
  $report='.local/evidence/phase3a/phase3f-collect-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
  Invoke-ProjectDotnet @($dll,'inventory-resume','--run',$b.InventoryId,'--detail-batch',$Batch,'--detail-batch-sha',$BatchSha,'--approval',$ApprovalFile,'--requests','120','--pages','25','--contents','20','--assets','100','--seconds','600','--mib','256','--report',$report)
 }elseif($Action -eq 'Scan'){
  if(-not $AllowedPublicHashes -or @($AllowedPublicHashes.Split(',')|Where-Object{$_ -notmatch '^[a-f0-9]{64}$'}).Count){throw 'Exact hashes of collected public bytes required; no arbitrary private scans.'}
  $env:Scanner__Provider='DefenderTest';$env:Scanner__DefenderAnonymousTestOnly='true';$env:Scanner__DefenderAllowedTestHashes=$AllowedPublicHashes
  $b=Get-Content -LiteralPath $Batch -Raw -Encoding UTF8|ConvertFrom-Json
  $report='.local/evidence/phase3a/phase3f-scan-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
  Invoke-ProjectDotnet @($dll,'inventory-scan','--run',$b.InventoryId,'--report',$report)
 }elseif($Action -in @('Preview','Preflight','Commit','Verify')){
  if($Database -eq 'LandPortal_Test'){$env:MigrationDiagnostics__Root=$ReportRoot+'-trace'}
  $cli=@($dll,('batch-'+$Action.ToLowerInvariant()),'--report',$ReportRoot,'--checkpoint',$Checkpoint,'--checkpoint-sha',$CheckpointSha)
  foreach($pair in @(@('--plan',$Plan),@('--plan-sha',$PlanSha),@('--inventory',$Inventory),@('--inventory-sha',$InventorySha),@('--proof-set',$ProofSet),@('--proof-set-sha',$ProofSetSha),@('--optional-images',$OptionalImages),@('--optional-images-sha',$OptionalImagesSha))){if($pair[1]){$cli+=$pair}}
  Invoke-ProjectDotnet $cli
 }else{
  $command=if($Action -eq 'Plan'){'source-detail-plan'}else{'nukeviet-bundle'}
  $cli=@($dll,$command,'--report',$ReportRoot,'--checkpoint',$Checkpoint,'--checkpoint-sha',$CheckpointSha)
  if($Bundle){$cli+=@('--bundle',$Bundle,'--bundle-sha',$BundleSha)}
  Invoke-ProjectDotnet $cli
 }
}finally{if($held){$mutex.ReleaseMutex()};$mutex.Dispose()}
