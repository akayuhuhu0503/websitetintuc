# Build-time dependency acquisition only. No runtime download, install, PATH/ACL change or executable invocation.
Set-StrictMode -Version Latest

function Assert-IisToolPath([string]$Path) {
 $full=[IO.Path]::GetFullPath($Path)
 if($full.StartsWith('\\',[StringComparison]::Ordinal)){throw 'FFmpeg: UNC dependency paths are not supported.'}
 for($node=$full;$node;$node=[IO.Path]::GetDirectoryName($node)){
  if((Test-Path -LiteralPath $node)-and ((Get-Item -LiteralPath $node).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'FFmpeg: linked dependency path refused.'}
 }
 return $full
}

function Assert-IisToolArchive([string]$Path,$Lock) {
 $safe=Assert-IisToolPath $Path
 if(-not(Test-Path -LiteralPath $safe -PathType Leaf)){throw 'FFmpeg: dependency archive is missing.'}
 if((Get-Item -LiteralPath $safe).Length -ne [long]$Lock.bytes -or
    (Get-FileHash -LiteralPath $safe -Algorithm SHA256).Hash -ine [string]$Lock.sha256){
  throw 'FFmpeg: archive does not match the reviewed publisher checksum. No executable was installed or run.'
 }
}

function Expand-IisToolArchive([string]$Path,[string]$Destination,$Lock) {
 Assert-IisToolArchive $Path $Lock
 $target=Assert-IisToolPath $Destination
 Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
 $archiveStream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 $hasher=[Security.Cryptography.SHA256]::Create()
 try {$lockedHash=[BitConverter]::ToString($hasher.ComputeHash($archiveStream)).Replace('-','').ToLowerInvariant()} finally {$hasher.Dispose()}
 if($lockedHash -cne [string]$Lock.sha256 -or $archiveStream.Length -ne [long]$Lock.bytes){$archiveStream.Dispose();throw 'FFmpeg: archive changed before extraction.'}
 $archiveStream.Position=0
 $zip=[IO.Compression.ZipArchive]::new($archiveStream,[IO.Compression.ZipArchiveMode]::Read,$false)
 try {
  $selected=@{};$seen=@{};[long]$expanded=0
  $required=@{'bin/ffmpeg.exe'='ffmpeg.exe';'bin/ffprobe.exe'='ffprobe.exe';'LICENSE'='LICENSE';'README.txt'='README.txt'}
  foreach($entry in $zip.Entries){
   $name=$entry.FullName.Replace('\','/')
   # Validate every entry, including entries that are not copied. Never extract arbitrary paths.
   if($name.StartsWith('/') -or $name.Contains(':') -or @($name.Split('/')|Where-Object {$_ -in @('.','..')}).Count -or
      -not $name.StartsWith(([string]$Lock.packageRoot+'/'),[StringComparison]::Ordinal) -or
      (($entry.ExternalAttributes -shr 16) -band 61440) -eq 40960){throw 'FFmpeg: unsafe ZIP entry refused.'}
   if($seen.ContainsKey($name)){throw 'FFmpeg: duplicate ZIP entry refused.'};$seen[$name]=$true
   $expanded+=$entry.Length
   if($expanded -gt 838860800){throw 'FFmpeg: expanded archive exceeds dependency size limit.'}
   $relative=$name.Substring(([string]$Lock.packageRoot).Length+1)
   if($required.ContainsKey($relative)){$selected[$relative]=$entry}
  }
  if($selected.Count -ne $required.Count){throw 'FFmpeg: archive must contain FFmpeg, FFprobe, LICENSE and README.'}
  foreach($relative in $required.Keys){
   $file=Assert-IisToolPath (Join-Path $target $required[$relative])
   if(Test-Path -LiteralPath $file){throw 'FFmpeg: existing tool destination refused; use a fresh publish output.'}
  }
  [void][IO.Directory]::CreateDirectory($target)
  foreach($relative in $required.Keys){
   $file=Join-Path $target $required[$relative]
   $entryStream=$selected[$relative].Open()
   try {$output=[IO.File]::Open($file,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try {$entryStream.CopyTo($output)} finally {$output.Dispose()}
   } finally {$entryStream.Dispose()}
  }
 } finally {$zip.Dispose()}
}

function Install-IisVideoTools([string]$Destination,[string]$CacheRoot) {
 $lockPath=Join-Path $PSScriptRoot 'dependencies/ffmpeg-win64.json'
 $lock=Get-Content -LiteralPath $lockPath -Raw -Encoding UTF8|ConvertFrom-Json
 if($lock.schema -ne 1 -or $lock.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $lock.bytes -lt 1 -or $lock.bytes -gt 268435456 -or
    $lock.archive -cnotmatch '^ffmpeg-[0-9.]+-essentials_build\.zip$' -or $lock.packageRoot -cne ([IO.Path]::GetFileNameWithoutExtension($lock.archive)) -or
    $lock.url -cne ('https://github.com/GyanD/codexffmpeg/releases/download/'+$lock.version+'/'+$lock.archive)){
  throw 'FFmpeg: committed dependency lock is invalid.'
 }
 $cache=Assert-IisToolPath $CacheRoot
 $target=Assert-IisToolPath $Destination
 [void][IO.Directory]::CreateDirectory($cache)
 $archive=Join-Path $cache $lock.archive
 if(-not(Test-Path -LiteralPath $archive)){
  $temporary=Join-Path $cache ($lock.archive+'.download-'+[Guid]::NewGuid().ToString('N'))
  # HTTPS source and checksum are pinned by source review, not trusted from downloaded content.
  $oldProtocol=[Net.ServicePointManager]::SecurityProtocol
  try {
   [Net.ServicePointManager]::SecurityProtocol=$oldProtocol -bor [Net.SecurityProtocolType]::Tls12
   Write-Output ('FFmpeg: fetching reviewed version '+$lock.version+' for this build (cached for subsequent builds).')
   Invoke-WebRequest -UseBasicParsing -Uri $lock.url -OutFile $temporary -TimeoutSec 300 -ErrorAction Stop
   Assert-IisToolArchive $temporary $lock
   [IO.File]::Move($temporary,$archive)
  } catch {
   throw ('FFmpeg: cannot prepare the reviewed dependency. Build needs HTTPS access to GitHub or its already verified local cache. No IIS changes. '+$_.Exception.GetType().Name)
  } finally {[Net.ServicePointManager]::SecurityProtocol=$oldProtocol}
 }
 Assert-IisToolArchive $archive $lock
 Expand-IisToolArchive $archive $target $lock
 Copy-Item -LiteralPath $lockPath -Destination (Join-Path $target 'package-origin.json') -ErrorAction Stop
 Write-Output ('FFmpeg '+$lock.version+': publisher checksum verified; exact LICENSE/README preserved. Native tools were not executed during Publish.')
}
