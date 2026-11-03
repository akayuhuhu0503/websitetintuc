[CmdletBinding()]
param([switch]$ConnectReadOnly,[string]$ReportFile,[Management.Automation.PSCredential]$DbaCredential)
# SELECT-only metadata inspection. Never activates/provisions a role or runs a migration.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $ConnectReadOnly){Write-Output 'PLAN: SELECT-only exact WIN-S1IBJB84OOO/website_tin_tuc metadata; no connection.';return}
if(-not $ReportFile){throw 'New private report required'}
$report=[IO.Path]::GetFullPath($ReportFile)
$private=[IO.Path]::GetFullPath((Join-Path $env:USERPROFILE 'LandPortal-Private')).TrimEnd('\')+'\'
if(-not $report.StartsWith($private,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $report)){throw 'Report must be new beneath current-user LandPortal-Private'}
for($p=$report;$p;$p=[IO.Path]::GetDirectoryName($p)){if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse path refused'}}
$security=New-Object Security.AccessControl.FileSecurity;$security.SetAccessRuleProtection($true,$false)
foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'))){$security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','Allow')))}
$stream=[IO.File]::Create($report,4096,[IO.FileOptions]::None,$security)
$writer=New-Object IO.StreamWriter($stream,([Text.UTF8Encoding]::new($false)));$writer.AutoFlush=$true
function Audit($v){$v.utc=[DateTimeOffset]::UtcNow.ToString('O');$writer.WriteLine(($v|ConvertTo-Json -Depth 6 -Compress))}
$connection=$null;$secret=$null
try{
 Audit @{stage='WAITING_FOR_DBA';readOnly=$true}
 if(-not $DbaCredential){$DbaCredential=Get-Credential -Message 'READ-ONLY metadata inspection: website_tin_tuc. No backup, role/grant or migration will run.'}
 if(-not $DbaCredential){throw 'Credential cancelled'}
 $b=New-Object Data.SqlClient.SqlConnectionStringBuilder
 $b['Data Source']='45.120.229.43,1433';$b['Initial Catalog']='website_tin_tuc';$b['Encrypt']=$true;$b['TrustServerCertificate']=$true;$b['Pooling']=$false;$b['Connect Timeout']=15
 $secret=$DbaCredential.Password.Copy();$secret.MakeReadOnly()
 $credential=New-Object Data.SqlClient.SqlCredential($DbaCredential.UserName,$secret)
 $connection=New-Object Data.SqlClient.SqlConnection($b.ConnectionString,$credential)
 $connection.Open()
 function Rows([string]$sql){
  $q=$connection.CreateCommand();$q.CommandTimeout=20;$q.CommandText=$sql
  try{$r=$q.ExecuteReader();try{$list=New-Object 'System.Collections.Generic.List[object]';while($r.Read()){$row=[ordered]@{};for($i=0;$i -lt $r.FieldCount;$i++){$row[$r.GetName($i)]=if($r.IsDBNull($i)){$null}else{$r.GetValue($i)}};$list.Add([pscustomobject]$row)};return $list.ToArray()}finally{$r.Dispose()}}finally{$q.Dispose()}
 }
 $target=@(Rows "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) AS ServerName,DB_NAME() AS DatabaseName,HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION') AS FullMetadata;")[0]
 if($target.ServerName -cne 'WIN-S1IBJB84OOO' -or $target.DatabaseName -cne 'website_tin_tuc' -or $target.FullMetadata -ne 1){throw 'Exact target and full metadata visibility required'}
 $tables=@(Rows "SELECT SCHEMA_NAME(schema_id)+N'.'+name AS Name FROM sys.tables WHERE is_ms_shipped=0 ORDER BY Name;")
 $history=@(Rows 'SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;')
 $email=@(Rows "SELECT t.name AS TableName,TYPE_NAME(c.system_type_id) AS TypeName,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.tables t ON c.object_id=t.object_id WHERE t.schema_id=SCHEMA_ID(N'dbo') AND t.name IN(N'DraftMetadata',N'RevisionMetadata') AND c.name=N'ConsultationEmail';")
 $role=@(Rows "SELECT name,type_desc FROM sys.database_principals WHERE name=N'LandPortalRuntime';")
 $app=@(Rows "SELECT name,type_desc,CASE WHEN sid=SUSER_SID(N'website_tin_tuc_app') THEN 1 ELSE 0 END AS LoginSidMatches FROM sys.database_principals WHERE name=N'website_tin_tuc_app';")
 $memberships=@(Rows "SELECT p.name AS RoleName FROM sys.database_role_members r JOIN sys.database_principals p ON p.principal_id=r.role_principal_id WHERE r.member_principal_id=DATABASE_PRINCIPAL_ID(N'website_tin_tuc_app');")
 $permissions=@(Rows "SELECT p.name AS PrincipalName,g.class_desc,g.permission_name,g.state_desc FROM sys.database_permissions g JOIN sys.database_principals p ON p.principal_id=g.grantee_principal_id WHERE p.name IN(N'website_tin_tuc_app',N'LandPortalRuntime');")
 $manifest=Join-Path (Split-Path $PSScriptRoot -Parent) '.local/publish/fr06-closure-final-20261102/manifest.json'
 if((Get-FileHash $manifest -Algorithm SHA256).Hash -ine '13594b2be96f46b7bd517b5160572800ab63ee4d93ce26ce64b4edf7df8d9a8e'){throw 'Reviewed final schema manifest differs'}
 $expected=Get-Content $manifest -Raw -Encoding UTF8|ConvertFrom-Json
 $schemaPass=(@(Compare-Object @($expected.expectedTables|Sort-Object) @($tables.Name|Sort-Object)).Count -eq 0 -and @(Compare-Object @($expected.expectedMigrations|Sort-Object) @($history.MigrationId|Sort-Object)).Count -eq 0 -and $email.Count -eq 2 -and @($email|Where-Object {$_.TypeName -cne 'nvarchar' -or $_.max_length -ne -1 -or -not $_.is_nullable}).Count -eq 0)
 Audit @{stage='READ_ONLY_METADATA_COMPLETE';target=$target;tables=$tables.Count;migrations=$history.Count;latestMigration=$history[-1].MigrationId;schemaMatchesReviewed17=$schemaPass;emailColumns=$email;runtimeRole=$role;appMapping=$app;appMemberships=$memberships;permissions=$permissions;sqlWrites=$false}
 Write-Output ('READ_ONLY_METADATA_COMPLETE; private report='+$report)
}catch{
 $number=$null;for($e=$_.Exception;$e;$e=$e.InnerException){if($e -is [Data.SqlClient.SqlException]){$number=$e.Number;break}}
 Audit @{stage='BLOCKED';sqlNumber=$number;sqlWrites=$false}
 Write-Output 'Read-only inspection blocked; consult sanitized private report.'
}finally{if($connection){$connection.Dispose()};if($secret){$secret.Dispose()};$DbaCredential=$null;$writer.Dispose()}
