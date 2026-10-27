[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9-]+$')][string]$RunId,
 [switch]$SkipSchemaGeneration
)
# Publishing does not load the workspace .env or initialize application/SQL code.
$script:ProjectRoot=Split-Path -Parent $PSScriptRoot
$script:DotnetPath=Join-Path $script:ProjectRoot '.local/dotnet/dotnet.exe'
if(-not(Test-Path -LiteralPath $script:DotnetPath)){$script:DotnetPath=(Get-Command dotnet -ErrorAction Stop).Source}
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$destination=Join-Path $script:ProjectRoot ('.local/publish/'+$RunId)
if(Test-Path -LiteralPath $destination){throw 'New unique package required'}
[void][IO.Directory]::CreateDirectory($destination)
foreach($project in @('Web','Worker','MigrationCli','UatDataHandoff')){
 $folder=switch($project){'Web'{'web'}'Worker'{'worker'}'MigrationCli'{'tools'}'UatDataHandoff'{'tools/uat-data'}}
 $projectFile=if($project -eq 'UatDataHandoff'){'tools/UatDataHandoff/UatDataHandoff.csproj'}else{'src/'+$project+'/'+$project+'.csproj'}
 $saved=$ErrorActionPreference;try{$ErrorActionPreference='Continue';
 $publishArgs=@('publish',(Join-Path $script:ProjectRoot $projectFile),'-c','Release','-p:RestoreLockedMode=true','-p:DebugType=None','-p:DebugSymbols=false','-p:IisHandoffPublish=true','--artifacts-path',(Join-Path $script:ProjectRoot '.local/build/iis-release'),'--output',($destination+'/'+$folder))
 if(Get-Command rtk -ErrorAction SilentlyContinue){& rtk proxy $script:DotnetPath @publishArgs *> ($destination+'/publish-'+$project+'.log')}else{& $script:DotnetPath @publishArgs *> ($destination+'/publish-'+$project+'.log')};$code=$LASTEXITCODE}finally{$ErrorActionPreference=$saved}
 if($code){throw ('Publish failed: '+$project)}
}
$ops=Join-Path $destination 'ops';[void][IO.Directory]::CreateDirectory($ops)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uat-runtime.ps1') -Destination $ops
foreach($name in @('uat-common.ps1','uat-target.example.json','prepare-uat-target.ps1','install-uat-iis.ps1','test-uat-preparation.ps1','probe-uat-defender.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $ops}
foreach($name in @('uat-data.ps1','prepare-iis-config.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $ops}
Copy-Item -LiteralPath (Join-Path $script:ProjectRoot 'docs/uat-data-handoff.md') -Destination ($ops+'/uat-data-runbook.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uat-operator-steps.md') -Destination $ops
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'export-uat-worker-tasks.ps1') -Destination $ops
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test-uat-worker-tasks.ps1') -Destination $ops
Copy-Item -LiteralPath (Join-Path $script:ProjectRoot 'docs/phase-3k-uat-plan.md') -Destination ($ops+'/UAT-16.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uat-config.example.json') -Destination $ops
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uat-operator-steps.md') -Destination ($ops+'/START.md')
# Portable framework-dependent IIS package. No machine-specific source path,
# secret, FFmpeg binary or actual environment settings enter the artifact.
$xml=@'
<?xml version="1.0" encoding="utf-8"?>
<configuration><location path="." inheritInChildApplications="false"><system.webServer><handlers><add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" /></handlers><aspNetCore processPath="dotnet" arguments=".\Web.dll" stdoutLogEnabled="false" hostingModel="inprocess"><environmentVariables><environmentVariable name="DOTNET_ENVIRONMENT" value="Staging" /><environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Staging" /><environmentVariable name="LANDPORTAL_HOME" value="D:\LandPortal-Uat-Private" /><environmentVariable name="LANDPORTAL_CONFIG" value="D:\LandPortal-Uat-Private\uat.json" /></environmentVariables></aspNetCore><security><requestFiltering><requestLimits maxAllowedContentLength="20971520" /></requestFiltering></security></system.webServer></location></configuration>
'@
[IO.File]::WriteAllText(($destination+'/web/web.config'),$xml,[Text.UTF8Encoding]::new($false))
if(-not $SkipSchemaGeneration){
 $saved=$ErrorActionPreference;try{$ErrorActionPreference='Continue';& rtk proxy $script:DotnetPath ($destination+'/tools/MigrationCli.dll') schema-script --output ($ops+'/schema-idempotent.sql') *> ($destination+'/schema-script.log');$code=$LASTEXITCODE}finally{$ErrorActionPreference=$saved};if($code){throw 'Offline schema generation failed'}
}
$records=@(foreach($dir in @('web','worker','tools','ops')){foreach($f in Get-ChildItem -LiteralPath ($destination+'/'+$dir) -File -Recurse){
 if($f.Name -match '^(\.env|appsettings\..+\.json)$' -or $f.Extension -in @('.pfx','.key','.pem','.bak') -or $f.Name -in @('ffmpeg.exe','ffprobe.exe')){throw 'Excluded secret/runtime/binary candidate in package'}
 @{path=$f.FullName.Substring($destination.Length+1).Replace('\','/');bytes=$f.Length;sha256=(Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}})
$manifest=@{schema=1;runId=$RunId;utc=[DateTimeOffset]::UtcNow.ToString('O');status='PUBLISHED_NOT_DEPLOYED';schemaIncluded=(-not $SkipSchemaGeneration);target='Staging / UNALLOCATED UAT database';proposedDatabase='LandPortal_Uat (not allocated)';files=$records;actualTargetAcceptance='NOT RUN';productionReady=$false}
[IO.File]::WriteAllText(($destination+'/manifest.json'),($manifest|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Write-Output ('UAT publish package: '+$destination+'; '+$records.Count+' hashed files; no installation/deployment/SQL changes')
