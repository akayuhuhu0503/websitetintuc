# Phase3H-Fix — packet và lệnh vận hành

> Cập nhật21/10: **b01 aa52120d / SHA41c7bf... đã APPLIED / VERIFIED LOCAL 20/20/60/40** sau sửa CNG public verifier. Dev60/120;remaining190/12packetchưaApply/chưađượcduyệt;52blockedgiữnguyên. Không chạy lại khối Apply cũ hoặc coi approvalb01coverpacketkhác. [Báo cáo mới](phase-3h-cng-apply-report.md), `.local/phase3h-cng-current.json`. Các trạng thái BLOCKED/unsigned/210READY bên dưới là lịch sử.


> 21/10 latest checkpoint: b01 đã được owner duyệt và ký ID33b5a7ff-1ae6-4c1e-b8d3-f143469d5de1 (hạn21/10/2026 13:16:09UTC); không ký lại theo khối chuẩn bị bên dưới. **BLOCKED PRE_APPLY** vì CNG/GetRSAPrivateKey trong certificate facts guard. Backupha88e46496PASS,0commit. Xem [báo cáo exact Apply](phase-3h-approved-apply-report.md) và `.local/phase3h-apply-current.json`. Chưa chạy safe-apply,repeat,HTTP; không chạy helper Apply lại để thử may rủi. Xử lý có kiểm chứng lỗiCNG, sau đó revalidateexactapproval/expiry/hash/proof/backup/currentDev/freshpreflight; scope chỉ20/20/60/40.


[Báo cáo hiện hành](phase-3h-fix-report.md). Pointer `.local/phase3h-fix-current.json`; run `phase3h-fix-20261021T050440Z-e5776be2`. **Không chạy lại các helper vào report đã tồn tại. Không ký/Apply trước xác nhận exact packet của chủ dự án.**

SQL root cause vẫn UNKNOWN/MONITORING. Một lần tái hiện có observer không lỗi; preflight không observer, replay Test và hai preview/preflight Dev cũng đạt. Không sửa query, transaction, index, pooling, AUTO_CLOSE hoặc cấu hình SQL để tạo kết quả PASS. Các dấu hiệu memory/AUTO_CLOSE chỉ là giả thuyết cần theo dõi khi lỗi thực sự tái diễn.

## Packet hiện hành

B01 mới thay packet3H cũ:

- BatchId `aa52120d-a6b9-41b9-b4b5-22ced4330113`.
- Manifest SHA256 `41c7bf061b472ec0d8aa5129eaa9384bb27e1966baa977fc74c82e78f63d6dfa`.
- Đích `MinhAnh\SQLEXPRESS/LandPortal_Dev`, delta20content/20SourceRecords/20revision/60asset/40alias.
- Review `.local/signing/reviews/phase3h-fix-20261021T050440Z-e5776be2/b01.json`, SHA `2f6d732ee6af59b9028728ecf9812eabe79026acbab28d56fcc8e9e854a2f793`; chưa có approvedBy/signature.
- B13 mới `becfc9bf-4f9e-420b-906b-3adcb613d908`, SHA `84461b83958f22f9da3e07093d6cd2f56f856b85fbfdda5a1348d4517bec49ae`, delta15/15/15/30.

`packets.json` trong run là danh sách13 packet mới nhất: b01/b13 mới, b02–b12 giữ file/ID/SHA cũ. Không dùng old b01 `8167414c.../0e6f6a...` thay cho packet trình lần này. Không dùng approval3G. Tất cả READY_FOR_APPROVAL; **EXECUTION vẫn bị chặn bởi thiếu exact owner approval/signature và fresh Dev backup/guards**.

## Kiểm tra đọc trước khi ký

Các lệnh sau chỉ đọc. Khởi chạy PowerShell tại workspace; context nạp cấu hình vào process, không sửa `.env`, không grant quyền. Mỗi `$out` phải mới:

