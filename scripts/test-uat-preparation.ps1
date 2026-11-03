[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReportRoot,[switch]$IisPublishOnly)
$ErrorActionPreference='Stop'
$report=[IO.Path]::GetFullPath($ReportRoot)
if(Test-Path -LiteralPath $report){throw 'New isolated test report required'}
[void][IO.Directory]::CreateDirectory($report)
$checks=@()
function Check([string]$Name,[bool]$Ok){if(-not $Ok){throw ('Check failed: '+$Name)};$script:checks+=@{name=$Name;pass=$true}}
function Denied([string]$Name,[scriptblock]$Action){$denied=$false;try{& $Action}catch{$denied=$true};Check $Name $denied}
if($IisPublishOnly){
 . (Join-Path $PSScriptRoot 'uat-common.ps1')
 . (Join-Path $PSScriptRoot 'iis-env.ps1')
 . (Join-Path $PSScriptRoot 'configure-iis-uat.ps1')
 $envFile=Join-Path $report 'technical-only.env'
 $base=@('ConnectionStrings__Portal=Server=45.120.229.43,1433;Database=website_tin_tuc;User ID=website_tin_tuc_app;Password=<technical-only-not-a-real-secret>;Encrypt=True;TrustServerCertificate=True',
  ('Runtime__RolePassword='+('X'*48)),'Deployment__ExpectedSqlServerIdentity=WIN-S1IBJB84OOO','Deployment__SqlTlsExceptionApproved=true','Deployment__SqlTlsExceptionReference=UNIT-TEST-ONLY-NOT-AUTHORITY')
 function SaveInput([string[]]$Lines){[IO.File]::WriteAllLines($envFile,$Lines,[Text.UTF8Encoding]::new($false))}
 SaveInput $base
 $inputSettings=Read-IisEnvironment $envFile -ForPublish;$c=$inputSettings.Settings
 Check 'Minimal env accepted without hostname storage scanner approval' (-not $c.Public.BaseUrl -and -not $c.Storage.MediaRoot -and -not $c.Scanner.PolicyReference -and -not $inputSettings.Metadata.LANDPORTAL_HOME)
 Check 'SQL identifiers inferred from connection, UAT defaults safe' ($c.Deployment.ExpectedSqlServer -ceq '45.120.229.43,1433' -and $c.Deployment.ExpectedSqlLogin -ceq 'website_tin_tuc_app' -and $c.Sql.ExpectedDatabase -ceq 'website_tin_tuc' -and $c.Deployment.TargetApproved -ceq 'false' -and $c.Video.Enabled -ceq 'false')
 $targetDataHome=Join-Path $report 'private';$publish=Join-Path $report 'code'
 & (Join-Path $PSScriptRoot 'prepare-iis-config.ps1') -EnvFile $envFile -DataHome $targetDataHome -PublishRoot $publish -ForPublish -DeferTarget -ValidateOnly
 Check 'Deferred validation opens no SQL and creates no config' (-not(Test-Path -LiteralPath $targetDataHome))
 Denied 'Runtime still rejects unallocated target' {Assert-UatRuntimeConfiguration $c $targetDataHome (Join-Path $targetDataHome 'uat.json') $publish}
 Denied 'Legacy immediate conversion still requires runtime gates' {& (Join-Path $PSScriptRoot 'prepare-iis-config.ps1') -EnvFile $envFile -DataHome $targetDataHome -PublishRoot $publish -ForPublish -ValidateOnly}
 SaveInput ($base+@('DOTNET_ENVIRONMENT=Production'));Denied 'Production environment denied' {Read-IisEnvironment $envFile -ForPublish}
 SaveInput ($base+@('Deployment__Profile=Production'));Denied 'Production profile denied' {Read-IisEnvironment $envFile -ForPublish}
 Denied 'Production cannot use TLS exception at SQL guard' {Assert-UatSqlConfiguration $c 'Production'}
 SaveInput ($base+@('Sql__ExpectedDatabase=DifferentDb'));Denied 'Different SQL target denied' {Read-IisEnvironment $envFile -ForPublish}
 SaveInput @($base|Where-Object {$_ -notlike 'Deployment__SqlTlsExceptionApproved=*'});Denied 'Missing TLS approval denied' {Read-IisEnvironment $envFile -ForPublish}
 SaveInput @($base|ForEach-Object {$_.Replace('User ID=website_tin_tuc_app','User ID=sa')});Denied 'sa runtime forbidden' {Read-IisEnvironment $envFile -ForPublish}
 SaveInput ($base+@('Media__AllowUnscannedDevelopmentUploads=true'));Denied 'Unscanned bypass forbidden' {Read-IisEnvironment $envFile -ForPublish}
 SaveInput ($base+@('Mail__Transport=Smtp'));Denied 'SMTP cannot be enabled by packaging' {Read-IisEnvironment $envFile -ForPublish}
 SaveInput $base
 $target=[pscustomobject]@{HostName='uat.example.invalid';Port=8443;MachineName='UNIT-TEST';SiteName='UNIT-TEST';ServiceAccount='UNITTEST\portal';DefenderSha256=('B'*64)}
 $derived=Resolve-IisUatSettings $c $target $targetDataHome $null
 Check 'Actual target binding maps once to host and URL' ($derived.Public.BaseUrl -ceq 'https://uat.example.invalid:8443/' -and $derived.Deployment.ExpectedHost -ceq $target.HostName)
 Check 'Storage and service pin derived without secret changes' ($derived.Storage.KeysRoot -ceq (Join-Path $targetDataHome 'keys') -and $derived.Runtime.RolePassword -ceq $c.Runtime.RolePassword -and $derived.ConnectionStrings.Portal -ceq $c.ConnectionStrings.Portal -and $derived.Scanner.DefenderBinarySha256 -ceq $target.DefenderSha256)
 Check 'Discovery does not manufacture authority' ($derived.Deployment.TargetApproved -ceq 'false' -and -not $derived.Scanner.PolicyReference -and -not $derived.Scanner.DefenderPrivacyPolicyReference)
 Denied 'Discovery alone cannot pass runtime' {Assert-UatRuntimeConfiguration $derived $targetDataHome (Join-Path $targetDataHome 'uat.json') $publish}
 $a=[pscustomobject]@{Status='PENDING';MachineName='UNIT-TEST';SiteName='UNIT-TEST';PhysicalSqlServer='WIN-S1IBJB84OOO';Database='website_tin_tuc';DeploymentReference='UNIT-TEST-ONLY';ScannerPolicyReference='UNIT-TEST-ONLY';DefenderPrivacyPolicyReference='UNIT-TEST-ONLY'}
 Denied 'Pending approval rejected' {Resolve-IisUatSettings $c $target $targetDataHome $a}
 $a.Status='APPROVED';$a.MachineName='WRONG'
 Denied 'Approval bound to different target rejected' {Resolve-IisUatSettings $c $target $targetDataHome $a}
 $a.MachineName='UNIT-TEST';$candidate=Resolve-IisUatSettings $c $target $targetDataHome $a
 Assert-UatRuntimeConfiguration $candidate $targetDataHome (Join-Path $targetDataHome 'uat.json') $publish
 Check 'Complete technical fixture reaches offline runtime validation only' ($candidate.Mail.Transport -ceq 'Disabled')
 $candidate.Video.Enabled='true'
 Denied 'Video approval still required at activation' {Assert-UatRuntimeConfiguration $candidate $targetDataHome (Join-Path $targetDataHome 'uat.json') $publish}
 foreach($name in @('build-for-iis.ps1','package-uat.ps1','iis-env.ps1','uat-common.ps1','prepare-iis-config.ps1','configure-iis-uat.ps1')){
  $errors=$null;$tokens=$null;$null=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name),[ref]$tokens,[ref]$errors);Check ('Parse '+$name) ($errors.Count -eq 0)
 }
 [IO.File]::WriteAllText((Join-Path $report 'result.json'),(@{status='PASS';checks=$checks;passed=$checks.Count;sqlConnections=0;iisChanges=0;targetAcceptance='NOT RUN';fixtures='Technical only; no actual authority'}|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
 Write-Output ('PASS '+$checks.Count+' focused offline IIS publish/activation checks; no SQL or IIS changes');return
}
foreach($name in @('uat-common.ps1','uat-runtime.ps1','prepare-uat-target.ps1','install-uat-iis.ps1','export-uat-worker-tasks.ps1','test-uat-worker-tasks.ps1','test-uat-preparation.ps1')){
 $errors=$null;$tokens=$null;$null=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name),[ref]$tokens,[ref]$errors);Check ('Parse '+$name) ($errors.Count -eq 0)
}
$code=Join-Path $report 'fixture-code';[void][IO.Directory]::CreateDirectory((Join-Path $code 'ops'));[void][IO.Directory]::CreateDirectory((Join-Path $code 'web'))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uat-config.example.json') -Destination (Join-Path $code 'ops')
$sessionSettings=(@('QUOTED_IDENTIFIER ON','ANSI_NULLS ON','ANSI_PADDING ON','ANSI_WARNINGS ON','ARITHABORT ON','CONCAT_NULL_YIELDS_NULL ON','NUMERIC_ROUNDABORT OFF','XACT_ABORT ON')|ForEach-Object {'SET '+$_+';'}) -join "`n"
[IO.File]::WriteAllText((Join-Path $code 'ops/schema-idempotent.sql'),(":ON ERROR EXIT`n;THROW 51000, 'UNALLOCATED',1;`nGO`n-- LANDPORTAL_SCHEMA_BODY`n"+$sessionSettings+"`nGO`n-- TECHNICAL FIXTURE ONLY: no schema or data`nGO`n"),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $code 'web/web.config'),'<configuration><location><system.webServer><aspNetCore><environmentVariables><environmentVariable name="LANDPORTAL_HOME" value="REPLACE"/><environmentVariable name="LANDPORTAL_CONFIG" value="REPLACE"/></environmentVariables></aspNetCore></system.webServer></location></configuration>',[Text.UTF8Encoding]::new($false))
$records=@(Get-ChildItem -LiteralPath $code -Recurse -File|ForEach-Object {@{path=$_.FullName.Substring($code.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
[IO.File]::WriteAllText((Join-Path $code 'manifest.json'),(@{files=$records}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
$t=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'uat-target.example.json') -Raw -Encoding UTF8|ConvertFrom-Json
$t.TargetApproved=$true;$t.ApprovalReference='TECHNICAL FIXTURE - NOT AUTHORITY';$t.MachineName='UAT-FIXTURE';$t.SqlServer='uat.invalid\SQL';$t.Database='AgencyPortal_UatAllocated';$t.HostName='uat.example.invalid';$t.CertificateThumbprint=('A'*40);$t.PublishRoot=$code;$t.DataHome=Join-Path $report 'private';$t.ServiceAccount='FIXTURE\portal'
$t.ScannerProvider='MicrosoftDefenderOnPrem';$t.ScannerPolicyReference='TECHNICAL FIXTURE POLICY NOT AUTHORITY';$t.DefenderPrivacyPolicyReference='TECHNICAL FIXTURE PRIVACY NOT AUTHORITY';$t.DefenderBinarySha256=('B'*64)
$inputFile=Join-Path $report 'fixture-target.json'
function SaveTarget{[IO.File]::WriteAllText($inputFile,($t|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))}
SaveTarget
$prepare=Join-Path $PSScriptRoot 'prepare-uat-target.ps1';$output=Join-Path $report 'prepared'
& $prepare -TargetFile $inputFile -PublishRoot $code -OutputDirectory $output
$cfg=Get-Content -LiteralPath (Join-Path $output 'uat.private-template.json') -Raw -Encoding UTF8|ConvertFrom-Json
Check 'Native Defender configuration binds policy pin and service identity' ($cfg.Scanner.Provider -ceq 'MicrosoftDefenderOnPrem' -and $cfg.Scanner.DefenderPrivacyPolicyReference -ceq $t.DefenderPrivacyPolicyReference -and $cfg.Scanner.DefenderBinarySha256 -ceq $t.DefenderBinarySha256 -and $cfg.Scanner.DefenderServiceIdentity -ceq $t.ServiceAccount)
Check 'Allocated DB preserved, mail disabled and no secret generated' ($cfg.Sql.ExpectedDatabase -ceq $t.Database -and $cfg.Mail.Transport -ceq 'Disabled' -and $cfg.Runtime.RolePassword -ceq '')
$schema=Get-Content -LiteralPath (Join-Path $output 'schema-bound.sqlcmd') -Raw -Encoding UTF8
Check 'SQLCMD stops on error and pins exact DB and physical SQL instance' ($schema.StartsWith(':ON ERROR EXIT') -and $schema.Contains('USE [AgencyPortal_UatAllocated]') -and $schema.Contains("SERVERPROPERTY('ServerName')") -and $schema.Contains("uat.invalid\SQL") -and -not $schema.Contains('UNALLOCATED'))
[xml]$web=Get-Content -LiteralPath (Join-Path $output 'web.config') -Raw -Encoding UTF8
Check 'IIS config references private home, no developer target' ($web.OuterXml.Contains($t.DataHome) -and -not $web.OuterXml.Contains('MinhAnh'))
$plan=@(& (Join-Path $PSScriptRoot 'install-uat-iis.ps1') -PreparedTarget $output)
Check 'Installer defaults to plan only on unconfirmed machine' (($plan -join '') -like '*PLAN ONLY*')
Denied 'Overwrite refused' {& $prepare -TargetFile $inputFile -PublishRoot $code -OutputDirectory $output}
$t.TargetApproved=$false;SaveTarget
Denied 'Unapproved target refused' {& $prepare -TargetFile $inputFile -PublishRoot $code -OutputDirectory (Join-Path $report 'bad-unapproved')}
$t.TargetApproved=$true;$t.Database='LandPortal_Dev';SaveTarget
Denied 'Dev database refused' {& $prepare -TargetFile $inputFile -PublishRoot $code -OutputDirectory (Join-Path $report 'bad-dev')}
$t.Database='AgencyPortal_UatAllocated';$t.DataHome=$code;SaveTarget
Denied 'Private/public overlap refused' {& $prepare -TargetFile $inputFile -PublishRoot $code -OutputDirectory (Join-Path $report 'bad-overlap')}
$t.DataHome=Join-Path $report 'private';SaveTarget
$t.DefenderPrivacyPolicyReference='';SaveTarget
Denied 'Missing Defender privacy authority refused' {& $prepare -TargetFile $inputFile -PublishRoot $code -OutputDirectory (Join-Path $report 'bad-privacy')}
$t.DefenderPrivacyPolicyReference='TECHNICAL FIXTURE PRIVACY NOT AUTHORITY';$t.DefenderBinarySha256='bad';SaveTarget
Denied 'Invalid Defender binary pin refused' {& $prepare -TargetFile $inputFile -PublishRoot $code -OutputDirectory (Join-Path $report 'bad-binary-pin')}
$t.DefenderBinarySha256=('B'*64);SaveTarget
[IO.File]::AppendAllText((Join-Path $code 'ops/schema-idempotent.sql'),'tampered')
Denied 'Schema template hash drift refused' {& $prepare -TargetFile $inputFile -PublishRoot $code -OutputDirectory (Join-Path $report 'bad-hash')}
[IO.File]::AppendAllText((Join-Path $output 'web.config'),'tampered')
Denied 'Prepared target tampering refused before installation' {& (Join-Path $PSScriptRoot 'install-uat-iis.ps1') -PreparedTarget $output}
$result=@{status='PASS';checks=$checks;passed=$checks.Count;sqlConnections=0;iisChanges=0;tasksInstalled=0;fixtures='Technical only; not UAT acceptance or approval';targetAcceptance='NOT RUN'}
[IO.File]::WriteAllText((Join-Path $report 'result.json'),($result|ConvertTo-Json -Depth 7),[Text.UTF8Encoding]::new($false))
Write-Output ('PASS '+$checks.Count+' focused offline preparation checks; no SQL/IIS/service changes')
