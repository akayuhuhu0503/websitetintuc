[CmdletBinding()]
param([ValidatePattern('^[^\s@]+@[^\s@]+\.[^\s@]+$')][string]$AdminEmail = 'admin@landportal.local')
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    $target = Join-Path $script:ProjectRoot '.env'
    if (Test-Path -LiteralPath $target) {
        Write-Output 'PRESERVED existing project .env. No credential or configuration changed.'
        exit 0
    }
    function New-LocalSecret {
        $bytes = New-Object byte[] 32
        $random = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($bytes) } finally { $random.Dispose() }
        return ([BitConverter]::ToString($bytes).Replace('-','') + '!aA7')
    }
    $lines = [IO.File]::ReadAllLines((Join-Path $script:ProjectRoot '.env.example'))
    $output = foreach ($line in $lines) {
        if ($line.StartsWith('Runtime__RolePassword=')) { 'Runtime__RolePassword=' + (New-LocalSecret) }
        elseif ($line.StartsWith('Bootstrap__AdminEmail=')) { 'Bootstrap__AdminEmail=' + $AdminEmail }
        else { $line }
    }
    $encoding = New-Object Text.UTF8Encoding $false
    [IO.File]::WriteAllLines($target, $output, $encoding)
    Write-Output 'CREATED ignored project .env with a random runtime-role secret. Bootstrap generates its own temporary account credential in the private delivery store; no default password.'
    exit 0
} catch { Write-Error ('Local initialization failed: ' + $_.Exception.GetType().Name); exit 1 }
