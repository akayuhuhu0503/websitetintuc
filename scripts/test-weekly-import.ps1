[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SourceZip)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = (Resolve-Path -LiteralPath $SourceZip).Path
$run = Join-Path $root ('.local\tests\weekly-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$dotnet = Join-Path $root '.local\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
Push-Location $root
try {
    & $dotnet build tests/LandPortal.WeeklyImportTests/LandPortal.WeeklyImportTests.csproj -c Release --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    & $dotnet tests/LandPortal.WeeklyImportTests/bin/Release/net10.0/LandPortal.WeeklyImportTests.dll $root $source $run
    if ($LASTEXITCODE -ne 0) { throw "Focused checks failed. See $run" }
    Write-Host "PASS: $run"
} finally { Pop-Location }
