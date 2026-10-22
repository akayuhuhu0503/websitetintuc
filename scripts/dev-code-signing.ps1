[CmdletBinding(SupportsShouldProcess=$true)]
param([Parameter(Mandatory=$true)][ValidateSet('Create','TrustCurrentUser','Inspect')][string]$Action,[ValidateSet('Development','Staging','Production')][string]$Environment='Development',[string]$Thumbprint,[ValidateRange(1,30)][int]$Days=30,[Parameter(Mandatory=$true)][string]$Report)
. (Join-Path $PSScriptRoot 'dev-signing-common.ps1')
Assert-DevSigningScope $Environment
$reportFull=Resolve-SigningPath $Report
if(Test-Path -LiteralPath $reportFull){throw 'Unique report required.'}
$certFolder=Resolve-SigningPath '.local/signing/certificates/receipt.json';$certFolder=Split-Path $certFolder
if($Action -eq 'Create'){
    if(-not $PSCmdlet.ShouldProcess('Cert:\CurrentUser\My','Create Development Code Signing certificate; non-exportable key')){return}
    Protect-SigningDirectory $certFolder
    $cert=New-SelfSignedCertificate -Type CodeSigningCert -Subject ('CN=LandPortal Development Only '+$env:COMPUTERNAME+' '+[Guid]::NewGuid().ToString('N').Substring(0,8)) -FriendlyName 'LandPortal Development Only - not Production authority' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyUsage DigitalSignature -KeyExportPolicy NonExportable -Provider 'Microsoft Software Key Storage Provider' -NotBefore ([DateTime]::Now.AddMinutes(-5)) -NotAfter ([DateTime]::Now.AddDays($Days))
    $public=Join-Path $certFolder ($cert.Thumbprint+'.cer')
    Export-Certificate -Cert $cert -FilePath $public -Type CERT|Out-Null
    $facts=Get-DevSigningCertificateFacts $cert
    if($facts.keyExportPolicy -cne 'None'){throw 'Key is not confirmed non-exportable; no trust configured.'}
    Write-SigningJson ('.local/signing/certificates/'+$cert.Thumbprint+'.json') ([ordered]@{thumbprint=$cert.Thumbprint;createdBySid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;publicCertificate=$public;environment='Development';privateKeyExported=$false;facts=$facts})
}else{
    $cert=Get-DevSigningCertificate $Thumbprint
    $receipt=Get-Content -LiteralPath (Join-Path $certFolder ($cert.Thumbprint+'.json')) -Encoding UTF8 -Raw|ConvertFrom-Json
    if($receipt.createdBySid -cne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -or $receipt.environment -cne 'Development'){throw 'Certificate receipt owner/scope differs.'}
    if($Action -eq 'TrustCurrentUser'){
        if(-not $PSCmdlet.ShouldProcess(('Cert:\CurrentUser\Root\'+$cert.Thumbprint),'Trust this Development certificate for current user only')){return}
        $public=New-Object Security.Cryptography.X509Certificates.X509Certificate2 (,$cert.RawData)
        $store=New-Object Security.Cryptography.X509Certificates.X509Store 'Root','CurrentUser'
        try{$store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite);$store.Add($public)}finally{$store.Close();$store.Dispose();$public.Dispose()}
    }
    $facts=Get-DevSigningCertificateFacts $cert
}
Write-SigningJson $Report ([ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('O');action=$Action;certificate=$facts;machineTrustChanged=$false;trustedPublisherChanged=$false;executionPolicyChanged=$false;runtimeSignerPolicyConfigured=$false;businessApprovalCreated=$false})
Write-Output ('Development certificate '+$cert.Thumbprint+'; action='+$Action+'; local chain='+$facts.chainValidLocalNoRevocation+'; non-exportable='+($facts.keyExportPolicy -eq 'None'))
