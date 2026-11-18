param(
    [Parameter(Mandatory=$true)][ValidateSet('Report','Inspect','Preflight','DryRun','Import','Reconcile','SyncLinks')][string]$Action,
    [Parameter(Mandatory=$true)][string]$Zip,
    [Parameter(Mandatory=$true)][string]$Output,
    [string]$Site = 'https://gdla.gov.vn/',
    [string]$PdfOverrides,
    [ValidateRange(1,32)][int]$Stt,
    [switch]$All
)
$ErrorActionPreference = 'Stop'
$commands = @{ Report='planning-report'; Inspect='planning-inspect'; Preflight='planning-preflight'; DryRun='planning-dry-run'; Import='planning-import'; Reconcile='planning-reconcile'; SyncLinks='planning-sync-links' }
$parameters = @($commands[$Action], '--zip', $Zip, '--out', $Output, '--site', $Site)
if ($Stt) { $parameters += @('--stt', [string]$Stt) }
if ($All) { $parameters += '--all' }
if ($PdfOverrides) { $parameters += @('--pdf-overrides', $PdfOverrides) }
$exe = Join-Path $PSScriptRoot 'WeeklyScheduleImport.exe'
if (Test-Path -LiteralPath $exe) { & $exe @parameters }
else { & dotnet (Join-Path $PSScriptRoot 'WeeklyScheduleImport.dll') @parameters }
if ($LASTEXITCODE -ne 0) { throw 'QHKHSDD dung do loi. Giu nguyen nhat ky; khong tu thu lai hoac ghi de.' }
