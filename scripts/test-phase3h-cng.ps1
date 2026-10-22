[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReportRoot,[Parameter(Mandatory=$true)][string]$Approval,[Parameter(Mandatory=$true)][string]$Plan,[Parameter(Mandatory=$true)][string]$PlanSha)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dev-signing-common.ps1')
Assert-DevSigningScope Development
$out=Resolve-SigningPath $ReportRoot '.local/evidence/phase3f'
if(Test-Path $out){throw 'Unique test evidence required'}
[void][IO.Directory]::CreateDirectory($out)
$thumb='94797BD79E275B09DEF613036BC00DF4EA91345F'
$folder=Resolve-SigningPath ('.local/signing/fixtures/cng-'+[Guid]::NewGuid().ToString('N')) '.local/signing/fixtures'
[void][IO.Directory]::CreateDirectory($folder)
$rows=New-Object 'System.Collections.Generic.List[object]'
function Check($name,[scriptblock]$body,[bool]$reject=$false){$errorText=$null;$raised=$false;try{& $body|Out-Null}catch{$raised=$true;$errorText=$_.Exception.Message};$pass=$raised -eq $reject;$rows.Add(@{name=$name;status=if($pass){'PASS'}else{'FAIL'};expectedReject=$reject;exception=$errorText});Write-Output ($(if($pass){'PASS '}else{'FAIL '})+$name)}
function Verify($file,$label,$signer=$thumb,$planPath=$script:fixturePlan,$planSha=$script:fixturePlanSha,$manifestPath=$script:fixturePlan,$manifestSha=$script:fixturePlanSha,$mode='Fixture'){
 & (Join-Path $PSScriptRoot 'test-dev-import-signature.ps1') -File $file -ExpectedThumbprint $signer -Plan $planPath -PlanSha $planSha -Manifest $manifestPath -ManifestSha $manifestSha -Mode $mode -Report ($out+'/'+$label+'.json')
}
Check 'Existing real approval Valid, exact hash/batch/database and public certificate only' {Verify $Approval 'actual-approval' $thumb $Plan $PlanSha $Plan $PlanSha 'ApprovedDocument'}
Check 'Public-only certificate has no private key and still supplies RSA/chain verification facts' {$native=Get-AuthenticodeSignature $Approval;$public=New-Object Security.Cryptography.X509Certificates.X509Certificate2 (,$native.SignerCertificate.RawData);try{$facts=Get-DevVerificationCertificateFacts $public;if($public.HasPrivateKey -or $facts.privateKeyAccessed -or $facts.rsaKeySize -ne 3072 -or -not $facts.chainValidLocalNoRevocation){throw 'Public certificate check failed'}}finally{$public.Dispose()}}
$script:fixturePlan=$folder+'/plan.json'
$batch=[Guid]::NewGuid().ToString()
Write-SigningJson $script:fixturePlan @{SchemaVersion=3;BatchId=$batch;Database='LandPortal_Test';TechnicalFixture=$true;Rows=@();TargetMediaRootSha256=('0'*64)}
$script:fixturePlanSha=Get-SigningSha $script:fixturePlan
$meta=@{ApprovalId=('TECHNICAL-CNG-'+[Guid]::NewGuid());BatchId=$batch;PlanSha256=$script:fixturePlanSha;ManifestSha256=$script:fixturePlanSha;TargetDatabase='LandPortal_Test';OperatorSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;ExpiresUtc=[DateTimeOffset]::UtcNow.AddHours(1).ToString('O');Actions=@('FixtureVerify');TechnicalFixture=$true;BusinessApproval=$false}
function SignFixture($label,$metadata){
 $file=$folder+'/'+$label+'.ps1';$text="# Technical Test fixture, not a Dev batch approval. Never execute.`r`n# LANDPORTAL_PHASE3C_APPROVAL "+($metadata|ConvertTo-Json -Compress)+"`r`nthrow 'Technical fixture only.'`r`n"
 [IO.File]::WriteAllText($file,$text,[Text.UTF8Encoding]::new($true))
 $cert=Get-DevSigningCertificate $thumb
 $sig=Set-AuthenticodeSignature -LiteralPath $file -Certificate $cert -HashAlgorithm SHA256
 if($sig.Status.ToString() -cne 'Valid'){throw 'Native fixture signing failed; no fixture accepted'}
 return $file
}
$valid=SignFixture 'valid' $meta
Check 'Native signing still uses actual existing private key for a Test-only fixture' {if((Get-AuthenticodeSignature $valid).Status.ToString() -cne 'Valid'){throw 'Fixture not native Valid'}}
Check 'Genuine new Test fixture passes verification' {Verify $valid 'fixture-valid'}
$tampered=$folder+'/tampered.ps1';Copy-Item $valid $tampered;[IO.File]::AppendAllText($tampered,"`r`n# altered")
Check 'Tampered Authenticode rejected' {Verify $tampered 'tampered'} $true
Check 'Wrong pinned thumbprint rejected' {Verify $valid 'wrong-pin' ('0'*40)} $true
$changed=$folder+'/changed-plan.json';[IO.File]::WriteAllText($changed,([IO.File]::ReadAllText($script:fixturePlan)+' '))
Check 'Altered plan bytes rejected' {Verify $valid 'changed-plan' $thumb $changed} $true
Check 'Altered manifest bytes rejected' {Verify $valid 'changed-manifest' $thumb $script:fixturePlan $script:fixturePlanSha $changed} $true
foreach($variant in @('batch','hash','database','expired','operator','action')){
 $m=$meta|ConvertTo-Json -Depth 8|ConvertFrom-Json
 switch($variant){batch{$m.BatchId=[Guid]::NewGuid().ToString()} hash{$m.PlanSha256='0'*64} database{$m.TargetDatabase='LandPortal_Dev'} expired{$m.ExpiresUtc=[DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O')} operator{$m.OperatorSid='S-1-0-0'} action{$m.Actions=@('Apply')}}
 $file=SignFixture $variant $m
 Check ('Native Valid signature but wrong '+$variant+' rejected') {Verify $file ('wrong-'+$variant)} $true
}
$unsigned=$folder+'/no-private.ps1';[IO.File]::WriteAllText($unsigned,"# Technical signing failure fixture`r`nthrow 'not approval'")
Check 'Signing with a public-only certificate fails closed without private-key access or ACL changes' {$cert=Get-Item ('Cert:\CurrentUser\My\'+$thumb);$public=New-Object Security.Cryptography.X509Certificates.X509Certificate2 (,$cert.RawData);try{$sig=Set-AuthenticodeSignature -LiteralPath $unsigned -Certificate $public -HashAlgorithm SHA256;if($sig.Status.ToString() -cne 'Valid'){throw 'No private signing key available'}}finally{$public.Dispose()}} $true
$failed=@($rows|Where-Object status -EQ FAIL).Count
Write-SigningJson ($out+'/checks.json') @{passed=$rows.Count-$failed;failed=$failed;tests=$rows.ToArray();fixtureRoot=$folder;actualApprovalResigned=$false;certCreated=$false;privateExported=$false;keyAclChanged=$false;sqlWrites=0;untrustedCertificateTest='Separate unchanged native bridge envelope trust-negative runner; no untrusted certificate installed'}
Write-Output ('RESULT '+($rows.Count-$failed)+'/'+$rows.Count+' CNG-focused checks')
if($failed){exit 1}
