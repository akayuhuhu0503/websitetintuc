[CmdletBinding()]
param([ValidateRange(1,50)][int]$Limit = 40, [ValidateRange(1,500)][int]$RequestBudget = 350, [switch]$Refresh)
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    $builder = Get-ProjectConnectionBuilder 'LandPortal_Dev'
    [Environment]::SetEnvironmentVariable('Sql__ExpectedDatabase', 'LandPortal_Dev', 'Process')
    $arguments = @('sample','--limit',$Limit.ToString(),'--request-budget',$RequestBudget.ToString())
    if ($Refresh) { $arguments += '--refresh' }
    Invoke-MigrationCli $arguments
    exit 0
} catch { Write-Error ('Sampling failed: ' + $_.Exception.GetType().Name + '; checkpoint and import issues remain in project storage.'); exit 1 }
