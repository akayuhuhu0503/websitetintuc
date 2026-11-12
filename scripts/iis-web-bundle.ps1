# Called by build-for-iis.ps1 only, after normal Publish and secret checks.
function ConvertTo-IisWebBundle([string]$Staging) {
 $root=[IO.Path]::GetFullPath($Staging).TrimEnd('\')
 $expected=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../.local/publish')).TrimEnd('\')
 if(-not $root.StartsWith($expected+'\',[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $root -Leaf) -notmatch '^TTDL-IIS(?:-[A-Za-z0-9-]+)?-building$'){throw 'Expected unused build staging directory'}
 $web=Join-Path $root 'web';$runtime=Join-Path $web '.runtime'
 if(Test-Path -LiteralPath $runtime){throw 'Existing embedded runtime refused'}
 foreach($node in Get-ChildItem -LiteralPath $root -Force -Recurse){if($node.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse package refused'}}
 [void][IO.Directory]::CreateDirectory($runtime)
 # All moves resolve inside this exact staging directory. No existing IIS/data path is touched.
 foreach($name in @('private','worker','tools','ops')) {
  $source=[IO.Path]::GetFullPath((Join-Path $root $name));$target=[IO.Path]::GetFullPath((Join-Path $runtime $name))
  if(-not $source.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or -not $target.StartsWith($web+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Move boundary refused'}
  Move-Item -LiteralPath $source -Destination $target
 }
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run-web-worker.ps1') -Destination (Join-Path $runtime 'run-worker.ps1')
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../docs/iis-web-only.md') -Destination (Join-Path $runtime 'README.md')
 [xml]$xml=Get-Content -LiteralPath (Join-Path $web 'web.config') -Raw -Encoding UTF8
 $vars=$xml.configuration.location.'system.webServer'.aspNetCore.environmentVariables
 $flag=$xml.CreateElement('environmentVariable');$flag.SetAttribute('name','LANDPORTAL_WEB_BUNDLE');$flag.SetAttribute('value','1');[void]$vars.AppendChild($flag)
 $filter=$xml.configuration.location.'system.webServer'.security.requestFiltering
 $hidden=$xml.CreateElement('hiddenSegments')
 foreach($segment in @('.runtime','.env')){$add=$xml.CreateElement('add');$add.SetAttribute('segment',$segment);[void]$hidden.AppendChild($add)}
 [void]$filter.AppendChild($hidden)
 [IO.File]::WriteAllText((Join-Path $web 'web.config'),$xml.OuterXml,[Text.UTF8Encoding]::new($false))
 $files=@(foreach($f in Get-ChildItem -LiteralPath $web -Force -File -Recurse){
  $relative=$f.FullName.Substring($web.Length+1).Replace('\','/')
  if($relative -eq '.runtime/private/uat.json'){continue}
  if($relative.StartsWith('.runtime/private/')){throw 'Unexpected private package file'}
  if($f.Name -like '.env*' -or $f.Extension -in @('.bak','.mdf','.ldf','.pfx','.key','.pem','.dpapi')){throw 'Excluded package file'}
  @{path=$relative;sha256=(Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$f.Length}
 })
 $manifest=@{schema=2;privateMode='WebBundle';utc=[DateTimeOffset]::UtcNow.ToString('O');environment='Staging';files=$files;privateConfiguration=@{path='private/uat.json';confidential=$true;sha256=(Get-FileHash -LiteralPath (Join-Path $runtime 'private/uat.json')).Hash.ToLowerInvariant()};schemaIncluded=$false;sqlExecuted=$false;storageDefault='../private';backgroundHost='Web';workerModes=@('Scheduler','Documents','InquiryScan','Video','Mail')}
 $json=$manifest|ConvertTo-Json -Depth 8
 [IO.File]::WriteAllText((Join-Path $runtime 'manifest.json'),$json,[Text.UTF8Encoding]::new($false))
 # Outer files are build receipts only; never needed on IIS.
 [IO.File]::WriteAllText((Join-Path $root 'manifest.json'),$json,[Text.UTF8Encoding]::new($false))
 [IO.File]::WriteAllText((Join-Path $root 'build-only.json'),(@{kind='TTDL-IIS-BUILD-ONLY';sealed=$false;manifestSha256=(Get-FileHash -LiteralPath (Join-Path $root 'manifest.json')).Hash.ToLowerInvariant()}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
}
