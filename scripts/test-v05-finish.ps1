[CmdletBinding()]
param([ValidateSet('All','V05','Interactions','Finish','Scan','Content','SiteHttp','Scheduler','Video','HttpNoUi')][string]$Region='V05',[switch]$SkipAudit)
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'video-test-profile.ps1')
Initialize-VideoTestTools
$id='v05-finish-tests-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$root=Join-Path $script:ProjectRoot ('.local/evidence/phase2d/'+$id)
if(Test-Path -LiteralPath $root){throw 'Unique evidence path already exists.'}
[IO.Directory]::CreateDirectory($root)|Out-Null
@{runId=$id;region=$Region;startedUtc=[DateTimeOffset]::UtcNow.ToString('O');state='PREFLIGHT_STARTED';note='A preflight marker is not a completed test result.'}|ConvertTo-Json|Set-Content -LiteralPath ($root+'/preflight.json') -Encoding UTF8
# Approved existing phase2d evidence/build boundary is reused with a distinct v05 namespace.
$build=Join-Path $script:ProjectRoot '.local/build/phase2d/v05-finish-current'
$legacy=@('phase2a','phase2b','v04','ui','phase2c','phase2d')|ForEach-Object{Join-Path $script:ProjectRoot ('.local/evidence/'+$_)}
$protected=@($legacy|Where-Object{Test-Path -LiteralPath $_}|ForEach-Object{Get-ChildItem -LiteralPath $_ -File -Recurse}|Where-Object{-not $_.FullName.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -and $_.Name -notmatch '^((web-)?live\.(stdout|stderr)|preview(-error)?)\.log$'}|ForEach-Object{@{path=$_.FullName.Substring($script:ProjectRoot.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
$files=@(& rtk proxy rg --files src tests scripts -g '!**/bin/**' -g '!**/obj/**' -g '!**/node_modules/**' -g '!**/vendor/**')
$code=@($files|Sort-Object|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()}})
$record=[ordered]@{runId=$id;startedUtc=[DateTimeOffset]::UtcNow.ToString('O');region=$Region;database='LandPortal_Test';code=$code;protectedArtifacts=$protected;complete=$false}
$record|ConvertTo-Json -Depth 8|Set-Content -LiteralPath ($root+'/run.json') -Encoding UTF8
$list=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'test.ps1'),'-Region',$Region,'-BuildRoot',$build,'-EvidenceRoot',$root)
if($SkipAudit){$list+='-SkipAudit'}
$prior=$ErrorActionPreference;$ErrorActionPreference='Continue'
& rtk proxy powershell @list 2>&1|Tee-Object -FilePath ($root+'/run.log')
$result=$LASTEXITCODE;$ErrorActionPreference=$prior
$record.artifactDrift=@($protected|Where-Object{(-not(Test-Path -LiteralPath $_.path)) -or (Get-FileHash -LiteralPath $_.path).Hash.ToLowerInvariant() -cne $_.sha256}|ForEach-Object{$_.path})
$record.codeDrift=@($code|Where-Object{(-not(Test-Path -LiteralPath $_.path)) -or (Get-FileHash -LiteralPath $_.path).Hash.ToLowerInvariant() -cne $_.sha256}|ForEach-Object{$_.path})
if($record.artifactDrift.Count -or $record.codeDrift.Count){$result=1}
$record.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O');$record.exitCode=$result;$record.complete=$true
$record.artifacts=@(Get-ChildItem -LiteralPath $root -Recurse -File|Where-Object{$_.Name -ne 'run.json'}|ForEach-Object{@{path=$_.FullName.Substring($root.Length+1);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
$record|ConvertTo-Json -Depth 8|Set-Content -LiteralPath ($root+'/run.json') -Encoding UTF8
Write-Output ('V05-Finish evidence '+$root+'; exit='+$result)
exit $result
