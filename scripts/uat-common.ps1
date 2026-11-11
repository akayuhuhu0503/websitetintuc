# UAT-only operator guards. Never loads project .env or modifies machine state.
function Assert-UatDatabaseName([string]$Database) {
 if($Database.Length -gt 128 -or $Database -notmatch '^[A-Za-z][A-Za-z0-9_]*$' -or $Database -imatch 'restore|prod|replace' -or $Database -iin @('master','model','msdb','tempdb','LandPortal_Dev','LandPortal_Test')){throw 'Allocated non-system UAT database required; Dev/Test/restore/Production forbidden'}
}
function Test-UatSqlCertificateTrustApproval($Settings,$Builder,[string]$Environment='Staging') {
 $names=$Settings.Deployment.PSObject.Properties.Name
 $reference=if($names -contains 'SqlTlsExceptionReference'){[string]$Settings.Deployment.SqlTlsExceptionReference}else{''}
 $hasControl=$false;foreach($character in $reference.ToCharArray()){if([char]::IsControl($character)){$hasControl=$true;break}}
 return $Environment -ceq 'Staging' -and $Settings.Deployment.Profile -ceq 'UAT' -and
  ($names -contains 'SqlTlsExceptionApproved') -and $Settings.Deployment.SqlTlsExceptionApproved -ceq 'true' -and
  $reference.Length -gt 4 -and -not [string]::IsNullOrWhiteSpace($reference) -and -not $hasControl -and
  $Settings.Deployment.ExpectedSqlServer -ceq '45.120.229.43,1433' -and $Builder['Data Source'] -ceq '45.120.229.43,1433' -and
  $Settings.Deployment.ExpectedSqlServerIdentity -ceq 'WIN-S1IBJB84OOO' -and
  $Settings.Sql.ExpectedDatabase -ceq 'website_tin_tuc' -and $Builder['Initial Catalog'] -ceq 'website_tin_tuc' -and
  $Settings.Deployment.SqlAuthentication -ceq 'SqlPassword' -and $Settings.Deployment.ExpectedSqlLogin -ceq 'website_tin_tuc_app' -and
  $Builder['User ID'] -ceq 'website_tin_tuc_app' -and -not $Builder['Integrated Security'] -and
  -not $Builder['Persist Security Info'] -and -not [string]::IsNullOrWhiteSpace([string]$Builder['Password']) -and $Builder['Encrypt']
}
function Assert-UatTarget($Settings,[string]$Environment='Staging') {
 if($Settings.Deployment.Profile -cne 'UAT'){throw 'UAT profile required'}
 Assert-UatSqlConfiguration $Settings $Environment
}
# Packaging validates SQL syntax and the scoped TLS exception without pretending
# that a deployment has been approved. Neither function opens a connection.
function Assert-UatSqlConfiguration($Settings,[string]$Environment='Staging') {
 Assert-UatDatabaseName ([string]$Settings.Sql.ExpectedDatabase)
 if($Settings.Deployment.Profile -cne 'UAT'){throw 'Only UAT packaging/runtime is supported'}
 try{
  # Windows PowerShell's legacy provider cannot parse the modern SqlClient TLS
  # hostname key. Runtime preserves/validates it using Microsoft.Data.SqlClient.
  $portable=New-Object System.Data.Common.DbConnectionStringBuilder
  $portable.set_ConnectionString([string]$Settings.ConnectionStrings.Portal)
  foreach($key in @($portable.Keys)){if(($key -replace ' ','') -ieq 'HostNameInCertificate'){if([string]::IsNullOrWhiteSpace([string]$portable[$key]) -or [string]$portable[$key] -match '[\r\n]'){throw 'Invalid TLS hostname'};[void]$portable.Remove($key)}}
  $builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder ($portable.get_ConnectionString())
 }catch{throw 'Invalid private SQL connection configuration; values are not logged'}
 $mode=if($Settings.Deployment.PSObject.Properties.Name -contains 'SqlAuthentication'){[string]$Settings.Deployment.SqlAuthentication}else{'Windows'}
 $auth=$false
 if($mode -ceq 'Windows'){$auth=$builder['Integrated Security'] -and -not $builder['User ID'] -and -not $builder['Password']}
 if($mode -ceq 'SqlPassword'){
  $login=if($Settings.Deployment.PSObject.Properties.Name -contains 'ExpectedSqlLogin'){[string]$Settings.Deployment.ExpectedSqlLogin}else{''}
  $auth=-not $builder['Integrated Security'] -and [string]$builder.Authentication -cin @('NotSpecified','SqlPassword') -and $login.Length -gt 0 -and $login -ine 'sa' -and $login -notmatch 'REPLACE|[\r\n]' -and $builder['User ID'] -ceq $login -and -not [string]::IsNullOrWhiteSpace($builder['Password']) -and -not $builder['Persist Security Info']
 }
 if($Environment -cne 'Staging' -or $builder['Initial Catalog'] -cne $Settings.Sql.ExpectedDatabase -or $builder['Data Source'] -cne $Settings.Deployment.ExpectedSqlServer -or -not $auth -or -not $builder['Encrypt'] -or ($builder['TrustServerCertificate'] -and -not (Test-UatSqlCertificateTrustApproval $Settings $builder $Environment)) -or [string]::IsNullOrWhiteSpace($builder['Data Source']) -or $builder['Data Source'] -imatch 'REPLACE'){throw 'UAT SQL target/authentication/TLS mismatch'}
}
function Get-UatSafePath([string]$Path) {
 if($Path -notmatch '^(?:[A-Za-z]:[\\/]|\\\\[^\\]+\\[^\\]+\\)' -or $Path.IndexOfAny([char[]]"`r`n`"<>|?*") -ge 0){throw 'Absolute safe path required'}
 $full=[IO.Path]::GetFullPath($Path).TrimEnd('\')
 for($node=$full;$node;$node=[IO.Path]::GetDirectoryName($node)) {
  if((Test-Path -LiteralPath $node) -and ((Get-Item -LiteralPath $node).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse path refused'}
 }
 return $full
}
function Assert-UatSeparatePaths([string]$DataHome,[string]$Config,[string]$PublishRoot) {
 $data=Get-UatSafePath $DataHome;$cfg=Get-UatSafePath $Config;$code=Get-UatSafePath $PublishRoot
 if(-not $cfg.StartsWith($data+'\',[StringComparison]::OrdinalIgnoreCase) -or $data -ieq $code -or $data.StartsWith($code+'\',[StringComparison]::OrdinalIgnoreCase) -or $code.StartsWith($data+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Separate private home/config and published application required'}
}

# Offline parity with UatDeployment.Validate: reject incomplete runtime settings
# before writing private config or starting any Web/Worker/SQL process. This is
# configuration validation, not authority to provision or deploy a target.
function Assert-UatRuntimeConfiguration($Settings,[string]$DataHome,[string]$Config,[string]$PublishRoot) {
 Assert-UatTarget $Settings
 Assert-UatSeparatePaths $DataHome $Config $PublishRoot
 $homePath=Get-UatSafePath $DataHome
 $physical=[string]$Settings.Deployment.ExpectedSqlServerIdentity
 if(([string]$Settings.Deployment.SqlAuthentication -ceq 'SqlPassword' -and [string]::IsNullOrWhiteSpace($physical)) -or $physical -match 'REPLACE|[\r\n]'){throw 'Bind the DBA-confirmed physical SQL server identity before runtime preparation'}
 $origin=$null
 if(-not [Uri]::TryCreate([string]$Settings.Public.BaseUrl,[UriKind]::Absolute,[ref]$origin) -or
    $origin.Scheme -cne 'https' -or $origin.AbsolutePath -cne '/' -or $origin.UserInfo.Length -ne 0 -or
    $origin.Query.Length -ne 0 -or $origin.Fragment.Length -ne 0 -or
    -not [string]::Equals($origin.IdnHost,[string]$Settings.Deployment.ExpectedHost,[StringComparison]::OrdinalIgnoreCase)){
  throw 'UAT public origin must use the exact approved HTTPS host without credentials, path, query or fragment'
 }
 $roots=@(foreach($name in @('MediaRoot','SnapshotRoot','TempRoot','KeysRoot')){
  $root=Get-UatSafePath ([string]$Settings.Storage.$name)
  if(-not $root.StartsWith($homePath+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'All UAT stores must be private children of the approved data home'}
  $root
 })
 for($i=0;$i -lt $roots.Count;$i++){
  for($j=$i+1;$j -lt $roots.Count;$j++){
   if($roots[$i] -ieq $roots[$j] -or $roots[$i].StartsWith($roots[$j]+'\',[StringComparison]::OrdinalIgnoreCase) -or $roots[$j].StartsWith($roots[$i]+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'UAT media, snapshot, temporary and key stores must be distinct and not nested'}
  }
 }
 $roleSecret=[string]$Settings.Runtime.RolePassword
 if($roleSecret.Length -lt 24 -or $roleSecret.Length -gt 128){throw 'Runtime role secret must contain 24 to 128 characters; value is not logged'}
 if([string]$Settings.Media.AllowUnscannedDevelopmentUploads -ine 'false'){throw 'UAT unscanned development uploads must be explicitly disabled'}
 # Scanner is probed at file operations, never a whole-site startup gate.
 Assert-UatOptionalServices $Settings
}

function Assert-UatOptionalServices($Settings) {
 if($Settings.Mail.Transport -cnotin @('Disabled','Smtp')){throw 'Only Disabled or approved Smtp transport supported'}
 if($Settings.Mail.Transport -ceq 'Smtp'){
  $port=0
  try{$null=New-Object Net.Mail.MailAddress ([string]$Settings.Mail.From)}catch{throw 'Valid SMTP From required'}
  if(-not [int]::TryParse([string]$Settings.Mail.Port,[ref]$port) -or $port -lt 1 -or $port -gt 65535 -or $Settings.Mail.Tls -cne 'StartTls' -or [string]$Settings.Mail.DeliveryEnabled -cnotin @('true','false') -or ([string]$Settings.Mail.PolicyReference).Length -lt 5 -or [Uri]::CheckHostName($Settings.Mail.Host) -eq 'Unknown'){throw 'Approved SMTP host/port/TLS/delivery policy required'}
  if([string]::IsNullOrEmpty($Settings.Mail.UserName) -ne [string]::IsNullOrEmpty($Settings.Mail.Password)){throw 'SMTP credentials must be paired; values withheld'}
 }
 if([string]$Settings.Video.Enabled -cnotin @('true','false')){throw 'Video Enabled must be true/false'}
 if([string]$Settings.Video.Enabled -ceq 'true'){
  # Empty values use runtime discovery. Existing explicit overrides/pins remain enforced.
  foreach($field in @('FfmpegPath','FfprobePath')){if(-not [string]::IsNullOrWhiteSpace([string]$Settings.Video.$field)){[void](Get-UatSafePath ([string]$Settings.Video.$field))}}
  foreach($field in @('FfmpegSha256','FfprobeSha256')){if(-not [string]::IsNullOrWhiteSpace([string]$Settings.Video.$field) -and [string]$Settings.Video.$field -cnotmatch '^[a-fA-F0-9]{64}$'){throw 'Explicit video binary pins must contain 64 hexadecimal characters'}}
  if([string]$Settings.Video.ProcessorApproved -cne 'true' -or ([string]$Settings.Video.LicenseReference).Length -lt 5){throw 'Video processor/license approval required before activation'}
 }
}
