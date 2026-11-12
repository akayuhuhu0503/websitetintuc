[CmdletBinding()]
param([ValidateSet('Scheduler','Documents','InquiryScan','Video','Mail')][string]$Mode='Video',[switch]$Once,[switch]$Health,[switch]$VerifyPackage,[string]$IisAppPool)
$ErrorActionPreference='Stop'
# Installed at web/.runtime/run-worker.ps1. Separate foreground process supervised by the existing task/service.
$manifest=Join-Path $PSScriptRoot 'manifest.json'
$m=Get-Content -LiteralPath $manifest -Raw -Encoding UTF8|ConvertFrom-Json
if($m.privateMode -cne 'WebBundle'){throw 'WebBundle manifest required'}
$web=Split-Path $PSScriptRoot -Parent
foreach($relative in @('.runtime/worker/Worker.dll','.runtime/worker/Infrastructure.dll')){
 $record=@($m.files|Where-Object path -eq $relative);$file=Join-Path $web $relative
 if($record.Count -ne 1 -or -not(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -ine $record[0].sha256){throw 'Worker binary/manifest mismatch; copy complete web release'}
}
foreach($entry in @(Get-ChildItem Env:|Where-Object {$_.Name -match '^(ConnectionStrings|Sql|Storage|Runtime|Mail|Scanner|Video|Media|Deployment|Public|AdminSession|Captcha|Bootstrap)__|^LANDPORTAL_'})){
 [Environment]::SetEnvironmentVariable($entry.Name,$null,'Process')
}
$env:DOTNET_ENVIRONMENT='Staging';$env:ASPNETCORE_ENVIRONMENT='Staging';$env:LANDPORTAL_PORTABLE_UAT='1';$env:LANDPORTAL_WEB_BUNDLE='1'
# Process-only selector for live WAS metadata; not a configuration/manifest override.
# The service account needs Read on that pool's native config, never Administrator.
if($IisAppPool){
 if($IisAppPool -ne $IisAppPool.Trim() -or $IisAppPool -in @('.','..') -or $IisAppPool.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or $IisAppPool.EndsWith('.')){throw 'Invalid IIS application pool name'}
 $env:LANDPORTAL_IIS_APP_POOL=$IisAppPool
}
$arguments=@((Join-Path $PSScriptRoot 'worker/Worker.dll'))
if($VerifyPackage){if($Once -or $Health){throw 'Offline package check cannot run queue/health modes'};& dotnet @arguments --verify-package;exit $LASTEXITCODE}
switch($Mode){'Documents'{$arguments+='--documents'}'InquiryScan'{$arguments+='--scan'}'Video'{$arguments+='--video'}'Mail'{$arguments+='--mail'}}
if($Once){$arguments+='--once'}
if($Health){if($Mode -notin @('Video','Documents','InquiryScan')){throw 'Health mode unsupported'};$arguments+='--health'}
& dotnet @arguments
exit $LASTEXITCODE
