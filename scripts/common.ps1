Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:ProjectRoot = Split-Path -Parent $PSScriptRoot
$script:DotnetPath = Join-Path $script:ProjectRoot '.local/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $script:DotnetPath)) { $script:DotnetPath = 'dotnet' }

function Import-ProjectEnvironment {
    $envPath = Join-Path $script:ProjectRoot '.env'
    if (-not (Test-Path -LiteralPath $envPath)) { return }
    foreach ($line in [IO.File]::ReadAllLines($envPath)) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) { continue }
        if ($trimmed -notmatch '^([A-Za-z_][A-Za-z0-9_]*)=(.*)$') { throw 'Invalid .env line. Expected KEY=value; no value was logged.' }
        $key = $matches[1]
        $value = $matches[2]
        if ($value.Length -ge 2 -and (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'")))) { $value = $value.Substring(1, $value.Length - 2) }
        if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($key, 'Process'))) { [Environment]::SetEnvironmentVariable($key, $value, 'Process') }
    }
}

function Invoke-ProjectDotnet {
    param([Parameter(Mandatory=$true)][string[]]$Arguments)
    Push-Location $script:ProjectRoot
    try {
        & rtk proxy $script:DotnetPath @Arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet command failed with exit code $LASTEXITCODE." }
    } finally { Pop-Location }
}

function Assert-ProjectDatabase {
    param([Parameter(Mandatory=$true)][string]$Database)
    if ($Database -cnotin @('LandPortal_Dev', 'LandPortal_Test')) { throw 'Database target must be exactly LandPortal_Dev or LandPortal_Test; other databases are outside this project scope.' }
}

function Get-ProjectConnectionBuilder {
    param([Parameter(Mandatory=$true)][string]$Database)
    Assert-ProjectDatabase $Database
    $value = [Environment]::GetEnvironmentVariable('ConnectionStrings__Portal', 'Process')
    if ([string]::IsNullOrWhiteSpace($value)) { throw 'ConnectionStrings__Portal is missing. Configure project .env or environment; no database connection was attempted.' }
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $value
    Assert-ProjectDatabase $builder['Initial Catalog']
    $builder['Initial Catalog'] = $Database
    return $builder
}

function Invoke-MigrationCli {
    param([Parameter(Mandatory=$true)][string[]]$Arguments)
    Invoke-ProjectDotnet -Arguments (@('run', '--project', 'src/MigrationCli/MigrationCli.csproj', '--no-launch-profile', '--') + $Arguments)
}

Import-ProjectEnvironment
