[CmdletBinding()]
param([ValidateSet('LandPortal_Test')][string]$Database='LandPortal_Test')
. (Join-Path $PSScriptRoot 'common.ps1')
$conn=New-Object System.Data.SqlClient.SqlConnection (Get-ProjectConnectionBuilder $Database).ConnectionString
$conn.Open()
try {
    $cmd=$conn.CreateCommand();$cmd.CommandText="SELECT DB_NAME() DatabaseName,CAST(SERVERPROPERTY('MachineName') AS nvarchar(200)) MachineName,CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(100)) Version,CAST(DATABASEPROPERTYEX(DB_NAME(),'Collation') AS nvarchar(100)) Collation,CAST(FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') AS int) FullTextInstalled,(SELECT compatibility_level FROM sys.databases WHERE name=DB_NAME()) Compatibility,(SELECT COUNT(*) FROM dbo.Contents) Contents,(SELECT COUNT(*) FROM dbo.RevisionSearchProjection) Projections";$r=$cmd.ExecuteReader();$r.Read()|Out-Null
    $facts=[ordered]@{};for($i=0;$i -lt $r.FieldCount;$i++){$facts[$r.GetName($i)]=$r.GetValue($i)};$r.Dispose();$cmd.Dispose()
    if($facts.DatabaseName -cne $Database -or $facts.MachineName -ine $env:COMPUTERNAME){throw 'Search probe must target this local project Test.'}
    $samples=@()
    $sql="SELECT COUNT(*) FROM dbo.Contents c JOIN dbo.RevisionSearchProjection p ON p.RevisionId=c.PublishedRevisionId WHERE c.IsPublic=1 AND c.IsTrashed=0 AND CHARINDEX(@term,p.Text)>0"
    for($i=0;$i -lt 5;$i++){$cmd=$conn.CreateCommand();$cmd.CommandText=$sql;[void]$cmd.Parameters.AddWithValue('@term','dat dai');$sw=[Diagnostics.Stopwatch]::StartNew();$found=$cmd.ExecuteScalar();$sw.Stop();$samples+=[ordered]@{run=$i+1;matched=$found;elapsedMs=$sw.Elapsed.TotalMilliseconds};$cmd.Dispose()}
    $cmd=$conn.CreateCommand();$cmd.CommandText='SET SHOWPLAN_XML ON';$cmd.ExecuteNonQuery()|Out-Null;$cmd.Dispose()
    $cmd=$conn.CreateCommand();$cmd.CommandText=$sql;[void]$cmd.Parameters.AddWithValue('@term','dat dai');$plan=$cmd.ExecuteScalar();$cmd.Dispose()
    $cmd=$conn.CreateCommand();$cmd.CommandText='SET SHOWPLAN_XML OFF';$cmd.ExecuteNonQuery()|Out-Null;$cmd.Dispose()
    $dir=Join-Path $script:ProjectRoot '.local/evidence/phase2b';[IO.Directory]::CreateDirectory($dir)|Out-Null
    [IO.File]::WriteAllText((Join-Path $dir 'search-plan.xml'),[string]$plan,[Text.UTF8Encoding]::new($false))
    $record=[ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('O');facts=$facts;query='normalized substring CHARINDEX parameter; published pointer and public state joined';scope='HTML text and typed metadata, no PDF content/OCR';applicationCache='none; SQL/cache cold not forcibly flushed';samples=$samples;planFile='search-plan.xml';limitation='Small existing Test corpus proves functionality only. Substring scans remain; number index is not claimed to optimize arbitrary substring. No heavy benchmark.'}
    [IO.File]::WriteAllText((Join-Path $dir 'search-probe.json'),(ConvertTo-Json $record -Depth 8),[Text.UTF8Encoding]::new($false))
    Write-Output ('Search probe SQL Test: Contents='+$facts.Contents+', projections='+$facts.Projections+', collation='+$facts.Collation+', FullTextInstalled='+$facts.FullTextInstalled+'. Recorded five timings and actual plan; not a production load benchmark.')
}finally{$conn.Dispose()}
