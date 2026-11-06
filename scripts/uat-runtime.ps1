[CmdletBinding()]
param(
 [Alias('Home')][string]$DataHome,
 [string]$Config,
 [Parameter(Mandatory=$true)][string]$PublishRoot,
 [ValidateSet('Web','Scheduler','Documents','InquiryScan','Video','Mail','Preflight')][string]$Mode='Web',
 [switch]$Once,[switch]$Health,[switch]$Online,[string]$Report
)
# Foreground supervised process only. This script never installs a service, edits
# IIS, grants permissions, migrates SQL or enables SMTP. Operator supplies a
# private config outside the published application and approves the target.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'uat-common.ps1')
$publishPath=[IO.Path]::GetFullPath($PublishRoot)
$packageManifest=Join-Path $publishPath 'manifest.json'
$portable=$false
if(Test-Path -LiteralPath $packageManifest){$packageInfo=Get-Content -LiteralPath $packageManifest -Raw -Encoding UTF8|ConvertFrom-Json;$portable=($packageInfo.PSObject.Properties.Name -contains 'privateMode')-and ($packageInfo.privateMode -ceq 'Portable')}
if($portable){
 if($DataHome -or $Config){throw 'Portable package uses its bound private config; do not supply Home/Config overrides'}
 # The shared .NET loader checks the manifest/hash, ACL, identity, real approval,
 # HTTPS, scanner and target guards before any Web/Worker/CLI SQL connection.
 $env:LANDPORTAL_PORTABLE_UAT='1'
}else{
if(-not $DataHome -or -not $Config){throw 'Legacy package requires private DataHome and Config'}
Assert-UatSeparatePaths $DataHome $Config $PublishRoot
$homePath=[IO.Path]::GetFullPath($DataHome);$configPath=[IO.Path]::GetFullPath($Config)
$publishPath=[IO.Path]::GetFullPath($PublishRoot)
if(-not $configPath.StartsWith($homePath.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Private config must be within UAT data home'}
if($homePath.StartsWith($publishPath.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)-or $publishPath.StartsWith($homePath.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Private data/config must be outside published app'}
if(-not(Test-Path -LiteralPath $configPath -PathType Leaf)){throw 'UAT private JSON required'}
$c=Get-Content -LiteralPath $configPath -Raw -Encoding UTF8|ConvertFrom-Json
Assert-UatRuntimeConfiguration $c $homePath $configPath $publishPath
foreach($node in @($configPath,$homePath,$publishPath)){if((Get-Item -LiteralPath $node).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse entry refused'}}
$env:LANDPORTAL_HOME=$homePath;$env:LANDPORTAL_CONFIG=$configPath
$env:LANDPORTAL_PORTABLE_UAT=$null
}
$env:DOTNET_ENVIRONMENT='Staging';$env:ASPNETCORE_ENVIRONMENT='Staging'
# Avoid ambient local/project override of security/SQL values. No persistent env
# is changed. Runtime validates SQL TLS/target, private paths, authorization and scanner.
foreach($entry in @(Get-ChildItem Env:|Where-Object {$_.Name -match '^(ConnectionStrings|Sql|Storage|Runtime|Mail|Scanner|Video|Media|Deployment|Public|AdminSession|Captcha|Bootstrap)__'})){
 [Environment]::SetEnvironmentVariable($entry.Name,$null,'Process')
}
if(($Online -or $Report) -and $Mode -ne 'Preflight'){throw 'Online/Report are read-only preflight options only'}
$component=if($Mode -eq 'Web'){'web'}elseif($Mode -eq 'Preflight'){'tools'}else{'worker'}
$assembly=Join-Path $publishPath ($component+'/'+$(if($Mode -eq 'Web'){'Web.dll'}elseif($Mode -eq 'Preflight'){'MigrationCli.dll'}else{'Worker.dll'}))
if(-not(Test-Path -LiteralPath $assembly)){throw 'Published runtime assembly absent'}
$argv=@($assembly)
if($Mode -eq 'Preflight'){
 if(-not $Report -or $Once -or $Health){throw 'New private Report required; no queue/health actions during preflight'}
 $argv+=@('uat-preflight','--report',$Report);if($Online){$argv+='--online'}
}elseif($Mode -eq 'Web'){$argv+=@('--contentRoot',(Join-Path $publishPath 'web'))}else{
 switch($Mode){'Documents'{$argv+='--documents'}'InquiryScan'{$argv+='--scan'}'Video'{$argv+='--video'}'Mail'{$argv+='--mail'}}
 if($Once){$argv+='--once'}
 if($Health){if($Mode -notin @('Documents','InquiryScan','Video')){throw 'Health supported by document/inquiry scanner and video only'};$argv+='--health'}
}
& dotnet @argv
exit $LASTEXITCODE
