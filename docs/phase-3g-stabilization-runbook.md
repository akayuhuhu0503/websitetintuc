# Packet Stabilization — review trước ký/Apply

[Báo cáo](phase-3g-stabilization-report.md). Batch **26afcb7a-b3c1-4708-9009-c3d296e0afdc**, manifestSHA **a6b02645a428b4f758e8ec1904119b39980f114f71a5d720a9c3be6f3473424f**;3content/4revision/7ảnh/6alias. Chưa có signed approval; **không chạy ký hoặc Apply từ việc đọc tài liệu này**. Yêu cầu hiện tại chỉ chuẩn bị packet.

## Cấu hình operator và dry-run exact packet

Trong PowerShell mới ở root workspace; cùng CurrentUser đã có certificate, không Create/Trust lại. Process pin được người dùng cho phép cho Development, không ghi .env/User/Machine env hoặc quyền SQL/Identity.

```powershell
$ErrorActionPreference='Stop'
. ./scripts/dev-signing-common.ps1
Assert-DevSigningScope Development
$r='.local/evidence/phase3f/phase3g-stabilize-20261021T021233Z-20e31fbb'
$plan=$r+'/devpreview/plan.json'
$sha='a6b02645a428b4f758e8ec1904119b39980f114f71a5d720a9c3be6f3473424f'
$review='.local/signing/reviews/phase3g-stabilize-20261021T021233Z-20e31fbb/review.json'
$reviewSha='9c4111468bfb36f3f691179e1954679385ee395c556592b59c8b3f0536fa23e4'
$thumb='94797BD79E275B09DEF613036BC00DF4EA91345F'
if((Get-SigningSha $plan) -cne $sha -or (Get-SigningSha $review) -cne $reviewSha){throw 'Reviewed bytes changed'}
$b=Get-ProjectConnectionBuilder LandPortal_Dev
$env:ConnectionStrings__Portal=$b.ConnectionString;$env:Sql__ExpectedDatabase='LandPortal_Dev'
$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false'
$env:SafeImport__ApprovedSignerThumbprints=$thumb;$env:MigrationDiagnostics__Root=$null
foreach($pair in @(@('MediaRoot','media'),@('SnapshotRoot','snapshots'),@('TempRoot','temp'),@('KeysRoot','keys'))){[Environment]::SetEnvironmentVariable(('Storage__'+$pair[0]),(Join-Path $script:ProjectRoot ('.local/'+$pair[1])),'Process')}
$cli='.local/build/phase3g/stabilization-current/bin/MigrationCli/debug/MigrationCli.dll'
$manifest=Get-Content ($r+'/batch-review.json') -Raw -Encoding UTF8|ConvertFrom-Json
if((Get-SigningSha $cli) -cne $manifest.compiledToolSha256){throw 'Reviewed operator binary changed'}
$cpSha='d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3'
$cp='.local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/'+$cpSha+'.bin'
$inv='.local/evidence/phase3c/phase3g-tracking-20261021T015245Z-a42b32ea-recovery-review/inventory.json'
$invSha='685b0bb56c72aecc4b6ddd5268c57120fab9a54964fb3cbb4ebca81397508f67'
$proof='.local/evidence/phase3c/phase3g-stabilize-20261021T021233Z-20e31fbb/scan/proof-set.json'
$proofSha='3ebcf7e9d9b93648147ec4874cc7bbf464162d6e892474ea990311a817498f68'
$deps=@('--checkpoint',$cp,'--checkpoint-sha',$cpSha,'--inventory',$inv,'--inventory-sha',$invSha,'--proof-set',$proof,'--proof-set-sha',$proofSha,'--plan',$plan,'--plan-sha',$sha)
$label='phase3g-next-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$out='.local/evidence/phase3f/'+$label
# Readonly; native current identity, proof bytes/receipts/TTL and source stamps revalidated.
rtk proxy $script:DotnetPath $cli batch-preflight --report ($out+'/preflight') @deps
if($LASTEXITCODE){throw 'Preflight failed; no signing/Apply. Reconcile/refresh only as appropriate.'}
```

Trước bất kỳ writeDev nào còn phải query lại đúng tuple trong `sql-identity.json`: DB_NAME/expectedDB LandPortal_Dev, SERVERPROPERTY MinhAnh/SQLEXPRESS, databaseGUIDb499750c-a0c8-4003-b7ee-dce4938f78dc, Windows service. `@@SERVERNAME` THANGPD đã được chủ máy xác nhận rename, không sửa metadata/guard để vượt điều kiện. Schema baseline10migrations/latestV05EditorialDocuments; operator phải có quyền sẵn, không grant.

## Phương án B khi proof đổi/hết hiệu lực

Giữ strict validation. Hiện proof earliestExpiry22/10/2026 09:18:45VN, definitions118; **không** được dùng nếu definitions đổi trước TTL. Không dùng policy A chưa được ATTT duyệt.

Job2fb80968-969a-4223-bd13-c33295b2badb đã được review đúng60publicTestcopies theo Stabilization request. Job cũ301f180receipt giữ nguyên. Resume/refresh cùng job2fb cho đến giới hạn3receipt/resource; không tạo job tự động để né giới hạn. Khi chạm limit, cần review job mới và giữ raw cũ. Không scan/promote originalDev53, không StageTest mặc định.

Chuẩn bị report/packet **mới**: nạp SQLTest và roots `.local/tests/phase2a/{media,snapshots,temp,keys}`, Development/MailDisabled/no unscanned bypass, giữ verified backup hợp lệ; chạy recovery-scan với exact`$inv/$invSha` và report Phase3C unique, rồi native validate/controls. Những lệnh này chỉ được dùng trên scope public Test đã review, không để kế thừa roots Dev.

