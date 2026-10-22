[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('PrepareFixture','Verify')][string]$Stage,[Parameter(Mandatory=$true)][string]$Thumbprint,[Parameter(Mandatory=$true)][string]$RunRecord)
. (Join-Path $PSScriptRoot 'dev-signing-common.ps1')
Assert-DevSigningScope 'Development'
$task=Get-Content -LiteralPath (Resolve-SigningPath $RunRecord) -Encoding UTF8 -Raw|ConvertFrom-Json
$folder=Resolve-SigningPath ($task.fixtureRoot+'/input.json') '.local/signing/fixtures';$folder=Split-Path $folder
$cert=Get-DevSigningCertificate $Thumbprint
function New-FixtureSignature([string]$Path,$Metadata) {
    $text="# Technical Test fixture only. Never execute.`r`n# LANDPORTAL_PHASE3C_APPROVAL "+($Metadata|ConvertTo-Json -Compress)+"`r`nthrow 'Technical fixture is not executable approval.'`r`n"
    $f=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$enc=[Text.UTF8Encoding]::new($true);$bom=$enc.GetPreamble();$bytes=$enc.GetBytes($text);$f.Write($bom,0,$bom.Length);$f.Write($bytes,0,$bytes.Length)}finally{$f.Dispose()}
    $signature=Set-AuthenticodeSignature -LiteralPath $Path -Certificate $cert -HashAlgorithm SHA256
    return $signature.Status.ToString()
}
if($Stage -eq 'PrepareFixture'){
    if(Test-Path (Join-Path $folder 'input.json')){throw 'Fixture already exists.'}
    Protect-SigningDirectory $folder
    $plan=Join-Path $folder 'plan.json';$manifest=Join-Path $folder 'manifest.json';$signed=Join-Path $folder 'fixture.ps1'
    Write-SigningJson $plan ([ordered]@{SchemaVersion=3;BatchId=[Guid]::NewGuid().ToString();Database='LandPortal_Test';TechnicalFixture=$true;Rows=@();TargetMediaRootSha256=('0'*64)})
    $planSha=Get-SigningSha $plan
    Write-SigningJson $manifest ([ordered]@{target='LandPortal_Test';planSha256=$planSha;technicalFixture=$true;purpose='Cryptographic verification only; no SQL/source record or Apply'})
    $manifestSha=Get-SigningSha $manifest
    $metadata=[ordered]@{ApprovalId=('TECHNICAL-FIXTURE-'+[Guid]::NewGuid().ToString());PlanSha256=$planSha;TargetDatabase='LandPortal_Test';OperatorSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;ExpiresUtc=[DateTimeOffset]::UtcNow.AddHours(2).ToString('O');Actions=@('FixtureVerify');ManifestSha256=$manifestSha;TechnicalFixture=$true;BusinessApproval=$false}
    $status=New-FixtureSignature $signed $metadata
    Write-SigningJson (Join-Path $folder 'input.json') ([ordered]@{plan=$plan;planSha=$planSha;manifest=$manifest;manifestSha=$manifestSha;file=$signed;metadata=$metadata;thumbprint=$cert.Thumbprint;signatureBeforeTrust=$status})
    Write-SigningJson ($task.root+'/fixture-before-trust.json') ([ordered]@{signatureStatus=$status;currentUserRootContains=(Test-Path ('Cert:\CurrentUser\Root\'+$cert.Thumbprint));businessApproval=$false;fixtureOnly=$true})
    Write-Output ('Fixture signed before trust; observed='+$status+'; not valid authority or Dev approval.')
    return
}
$fixtureInfo=Get-Content -LiteralPath (Join-Path $folder 'input.json') -Encoding UTF8 -Raw|ConvertFrom-Json
$readySignature=Get-AuthenticodeSignature -LiteralPath $fixtureInfo.file
if($readySignature.Status.ToString() -cne 'Valid'){
    $blocked='blocked-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
    Write-SigningJson ($task.root+'/'+$blocked+'.json') (@{status='BLOCKED_BEFORE_SCOPE_TESTS';signatureStatus=$readySignature.Status.ToString();testsRun=0;reason='CurrentUser trust/Authenticode Valid required before scope negatives can prove the intended rejection.'})
    Write-Output 'BLOCKED: no valid Authenticode signature yet; do not count scope negatives as PASS.'
    exit 2
}
$attempt='verify-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$attemptFolder=Join-Path $folder $attempt;Protect-SigningDirectory $attemptFolder
$rows=New-Object 'System.Collections.Generic.List[object]'
function Check([string]$Name,[scriptblock]$Body,[bool]$Reject=$false) {
    $raised=$false;$reason=$null
    try{& $Body|Out-Null}catch{$raised=$true;$reason=$_.Exception.Message}
    $pass=($raised -eq $Reject)
    $rows.Add([pscustomobject]@{name=$Name;status=if($pass){'PASS'}else{'FAIL'};expectedReject=$Reject;exception=$reason})
    $prefix=if($pass){'PASS '}else{'FAIL '};Write-Output ($prefix+$Name)
}
function Verify-File([string]$Path,[string]$Label,[string]$Signer=$Thumbprint,[string]$PlanPath=$fixtureInfo.plan,[string]$ManifestPath=$fixtureInfo.manifest,[string]$Mode='Fixture') {
    & (Join-Path $PSScriptRoot 'test-dev-import-signature.ps1') -File $Path -ExpectedThumbprint $Signer -Plan $PlanPath -PlanSha $fixtureInfo.planSha -Manifest $ManifestPath -ManifestSha $fixtureInfo.manifestSha -Mode $Mode -Report (Join-Path $attemptFolder ($Label+'.json'))
}
Check 'CurrentUser chain valid, Code Signing EKU and non-exportable CNG key' { $f=Get-DevSigningCertificateFacts $cert;if(-not $f.chainValidLocalNoRevocation -or $f.keyExportPolicy -cne 'None'){throw 'Certificate check failed'} }
Check 'Genuine Authenticode and exact Test fixture plan/manifest binding' {Verify-File $fixtureInfo.file 'positive'}
$tampered=Join-Path $attemptFolder 'tampered.ps1';$content=[IO.File]::ReadAllText($fixtureInfo.file,[Text.Encoding]::UTF8).Replace('Technical Test fixture only.','Changed Test fixture only.');[IO.File]::WriteAllText($tampered,$content,[Text.UTF8Encoding]::new($true))
Check 'Changed signed bytes rejected' {Verify-File $tampered 'tampered'} $true
Check 'Unapproved thumbprint rejected' {Verify-File $fixtureInfo.file 'wrong-signer' ('0'*40)} $true
$planChanged=Join-Path $attemptFolder 'changed-plan.json';[IO.File]::WriteAllText($planChanged,([IO.File]::ReadAllText($fixtureInfo.plan)+' '))
Check 'Changed plan bytes rejected' {Verify-File $fixtureInfo.file 'plan-changed' $Thumbprint $planChanged} $true
$manifestChanged=Join-Path $attemptFolder 'changed-manifest.json';[IO.File]::WriteAllText($manifestChanged,([IO.File]::ReadAllText($fixtureInfo.manifest)+' '))
Check 'Changed manifest bytes rejected' {Verify-File $fixtureInfo.file 'manifest-changed' $Thumbprint $fixtureInfo.plan $manifestChanged} $true
foreach($variant in @('expired','wrong-operator','wrong-database','wrong-action')){
    $m=$fixtureInfo.metadata|ConvertTo-Json -Depth 8|ConvertFrom-Json
    switch($variant){'expired'{$m.ExpiresUtc=[DateTimeOffset]::UtcNow.AddHours(-1).ToString('O')};'wrong-operator'{$m.OperatorSid='S-1-0-0'};'wrong-database'{$m.TargetDatabase='LandPortal_Dev'};'wrong-action'{$m.Actions=@('Apply')}}
    $path=Join-Path $attemptFolder ($variant+'.ps1');$null=New-FixtureSignature $path $m
    Check ('Valid signature with '+$variant+' scope rejected') {Verify-File $path $variant} $true
}
Check 'Fixture cannot become an ApprovedDocument for Dev' {Verify-File $fixtureInfo.file 'not-business' $Thumbprint $fixtureInfo.plan $fixtureInfo.manifest 'ApprovedDocument'} $true
Check 'Production certificate provisioning denied before side effects' {& (Join-Path $PSScriptRoot 'dev-code-signing.ps1') -Action Create -Environment Production -Report (Join-Path $attemptFolder 'forbidden-production.json')} $true
Check 'Staging verifier denied' {& (Join-Path $PSScriptRoot 'test-dev-import-signature.ps1') -Environment Staging -File $fixtureInfo.file -ExpectedThumbprint $Thumbprint -Plan $fixtureInfo.plan -PlanSha $fixtureInfo.planSha -Manifest $fixtureInfo.manifest -ManifestSha $fixtureInfo.manifestSha -Report (Join-Path $attemptFolder 'forbidden-staging.json')} $true
Check 'Missing explicit user approval cannot create a Dev document' {& (Join-Path $PSScriptRoot 'sign-dev-import-approval.ps1') -Review '.local/signing/reviews/NO_APPROVAL.json' -ReviewSha ('0'*64) -Thumbprint $Thumbprint -ApprovedBy 'TECHNICAL CONTROL' -ApprovalReference 'NOT AUTHORIZATION' -ConfirmPlanSha ('0'*64) -ExpiresUtc ([DateTimeOffset]::UtcNow.AddHours(1))} $true
Check 'Signing path traversal rejected' {$null=Resolve-SigningPath '../outside-project.txt'} $true
Check 'Certificate validity did not create runtime signer policy or actual approval files' {if(-not [string]::IsNullOrWhiteSpace($env:SafeImport__ApprovedSignerThumbprints) -or @(Get-ChildItem (Join-Path $script:ProjectRoot '.local/approvals/phase3c') -Filter '*.ps1' -ErrorAction SilentlyContinue).Count){throw 'Unexpected actual runtime authority'}}
$failed=@($rows|Where-Object status -eq 'FAIL').Count
Write-SigningJson ($task.root+'/'+$attempt+'.json') ([ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('O');purpose='Dedicated cryptography/scope tests only; no SQL or Dev Apply';tests=$rows.ToArray();passed=$rows.Count-$failed;failed=$failed;fixture=$folder;privateKeyExported=$false;actualBusinessApprovalCreated=$false})
Write-Output ('RESULT '+($rows.Count-$failed)+'/'+$rows.Count+' focused signing checks; no full regression.')
if($failed){exit 1}
