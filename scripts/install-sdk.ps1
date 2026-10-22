[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = '10.0.112'
$destination = Join-Path $projectRoot '.local/dotnet'
try {
    $metadata = Invoke-RestMethod -Uri 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json'
    $release = $metadata.releases | Where-Object { $_.'release-version' -eq '10.0.12' }
    $sdk = $release.sdks | Where-Object { $_.version -eq $version }
    $archive = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' }
    if (-not $archive -or $archive.url -notmatch '^https://(builds\.dotnet\.microsoft\.com|download\.visualstudio\.microsoft\.com)/') { throw 'Official SDK archive could not be verified.' }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $zipPath = Join-Path $projectRoot '.local/dotnet-sdk.zip'
    Invoke-WebRequest -Uri $archive.url -OutFile $zipPath -UseBasicParsing
    $actualHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA512).Hash
    if ($actualHash -ne $archive.hash) { throw 'SDK archive SHA512 mismatch; archive was not expanded.' }
    Expand-Archive -LiteralPath $zipPath -DestinationPath $destination -Force
    & (Join-Path $destination 'dotnet.exe') --list-sdks
    if ($LASTEXITCODE -ne 0) { throw 'Installed SDK validation failed.' }
    & (Join-Path $destination 'dotnet.exe') --list-runtimes
    if ($LASTEXITCODE -ne 0) { throw 'Installed runtime validation failed.' }
    Write-Output "PASS isolated SDK $version; official SHA512 validated."
    exit 0
} catch {
    Write-Error ('SDK install failed: ' + $_.Exception.Message)
    exit 1
}
