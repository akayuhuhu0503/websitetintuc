# UAT-only operator guards. Never loads project .env or modifies machine state.
function Assert-UatDatabaseName([string]$Database) {
 if($Database.Length -gt 128 -or $Database -notmatch '^[A-Za-z][A-Za-z0-9_]*$' -or $Database -imatch 'restore|prod|replace' -or $Database -iin @('master','model','msdb','tempdb','LandPortal_Dev','LandPortal_Test')){throw 'Allocated non-system UAT database required; Dev/Test/restore/Production forbidden'}
}
function Assert-UatTarget($Settings) {
 Assert-UatDatabaseName ([string]$Settings.Sql.ExpectedDatabase)
 if($Settings.Deployment.Profile -cne 'UAT' -or [string]$Settings.Deployment.TargetApproved -cne 'true' -or ([string]$Settings.Deployment.TargetApprovalReference).Length -lt 5 -or [string]$Settings.Deployment.TargetApprovalReference -match '[\r\n]'){throw 'Explicit allocated UAT target and operator approval reference required'}
 $builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder ([string]$Settings.ConnectionStrings.Portal)
 if($builder['Initial Catalog'] -cne $Settings.Sql.ExpectedDatabase -or $builder['Data Source'] -cne $Settings.Deployment.ExpectedSqlServer -or -not $builder['Integrated Security'] -or -not $builder['Encrypt'] -or $builder['TrustServerCertificate'] -or [string]::IsNullOrWhiteSpace($builder['Data Source']) -or $builder['Data Source'] -imatch 'REPLACE'){throw 'UAT SQL target/authentication/TLS mismatch'}
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