```powershell
. ./.local/phase3h-fix-context.ps1
$packets=Get-Content ($r+'/packets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$p=$packets.batches | Where-Object label -EQ 'b01'
Set-FProfile 'LandPortal_Dev'
$deps=(Packet-Deps $p)+@('--plan',$p.plan,'--plan-sha',$p.manifestSha256)
$out='.local/evidence/phase3f/approved-hfix-'+[Guid]::NewGuid().ToString('N')
rtk proxy $script:DotnetPath $cli batch-preflight --report ($out+'/preflight') @deps
if($LASTEXITCODE){throw 'STOP: guard/SQL failure; reconcile before retry'}
```

Fresh preflight phải kiểm đúng instance/database/media root/schema/stamp/receipt và complete native proof. Strict policy vẫn TTL24h/current engine+definitions/definitions age72h; earliest proof expiry22/10/2026 11:05:12VN. Definition drift làm mất hiệu lực ngay. Nếu proof/plan/stamp thay đổi: tạo packet mới/report mới và xin xác nhận SHA mới; không sửa dependency dưới approval cũ. Không scan/promote originalDev53.

## Ký approval — chỉ sau xác nhận thật

**Khối lệnh này chỉ được chuẩn bị, chưa chạy.** Người vận hành phải có xác nhận riêng đúng BatchId/SHA/delta/đích ở trên. Read-Host không thay thế xác nhận đó:

```powershell
$approvedBy=Read-Host 'Nguoi phe duyet THUC TE da xac nhan exact batch va SHA'
$reference=Read-Host 'Tham chieu xac nhan exact batch/SHA cua chu du an'
$expiry=[DateTimeOffset]::UtcNow.AddHours(6)
rtk proxy powershell -NoProfile -File scripts/sign-dev-import-approval.ps1 `
 -Review $p.review -ReviewSha $p.reviewSha `
 -Thumbprint 94797BD79E275B09DEF613036BC00DF4EA91345F `
 -ApprovedBy $approvedBy -ApprovalReference $reference `
 -ConfirmPlanSha $p.manifestSha256 -ExpiresUtc ($expiry.ToUniversalTime().ToString('O')) -Actions Apply -ConfirmUserApproval
```

Certificate/trust CurrentUser đã có, không Create/Trust lại. Approval actionApply không cấp actionRollback; rollback cần scope/action được chủ dự án duyệt riêng. Không execute approval.ps1; đó là tài liệu ký để CLI đọc. Hạn approval không thay TTL của proof.

## Backup — chưa chạy cho Dev mở rộng

Sau ký hợp lệ, tạo backup mới cho **Dev40/60 hiện tại** và SQLTest receipt hiện hành. Backup Dev baseline37 của3G không đủ. Dùng helper long-path đã PASS, private root đã có ACL SQL service/user phù hợp; không cấp ACL mới hoặc thay guard:

```powershell
$approval=Read-Host 'Duong dan approval PS1 DA KY dung packet'
$dep=$r+'/b01-backup-dependencies.json'
$depSha=Get-SigningSha $dep
$id='hfix'+[Guid]::NewGuid().ToString('N').Substring(0,6)
& ./scripts/backup-approved-batch.ps1 `
 -BackupRoot '.local/backups/LandPortal_Test-20261021T015310Z' `
 -DirectoryId $id -EvidenceDirectory ($out+'/backup') `
 -DependencyManifest $dep -DependencyManifestSha $depSha `
 -AdditionalFiles @($approval,($approval+'.receipt.json'))
if($LASTEXITCODE){throw 'STOP: incomplete backup; no Apply'}
$backupReceipt=$out+'/backup/backup-LandPortal_Dev.json'
```

209 explicit b01 dependency files ở manifest này là **PREPARED_NOT_BACKED_UP**: raw/cache/quarantine/proof/checkpoint/newplan/review. Helper còn snapshot toàn bộ media/snapshot/key Dev và SQLDev/Test. SQL COPY_ONLY/CHECKSUM/VERIFYONLY/header/SHA, file count/size/hash/source readback và receipt≤12h phải đạt. Xác minh coverage của signed approval/receipt đã thêm. Không gọi VERIFYONLY là restore drill. Không ghi đè backup cũ.

## Apply/verify/idempotency — chưa chạy Dev

