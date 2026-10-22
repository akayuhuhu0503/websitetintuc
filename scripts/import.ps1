[CmdletBinding()]
param([string]$Manifest = '.local/snapshots/sample-manifest.json', [ValidateSet('LandPortal_Dev','LandPortal_Test')][string]$Database = 'LandPortal_Dev')
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    $builder = Get-ProjectConnectionBuilder $Database
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Portal', $builder.ConnectionString, 'Process')
    [Environment]::SetEnvironmentVariable('Sql__ExpectedDatabase', $Database, 'Process')
    Invoke-MigrationCli @('import','--manifest',$Manifest)
    exit 0
} catch { Write-Error ('Import failed: ' + $_.Exception.GetType().Name + '; inspect project import issues.'); exit 1 }
