[CmdletBinding()]
param(
    [ValidateSet('LandPortal_Dev','LandPortal_Test')][string]$Database = 'LandPortal_Dev',
    [Parameter(Mandatory=$true)][string]$UserId,
    [Parameter(Mandatory=$true)][string]$ExpectedUserName,
    [Parameter(Mandatory=$true)][string[]]$Permission,
    [switch]$Apply
)
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    $builder = Get-ProjectConnectionBuilder $Database
    if (-not $builder.IntegratedSecurity -or $builder.DataSource -notin @('.\SQLEXPRESS','localhost\SQLEXPRESS','(local)\SQLEXPRESS')) { throw 'Only the project local SQL Express Windows-authenticated connection is accepted.' }
    $catalogFile = Join-Path $script:ProjectRoot 'src/Application/TextNormalizer.cs'
    # Expand match groups explicitly for compatibility with Windows PowerShell 5.1.
    $catalog = @([regex]::Matches([IO.File]::ReadAllText($catalogFile), 'public const string \w+\s*=\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    $requested = @($Permission | ForEach-Object { $_.Split(',') } | ForEach-Object { $_.Trim() } | Sort-Object -Unique)
    if ($requested.Count -eq 0 -or @($requested | Where-Object { $_ -cnotin $catalog }).Count -gt 0) { throw 'The requested permission is outside the application catalog.' }
    $connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = 'SELECT DB_NAME(), CONVERT(nvarchar(128),SERVERPROPERTY(''MachineName'')),UserName,IsDisabled FROM dbo.AspNetUsers WHERE Id=@id'
    [void]$command.Parameters.Add('@id',[System.Data.SqlDbType]::NVarChar,450); $command.Parameters['@id'].Value=$UserId
    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) { $reader.Close(); throw 'The exact target user ID does not exist.' }
    $actualDatabase=$reader.GetString(0); $machine=$reader.GetString(1); $actualName=$reader.GetString(2); $disabled=$reader.GetBoolean(3)
    $reader.Close()
    if ($actualDatabase -cne $Database -or $machine -ine $env:COMPUTERNAME -or $actualName -cne $ExpectedUserName -or $disabled) { throw 'Target database/machine/user-name/active-user verification failed.' }
    $command.CommandText='SELECT ClaimValue FROM dbo.AspNetUserClaims WHERE UserId=@id AND ClaimType=N''permission'''
    $reader=$command.ExecuteReader(); $before=@(); while($reader.Read()) { $before += $reader.GetString(0) }; $reader.Close()
    $missing=@($requested | Where-Object { $_ -cnotin $before })
    if (-not $Apply) {
        [pscustomobject]@{ mode='DRY_RUN';database=$Database;userId=$UserId;verifiedUserName=$actualName;requestedPermissions=$requested;permissionsToAdd=$missing;passwordChanged=$false;applied=$false } | ConvertTo-Json -Depth 5
        exit 0
    }
    $transaction=$connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
    try {
        $command.Transaction=$transaction; $command.Parameters.Clear()
        $command.CommandText="DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'LandPortal.IdentityAdministration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000; IF @result<0 THROW 51001, 'Identity administration is busy.', 1;"
        [void]$command.ExecuteNonQuery()
        $command.CommandText='SELECT UserName,IsDisabled FROM dbo.AspNetUsers WITH (UPDLOCK,HOLDLOCK) WHERE Id=@id'
        [void]$command.Parameters.Add('@id',[System.Data.SqlDbType]::NVarChar,450); $command.Parameters['@id'].Value=$UserId
        $reader=$command.ExecuteReader()
        if(-not $reader.Read()) { $reader.Close(); throw 'The target user changed.' }
        $verifiedName=$reader.GetString(0);$verifiedDisabled=$reader.GetBoolean(1);$reader.Close()
        if($verifiedName -cne $ExpectedUserName -or $verifiedDisabled) { throw 'The target user changed.' }
        $command.CommandText='SELECT ClaimValue FROM dbo.AspNetUserClaims WHERE UserId=@id AND ClaimType=N''permission'''
        $reader=$command.ExecuteReader();$before=@();while($reader.Read()){$before+=$reader.GetString(0)};$reader.Close()
        $missing=@($requested | Where-Object { $_ -cnotin $before })
        foreach($item in $missing) {
            $command.CommandText='INSERT INTO dbo.AspNetUserClaims (UserId,ClaimType,ClaimValue) VALUES (@id,N''permission'',@permission)'
            if(-not $command.Parameters.Contains('@permission')) { [void]$command.Parameters.Add('@permission',[System.Data.SqlDbType]::NVarChar,256) }
            $command.Parameters['@permission'].Value=$item;[void]$command.ExecuteNonQuery()
        }
        if($missing.Count -gt 0) {
            $command.CommandText='UPDATE dbo.AspNetUsers SET SecurityStamp=@stamp,ConcurrencyStamp=@concurrency WHERE Id=@id'
            [void]$command.Parameters.Add('@stamp',[System.Data.SqlDbType]::NVarChar,64);$command.Parameters['@stamp'].Value=[guid]::NewGuid().ToString('D')
            [void]$command.Parameters.Add('@concurrency',[System.Data.SqlDbType]::NVarChar,64);$command.Parameters['@concurrency'].Value=[guid]::NewGuid().ToString('D');[void]$command.ExecuteNonQuery()
        }
        $detail=@{database=$Database;userId=$UserId;userName=$ExpectedUserName;before=@($before|Sort-Object -Unique);after=@(($before+$requested)|Sort-Object -Unique);added=$missing;mechanism='ExplicitLocalOperatorGrant';passwordChanged=$false}|ConvertTo-Json -Depth 6 -Compress
        $command.CommandText='INSERT INTO dbo.AuditLogs (Actor,Action,ContentId,RevisionId,Detail,CreatedUtc) VALUES (@actor,N''OperatorPermissionsGranted'',NULL,NULL,@detail,SYSUTCDATETIME())'
        [void]$command.Parameters.Add('@actor',[System.Data.SqlDbType]::NVarChar,450);$command.Parameters['@actor'].Value=[System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        [void]$command.Parameters.Add('@detail',[System.Data.SqlDbType]::NVarChar,-1);$command.Parameters['@detail'].Value=$detail;[void]$command.ExecuteNonQuery()
        $transaction.Commit()
    } catch { $transaction.Rollback(); throw }
    [pscustomobject]@{mode='APPLIED';database=$Database;userId=$UserId;addedPermissions=$missing;passwordChanged=$false;audited=$true}|ConvertTo-Json -Depth 5
} catch { Write-Error ('Permission operation refused/failed: '+$_.Exception.GetType().Name+'. No secret was logged.'); exit 1 }
finally { if($null -ne (Get-Variable connection -ErrorAction SilentlyContinue) -and $connection) { $connection.Dispose() } }
