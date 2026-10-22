[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('LandPortal_Dev','LandPortal_Test')][string]$Database,[switch]$IsolatedBuild)
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    $builder = Get-ProjectConnectionBuilder $Database
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Portal', $builder.ConnectionString, 'Process')
    [Environment]::SetEnvironmentVariable('Sql__ExpectedDatabase', $Database, 'Process')
    Write-Output "Explicit migration target: $Database. No web-startup migration."
    $migrationArguments=@('run','--project','src/MigrationCli/MigrationCli.csproj','--no-launch-profile')
    if($IsolatedBuild){$migrationArguments+=@('--artifacts-path',(Join-Path $script:ProjectRoot '.local/build/v04'))}
    Invoke-ProjectDotnet ($migrationArguments+@('--','migrate'))
    exit 0
} catch { Write-Error ('Migration failed: ' + $_.Exception.GetType().Name + '; inspect project logs.'); exit 1 }
