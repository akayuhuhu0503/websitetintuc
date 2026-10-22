[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    $builder = Get-ProjectConnectionBuilder 'LandPortal_Dev'
    [Environment]::SetEnvironmentVariable('Sql__ExpectedDatabase', 'LandPortal_Dev', 'Process')
    Invoke-MigrationCli @('bootstrap')
    exit 0
} catch { Write-Error ('Admin bootstrap failed: ' + $_.Exception.GetType().Name + '. No credential logged.'); exit 1 }
