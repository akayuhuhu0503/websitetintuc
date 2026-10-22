[CmdletBinding()]
param([switch]$NoBuild, [switch]$AllowUnscannedMedia, [switch]$Once, [switch]$TestProfile, [switch]$Background, [switch]$IsolatedBuild)
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    if ($TestProfile) {
        . (Join-Path $PSScriptRoot 'test-profile.ps1')
        Initialize-ProjectTestProfile
        $target='LandPortal_Test'
    } else { $target='LandPortal_Dev' }
    $builder=Get-ProjectConnectionBuilder $target
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Portal',$builder.ConnectionString,'Process')
    [Environment]::SetEnvironmentVariable('Sql__ExpectedDatabase',$target,'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT','Development','Process')
    [Environment]::SetEnvironmentVariable('Public__BaseUrl','http://127.0.0.1:5188','Process')
    [Environment]::SetEnvironmentVariable('Media__AllowUnscannedDevelopmentUploads',$(if($AllowUnscannedMedia){'true'}else{'false'}),'Process')
    $arguments=@('run','--project','src/Worker/Worker.csproj','--no-launch-profile')
    $artifactArguments=@();if($IsolatedBuild){$artifactArguments=@('--artifacts-path',(Join-Path $script:ProjectRoot '.local/build/v04'));$arguments+=$artifactArguments}
    if($NoBuild){$arguments+='--no-build'}
    if($Once){$arguments+=@('--','--once')}
    Write-Output "Local publication worker: $target. Ctrl+C to stop; SQL leases expire without resetting schedules."
    if($AllowUnscannedMedia){Write-Output 'Explicit LOCAL Development exception only; uploaded files remain NotScanned.'}
    if($Background){
        if($Once){throw 'Use -Once in foreground; background worker has an explicit stop descriptor.'}
        if(-not $NoBuild){Invoke-ProjectDotnet (@('build','src/Worker/Worker.csproj','--no-restore')+$artifactArguments)}
        $workerDirectory=Join-Path $script:ProjectRoot $(if($IsolatedBuild){'.local/build/v04/bin/Worker/debug'}else{'src/Worker/bin/Debug/net10.0'})
        $runtime=Get-ChildItem -LiteralPath $workerDirectory -Filter '*.runtimeconfig.json' -File
        if(@($runtime).Count -ne 1){throw 'Build the worker first; expected one runtime entry.'}
        $workerDll=$runtime.FullName.Replace('.runtimeconfig.json','.dll')
        $descriptorId=[Guid]::NewGuid().ToString('N');$stateRoot=Join-Path $script:ProjectRoot '.local/workers'
        [IO.Directory]::CreateDirectory($stateRoot)|Out-Null
        $process=Start-Process -FilePath $script:DotnetPath -ArgumentList @('exec',('"'+$workerDll+'"')) -WorkingDirectory $script:ProjectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stateRoot "$descriptorId.stdout.log") -RedirectStandardError (Join-Path $stateRoot "$descriptorId.stderr.log")
        $process.Refresh()
        @{ id=$descriptorId; pid=$process.Id; startedUtc=$process.StartTime.ToUniversalTime().ToString('O'); workerDll=$workerDll; executable=[IO.Path]::GetFullPath($script:DotnetPath); database=$target }|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $stateRoot "$descriptorId.json") -Encoding UTF8
        Write-Output "Owned background worker descriptor: $descriptorId. Stop with rtk proxy powershell -File scripts/stop-worker-dev.ps1 -Id $descriptorId"
        exit 0
    }
    Invoke-ProjectDotnet $arguments
    exit 0
} catch { Write-Error ('Worker startup failed: '+$_.Exception.GetType().Name+'; inspect private project logs.');exit 1 }
