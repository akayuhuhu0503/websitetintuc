[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'iis-video-tools.ps1')
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$repo=Split-Path -Parent $PSScriptRoot
$run=Join-Path $repo ('.local/tests/iis-video-dependency-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$script:checks=0
function Assert-Check([bool]$Condition,[string]$Name){if(-not $Condition){throw ('FAIL '+$Name)};$script:checks++;Write-Output ('PASS '+$Name)}
function Assert-Rejected([scriptblock]$Action,[string]$Pattern,[string]$Name){
 $message='';try{& $Action}catch{$message=$_.Exception.Message}
 Assert-Check ($message -like $Pattern) $Name
}
function New-Fixture([string]$Name,[string[]]$Entries) {
 $file=Join-Path $run ($Name+'.zip')
 $zip=[IO.Compression.ZipFile]::Open($file,[IO.Compression.ZipArchiveMode]::Create)
 try{foreach($path in $Entries){
  $entry=$zip.CreateEntry($path);$stream=$entry.Open()
  try{$bytes=[Text.Encoding]::UTF8.GetBytes('FIXTURE ONLY: '+$path);$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
 }}finally{$zip.Dispose()}
 return [pscustomobject]@{Path=$file;Lock=[pscustomobject]@{packageRoot='tools-test';bytes=(Get-Item -LiteralPath $file).Length;sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()}}
}
$entries=@('tools-test/bin/ffmpeg.exe','tools-test/bin/ffprobe.exe','tools-test/LICENSE','tools-test/README.txt')
$good=New-Fixture 'valid' $entries
Assert-IisToolArchive $good.Path $good.Lock
Assert-Check $true 'valid reviewed bytes/hash accepted'
$out=Join-Path $run 'out'
Expand-IisToolArchive $good.Path $out $good.Lock
Assert-Check (@(Get-ChildItem -LiteralPath $out -File).Count -eq 4) 'only two executables and original notices extracted'
Assert-Check ([IO.File]::ReadAllText((Join-Path $out 'LICENSE')) -ceq 'FIXTURE ONLY: tools-test/LICENSE') 'license bytes preserved'
Assert-Check ([IO.File]::ReadAllText((Join-Path $out 'README.txt')) -ceq 'FIXTURE ONLY: tools-test/README.txt') 'publisher readme bytes preserved'
Assert-Rejected {Expand-IisToolArchive $good.Path $out $good.Lock} '*existing tool destination refused*' 'existing binary is never overwritten'
$badHash=[pscustomobject]@{packageRoot='tools-test';bytes=$good.Lock.bytes;sha256=('0'*64)}
Assert-Rejected {Assert-IisToolArchive $good.Path $badHash} '*does not match*' 'wrong checksum refused before extraction'
$badSize=[pscustomobject]@{packageRoot='tools-test';bytes=1;sha256=$good.Lock.sha256}
Assert-Rejected {Assert-IisToolArchive $good.Path $badSize} '*does not match*' 'wrong archive length refused'
$traversal=New-Fixture 'traversal' ($entries+@('tools-test/../../escape.txt'))
Assert-Rejected {Expand-IisToolArchive $traversal.Path (Join-Path $run 'traversal') $traversal.Lock} '*unsafe ZIP entry*' 'zip traversal refused including unused entries'
$foreign=New-Fixture 'foreign' ($entries+@('elsewhere/private.txt'))
Assert-Rejected {Expand-IisToolArchive $foreign.Path (Join-Path $run 'foreign') $foreign.Lock} '*unsafe ZIP entry*' 'unexpected archive root refused'
$duplicate=New-Fixture 'duplicate' ($entries+@('tools-test/LICENSE'))
Assert-Rejected {Expand-IisToolArchive $duplicate.Path (Join-Path $run 'duplicate') $duplicate.Lock} '*duplicate ZIP entry*' 'duplicate archive entry refused'
$missing=New-Fixture 'missing' @('tools-test/bin/ffmpeg.exe','tools-test/bin/ffprobe.exe','tools-test/README.txt')
Assert-Rejected {Expand-IisToolArchive $missing.Path (Join-Path $run 'missing') $missing.Lock} '*LICENSE and README*' 'missing license fails closed'
$lock=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies/ffmpeg-win64.json') -Raw|ConvertFrom-Json
Assert-Check ($lock.sha256 -ceq '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba' -and $lock.version -ceq '9.0.2' -and $lock.url -ceq 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip') 'committed dependency is pinned to reviewed publisher release'
Assert-Check (-not(Test-Path -LiteralPath (Join-Path $run 'escape.txt'))) 'unsafe archives never create outside files'
Write-Output ('PASS '+$script:checks+' dependency checks. Fixture files retained in '+$run+'. No native execution, SQL or IIS.')
