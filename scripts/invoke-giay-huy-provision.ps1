[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ViewPath,[Parameter(Mandatory=$true)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$ViewSha256,
 [Parameter(Mandatory=$true)][string]$PrivateRoot,[Parameter(Mandatory=$true)][switch]$ApprovedLanUatSourceTls)
$ErrorActionPreference='Stop'
 $full=[IO.Path]::GetFullPath($PrivateRoot);$repo=Split-Path -Parent $PSScriptRoot
 if($full.StartsWith($repo.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or $full -ieq $repo -or $full -match '(?i)[\\/]wwwroot([\\/]|$)'){throw 'Private root must stay outside repository and webroot'}
if(-not $ApprovedLanUatSourceTls){throw 'Explicit approved source LAN/UAT TLS exception required'}
if(-not [IO.Path]::IsPathRooted($PrivateRoot) -or -not(Test-Path $PrivateRoot -PathType Container) -or (Get-ChildItem -LiteralPath $PrivateRoot -Force|Select-Object -First 1)){throw 'Use an existing empty private directory outside repository/webroot with operator-only ACL'}
$acl=Get-Acl -LiteralPath $PrivateRoot
if(-not $acl.AreAccessRulesProtected){throw 'Private directory must have inheritance disabled and operator-only access'}
$allowed=@([Security.Principal.WindowsIdentity]::GetCurrent().User.Value,'S-1-5-18')
foreach($rule in $acl.Access){if($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -notin $allowed){throw 'Private directory permits another principal'}}
if((Get-FileHash -LiteralPath $ViewPath -Algorithm SHA256).Hash -ine $ViewSha256){throw 'View SHA mismatch'}
$credential=Get-Credential -Message 'Approved source view/login only. Never use runtime credentials for provisioning.'
if(-not $credential){throw 'Credential cancelled'}
$b=New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$b['Data Source']='45.120.229.43,1433';$b['Initial Catalog']='QuanLyDuAnTrungTamDuLieuDatDai';$b['User ID']=$credential.UserName;$b['Password']=$credential.GetNetworkCredential().Password
$b['Encrypt']=$true;$b['TrustServerCertificate']=$true;$b['Connect Timeout']=5;$b['ConnectRetryCount']=0
$connection=[System.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
try{
 $connection.Open();. (Join-Path $PSScriptRoot 'provision-giay-huy-view.ps1')
 Invoke-GiayHuyProvision -Connection $connection -PrivateRoot $PrivateRoot -ViewPath (Resolve-Path $ViewPath).Path -ExpectedViewSha $ViewSha256.ToUpperInvariant()
 Write-Output 'Source view/login verified. Secret remains in private config only.'
}catch{
 $e=$_.Exception.GetBaseException();$number=$null;if($e -is [System.Data.SqlClient.SqlException]){$number=$e.Number}
 [IO.File]::WriteAllText((Join-Path $PrivateRoot 'blocked.json'),(@{status='BLOCKED_RECONCILE_BEFORE_RETRY';stage=$script:GiayHuyProvisionStage;sqlNumber=$number;type=$e.GetType().Name}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
 throw 'Provisioning blocked. Inspect private stage receipt; never recreate/reset existing objects.'
}finally{$connection.Dispose();$b.Clear();$credential=$null}
