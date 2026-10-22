[CmdletBinding()]
param([switch]$SkipAudit,[switch]$BaselineExistingBuild,[switch]$HttpOnly,[switch]$IsolatedBuild,[string]$BuildRoot,[string]$EvidenceRoot,[ValidateSet('All','Unblock','Batch','Inventory','Content','Accounts','Scheduler','Site','AccountHttp','SchedulerHttp','SiteHttp','HttpNoUi','V04','Interactions','FinishSql','FinishHttp','Finish','Scan','Video','V05')][string]$Region='All')
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'test-profile.ps1')
$testMutex = $null
$hasTestLock = $false
try {
    $testMutex = New-Object System.Threading.Mutex -ArgumentList $false, 'Local\LandPortal_SQL_Test_Run'
    try { $hasTestLock = $testMutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $hasTestLock = $true }
    if (-not $hasTestLock) { throw 'Another LandPortal_Test suite is running. Wait for that run to finish before starting this suite.' }
    Initialize-ProjectTestProfile
    if($EvidenceRoot){$full=[IO.Path]::GetFullPath($EvidenceRoot);if(-not (@('phase2c','phase2d','phase3a','phase3b')|Where-Object{$full.StartsWith((Join-Path $script:ProjectRoot ('.local/evidence/'+$_))+'\',[StringComparison]::OrdinalIgnoreCase)})){throw 'EvidenceRoot must be a unique phase2c run directory.'};if(Test-Path -LiteralPath (Join-Path $full 'sql-tests.json')){throw 'Refusing to overwrite test evidence.'};$env:LandPortal__EvidenceRoot=$full}
    if(-not $HttpOnly -and $Region -in @('All','Video') -and -not $env:LandPortal__VideoFixture){
        . (Join-Path $PSScriptRoot 'video-test-profile.ps1');Initialize-VideoTestTools
        $fixtureDir=Join-Path $script:ProjectRoot ('.local/tests/phase2d/fixtures/'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($fixtureDir)|Out-Null
        $env:LandPortal__VideoFixture=Join-Path $fixtureDir 'anonymous.webm'
        & rtk proxy node (Join-Path $script:ProjectRoot 'tests/browser/create-video-fixture.mjs') $env:LandPortal__VideoFixture
        if($LASTEXITCODE -ne 0){throw 'Real anonymous video fixture unavailable; video E2E BLOCKED.'}
    }
    $artifactArguments=@()
    if($BuildRoot){$BuildRoot=[IO.Path]::GetFullPath($BuildRoot);if(-not (@('phase2c','phase2d','phase3a','phase3b')|Where-Object{$BuildRoot.StartsWith((Join-Path $script:ProjectRoot ('.local/build/'+$_))+'\',[StringComparison]::OrdinalIgnoreCase)})){throw '2C BuildRoot must remain isolated.'};$IsolatedBuild=$true}
    if($IsolatedBuild){if(-not $BuildRoot){$BuildRoot=Join-Path $script:ProjectRoot '.local/build/v04'};$artifactArguments=@('--artifacts-path',$BuildRoot);$env:LandPortal__BuildRoot=$BuildRoot}
    if (-not $BaselineExistingBuild) {
        Invoke-ProjectDotnet (@('restore','LandPortal.sln','--locked-mode')+$artifactArguments)
        Invoke-ProjectDotnet (@('build','LandPortal.sln','--no-restore')+$artifactArguments)
    } else { Write-Output 'This command uses existing Debug binaries; no build or package audit evidence is implied. Match the recorded code state and build time.' }
    $runArguments=@('run','--project','tests/LandPortal.Tests/LandPortal.Tests.csproj','--no-build','--no-launch-profile')
    $runArguments+=$artifactArguments
    if($HttpOnly){$runArguments+=@('--','--http-only')}
    elseif($Region -ne 'All') { $flag=@{Unblock='--unblock-only';Batch='--batch-only';Inventory='--inventory-only';Content='--content-only';Accounts='--accounts-only';Scheduler='--scheduler-only';Site='--site-only';AccountHttp='--account-http-only';SchedulerHttp='--scheduler-http-only';SiteHttp='--site-http-only';HttpNoUi='--http-no-ui';V04='--v04-only';Interactions='--interactions-only';FinishSql='--finish-sql-only';FinishHttp='--finish-http-only';Finish='--finish-only';Scan='--scan-only';Video='--video-only';V05='--v05-only'}[$Region];$runArguments+=@('--',$flag) }
    Invoke-ProjectDotnet $runArguments
    if (-not $SkipAudit) { Invoke-ProjectDotnet @('list','LandPortal.sln','package','--vulnerable','--include-transitive') }
    if($HttpOnly -or $Region -ne 'All') { Write-Output ('PASS executed focused region '+$Region+' on real SQL Test; other regions NOT RUN in this command. Read per-run evidence.') }
    elseif($BaselineExistingBuild) { Write-Output 'PASS executed baseline SQL risk tests with existing Debug binaries; build NOT RUN.' }
    else { Write-Output 'PASS build and executed SQL risk tests. UI and deferred modules have separate evidence.' }
    exit 0
} catch { Write-Error ('Tests failed: ' + $_.Exception.GetType().Name + '; inspect output and .local/evidence/sql-tests.json.'); exit 1 }
finally {
    if ($hasTestLock) { $testMutex.ReleaseMutex() }
    if ($null -ne $testMutex) { $testMutex.Dispose() }
}
