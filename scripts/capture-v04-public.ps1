[CmdletBinding()]
param([ValidateSet('before','after')][string]$Stage='after',[ValidateRange(1024,65535)][int]$Port=5198,[switch]$IsolatedBuild)
. (Join-Path $PSScriptRoot 'common.ps1')
$directory=Join-Path $script:ProjectRoot ('.local/evidence/v04/ui/'+$Stage)
[IO.Directory]::CreateDirectory($directory)|Out-Null
if(@(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue).Count){throw 'The requested port is already owned; no process was stopped.'}
$builder=Get-ProjectConnectionBuilder 'LandPortal_Dev'
if(-not $builder.IntegratedSecurity -or $builder.DataSource -notin @('.\SQLEXPRESS','localhost\SQLEXPRESS','(local)\SQLEXPRESS')){throw 'Public UI probe requires project local SQL Dev.'}
$conn=New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
$conn.Open()
try{
 $cmd=$conn.CreateCommand();$cmd.CommandText="SELECT DB_NAME(),CAST(SERVERPROPERTY('MachineName') AS nvarchar(100))";$r=$cmd.ExecuteReader();[void]$r.Read();if($r.GetString(0) -cne 'LandPortal_Dev' -or $r.GetString(1) -ine $env:COMPUTERNAME){throw 'Wrong target'};$r.Dispose()
 $cmd.CommandText="SELECT TOP(1) c.Id,r.Title,r.Summary FROM dbo.Contents c JOIN dbo.SourceRecords s ON s.ContentId=c.Id JOIN dbo.Revisions r ON r.Id=c.PublishedRevisionId WHERE s.Source=N'qldd.mae.gov.vn' AND c.Kind=0 AND c.IsPublic=1 AND c.IsTrashed=0 ORDER BY c.SourcePublishedDate DESC,c.Id";$r=$cmd.ExecuteReader();if(-not $r.Read()){throw 'No genuine public source article'};$article=[ordered]@{id=$r.GetGuid(0).ToString();title=$r.GetString(1);summary=$r.GetString(2)};$r.Dispose()
 $cmd.CommandText="SELECT Slug FROM dbo.Categories WHERE SourceUrl<>N'' AND SortOrder<14 ORDER BY SortOrder,Id";$r=$cmd.ExecuteReader();$categories=@();while($r.Read()){$categories+=$r.GetString(0)};$r.Dispose()
 $webPath=if($IsolatedBuild){Join-Path $script:ProjectRoot '.local/build/v04/bin/Web/debug/Web.dll'}else{Join-Path $script:ProjectRoot 'src/Web/bin/Debug/net10.0/Web.dll'}
 $fixture=[ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('O');database='LandPortal_Dev';baseUrl='http://127.0.0.1:'+$Port;stage=$Stage;directory=$directory;article=$article;categories=$categories;webAssemblySha256=(Get-FileHash $webPath).Hash;mutations='NONE';allowUnscannedUpload=$false}
 [IO.File]::WriteAllText((Join-Path $directory 'fixture.json'),(ConvertTo-Json $fixture -Depth 6),[Text.UTF8Encoding]::new($false))
}finally{$conn.Dispose()}
$arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'run-dev.ps1'),'-NoBuild','-Port',$Port)
if($IsolatedBuild){$arguments+='-IsolatedBuild'}
$launcher=Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -WorkingDirectory $script:ProjectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $directory 'web.stdout.log') -RedirectStandardError (Join-Path $directory 'web.stderr.log')
$ownedIds=New-Object 'System.Collections.Generic.List[int]';$ownedIds.Add($launcher.Id)
try{
 $ready=$false;for($i=0;$i -lt 40;$i++){try{if((Invoke-WebRequest -UseBasicParsing ($fixture.baseUrl+'/ready') -TimeoutSec 2).StatusCode -eq 200){$ready=$true;break}}catch{};if($launcher.HasExited){throw 'Owned web process exited'};Start-Sleep -Milliseconds 200}
 if(-not $ready){throw 'Owned web readiness failed'}
 & rtk proxy node tests/browser/v04-public.mjs (Join-Path $directory 'fixture.json')
 if($LASTEXITCODE -ne 0){throw 'Public UI checks failed; inspect v04 stage report'}
}finally{
 for($i=0;$i -lt $ownedIds.Count;$i++){foreach($child in Get-CimInstance Win32_Process -Filter ('ParentProcessId='+$ownedIds[$i])){if(-not $ownedIds.Contains([int]$child.ProcessId)){$ownedIds.Add([int]$child.ProcessId)}}}
 for($i=$ownedIds.Count-1;$i -ge 0;$i--){$owned=Get-Process -Id $ownedIds[$i] -ErrorAction SilentlyContinue;if($null -ne $owned){Stop-Process -Id $ownedIds[$i];[void]$owned.WaitForExit(3000)}}
 $remaining=@($ownedIds|Where-Object{Get-Process -Id $_ -ErrorAction SilentlyContinue})
 [IO.File]::WriteAllText((Join-Path $directory 'cleanup.json'),(ConvertTo-Json ([ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('O');ownedIds=$ownedIds.ToArray();remainingOwnedIds=$remaining;stopped=($remaining.Count -eq 0)})),[Text.UTF8Encoding]::new($false))
}
