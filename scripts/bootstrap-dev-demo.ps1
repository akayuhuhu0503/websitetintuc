param(
 [Parameter(Mandatory=$true)][string]$Cli,
 [Parameter(Mandatory=$true)][ValidateSet('LandPortal_Dev','LandPortal_Test')][string]$Database,
 [Parameter(Mandatory=$true)][string]$ConfirmTarget,
 [Parameter(Mandatory=$true)][string]$BackupReceipt,
 [int]$CategoryId,
 [switch]$Disable
)
. (Join-Path $PSScriptRoot 'common.ps1')
if($env:ASPNETCORE_ENVIRONMENT -cne 'Development' -or $env:DOTNET_ENVIRONMENT -in @('Staging','Production')){throw 'Explicit Development environment required'}
if($env:Sql__ExpectedDatabase -cne $Database){throw 'Expected database differs; load an explicit private environment before use'}
$cliPath=[IO.Path]::GetFullPath($Cli)
if(-not(Test-Path -LiteralPath $cliPath -PathType Leaf)){throw 'Built MigrationCli.dll required'}
$folder=Join-Path $script:ProjectRoot '.local/credentials'
if(Test-Path -LiteralPath $folder){if((Get-Item -LiteralPath $folder).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse credential folder refused'}}
[void][IO.Directory]::CreateDirectory($folder)
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
& rtk proxy icacls $folder /inheritance:r /grant:r ('*'+$sid.Value+':(OI)(CI)F') '*S-1-5-18:(OI)(CI)F'
if($LASTEXITCODE -ne 0){throw 'Cannot protect private credential folder; no bootstrap executed'}
$credentialFile=Join-Path $folder ('dev-demo-'+[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N')+'.dpapi')
$arguments=@($cliPath,'dev-demo-accounts','--confirm-target',$ConfirmTarget,'--backup-receipt',$BackupReceipt,'--category',$CategoryId.ToString(),'--credentials',$credentialFile)
if($Disable){$arguments+='--disable'}
& rtk proxy $script:DotnetPath @arguments
if($LASTEXITCODE -ne 0){throw 'Demo account operation rejected; inspect target before retry'}
if(Test-Path -LiteralPath $credentialFile){Write-Output ('Credentials encrypted for this Windows user: '+$credentialFile)}
