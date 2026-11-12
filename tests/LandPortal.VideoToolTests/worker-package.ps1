# Isolated launcher smoke. Synthetic config only; --verify-package exits before SQL/queue access.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BuiltWorker,[Parameter(Mandatory=$true)][string]$BuiltWeb,[Parameter(Mandatory=$true)][string]$DotnetDirectory,[switch]$VerifyIisPoolSelector)
$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('LandPortal-WorkerPackage-'+[Guid]::NewGuid().ToString('N'))
$web=Join-Path $root 'web';$runtime=Join-Path $web '.runtime';$worker=Join-Path $runtime 'worker'
[void][IO.Directory]::CreateDirectory($worker)
[void][IO.Directory]::CreateDirectory((Join-Path $runtime 'private'))
Get-ChildItem -LiteralPath $BuiltWorker | Copy-Item -Destination $worker -Recurse
foreach($name in @('Web.dll','Infrastructure.dll')){Copy-Item -LiteralPath (Join-Path $BuiltWeb $name) -Destination $web}
[IO.File]::WriteAllText((Join-Path $web 'web.config'),'<configuration />')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../scripts/run-web-worker.ps1') -Destination (Join-Path $runtime 'run-worker.ps1')
$settings=@{
 DOTNET_ENVIRONMENT='Staging';ASPNETCORE_ENVIRONMENT='Staging'
 Deployment=@{Profile='UAT';ExpectedSqlServer='fixture.invalid';ExpectedSqlServerIdentity='FIXTURE';ExpectedHost='fixture.invalid'}
 ConnectionStrings=@{Portal='Server=fixture.invalid;Database=LandPortal_UatFixture;Integrated Security=True;Encrypt=True;TrustServerCertificate=False'}
 Sql=@{ExpectedDatabase='LandPortal_UatFixture'};Runtime=@{RolePassword='Synthetic fixture only, never a credential'}
 Public=@{BaseUrl='https://fixture.invalid/'}
 Media=@{AllowManagedImageWithoutAv=$true;AllowManagedDocumentsWithoutAv=$true}
 Video=@{Enabled=$true;AllowManagedVideoWithoutAv=$true}
}
$private=Join-Path $runtime 'private/uat.json'
[IO.File]::WriteAllText($private,($settings|ConvertTo-Json -Depth 8))
$files=@(foreach($path in @('Web.dll','Infrastructure.dll','web.config','.runtime/worker/Worker.dll','.runtime/worker/Infrastructure.dll')){
 @{path=$path;sha256=(Get-FileHash -LiteralPath (Join-Path $web $path)).Hash.ToLowerInvariant()}
})
$manifest=@{privateMode='WebBundle';privateConfiguration=@{path='private/uat.json';confidential=$true;sha256=(Get-FileHash -LiteralPath $private).Hash.ToLowerInvariant()};files=$files}
[IO.File]::WriteAllText((Join-Path $runtime 'manifest.json'),($manifest|ConvertTo-Json -Depth 8))
$oldPath=$env:PATH
$oldPoolSelector=$env:LANDPORTAL_IIS_APP_POOL
try{
 $env:PATH=$DotnetDirectory+[IO.Path]::PathSeparator+$env:PATH
 $launcher=Join-Path $runtime 'run-worker.ps1'
 $arguments=@('-NoProfile','-NonInteractive','-File',$launcher,'-Mode','Video','-VerifyPackage')
 if($VerifyIisPoolSelector){
  # Test-only shim verifies what the real child receives, then delegates to the installed dotnet.
  # It adds no production test flag and never starts a queue or opens SQL.
  $dotnetExe=[IO.Path]::GetFullPath((Join-Path $DotnetDirectory 'dotnet.exe'))
  if(-not(Test-Path -LiteralPath $dotnetExe) -or $dotnetExe.Contains('"') -or $dotnetExe.Contains('%')){throw 'Invalid local dotnet executable for launcher fixture'}
  $shim=Join-Path $root 'shim';[void][IO.Directory]::CreateDirectory($shim)
  $calls=Join-Path $root 'dotnet-invocations.txt'
  $cmd=@('@echo off','if not "%LANDPORTAL_IIS_APP_POOL%"=="WWW_GDLA" exit /b 95',('echo invoked>>"'+$calls+'"'),('"'+$dotnetExe+'" %*'),'exit /b %ERRORLEVEL%')
  [IO.File]::WriteAllLines((Join-Path $shim 'dotnet.cmd'),$cmd,[Text.Encoding]::ASCII)
  $env:PATH=$shim+[IO.Path]::PathSeparator+$env:PATH
  $env:LANDPORTAL_IIS_APP_POOL='STALE_AMBIENT_POOL'
  $arguments+=@('-IisAppPool','WWW_GDLA')
 }
 $output=& powershell @arguments
 if($LASTEXITCODE -ne 0){throw 'Isolated Worker launcher verification failed'}
 $state=($output -join [Environment]::NewLine)|ConvertFrom-Json
 if($state.mode -ne 'OFFLINE_PACKAGE_CHECK_ONLY' -or $state.sqlOpened -ne $false -or $state.workerStarted -ne $false -or $state.video -ne $true -or $state.managedVideo -ne $true -or $state.managedImage -ne $true -or $state.managedDocuments -ne $true){throw 'Isolated Worker loaded unexpected flags'}
 Write-Output 'PASS: actual WebBundle launcher and Worker process load JSON boolean policies; SQL and queue never opened.'
 if($VerifyIisPoolSelector){
  if(-not(Test-Path -LiteralPath $calls) -or @(Get-Content -LiteralPath $calls).Count -ne 1){throw 'Launcher did not execute the pool-selector assertion shim exactly once'}
  Write-Output 'PASS: real launcher replaces stale ambient selector and forwards WWW_GDLA to the child process.'
  $negativeOutput=Join-Path $root 'invalid-pool.stdout.txt';$negativeError=Join-Path $root 'invalid-pool.stderr.txt'
  $negativeArguments='-NoProfile -NonInteractive -File "'+$launcher+'" -Mode Video -VerifyPackage -IisAppPool "../other"'
  $negative=Start-Process -FilePath 'powershell.exe' -ArgumentList $negativeArguments -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $negativeOutput -RedirectStandardError $negativeError
  if($negative.ExitCode -eq 0 -or @(Get-Content -LiteralPath $calls).Count -ne 1 -or (Get-Content -LiteralPath $negativeError -Raw) -notmatch 'Invalid IIS application pool name'){throw 'Unsafe pool name was not rejected before Worker execution'}
  Write-Output 'PASS: invalid pool path is rejected before dotnet executes; no operational metadata/SQL/queue accessed.'
 }
}finally{$env:PATH=$oldPath;$env:LANDPORTAL_IIS_APP_POOL=$oldPoolSelector}
