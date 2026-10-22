[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReportRoot)
. (Join-Path $PSScriptRoot 'common.ps1')
$folder=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $ReportRoot));$prefix=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot '.local/evidence/phase3e'))+'\'
if(-not $folder.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path $folder)){throw 'New private Phase3E report required.'}
[IO.Directory]::CreateDirectory($folder)|Out-Null
$utf8=[Text.UTF8Encoding]::new($false);$events=Join-Path $folder 'operations.jsonl'
function Event($value){[IO.File]::AppendAllText($events,(ConvertTo-Json $value -Depth 10 -Compress)+[Environment]::NewLine,$utf8)}
$connection=(Get-ProjectConnectionBuilder 'LandPortal_Test').ConnectionString
$a=New-Object System.Data.SqlClient.SqlConnection $connection;$b=New-Object System.Data.SqlClient.SqlConnection $connection
$result=New-Object Collections.Generic.List[object]
function ReadRows($r){$list=New-Object Collections.Generic.List[object];while($r.Read()){$row=[ordered]@{};for($j=0;$j -lt $r.FieldCount;$j++){$v=$r.GetValue($j);if($v -is [DBNull]){$v=$null};$row[$r.GetName($j)]=$v};$list.Add($row)};return ,$list.ToArray()}
try{
 $watch=[Diagnostics.Stopwatch]::StartNew();$a.Open();$b.Open();Event @{operation='connect';status='PASS';elapsedMs=$watch.ElapsedMilliseconds;utc=[DateTimeOffset]::UtcNow.ToString('O')}
 $check=$a.CreateCommand();$check.CommandText="SELECT DB_NAME(),CAST(SERVERPROPERTY('MachineName') AS nvarchar(200)),@@SPID";$r=$check.ExecuteReader();$r.Read()|Out-Null
 if($r.GetString(0) -cne 'LandPortal_Test' -or $r.GetString(1) -ine $env:COMPUTERNAME){throw 'Wrong project target.'};$sid=$r.GetInt16(2);$r.Dispose();$check.Dispose()
 $queries=[ordered]@{
  resource='SELECT physical_memory_in_use_kb,locked_page_allocations_kb,process_physical_memory_low,process_virtual_memory_low FROM sys.dm_os_process_memory';
  tables="SELECT OBJECT_NAME(object_id) AS table_name,SUM(row_count) AS rows,SUM(reserved_page_count)*8 AS reserved_kb,SUM(lob_used_page_count)*8 AS lob_kb FROM sys.dm_db_partition_stats WHERE object_id IN(OBJECT_ID('dbo.AuditLogs'),OBJECT_ID('dbo.Revisions'),OBJECT_ID('dbo.SourceRecords')) GROUP BY object_id";
  indexes="SELECT i.name,i.index_id,c.name AS column_name,ic.key_ordinal,ic.is_included_column FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID('dbo.AuditLogs') ORDER BY i.index_id,ic.key_ordinal";
  receipt="SELECT COUNT(*) AS receipts FROM dbo.AuditLogs WHERE Action=N'Phase3B.Import' AND Detail LIKE N'7768b8bf-25ee-4b6e-99e8-94b117a72694|%'";
  binding="SELECT TOP(1) Id FROM dbo.AuditLogs WHERE Action=N'Phase3C.InventoryBinding' AND Actor=N'301f013b-ff04-43d5-8f2f-460b8bfae8f8'";
  source="SELECT COUNT(*) AS contents FROM dbo.SourceRecords WHERE Source=N'qldd.mae.gov.vn' AND SourceId IN(N'3666',N'3670',N'3673')"
 }
 foreach($entry in $queries.GetEnumerator()){
  $op=[Guid]::NewGuid().ToString();$hash=[Security.Cryptography.SHA256]::Create();$fingerprint=([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($entry.Value)))).Replace('-','').ToLowerInvariant();$hash.Dispose()
  $cmd=$a.CreateCommand();$cmd.CommandTimeout=8;$cmd.CommandText=$entry.Value;$start=[DateTimeOffset]::UtcNow;$watch.Restart();Event @{operationId=$op;stage='diagnose';query=$entry.Key;fingerprint=$fingerprint;startedUtc=$start.ToString('O');timeoutSeconds=8;transaction='NONE';sqlTemplate=$entry.Value}
  $task=$cmd.ExecuteReaderAsync();$observations=@();$polls=0
  while(-not $task.IsCompleted -and $polls -lt 3){Start-Sleep -Milliseconds 1000;$polls++;if($task.IsCompleted){break};$observer=$b.CreateCommand();$observer.CommandTimeout=2
   $observer.CommandText='SELECT r.status,r.command,r.blocking_session_id,r.wait_type,r.wait_time,r.total_elapsed_time,r.cpu_time,r.reads,r.logical_reads,r.open_transaction_count,r.granted_query_memory,s.transaction_isolation_level FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id WHERE r.session_id=@sid AND r.database_id=DB_ID()';[void]$observer.Parameters.AddWithValue('@sid',$sid)
   try{$r=$observer.ExecuteReader();$o=ReadRows $r;$r.Dispose();$observations+=,$o;Event @{operationId=$op;observation=$o;elapsedMs=$watch.ElapsedMilliseconds}}catch{Event @{operationId=$op;observerError=$_.Exception.GetType().Name}}finally{$observer.Dispose()}
  }
  try{$r=$task.GetAwaiter().GetResult();$data=ReadRows $r;$r.Dispose();$status='PASS';$errorCode=$null}catch{$data=@();$status='FAIL';$errorCode=$_.Exception.GetBaseException().GetType().Name;if($_.Exception.GetBaseException() -is [System.Data.SqlClient.SqlException]){$errorCode=$_.Exception.GetBaseException().Number}}
  $watch.Stop();$cmd.Dispose();$row=[ordered]@{operationId=$op;query=$entry.Key;fingerprint=$fingerprint;startedUtc=$start.ToString('O');finishedUtc=[DateTimeOffset]::UtcNow.ToString('O');elapsedMs=$watch.ElapsedMilliseconds;status=$status;errorCode=$errorCode;transaction='NONE';rows=$data;observations=$observations};$result.Add($row);Event $row
  Write-Output ($entry.Key+' '+$status+' '+$watch.ElapsedMilliseconds+'ms error='+$errorCode)
 }
}finally{$a.Dispose();$b.Dispose();[IO.File]::WriteAllText((Join-Path $folder 'summary.json'),(ConvertTo-Json @($result.ToArray()) -Depth 14),$utf8)}
