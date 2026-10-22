[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$File,[Parameter(Mandatory=$true)][string]$ExpectedThumbprint,[Parameter(Mandatory=$true)][string]$Plan,[Parameter(Mandatory=$true)][string]$PlanSha,[Parameter(Mandatory=$true)][string]$Manifest,[Parameter(Mandatory=$true)][string]$ManifestSha,[Parameter(Mandatory=$true)][string]$Report,[ValidateSet('Fixture','ApprovedDocument')][string]$Mode='Fixture',[ValidateSet('Development','Staging','Production')][string]$Environment='Development')
. (Join-Path $PSScriptRoot 'dev-signing-common.ps1')
Assert-DevSigningScope $Environment
$f=Resolve-SigningPath $File;$p=Resolve-SigningPath $Plan;$m=Resolve-SigningPath $Manifest
if($Mode -eq 'Fixture'){$null=Resolve-SigningPath $File '.local/signing/fixtures'}else{$null=Resolve-SigningPath $File '.local/approvals/phase3c'}
$lock=[IO.File]::Open($f,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try{
    $sig=Get-AuthenticodeSignature -LiteralPath $f
    if($sig.Status.ToString() -cne 'Valid' -or $null -eq $sig.SignerCertificate -or $sig.SignerCertificate.Thumbprint -ine $ExpectedThumbprint){throw 'Invalid or unexpected Authenticode signer.'}
    $cert=$sig.SignerCertificate;$facts=Get-DevVerificationCertificateFacts $cert
    if(-not $facts.chainValidLocalNoRevocation -or $facts.subject -notlike 'CN=LandPortal Development Only *' -or $facts.rsaKeySize -lt 2048 -or $facts.enhancedKeyUsageOids -notcontains '1.3.6.1.5.5.7.3.3' -or [DateTimeOffset]$facts.notBeforeUtc -gt [DateTimeOffset]::UtcNow -or [DateTimeOffset]$facts.notAfterUtc -le [DateTimeOffset]::UtcNow){throw 'Public certificate chain, purpose, RSA key or validity check failed.'}
    if((Get-SigningSha $p) -cne $PlanSha -or (Get-SigningSha $m) -cne $ManifestSha){throw 'Plan/manifest bytes changed.'}
    $lines=@([IO.File]::ReadAllLines($f,[Text.Encoding]::UTF8)|Where-Object{$_.StartsWith('# LANDPORTAL_PHASE3C_APPROVAL ',[StringComparison]::Ordinal)})
    if($lines.Count -ne 1){throw 'Exactly one signed metadata line required.'}
    $a=$lines[0].Substring('# LANDPORTAL_PHASE3C_APPROVAL '.Length)|ConvertFrom-Json
    $planData=Get-Content -LiteralPath $p -Raw -Encoding UTF8|ConvertFrom-Json
    $manifestData=Get-Content -LiteralPath $m -Raw -Encoding UTF8|ConvertFrom-Json
    if($a.PlanSha256 -cne $PlanSha -or $a.ManifestSha256 -cne $ManifestSha -or $a.OperatorSid -cne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -or [DateTimeOffset]$a.ExpiresUtc -le [DateTimeOffset]::UtcNow -or [DateTimeOffset]$a.ExpiresUtc -gt [DateTimeOffset]::UtcNow.AddDays(7) -or [string]::IsNullOrWhiteSpace($a.ApprovalId)){throw 'Signed scope/operator/expiry differs.'}
    $manifestIsPlan=($p -ieq $m -and $PlanSha -ceq $ManifestSha)
    $manifestMatches=if($manifestIsPlan){$true}else{($manifestData.target -ceq $planData.Database -and $manifestData.planSha256 -ceq $PlanSha)}
    if($a.TargetDatabase -cne $planData.Database -or -not $manifestMatches){throw 'Target or manifest binding differs.'}
    if($Mode -eq 'ApprovedDocument' -or $a.PSObject.Properties.Name -contains 'BatchId'){
        if($a.BatchId -cne $planData.BatchId){throw 'Signed approval batch identity differs.'}
    }
    if($Mode -eq 'Fixture'){
        if($a.TargetDatabase -cne 'LandPortal_Test' -or @($a.Actions).Count -ne 1 -or $a.Actions[0] -cne 'FixtureVerify' -or -not $a.TechnicalFixture){throw 'Fixture must be Test-only and cannot authorize Apply.'}
    }else{
        if($a.TargetDatabase -cne 'LandPortal_Dev' -or $planData.SchemaVersion -ne 3 -or @($a.Actions).Count -eq 0 -or @($a.Actions|Where-Object{$_ -notin @('Apply','Rollback')}).Count -or [string]::IsNullOrWhiteSpace($a.ApprovedBy) -or [string]::IsNullOrWhiteSpace($a.ApprovalReference)){throw 'Explicit Dev business approval metadata required.'}
    }
    Write-SigningJson $Report ([ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('O');status='PASS';mode=$Mode;signatureStatus=$sig.Status.ToString();thumbprint=$cert.Thumbprint;certificate=$facts;planSha256=$PlanSha;manifestSha256=$ManifestSha;approvalFileSha256=Get-SigningSha $f;metadata=$a;businessApprovalMetadataPresent=($Mode -eq 'ApprovedDocument');independentBusinessAuthorityVerified=$false;operatorMustCheckApprovalReference=$true;sqlChanged=$false;existingApplyGuardsStillRequired=$true})
    Write-Output ('PASS Authenticode + certificate + exact batch binding; mode='+$Mode+'; no SQL execution.')
}finally{$lock.Dispose()}
