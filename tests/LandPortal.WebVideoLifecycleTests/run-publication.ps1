param([Parameter(Mandatory=$true)][string]$DotnetPath)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$DotnetPath=(Resolve-Path -LiteralPath $DotnetPath).Path
$id=[Guid]::NewGuid().ToString('N').Substring(0,12)
$instance='LPWebPublication_'+$id
$run=Join-Path $root ('.local/tests/web-publication-'+$id)
[void][IO.Directory]::CreateDirectory($run)
Push-Location $root
try {
 & $DotnetPath build (Join-Path $PSScriptRoot 'LandPortal.WebVideoLifecycleTests.csproj') -c Release --artifacts-path (Join-Path $root '.local/build/web-video-lifecycle')
 if($LASTEXITCODE){throw 'Focused publication build failed; SQL not created.'}
 $assembly=Join-Path $root '.local/build/web-video-lifecycle/bin/LandPortal.WebVideoLifecycleTests/release/LandPortal.WebVideoLifecycleTests.dll'
 & $DotnetPath $assembly --maintenance
 if($LASTEXITCODE){throw 'Focused maintenance checks failed; SQL not created.'}
 & SqlLocalDB.exe create $instance -s
 if($LASTEXITCODE){throw 'Cannot create the new isolated LocalDB instance.'}
 try { & $DotnetPath $assembly --publication $instance $run; $result=$LASTEXITCODE }
 finally { & SqlLocalDB.exe stop $instance }
 Write-Output ('Focused publication evidence: '+$run)
 exit $result
} finally { Pop-Location }
