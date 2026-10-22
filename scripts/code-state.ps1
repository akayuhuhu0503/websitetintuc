[CmdletBinding()]
param([string]$Label='checkpoint',[ValidateSet('phase2b','v04')][string]$EvidencePhase='phase2b')
. (Join-Path $PSScriptRoot 'common.ps1')
if($Label -notmatch '^[a-zA-Z0-9_-]{1,80}$'){throw 'Invalid evidence label.'}
$paths=@(& rtk proxy rg --files src tests scripts -g '!bin/**' -g '!obj/**' -g '!node_modules/**' -g '!vendor/**')
$rows=@();foreach($path in $paths) {if($path -match '[\\/](bin|obj|node_modules|vendor)[\\/]'){continue};$file=Join-Path $script:ProjectRoot $path;if(Test-Path -LiteralPath $file -PathType Leaf){$rows+=[ordered]@{path=$path;sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()}}}
$text=ConvertTo-Json $rows -Depth 6;$sha=[Security.Cryptography.SHA256]::Create();try{$hash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
$dir=Join-Path $script:ProjectRoot ('.local/evidence/'+$EvidencePhase);[IO.Directory]::CreateDirectory($dir)|Out-Null
$record=[ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('O');label=$Label;files=$rows.Count;codeHash=$hash;manifest=$rows}
[IO.File]::WriteAllText((Join-Path $dir ('code-state-'+$Label+'.json')),(ConvertTo-Json $record -Depth 8),[Text.UTF8Encoding]::new($false))
Write-Output ('Recorded '+$rows.Count+' code files; no .env/runtime data included; codeHash='+$hash)
