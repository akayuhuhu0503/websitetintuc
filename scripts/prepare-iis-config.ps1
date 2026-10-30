[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$EnvFile,
 [Parameter(Mandatory=$true)][string]$DataHome,
 [Parameter(Mandatory=$true)][string]$PublishRoot
)
# Offline converter only. Does not load an application, execute .env, contact SQL,
# change IIS/services or copy secrets into the published directory.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'uat-common.ps1')
$code=Get-UatSafePath $PublishRoot;$homePath=Get-UatSafePath $DataHome
$source=Get-UatSafePath $EnvFile;$config=Join-Path $homePath 'uat.json'
Assert-UatSeparatePaths $homePath $config $code
if(-not(Test-Path -LiteralPath $homePath -PathType Container)){throw 'Create a private NTFS data home with approved ACL first'}
if($source.StartsWith($code+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Secret input must be outside the published directory'}
if(-not(Test-Path -LiteralPath $source -PathType Leaf)){throw 'Existing private .env input required'}
foreach($file in @($config,(Join-Path $homePath 'iis.web.config'))){if(Test-Path -LiteralPath $file){throw 'Existing private configuration refused; no overwrite'}}
$template=Join-Path $PSScriptRoot 'uat-config.example.json'
$settings=Get-Content -LiteralPath $template -Raw -Encoding UTF8|ConvertFrom-Json
$seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($line in [IO.File]::ReadAllLines($source)){
 $text=$line.Trim();if(-not $text -or $text.StartsWith('#')){continue}
 if($text -notmatch '^([A-Za-z][A-Za-z0-9_]*__(?:[A-Za-z0-9_]+))=(.*)$'){throw 'Invalid input: only application Section__Key=value entries are accepted; values are not logged'}
 $key=$matches[1];$value=$matches[2]
 if(-not $seen.Add($key)){throw 'Duplicate configuration key; values are not logged'}
 if($value.Length -ge 2 -and (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'")))){$value=$value.Substring(1,$value.Length-2)}
 $parts=$key -split '__';if(@($parts|Where-Object {$_ -notmatch '^[A-Za-z][A-Za-z0-9]*$'}).Count){throw 'Invalid configuration key segments'}
 $node=$settings
 for($i=0;$i -lt $parts.Length-1;$i++){
  $property=$node.PSObject.Properties[$parts[$i]]
  if($null -eq $property){$node|Add-Member -MemberType NoteProperty -Name $parts[$i] -Value ([pscustomobject]@{});$property=$node.PSObject.Properties[$parts[$i]]}
  if($property.Value -isnot [pscustomobject]){throw 'Configuration key/object collision'}
  $node=$property.Value
 }
 $node|Add-Member -MemberType NoteProperty -Name $parts[-1] -Value $value -Force
}
# All validation below is in memory. No SqlConnection.Open or application startup.
Assert-UatRuntimeConfiguration $settings $homePath $config $code
[xml]$web=Get-Content -LiteralPath (Join-Path $code 'web/web.config') -Raw -Encoding UTF8
foreach($v in $web.configuration.location.'system.webServer'.aspNetCore.environmentVariables.environmentVariable){
 if($v.name -eq 'LANDPORTAL_HOME'){$v.SetAttribute('value',[string]$homePath)}
 if($v.name -eq 'LANDPORTAL_CONFIG'){$v.SetAttribute('value',[string]$config)}
}
# Secure files at creation time: operator + SYSTEM only. Operator grants read to
# the approved Web/Worker identities later; this script changes no existing ACL.
$security=New-Object Security.AccessControl.FileSecurity
$security.SetAccessRuleProtection($true,$false)
foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'))){$security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','Allow')))}
function Write-PrivateFile([string]$Path,[string]$Text){
 $stream=[IO.File]::Create($Path,4096,[IO.FileOptions]::None,$security)
 try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($Text);$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
}
Write-PrivateFile $config ($settings|ConvertTo-Json -Depth 16)
Write-PrivateFile (Join-Path $homePath 'iis.web.config') $web.OuterXml
Write-Output 'Private uat.json and iis.web.config prepared; secrets not printed. No SQL, IIS or service operation performed. Review configuration/ACL before installing the bound web.config.'