Khối sau cũng **chưa chạy**, chỉ dùng khi owner đã duyệt và signed approval, fresh backup/proof/target/preflight đều đạt:

```powershell
Set-FProfile 'LandPortal_Dev'
$env:SafeImport__ApprovedSignerThumbprints='94797BD79E275B09DEF613036BC00DF4EA91345F'
rtk proxy powershell -NoProfile -File scripts/test-dev-import-signature.ps1 `
 -Mode ApprovedDocument -File $approval -ExpectedThumbprint $env:SafeImport__ApprovedSignerThumbprints `
 -Plan $p.plan -PlanSha $p.manifestSha256 -Manifest $p.plan -ManifestSha $p.manifestSha256 `
 -Report ($out+'/approval-preflight.json')
if($LASTEXITCODE){throw 'STOP: signature/scope failure'}
rtk proxy $script:DotnetPath $cli batch-preflight --report ($out+'/before-apply') @deps
if($LASTEXITCODE){throw 'STOP: preflight failed'}
rtk proxy $script:DotnetPath $cli safe-apply --report ($out+'/apply') @deps --approval $approval --backup-receipt $backupReceipt
if($LASTEXITCODE){throw 'STOP: reconcile before retry; do not assume rollback'}
rtk proxy $script:DotnetPath $cli batch-reconcile --report ($out+'/reconcile') @deps
if($LASTEXITCODE){throw 'STOP: batch needs reconciliation'}
rtk proxy $script:DotnetPath $cli batch-verify --report ($out+'/verify-readback') @deps
if($LASTEXITCODE){throw 'STOP: mismatch; no later batch'}
rtk proxy $script:DotnetPath $cli safe-apply --report ($out+'/repeat') @deps --approval $approval --backup-receipt $backupReceipt
if($LASTEXITCODE){throw 'STOP: idempotency uncertain'}
rtk proxy $script:DotnetPath $cli safe-verify --report ($out+'/verify-repeat') @deps
```

Process signer pin này đã được owner cho phép cho Dev trước đó; không ghi `.env`/persistent environment hoặc dùng cho Production. Theo dõi exact20/20/60/40, source/date/author/categories/hash/alias/publication,17 nhóm before/after expected delta, old40/53original và Identity/env; smoke đúng bài/ảnh mới. Không chạy full regression/UI website.

Nếu cần rollback sau lỗi, reconcile trước và lấy **approval actionRollback** đúng packet. Công cụ giữ nguyên:

```powershell
# Only after explicit actionRollback approval; never reuse Apply-only as Rollback authority.
rtk proxy $script:DotnetPath $cli safe-rollback --report ($out+'/rollback') @deps --approval $rollbackApproval --backup-receipt $backupReceipt
```

Rollback chỉ withdraw publication batch mới chưa editor sửa, giữ source/revision/asset/alias/audit/file; không xóa/reset/restore toàn database. Sau gỡ kiểm public/private grant. Không gọi repeat cùng batch để tái xuất bản sau rollback.

## Chẩn đoán khi có lỗi mới

`scripts/diagnose-phase3h-fix.ps1 -ReportRoot <NEW_PHASE3F_DIR>` chạy **một** Test preflight của b01 với observer chỉ session của CLI; không Apply. `scripts/inspect-phase3h-preflight.ps1` tạo2 actual plan của equivalent audit predicates, operator metadata connection riêng; không đồng nhất fingerprint hoặc tốc độ với EF runtime. `scripts/test-phase3h-fix.ps1` parse/negative scope guards,0SQL. Report cũ không được tái sử dụng.

Không loop để ép tái hiện, không tăng timeout/NOLOCK/KILL session khác, không sửa global memory/AUTO_CLOSE theo giả thuyết. Connection/session khác nhau, stage/batch/operation/fingerprint/elapsed/row count phải ghi đúng; thiếu field thì ghi unavailable. Observer giữ DB mở có thể ảnh hưởng cold/AUTO_CLOSE, nên không coi một sample warm là chứng minh hết lỗi. Historical incidents, SQL restore/UAT/Production vẫn riêng; **CHƯA PRODUCTION READY**.
