[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][Alias('Home')][string]$DataHome,
 [Parameter(Mandatory=$true)][string]$Config,
 [Parameter(Mandatory=$true)][string]$PublishRoot,
 [ValidateSet('Web','Scheduler','Documents','InquiryScan','Video','Mail')][string]$Mode='Web',
 [switch]$Once,[switch]$Health
)
# Foreground supervised process only. This script never installs a service, edits
# IIS, grants permissions, migrates SQL or enables SMTP. Operator supplies a
# private config outside the published application and approves the target.
$ErrorActionPreference='Stop'
$homePath=[IO.Path]::GetFullPath($DataHome);$configPath=[IO.Path]::GetFullPath($Config)
$publishPath=[IO.Path]::GetFullPath($PublishRoot)
if(-not $configPath.StartsWith($homePath.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Private config must be within UAT data home'}
if($homePath.StartsWith($publishPath.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)-or $publishPath.StartsWith($homePath.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Private data/config must be outside published app'}
if(-not(Test-Path -LiteralPath $configPath -PathType Leaf)){throw 'UAT private JSON required'}
$c=Get-Content -LiteralPath $configPath -Raw -Encoding UTF8|ConvertFrom-Json
if($c.Deployment.Profile -cne 'UAT' -or $c.Sql.ExpectedDatabase -cne 'LandPortal_Uat'){throw 'Explicit UAT target only'}
foreach($node in @($configPath,$homePath,$publishPath)){if((Get-Item -LiteralPath $node).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse entry refused'}}
$env:LANDPORTAL_HOME=$homePath;$env:LANDPORTAL_CONFIG=$configPath
$env:DOTNET_ENVIRONMENT='Staging';$env:ASPNETCORE_ENVIRONMENT='Staging'
# Avoid ambient local/project override of security/SQL values. No persistent env
# is changed. Runtime validates SQL TLS/target, private paths, MFA and scanner.
foreach($entry in @(Get-ChildItem Env:|Where-Object {$_.Name -match '^(ConnectionStrings|Sql|Storage|Runtime|Mail|Scanner|Video|Media|Deployment|Public|AdminSession|Captcha|Bootstrap)__'})){
 [Environment]::SetEnvironmentVariable($entry.Name,$null,'Process')
}
$component=if($Mode -eq 'Web'){'web'}else{'worker'}
$assembly=Join-Path $publishPath ($component+'/'+$(if($Mode -eq 'Web'){'Web.dll'}else{'Worker.dll'}))
if(-not(Test-Path -LiteralPath $assembly)){throw 'Published runtime assembly absent'}
$argv=@($assembly)
if($Mode -eq 'Web'){$argv+=@('--contentRoot',(Join-Path $publishPath 'web'))}else{
 switch($Mode){'Documents'{$argv+='--documents'}'InquiryScan'{$argv+='--scan'}'Video'{$argv+='--video'}'Mail'{$argv+='--mail'}}
 if($Once){$argv+='--once'}
 if($Health){if($Mode -notin @('InquiryScan','Video')){throw 'Health supported by scanner/video only'};$argv+='--health'}
}
& dotnet @argv
exit $LASTEXITCODE
