[CmdletBinding()]
param([string]$Settings)
# Read-only inventory/guard probe: no scan, service installation, AV policy change,
# SQL, credentials output or implicit authorization. Run as the actual service user.
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
. (Join-Path $PSScriptRoot 'uat-common.ps1')
try{
 $status=Get-MpComputerStatus;$privacy=Get-MpPreference
 $platform=Join-Path $env:ProgramData 'Microsoft/Windows Defender/Platform'
 $binary=Get-ChildItem -LiteralPath $platform -Directory|Sort-Object Name -Descending|ForEach-Object{Join-Path $_.FullName 'MpCmdRun.exe'}|Where-Object{Test-Path -LiteralPath $_ -PathType Leaf}|Select-Object -First 1
 if(-not $binary){$binary=Join-Path $env:ProgramFiles 'Windows Defender/MpCmdRun.exe'}
 $signature=Get-AuthenticodeSignature -LiteralPath $binary;$sha=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant()
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent().Name
 $nativeReady=$status.AMServiceEnabled -and $status.AntivirusEnabled -and $status.AMRunningMode -eq 'Normal' -and $signature.Status -eq 'Valid' -and $signature.SignerCertificate.Subject -match 'Microsoft' -and $status.AntivirusSignatureLastUpdated.ToUniversalTime() -gt [DateTime]::UtcNow.AddHours(-72)
 $privacyReady=([int]$privacy.SubmitSamplesConsent -eq 2 -and [int]$privacy.MAPSReporting -eq 0)
 $authorized=$false
 if($Settings){
  $c=Get-Content -LiteralPath $Settings -Raw -Encoding UTF8|ConvertFrom-Json;Assert-UatTarget $c
  $authorized=$c.Scanner.Provider -ceq 'MicrosoftDefenderOnPrem' -and ([string]$c.Scanner.PolicyReference).Length -gt 4 -and ([string]$c.Scanner.DefenderPrivacyPolicyReference).Length -gt 4 -and $c.Scanner.DefenderServiceIdentity -ieq $identity -and $c.Scanner.DefenderBinarySha256 -ieq $sha
 }
 @{status=if(-not $Settings){'INVENTORY_ONLY_NOT_RUNTIME_ACCEPTANCE'}elseif($nativeReady -and $privacyReady -and $authorized){'LOCAL_GUARDS_READY_TARGET_ENGINE_E2E_STILL_REQUIRED'}else{'BLOCKED'};binarySha256=$sha;engine=$status.AMEngineVersion;definitions=$status.AntivirusSignatureVersion;definitionsUpdatedUtc=$status.AntivirusSignatureLastUpdated.ToUniversalTime().ToString('O');signedMicrosoft=($signature.Status -eq 'Valid' -and $signature.SignerCertificate.Subject -match 'Microsoft');privacyReady=$privacyReady;policyRequires='SubmitSamplesConsent=2 and MAPSReporting=0, configured externally by TTDL authority';serviceIdentity=$identity;configuredIdentityAndPinMatch=$authorized;scannedFiles=0;settingsChanged=$false}|ConvertTo-Json
 if($Settings -and (-not $nativeReady -or -not $privacyReady -or -not $authorized)){exit 2}
}catch{Write-Output ('Defender inventory BLOCKED: '+$_.Exception.GetType().Name+'; no policy or service changes');exit 2}
