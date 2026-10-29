[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PlanFile,[Parameter(Mandatory=$true)][string]$PlanSha256,[switch]$Apply)
# Explicit reviewed file allowlist ONLY. Never discovers/deletes whole trees,
# databases, source or backups by age. Use a fresh inventory to prepare a plan.
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\');$prefix=$root+'\'
function NoReparse([string]$path){
 for($p=$path;$p -and ($p -ieq $root -or $p.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase));$p=[IO.Path]::GetDirectoryName($p)){
  if([IO.File]::GetAttributes(('\\?\'+$p)) -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse path refused'}
 }
}
$planPath=[IO.Path]::GetFullPath($PlanFile)
if(-not $planPath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Plan must be inside this workspace'}
NoReparse $planPath
if((Get-FileHash -LiteralPath $planPath -Algorithm SHA256).Hash -ine $PlanSha256){throw 'Reviewed plan SHA mismatch'}
$plan=Get-Content -LiteralPath $planPath -Raw -Encoding UTF8|ConvertFrom-Json
if($plan.kind -cne 'LandPortal.ExplicitCleanup.v1' -or $plan.root -ine $root){throw 'Cleanup plan scope mismatch'}
$protected=@($plan.protectedPrefixes|ForEach-Object {[IO.Path]::GetFullPath((Join-Path $root $_)).TrimEnd('\')+'\'})
$tracked=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$info=New-Object Diagnostics.ProcessStartInfo;$info.FileName='rtk';$info.Arguments='proxy git ls-files -z';$info.WorkingDirectory=$root;$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
$p=[Diagnostics.Process]::Start($info);$raw=$p.StandardOutput.ReadToEnd();$err=$p.StandardError.ReadToEnd();$p.WaitForExit();if($p.ExitCode){throw 'Cannot enumerate protected tracked files'};$p.Dispose()
foreach($name in $raw.Split([char]0)){if($name){[void]$tracked.Add($name.Replace('/','\'))}}
$active=New-Object 'System.Collections.Generic.List[string]'
function Refresh-Active {
 $active.Clear()
 foreach($p in Get-CimInstance Win32_Process){
  foreach($m in [regex]::Matches(([string]$p.CommandLine+' '+[string]$p.ExecutablePath),'(?i)(?:[A-Z]:[\/][^"\s]*website_tin_tuc[\/][^"\s]+|\.local[\/][^"\s]+)')){
   $v=$m.Value.Trim('"',"'");if($v.StartsWith('.local')){$v=Join-Path $root $v}
   if(Test-Path -LiteralPath $v){$item=Get-Item -LiteralPath $v -Force;$dir=if($item.PSIsContainer){$item.FullName}else{$item.DirectoryName};$active.Add($dir.TrimEnd('\')+'\')}
  }
 }
 $lan=Join-Path $root '.local/lan-demo/current.json'
 if(Test-Path -LiteralPath $lan){$state=Get-Content $lan -Raw -Encoding UTF8|ConvertFrom-Json;foreach($k in @('web','setup')){if($state.PSObject.Properties.Name -contains $k){$active.Add(([IO.Path]::GetDirectoryName([string]$state.$k)).TrimEnd('\')+'\')}}}
 $iis=Join-Path $env:windir 'System32/inetsrv/config/applicationHost.config'
 if(Test-Path -LiteralPath $iis){[xml]$x=[IO.File]::ReadAllText($iis);foreach($v in $x.configuration.'system.applicationHost'.sites.site.application.virtualDirectory){if($v.physicalPath){$active.Add([IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables([string]$v.physicalPath)).TrimEnd('\')+'\')}}}
}
Refresh-Active
$sum=($plan.files|Measure-Object -Property bytes -Sum).Sum
if(-not $Apply){Write-Output ('PLAN ONLY: '+$plan.files.Count+' explicit files; '+$sum+' bytes; no deletion.');return}
$ledger=Join-Path ([IO.Path]::GetDirectoryName($planPath)) ('deleted-'+[guid]::NewGuid().ToString('N')+'.jsonl')
$writer=New-Object IO.StreamWriter($ledger,$false,([Text.UTF8Encoding]::new($false)));$writer.AutoFlush=$true
$deleted=0;$bytes=0L;$skipped=0;$groups=@{};$n=0
try{
 foreach($f in $plan.files){
  $n++;if($n%500 -eq 0){Refresh-Active}
  $status='KEPT';$reason='';$full=''
  try{
   $rel=([string]$f.path).Replace('/','\')
   if([IO.Path]::IsPathRooted($rel) -or @($rel.Split('\')|Where-Object {$_ -in @('.','..')}).Count){throw 'Invalid relative path'}
   $full=[IO.Path]::GetFullPath(('\\?\'+(Join-Path $root $rel))).Substring(4)
   $io='\\?\'+$full
   if(-not $full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or $tracked.Contains($rel)){throw 'Outside workspace or Git-tracked source'}
   if($rel -notmatch '^(\.local\\(?:build|publish|backups|handoff-source)\\|(?:src|tests|tools)\\.+\\(?:bin|obj|node_modules)\\|scripts\\__pycache__\\)'){throw 'Outside approved artifact classes'}
   if([IO.Path]::GetExtension($full) -in @('.mdf','.ldf','.ndf','.pfx','.p12','.key','.pem','.cer','.crt','.snk','.dpapi','.cs','.csproj','.sln','.props','.targets','.ps1','.py','.sql','.md') -or [IO.Path]::GetFileName($full) -like '.env*' -or [IO.Path]::GetFileName($full) -match '(?i)^(credentials|tai-khoan|appsettings\..*\.Local\.json|private.*\.json)'){throw 'Database/key/source/private configuration protection'}
   if([IO.Path]::GetExtension($full) -ieq '.bak' -and ($f.group -cne 'superseded-local-dev-test-sql' -or $rel -notmatch '^\.local\\backups\\.+\\LandPortal_(Dev|Test)\.bak$')){throw 'Unknown/shared/LAN SQL backup protected'}
   foreach($keep in @($protected)+@($active.ToArray())){if($full.StartsWith($keep,[StringComparison]::OrdinalIgnoreCase)){throw 'Protected or active dependency'}}
   NoReparse $full
   if(-not [IO.File]::Exists($io)){throw 'Missing or not a regular file'}
   $item=New-Object IO.FileInfo $io
   if($item.Length -ne [long]$f.bytes -or $item.LastWriteTimeUtc.Ticks -ne [long]$f.mtimeTicks){throw 'File changed after inventory'}
   if($f.PSObject.Properties.Name -contains 'canonical'){
    $canonical=[IO.Path]::GetFullPath([string]$f.canonical);NoReparse $canonical
    $c=New-Object IO.FileInfo ('\\?\'+$canonical)
    if($c.Length -ne [long]$f.canonicalBytes -or $c.LastWriteTimeUtc.Ticks -ne [long]$f.canonicalTicks){throw 'Verified retained copy changed'}
   }
   # Exclusive probe protects outputs still held by an unobserved process.
   $probe=[IO.File]::Open($io,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None);$probe.Dispose()
   if($full.Length -ge 248){[IO.File]::Delete($io)}else{Remove-Item -LiteralPath $full -Force -ErrorAction Stop}
   if([IO.File]::Exists($io)){throw 'Deletion did not remove file'}
   $status='DELETED';$deleted++;$bytes+=[long]$f.bytes
   if(-not $groups.ContainsKey([string]$f.group)){$groups[[string]$f.group]=0L};$groups[[string]$f.group]+=[long]$f.bytes
  }catch{$skipped++;$reason=$_.Exception.GetType().Name+': '+$_.Exception.Message}
  $writer.WriteLine((@{path=$f.path;bytes=$f.bytes;group=$f.group;status=$status;reason=$reason}|ConvertTo-Json -Compress))
 }
}finally{$writer.Dispose()}
$result=@{deletedFiles=$deleted;deletedBytes=$bytes;skipped=$skipped;groups=$groups;ledger=$ledger;sqlExecuted=$false}
$result|ConvertTo-Json -Depth 4
[IO.File]::WriteAllText(($ledger+'.summary.json'),($result|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
