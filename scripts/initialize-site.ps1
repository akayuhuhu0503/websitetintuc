[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('LandPortal_Dev','LandPortal_Test')][string]$Database,[switch]$NoBuild)
. (Join-Path $PSScriptRoot 'common.ps1')
$builder=Get-ProjectConnectionBuilder $Database
$env:ConnectionStrings__Portal=$builder.ConnectionString
$env:Sql__ExpectedDatabase=$Database
$env:ASPNETCORE_ENVIRONMENT='Production'
$env:Media__AllowUnscannedDevelopmentUploads='false'
Write-Output ('Explicit one-time configuration initialization target '+$Database+'; no source payload or Identity grants changed.')
$cliArgs=@('run','--project','src/MigrationCli/MigrationCli.csproj','--no-launch-profile');if($NoBuild){$cliArgs+='--no-build'};$cliArgs+=@('--','site-initialize')
Invoke-ProjectDotnet $cliArgs
