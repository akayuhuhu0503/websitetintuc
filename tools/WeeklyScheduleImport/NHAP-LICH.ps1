[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Preflight','DryRun','Import','Reconcile','SyncLinks')][string]$Action,
    [Parameter(Mandatory=$true)][string]$IisWeb,
    [string]$SourceZip = (Join-Path $PSScriptRoot 'website_upload.zip'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'ket-qua'),
    [string]$User,
    [string]$PlanSha256
)
$ErrorActionPreference = 'Stop'
$tool = Join-Path $PSScriptRoot 'WeeklyScheduleImport.dll'
if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw 'Missing published WeeklyScheduleImport.dll. Copy the complete CLI publish folder.' }
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$mode = @{Preflight='preflight'; DryRun='dry-run'; Import='import'; Reconcile='reconcile'; SyncLinks='sync-links'}[$Action]
$arguments = @($tool, $mode, '--iis-web', $IisWeb, '--zip', $SourceZip, '--out', $OutputDirectory)
if ($Action -ne 'Preflight') {
    if ([string]::IsNullOrWhiteSpace($User)) { $User = Read-Host 'Ten dang nhap CMS hien hanh' }
    if ([string]::IsNullOrWhiteSpace($User)) { throw 'CMS username required; no account is created or reset.' }
    $arguments += @('--user', $User)
}
if ($Action -eq 'Import') {
    if ($PlanSha256 -notmatch '^[a-f0-9]{64}$') { throw 'Import requires the exact PlanSha256 reported by DryRun after reviewing plan.json.' }
    $arguments += @('--confirm-plan', $PlanSha256)
}
# Password is read by the CLI without echo. Never collect/pass it as a script argument.
& $dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Weekly import stopped (exit $LASTEXITCODE). Read the BLOCKED reason above; do not retry Import blindly." }
