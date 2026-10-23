[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PublishRoot,[Parameter(Mandatory=$true)][string]$ReportRoot)
$ErrorActionPreference='Stop'
$report=[IO.Path]::GetFullPath($ReportRoot)
if(Test-Path -LiteralPath $report){throw 'New isolated test report required'}
[void][IO.Directory]::CreateDirectory($report)
$data=Join-Path $report 'private';[void][IO.Directory]::CreateDirectory($data)
$config=Join-Path $data 'uat.json'
$fixture=@{Deployment=@{Profile='UAT';TargetApproved='true';TargetApprovalReference='Technical fixture only';ExpectedSqlServer='uat.invalid'};Sql=@{ExpectedDatabase='AgencyPortal_UatAllocated'};ConnectionStrings=@{Portal='Server=uat.invalid;Database=AgencyPortal_UatAllocated;Integrated Security=true;Encrypt=true;TrustServerCertificate=false'}}
[IO.File]::WriteAllText($config,($fixture|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
$export=Join-Path $PSScriptRoot 'export-uat-worker-tasks.ps1'
$errors=$null;$tokens=$null;$null=[Management.Automation.Language.Parser]::ParseFile($export,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Exporter syntax failure'}
$checks=@(@{name='PowerShell parse';pass=$true})
$output=Join-Path $report 'task-xml'
& $export -DataHome $data -Config $config -PublishRoot $PublishRoot -ServiceAccount 'UATTEST\portalworker' -OutputDirectory $output
foreach($mode in @('Scheduler','Documents','InquiryScan','Video','Mail')){
 [xml]$doc=Get-Content -LiteralPath (Join-Path $output ('LandPortal-Uat-'+$mode+'.xml')) -Raw -Encoding UTF8
 $ns=[Xml.XmlNamespaceManager]::new($doc.NameTable);$ns.AddNamespace('t','http://schemas.microsoft.com/windows/2004/02/mit/task')
 function Value($path){return $doc.SelectSingleNode($path,$ns).InnerText}
 if((Value '//t:RunLevel') -cne 'LeastPrivilege' -or (Value '//t:RestartOnFailure/t:Count') -cne '3' -or (Value '//t:RestartOnFailure/t:Interval') -cne 'PT1M' -or (Value '//t:MultipleInstancesPolicy') -cne 'IgnoreNew' -or (Value '//t:ExecutionTimeLimit') -cne 'PT0S' -or (Value '//t:Arguments') -notlike ('*-Mode '+$mode) -or (Value '//t:Arguments') -match 'ExecutionPolicy|Password'){throw 'Task guard/hosting configuration mismatch'}
 $checks+=@{name=('XML '+$mode+' dedicated identity/restart/bounds');pass=$true}
}
function Denied([string]$name,[scriptblock]$call){$denied=$false;try{& $call}catch{$denied=$true};if(-not $denied){throw ('Negative guard unexpectedly allowed: '+$name)};$script:checks+=@{name=$name;pass=$true}}
Denied 'Existing output overwrite denied' {& $export -DataHome $data -Config $config -PublishRoot $PublishRoot -ServiceAccount 'UATTEST\portalworker' -OutputDirectory $output}
Denied 'Built-in privileged identity denied' {& $export -DataHome $data -Config $config -PublishRoot $PublishRoot -ServiceAccount 'NT AUTHORITY\SYSTEM' -OutputDirectory ($report+'/bad-system')}
Denied 'Public/private roots overlap denied' {& $export -DataHome $PublishRoot -Config $config -PublishRoot $PublishRoot -ServiceAccount 'UATTEST\portalworker' -OutputDirectory ($report+'/bad-roots')}
Denied 'Config outside private home denied' {& $export -DataHome ($report+'/another-home') -Config $config -PublishRoot $PublishRoot -ServiceAccount 'UATTEST\portalworker' -OutputDirectory ($report+'/bad-config')}
$fixture.Sql.ExpectedDatabase='LandPortal_Dev'
[IO.File]::WriteAllText($config,($fixture|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
Denied 'Dev database config denied' {& $export -DataHome $data -Config $config -PublishRoot $PublishRoot -ServiceAccount 'UATTEST\portalworker' -OutputDirectory ($report+'/bad-db')}
$result=@{status='PASS';checks=$checks;passed=$checks.Count;tasksRegistered=0;servicesInstalled=0;sqlConnections=0;realCredentialsUsed=$false;crashRestartOnTarget='NOT RUN';scope='Offline XML preparation and negative guards only; not a scheduler execution or target acceptance test'}
[IO.File]::WriteAllText((Join-Path $report 'result.json'),($result|ConvertTo-Json -Depth 7),[Text.UTF8Encoding]::new($false))
Write-Output ('PASS '+$checks.Count+' exporter/negative checks; no task/service registration or SQL connection')
