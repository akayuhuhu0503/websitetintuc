param([Parameter(Mandatory=$true)][string]$WebRoot)
$ErrorActionPreference='Stop'
$web=[IO.Path]::GetFullPath($WebRoot)
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
foreach($file in @('scripts/build-for-iis.ps1','scripts/iis-web-bundle.ps1','scripts/run-web-worker.ps1')){
 $tokens=$null;$errors=$null
 [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $repo $file),[ref]$tokens,[ref]$errors)
 if($errors.Count){throw ('PowerShell parse failed: '+$file)}
}
$manifest=Get-Content -LiteralPath (Join-Path $web '.runtime/manifest.json') -Raw|ConvertFrom-Json
if($manifest.privateMode -ne 'WebBundle'){throw 'WebBundle required'}
$count=0
foreach($item in $manifest.files){
 $path=[IO.Path]::GetFullPath((Join-Path $web $item.path))
 if(-not $path.StartsWith($web.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath $path -PathType Leaf)){throw 'Package boundary/missing file'}
 if((Get-FileHash -LiteralPath $path).Hash -ine $item.sha256){throw 'Package hash mismatch'}
 $count++
}
if(Test-Path -LiteralPath (Join-Path $web 'wwwroot/.runtime')){throw 'Runtime must not be copied into wwwroot'}
$saved=$env:PATH
try {
 $env:PATH=(Join-Path $repo '.local/dotnet')+';'+$saved
 & powershell -NoProfile -File (Join-Path $web '.runtime/run-worker.ps1') -VerifyPackage
 if($LASTEXITCODE){throw 'Published independent Worker offline inspection failed'}
}finally{$env:PATH=$saved}
Write-Output ('PASS PowerShell parser, '+$count+' published file hashes and actual Worker launcher. No SQL connection or queue processing.')
