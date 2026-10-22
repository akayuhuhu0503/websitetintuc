[CmdletBinding()]
param([string]$OutputDirectory = '.local/publish/phase2b/web')
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    $outputPath = if([IO.Path]::IsPathRooted($OutputDirectory)){$OutputDirectory}else{Join-Path $script:ProjectRoot $OutputDirectory}
    $output = [IO.Path]::GetFullPath($outputPath)
    $publishRoot = [IO.Path]::GetFullPath((Join-Path $script:ProjectRoot '.local/publish')) + [IO.Path]::DirectorySeparatorChar
    if(-not $output.StartsWith($publishRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Publish output must remain in this project .local/publish directory.'}
    Invoke-ProjectDotnet @('publish','src/Web/Web.csproj','--configuration','Release','--output',$output,'-p:RestoreLockedMode=true')
    if (Test-Path -LiteralPath (Join-Path $output '.env')) { throw 'Unexpected secret file in publish output.' }
    Write-Output ('PASS local publish package created at '+$output+'. No deployment performed.')
    exit 0
} catch { Write-Error ('Publish failed: ' + $_.Exception.GetType().Name + '; inspect build output.'); exit 1 }
