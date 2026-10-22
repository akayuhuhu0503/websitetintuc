# Development operator tools only. Never change runtime authorization or machine policy.
. (Join-Path $PSScriptRoot 'common.ps1')
function Assert-DevSigningScope([string]$Environment) {
    if($Environment -cne 'Development' -or $env:COMPUTERNAME -ine 'MinhAnh' -or $env:DOTNET_ENVIRONMENT -in @('Production','Staging') -or $env:ASPNETCORE_ENVIRONMENT -in @('Production','Staging')){throw 'Local MinhAnh Development signing only; Production/Staging forbidden.'}
}
function Resolve-SigningPath([string]$Path,[string]$Prefix='.local') {
    $full=if([IO.Path]::IsPathRooted($Path)){[IO.Path]::GetFullPath($Path)}else{[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $Path))}
    $allowed=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $Prefix)).TrimEnd('\')+'\'
    if(-not $full.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Signing path escaped its private project scope.'}
    $node=$full
    while($node -and $node.Length -ge $script:ProjectRoot.Length){if(Test-Path -LiteralPath $node){if((Get-Item -LiteralPath $node -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse signing path refused.'}};$node=Split-Path -Parent $node}
    return $full
}
function Protect-SigningDirectory([string]$Path) {
    [IO.Directory]::CreateDirectory($Path)|Out-Null
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & rtk proxy icacls $Path /inheritance:r /grant:r ('*'+$sid+':(OI)(CI)F') '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Cannot protect project signing directory.'}
}
function Write-SigningJson([string]$Path,$Value) {
    $full=Resolve-SigningPath $Path
    [IO.Directory]::CreateDirectory((Split-Path $full))|Out-Null
    $file=[IO.File]::Open($full,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$bytes=[Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Value -Depth 18));$file.Write($bytes,0,$bytes.Length);$file.Flush($true)}finally{$file.Dispose()}
}
function Get-SigningSha([string]$Path) {return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Get-DevSigningCertificate([string]$Thumbprint) {
    if($Thumbprint -notmatch '^[A-Fa-f0-9]{40}$'){throw 'Exact certificate thumbprint required.'}
    $cert=Get-Item -LiteralPath ('Cert:\CurrentUser\My\'+$Thumbprint)
    if($cert.Subject -notlike 'CN=LandPortal Development Only *' -or -not $cert.HasPrivateKey -or $cert.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow -or $cert.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow){throw 'Expected current Development-only certificate/private key.'}
    $eku=@(foreach($ext in $cert.Extensions){if($ext -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]){foreach($oid in $ext.EnhancedKeyUsages){$oid.Value}}})
    if($eku -notcontains '1.3.6.1.5.5.7.3.3'){throw 'Code Signing EKU required.'}
    return $cert
}
function Get-DevSigningCertificateFacts($Certificate) {
    $chain=New-Object Security.Cryptography.X509Certificates.X509Chain
    # Local self-signed Dev cert has no CA revocation service. Authenticode is checked separately.
    $chain.ChainPolicy.RevocationMode=[Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
    try{
        $valid=$chain.Build($Certificate)
        $rsa=[Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Certificate)
        try{$exportPolicy=if($rsa -is [Security.Cryptography.RSACng]){$rsa.Key.ExportPolicy.ToString()}else{'UNKNOWN'};$keySize=if($rsa){$rsa.KeySize}else{0}}finally{if($rsa){$rsa.Dispose()}}
        $eku=@(foreach($ext in $Certificate.Extensions){if($ext -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]){foreach($oid in $ext.EnhancedKeyUsages){$oid.Value}}})
        return [ordered]@{subject=$Certificate.Subject;thumbprint=$Certificate.Thumbprint;notBeforeUtc=$Certificate.NotBefore.ToUniversalTime().ToString('O');notAfterUtc=$Certificate.NotAfter.ToUniversalTime().ToString('O');enhancedKeyUsageOids=$eku;rsaKeySize=$keySize;hasPrivateKey=$Certificate.HasPrivateKey;keyExportPolicy=$exportPolicy;chainValidLocalNoRevocation=$valid;chainStatus=@($chain.ChainStatus|ForEach-Object{$_.Status.ToString()});chainThumbprints=@($chain.ChainElements|ForEach-Object{$_.Certificate.Thumbprint});currentUserRootContains=(Test-Path -LiteralPath ('Cert:\CurrentUser\Root\'+$Certificate.Thumbprint));purpose='Development only; certificate validity is not business batch approval'}
    }finally{$chain.Dispose()}
}

# Verification needs only the signed document's public certificate. Never open a
# private CNG key to verify a signature or infer signing permissions from it.
# Provisioning/signing inspections above retain their private-key policy checks.
function Get-DevVerificationCertificateFacts($Certificate) {
    $public=New-Object Security.Cryptography.X509Certificates.X509Certificate2 (,$Certificate.RawData)
    $chain=New-Object Security.Cryptography.X509Certificates.X509Chain
    $rsa=$null
    try {
        $chain.ChainPolicy.RevocationMode=[Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
        $valid=$chain.Build($public)
        $rsa=[Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($public)
        if(-not $rsa){throw 'RSA public signing key required.'}
        $eku=@(foreach($ext in $public.Extensions){if($ext -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]){foreach($oid in $ext.EnhancedKeyUsages){$oid.Value}}})
        return [ordered]@{subject=$public.Subject;thumbprint=$public.Thumbprint;notBeforeUtc=$public.NotBefore.ToUniversalTime().ToString('O');notAfterUtc=$public.NotAfter.ToUniversalTime().ToString('O');enhancedKeyUsageOids=$eku;rsaKeySize=$rsa.KeySize;hasPrivateKey=$public.HasPrivateKey;privateKeyAccessed=$false;keyExportPolicy='NOT_ACCESSED_VERIFICATION';chainValidLocalNoRevocation=$valid;chainStatus=@($chain.ChainStatus|ForEach-Object{$_.Status.ToString()});chainThumbprints=@($chain.ChainElements|ForEach-Object{$_.Certificate.Thumbprint});currentUserRootContains=(Test-Path -LiteralPath ('Cert:\CurrentUser\Root\'+$public.Thumbprint));purpose='Public certificate verification only; does not attest private-key access/export policy or business authority'}
    }finally{if($rsa){$rsa.Dispose()};$chain.Dispose();$public.Dispose()}
}
