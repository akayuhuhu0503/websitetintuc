[CmdletBinding()]
param([ValidateSet('All','Interactions','V04','HttpNoUi')][string]$Region='Interactions',[switch]$SkipAudit)
. (Join-Path $PSScriptRoot 'common.ps1')
$stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$evidence=Join-Path $script:ProjectRoot ('.local/evidence/phase2c/tests-'+$stamp)
if(Test-Path -LiteralPath $evidence){throw 'Evidence directory exists.'};[IO.Directory]::CreateDirectory($evidence)|Out-Null
$build=Join-Path $script:ProjectRoot '.local/build/phase2c/current'
$paths=@(& rtk proxy rg --files src tests scripts -g '!**/bin/**' -g '!**/obj/**' -g '!**/node_modules/**' -g '!**/vendor/**')
$rows=@($paths|Sort-Object|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $script:ProjectRoot $_)).Hash.ToLowerInvariant()}})
$record=[ordered]@{runId=$stamp;startedUtc=[DateTimeOffset]::UtcNow.ToString('O');region=$Region;database='LandPortal_Test';code=$rows;artifactComplete=$false}
[IO.File]::WriteAllText((Join-Path $evidence 'run.json'),($record|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
$argsList=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'test.ps1'),'-Region',$Region,'-BuildRoot',$build,'-EvidenceRoot',$evidence)
if($SkipAudit){$argsList+='-SkipAudit'}
$previousPreference=$ErrorActionPreference;$ErrorActionPreference='Continue'
& rtk proxy powershell @argsList 2>&1|Tee-Object -FilePath (Join-Path $evidence 'run.log')
$result=$LASTEXITCODE
$ErrorActionPreference=$previousPreference
$record.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O');$record.exitCode=$result;$record.artifactComplete=$true
$record.artifacts=@(Get-ChildItem -LiteralPath $evidence -Recurse -File|Where-Object{$_.Name -ne 'run.json'}|ForEach-Object{@{path=$_.FullName.Substring($evidence.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant();bytes=$_.Length}})
[IO.File]::WriteAllText((Join-Path $evidence 'run.json'),($record|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Write-Output ('2C evidence: '+$evidence+'; exit='+$result)
exit $result
