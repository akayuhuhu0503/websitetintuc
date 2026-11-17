[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Preflight','DryRun','Pilot','ImportAll','Reconcile','SyncLinks')][string]$Action,
    [string]$SourceZip = 'C:\website_upload\website_upload.zip',
    [string]$OutputDirectory = (Join-Path $env:USERPROFILE 'LandPortal-Private\weekly43-web'),
    [string]$Site = 'https://gdla.gov.vn/',
    [string]$Dotnet
)
$ErrorActionPreference = 'Stop'
$mode = @{Preflight='web-preflight';DryRun='web-dry-run';Pilot='web-import';ImportAll='web-import';Reconcile='web-reconcile';SyncLinks='web-sync-links'}[$Action]
$arguments = @($mode,'--zip',$SourceZip,'--out',$OutputDirectory,'--site',$Site)
if ($Action -eq 'ImportAll') { $arguments += '--all' }
$dll = Join-Path $PSScriptRoot 'WeeklyScheduleImport.dll'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw 'Use the complete published CLI folder on the Developer PC.' }
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'coreclr.dll')) {
    & (Join-Path $PSScriptRoot 'WeeklyScheduleImport.exe') @arguments
} else {
    if ([string]::IsNullOrWhiteSpace($Dotnet)) { $Dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
    & $Dotnet $dll @arguments
}
if ($LASTEXITCODE -ne 0) { throw "WEB IMPORT stopped (exit $LASTEXITCODE). Keep the same output directory; reconcile before resuming. No password or cookie is stored." }
