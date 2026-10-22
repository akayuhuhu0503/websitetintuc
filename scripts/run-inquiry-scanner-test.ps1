[CmdletBinding()]
param([ValidateRange(0,65535)][int]$ClamdPort=0,[switch]$Once,[Guid]$FileId=[Guid]::Empty,[ValidateSet('Clamd','DefenderTest')][string]$Provider='Clamd',[string]$ApprovedAnonymousHashes,[switch]$ScanBuild,[switch]$Health)
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'test-profile.ps1')
Initialize-ProjectTestProfile
$env:DOTNET_ENVIRONMENT='Development'
$env:Scanner__ClamdPort=$ClamdPort.ToString()
$env:Scanner__Provider=$Provider
if($Provider -eq 'DefenderTest'){if(-not $ApprovedAnonymousHashes){throw 'Explicit anonymous/public Test SHA256 allowlist required; arbitrary private files are prohibited.'};$env:Scanner__DefenderAnonymousTestOnly='true';$env:Scanner__DefenderAllowedTestHashes=$ApprovedAnonymousHashes}
$env:Public__BaseUrl='http://127.0.0.1:5219'
$build=Join-Path $script:ProjectRoot $(if($ScanBuild){'.local/build/phase2c/scan-current'}else{'.local/build/phase2c/finish-current'})
$arguments=@((Join-Path $build 'bin/Worker/debug/Worker.dll'),'--scan')
if($Once){$arguments+='--once'}
if($Health){$arguments+='--health'}
if($FileId -ne [Guid]::Empty){$arguments+=@('--file',$FileId.ToString())}
Write-Output 'SQL Test/storage only. Genuine engine required; missing/error never Clean. DefenderTest only explicit anonymous/public hashes; no AV policies or services changed.'
Invoke-ProjectDotnet $arguments
