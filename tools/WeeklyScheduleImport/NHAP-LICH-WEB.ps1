[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Preflight','DryRun','Pilot','ImportAll','Reconcile','SyncLinks','DateReport','DatePlan','SyncDates','VerifyDates')][string]$Action,
    [string]$SourceZip = 'C:\website_upload\website_upload.zip',
    [string]$OutputDirectory = (Join-Path $env:USERPROFILE 'LandPortal-Private\weekly43-web'),
    [string]$Site = 'https://gdla.gov.vn/',
    [string]$Dotnet,
    [string]$DatePlan,
    [ValidateRange(1,43)][int]$Stt
)
$ErrorActionPreference = 'Stop'
$mode = @{Preflight='web-preflight';DryRun='web-dry-run';Pilot='web-import';ImportAll='web-import';Reconcile='web-reconcile';SyncLinks='web-sync-links';DateReport='web-date-report';DatePlan='web-date-plan';SyncDates='web-sync-dates';VerifyDates='web-verify-dates'}[$Action]
$arguments = @($mode,'--zip',$SourceZip,'--out',$OutputDirectory,'--site',$Site)
if ($Action -eq 'ImportAll') { $arguments += '--all' }
if ($DatePlan) { $arguments += @('--date-plan',$DatePlan) }
if ($PSBoundParameters.ContainsKey('Stt')) { $arguments += @('--stt',[string]$Stt) }
$dll = Join-Path $PSScriptRoot 'WeeklyScheduleImport.dll'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw 'Use the complete published CLI folder on the Developer PC.' }
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'coreclr.dll')) {
    & (Join-Path $PSScriptRoot 'WeeklyScheduleImport.exe') @arguments
} else {
    if ([string]::IsNullOrWhiteSpace($Dotnet)) { $Dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
    & $Dotnet $dll @arguments
}
if ($LASTEXITCODE -ne 0) { throw "WEB IMPORT stopped (exit $LASTEXITCODE). Keep the same output directory; reconcile before resuming. No password or cookie is stored." }
