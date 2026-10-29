# Code Signing cho Development local

**Cập nhật FinalApproval20/10:** owner đã cho phép application signer pin **process-only Development** cho cert hiện có; actual `PortalConfiguration.Load` đã xác minh đọc đúng pin. Không sửa.env/persistUserMachine/globaltrust. [Packet exact batch mới](phase-3g-final-approval-report.md), [lệnh hiện hành](phase-3g-final-approval-runbook.md). READY_FOR_APPROVAL/chưa ký realapproval/chưaApply; phải chờ exact batch+SHA confirmation. Những đoạn “chưa cấu hình policy” dưới đây là checkpoint setup lịch sử, không yêu cầu tạo/thêm trust cert lần nữa.

**Đã hoàn tất trust CurrentUser sau khi người dùng chọn Yes:** cùng certificate thumbprint94797BD79E275B09DEF613036BC00DF4EA91345F, chain/AuthenticodeValid và16/16fixture checksPASS. Không cần chạy lại Create/Trust. Xem [báo cáo cập nhật](dev-code-signing-report.md), run`dev-signing-confirmed-20261020T030517Z-a0e9ad77`. Các mô tả Security Warning bên dưới giải thích quy trình/lịch sử, không còn là blocker hiện tại. Approval batch thật/runtime signer policy vẫn chưa được tạo/cấu hình; không Apply Dev.

Máy MinhAnh, người dùng đã xác nhận Administrator và lịch sử đổi tên THANGPD → MinhAnh ngày20/10/2026. Đây là cùng SQL instance đã kiểm chứng. Không đổi `@@SERVERNAME`, máy, SQL guard, `.env` hoặc quyền ứng dụng. Hướng dẫn này chỉ áp dụng Development; không phải cấu hình signer Production.

## Loại chứng chỉ và cơ chế

Ứng dụng hiện yêu cầu **Authenticode cho approval `.ps1`**, không thực thi tệp đó. Metadata ký chứa SHA256 của `SourceBatchPlan` JSON; không có detached signature JSON riêng. Có thể bind thêm SHA của manifest đối chiếu. PlanJSON chính là manifest máy đọc dùng để nhập, nên cũng có thể dùng cùng file cho hai tham số Plan/Manifest.

