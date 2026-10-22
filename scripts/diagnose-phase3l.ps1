[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReportRoot,[Parameter(Mandatory=$true)][string]$Inventory,[Parameter(Mandatory=$true)][string]$InventorySha)
. (Join-Path $PSScriptRoot 'dev-signing-common.ps1')
$ErrorActionPreference='Stop'
$s=Get-Content .local/phase3l-current.json -Raw -Encoding UTF8|ConvertFrom-Json
$out=Resolve-SigningPath $ReportRoot '.local/evidence/phase3f'
if(Test-Path -LiteralPath $out){throw 'Unique diagnostic root required'}
if((Get-SigningSha $s.cli) -cne $s.cliSha -or (Get-SigningSha '.env') -cne $s.envSha){throw 'Reviewed executable/environment differs'}
$inv=Resolve-SigningPath $Inventory '.local/evidence/phase3c'
if((Get-SigningSha $inv) -cne $InventorySha){throw 'Inventory hash differs'}
[void][IO.Directory]::CreateDirectory($out)
$builder=Get-ProjectConnectionBuilder LandPortal_Test
$env:ConnectionStrings__Portal=$builder.ConnectionString;$env:Sql__ExpectedDatabase='LandPortal_Test'
foreach($name in @('MediaRoot','SnapshotRoot','TempRoot','KeysRoot')){
 $folder=switch($name){'MediaRoot'{'media'} 'SnapshotRoot'{'snapshots'} 'TempRoot'{'temp'} 'KeysRoot'{'keys'}}
 $path=Join-Path $script:ProjectRoot ('.local/tests/phase2a/'+$folder)
 if(-not(Test-Path -LiteralPath $path -PathType Container)){throw 'Existing Test stores required; no copying fixtures'}
 [Environment]::SetEnvironmentVariable(('Storage__'+$name),$path,'Process')
}
$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false';$env:MigrationDiagnostics__Root=$out+'/trace'
$utf8=[Text.UTF8Encoding]::new($false)
function Event($x){[IO.File]::AppendAllText(($out+'/observer.jsonl'),($x|ConvertTo-Json -Depth 12 -Compress)+[Environment]::NewLine,$utf8)}
function Rows($rd){$list=New-Object Collections.Generic.List[object];while($rd.Read()){$row=[ordered]@{};for($i=0;$i -lt $rd.FieldCount;$i++){$v=$rd.GetValue($i);if($v -is [DBNull]){$v=$null};$row[$rd.GetName($i)]=$v};$list.Add($row)};return ,$list.ToArray()}
$builder=Get-ProjectConnectionBuilder LandPortal_Test
$builder['Initial Catalog']='master';$builder['Application Name']='LandPortal.Phase3L.Observer';$builder['Connect Timeout']=5
$observer=New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
$lease=New-Object Threading.Mutex -ArgumentList $false,'Local\LandPortal_SQL_Test_Run';$held=$false;$process=$null
$summary=@{status='IN_PROGRESS';reproductions=1;sqlWrites=0;observerCatalog='master metadata only; does not open/warm Test before CLI';startedUtc=[DateTimeOffset]::UtcNow.ToString('O');observerErrors=0}
try{
 try{$held=$lease.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true};if(-not $held){throw 'SQL Test busy'}
 $observer.Open();$cmd=$observer.CreateCommand();$cmd.CommandTimeout=5
 $cmd.CommandText="SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(200)) serverName,@@SPID observerSession,name,state_desc,is_auto_close_on,compatibility_level FROM sys.databases WHERE name=N'LandPortal_Test'"
 $rd=$cmd.ExecuteReader();try{$identity=Rows $rd}finally{$rd.Dispose()};if($identity[0].serverName -ine 'MinhAnh\SQLEXPRESS'){throw 'Unexpected instance'}
 Event @{stage='identity';utc=[DateTimeOffset]::UtcNow.ToString('O');rows=$identity};$cmd.Dispose()
 $argsList=@($s.cli,'recovery-sql-check','--inventory',$inv,'--inventory-sha',$InventorySha,'--report',('.local/evidence/phase3c/'+$s.runId+'/'+(Split-Path $out -Leaf)))
 $start=New-Object Diagnostics.ProcessStartInfo;$start.FileName=$script:DotnetPath;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.WorkingDirectory=$script:ProjectRoot
 $start.Arguments=(@($argsList|ForEach-Object{'"'+([string]$_).Replace('"','\"')+'"'}) -join ' ')
 $process=New-Object Diagnostics.Process;$process.StartInfo=$start;[void]$process.Start();$summary.cliPid=$process.Id
 $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$clock=[Diagnostics.Stopwatch]::StartNew();$poll=0;$observerDisabled=$false
 while(-not $process.HasExited -and $clock.Elapsed.TotalSeconds -lt 75){
  Start-Sleep -Milliseconds 400;if($observerDisabled){continue};$poll++;$q=$observer.CreateCommand();$q.CommandTimeout=2
  $q.CommandText=@'
SELECT s.session_id,s.host_process_id,s.status sessionStatus,s.open_transaction_count sessionTransactions,r.status requestStatus,r.command,r.blocking_session_id,r.wait_type,r.last_wait_type,r.wait_resource,r.wait_time,r.total_elapsed_time,r.cpu_time,r.reads,r.logical_reads,r.row_count,r.open_transaction_count requestTransactions,CONVERT(varchar(18),r.query_hash,1) queryHash,c.connection_id,st.transaction_id,t.transaction_begin_time,t.transaction_state
FROM sys.dm_exec_sessions s LEFT JOIN sys.dm_exec_requests r ON r.session_id=s.session_id LEFT JOIN sys.dm_exec_connections c ON c.session_id=s.session_id LEFT JOIN sys.dm_tran_session_transactions st ON st.session_id=s.session_id LEFT JOIN sys.dm_tran_active_transactions t ON t.transaction_id=st.transaction_id
WHERE s.is_user_process=1 AND s.host_process_id=@pid;
'@
  [void]$q.Parameters.AddWithValue('@pid',$process.Id)
  try{$rd=$q.ExecuteReader();try{$data=Rows $rd}finally{$rd.Dispose()};Event @{stage='own-cli';utc=[DateTimeOffset]::UtcNow.ToString('O');elapsedMs=$clock.ElapsedMilliseconds;rows=$data}}catch{$summary.observerErrors++;$observerDisabled=$true;Event @{stage='observer-failure';error=$_.Exception.GetBaseException().GetType().Name;childNotCancelled=$true}}finally{$q.Dispose()}
 }
 if(-not $process.HasExited){if(-not $process.WaitForExit(5000)){$summary.ownProcessCancelled=$true;$process.Kill();$process.WaitForExit()}}
 [IO.File]::WriteAllText(($out+'/stdout.log'),$stdout.GetAwaiter().GetResult(),$utf8);[IO.File]::WriteAllText(($out+'/stderr.log'),$stderr.GetAwaiter().GetResult(),$utf8)
 $summary.exitCode=$process.ExitCode;$summary.elapsedMs=$clock.ElapsedMilliseconds;$summary.polls=$poll;$summary.status=if($process.ExitCode -eq 0){'SQL_READINESS_PASS_NOT_PROOF_APPROVAL'}else{'FAIL_STOP_RECONCILE'}
 if($observer.State -eq [Data.ConnectionState]::Open){
  $meta=$observer.CreateCommand();$meta.CommandTimeout=5;$meta.CommandText='SELECT physical_memory_in_use_kb,process_physical_memory_low,process_virtual_memory_low FROM sys.dm_os_process_memory'
  try{$rd=$meta.ExecuteReader();try{Event @{stage='sql-process-memory';rows=Rows $rd}}finally{$rd.Dispose()}}catch{Event @{stage='post-metadata-unavailable';error=$_.Exception.GetBaseException().GetType().Name}}finally{$meta.Dispose()}
 }
 Write-Output ($summary.status+'; elapsed='+$summary.elapsedMs+'ms')
}catch{$summary.status='FAIL_STOP_RECONCILE';$summary.error=$_.Exception.GetBaseException().GetType().Name;throw}
finally{$summary.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O');Write-SigningJson ($ReportRoot+'/summary.json') $summary;if($process){$process.Dispose()};$observer.Dispose();if($held){$lease.ReleaseMutex()};$lease.Dispose()}
