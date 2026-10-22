[CmdletBinding()]
param([ValidateSet('Start','Resume','Report','Scan')][string]$Action='Report',[Guid]$RunId=[Guid]::Empty,[switch]$Offline,[switch]$SeedLegacy,[switch]$ReparseFailed,[switch]$ReparseAll,[switch]$NoBuild,[int]$Requests=20,[int]$Pages=16,[int]$Contents=8,[int]$Assets=8,[int]$Seconds=300,[int]$MiB=128,[string]$RefreshUrl,[string]$ApprovalFile,[string]$ReportRoot,[ValidateSet('Clamd','DefenderTest')][string]$Scanner='Clamd',[string]$ApprovedAnonymousHashes)
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'test-profile.ps1')
$lease=New-Object Threading.Mutex -ArgumentList $false,'Local\LandPortal_SQL_Test_Run';$held=$false
try{
 try{$held=$lease.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true}
 if(-not $held){throw 'SQL Test is in use; do not run collector alongside a regression or another manual Test operation.'}
 Initialize-ProjectTestProfile
 $env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false'
 $env:Scanner__Provider=$Scanner
 if($Scanner -eq 'DefenderTest'){
  if(-not $ApprovedAnonymousHashes -or @($ApprovedAnonymousHashes.Split(',')|Where-Object{$_ -notmatch '^[a-fA-F0-9]{64}$'}).Count){throw 'Defender Test needs an explicit reviewed public/anonymous SHA allowlist; no original asset scan or private citizen files.'}
  $env:Scanner__DefenderAnonymousTestOnly='true';$env:Scanner__DefenderAllowedTestHashes=$ApprovedAnonymousHashes
 }
 $build=Join-Path $script:ProjectRoot '.local/build/phase3a/current'
 if(-not $NoBuild){Invoke-ProjectDotnet @('restore','LandPortal.sln','--locked-mode','--artifacts-path',$build);Invoke-ProjectDotnet @('build','LandPortal.sln','--no-restore','--artifacts-path',$build)}
 $dll=Join-Path $build 'bin/MigrationCli/debug/MigrationCli.dll'
 if(-not(Test-Path -LiteralPath $dll)){throw 'Build Phase3A first; no old-binary fallback.'}
 $argsForCli=@($dll,('inventory-'+$Action.ToLowerInvariant()),'--requests',"$Requests",'--pages',"$Pages",'--contents',"$Contents",'--assets',"$Assets",'--seconds',"$Seconds",'--mib',"$MiB")
 if($Action -ne 'Start'){if($RunId -eq [Guid]::Empty){throw 'Explicit RunId required.'};$argsForCli+=@('--run',$RunId.ToString())}
 if($Offline){$argsForCli+='--offline'}
 if($ReparseFailed){$argsForCli+='--reparse-failed'}
 if($ReparseAll){$argsForCli+='--reparse-all'}
 if($SeedLegacy){$argsForCli+=@('--seed-manifest',(Join-Path $env:Storage__SnapshotRoot 'sample-manifest.json'))}
 foreach($pair in @(@('--refresh-url',$RefreshUrl),@('--approval',$ApprovalFile),@('--report',$ReportRoot))){if($pair[1]){$argsForCli+=$pair}}
 Invoke-ProjectDotnet $argsForCli
}finally{if($held){$lease.ReleaseMutex()};$lease.Dispose()}
