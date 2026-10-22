[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidatePattern('^[a-f0-9]{32}$')][string]$Id)
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    $descriptorPath=Join-Path $script:ProjectRoot ".local/workers/$Id.json"
    if(-not (Test-Path -LiteralPath $descriptorPath)){throw 'Owned worker descriptor was not found.'}
    $descriptor=Get-Content -LiteralPath $descriptorPath -Raw|ConvertFrom-Json
    if($descriptor.id -cne $Id){throw 'Worker descriptor identity differs.'}
    $workerProcess=Get-Process -Id ([int]$descriptor.pid) -ErrorAction SilentlyContinue
    if($null -eq $workerProcess){Write-Output 'Owned worker already exited; no process was stopped.';exit 0}
    $expectedWorkerRoot=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot 'src/Worker/bin'))+'\'
    if(-not ([IO.Path]::GetFullPath($descriptor.workerDll)).StartsWith($expectedWorkerRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Descriptor escaped this project worker build.'}
    if($workerProcess.StartTime.ToUniversalTime().ToString('O') -cne $descriptor.startedUtc -or $workerProcess.Path -cne $descriptor.executable){throw 'PID was reused or executable differs; no process was stopped.'}
    $command=Get-CimInstance Win32_Process -Filter ('ProcessId='+$descriptor.pid)
    if($null -eq $command -or -not $command.CommandLine.Contains($descriptor.workerDll)){throw 'Process is not the descriptor worker; no process was stopped.'}
    Stop-Process -Id $workerProcess.Id
    Write-Output 'Owned local worker stopped. Any unfinished SQL lease expires; schedules were not reset.'
    exit 0
} catch {Write-Error ('Worker stop refused: '+$_.Exception.GetType().Name+'; no unrelated process was stopped.');exit 1}
