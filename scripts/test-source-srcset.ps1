[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
$id='srcset-tests-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$root=Join-Path $script:ProjectRoot ('.local/evidence/phase3f/'+$id)
$build=Join-Path $script:ProjectRoot ('.local/build/'+$id)
if((Test-Path $root) -or (Test-Path $build)){throw 'Unique evidence/build required'}
[void][IO.Directory]::CreateDirectory($root)
$paths=@('src/MigrationCli/SourceParser.cs','src/MigrationCli/SourceSrcset.cs','tests/LandPortal.Tests/SrcsetParserTests.cs','tests/LandPortal.Tests/Program.cs','scripts/test-source-srcset.ps1')
$code=@($paths|ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})
$record=[ordered]@{runId=$id;startedUtc=[DateTimeOffset]::UtcNow.ToString('O');scope='Four offline srcset parser groups only; no SQL opens/writes, fixture copies, scans or source requests';code=$code;complete=$false}
$connection=Get-ProjectConnectionBuilder 'LandPortal_Test'
$env:ConnectionStrings__Portal=$connection.ConnectionString;$env:Sql__ExpectedDatabase='LandPortal_Test'
$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false';$env:LandPortal__EvidenceRoot=$root
$exit=0
try {
 foreach($job in @(@('restore',@('restore','tests/LandPortal.Tests/LandPortal.Tests.csproj','--locked-mode','--artifacts-path',$build)),@('build',@('build','tests/LandPortal.Tests/LandPortal.Tests.csproj','--no-restore','--artifacts-path',$build)),@('tests',@((Join-Path $build 'bin/LandPortal.Tests/debug/LandPortal.Tests.dll'),'--srcset-only')))){
  $saved=$ErrorActionPreference;try{$ErrorActionPreference='Continue';& rtk proxy $script:DotnetPath @($job[1]) *> ($root+'/'+$job[0]+'.log');$exit=$LASTEXITCODE}finally{$ErrorActionPreference=$saved}
  Get-Content ($root+'/'+$job[0]+'.log') -Tail 7
  if($exit){throw ('Stopped '+$job[0])}
 }
}catch{$exit=1;$_|Out-String|Set-Content -LiteralPath ($root+'/failure.log') -Encoding UTF8}
$record.codeDrift=@($code|Where-Object {(Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $_.sha256}|ForEach-Object {$_.path})
if($record.codeDrift.Count){$exit=1}
$record.complete=$true;$record.exitCode=$exit;$record.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O')
$record|ConvertTo-Json -Depth 8|Set-Content -LiteralPath ($root+'/run.json') -Encoding UTF8
Write-Output ('Srcset-only evidence '+$root+'; exit='+$exit+'; full regression NOT RUN')
exit $exit
