$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../scripts/uat-common.ps1')
$settings=[pscustomobject]@{
 Mail=[pscustomobject]@{Transport='Disabled'}
 Video=[pscustomobject]@{Enabled='true';ProcessorApproved='true';LicenseReference='isolated-test';FfmpegPath='';FfprobePath='';FfmpegSha256='';FfprobeSha256=''}
}
$count=0
function Expect-Rejected([scriptblock]$Action) {
 $rejected=$false
 try { & $Action } catch { $rejected=$true }
 if(-not $rejected){throw 'Expected configuration rejection'}
}
Assert-UatOptionalServices $settings;$count++
$settings.Video.FfmpegSha256='invalid'
Expect-Rejected { Assert-UatOptionalServices $settings };$count++
$settings.Video.FfmpegSha256='a'*64
Assert-UatOptionalServices $settings;$count++
$settings.Video.FfmpegPath='relative.exe'
Expect-Rejected { Assert-UatOptionalServices $settings };$count++
$settings.Video.FfmpegPath=''
$settings.Video.ProcessorApproved='false'
Expect-Rejected { Assert-UatOptionalServices $settings };$count++
$settings.Video.ProcessorApproved='true';$settings.Video.LicenseReference=''
Expect-Rejected { Assert-UatOptionalServices $settings };$count++
foreach($file in @('scripts/build-for-iis.ps1','scripts/uat-common.ps1')){
 $tokens=$null;$errors=$null
 [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot ('../../'+$file)),[ref]$tokens,[ref]$errors)
 if($errors.Count){throw "PowerShell syntax failed: $file"};$count++
}
Write-Output "PASS: $count offline configuration/parser checks; no SQL or deployment."
