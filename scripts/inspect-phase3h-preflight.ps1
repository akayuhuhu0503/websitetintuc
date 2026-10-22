[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReportRoot)
. (Join-Path $PSScriptRoot 'dev-signing-common.ps1')
$fix=Get-Content .local/phase3h-fix-current.json -Raw -Encoding UTF8|ConvertFrom-Json
$folder=Resolve-SigningPath $ReportRoot '.local/evidence/phase3f';if(Test-Path $folder){throw 'Unique report required'};[void][IO.Directory]::CreateDirectory($folder)
$packet=(Get-Content ($fix.priorRoot+'/batches.json') -Raw -Encoding UTF8|ConvertFrom-Json).batches[0]
$inventory=Get-Content $packet.inventory -Raw -Encoding UTF8|ConvertFrom-Json
$builder=Get-ProjectConnectionBuilder 'LandPortal_Test';$builder['Application Name']='LandPortal.Phase3HFix.AuditPlan';$conn=New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
$utf8=[Text.UTF8Encoding]::new($false);$messages=New-Object Collections.Generic.List[string]
$handler=[System.Data.SqlClient.SqlInfoMessageEventHandler]{param($sender,$event) $messages.Add($event.Message)};$conn.add_InfoMessage($handler)
$watch=[Diagnostics.Stopwatch]::StartNew();$summary=[ordered]@{scope='One equivalent parameterized Test audit predicate probe, operator metadata connection. Not byte-identical EF SQL; not a timeout reproduction or production benchmark.';jobId=$inventory.JobId;startedUtc=[DateTimeOffset]::UtcNow.ToString('O');transaction='NONE'}
try{
 $conn.Open();$cmd=$conn.CreateCommand();$cmd.CommandTimeout=5;$cmd.CommandText='SELECT DB_NAME(),CAST(SERVERPROPERTY(''ServerName'') AS nvarchar(200)),@@SPID';$rd=$cmd.ExecuteReader();try{[void]$rd.Read();if($rd.GetString(0) -cne 'LandPortal_Test' -or $rd.GetString(1) -ine 'MinhAnh\SQLEXPRESS'){throw 'Wrong Test target'};$summary.spid=$rd.GetInt16(2);$summary.clientConnectionId=[string]$conn.ClientConnectionId}finally{$rd.Dispose()}
 $cmd.CommandText=@'
SET STATISTICS IO ON; SET STATISTICS XML ON;
SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE Action=N'Phase3C.InventoryBinding' AND Actor=@job AND Detail=@sha) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS bound;
SELECT TOP(181) Detail FROM dbo.AuditLogs WHERE Action=N'Phase3C.ScanProof' AND Actor=@job;
SET STATISTICS XML OFF; SET STATISTICS IO OFF;
'@
 [void]$cmd.Parameters.Add('@job',[Data.SqlDbType]::NVarChar,4000);$cmd.Parameters['@job'].Value=[string]$inventory.JobId
 [void]$cmd.Parameters.Add('@sha',[Data.SqlDbType]::NVarChar,4000);$cmd.Parameters['@sha'].Value=$packet.inventorySha
 $hash=[Security.Cryptography.SHA256]::Create();try{$summary.commandFingerprint=([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($cmd.CommandText)))).Replace('-','').ToLowerInvariant()}finally{$hash.Dispose()}
 $summary.sqlTemplate=$cmd.CommandText;$summary.parameterValuesLogged=$false;$summary.timeoutSeconds=5;$summary.resultSets=@();$summary.actualPlans=@();$index=0;$plans=0;$rd=$cmd.ExecuteReader()
 try{
  do{
   $rows=0;$isXml=$rd.FieldCount -eq 1 -and ($rd.GetDataTypeName(0) -eq 'xml' -or $rd.GetName(0) -match 'Showplan')
   while($rd.Read()){
    $rows++
    if($isXml){$plans++;$path=$folder+'/actual-plan-'+$plans+'.xml';[IO.File]::WriteAllText($path,$rd.GetString(0),$utf8);$summary.actualPlans+=@{path=$path;sha256=Get-SigningSha $path}}
    elseif($index -eq 0 -and -not $rd.GetBoolean(0)){throw 'Inventory binding missing'}
   }
   $summary.resultSets+=@{index=$index;rowsReturned=$rows;isActualPlan=$isXml};$index++
  }while($rd.NextResult())
 }finally{$rd.Dispose();$cmd.Dispose()}
 if($plans -ne 2){throw 'Both actual XML plans must be captured; result counts alone are insufficient'}
 $summary.status='PASS';$summary.elapsedMs=$watch.ElapsedMilliseconds;$summary.statisticsIO=$messages.ToArray();Write-Output ('Actual audit probe PASS '+$watch.ElapsedMilliseconds+'ms; plans='+$plans)
}catch{$summary.status='FAIL';$summary.error=$_.Exception.GetBaseException().GetType().Name;throw}
finally{$conn.remove_InfoMessage($handler);$conn.Dispose();$summary.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O');Write-SigningJson ($ReportRoot+'/summary.json') $summary}
