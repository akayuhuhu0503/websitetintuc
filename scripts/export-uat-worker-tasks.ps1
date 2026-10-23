[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$DataHome,
 [Parameter(Mandatory=$true)][string]$Config,
 [Parameter(Mandatory=$true)][string]$PublishRoot,
 [Parameter(Mandatory=$true)][string]$ServiceAccount,
 [Parameter(Mandatory=$true)][string]$OutputDirectory
)
# Export only. No task registration, credentials, ACL grants or system changes.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'uat-common.ps1')
Assert-UatSeparatePaths $DataHome $Config $PublishRoot
$homePath=[IO.Path]::GetFullPath($DataHome).TrimEnd('\')
$configPath=[IO.Path]::GetFullPath($Config)
$publishPath=[IO.Path]::GetFullPath($PublishRoot).TrimEnd('\')
$outputPath=[IO.Path]::GetFullPath($OutputDirectory)
$accountParts=@($ServiceAccount.Split([char]92))
if($accountParts.Count -ne 2 -or @($accountParts|Where-Object {$_ -notmatch '^[A-Za-z0-9_.-]+$'}).Count -or $ServiceAccount -match '(?i)(administrator|system|builtin|localservice|networkservice)'){throw 'Dedicated domain or machine service account required; no built-in privileged identity'}
foreach($p in @($homePath,$configPath,$publishPath,$outputPath)){
 if($p.IndexOfAny([char[]]"`r`n`"<>|?") -ge 0){throw 'Invalid path characters'}
 for($node=$p;$node;$node=[IO.Path]::GetDirectoryName($node)){
  if((Test-Path -LiteralPath $node) -and ((Get-Item -LiteralPath $node).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse path refused'}
 }
}
if(-not $configPath.StartsWith($homePath+'\',[StringComparison]::OrdinalIgnoreCase) -or $homePath.StartsWith($publishPath+'\',[StringComparison]::OrdinalIgnoreCase) -or $publishPath.StartsWith($homePath+'\',[StringComparison]::OrdinalIgnoreCase) -or $homePath -ieq $publishPath){throw 'Separate private home/config and published application required'}
if(Test-Path -LiteralPath $outputPath){throw 'New task output directory required; no overwrite'}
if($outputPath.StartsWith($publishPath+'\web\',[StringComparison]::OrdinalIgnoreCase) -or $outputPath -ieq ($publishPath+'\web')){throw 'Task definitions must stay outside webroot'}
$launcher=Join-Path $publishPath 'ops/uat-runtime.ps1'
if(-not(Test-Path -LiteralPath $configPath -PathType Leaf) -or -not(Test-Path -LiteralPath $launcher -PathType Leaf) -or -not(Test-Path -LiteralPath (Join-Path $publishPath 'worker/Worker.dll') -PathType Leaf)){throw 'Existing private config and published launcher/worker required'}
$cfg=Get-Content -LiteralPath $configPath -Raw -Encoding UTF8|ConvertFrom-Json
Assert-UatTarget $cfg
[void][IO.Directory]::CreateDirectory($outputPath)
$namespace='http://schemas.microsoft.com/windows/2004/02/mit/task'
$settings=[Xml.XmlWriterSettings]::new();$settings.Indent=$true;$settings.Encoding=[Text.UTF8Encoding]::new($true)
$rows=@()
foreach($mode in @('Scheduler','Documents','InquiryScan','Video','Mail')){
 $path=Join-Path $outputPath ('LandPortal-Uat-'+$mode+'.xml')
 $writer=[Xml.XmlWriter]::Create($path,$settings)
 try{
  $writer.WriteStartDocument();$writer.WriteStartElement('Task',$namespace);$writer.WriteAttributeString('version','1.4')
  $writer.WriteStartElement('RegistrationInfo');$writer.WriteElementString('Description','LandPortal UAT '+$mode+'; operator must review target and register with approved service identity. No credentials in XML.');$writer.WriteEndElement()
  $writer.WriteStartElement('Triggers');$writer.WriteStartElement('BootTrigger');$writer.WriteElementString('Enabled','true');$writer.WriteElementString('Delay','PT2M');$writer.WriteEndElement();$writer.WriteEndElement()
  $writer.WriteStartElement('Principals');$writer.WriteStartElement('Principal');$writer.WriteAttributeString('id','OperatorService');$writer.WriteElementString('UserId',$ServiceAccount);$writer.WriteElementString('LogonType','Password');$writer.WriteElementString('RunLevel','LeastPrivilege');$writer.WriteEndElement();$writer.WriteEndElement()
  $writer.WriteStartElement('Settings');$writer.WriteElementString('MultipleInstancesPolicy','IgnoreNew');$writer.WriteElementString('DisallowStartIfOnBatteries','false');$writer.WriteElementString('StopIfGoingOnBatteries','false');$writer.WriteElementString('AllowHardTerminate','false');$writer.WriteElementString('StartWhenAvailable','true');$writer.WriteElementString('ExecutionTimeLimit','PT0S');$writer.WriteStartElement('RestartOnFailure');$writer.WriteElementString('Interval','PT1M');$writer.WriteElementString('Count','3');$writer.WriteEndElement();$writer.WriteEndElement()
  $writer.WriteStartElement('Actions');$writer.WriteAttributeString('Context','OperatorService');$writer.WriteStartElement('Exec');$writer.WriteElementString('Command',(Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'));$writer.WriteElementString('Arguments',('-NoProfile -NonInteractive -File "'+$launcher+'" -DataHome "'+$homePath+'" -Config "'+$configPath+'" -PublishRoot "'+$publishPath+'" -Mode '+$mode));$writer.WriteElementString('WorkingDirectory',$publishPath);$writer.WriteEndElement();$writer.WriteEndElement()
  $writer.WriteEndElement();$writer.WriteEndDocument()
 }finally{$writer.Dispose()}
 $rows+=@{mode=$mode;path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$result=@{status='PREPARED_NOT_REGISTERED';tasks=$rows;environment='Staging';database=$cfg.Sql.ExpectedDatabase;approvalReference=$cfg.Deployment.TargetApprovalReference;credentialsIncluded=$false;tasksInstalled=0;identityGranted=$false;restart='Host scheduler: 3 attempts / 1 minute; SQL durable leases survive restart';targetAcceptance='NOT RUN';utc=[DateTimeOffset]::UtcNow.ToString('O')}
$json=$result|ConvertTo-Json -Depth 8
[IO.File]::WriteAllText((Join-Path $outputPath 'tasks-manifest.json'),$json,[Text.UTF8Encoding]::new($false))
Write-Output 'Prepared 5 UAT worker task XML files; no task installed or service identity changed. Operator review/registration and crash-restart acceptance are still required.'
