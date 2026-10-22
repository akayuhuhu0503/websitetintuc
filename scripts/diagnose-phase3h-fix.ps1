[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReportRoot)
. (Join-Path $PSScriptRoot 'dev-signing-common.ps1')
. (Join-Path $PSScriptRoot 'test-profile.ps1')
$fix=Get-Content .local/phase3h-fix-current.json -Raw -Encoding UTF8|ConvertFrom-Json
$out=Resolve-SigningPath $ReportRoot '.local/evidence/phase3f'
if(Test-Path $out){throw 'Unique diagnostic directory required'};[void][IO.Directory]::CreateDirectory($out)
if((Get-SigningSha $fix.cli) -cne $fix.cliSha -or (Get-SigningSha '.env') -cne $fix.envSha){throw 'Exact reviewed baseline required'}
Initialize-ProjectTestProfile
$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false';$env:MigrationDiagnostics__Root=$out+'/trace'
$packet=(Get-Content ($fix.priorRoot+'/batches.json') -Raw -Encoding UTF8|ConvertFrom-Json).batches[0]
$plan=$fix.priorRoot+'/b01/test/preview/plan.json';$planSha=Get-SigningSha $plan;$planData=Get-Content $plan -Raw -Encoding UTF8|ConvertFrom-Json
$operation=[Guid]::NewGuid().ToString();$events=$out+'/observer.jsonl';$utf8=[Text.UTF8Encoding]::new($false)
function Event($data){[IO.File]::AppendAllText($events,($data|ConvertTo-Json -Depth 14 -Compress)+[Environment]::NewLine,$utf8)}
function Rows($rd){$rows=New-Object Collections.Generic.List[object];while($rd.Read()){$row=[ordered]@{};for($i=0;$i -lt $rd.FieldCount;$i++){$v=$rd.GetValue($i);if($v -is [DBNull]){$v=$null};$row[$rd.GetName($i)]=$v};$rows.Add($row)};return ,$rows.ToArray()}
$builder=Get-ProjectConnectionBuilder 'LandPortal_Test';$builder['Application Name']='LandPortal.Phase3HFix.Observer';$builder['Connect Timeout']=5
$observer=New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
$lease=New-Object Threading.Mutex -ArgumentList $false,'Local\LandPortal_SQL_Test_Run';$held=$false;$process=$null;$summary=[ordered]@{operationId=$operation;batchId=$planData.BatchId;planSha256=$planSha;startedUtc=[DateTimeOffset]::UtcNow.ToString('O');reproductionCount=1;writesIssued=0;observerErrors=0}
try{
 try{$held=$lease.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true};if(-not $held){throw 'SQLTest in use'}
 $observer.Open();$cmd=$observer.CreateCommand();$cmd.CommandTimeout=5;$cmd.CommandText='SELECT DB_NAME() databaseName,CAST(SERVERPROPERTY(''ServerName'') AS nvarchar(200)) serverName,@@SPID observerSession,@@TRANCOUNT ownTransactions';$rd=$cmd.ExecuteReader();try{$identity=Rows $rd}finally{$rd.Dispose()}
 if($identity[0].databaseName -cne 'LandPortal_Test' -or $identity[0].serverName -ine 'MinhAnh\SQLEXPRESS'){throw 'Wrong Test instance/database'};Event @{utc=[DateTimeOffset]::UtcNow.ToString('O');operationId=$operation;stage='observer-identity';rows=$identity}
 $metadata=[ordered]@{
  database='SELECT name,compatibility_level,is_auto_close_on,is_auto_shrink_on,is_read_committed_snapshot_on,state_desc FROM sys.databases WHERE database_id=DB_ID()';
  tables='SELECT OBJECT_NAME(object_id) tableName,SUM(row_count) tableRows,SUM(lob_used_page_count)*8 lobKB,SUM(reserved_page_count)*8 reservedKB FROM sys.dm_db_partition_stats WHERE index_id IN(0,1) AND object_id IN(OBJECT_ID(''dbo.AuditLogs''),OBJECT_ID(''dbo.Contents''),OBJECT_ID(''dbo.Revisions''),OBJECT_ID(''dbo.SourceRecords''),OBJECT_ID(''dbo.MediaAssets'')) GROUP BY object_id';
  memory='SELECT physical_memory_in_use_kb,process_physical_memory_low,process_virtual_memory_low FROM sys.dm_os_process_memory';
  osMemory='SELECT total_physical_memory_kb,available_physical_memory_kb,system_low_memory_signal_state,system_memory_state_desc FROM sys.dm_os_sys_memory';
  indexes='SELECT i.name,i.index_id,c.name columnName,ic.key_ordinal,ic.is_included_column FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(''dbo.AuditLogs'') ORDER BY i.index_id,ic.key_ordinal'
 }
 foreach($entry in $metadata.GetEnumerator()){
  $cmd.CommandText=$entry.Value;$clock=[Diagnostics.Stopwatch]::StartNew();try{$rd=$cmd.ExecuteReader();try{$data=Rows $rd}finally{$rd.Dispose()};Event @{utc=[DateTimeOffset]::UtcNow.ToString('O');operationId=$operation;stage=$entry.Key;elapsedMs=$clock.ElapsedMilliseconds;rows=$data}}catch{Event @{utc=[DateTimeOffset]::UtcNow.ToString('O');stage=$entry.Key;error=$_.Exception.GetBaseException().GetType().Name};throw}
 }
 $cmd.Dispose()
 $argv=@($fix.cli,'batch-preflight','--checkpoint',$packet.checkpoint,'--checkpoint-sha',$packet.checkpointSha,'--inventory',$packet.inventory,'--inventory-sha',$packet.inventorySha,'--proof-set',$packet.proofSet,'--proof-set-sha',$packet.proofSetSha,'--plan',$plan,'--plan-sha',$planSha,'--report',($out+'/preflight'))
 $start=New-Object Diagnostics.ProcessStartInfo;$start.FileName=$script:DotnetPath;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.WorkingDirectory=$script:ProjectRoot
 $start.Arguments=(@($argv|ForEach-Object{'"'+([string]$_).Replace('"','\"')+'"'}) -join ' ')
 $process=New-Object Diagnostics.Process;$process.StartInfo=$start;[void]$process.Start();$summary.cliPid=$process.Id
 $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$watch=[Diagnostics.Stopwatch]::StartNew();$poll=0
 while(-not $process.HasExited -and $watch.Elapsed.TotalSeconds -lt 90){
  Start-Sleep -Milliseconds 500;$poll++;$query=$observer.CreateCommand();$query.CommandTimeout=2
  $query.CommandText='SELECT s.session_id,s.host_process_id,s.status sessionStatus,s.open_transaction_count sessionTransactions,r.request_id,r.status requestStatus,r.command,r.blocking_session_id,r.wait_type,r.wait_time,r.total_elapsed_time,r.cpu_time,r.reads,r.logical_reads,r.row_count,r.open_transaction_count requestTransactions,r.granted_query_memory,CONVERT(varchar(18),r.query_hash,1) queryHash,c.connection_id,c.connect_time,st.transaction_id,t.transaction_begin_time,t.transaction_state FROM sys.dm_exec_sessions s LEFT JOIN sys.dm_exec_requests r ON r.session_id=s.session_id LEFT JOIN sys.dm_exec_connections c ON c.session_id=s.session_id LEFT JOIN sys.dm_tran_session_transactions st ON st.session_id=s.session_id LEFT JOIN sys.dm_tran_active_transactions t ON t.transaction_id=st.transaction_id WHERE s.is_user_process=1 AND s.host_process_id=@pid AND (s.database_id=DB_ID() OR r.database_id=DB_ID())';[void]$query.Parameters.AddWithValue('@pid',$process.Id)
  try{$rd=$query.ExecuteReader();try{$rows=Rows $rd}finally{$rd.Dispose()};Event @{utc=[DateTimeOffset]::UtcNow.ToString('O');operationId=$operation;batchId=$planData.BatchId;stage='own-cli-session';elapsedMs=$watch.ElapsedMilliseconds;rows=$rows}}catch{$summary.observerErrors++;Event @{utc=[DateTimeOffset]::UtcNow.ToString('O');stage='observer-failed';error=$_.Exception.GetBaseException().GetType().Name};break}finally{$query.Dispose()}
 }
 if(-not $process.HasExited){if(-not $process.WaitForExit(5000)){$summary.ownDiagnosticCancelled=$true;$process.Kill();$process.WaitForExit()}}
 [IO.File]::WriteAllText(($out+'/stdout.log'),$stdout.GetAwaiter().GetResult(),$utf8);[IO.File]::WriteAllText(($out+'/stderr.log'),$stderr.GetAwaiter().GetResult(),$utf8)
 $summary.exitCode=$process.ExitCode;$summary.elapsedMs=$watch.ElapsedMilliseconds;$summary.polls=$poll;$summary.connectionPooling='Existing runtime policy false; observer provider separate; unchanged';$summary.sqlTimeoutSeconds='Existing CLI command30/connect default; observer command2/metadata5 reduced, not increased'
 Write-Output ('Controlled Test preflight exit='+$process.ExitCode+' elapsedMs='+$watch.ElapsedMilliseconds)
}catch{$summary.failure=$_.Exception.GetBaseException().GetType().Name;Event @{utc=[DateTimeOffset]::UtcNow.ToString('O');operationId=$operation;stage='diagnostic-stop';error=$summary.failure};throw}
finally{$summary.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O');Write-SigningJson ($ReportRoot+'/summary.json') $summary;if($process){$process.Dispose()};$observer.Dispose();if($held){$lease.ReleaseMutex()};$lease.Dispose()}
