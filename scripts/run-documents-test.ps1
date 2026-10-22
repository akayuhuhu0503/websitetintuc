[CmdletBinding()]
param([ValidateSet('Web','Worker')][string]$Component='Worker',[ValidateSet('Clamd','DefenderTest')][string]$Provider='Clamd',[string]$ApprovedAnonymousHashes,[Guid]$AssetId=[Guid]::Empty,[switch]$Once,[switch]$Health,[ValidateRange(1024,65535)][int]$Port=5289)
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'test-profile.ps1')
Initialize-ProjectTestProfile
$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Public__BaseUrl='http://127.0.0.1:'+$Port
$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false';$env:Scanner__Provider=$Provider
if($Provider -eq 'DefenderTest'){
 if(-not $ApprovedAnonymousHashes -or @($ApprovedAnonymousHashes.Split(',')|Where-Object{$_ -notmatch '^[a-fA-F0-9]{64}$'}).Count){throw 'Defender Test requires explicit comma-separated anonymous/public SHA256 allowlist; no private citizen files.'}
 $env:Scanner__DefenderAnonymousTestOnly='true';$env:Scanner__DefenderAllowedTestHashes=$ApprovedAnonymousHashes
}
$build=Join-Path $script:ProjectRoot '.local/build/phase2d/v05-finish-current'
$project=if($Component -eq 'Web'){'Web'}else{'Worker'}
$dll=Join-Path $build ('bin/'+$project+'/debug/'+$project+'.dll');if(-not(Test-Path -LiteralPath $dll)){throw 'Build V05-Finish first; no old binary fallback.'}
$arguments=@($dll)
if($Component -eq 'Web'){$arguments+=@('--contentRoot',(Join-Path $script:ProjectRoot 'src/Web'))}else{
 $arguments+='--documents';if($Health){$arguments+='--health'};if($Once){$arguments+='--once'};if($AssetId -ne [Guid]::Empty){$arguments+=@('--asset',$AssetId.ToString())}
}
Write-Output 'SQL Test / isolated Test storage / mail disabled. No source asset selected; no global service installed.'
Invoke-ProjectDotnet $arguments
