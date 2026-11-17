[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$DotNet,[Parameter(Mandatory=$true)][string]$ToolMetadata)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root
$id=[Guid]::NewGuid().ToString('N').Substring(0,12)
$instance='LPVideoTimeout_'+$id
$run=Join-Path $root ('.local/tests/video-timeout-'+$id)
[void][IO.Directory]::CreateDirectory($run)
. (Join-Path $PSScriptRoot 'uat-common.ps1')
. (Join-Path $PSScriptRoot 'iis-env.ps1')
# Entirely synthetic values, parsed offline. Never reads the real .env.
$inputFile=Join-Path $run 'fixture.env'
$fixture=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ttdl.env.example'))
$fixture=$fixture.Replace('Password=;','Password=FixtureOnly-NoServerContact-12!;').Replace('Runtime__RolePassword=',('Runtime__RolePassword=FixtureRole-'+[Guid]::NewGuid().ToString('N')))
[IO.File]::WriteAllText($inputFile,$fixture,[Text.UTF8Encoding]::new($false))
$parsed=Read-IisEnvironment $inputFile -ForPublish
if([int]$parsed.Settings.Video.ProcessSeconds -ne 1800){throw 'Publish default did not flow from minimal env'}
$sealed=Join-Path $run 'synthetic-uat.json'
[IO.File]::WriteAllText($sealed,($parsed.Settings|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
$hash=(Get-FileHash -LiteralPath $sealed -Algorithm SHA256).Hash
if([int](Get-Content -LiteralPath $sealed -Raw|ConvertFrom-Json).Video.ProcessSeconds -ne 1800 -or (Get-FileHash -LiteralPath $sealed -Algorithm SHA256).Hash -cne $hash){throw 'Offline serialization/hash check failed'}
[IO.File]::AppendAllText($inputFile,"`nVideo__ProcessSeconds=300`n")
if([int](Read-IisEnvironment $inputFile -ForPublish).Settings.Video.ProcessSeconds -ne 300){throw 'Explicit configuration lost'}
[IO.File]::WriteAllText($inputFile,($fixture+"`nVideo__ProcessSeconds=1801`n"),[Text.UTF8Encoding]::new($false))
$denied=$false;try{Read-IisEnvironment $inputFile -ForPublish|Out-Null}catch{$denied=$true}
if(-not $denied){throw 'Over-limit publish configuration accepted'}
Write-Output 'PASS offline Publish input: default1800, serialization/hash, explicit300, rejects1801'
$build=Join-Path $root '.local/build/video-timeout'
& $DotNet build tests/LandPortal.WebVideoLifecycleTests/LandPortal.WebVideoLifecycleTests.csproj -c Release --artifacts-path $build
if($LASTEXITCODE){throw 'Focused test/Web build failed; no SQL instance created'}
& $DotNet build src/Worker/Worker.csproj -c Release --artifacts-path $build
if($LASTEXITCODE){throw 'Worker Release build failed; no SQL instance created'}
& SqlLocalDB.exe create $instance -s
if($LASTEXITCODE){throw 'Cannot create new isolated SQL Test instance'}
try{
 & $DotNet (Join-Path $build 'bin/LandPortal.WebVideoLifecycleTests/release/LandPortal.WebVideoLifecycleTests.dll') --timeout $instance $run $ToolMetadata
 $result=$LASTEXITCODE
}finally{& SqlLocalDB.exe stop $instance}
Write-Output ('Focused Test result: '+$run)
exit $result
