[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$TargetFile,[Parameter(Mandatory=$true)][string]$PublishRoot,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'uat-common.ps1')
$code=Get-UatSafePath $PublishRoot;$output=Get-UatSafePath $OutputDirectory
$t=Get-Content -LiteralPath $TargetFile -Raw -Encoding UTF8|ConvertFrom-Json
Assert-UatDatabaseName ([string]$t.Database)
if($t.TargetApproved -ne $true -or ([string]$t.ApprovalReference).Length -lt 5 -or $t.ApprovalReference -match '[\r\n]' -or $t.SqlServer -match 'REPLACE|[\r\n]' -or [string]::IsNullOrWhiteSpace($t.SqlServer) -or $t.MachineName -notmatch '^[A-Za-z0-9-]+$' -or $t.MachineName -match 'REPLACE' -or $t.HostName -notmatch '^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])$' -or $t.HostName -match 'REPLACE' -or $t.CertificateThumbprint -notmatch '^[A-Fa-f0-9]{40}$'){throw 'Allocated machine/server/database/HTTPS certificate and operator approval reference required'}
foreach($name in @($t.SiteName,$t.AppPoolName)){if($name -notmatch '^[A-Za-z][A-Za-z0-9_-]{0,63}$'){throw 'Safe dedicated IIS names required'}}
if((Get-UatSafePath $t.PublishRoot) -ine $code){throw 'Target publish path differs from explicit package path'}
$homePath=Get-UatSafePath $t.DataHome;$config=Join-Path $homePath 'uat.json'
Assert-UatSeparatePaths $homePath $config $code
if($output -ieq $code -or $output.StartsWith($code+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Prepared target files must stay outside published application'}
if(Test-Path -LiteralPath $output){throw 'New target packet required; no overwrite'}
$manifest=Get-Content -LiteralPath (Join-Path $code 'manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
foreach($relative in @('ops/uat-config.example.json','ops/schema-idempotent.sql','web/web.config')){
 $records=@($manifest.files|Where-Object {$_.path -ceq $relative});$file=Join-Path $code $relative
 if($records.Count -ne 1 -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $records[0].sha256){throw 'Package template hash differs; no target packet created'}
}
$c=Get-Content -LiteralPath (Join-Path $code 'ops/uat-config.example.json') -Raw -Encoding UTF8|ConvertFrom-Json
$c.Deployment.TargetApproved='true';$c.Deployment.TargetApprovalReference=$t.ApprovalReference;$c.Deployment.ExpectedSqlServer=$t.SqlServer;$c.Deployment.ExpectedHost=$t.HostName
$c.Sql.ExpectedDatabase=$t.Database
$sql=New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$sql['Data Source']=$t.SqlServer;$sql['Initial Catalog']=$t.Database;$sql['Integrated Security']=$true;$sql['Encrypt']=$true;$sql['TrustServerCertificate']=$false
$c.ConnectionStrings.Portal=$sql.ConnectionString;$c.Public.BaseUrl='https://'+$t.HostName
$c.Storage.MediaRoot=Join-Path $homePath 'media';$c.Storage.SnapshotRoot=Join-Path $homePath 'snapshots';$c.Storage.TempRoot=Join-Path $homePath 'temp';$c.Storage.KeysRoot=Join-Path $homePath 'keys'
$c.Scanner.Provider=if($t.PSObject.Properties.Name -contains 'ScannerProvider'){[string]$t.ScannerProvider}else{'Clamd'}
if($c.Scanner.Provider -eq 'MicrosoftDefenderOnPrem'){
 if(([string]$t.DefenderPrivacyPolicyReference).Length -lt 5 -or [string]$t.DefenderBinarySha256 -notmatch '^[A-Fa-f0-9]{64}$' -or ([string]$t.ServiceAccount).Length -lt 4 -or [string]$t.ServiceAccount -match 'REPLACE|[\r\n]'){throw 'Approved Defender privacy policy, binary SHA256 and actual service identity required before preparing target files'}
 $c.Scanner.DefenderPrivacyPolicyReference=[string]$t.DefenderPrivacyPolicyReference
 $c.Scanner.DefenderBinarySha256=[string]$t.DefenderBinarySha256
 $c.Scanner.DefenderServiceIdentity=[string]$t.ServiceAccount
}
elseif($c.Scanner.Provider -cne 'Clamd'){throw 'Unsupported UAT scanner provider'}
$c.Scanner.ClamdHost=$t.ScannerHost;$c.Scanner.ClamdPort=[string]$t.ScannerPort;$c.Scanner.PolicyReference=$t.ScannerPolicyReference
$c.Mail.Host=$t.SmtpHost;$c.Mail.Port=[string]$t.SmtpPort;$c.Mail.From=$t.SmtpFrom
# Runtime role secret, mail credentials and bootstrap credentials remain absent.
Assert-UatTarget $c
$schema=Get-Content -LiteralPath (Join-Path $code 'ops/schema-idempotent.sql') -Raw -Encoding UTF8
$marker='-- LANDPORTAL_SCHEMA_BODY';$offset=$schema.IndexOf($marker,[StringComparison]::Ordinal)
if($offset -lt 0 -or $schema.LastIndexOf($marker,[StringComparison]::Ordinal) -ne $offset){throw 'Schema template marker missing or duplicated'}
$serverLiteral=([string]$t.SqlServer).Replace("'","''");$dbLiteral=([string]$t.Database).Replace("'","''")
$header=":ON ERROR EXIT`r`nUSE ["+$t.Database+"];`r`nGO`r`nIF DB_NAME() <> N'"+$dbLiteral+"' OR COALESCE(CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),N'') <> N'"+$serverLiteral+"'`r`nBEGIN`r`n ;THROW 51000, 'UAT exact target mismatch: stop without applying schema.', 1;`r`nEND`r`nGO`r`n"
[void][IO.Directory]::CreateDirectory($output)
$encoding=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $output 'uat.private-template.json'),($c|ConvertTo-Json -Depth 8),$encoding)
[IO.File]::WriteAllText((Join-Path $output 'target.json'),($t|ConvertTo-Json -Depth 5),$encoding)
[IO.File]::WriteAllText((Join-Path $output 'schema-bound.sqlcmd'),($header+$schema.Substring($offset+$marker.Length)),$encoding)
[xml]$web=Get-Content -LiteralPath (Join-Path $code 'web/web.config') -Raw -Encoding UTF8
foreach($v in $web.configuration.location.'system.webServer'.aspNetCore.environmentVariables.environmentVariable){if($v.name -eq 'LANDPORTAL_HOME'){$v.SetAttribute('value',[string]$homePath)};if($v.name -eq 'LANDPORTAL_CONFIG'){$v.SetAttribute('value',[string]$config)}}
$web.Save((Join-Path $output 'web.config'))
$files=@(Get-ChildItem -LiteralPath $output -File|ForEach-Object {@{path=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
[IO.File]::WriteAllText((Join-Path $output 'target-manifest.json'),(@{status='PREPARED_NOT_INSTALLED';packageManifestSha256=(Get-FileHash -LiteralPath (Join-Path $code 'manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant();files=$files;database=$t.Database;approvalReference=$t.ApprovalReference;secretsIncluded=$false;sqlExecuted=$false;servicesInstalled=0;productionReady=$false}|ConvertTo-Json -Depth 7),$encoding)
Write-Output 'Prepared target config, IIS config and exact-target SQLCMD schema. No secrets, database creation, SQL execution or installation. Operator must provision secrets/ACL and review before execution.'
