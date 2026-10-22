[CmdletBinding()]
param([ValidateSet('Web','Worker','Sink')][string]$Component='Web',[ValidateRange(1024,65535)][int]$Port=5219,[ValidateRange(1024,65535)][int]$SinkPort=2525,[switch]$NoBuild,[switch]$Once,[switch]$Finish,[switch]$Scan)
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'test-profile.ps1')
Initialize-ProjectTestProfile
$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Public__BaseUrl='http://127.0.0.1:'+$Port
$env:Mail__Transport='TestSink';$env:Mail__SinkPort=$SinkPort.ToString();$env:Media__AllowUnscannedDevelopmentUploads='false'
$build=Join-Path $script:ProjectRoot $(if($Scan){'.local/build/phase2c/scan-current'}elseif($Finish){'.local/build/phase2c/finish-current'}else{'.local/build/phase2c/current'})
if($Component -eq 'Sink'){
 $id=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
 $folder=Join-Path $script:ProjectRoot ('.local/tests/phase2c/mail/'+$id)
 Write-Output ('Test sink on loopback:'+ $SinkPort+'; raw mail remains private in '+$folder)
 & rtk proxy node (Join-Path $script:ProjectRoot 'tests/browser/mail-sink.mjs') $SinkPort $folder
 exit $LASTEXITCODE
}
$project=if($Component -eq 'Worker'){'Worker'}else{'Web'}
if(-not $NoBuild){Invoke-ProjectDotnet @('build',('src/'+$project+'/'+$project+'.csproj'),'--artifacts-path',$build)}
$dll=Join-Path $build ('bin/'+$project+'/debug/'+$project+'.dll')
$arguments=@($dll)
if($project -eq 'Worker'){$arguments+='--mail';if($Once){$arguments+='--once'}}else{$arguments+=@('--contentRoot',(Join-Path $script:ProjectRoot 'src/Web'))}
Invoke-ProjectDotnet $arguments
