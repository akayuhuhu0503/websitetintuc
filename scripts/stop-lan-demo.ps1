[CmdletBinding()]
param([switch]$DisableAccounts)
. (Join-Path $PSScriptRoot 'common.ps1')
$state=Get-Content (Join-Path $script:ProjectRoot '.local/lan-demo/current.json') -Raw -Encoding UTF8|ConvertFrom-Json
$processFile=Join-Path $state.home 'process.json'
if(Test-Path $processFile){$record=Get-Content $processFile -Raw -Encoding UTF8|ConvertFrom-Json;$process=Get-Process -Id $record.pid -ErrorAction SilentlyContinue
 if($process){$native=Get-CimInstance Win32_Process -Filter ('ProcessId='+$record.pid)
  $expected=if($record.PSObject.Properties.Name -contains 'hostScript'){$record.hostScript}else{$state.web}
  if($process.StartTime.ToUniversalTime().ToString('O') -cne $record.startedUtc -or -not $native.CommandLine.Contains($expected)){throw 'Process identity differs; refusing to stop unrelated application'}
  & rtk proxy taskkill /PID $record.pid /T /F | Out-Null
  if($LASTEXITCODE){throw 'Demo process tree stop failed'}
  Write-Output 'LAN demo Web stopped; no database deleted.'
 }else{Write-Output 'LAN demo Web is already stopped.'}
}
if($DisableAccounts){$env:LAN_DEMO_HOME=$state.home;& rtk proxy $script:DotnetPath $state.setup disable;if($LASTEXITCODE){throw 'LAN account disable failed'}}
