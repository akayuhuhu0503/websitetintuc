[CmdletBinding()]
param([switch]$Replace,
 [string]$EnvFile,
 [ValidateSet('Auto','Transfer','Local')][string]$PrivateMode='Auto',
 [ValidatePattern('^TTDL-IIS(?:-[A-Za-z0-9][A-Za-z0-9-]{0,63})?$')][string]$OutputName='TTDL-IIS')
# Build only: parses private .env as data; never sources it or starts an application/SQL tool.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$repo=Split-Path -Parent $PSScriptRoot
$base=[IO.Path]::GetFullPath((Join-Path $repo '.local/publish'))
$output=Join-Path $base $OutputName;$staging=Join-Path $base ($OutputName+'-building');$previous=Join-Path $base ($OutputName+'.previous')
foreach($path in @($base,$output,$staging,$previous)){
 for($node=$path;$node;$node=[IO.Path]::GetDirectoryName($node)){
  if((Test-Path -LiteralPath $node)-and ((Get-Item -LiteralPath $node).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse publish path refused'}
 }
}
if(Test-Path -LiteralPath $staging){throw 'Incomplete TTDL-IIS-building exists; inspect its logs and preserve/remove it explicitly before retry. No automatic deletion.'}
if(Test-Path -LiteralPath $output){
 if(-not $Replace){throw 'TTDL-IIS already exists; no overwrite. Use -Replace only for an unused, unsealed build. The previous directory and its manifest will be retained.'}
 $marker=Join-Path $output 'build-only.json'
 if(-not(Test-Path -LiteralPath $marker)){throw 'Unknown/sealed output refused'}
 $old=Get-Content -LiteralPath $marker -Raw -Encoding UTF8|ConvertFrom-Json
 if($old.kind -cne 'TTDL-IIS-BUILD-ONLY' -or $old.sealed -ne $false -or (Test-Path -LiteralPath $previous)){throw 'Sealed output or retained previous build refused; operator must preserve handoff evidence separately'}
 if(-not(Test-Path -LiteralPath (Join-Path $output 'manifest.json')) -or (Get-FileHash -LiteralPath (Join-Path $output 'manifest.json') -Algorithm SHA256).Hash -ine $old.manifestSha256){throw 'Previous manifest binding mismatch; no replacement permitted'}
 foreach($process in Get-CimInstance Win32_Process){
  if(([string]$process.ExecutablePath).StartsWith($output+'\',[StringComparison]::OrdinalIgnoreCase)-or ([string]$process.CommandLine).IndexOf($output,[StringComparison]::OrdinalIgnoreCase)-ge 0){throw 'Output is referenced by a running process; stop through its approved runbook first'}
 }
 $iis=Join-Path $env:windir 'System32/inetsrv/config/applicationHost.config'
 if(Test-Path -LiteralPath $iis){
  # Refuse on read denial, and on an active IIS path; never edit IIS.
  if(([IO.File]::ReadAllText($iis)).IndexOf($output,[StringComparison]::OrdinalIgnoreCase)-ge 0){throw 'Output is referenced by IIS; no replacement permitted'}
 }
}
. (Join-Path $PSScriptRoot 'uat-common.ps1')
. (Join-Path $PSScriptRoot 'iis-env.ps1')
if(-not $EnvFile){$EnvFile=Join-Path $repo '.env'}
$EnvFile=Get-UatSafePath $EnvFile
if(-not(Test-Path -LiteralPath $EnvFile -PathType Leaf)){throw 'Thiếu .env: đặt file cấu hình private đã được cấp vào gốc source, hoặc truyền -EnvFile; không dùng mật khẩu giả'}
$envSha=(Get-FileHash -LiteralPath $EnvFile -Algorithm SHA256).Hash
$inputSettings=Read-IisEnvironment $EnvFile -ForPublish
$targetHome=''
if($inputSettings.Metadata.LANDPORTAL_HOME){
 $targetHome=Get-UatSafePath ([string]$inputSettings.Metadata.LANDPORTAL_HOME)
 if($targetHome.StartsWith($repo+'\',[StringComparison]::OrdinalIgnoreCase) -or $targetHome -ieq $repo){throw 'LANDPORTAL_HOME must be outside the source repository'}
}
if($PrivateMode -eq 'Local'){
 $privateHome=if($targetHome){$targetHome}else{Join-Path ([Environment]::GetFolderPath('UserProfile')) ('LandPortal-IIS-Private\'+[Guid]::NewGuid().ToString('N'))}
 if(Test-Path -LiteralPath (Join-Path $privateHome 'uat.json')){throw 'Existing private uat.json refused; no overwrite'}
}else{
 $privateHome=Join-Path ([Environment]::GetFolderPath('UserProfile')) ('LandPortal-IIS-Private\'+[Guid]::NewGuid().ToString('N'))
 [void](Get-UatSafePath $privateHome)
 Assert-UatSeparatePaths $privateHome (Join-Path $privateHome 'uat.json') $output
}
& (Join-Path $PSScriptRoot 'prepare-iis-config.ps1') -EnvFile $EnvFile -DataHome $privateHome -PublishRoot $output -ForPublish -DeferTarget -ValidateOnly
$dotnet=Join-Path $repo '.local/dotnet/dotnet.exe'
if(-not(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$node=(Get-Command node -ErrorAction Stop).Source;$npm=(Get-Command npm.cmd -ErrorAction Stop).Source
function Invoke-BuildCommand([string]$Exe,[string[]]$CommandArgs){
 $saved=$ErrorActionPreference
 try{$ErrorActionPreference='Continue';if(Get-Command rtk -ErrorAction SilentlyContinue){& rtk proxy $Exe @CommandArgs}else{& $Exe @CommandArgs};$code=$LASTEXITCODE}finally{$ErrorActionPreference=$saved}
 if($code -ne 0){throw ('Build command failed: '+[IO.Path]::GetFileName($Exe)+' (exit '+$code+'). No deployment performed.')}
}
Push-Location $repo
try{
 $expected=(Get-Content -LiteralPath (Join-Path $repo 'global.json') -Raw|ConvertFrom-Json).sdk.version
 $version=(& $dotnet --version|Out-String).Trim();if($LASTEXITCODE -ne 0 -or $version -cne $expected){throw ('Install .NET SDK '+$expected+' from global.json; selected SDK differs.')}
 Write-Output ('SDK '+$version+'; checking Node.js/npm')
 Invoke-BuildCommand $node @('--version');Invoke-BuildCommand $npm @('--version')
 Push-Location (Join-Path $repo 'src/Web')
 try{
  # npm ci uses the committed lockfile and never changes it.
  Invoke-BuildCommand $npm @('ci','--no-audit','--no-fund')
  Invoke-BuildCommand $npm @('run','build')
 }finally{Pop-Location}
 # Reuse the existing publisher, but do not generate/run schema or application code.
 & (Join-Path $PSScriptRoot 'package-uat.ps1') -RunId ($OutputName+'-building') -SkipSchemaGeneration -AllowAdditionalPackage:($OutputName -cne 'TTDL-IIS')
 if(-not(Test-Path -LiteralPath (Join-Path $staging 'manifest.json'))){throw 'Publisher did not complete'}
 Copy-Item -LiteralPath (Join-Path $repo 'docs/iis-one-command.md') -Destination (Join-Path $staging 'START.md')
 Copy-Item -LiteralPath (Join-Path $repo 'docs/TTDL-GITHUB-IIS-HANDOFF.md') -Destination (Join-Path $staging 'TTDL-GITHUB-IIS-HANDOFF.md')
 Copy-Item -LiteralPath (Join-Path $repo 'docs/iis-one-command.md') -Destination (Join-Path $staging 'iis-one-command.md')
 $config=Join-Path $staging 'web/web.config'
 [xml]$xml=Get-Content -LiteralPath $config -Raw -Encoding UTF8
 if((Get-FileHash -LiteralPath $EnvFile -Algorithm SHA256).Hash -cne $envSha){throw 'Input .env changed during build; configuration not released'}
 if(-not(Test-Path -LiteralPath $privateHome -PathType Container)){
  $acl=New-Object Security.AccessControl.DirectorySecurity
  $acl.SetAccessRuleProtection($true,$false)
  foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'))){$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))}
  [void][IO.Directory]::CreateDirectory($privateHome,$acl)
 }
 & (Join-Path $PSScriptRoot 'prepare-iis-config.ps1') -EnvFile $EnvFile -DataHome $privateHome -PublishRoot $staging -ForPublish -DeferTarget
 Copy-Item -LiteralPath (Join-Path $privateHome 'iis.web.config') -Destination $config
 # No private JSON or private-input digest is copied into the public package.
 $required=@('web/Web.dll','web/Web.runtimeconfig.json','web/web.config','web/wwwroot/css/site.css','web/wwwroot/vendor/jodit/jodit.min.js','worker/Worker.dll','worker/Worker.runtimeconfig.json','tools/MigrationCli.dll','tools/uat-data/UatDataHandoff.dll','ops/uat-runtime.ps1','ops/uat-data.ps1','ops/prepare-iis-config.ps1','ops/configure-iis-uat.ps1','ops/iis-approval.example.json','ops/uat-config.example.json','START.md')
 foreach($relative in $required){if(-not(Test-Path -LiteralPath (Join-Path $staging $relative) -PathType Leaf)){throw ('Missing publish output: '+$relative)}}
 foreach($relative in @('web/wwwroot/css/font-faces.css','web/wwwroot/css/typography.css','web/wwwroot/css/giay-huy-lookup.css','web/wwwroot/js/giay-huy-lookup.js','web/wwwroot/fonts/roboto-OFL.txt','web/wwwroot/fonts/merriweather-OFL.txt','web/wwwroot/fonts/sources.json','TTDL-GITHUB-IIS-HANDOFF.md','iis-one-command.md','CAU-HINH-IIS.cmd','ops/complete-iis-publish.ps1')){if(-not(Test-Path -LiteralPath (Join-Path $staging $relative) -PathType Leaf)){throw ('Missing current handoff asset: '+$relative)}}
 $fonts=Get-Content -LiteralPath (Join-Path $repo 'src/Web/wwwroot/fonts/sources.json') -Raw -Encoding UTF8|ConvertFrom-Json
 foreach($font in $fonts.files){
  if($font.file -cnotmatch '^(roboto|merriweather)-(latin|latin-ext|vietnamese)\.woff2$'){throw 'Unexpected font inventory path'}
  $fontPath=Join-Path $staging ('web/wwwroot/fonts/'+$font.file)
  if(-not(Test-Path -LiteralPath $fontPath)-or (Get-Item -LiteralPath $fontPath).Length -ne $font.bytes -or (Get-FileHash -LiteralPath $fontPath).Hash -ine $font.sha256){throw 'Published font byte/hash mismatch'}
 }
 $secrets=@([string]$inputSettings.Settings.Runtime.RolePassword,[string]$inputSettings.Settings.Mail.Password)
 $sqlValues=New-Object System.Data.Common.DbConnectionStringBuilder
 $sqlValues.set_ConnectionString([string]$inputSettings.Settings.ConnectionStrings.Portal)
 foreach($key in @($sqlValues.Keys)){if($key -iin @('Password','Pwd')){$secrets+=[string]$sqlValues[$key]}}
 if([string]$inputSettings.Settings.ConnectionStrings.GiayHuyReadOnly){
  $sourceSql=New-Object System.Data.Common.DbConnectionStringBuilder
  $sourceSql.set_ConnectionString([string]$inputSettings.Settings.ConnectionStrings.GiayHuyReadOnly)
  foreach($key in @($sourceSql.Keys)){if($key -iin @('Password','Pwd')){$secrets+=[string]$sourceSql[$key]}}
 }
 foreach($f in Get-ChildItem -LiteralPath $staging -File -Recurse){
  if($f.Extension -in @('.json','.config','.xml','.ps1','.md','.log','.txt','.js','.css','.html','.sql','.sqlcmd') -or $f.Name -like '.env*'){
   $body=[IO.File]::ReadAllText($f.FullName)
   foreach($secret in $secrets){if($secret -and $body.Contains($secret)){throw 'Private value detected in public output; release refused (value withheld)'}}
   if($body -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'){throw 'Private key detected; release refused'}
  }
 }
 $records=@(foreach($folder in @('web','worker','tools','ops')){foreach($f in Get-ChildItem -LiteralPath (Join-Path $staging $folder) -Recurse -File){
  if($f.Name -like '.env*' -or $f.Extension -in @('.pdb','.bak','.mdf','.ldf','.key','.pfx','.pem','.dpapi') -or $f.Name -match 'tai-khoan|credentials'){throw 'Excluded file found; output not released'}
  @{path=$f.FullName.Substring($staging.Length+1).Replace('\','/');bytes=$f.Length;sha256=(Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
 }})
 $encoding=[Text.UTF8Encoding]::new($false)
 foreach($name in @('START.md','TTDL-GITHUB-IIS-HANDOFF.md','iis-one-command.md','CAU-HINH-IIS.cmd')){$records+=@{path=$name;bytes=(Get-Item -LiteralPath (Join-Path $staging $name)).Length;sha256=(Get-FileHash -LiteralPath (Join-Path $staging $name) -Algorithm SHA256).Hash.ToLowerInvariant()}}
 $manifest=@{schema=1;runId=$OutputName;utc=[DateTimeOffset]::UtcNow.ToString('O');status='PUBLISHED_CONFIG_PREPARED_NOT_DEPLOYED';privateMode=$PrivateMode;environment='Staging';schemaIncluded=$false;sqlExecuted=$false;servicesInstalled=0;workerModes=@('Scheduler','Documents','InquiryScan','Video','Mail');files=$records;productionReady=$false}
 [IO.File]::WriteAllText((Join-Path $staging 'manifest.json'),($manifest|ConvertTo-Json -Depth 8),$encoding)
 [IO.File]::WriteAllText((Join-Path $staging 'build-only.json'),(@{kind='TTDL-IIS-BUILD-ONLY';sealed=$false;manifestSha256=(Get-FileHash -LiteralPath (Join-Path $staging 'manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()}|ConvertTo-Json),$encoding)
 if(Test-Path -LiteralPath $output){Move-Item -LiteralPath $output -Destination $previous}
 Move-Item -LiteralPath $staging -Destination $output
 Write-Output ('PUBLISH PASS: '+$output)
 Write-Output ('Copy to IIS: '+(Join-Path $output 'web'))
 Write-Output ('PRIVATE CONFIG (not in publish): '+(Join-Path $privateHome 'uat.json'))
 Write-Output 'Target paths/HTTPS/Defender are discovered by ops/configure-iis-uat.ps1 on IIS; activation remains gated. No SQL was contacted.'
 Write-Output 'Web + Worker (5 modes) + tools + ops ready. SQL/IIS/deployment NOT RUN. Read START.md.'
 if($PrivateMode -in @('Auto','Local')){
  $gaps=@(Get-IisInputGaps $inputSettings)
  if($gaps.Count){Write-Output ('CHƯA GẮN IIS: cần TTDL cung cấp/phê duyệt trong cùng .env: '+($gaps -join '; '))}
  elseif($inputSettings.Metadata.IIS_MACHINE_NAME -ine $env:COMPUTERNAME -or -not(Test-Path (Join-Path $env:windir 'System32/inetsrv/Microsoft.Web.Administration.dll'))){Write-Output 'BUILD KHÁC MÁY IIS: copy gói Publish, cấu hình IIS trỏ web/, rồi mở CAU-HINH-IIS.cmd và chọn cùng file .env. Không tạo/chuyển uat.json thủ công.'}
  else{
   & powershell -NoProfile -NonInteractive -File (Join-Path $output 'ops/complete-iis-publish.ps1') -EnvFile $EnvFile -PublishRoot $output -PrepareForCopy
   if($LASTEXITCODE){Write-Output 'PUBLISH PASS / GẮN IIS BLOCKED: xem điều kiện tiếng Việt bên trên. Không khởi động dịch vụ hoặc bỏ guard.'}
  }
 }
}finally{Pop-Location}
