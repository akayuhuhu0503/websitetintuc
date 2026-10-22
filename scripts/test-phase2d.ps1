[CmdletBinding()]
param([ValidateSet('All','Video','V04','Finish','Scan','Scheduler')][string]$Region='Video',[switch]$SkipAudit)
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'video-test-profile.ps1')
Initialize-VideoTestTools
$stamp='video-tests-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$evidence=Join-Path $script:ProjectRoot ('.local/evidence/phase2d/'+$stamp)
if(Test-Path -LiteralPath $evidence){throw 'Evidence directory exists.'}
[IO.Directory]::CreateDirectory($evidence)|Out-Null
$build=Join-Path $script:ProjectRoot '.local/build/phase2d/current'
$legacyRoots=@('phase2a','phase2b','v04','ui')|ForEach-Object{Join-Path $script:ProjectRoot ('.local/evidence/'+$_)}
$legacyBefore=@($legacyRoots|Where-Object{Test-Path -LiteralPath $_}|ForEach-Object{Get-ChildItem -LiteralPath $_ -Recurse -File}|ForEach-Object{@{path=$_.FullName.Substring($script:ProjectRoot.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
$paths=@(& rtk proxy rg --files src tests scripts -g '!**/bin/**' -g '!**/obj/**' -g '!**/node_modules/**' -g '!**/vendor/**')
$rows=@($paths|Sort-Object|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $script:ProjectRoot $_)).Hash.ToLowerInvariant()}})
$sha=[Security.Cryptography.SHA256]::Create()
try{
 $gitState=(& rtk proxy git status --short|Out-String)
 $codeHash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($rows|ConvertTo-Json -Depth 8 -Compress))))).Replace('-','').ToLowerInvariant()
 $workingHash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($gitState+$codeHash)))).Replace('-','').ToLowerInvariant()
}finally{$sha.Dispose()}
$record=[ordered]@{runId=$stamp;startedUtc=[DateTimeOffset]::UtcNow.ToString('O');command=('scripts/test-phase2d.ps1 -Region '+$Region+' SkipAudit='+$SkipAudit);region=$Region;database='LandPortal_Test';frozenCodeSha256=$codeHash;workingStateSha256=$workingHash;code=$rows;protectedLegacyArtifacts=$legacyBefore;artifactState='INCOMPLETE';artifactComplete=$false}
[IO.File]::WriteAllText((Join-Path $evidence 'run.json'),($record|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
if($Region -in @('All','Video')){
 $fixture=Join-Path $evidence 'video-fixture.webm'
 & rtk proxy node tests/browser/create-video-fixture.mjs $fixture
 if($LASTEXITCODE -ne 0){throw 'Anonymous video fixture generation failed; no scanner/processor success implied.'}
 $env:LandPortal__VideoFixture=$fixture
}
$argsList=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'test.ps1'),'-Region',$Region,'-BuildRoot',$build,'-EvidenceRoot',$evidence)
if($SkipAudit){$argsList+='-SkipAudit'}
$previousPreference=$ErrorActionPreference;$ErrorActionPreference='Continue'
& rtk proxy powershell @argsList 2>&1|Tee-Object -FilePath (Join-Path $evidence 'run.log')
$result=$LASTEXITCODE
$ErrorActionPreference=$previousPreference
$legacyDrift=@($legacyBefore|Where-Object{(-not(Test-Path -LiteralPath (Join-Path $script:ProjectRoot $_.path))) -or (Get-FileHash -LiteralPath (Join-Path $script:ProjectRoot $_.path)).Hash.ToLowerInvariant() -cne $_.sha256}|ForEach-Object{$_.path})
$record.legacyArtifactDrift=$legacyDrift
if($legacyDrift.Count -gt 0){$result=1;Write-Output 'FAIL legacy artifact drift; retain incident and never fabricate recovery.'}
$record.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O');$record.exitCode=$result;$record.artifactComplete=$true
$record.artifactState='COMPLETE'
$record.artifacts=@(Get-ChildItem -LiteralPath $evidence -Recurse -File|Where-Object{$_.Name -ne 'run.json'}|ForEach-Object{@{path=$_.FullName.Substring($evidence.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant();bytes=$_.Length}})
[IO.File]::WriteAllText((Join-Path $evidence 'run.json'),($record|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Write-Output ('2D-VIDEO evidence: '+$evidence+'; exit='+$result)
exit $result
