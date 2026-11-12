param(
 [Parameter(Mandatory=$true)][string]$DotnetPath,
 [Parameter(Mandatory=$true)][string]$ToolConfigurationPath
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$DotnetPath=(Resolve-Path -LiteralPath $DotnetPath).Path
$ToolConfigurationPath=(Resolve-Path -LiteralPath $ToolConfigurationPath).Path
$id=[Guid]::NewGuid().ToString('N').Substring(0,12)
$instance='LPWebVideo_'+$id
$run=Join-Path $root ('.local/tests/web-video-'+$id)
[void][IO.Directory]::CreateDirectory($run)
Push-Location $root
try {
 & $DotnetPath build (Join-Path $PSScriptRoot 'LandPortal.WebVideoLifecycleTests.csproj') -c Release --artifacts-path (Join-Path $root '.local/build/web-video-lifecycle')
 if($LASTEXITCODE){throw 'Focused lifecycle test build failed; SQL not created.'}
 & SqlLocalDB.exe create $instance -s
 if($LASTEXITCODE){throw 'Cannot create the new isolated LocalDB instance.'}
 try {
  & $DotnetPath (Join-Path $root '.local/build/web-video-lifecycle/bin/LandPortal.WebVideoLifecycleTests/release/LandPortal.WebVideoLifecycleTests.dll') $instance $run $ToolConfigurationPath
  $result=$LASTEXITCODE
 } finally {
  & SqlLocalDB.exe stop $instance
 }
 Write-Output ('Focused lifecycle evidence: '+$run)
 exit $result
} finally { Pop-Location }
