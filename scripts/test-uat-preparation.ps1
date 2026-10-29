[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReportRoot)
$ErrorActionPreference='Stop'
$report=[IO.Path]::GetFullPath($ReportRoot)
if(Test-Path -LiteralPath $report){throw 'New isolated test report required'}
[void][IO.Directory]::CreateDirectory($report)
$checks=@()
function Check([string]$Name,[bool]$Ok){if(-not $Ok){throw ('Check failed: '+$Name)};$script:checks+=@{name=$Name;pass=$true}}
function Denied([string]$Name,[scriptblock]$Action){$denied=$false;try{& $Action}catch{$denied=$true};Check $Name $denied}
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
