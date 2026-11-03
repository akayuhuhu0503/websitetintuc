[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$EnvFile,
 [Parameter(Mandatory=$true)][string]$DataHome,
 [Parameter(Mandatory=$true)][string]$PublishRoot,
 [string]$TargetDataHome,
 [switch]$ValidateOnly,
 [switch]$ForPublish,
 [switch]$DeferTarget
)
# Offline converter only. Does not load an application, execute .env, contact SQL,
# change IIS/services or copy secrets into the published directory.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'uat-common.ps1')
$code=Get-UatSafePath $PublishRoot;$homePath=Get-UatSafePath $DataHome
$source=Get-UatSafePath $EnvFile;$config=Join-Path $homePath 'uat.json'
Assert-UatSeparatePaths $homePath $config $code
if(-not $ValidateOnly -and -not(Test-Path -LiteralPath $homePath -PathType Container)){throw 'Create a private NTFS data home with approved ACL first'}
if($source.StartsWith($code+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Secret input must be outside the published directory'}
if(-not(Test-Path -LiteralPath $source -PathType Leaf)){throw 'Existing private .env input required'}
foreach($file in @($config,(Join-Path $homePath 'iis.web.config'))){if(Test-Path -LiteralPath $file){throw 'Existing private configuration refused; no overwrite'}}
. (Join-Path $PSScriptRoot 'iis-env.ps1')
$inputSettings=Read-IisEnvironment $source -ForPublish:$ForPublish
$settings=$inputSettings.Settings
$runtimeHome=if($TargetDataHome){Get-UatSafePath $TargetDataHome}else{$homePath}
# All validation below is in memory. No SqlConnection.Open or application startup.
if($DeferTarget){
 if(-not $ForPublish){throw 'Deferred target is only supported for offline publishing'}
 Assert-IisPublishConfiguration $settings
 $settings.Deployment.TargetApproved='false';$settings.Deployment.TargetApprovalReference=''
}else{Assert-UatRuntimeConfiguration $settings $runtimeHome ([IO.Path]::Combine($runtimeHome,'uat.json')) $code}
if($ValidateOnly){Write-Output 'CONFIG VALIDATION PASS (offline; no SQL connection)';return}
[xml]$web=Get-Content -LiteralPath (Join-Path $code 'web/web.config') -Raw -Encoding UTF8
foreach($v in @($web.configuration.location.'system.webServer'.aspNetCore.environmentVariables.environmentVariable)){
 if(-not $DeferTarget -and $v.name -ceq 'Deployment__TargetApproved'){[void]$v.ParentNode.RemoveChild($v);continue}
 if($v.name -eq 'LANDPORTAL_HOME'){$v.SetAttribute('value',$(if($DeferTarget){''}else{[string]$runtimeHome}))}
 if($v.name -eq 'LANDPORTAL_CONFIG'){$v.SetAttribute('value',$(if($DeferTarget){''}else{[string]([IO.Path]::Combine($runtimeHome,'uat.json'))}))}
}
# Secure files at creation time: operator + SYSTEM only. Operator grants read to
# the approved Web/Worker identities later; this script changes no existing ACL.
$security=New-Object Security.AccessControl.FileSecurity
$security.SetAccessRuleProtection($true,$false)
foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'))){$security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','Allow')))}
function Write-PrivateFile([string]$Path,[string]$Text){
 $stream=[IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[Security.AccessControl.FileSystemRights]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::None,$security)
 try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($Text);$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
}
Write-PrivateFile $config ($settings|ConvertTo-Json -Depth 16)
Write-PrivateFile (Join-Path $homePath 'iis.web.config') $web.OuterXml
Write-Output 'Private uat.json and iis.web.config prepared; secrets not printed. No SQL, IIS or service operation performed. Review configuration/ACL before installing the bound web.config.'
