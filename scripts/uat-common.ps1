# UAT-only operator guards. Never loads project .env or modifies machine state.
function Assert-UatDatabaseName([string]$Database) {
 if($Database.Length -gt 128 -or $Database -notmatch '^[A-Za-z][A-Za-z0-9_]*$' -or $Database -imatch 'restore|prod|replace' -or $Database -iin @('master','model','msdb','tempdb','LandPortal_Dev','LandPortal_Test')){throw 'Allocated non-system UAT database required; Dev/Test/restore/Production forbidden'}
}
function Assert-UatTarget($Settings) {
 Assert-UatDatabaseName ([string]$Settings.Sql.ExpectedDatabase)
 if($Settings.Deployment.Profile -cne 'UAT' -or [string]$Settings.Deployment.TargetApproved -cne 'true' -or ([string]$Settings.Deployment.TargetApprovalReference).Length -lt 5 -or [string]$Settings.Deployment.TargetApprovalReference -match '[\r\n]'){throw 'Explicit allocated UAT target and operator approval reference required'}
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
 if($builder['Initial Catalog'] -cne $Settings.Sql.ExpectedDatabase -or $builder['Data Source'] -cne $Settings.Deployment.ExpectedSqlServer -or -not $auth -or -not $builder['Encrypt'] -or $builder['TrustServerCertificate'] -or [string]::IsNullOrWhiteSpace($builder['Data Source']) -or $builder['Data Source'] -imatch 'REPLACE'){throw 'UAT SQL target/authentication/TLS mismatch'}
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
 $provider=[string]$Settings.Scanner.Provider
 if(([string]$Settings.Scanner.PolicyReference).Length -lt 5){throw 'Approved on-prem scanner policy reference required'}
 if($provider -ceq 'Clamd'){
  $port=0
  if([string]::IsNullOrWhiteSpace([string]$Settings.Scanner.ClamdHost) -or -not [int]::TryParse([string]$Settings.Scanner.ClamdPort,[ref]$port) -or $port -lt 1024 -or $port -gt 65535){throw 'Approved Clamd host and valid service port required'}
 }elseif($provider -ceq 'MicrosoftDefenderOnPrem'){
  if(([string]$Settings.Scanner.DefenderPrivacyPolicyReference).Length -lt 5 -or
     [string]$Settings.Scanner.DefenderBinarySha256 -cnotmatch '^[A-Fa-f0-9]{64}$' -or
     ([string]$Settings.Scanner.DefenderServiceIdentity).Length -lt 4 -or
     [string]$Settings.Scanner.DefenderServiceIdentity -match 'REPLACE|[\r\n]'){throw 'Approved Defender privacy policy, binary pin and service identity required'}
 }else{throw 'Only approved on-prem Clamd or Microsoft Defender is supported for UAT'}
}
