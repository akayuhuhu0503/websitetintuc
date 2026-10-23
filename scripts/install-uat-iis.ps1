[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='High')]
param([Parameter(Mandatory=$true)][string]$PreparedTarget,[switch]$Apply,[System.Management.Automation.PSCredential]$WebCredential)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'uat-common.ps1')
$prepared=Get-UatSafePath $PreparedTarget
$manifest=Get-Content -LiteralPath (Join-Path $prepared 'target-manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
foreach($name in @('target.json','web.config','uat.private-template.json','schema-bound.sqlcmd')){
 $record=@($manifest.files|Where-Object {$_.path -ceq $name})
 if($record.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $prepared $name) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $record[0].sha256){throw 'Prepared target packet hash mismatch'}
}
$t=Get-Content -LiteralPath (Join-Path $prepared 'target.json') -Raw -Encoding UTF8|ConvertFrom-Json
$code=Get-UatSafePath $t.PublishRoot;$homePath=Get-UatSafePath $t.DataHome;$config=Join-Path $homePath 'uat.json'
Assert-UatSeparatePaths $homePath $config $code;Assert-UatDatabaseName $t.Database
if($t.TargetApproved -ne $true -or ([string]$t.ApprovalReference).Length -lt 5 -or $t.Database -cne $manifest.database -or $t.ApprovalReference -cne $manifest.approvalReference){throw 'Target approval scope mismatch'}
if(-not $Apply){Write-Output 'PLAN ONLY: verify package/config/SQL, back up IIS config, create new stopped pool/site, install bound web.config, bind existing HTTPS certificate, then start. No Windows feature, IIS, service, SQL or credential operation performed. Use -Apply on the approved machine after DBA/scanner/ACL setup.';return}
if($env:COMPUTERNAME -ine $t.MachineName){throw 'This is not the approved IIS machine'}
if(-not $WebCredential -or $WebCredential.UserName -ine $t.ServiceAccount -or $WebCredential.UserName -imatch 'administrator|system|builtin|localservice|networkservice'){throw 'Approved dedicated Web service credential required via Get-Credential; never pass password text'}
if(-not $PSCmdlet.ShouldProcess(($t.MachineName+'/'+$t.SiteName),'Create dedicated UAT IIS site/pool and HTTPS binding; no DB creation, worker registration or ACL grants')){return}
$cfg=Get-Content -LiteralPath $config -Raw -Encoding UTF8|ConvertFrom-Json
Assert-UatTarget $cfg
if($cfg.Sql.ExpectedDatabase -cne $t.Database -or $cfg.Deployment.ExpectedSqlServer -cne $t.SqlServer -or $cfg.Deployment.ExpectedHost -cne $t.HostName -or $cfg.Deployment.TargetApprovalReference -cne $t.ApprovalReference){throw 'Private config differs from approved target packet'}
$package=Get-Content -LiteralPath (Join-Path $code 'manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
if((Get-FileHash -LiteralPath (Join-Path $code 'manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() -cne $manifest.packageManifestSha256){throw 'Original package manifest changed'}
foreach($file in $package.files){
 $path=Get-UatSafePath (Join-Path $code $file.path)
 if(-not $path.StartsWith($code+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256){throw 'Package file hash or path mismatch'}
}
Import-Module WebAdministration -ErrorAction Stop
if(-not(Get-WebGlobalModule -Name AspNetCoreModuleV2)){throw 'ASP.NET Core 10 Hosting Bundle / ANCM prerequisite missing; no automatic feature installation'}
if((Test-Path ('IIS:\Sites\'+$t.SiteName)) -or (Test-Path ('IIS:\AppPools\'+$t.AppPoolName))){throw 'Existing IIS site/pool refused; no overwrite or retry after partial installation'}
foreach($site in Get-Website){
 if([Environment]::ExpandEnvironmentVariables([string]$site.physicalPath).TrimEnd('\') -ieq (Join-Path $code 'web')){throw 'Package web path already used by another IIS site'}
 foreach($binding in $site.bindings.Collection){if($binding.protocol -eq 'https' -and $binding.bindingInformation -like ('*:443:'+$t.HostName)){throw 'Existing HTTPS host binding refused'}}
}
$cert=Get-Item -LiteralPath ('Cert:\LocalMachine\My\'+$t.CertificateThumbprint)
if(-not $cert.HasPrivateKey -or $cert.NotBefore -gt (Get-Date) -or $cert.NotAfter -le (Get-Date) -or @($cert.DnsNameList|Where-Object {$_.Unicode -ieq $t.HostName}).Count -eq 0 -or @($cert.EnhancedKeyUsageList|Where-Object {$_.ObjectId -eq '1.3.6.1.5.5.7.3.1'}).Count -eq 0){throw 'Existing valid exact-host server-auth HTTPS certificate required; no certificate creation/trust change'}
$chain=New-Object Security.Cryptography.X509Certificates.X509Chain
try{$chain.ChainPolicy.RevocationMode='Online';if(-not $chain.Build($cert)){throw 'HTTPS certificate chain/revocation check failed'}}finally{$chain.Dispose()}
$run='iis-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)
$report=Join-Path $homePath ('reports/'+$run);[void][IO.Directory]::CreateDirectory($report)
& (Join-Path $PSHOME 'powershell.exe') -NoProfile -NonInteractive -File (Join-Path $code 'ops/uat-runtime.ps1') -DataHome $homePath -Config $config -PublishRoot $code -Mode Preflight -Online -Report (Join-Path $report 'sql-readonly.json')
if($LASTEXITCODE -ne 0){throw 'Read-only UAT runtime/SQL preflight failed; no IIS writes'}
Backup-WebConfiguration -Name $run | Out-Null
Copy-Item -LiteralPath (Join-Path $code 'web/web.config') -Destination (Join-Path $report 'web-before.config') -ErrorAction Stop
# No automatic deletion/retry if installation fails after this point. Reconcile
# the new site/pool, this audit and IIS backup before further operator action.
$audit=@{status='STARTED_RECONCILE_IF_INTERRUPTED';run=$run;machine=$t.MachineName;site=$t.SiteName;pool=$t.AppPoolName;database=$t.Database;iisBackup=$run;targetManifestSha256=(Get-FileHash -LiteralPath (Join-Path $prepared 'target-manifest.json') -Algorithm SHA256).Hash;sqlWrites=0;workersInstalled=0}
$auditPath=Join-Path $report 'installation.json'
[IO.File]::WriteAllText($auditPath,($audit|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
try{
 New-WebAppPool -Name $t.AppPoolName | Out-Null
 Set-ItemProperty ('IIS:\AppPools\'+$t.AppPoolName) -Name managedRuntimeVersion -Value ''
 Set-ItemProperty ('IIS:\AppPools\'+$t.AppPoolName) -Name enable32BitAppOnWin64 -Value $false
 $secret=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($WebCredential.Password)
 try{$password=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($secret);Set-ItemProperty ('IIS:\AppPools\'+$t.AppPoolName) -Name processModel -Value @{identityType=3;userName=$WebCredential.UserName;password=$password}}finally{$password=$null;[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secret)}
 Copy-Item -LiteralPath (Join-Path $prepared 'web.config') -Destination (Join-Path $code 'web/web.config') -ErrorAction Stop
 New-Website -Name $t.SiteName -PhysicalPath (Join-Path $code 'web') -ApplicationPool $t.AppPoolName -Port 443 -HostHeader $t.HostName -Ssl -SslFlags 1 | Out-Null
 Get-WebBinding -Name $t.SiteName -Protocol https | ForEach-Object {$_.AddSslCertificate($t.CertificateThumbprint,'My')}
 $audit.status='INSTALLED_NOT_ACCEPTED';$audit.boundWebConfigSha256=(Get-FileHash -LiteralPath (Join-Path $code 'web/web.config') -Algorithm SHA256).Hash
}catch{$audit.status='PARTIAL_RECONCILE_REQUIRED';throw 'IIS installation interrupted; inspect private installation audit and IIS backup before retry. No automatic cleanup or SQL rollback performed.'}finally{[IO.File]::WriteAllText($auditPath,($audit|ConvertTo-Json),[Text.UTF8Encoding]::new($false))}
Write-Output 'UAT IIS installed; actual HTTPS/admin/scanner/mail/worker/UAT acceptance still required. No worker task registered, database created or permissions granted.'
