[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Review,[Parameter(Mandatory=$true)][string]$ReviewSha,[Parameter(Mandatory=$true)][string]$Thumbprint,[Parameter(Mandatory=$true)][string]$ApprovedBy,[Parameter(Mandatory=$true)][string]$ApprovalReference,[Parameter(Mandatory=$true)][string]$ConfirmPlanSha,[Parameter(Mandatory=$true)][DateTimeOffset]$ExpiresUtc,[ValidateSet('Apply','Rollback')][string[]]$Actions=@('Apply'),[switch]$ConfirmUserApproval,[ValidateSet('Development','Staging','Production')][string]$Environment='Development')
. (Join-Path $PSScriptRoot 'dev-signing-common.ps1')
Assert-DevSigningScope $Environment
if(-not $ConfirmUserApproval -or [string]::IsNullOrWhiteSpace($ApprovedBy) -or [string]::IsNullOrWhiteSpace($ApprovalReference)){throw 'Explicit real user approval/approver/reference required; no approval synthesized.'}
$path=Resolve-SigningPath $Review '.local/signing/reviews'
if((Get-SigningSha $path) -cne $ReviewSha){throw 'Review hash differs.'}
$r=Get-Content -LiteralPath $path -Encoding UTF8 -Raw|ConvertFrom-Json
if($r.status -cne 'PENDING_USER_APPROVAL' -or $r.targetDatabase -cne 'LandPortal_Dev' -or $ConfirmPlanSha -cne $r.planSha256 -or $r.operatorSid -cne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value){throw 'Wrong review target/hash/operator.'}
$p=Resolve-SigningPath $r.planPath '.local/evidence';$m=Resolve-SigningPath $r.manifestPath '.local/evidence'
if((Get-SigningSha $p) -cne $r.planSha256 -or (Get-SigningSha $m) -cne $r.manifestSha256){throw 'Reviewed bytes changed.'}
$cert=Get-DevSigningCertificate $Thumbprint
if($ExpiresUtc -le [DateTimeOffset]::UtcNow -or $ExpiresUtc -gt [DateTimeOffset]::UtcNow.AddDays(7) -or $ExpiresUtc.UtcDateTime -gt $cert.NotAfter.ToUniversalTime()){throw 'Invalid approval expiry.'}
$id=[Guid]::NewGuid().ToString();$out=Resolve-SigningPath ('.local/approvals/phase3c/'+$id+'.ps1') '.local/approvals/phase3c'
Protect-SigningDirectory (Split-Path $out)
$approval=[ordered]@{ApprovalId=$id;PlanSha256=$r.planSha256;TargetDatabase='LandPortal_Dev';OperatorSid=$r.operatorSid;ExpiresUtc=$ExpiresUtc.ToUniversalTime().ToString('O');Actions=$Actions;ApprovedBy=$ApprovedBy;ApprovalReference=$ApprovalReference;ManifestSha256=$r.manifestSha256;BatchId=$r.batchId;Environment='Development';NotProductionAuthorization=$true}
$text="# Development approval data only. Never execute this document.`r`n# LANDPORTAL_PHASE3C_APPROVAL "+($approval|ConvertTo-Json -Compress)+"`r`nthrow 'Approval document is not executable.'`r`n"
$stream=[IO.File]::Open($out,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$encoding=[Text.UTF8Encoding]::new($true);$bom=$encoding.GetPreamble();$bytes=$encoding.GetBytes($text);$stream.Write($bom,0,$bom.Length);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
$sig=Set-AuthenticodeSignature -LiteralPath $out -Certificate $cert -HashAlgorithm SHA256
if($sig.Status.ToString() -cne 'Valid' -or $sig.SignerCertificate.Thumbprint -cne $cert.Thumbprint){throw 'Signature not valid; keep evidence and do not Apply.'}
Write-SigningJson ($out+'.receipt.json') ([ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('O');approvalPath=$out;approvalSha256=Get-SigningSha $out;thumbprint=$cert.Thumbprint;metadata=$approval;callerAttestedUserApproval=$true;applied=$false;runtimeSignerPolicyConfigured=$false;proofBackupAndStampValidation='REQUIRED_BY_EXISTING_APPLY_TOOL'})
Write-Output ('Signed explicit caller-approved Dev document: '+$out+'; SQL untouched.')
