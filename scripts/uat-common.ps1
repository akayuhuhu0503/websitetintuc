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
