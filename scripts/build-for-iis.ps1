[CmdletBinding()]
param([switch]$Replace)
# Build only: never sources common.ps1/.env or starts a Web/Worker/SQL tool.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$repo=Split-Path -Parent $PSScriptRoot
$base=[IO.Path]::GetFullPath((Join-Path $repo '.local/publish'))
$output=Join-Path $base 'TTDL-IIS';$staging=Join-Path $base 'TTDL-IIS-building';$previous=Join-Path $base 'TTDL-IIS.previous'
foreach($path in @($base,$output,$staging,$previous)){
 for($node=$path;$node;$node=[IO.Path]::GetDirectoryName($node)){
  if((Test-Path -LiteralPath $node)-and ((Get-Item -LiteralPath $node).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse publish path refused'}
 }
}
if(Test-Path -LiteralPath $staging){throw 'Incomplete TTDL-IIS-building exists; inspect its logs and preserve/remove it explicitly before retry. No automatic deletion.'}
if(Test-Path -LiteralPath $output){
 if(-not $Replace){throw 'TTDL-IIS already exists; no overwrite. Use -Replace only for an unused, unsealed build. The previous directory and its manifest will be retained.'}
 $marker=Join-Path $output 'build-only.json'
 if(-not(Test-Path -LiteralPath $marker)){throw 'Unknown/sealed output refused'}
 $old=Get-Content -LiteralPath $marker -Raw -Encoding UTF8|ConvertFrom-Json
 if($old.kind -cne 'TTDL-IIS-BUILD-ONLY' -or $old.sealed -ne $false -or (Test-Path -LiteralPath $previous)){throw 'Sealed output or retained previous build refused; operator must preserve handoff evidence separately'}
 foreach($process in Get-CimInstance Win32_Process){
  if(([string]$process.ExecutablePath).StartsWith($output+'\',[StringComparison]::OrdinalIgnoreCase)-or ([string]$process.CommandLine).IndexOf($output,[StringComparison]::OrdinalIgnoreCase)-ge 0){throw 'Output is referenced by a running process; stop through its approved runbook first'}
 }
 $iis=Join-Path $env:windir 'System32/inetsrv/config/applicationHost.config'
 if(Test-Path -LiteralPath $iis){
  # Refuse on read denial, and on an active IIS path; never edit IIS.
  if(([IO.File]::ReadAllText($iis)).IndexOf($output,[StringComparison]::OrdinalIgnoreCase)-ge 0){throw 'Output is referenced by IIS; no replacement permitted'}
 }
}
$dotnet=Join-Path $repo '.local/dotnet/dotnet.exe'
if(-not(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$node=(Get-Command node -ErrorAction Stop).Source;$npm=(Get-Command npm.cmd -ErrorAction Stop).Source
function Invoke-BuildCommand([string]$Exe,[string[]]$CommandArgs){
 $saved=$ErrorActionPreference
 try{$ErrorActionPreference='Continue';if(Get-Command rtk -ErrorAction SilentlyContinue){& rtk proxy $Exe @CommandArgs}else{& $Exe @CommandArgs};$code=$LASTEXITCODE}finally{$ErrorActionPreference=$saved}
 if($code -ne 0){throw ('Build command failed: '+[IO.Path]::GetFileName($Exe)+' (exit '+$code+'). No deployment performed.')}
}
Push-Location $repo
try{
 $expected=(Get-Content -LiteralPath (Join-Path $repo 'global.json') -Raw|ConvertFrom-Json).sdk.version
 $version=(& $dotnet --version|Out-String).Trim();if($LASTEXITCODE -ne 0 -or $version -cne $expected){throw ('Install .NET SDK '+$expected+' from global.json; selected SDK differs.')}
 Write-Output ('SDK '+$version+'; checking Node.js/npm')
 Invoke-BuildCommand $node @('--version');Invoke-BuildCommand $npm @('--version')
 Push-Location (Join-Path $repo 'src/Web')
 try{
  # npm ci uses the committed lockfile and never changes it.
  Invoke-BuildCommand $npm @('ci','--no-audit','--no-fund')
  Invoke-BuildCommand $npm @('run','build')
 }finally{Pop-Location}
 # Reuse the existing publisher, but do not generate/run schema or application code.
 & (Join-Path $PSScriptRoot 'package-uat.ps1') -RunId 'TTDL-IIS-building' -SkipSchemaGeneration
 if(-not(Test-Path -LiteralPath (Join-Path $staging 'manifest.json'))){throw 'Publisher did not complete'}
 Copy-Item -LiteralPath (Join-Path $repo 'docs/iis-one-command.md') -Destination (Join-Path $staging 'START.md')
 $config=Join-Path $staging 'web/web.config'
 [xml]$xml=Get-Content -LiteralPath $config -Raw -Encoding UTF8
 foreach($v in $xml.configuration.location.'system.webServer'.aspNetCore.environmentVariables.environmentVariable){
  if($v.name -eq 'LANDPORTAL_HOME'){$v.value='__TTDL_PRIVATE_HOME_REQUIRED__'}
  if($v.name -eq 'LANDPORTAL_CONFIG'){$v.value='__TTDL_PRIVATE_CONFIG_REQUIRED__'}
 }
 $xml.Save($config)
 $required=@('web/Web.dll','web/Web.runtimeconfig.json','web/web.config','web/wwwroot/css/site.css','web/wwwroot/vendor/jodit/jodit.min.js','worker/Worker.dll','worker/Worker.runtimeconfig.json','tools/MigrationCli.dll','tools/uat-data/UatDataHandoff.dll','ops/uat-runtime.ps1','ops/uat-data.ps1','ops/prepare-iis-config.ps1','ops/uat-config.example.json','START.md')
 foreach($relative in $required){if(-not(Test-Path -LiteralPath (Join-Path $staging $relative) -PathType Leaf)){throw ('Missing publish output: '+$relative)}}
 $records=@(foreach($folder in @('web','worker','tools','ops')){foreach($f in Get-ChildItem -LiteralPath (Join-Path $staging $folder) -Recurse -File){
  if($f.Name -like '.env*' -or $f.Extension -in @('.pdb','.bak','.mdf','.ldf','.key','.pfx','.pem','.dpapi') -or $f.Name -match 'tai-khoan|credentials'){throw 'Excluded file found; output not released'}
  @{path=$f.FullName.Substring($staging.Length+1).Replace('\','/');bytes=$f.Length;sha256=(Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
 }})
 $encoding=[Text.UTF8Encoding]::new($false)
 $records+=@{path='START.md';bytes=(Get-Item -LiteralPath (Join-Path $staging 'START.md')).Length;sha256=(Get-FileHash -LiteralPath (Join-Path $staging 'START.md') -Algorithm SHA256).Hash.ToLowerInvariant()}
 $manifest=@{schema=1;runId='TTDL-IIS';utc=[DateTimeOffset]::UtcNow.ToString('O');status='PUBLISHED_NOT_CONFIGURED_NOT_DEPLOYED';schemaIncluded=$false;sqlExecuted=$false;servicesInstalled=0;workerModes=@('Scheduler','Documents','InquiryScan','Video','Mail');files=$records;productionReady=$false}
 [IO.File]::WriteAllText((Join-Path $staging 'manifest.json'),($manifest|ConvertTo-Json -Depth 8),$encoding)
 [IO.File]::WriteAllText((Join-Path $staging 'build-only.json'),(@{kind='TTDL-IIS-BUILD-ONLY';sealed=$false;manifestSha256=(Get-FileHash -LiteralPath (Join-Path $staging 'manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()}|ConvertTo-Json),$encoding)
 if(Test-Path -LiteralPath $output){Move-Item -LiteralPath $output -Destination $previous}
 Move-Item -LiteralPath $staging -Destination $output
 Write-Output ('PUBLISH PASS: '+$output)
 Write-Output ('Copy to IIS: '+(Join-Path $output 'web'))
 Write-Output 'Web + Worker (5 modes) + MigrationCli + UatDataHandoff + ops. SQL/configuration/deployment NOT RUN. Read START.md.'
}finally{Pop-Location}
