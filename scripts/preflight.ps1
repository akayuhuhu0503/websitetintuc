[CmdletBinding()]
param([ValidateSet('LandPortal_Dev','LandPortal_Test')][string]$Database = 'LandPortal_Dev')
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    Invoke-ProjectDotnet @('--version')
    Invoke-ProjectDotnet @('--list-runtimes')
    $builder = Get-ProjectConnectionBuilder $Database
    $builder['Initial Catalog'] = 'master'
    $connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(100)), CAST(SERVERPROPERTY('Edition') AS nvarchar(200)), CAST(SERVERPROPERTY('EngineEdition') AS int), DB_ID(@database)"
        [void]$command.Parameters.AddWithValue('@database', $Database)
        $reader = $command.ExecuteReader()
        if (-not $reader.Read()) { throw 'SQL server properties unavailable.' }
        Write-Output ('SQL version ' + $reader.GetString(0) + '; edition ' + $reader.GetString(1) + '; target ' + $Database + '; exists=' + (-not $reader.IsDBNull(3)))
        $reader.Close()
    } finally { $connection.Dispose() }
    foreach ($key in @('Storage__MediaRoot','Storage__SnapshotRoot','Storage__TempRoot','Storage__KeysRoot')) {
        $configured = [Environment]::GetEnvironmentVariable($key, 'Process')
        if ([string]::IsNullOrWhiteSpace($configured)) { throw "$key is missing." }
        $resolved = if ([IO.Path]::IsPathRooted($configured)) { [IO.Path]::GetFullPath($configured) } else { [IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $configured)) }
        $workspacePrefix = [IO.Path]::GetFullPath($script:ProjectRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
        if (-not $resolved.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "$key must remain within this project's workspace in local phase 0-1." }
        Write-Output ("PASS configured $key (project-local; value omitted)")
    }
    Write-Output 'PASS environment preflight. Source network is tested separately by the throttled sampler.'
    exit 0
} catch { Write-Error ('Preflight failed: ' + $_.Exception.GetType().Name + '; ' + $_.Exception.Message); exit 1 }
