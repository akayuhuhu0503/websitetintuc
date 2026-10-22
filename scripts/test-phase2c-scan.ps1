[CmdletBinding()]
param([ValidateSet('All','Scan')][string]$Region='Scan',[switch]$SkipAudit)
. (Join-Path $PSScriptRoot 'common.ps1')
$stamp='scan-tests-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$evidence=Join-Path $script:ProjectRoot ('.local/evidence/phase2c/'+$stamp)
if(Test-Path -LiteralPath $evidence){throw 'Evidence directory exists.'}
[IO.Directory]::CreateDirectory($evidence)|Out-Null
$build=Join-Path $script:ProjectRoot '.local/build/phase2c/scan-current'
$paths=@(& rtk proxy rg --files src tests scripts -g '!**/bin/**' -g '!**/obj/**' -g '!**/node_modules/**' -g '!**/vendor/**')
$rows=@($paths|Sort-Object|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $script:ProjectRoot $_)).Hash.ToLowerInvariant()}})
$sha=[Security.Cryptography.SHA256]::Create()
try{
 $gitState=(& rtk proxy git status --short|Out-String)
 $codeHash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($rows|ConvertTo-Json -Depth 8 -Compress))))).Replace('-','').ToLowerInvariant()
 $workingHash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($gitState+$codeHash)))).Replace('-','').ToLowerInvariant()
}finally{$sha.Dispose()}
$record=[ordered]@{runId=$stamp;startedUtc=[DateTimeOffset]::UtcNow.ToString('O');command=('scripts/test-phase2c-scan.ps1 -Region '+$Region+' SkipAudit='+$SkipAudit);region=$Region;database='LandPortal_Test';frozenCodeSha256=$codeHash;workingStateSha256=$workingHash;code=$rows;artifactState='INCOMPLETE';artifactComplete=$false}
[IO.File]::WriteAllText((Join-Path $evidence 'run.json'),($record|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
$argsList=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'test.ps1'),'-Region',$Region,'-BuildRoot',$build,'-EvidenceRoot',$evidence)
if($SkipAudit){$argsList+='-SkipAudit'}
$previousPreference=$ErrorActionPreference;$ErrorActionPreference='Continue'
& rtk proxy powershell @argsList 2>&1|Tee-Object -FilePath (Join-Path $evidence 'run.log')
$result=$LASTEXITCODE
$ErrorActionPreference=$previousPreference
$record.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O');$record.exitCode=$result;$record.artifactComplete=$true
$record.artifactState='COMPLETE'
$record.artifacts=@(Get-ChildItem -LiteralPath $evidence -Recurse -File|Where-Object{$_.Name -ne 'run.json'}|ForEach-Object{@{path=$_.FullName.Substring($evidence.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant();bytes=$_.Length}})
[IO.File]::WriteAllText((Join-Path $evidence 'run.json'),($record|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Write-Output ('2C-SCAN evidence: '+$evidence+'; exit='+$result)
exit $result
