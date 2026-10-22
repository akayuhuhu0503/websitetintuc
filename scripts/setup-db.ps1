[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('LandPortal_Dev','LandPortal_Test')][string]$Database)
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    $builder = Get-ProjectConnectionBuilder $Database
    $builder['Initial Catalog'] = 'master'
    $connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = 'SELECT DB_ID(@database)'
        [void]$command.Parameters.AddWithValue('@database', $Database)
        $exists = $command.ExecuteScalar()
        if ($exists -is [DBNull] -or $null -eq $exists) {
            $command.Parameters.Clear()
            $command.CommandText = 'CREATE DATABASE [' + $Database + ']'
            $command.ExecuteNonQuery() | Out-Null
            Write-Output "CREATED $Database only."
        } else { Write-Output "EXISTS $Database; preserved existing data." }
    } finally { $connection.Dispose() }
    Write-Output 'No schema change performed. Run migrate.ps1 separately against this named database.'
    exit 0
} catch { Write-Error ('Database setup failed: ' + $_.Exception.GetType().Name + '. Connection details omitted.'); exit 1 }
