[CmdletBinding()]
param([ValidateRange(1024,65535)][int]$Port = 5188, [switch]$NoBuild, [switch]$AllowUnscannedMedia,[switch]$IsolatedBuild,[switch]$Phase2C,[switch]$Phase2CFinish,[switch]$Phase2CScan,[switch]$Phase2D,[switch]$V05,[switch]$V05Finish)
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    $builder = Get-ProjectConnectionBuilder 'LandPortal_Dev'
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Portal', $builder.ConnectionString, 'Process')
    [Environment]::SetEnvironmentVariable('Sql__ExpectedDatabase', 'LandPortal_Dev', 'Process')
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Development', 'Process')
    $url = "http://127.0.0.1:$Port"
    [Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', $url, 'Process')
    [Environment]::SetEnvironmentVariable('Public__BaseUrl', $url, 'Process')
    [Environment]::SetEnvironmentVariable('Media__AllowUnscannedDevelopmentUploads', $(if($AllowUnscannedMedia){'true'}else{'false'}), 'Process')
    $arguments = @('run','--project','src/Web/Web.csproj','--no-launch-profile')
    if ($NoBuild) { $arguments += '--no-build' }
    if($V05Finish){$arguments+=@('--artifacts-path',(Join-Path $script:ProjectRoot '.local/build/phase2d/v05-finish-current'))}
    elseif($V05){$arguments+=@('--artifacts-path',(Join-Path $script:ProjectRoot '.local/build/phase2d/v05-current'))}
    elseif($Phase2D){$arguments+=@('--artifacts-path',(Join-Path $script:ProjectRoot '.local/build/phase2d/current'))}
    elseif($Phase2CScan){$arguments+=@('--artifacts-path',(Join-Path $script:ProjectRoot '.local/build/phase2c/scan-current'))}
    elseif($Phase2CFinish){$arguments+=@('--artifacts-path',(Join-Path $script:ProjectRoot '.local/build/phase2c/finish-current'))}
    elseif($Phase2C){$arguments+=@('--artifacts-path',(Join-Path $script:ProjectRoot '.local/build/phase2c/current'))}
    elseif($IsolatedBuild){$arguments+=@('--artifacts-path',(Join-Path $script:ProjectRoot '.local/build/v04'))}
    Write-Output "Local web: $url ; Ctrl+C to stop."
    if($AllowUnscannedMedia){Write-Output 'Explicit LOCAL development exception: uploaded files remain NotScanned. This is not malware scanning or production approval.'}
    Invoke-ProjectDotnet $arguments
    exit 0
} catch { Write-Error ('Development startup failed: ' + $_.Exception.GetType().Name + '; inspect project logs.'); exit 1 }
