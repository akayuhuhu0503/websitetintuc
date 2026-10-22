[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Plan,[Parameter(Mandatory=$true)][string]$PlanSha,[Parameter(Mandatory=$true)][string]$Manifest,[Parameter(Mandatory=$true)][string]$ManifestSha,[Parameter(Mandatory=$true)][string]$Output,[ValidateSet('Development','Staging','Production')][string]$Environment='Development')
. (Join-Path $PSScriptRoot 'dev-signing-common.ps1')
Assert-DevSigningScope $Environment
$p=Resolve-SigningPath $Plan '.local/evidence';$m=Resolve-SigningPath $Manifest '.local/evidence';$out=Resolve-SigningPath $Output '.local/signing/reviews'
if((Get-SigningSha $p) -cne $PlanSha -or (Get-SigningSha $m) -cne $ManifestSha){throw 'Exact plan/manifest hash required.'}
$planData=Get-Content -LiteralPath $p -Raw -Encoding UTF8|ConvertFrom-Json
$manifestData=Get-Content -LiteralPath $m -Raw -Encoding UTF8|ConvertFrom-Json
$manifestIsPlan=($p -ieq $m -and $PlanSha -ceq $ManifestSha)
$manifestMatches=if($manifestIsPlan){$true}else{($manifestData.target -ceq 'LandPortal_Dev' -and $manifestData.planSha256 -ceq $PlanSha)}
if($planData.Database -cne 'LandPortal_Dev' -or $planData.SchemaVersion -ne 3 -or -not $manifestMatches){throw 'Review requires matching schema3 Dev plan and manifest (the plan itself may be the manifest).'}
Protect-SigningDirectory (Split-Path $out)
Write-SigningJson $Output ([ordered]@{status='PENDING_USER_APPROVAL';reviewId=[Guid]::NewGuid().ToString();createdUtc=[DateTimeOffset]::UtcNow.ToString('O');planPath=$p;planSha256=$PlanSha;manifestPath=$m;manifestSha256=$ManifestSha;batchId=$planData.BatchId;targetDatabase='LandPortal_Dev';targetMediaRootSha256=$planData.TargetMediaRootSha256;operatorSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;requestedActions=@('Apply');freshProofBackupAndSourceStamps='MUST_REVALIDATE_BEFORE_SIGN_AND_APPLY';approvedBy=$null;approvalReference=$null;signatureCreated=$false;executableAuthorization=$false})
Write-Output 'Review draft created only. No approval, signature, runtime trust or SQL change.'
