param([Parameter(Mandatory=$true)][string]$SavedQlddSnapshot,[string]$DotnetPath='dotnet',[switch]$WaitForBrowser,[switch]$ImagesOnly,[switch]$CycleOnly)
$ErrorActionPreference='Stop'
$checkout=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$instance='LPSync_'+[Guid]::NewGuid().ToString('N').Substring(0,12)
$run=Join-Path $checkout ('.local\tests\source-sync-'+$instance)
$dotnet=Get-Command $DotnetPath -ErrorAction Stop
if(!(Test-Path -LiteralPath $SavedQlddSnapshot)){throw 'Missing saved public QLDD article snapshot'}
& $dotnet.Source build (Join-Path $checkout 'tests\LandPortal.SourceSyncTests\LandPortal.SourceSyncTests.csproj') -c Release -v minimal
if($LASTEXITCODE-ne 0){throw 'Focused test build failed'}
& SqlLocalDB create $instance
if($LASTEXITCODE-ne 0){throw 'Cannot create isolated LocalDB instance'}
try {
    if($WaitForBrowser){$env:SOURCE_SYNC_BROWSER='1'}
    & SqlLocalDB start $instance
    if($LASTEXITCODE-ne 0){throw 'Cannot start isolated LocalDB instance'}
    $testArgs=@($checkout,$instance,$run,([IO.Path]::GetFullPath($SavedQlddSnapshot)))
    if($ImagesOnly){$testArgs+='--image-only'}
    if($CycleOnly){$testArgs+='--cycle-only'}
    & $dotnet.Source (Join-Path $checkout 'tests\LandPortal.SourceSyncTests\bin\Release\net10.0\LandPortal.SourceSyncTests.dll') @testArgs
    $testExit=$LASTEXITCODE
    Write-Output ('Focused result directory: '+$run)
    if($testExit-ne 0){throw 'Source sync checks failed; inspect failure.txt in the isolated run'}
} finally {
    Remove-Item Env:SOURCE_SYNC_BROWSER -ErrorAction SilentlyContinue
    & SqlLocalDB stop $instance
    # Keep test files for inspection; never delete or stop other instances.
}
