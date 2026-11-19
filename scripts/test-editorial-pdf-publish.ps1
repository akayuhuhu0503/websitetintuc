$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run=Join-Path $repo ('.local/publish/TTDL-IIS-Editorial-Limits-'+[guid]::NewGuid().ToString('N')+'-building')
foreach($part in @('web','private','worker','tools','ops')){[void][IO.Directory]::CreateDirectory((Join-Path $run $part))}
$text=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'package-uat.ps1'))
$match=[regex]::Match($text,'(?s)<\?xml version="1.0" encoding="utf-8"\?>\s*<configuration>.*?</configuration>')
if(-not $match.Success){throw 'Publisher XML template missing'}
[IO.File]::WriteAllText((Join-Path $run 'web/web.config'),$match.Value,[Text.UTF8Encoding]::new($false))
# Empty test fixture, never a real runtime configuration or authorization.
[IO.File]::WriteAllText((Join-Path $run 'private/uat.json'),'{}',[Text.UTF8Encoding]::new($false))
$privateHash=(Get-FileHash -LiteralPath (Join-Path $run 'private/uat.json')).Hash
. (Join-Path $PSScriptRoot 'iis-web-bundle.ps1')
ConvertTo-IisWebBundle -Staging $run
[xml]$xml=[IO.File]::ReadAllText((Join-Path $run 'web/web.config'))
if($xml.SelectSingleNode("/configuration/location[@path='.']/system.webServer/security/requestFiltering/requestLimits").maxAllowedContentLength -ne '20971520'){throw 'Ordinary IIS limit changed'}
if($xml.SelectSingleNode("/configuration/location[@path='admin/documents']/system.webServer/security/requestFiltering/requestLimits").maxAllowedContentLength -ne '110100480'){throw 'Editorial IIS limit missing'}
$documentHandlers=$xml.SelectSingleNode("/configuration/location[@path='admin/documents']/system.webServer/handlers")
if($null -eq $documentHandlers){throw 'Editorial route must explicitly retain ANCM handler'}
$handler=$documentHandlers.SelectSingleNode("add[@name='aspNetCore']")
if($null -eq $handler -or $handler.path -ne '*' -or $handler.verb -ne '*' -or $handler.modules -ne 'AspNetCoreModuleV2' -or $handler.resourceType -ne 'Unspecified'){throw 'Editorial GET/POST/subpaths must reach ASP.NET Core'}
if($documentHandlers.SelectNodes("remove[@name='aspNetCore']").Count -ne 1 -or $documentHandlers.FirstChild.LocalName -ne 'remove' -or $documentHandlers.SelectNodes('clear').Count -ne 0){throw 'Replace only ANCM to avoid duplicate inherited mappings; preserve other handlers'}
if($xml.SelectNodes("/configuration/location[@path='.']/system.webServer/security/requestFiltering/hiddenSegments/add[@segment='.runtime' or @segment='.env']").Count -ne 2){throw 'Private hidden segments lost'}
if($xml.SelectNodes("/configuration/location[@path='.']/system.webServer/aspNetCore/environmentVariables/environmentVariable[@name='LANDPORTAL_WEB_BUNDLE' and @value='1']").Count -ne 1){throw 'WebBundle environment binding lost'}
$manifest=Get-Content -LiteralPath (Join-Path $run 'web/.runtime/manifest.json') -Raw | ConvertFrom-Json
$webConfigHash=(Get-FileHash -LiteralPath (Join-Path $run 'web/web.config')).Hash.ToLowerInvariant()
if(@($manifest.files | Where-Object { $_.path -eq 'web.config' -and $_.sha256 -eq $webConfigHash }).Count -ne 1){throw 'Config not sealed in manifest'}
if($privateHash -ne (Get-FileHash -LiteralPath (Join-Path $run 'web/.runtime/private/uat.json')).Hash){throw 'Private fixture changed'}
foreach($name in @('package-uat.ps1','build-for-iis.ps1','iis-web-bundle.ps1','prepare-iis-config.ps1','prepare-uat-target.ps1','configure-iis-uat.ps1')){
 $tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name),[ref]$tokens,[ref]$errors)
 if($errors.Count){throw ('PowerShell parse failed: '+$name)}
}
$report=@{status='PASS';checks=10;ordinaryBytes=20971520;editorialRequestBytes=110100480;editorialHandlerExplicit=$true;privateUnchanged=$true;manifestHashVerified=$true;iisExecuted=$false;sqlExecuted=$false;fixture=$run}
[IO.File]::WriteAllText((Join-Path $run 'editorial-publish-test.json'),($report|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output ('PASS: 10 publish/XML checks; real WebBundle transformation; no IIS/SQL. '+$run)
