# Invoked as a detached process by the local Windows process provider, not a service.
param([Parameter(Mandatory=$true)][string]$RtkPath)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$state=Get-Content (Join-Path $repo '.local/lan-demo/current.json') -Raw -Encoding UTF8|ConvertFrom-Json
$env:LANDPORTAL_CONFIG=$state.config;$env:LANDPORTAL_HOME=$state.home
$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development'
foreach($entry in @(Get-ChildItem Env:)){if($entry.Name -match '^(ConnectionStrings|Sql|Runtime|Storage|Public|Deployment|Mail|Scanner|Media|Video|InquiryFiles|AdminSession|Captcha|SourceImport)__'){[Environment]::SetEnvironmentVariable($entry.Name,$null,'Process')}}
Set-Location $repo
$dotnet=Join-Path $repo '.local/dotnet/dotnet.exe'
$stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$ErrorActionPreference='Continue' # Native stderr is logged; process exit still determines failure.
& $RtkPath proxy $dotnet $state.web --contentRoot (Join-Path $repo 'src/Web') *> (Join-Path $state.home ('detached-'+$stamp+'.log'))
exit $LASTEXITCODE
