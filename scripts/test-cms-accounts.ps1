param([Parameter(Mandatory=$true)][string]$DotnetPath)
$ErrorActionPreference='Stop'
$fixtureId=[Guid]::NewGuid().ToString('N').Substring(0,12)
$fixtureName='LPCms_'+$fixtureId
$fixtureRun=Join-Path (Get-Location) ('.local\tests\cms-account-'+$fixtureId)
$testDll=Join-Path (Get-Location) 'tests\LandPortal.CmsAccountTests\bin\Release\net10.0\LandPortal.CmsAccountTests.dll'
if(!(Test-Path -LiteralPath $testDll)){throw 'Build LandPortal.CmsAccountTests Release first.'}
& SqlLocalDB create $fixtureName -s
if($LASTEXITCODE -ne 0){throw 'Cannot create isolated LocalDB instance.'}
$fixtureExit=1
try {
    & $DotnetPath $testDll $fixtureName $fixtureRun
    $fixtureExit=$LASTEXITCODE
} finally {
    & SqlLocalDB stop $fixtureName
}
Write-Output ('Fixture='+$fixtureName+' Run='+$fixtureRun)
exit $fixtureExit
