[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    Push-Location (Join-Path $script:ProjectRoot 'tests/browser')
    try {
        & rtk proxy npm ci --ignore-scripts
        if ($LASTEXITCODE -ne 0) { throw 'Browser test dependency restore failed.' }
        & rtk proxy npm test
        if ($LASTEXITCODE -ne 0) { throw 'Browser checks failed; inspect private UI report.' }
    } finally { Pop-Location }
    exit 0
} catch { Write-Error ('UI checks failed: ' + $_.Exception.Message); exit 1 }
