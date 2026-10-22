[CmdletBinding()]
param([string]$OutputDirectory='.local/publish/phase2b/worker')
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    if([IO.Path]::IsPathRooted($OutputDirectory)){$resolved=[IO.Path]::GetFullPath($OutputDirectory)}else{$resolved=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $OutputDirectory))}
    $allowed=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot '.local/publish'))+'\'
    if(-not $resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Worker package must stay in this project .local/publish.'}
    Invoke-ProjectDotnet @('restore','src/Worker/Worker.csproj','--locked-mode')
    Invoke-ProjectDotnet @('publish','src/Worker/Worker.csproj','-c','Release','--no-restore','-o',$resolved)
    if(Get-ChildItem -LiteralPath $resolved -Recurse -Force -File|Where-Object {$_.Name -eq '.env'}){throw 'Private environment file appeared in worker package.'}
    if(-not (Test-Path -LiteralPath (Join-Path $resolved 'Worker.dll'))){throw 'Worker runtime assembly is missing.'}
    Write-Output "Local worker package: $resolved. No service was installed; configure environment and run console explicitly."
    exit 0
} catch {Write-Error ('Worker packaging failed: '+$_.Exception.GetType().Name+'; inspect private project logs.');exit 1}