Code `ApprovedBatchExecution.CreateAsync` yêu cầu `Get-AuthenticodeSignature` trả `Valid`, signer thumbprint khớp `SafeImport__ApprovedSignerThumbprints`, exact schema3/target/media-root/operatorSID/action/expiry và verified backup. Self-signed được dùng trên máy Dev **khi** Windows tin cậy nó và người vận hành đã chọn policy; không bỏ bước xác minh nào. Certificate hợp lệ không tự phê duyệt một batch. [Microsoft mô tả Code Signing/self-signed](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_signing), [New-SelfSignedCertificate](https://learn.microsoft.com/en-us/powershell/module/pki/new-selfsignedcertificate).

## Công cụ mới

|Script|Phạm vi|
|---|---|
|`scripts/dev-code-signing.ps1`|Create/TrustCurrentUser/Inspect; chỉ MinhAnh Development, không LocalMachine/TrustedPublisher/execution policy|
|`scripts/dev-signing-common.ps1`|Guard môi trường, path/reparse, file CreateNew, private project ACL, certificate facts|
|`scripts/prepare-dev-import-review.ps1`|Draft PENDING_USER_APPROVAL có batch/target/plan+manifestSHA/operator; không tạo approval ký|
|`scripts/sign-dev-import-approval.ps1`|Chỉ dùng khi người thật cung cấp ApprovedBy/ApprovalReference/ConfirmPlanSha/ConfirmUserApproval; không gọi ở lượt này|
|`scripts/test-dev-import-signature.ps1`|Read-only kiểm chain/EKU/hạn/keypolicy/Authenticode/binding; phân biệt Fixture và ApprovedDocument; không xác minh thay quyết định nghiệp vụ ngoài hệ thống|
|`scripts/test-dev-code-signing.ps1`|Fixture riêng Test-only, ký thật, negative tests; không SQL mutation hoặc Apply|

## Certificate đã tạo

Thumbprint công khai: **`94797BD79E275B09DEF613036BC00DF4EA91345F`**.

RSA3072/SHA256, Code Signing EKU, DigitalSignature, khóa CNG **NonExportable** (ExportPolicy=None), CurrentUser/My. Hạn30ngày, đến `2026-11-19T02:16:50Z` (09:16:50 giờ Việt Nam). Không export private key/PFX; `.cer` public và receipt nằm trong ignored `.local/signing/certificates/`. Windows tự quản lý private key trong KSP user profile, không trong repository.

Root trust chỉ thêm certificate public này vào **CurrentUser/Root**, không LocalMachine hoặc TrustedPublisher. Windows có thể hiển thị **Security Warning** yêu cầu người dùng xác nhận; không tự bấm hoặc dùng công cụ khác để né cảnh báo. Trước trust, fixture đã ký trả `UnknownError`, chưa hợp lệ. Trạng thái cuối xem report/evidence của lượt, không suy từ việc key đã được tạo.

Không cần tạo certificate thứ hai. Nếu làm lại trên profile Dev khác, phải được chủ profile cho phép, và dùng report mới:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-code-signing.ps1 -Action Create -Environment Development -Days 30 -Report .local/evidence/phase3f/NEW_UNIQUE_RUN/certificate-created.json
# Máy/profile hiện tại đã tạo; chỉ Inspect hoặc Trust đúng thumbprint:
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-code-signing.ps1 -Action Inspect -Environment Development -Thumbprint 94797BD79E275B09DEF613036BC00DF4EA91345F -Report .local/evidence/phase3f/NEW_UNIQUE_RUN/certificate-inspect.json
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-code-signing.ps1 -Action TrustCurrentUser -Environment Development -Thumbprint 94797BD79E275B09DEF613036BC00DF4EA91345F -Report .local/evidence/phase3f/NEW_UNIQUE_RUN/certificate-trust.json
```

Chỉ chạy một yêu cầu Trust tại một thời điểm; nếu Security Warning đang mở, xử lý hộp thoại đó, không mở thêm. `-ExecutionPolicy Bypass` ở lệnh khởi chạy chỉ tác động tiến trình con, không đổi policy toàn máy. Không có `Set-ExecutionPolicy` trong các script mới. Certificate CurrentUser trust áp dụng xác minh chữ ký của certificate này trong user profile; giới hạn project/Development được guard công cụ và policy runtime thực thi, không gọi đây là signer Production.

## Review và ký batch thật: người vận hành thực hiện sau phê duyệt

Draft đã tạo cho batch ưu tiên cũ tại `.local/signing/reviews/dev-signing-20261020T021606Z-c89a0a6f/priority-review.json`, **PENDING_USER_APPROVAL**, ApprovedBy/reference rỗng, không có signed approval. Phải fresh preview/proof/backup trước dùng batch thật; draft cũ không chứng minh freshness.

Tạo review từ plan mới bằng SHA exact; có thể dùng plan làm manifest:

```powershell
$plan = 'DUONG_DAN_PLAN_DEV_MOI.json'
$sha = (Get-FileHash -LiteralPath $plan -Algorithm SHA256).Hash.ToLowerInvariant()
$review = '.local/signing/reviews/NEW_UNIQUE_RUN/review.json'
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare-dev-import-review.ps1 -Plan $plan -PlanSha $sha -Manifest $plan -ManifestSha $sha -Output $review
```

Sau khi chủ dự án kiểm đúng danh sách/delta/hash và cung cấp phê duyệt rõ ràng, **người vận hành** mới chạy lệnh bên dưới, thay họ tên/reference thật. Không chạy nguyên placeholder và không gọi việc chuẩn bị lệnh là phê duyệt:

```powershell
$reviewSha = (Get-FileHash -LiteralPath $review -Algorithm SHA256).Hash.ToLowerInvariant()
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/sign-dev-import-approval.ps1 -Review $review -ReviewSha $reviewSha -Thumbprint 94797BD79E275B09DEF613036BC00DF4EA91345F -ApprovedBy 'NGUOI_DUYET_THAT' -ApprovalReference 'THAM_CHIEU_QUYET_DINH_THAT' -ConfirmPlanSha $sha -ExpiresUtc ([DateTimeOffset]::UtcNow.AddHours(24)) -Actions Apply -ConfirmUserApproval
```

Script chỉ ký attestation do caller khẳng định; không tự xác thực danh tính/thẩm quyền ngoài hệ thống. Approval có thêm ManifestSha256, BatchId, ApprovedBy/reference; code hiện hành giữ các gate gốc. Tệp approval luôn có dòng comment đầu, metadata ở dòng tiếp theo để BOM không làm sai tiền tố C#; không execute `.ps1` này. Không tự đặt application trusted thumbprint trong lượt setup.

## Lệnh dry-run, backup, Apply và verify theo công cụ hiện hành

**Lệnh chuẩn bị, chưa thực thi.** Proof phải còn TTL24h và đúng engine/definitions hiện tại; không dùng mù proofset19/10. Batch mở rộng còn521asset ngoài lần refresh60 và alias review, không gắn READY cho toàn bộ. Ưu tiên cohort40 độc lập3bài. Không cộng hai plan có giao nhau.

Trong PowerShell mới ở root workspace, khai báo dependency thật và report mới:

```powershell
. ./scripts/common.ps1
$label = 'dev-apply-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$report = '.local/evidence/phase3f/' + $label
$checkpointSha = 'd41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3'
$checkpoint = '.local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/' + $checkpointSha + '.bin'
$inventory = '.local/evidence/phase3c/phase3c-20261019T033700Z-136775a5/inventory/inventory.json'
$inventorySha = '5a5ae635bddb6a3de9c7b3fbea2a75441ad3bc375684cd36a47d755bb0a44b0e'
$proofSet = Read-Host 'Duong dan proof-set Test native moi con hieu luc'
$proofSha = (Get-FileHash -LiteralPath $proofSet -Algorithm SHA256).Hash.ToLowerInvariant()
$optional = '.local/evidence/phase3f/phase3f-20261019T070725Z-fa27c9d0/optional-images-existing-review.json'
$optionalSha = '10df0a7801cb30216593c6ea5bf8f537140e992fab3743faf037c938886a13ab'
# Dry-run Dev; no writes; fail-closed if proof/stamps invalid:
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3f.ps1 -Action Preview -Database LandPortal_Dev -NoBuild -ReportRoot ($report+'/preview') -Checkpoint $checkpoint -CheckpointSha $checkpointSha -Inventory $inventory -InventorySha $inventorySha -ProofSet $proofSet -ProofSetSha $proofSha -OptionalImages $optional -OptionalImagesSha $optionalSha
if($LASTEXITCODE){throw 'Preview failed; no Apply'}
$plan = $report+'/preview/plan.json'
$planSha = (Get-FileHash -LiteralPath $plan -Algorithm SHA256).Hash.ToLowerInvariant()
# Explicit process-local configuration only; no .env or grants:
$dev = Get-ProjectConnectionBuilder 'LandPortal_Dev'
$env:ConnectionStrings__Portal = $dev.ConnectionString
$env:Sql__ExpectedDatabase = 'LandPortal_Dev'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Mail__Transport = 'Disabled'
$env:Media__AllowUnscannedDevelopmentUploads = 'false'
$cli = '.local/build/phase3f/current/bin/MigrationCli/debug/MigrationCli.dll'
$deps = @('--checkpoint',$checkpoint,'--checkpoint-sha',$checkpointSha,'--inventory',$inventory,'--inventory-sha',$inventorySha,'--proof-set',$proofSet,'--proof-set-sha',$proofSha,'--plan',$plan,'--plan-sha',$planSha)
# Reconcile before any retry:
rtk proxy $script:DotnetPath $cli batch-preflight --report ($report+'/preflight') @deps
if($LASTEXITCODE){throw 'Preflight failed'}
# New SQL/media/snapshot/keys backup; no global folder grant:
$backupDir = '.local/evidence/phase3c/' + $label + '-backup'
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/backup-project.ps1 -Database LandPortal_Dev -EvidenceDirectory $backupDir
if($LASTEXITCODE){throw 'Backup failed; no Apply'}
$backupReceipt = $backupDir+'/backup-LandPortal_Dev.json'
$receipt = Get-Content -LiteralPath $backupReceipt -Raw | ConvertFrom-Json
$backupManifest = Get-Content -LiteralPath (Join-Path $receipt.path 'manifest.json') -Raw | ConvertFrom-Json
foreach($file in $backupManifest.files){
    $copy = [IO.Path]::GetFullPath((Join-Path $receipt.path $file.relativePath))
    if(-not $copy.StartsWith($receipt.path.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe backup path'}
    if((Get-FileHash -LiteralPath $copy).Hash.ToLowerInvariant() -cne $file.sha256){throw 'Backup readback mismatch'}
}
# Also protect updated SQLTest proof receipts and private job proof dependencies per runbook.
# STOP: operator reviews exact new plan and signs it using the steps above.
# Only AFTER owner approval of this certificate/policy and exact batch:
$approval = Read-Host 'Duong dan approval ps1 DA DUOC NGUOI CO THAM QUYEN KY'
$env:SafeImport__ApprovedSignerThumbprints = '94797BD79E275B09DEF613036BC00DF4EA91345F'
# This line configures future operator policy; it was NOT executed in setup.
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-dev-import-signature.ps1 -Mode ApprovedDocument -File $approval -ExpectedThumbprint $env:SafeImport__ApprovedSignerThumbprints -Plan $plan -PlanSha $planSha -Manifest $plan -ManifestSha $planSha -Report ($report+'/signature-preflight.json')
if($LASTEXITCODE){throw 'Signature preflight failed'}
rtk proxy $script:DotnetPath $cli safe-apply --report ($report+'/apply') @deps --approval $approval --backup-receipt $backupReceipt
if($LASTEXITCODE){throw 'Apply uncertain or failed: reconcile before retry, do not continue'}
rtk proxy $script:DotnetPath $cli safe-verify --report ($report+'/verify') @deps
if($LASTEXITCODE){throw 'Verify failed: stop further batches'}
```

`safe-apply` dùng Windows operator hiện có; không suy quyền SQL từ Administrator và không tự grant. Backup<=12h, approval<=7ngày và exact action bắt buộc. Sau verify cần idempotency theo receipt, smoke đúng bài/media và đối chiếu oldDev/Identity/env. Không chạy các lệnh Apply này trong lượt setup; không signed real approval được tạo. Không sửa policy Production/Windows toàn máy hoặc dùng test fixture làm authority.
