# Phase3K — resume có kiểm soát

Checkpoint `.local/phase3k-current.json`; [báo cáo](phase-3k-report.md). **PARTIAL / BLOCKED_SQL_PREFLIGHT**; không chạy lại12packet3I. Không chạy lại helper init/backup/scan/stage vào artifact cũ. Chỉ dùng report path mới; đọc plan/proof/receipts thực trước mỗi bước.

**Final override:** Defender đã143,proof78tài nguyên quét140 hiện **STALE**. Đọc `final-safety-gates.json`, không dùng `independent-proof-readback.json`/Testpreflight trước đó làm freshness hiện hành. Packet9 BLOCKED cảSQL lẫnproof. Không chạy ví dụpreflight phía dưới trước refresh đúngbyte/proof/plan và điều traSQL; chưa cóexactownerapproval chohashmới.

## Điểm tiếp tục

1. Đọc `stage-trace/`, `stage-resume-trace/`, `new9-dev-preflight.log` và reconcile trong run. Đã dừng sau1Devtimeout +2Testtimeout. Không retry tiếp trước điều tra có thay đổi/bằng chứng rõ; không tăng command timeout, sửa quyền hoặc tắt guard. Exact query fingerprint có trong report; role activation đã PASS nhưng nguyên nhân query timeout chưa rõ. Nếu cần reproduction, chỉ trên Test đã backup, có instrumentation và phạm vi hữu hạn.
2. Recovery job60 đã120receipts,2/resource; không tạo job mới/resetlimit để né budget. Native proof job9/60 còn TTL24h nhưng definitions thay đổi làm invalid sớm. Kiểm currentDefender/inventorySHA/proofSHA/quarantinebyte/durable receipt. Refresh chỉ các bản sao Test được phép, report mới, append audit. Không scan/promote53originalDev.
3. Test đã stage Categories59/60; không INSERT lại. So nhãn/ID/URL/rawSHA và relations. `gap34-work.json` có projected checkpoint34 +proof/optional-review hashes. Sau khi gate thực đạt, dùng importer hiện có tạo preview rồi comparison revisions; không cập nhật PublishedRevisionId/bản biên tập bằng SQL. Article3677 Test cótitle/body khác cache phải giữ review. Đối chiếu metadata/categories/assets và revision trước/sau; verify/repeat theo receipt, không overwrite để ép khớp.
4. Packet9 độc lập đã có Devpreview, nhưng Devpreflight chưa đạt. `new9-packet.json` chỉBLOCKED. Sau điều tra, preflight mới dùng exact plan/dependencies và output mới. Nếu proof/plan/hash thay đổi, tạo packet mới, không giữ ID/hash cũ để xin duyệt. Sau mọi guard đạt mới trình **exact batch+hash+9/9/18/18+target** cho chủ dự án. Chưa được ký/Apply chỉ nhờ có unsigned review hoặc approval3I.
5. Chỉ khi có exact owner approval: signer/Authenticode/chain/pin/EKU/date/SID/scope; fresh complete backupDev +SQLTest durable receipts/scoped bytes/approval; targetMinhAnhSQLEXPRESS/LandPortal_Dev +schema; strictproof/currentengine; freshpreflight; Apply đúng packet; reconcile/readback/verify/idempotency; smoke affectedarticles/category/search/assets/ACL. Dừng khi mismatch/uncertain/timeout, reconcile trước retry. Rollback đúng batch, không restore đè toànDev vì lỗi nhỏ.

Ví dụ preflight **sau điều tra**, không phải lệnh bỏ guard hoặc tự retry ngay:

```powershell
rtk proxy powershell -NoProfile
# Trong phiên chỉ lưu cấu hình Process, không sửa .env:
. ./.local/phase3h-fix-context.ps1
$s = Get-Content .local/phase3k-current.json -Raw -Encoding UTF8 | ConvertFrom-Json
$r = $s.root
$w = Get-Content ($r + '/new9-work.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$p = Get-Content ($r + '/new9-packet.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Set-FProfile LandPortal_Dev
# NEW_UNIQUE nằm trong .local/evidence/phase3f, không thay bằng report cũ.
Invoke-F 'NEW_UNIQUE-preflight' @('batch-preflight',
 '--plan', $p.plan, '--plan-sha', $p.manifestSha256,
 '--checkpoint', $w.checkpoint, '--checkpoint-sha', $w.checkpointSha,
 '--inventory', ($w.scanRoot + '/inventory/inventory.json'),
 '--inventory-sha', (Get-SigningSha ($w.scanRoot + '/inventory/inventory.json')),
 '--proof-set', ($w.scanRoot + '/scan/proof-set.json'),
 '--proof-set-sha', (Get-SigningSha ($w.scanRoot + '/scan/proof-set.json')),
 '--report', ($r + '/NEW_UNIQUE-preflight'))
```

Sau reviewgate, dùng cơ chế signing/safe-apply/rollback hiện hành tại [runbook3I](phase-3i-runbook.md); không dùng receipt3I làm approval packet9. Helper3K là operator harness nội bộ, không phải web endpoint hoặc production service. Website vẫn chạy bằng các lệnh trong checkpoint3J; không start mới hoặc thay URLpreview trong3K vì không thay public app.

## Drill restore tiếp theo — NOT RUN

Nguồn hiện hành: `backup/backup-complete.json` trong run3K, Dev250. `before-stage` bổ sung Test proof receipts trước2catalogrows; không tuyên bố là bảnbackupTestsaucatalog. Đọc hash/path thật, SQLheader/CHECKSUM/FILELISTONLY/diskspace; không viết đè backup. Chọn database mới chưa tồn tại theo guard hiện có `LandPortal_Restore3J_<8hex>` và report path mới `.local/evidence/phase3j/<NEW_RESTORE_RUN>/restore`. Prefix3J là guard kỹ thuật hiện tại, không phải yêu cầu ghi đè kết quả3J cũ.

```powershell
# Chỉ chạy khi đã review input/hash và có cửa sổ tài nguyên; chưa chạy trong3K.
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/restore-project-rehearsal.ps1 `
 -BackupReceipt <PHASE3K_COMPLETE_BACKUP_RECEIPT> `
 -BackupReceiptSha256 <ACTUAL_RECEIPT_SHA256> `
 -RestoreDatabase LandPortal_Restore3J_<NEW_8HEX> `
 -ReportDirectory .local/evidence/phase3j/<NEW_RESTORE_RUN>/restore
```

Không dùng audit hardcoded235 của3J để nghiệm thu250. Review baseline riêng từ backup mới, đối chiếu schema250contents261revisions584media500aliases/relations/public pointers/scanstates/Identity/permissions/allbytehashes, DBCCCHECKDB và đọc bài/PDF thật. Files restore ngoài media đang chạy, giữ recoveryDBREAD_ONLY, không worker/mail; ghi RPO/RTO. Không DROP `LandPortal_Restore3J_3f7ec1d0`; không WITHREPLACE/restore DevTest. Target staging/Production restore vẫn cần môi trường/authority/ATTT riêng.

## UAT và vận hành

[16cases và input hạ tầng](phase-3k-uat-plan.md), [scannerProduction](scanner-production-readiness.md), [cutover](gdla-cutover-readiness.md), [NukeViet/export/mapping](phase-3k-nukeviet-export-handoff.md). Tất cả UAT thực/hostingtarget/MFA/scannerSMTPProduction/HTTPS301target chưa nghiệm thu. Không càiIIS/service, gửi email, publicdata, đổiDNS hoặc dùng DefenderTest cho Production. Quản trị viên cung cấp secret qua secret store, không `.env`/repo/log.
