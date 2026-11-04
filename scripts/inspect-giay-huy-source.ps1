[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference='Stop'
# DBA survey only. The website runtime must never call this script or use this credential.
if(-not [IO.Path]::IsPathRooted($OutputPath) -or (Test-Path -LiteralPath $OutputPath)){throw 'Use a new absolute private output path.'}
$parent=Split-Path -Parent $OutputPath
if(-not(Test-Path -LiteralPath $parent -PathType Container)){throw 'Private output parent must already exist.'}
$credential=Get-Credential -Message 'READ ONLY: inspect GiayHuy source on WIN-S1IBJB84OOO/QuanLyDuAnTrungTamDuLieuDatDai. No SQL writes.'
if($null -eq $credential){throw 'Credential entry cancelled; no SQL connection.'}
$connection=$null;$builder=$null;$stage='CONNECTION_CONFIGURATION'
try {
 $builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder
 # SqlConnectionStringBuilder is IDictionary: PS5 property adaptation treats DataSource as a keyword.
 # Use canonical SQL keywords rather than the PowerShell dictionary property adapter.
 $builder['Data Source']='45.120.229.43,1433'
 $builder['Initial Catalog']='QuanLyDuAnTrungTamDuLieuDatDai'
 $builder['User ID']=$credential.UserName
 $builder['Password']=$credential.GetNetworkCredential().Password
 $builder['Encrypt']=$true;$builder['TrustServerCertificate']=$false
 $builder['Connect Timeout']=5;$builder['ConnectRetryCount']=0
 $builder['Application Name']='LandPortal.GiayHuy.DBA.ReadOnlySurvey'
 $connection=[System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
 $stage='OPEN_CONNECTION'
 $connection.Open()
 $stage='TARGET_GUARD';$guard=$connection.CreateCommand();$guard.CommandTimeout=5
 $guard.CommandText="SELECT CASE WHEN CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))=N'WIN-S1IBJB84OOO' AND DB_NAME()=N'QuanLyDuAnTrungTamDuLieuDatDai' THEN 1 ELSE 0 END;"
 if([int]$guard.ExecuteScalar() -ne 1){throw 'Source target mismatch; no further query.'}
 $stage='METADATA_AND_ELIGIBLE_COUNT';$command=$connection.CreateCommand();$command.CommandTimeout=10
 $command.CommandText=@'
SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) PhysicalServer,DB_NAME() DatabaseName,
 CONVERT(nvarchar(30),SERVERPROPERTY('ProductVersion')) ProductVersion;
SELECT COUNT_BIG(1) OfficialEligibleCount FROM dbo.GiayChungNhanHuy g
 WHERE g.hieuLuc=1 AND g.isDel=0 AND EXISTS (
  SELECT 1 FROM dbo.GiayChungNhanHuyDuLieuDoiSoat d
  INNER JOIN dbo.GiayChungNhanHuyTaiLieu t ON t.taiLieuID=d.taiLieuID
    AND t.hieuLuc=1 AND t.isDel=0 AND ISNULL(t.trangThaiDoiSoat,N'')<>N'LOAI_BO'
  WHERE d.giayChungNhanHuyID=g.giayChungNhanHuyID AND d.trangThaiXacNhan=N'DA_XAC_NHAN'
    AND d.hieuLuc=1 AND d.isDel=0
    AND (g.nguonDuLieuDoiSoatID IS NULL OR d.duLieuDoiSoatID=g.nguonDuLieuDoiSoatID));
SELECT s.name SchemaName,t.name TableName,c.name ColumnName,ty.name TypeName,c.max_length,c.is_nullable
 FROM sys.tables t INNER JOIN sys.schemas s ON s.schema_id=t.schema_id
 INNER JOIN sys.columns c ON c.object_id=t.object_id
 INNER JOIN sys.types ty ON ty.user_type_id=c.user_type_id
 WHERE s.name=N'dbo' AND t.name IN (N'GiayChungNhanHuy',N'GiayChungNhanHuyGiaTriChot',N'GiayChungNhanHuyDuLieuDoiSoat',N'GiayChungNhanHuyTaiLieu')
 ORDER BY t.name,c.column_id;
'@
 $adapter=New-Object System.Data.SqlClient.SqlDataAdapter($command)
 $data=New-Object System.Data.DataSet
 [void]$adapter.Fill($data)
 $sets=@();foreach($table in $data.Tables){$rows=@();foreach($row in $table.Rows){$item=[ordered]@{};foreach($column in $table.Columns){$value=$row[$column];if($value -is [DBNull]){$value=$null};$item[$column.ColumnName]=$value};$rows+=,$item};$sets+=,@($rows)}
 $result=[ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('O');status='READ_ONLY_SURVEY_PASS';sqlWrites=$false;tlsValidation='STRICT';results=$sets}
 [IO.File]::WriteAllText($OutputPath,($result|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
 Write-Output 'READ_ONLY_SURVEY_PASS; metadata saved to requested private output.'
}catch{
 $problem=$_.Exception.GetBaseException();$number=$null;if($problem -is [System.Data.SqlClient.SqlException]){$number=$problem.Number}
 $native=$null;if($problem -is [ComponentModel.Win32Exception]){$native=$problem.NativeErrorCode}
 $result=[ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('O');status='BLOCKED';stage=$stage;sqlWrites=$false;sqlNumber=$number;nativeErrorCode=$native;tlsValidation='STRICT';errorType=$problem.GetType().Name;scriptLine=$_.InvocationInfo.ScriptLineNumber}
 [IO.File]::WriteAllText($OutputPath,($result|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
 Write-Output 'READ_ONLY_SURVEY_BLOCKED; no automatic retry or TLS bypass. Inspect safe metadata report.'
}finally{if($connection){$connection.Dispose()};if($builder){$builder.Clear()};$credential=$null}
