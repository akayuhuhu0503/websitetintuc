# Dot-source only from an authorized DBA session. No stored DBA credential.
function Invoke-GiayHuyProvision {
 param([System.Data.SqlClient.SqlConnection]$Connection,[string]$PrivateRoot,[string]$ViewPath,[string]$ExpectedViewSha)
 $ErrorActionPreference='Stop'
 if(Test-Path -LiteralPath (Join-Path $PrivateRoot 'source-runtime.private.json')){throw 'Existing private runtime file preserved; reconcile and use a fresh run directory'}
 if((Get-FileHash -LiteralPath $ViewPath -Algorithm SHA256).Hash -cne $ExpectedViewSha){throw 'View hash drift'}
 $view=[IO.File]::ReadAllText($ViewPath,[Text.Encoding]::UTF8)
 if($view -notmatch '^CREATE VIEW dbo\.vw_GiayChungNhanHuyCongKhai AS' -or $view -match '(?i)\b(DROP|ALTER|INSERT|UPDATE|DELETE|EXECUTE|EXEC|GRANT|DENY|REVOKE|USE|INTO|BACKUP|RESTORE|TRUNCATE|OPENROWSET|OPENQUERY|OPENDATASOURCE)\b' -or $view.Substring('CREATE VIEW dbo.vw_GiayChungNhanHuyCongKhai AS'.Length) -match '(?i)\bCREATE\b'){throw 'Unexpected view definition scope'}
 function Scalar([string]$sql){$c=$Connection.CreateCommand();$c.CommandTimeout=15;$c.CommandText=$sql;try{return $c.ExecuteScalar()}finally{$c.Dispose()}}
 function Exec([string]$sql,[System.Data.SqlClient.SqlTransaction]$tx){$c=$Connection.CreateCommand();$c.Transaction=$tx;$c.CommandTimeout=15;$c.CommandText=$sql;try{[void]$c.ExecuteNonQuery()}finally{$c.Dispose()}}
 $target=Scalar "SELECT CASE WHEN CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))=N'WIN-S1IBJB84OOO' AND DB_NAME()=N'QuanLyDuAnTrungTamDuLieuDatDai' AND IS_SRVROLEMEMBER('sysadmin')=1 AND @@TRANCOUNT=0 THEN 1 ELSE 0 END"
 if([int]$target -ne 1){throw 'Wrong source/authority/transaction state'}
 if([int](Scalar "SELECT CASE WHEN OBJECT_ID(N'dbo.vw_GiayChungNhanHuyCongKhai') IS NULL AND SUSER_ID(N'landportal_giayhuy_reader') IS NULL AND USER_ID(N'landportal_giayhuy_reader') IS NULL THEN 1 ELSE 0 END") -ne 1){throw 'Existing view/login/user requires reconciliation; never overwrite/reset'}
 if([int](Scalar "SELECT COUNT(1) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.GiayChungNhanHuyGiaTriChot') AND name=N'UX_GCNH_GTChot_Field' AND is_unique=1 AND is_disabled=0 AND filter_definition=N'([isDel]=(0))'") -ne 1){throw 'Final field uniqueness not verified'}
 $script:GiayHuyProvisionStage='READ_ONLY_SCHEMA_FINGERPRINT'
 $fingerprintSql="SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT t.object_id,t.name TableName,c.column_id,c.name ColumnName,c.system_type_id,c.max_length,c.is_nullable FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id WHERE t.name IN(N'GiayChungNhanHuy',N'GiayChungNhanHuyGiaTriChot',N'GiayChungNhanHuyDuLieuDoiSoat',N'GiayChungNhanHuyTaiLieu') ORDER BY t.object_id,c.column_id FOR JSON PATH)),2)"
 $before=[string](Scalar $fingerprintSql)
 $describe=$Connection.CreateCommand();$describe.CommandTimeout=15;$describe.CommandText='EXEC sys.sp_describe_first_result_set @tsql=@query;'
 [void]$describe.Parameters.Add('@query',[Data.SqlDbType]::NVarChar,-1);$describe.Parameters['@query'].Value=$view.Substring('CREATE VIEW dbo.vw_GiayChungNhanHuyCongKhai AS'.Length)
 $columns=@();$dr=$describe.ExecuteReader();while($dr.Read()){$columns+=,[string]$dr['name']};$dr.Close();$describe.Dispose()
 if(($columns -join ',') -cne 'RecordId,IssueDate,IssueDateText,DecisionNumber,DecisionContent,CertificateSerial,RegisterNumber,Owner1,Owner2,YearText,YearNumber,ProvinceKey'){throw 'View projection compilation mismatch'}
 $bytes=New-Object byte[] 36;$rng=[Security.Cryptography.RandomNumberGenerator]::Create();$rng.GetBytes($bytes);$rng.Dispose()
 $password='Aa1!'+[Convert]::ToBase64String($bytes).Replace('/','_').Replace('+','-')
 $app=New-Object System.Data.SqlClient.SqlConnectionStringBuilder
 $app['Data Source']='45.120.229.43,1433';$app['Initial Catalog']='QuanLyDuAnTrungTamDuLieuDatDai';$app['User ID']='landportal_giayhuy_reader';$app['Password']=$password
 $app['Encrypt']=$true;$app['TrustServerCertificate']=$true;$app['Connect Timeout']=4;$app['ConnectRetryCount']=0
 $config=@{ConnectionStrings=@{GiayHuyReadOnly=$app.ConnectionString};GiayHuy=@{PublicationScope='OfficialConfirmedEightFields-OwnerApproved-20261104';LanUatTlsException='OwnerApproved-20261104'}}
 [IO.File]::WriteAllText((Join-Path $PrivateRoot 'source-runtime.private.json'),($config|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
 $script:GiayHuyProvisionStage='DDL_TRANSACTION';$tx=$Connection.BeginTransaction();$committed=$false
 try{
  Exec "SET XACT_ABORT ON;SET ANSI_NULLS ON;SET QUOTED_IDENTIFIER ON;SET ANSI_PADDING ON;SET ANSI_WARNINGS ON;SET CONCAT_NULL_YIELDS_NULL ON;SET ARITHABORT ON;SET NUMERIC_ROUNDABORT OFF;" $tx
  Exec $view $tx
  Exec ("CREATE LOGIN [landportal_giayhuy_reader] WITH PASSWORD=N'"+$password.Replace("'","''")+"',CHECK_POLICY=ON,CHECK_EXPIRATION=OFF,DEFAULT_DATABASE=[QuanLyDuAnTrungTamDuLieuDatDai];") $tx
  Exec "CREATE USER [landportal_giayhuy_reader] FOR LOGIN [landportal_giayhuy_reader];GRANT SELECT ON OBJECT::dbo.vw_GiayChungNhanHuyCongKhai TO [landportal_giayhuy_reader];" $tx
  $tx.Commit();$committed=$true;$script:GiayHuyProvisionStage='COMMITTED_VERIFY'
 }catch{if(-not $committed){try{$tx.Rollback()}catch{}};throw}finally{$tx.Dispose();$password=$null}
 if([string](Scalar $fingerprintSql) -cne $before){throw 'Source table structure drift; reconcile without retry'}
 $count=[long](Scalar 'SELECT COUNT_BIG(1) FROM dbo.vw_GiayChungNhanHuyCongKhai')
 if([long](Scalar 'SELECT COUNT_BIG(1)-COUNT_BIG(DISTINCT RecordId) FROM dbo.vw_GiayChungNhanHuyCongKhai') -ne 0){throw 'View duplicates detected'}
 $reader=[System.Data.SqlClient.SqlConnection]::new($app.ConnectionString)
 try{
  $reader.Open();$c=$reader.CreateCommand();$c.CommandTimeout=10
  $c.CommandText="SELECT CASE WHEN ORIGINAL_LOGIN()=N'landportal_giayhuy_reader' AND DB_NAME()=N'QuanLyDuAnTrungTamDuLieuDatDai' AND IS_SRVROLEMEMBER('sysadmin')=0 AND IS_MEMBER('db_owner')=0 AND IS_MEMBER('db_datareader')=0 AND IS_MEMBER('db_datawriter')=0 AND IS_MEMBER('db_ddladmin')=0 AND HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CONTROL')=0 AND HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','SELECT')=0 AND HAS_PERMS_BY_NAME('dbo.vw_GiayChungNhanHuyCongKhai','OBJECT','SELECT')=1 AND HAS_PERMS_BY_NAME('dbo.vw_GiayChungNhanHuyCongKhai','OBJECT','INSERT')=0 AND HAS_PERMS_BY_NAME('dbo.vw_GiayChungNhanHuyCongKhai','OBJECT','UPDATE')=0 AND HAS_PERMS_BY_NAME('dbo.vw_GiayChungNhanHuyCongKhai','OBJECT','DELETE')=0 AND HAS_DBACCESS('website_tin_tuc')=0 THEN 1 ELSE 0 END"
  if([int]$c.ExecuteScalar() -ne 1){throw 'Runtime privileges guard failed'}
  $denials=0;foreach($table in @('GiayChungNhanHuy','GiayChungNhanHuyGiaTriChot','GiayChungNhanHuyDuLieuDoiSoat','GiayChungNhanHuyTaiLieu')){
   $c.CommandText='SELECT TOP(0) hieuLuc FROM dbo.'+$table
   try{[void]$c.ExecuteScalar();throw 'Unexpected base table read permission'}catch{$denied=$_.Exception.GetBaseException();if($denied -isnot [System.Data.SqlClient.SqlException] -or $denied.Number -ne 229){throw};$denials++}
  }
  $c.CommandText='SELECT COUNT_BIG(1) FROM dbo.vw_GiayChungNhanHuyCongKhai';if([long]$c.ExecuteScalar() -ne $count){throw 'View read count drift; reconcile'}
  $c.CommandText="SELECT COUNT(1) FROM sys.databases WHERE database_id>4 AND name<>N'QuanLyDuAnTrungTamDuLieuDatDai' AND HAS_DBACCESS(name)=1"
  if([int]$c.ExecuteScalar() -ne 0){throw 'Runtime login has access to another user database; stop activation'}
 }finally{$reader.Dispose();$app.Clear()}
 # Only metadata from other databases: verify no mapping exists outside the approved source.
 $c=$Connection.CreateCommand();$c.CommandText="SELECT name FROM sys.databases WHERE database_id>4 AND state=0 AND name<>N'QuanLyDuAnTrungTamDuLieuDatDai'";$dbs=@();$r=$c.ExecuteReader();while($r.Read()){$dbs+=,$r.GetString(0)};$r.Close();$otherMappings=0
 foreach($dbName in $dbs){$otherMappings += [int](Scalar ('SELECT COUNT(1) FROM ['+$dbName.Replace(']',']]')+'].sys.database_principals WHERE sid=SUSER_SID(N''landportal_giayhuy_reader'')'))}
 if($otherMappings -ne 0){throw 'Unexpected cross-database mapping'}
 $planCommand=$Connection.CreateCommand();$planCommand.CommandTimeout=15
 $planCommand.CommandText="SET STATISTICS XML ON;SELECT COUNT_BIG(1) OVER() Total,v.RecordId,v.IssueDateText,v.DecisionNumber,v.DecisionContent,v.CertificateSerial,v.RegisterNumber,v.Owner1,v.Owner2,v.YearText FROM dbo.vw_GiayChungNhanHuyCongKhai v WHERE v.YearNumber=2026 AND v.ProvinceKey=N'ha-noi' ORDER BY v.IssueDate DESC,v.RecordId OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY OPTION(RECOMPILE);SET STATISTICS XML OFF;"
 $timer=[Diagnostics.Stopwatch]::StartNew();$planReader=$planCommand.ExecuteReader();$planCaptured=$false
 do{while($planReader.Read()){if($planReader.FieldCount -eq 1 -and $planReader.GetDataTypeName(0) -eq 'xml'){[IO.File]::WriteAllText((Join-Path $PrivateRoot 'default-query-actual-plan.sqlplan'),$planReader.GetSqlXml(0).Value,[Text.UTF8Encoding]::new($false));$planCaptured=$true}}}while($planReader.NextResult())
 $planReader.Close();$timer.Stop();$planCommand.Dispose()
 if(-not $planCaptured){throw 'Actual plan was not captured'}
 $receipt=@{status='VIEW_LOGIN_VERIFIED';server='WIN-S1IBJB84OOO';database='QuanLyDuAnTrungTamDuLieuDatDai';viewSha256=$ExpectedViewSha;officialCount=$count;directTableDenials=$denials;otherUserDbMappings=$otherMappings;sourceTableSchemaUnchanged=$true;businessDmlExecuted=$false;actualPlanCaptured=$planCaptured;defaultPlanWallMs=$timer.Elapsed.TotalMilliseconds;utc=[DateTimeOffset]::UtcNow.ToString('O');approvalReference='Owner attachment ffd9e929-bbee-49e5-8d86-297e4ec9eefe / 2026-11-04'}
 [IO.File]::WriteAllText((Join-Path $PrivateRoot 'provision-receipt.json'),($receipt|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
}
