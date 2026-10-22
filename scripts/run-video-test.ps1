[CmdletBinding()]
param([ValidateSet('Web','Worker','Health','GC')][string]$Component='Web',[ValidateRange(1024,65535)][int]$Port=5259,[switch]$NoBuild,[switch]$Once,[Guid]$AssetId=[Guid]::Empty,[ValidateSet('Clamd','DefenderTest')][string]$Provider='Clamd',[ValidateRange(0,65535)][int]$ClamdPort=0,[string]$ApprovedAnonymousHashes)
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'test-profile.ps1')
. (Join-Path $PSScriptRoot 'video-test-profile.ps1')
Initialize-ProjectTestProfile
Initialize-VideoTestTools
$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Public__BaseUrl='http://127.0.0.1:'+$Port;$env:Video__Enabled='true'
$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false'
$env:Scanner__Provider=$Provider;$env:Scanner__ClamdPort=$ClamdPort.ToString()
if($Provider -eq 'DefenderTest'){
 if(-not $ApprovedAnonymousHashes -or @($ApprovedAnonymousHashes.Split(',')|Where-Object{$_ -cnotmatch '^[a-f0-9]{64}$'}).Count -gt 0){throw 'Explicit anonymous/public SHA256 allowlist required. No private citizen data or Production scanning is authorized.'}
 $env:Scanner__DefenderAnonymousTestOnly='true';$env:Scanner__DefenderAllowedTestHashes=$ApprovedAnonymousHashes
}
$build=Join-Path $script:ProjectRoot '.local/build/phase2d/current'
$project=if($Component -eq 'Web'){'Web'}else{'Worker'}
if(-not $NoBuild){Invoke-ProjectDotnet @('build',('src/'+$project+'/'+$project+'.csproj'),'--artifacts-path',$build)}
$arguments=@((Join-Path $build ('bin/'+$project+'/debug/'+$project+'.dll')))
if($Component -eq 'Web'){$arguments+=@('--contentRoot',(Join-Path $script:ProjectRoot 'src/Web'));Write-Output ('Test video web: '+$env:Public__BaseUrl+' ; uploads remain private until genuine scan, processing and CMS publication.')}
else{$arguments+='--video';if($Once){$arguments+='--once'};if($AssetId -ne [Guid]::Empty){$arguments+=@('--asset',$AssetId.ToString())};if($Component -eq 'Health'){$arguments+='--health'};if($Component -eq 'GC'){$arguments+='--gc'}}
Invoke-ProjectDotnet $arguments