```powershell
# Chỉ trong operator session SQLTest/storageTest đã xác minh:
$scanReport='.local/evidence/phase3c/'+$label+'-fresh-scan'
rtk proxy $script:DotnetPath $cli recovery-scan --report $scanReport --inventory $inv --inventory-sha $invSha
if($LASTEXITCODE){throw 'Scan uncertain/failed; reconcile SQL receipt and fail closed'}
$newProof=$scanReport+'/proof-set.json'
$newProofSha=Get-SigningSha $newProof
# Sau đó dùng newProof/newProofSha cho Test preview/preflight rồi Dev preview/preflight
# với report mới và optional-image review bound. Không thay vào $deps của plan đã duyệt.
```

Các bước đã thực hiện trong run hiện hành được tái lập bằng `.local/phase3g-stabilization-ops.ps1 -Action Controls|Validate|TestPreview|TestPreflight|DevPreview|DevPreflight` với pointer **của run mới**; helper từ chối ghi đè log. Recovery-registry/scan/source-proof/technicalreview hiện có được ghi trong `reviewed-recovery-job.json`, `register.log`, `scan.log`. Không dùng lại cùng report. Không chạy StageTest/Commit/Apply từ helper này.

Tạo plan/hash mới nếu proof-set thay đổi, kiểm actual delta và bộ7SHA; prepare unsigned review mới bằng `prepare-dev-import-review.ps1`. Chủ dự án duyệt exactbatch/hash **sau** scan/preview; ký và preflight càng sát thời điểm Apply càng tốt. Nếu engine đổi giữa ký và thực thi, guard chặn; phải refresh/preview/review/sign mới, không tái sử dụng approval cũ. Không hứa rằng strict B sẽ cho phép definitions cũ hoặc tránh mọi vòng duyệt lại.

## Ký và thực thi sau phê duyệt riêng

Phần này **NOT RUN**, cần chủ dự án xác nhận batch/hash26af.../a6b... và phạm vi trước. Không tự điền ApprovedBy/reference như một approval giả. Dùng thông tin người duyệt thật:

```powershell
$approvedBy=Read-Host 'Nguoi duyet thuc te'
$reference=Read-Host 'Tham chieu xac nhan exact batch/hash'
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/sign-dev-import-approval.ps1 -Review $review -ReviewSha $reviewSha -Thumbprint $thumb -ApprovedBy $approvedBy -ApprovalReference $reference -ConfirmPlanSha $sha -ExpiresUtc ([DateTimeOffset]::UtcNow.AddHours(12)) -Actions Apply -ConfirmUserApproval
if($LASTEXITCODE){throw 'Signature failed; stop'}
$approval=Read-Host 'Duong dan approval ps1 vua duoc tool ky'
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-dev-import-signature.ps1 -Mode ApprovedDocument -File $approval -ExpectedThumbprint $thumb -Plan $plan -PlanSha $sha -Manifest $plan -ManifestSha $sha -Report ($out+'/signature.json')
if($LASTEXITCODE){throw 'Signed scope or trust invalid'}
```

Không execute approval `.ps1`. Đây là Authenticode metadata document bind canonicalplanSHA/target/SID/action/expiry, không detached JSON signature. CurrentUser trust/cert hợp lệ không tự cấp business approval; certificate này chỉ Dev.

Sau approval hợp lệ cần **backup Dev mới** (COPY_ONLY/CHECKSUM/VERIFYONLY + từng file readback + SQL HEADER), media/snapshots/keys; receipt<=12h. Đồng thời backup SQLTest chứa60receipt mới/job binding và127dependency trong allowlist, thêm actualsignedapproval/receipt vào privatebackup. Allowlist hiện PREPARED, chưa gọi là backup. BackupTest dùng trước test không chứa proof vừa tạo. Không grant SQL/Identity; NTFS backup service-folder theo quy trình đã được phép, nếu không ghi backup được thì dừng. VERIFYONLY không phải restore drill.

```powershell
# Chỉ sau owner approval/signature; chuẩn bị verified backup đúng target theo backup runbook.
$backupReceipt=Read-Host 'Receipt backup Dev moi da VERIFYONLY/hash/readback/header va dependency SQLTest da bao ve'
rtk proxy $script:DotnetPath $cli batch-preflight --report ($out+'/before-apply') @deps
if($LASTEXITCODE){throw 'Guard failed; no Apply'}
rtk proxy $script:DotnetPath $cli safe-apply --report ($out+'/apply') @deps --approval $approval --backup-receipt $backupReceipt
if($LASTEXITCODE){throw 'Stop and reconcile exact batch, do not retry blindly'}
rtk proxy $script:DotnetPath $cli safe-verify --report ($out+'/verify') @deps
if($LASTEXITCODE){throw 'Mismatch: stop further batches'}
```

Sau commit ack/verify/reconcile mới repeat sameplan để kiểm AlreadyCommitted/0delta content/revision/asset/alias và smoke vài trang/7ảnh theo ACL. So37bản cũ riêng với expected3/4/7/6; không biến fixture thành nguồn. Authorization audit có thể append khi repeat theo thiết kế.

Nếu ack không rõ, chỉ batch-preflight/reconcile readonly trước retry. Rollback cần exact signed actionRollback riêng; `safe-rollback` chỉ withdraw batch-owned unedited publication, giữ source/revisions/media/alias/audit. Không reset/xóa SQL hoặc restore toànDB để xử lý lỗi nhỏ. Không tự Apply Dev, đổi.env/grants/deploy/DNS/email trong mốc Stabilization.
