[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
$private=[IO.Path]::GetFullPath((Join-Path $env:USERPROFILE 'LandPortal-Private')).TrimEnd('\')+'\'
if(-not $output.StartsWith($private,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)){throw 'New private directory required'}
for($p=$output;$p;$p=[IO.Path]::GetDirectoryName($p)){if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse path refused'}}
$security=New-Object Security.AccessControl.DirectorySecurity;$security.SetAccessRuleProtection($true,$false)
foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'))){$security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))}
[void][IO.Directory]::CreateDirectory($output,$security)
Add-Type -Path (Join-Path $PSScriptRoot 'SqlTlsCertificateProbe.cs')
$result=[SqlTlsCertificateProbe]::Inspect()
if($result.PublicCertificateDer){[IO.File]::WriteAllBytes((Join-Path $output 'sql-peer-public.cer'),$result.PublicCertificateDer)}
$report=[ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('O');endpoint=$result.Endpoint;targetName=$result.TargetName;status=$result.Status;certificate=$result.Leaf;chain=$result.Chain;chainStatus=$result.ChainStatus;sslPolicyErrors=$result.PolicyErrors;failureType=$result.FailureType;serverEncryption=$result.ServerEncryption;protocol=$result.Protocol;loginSent=$result.LoginSent;sqlExecuted=$result.SqlExecuted;validationBypassed=$result.ValidationBypassed;trustStoreChanged=$false;revocationCheck='NOT_RUN';scope='PRELOGIN_TLS_ONLY';client='Windows current machine/user trust; not the unallocated IIS server'}
$report|ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'tls-report.json') -Encoding UTF8
$report|ConvertTo-Json -Depth 8
