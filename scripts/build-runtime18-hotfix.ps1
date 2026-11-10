[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$id=[Guid]::NewGuid().ToString('N')
$artifacts=Join-Path $repo ('.local/runtime18-build-'+$id)
$out=Join-Path $repo ('.local/runtime18-delivery-'+$id)
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
Push-Location $repo
try {
 foreach($project in @('Web','Worker','MigrationCli')) {
  & $dotnet build ('src/'+$project+'/'+$project+'.csproj') -c Release --artifacts-path $artifacts
  if($LASTEXITCODE -ne 0){throw ('Build failed: '+$project)}
 }
 [void][IO.Directory]::CreateDirectory((Join-Path $out 'payload'))
 [void][IO.Directory]::CreateDirectory((Join-Path $out 'probe'))
 $bin=Join-Path $artifacts 'bin'
 $infra=Join-Path $bin 'Infrastructure/release/Infrastructure.dll'
 Copy-Item -LiteralPath $infra -Destination (Join-Path $out 'payload/Infrastructure.dll')
 foreach($project in @('Web','Worker')) {
  if((Get-FileHash $infra).Hash -cne (Get-FileHash (Join-Path $bin ($project+'/release/Infrastructure.dll'))).Hash){throw 'Web/Worker shared binary mismatch'}
 }
 Get-ChildItem -LiteralPath (Join-Path $bin 'MigrationCli/release') | Where-Object { $_.Name -notlike '*.pdb' -and $_.Name -notlike '*.xml' -and $_.Name -notlike 'appsettings*.json' } | Copy-Item -Destination (Join-Path $out 'probe') -Recurse
 $libraries=@{}
 foreach($project in @('Infrastructure','Web','Worker')) {
  $deps=Get-Content -LiteralPath (Join-Path $bin ($project+'/release/'+$project+'.deps.json')) -Raw | ConvertFrom-Json
  $libraries[$project]=@($deps.libraries.PSObject.Properties.Name)
 }
 $required=@($libraries.Infrastructure | Where-Object {$_ -in $libraries.Web -and $_ -in $libraries.Worker} | Sort-Object)
 $files=@(Get-ChildItem -LiteralPath $out -Recurse -File | ForEach-Object {
  @{path=$_.FullName.Substring($out.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$_.Length}
 })
 $manifest=Join-Path $out 'delivery.json'
 [IO.File]::WriteAllText($manifest,(@{kind='GDLA.Runtime18.Hotfix';requiredLibraries=$required;files=$files}|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
 $sha=(Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()
 $template=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'update-uat-runtime18.ps1') -Raw
 [IO.File]::WriteAllText((Join-Path $out 'CAP-NHAT-RUNTIME18.ps1'),$template.Replace('__DELIVERY_SHA__',$sha),[Text.UTF8Encoding]::new($true))
 Write-Output ('BUILD READY (not deployed): '+$out)
 Write-Output ('powershell -NoProfile -File "'+(Join-Path $out 'CAP-NHAT-RUNTIME18.ps1')+'"')
} finally { Pop-Location }
