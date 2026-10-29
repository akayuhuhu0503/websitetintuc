# Checkpoint thực thi mới — approved Batch9 đã hoàn thành

Đọc [báo cáo approved Batch9](phase-3l-approved-batch9-report.md) và `.local/phase3l-approved-current.json` trước các bước lịch sử bên dưới. e02a8cb2/6e2374... đã commit/verify/repeat, **không nhập lại hoặc ký lại** để chuyển sang batch khác. Owner exactconfirmation và approvalApply đã có và được dùng đúngscope. Dev259/270/602/518,53NotScanned giữnguyên. Backup la0494e318/17oldgroups0diff/rollbackNOTNEEDED. Parser3075 đãTest1bài5ảnh; futureDev cần strict3Cpublicproof vàmanifest/exactconfirmationriêng. PDF48MiB chưađượcduyệt. SQLrootUNKNOWN/MONITORING. Các lệnh helper ghiCreateNew chỉ tái sử dụng bằng run/report mới, reconcile trước writes; không rerun vào evidence cũ. 27DevHTTP,8TestHTTP,4parser là các kiểm tra riêng;fullsuiteNOTRUN. Các bước "chưa ký/khôngký trong3L" phía dưới mô tả lượt trước, đã được exactuserapproval mới thay thế cho duy nhất batch9.

---

# Phase3L — SQL readiness, proof sát approval và resume

Đọc `.local/phase3l-current.json`, [báo cáo](phase-3l-report.md) và **`new9-final-ready.json`** trong run được trỏ tới. **Không ký/Apply trong3L; không chạy lại12packet3I.** Dùng report/runID mới cho mỗi thao tác, không chạy lại helper vào artifact cũ.

Sau sửa cuối: `recovery-scan --reuse-only` kiểm reuse đúng byte/proof/currentidentity/receipt, **không gọi scanner khi stale**; khi thiếu/stale sẽ FAIL để người vận hành quyết định refresh. Snapshot receipts chỉ dùng trong operation đang giữ exclusive job lease, tối đa180; không cache trust giữa các lượt.3Lreuse18PASS, stale140denied và Test/Devpreflight sau sửa cuốiPASS. Không coi `--reuse-only` hoặc SQL-readiness là approval/Apply permit.

## Quy trình tránh quét thừa

1. Kiểm schema/target và exactinventory/sourcecache; chạy SQL-readiness **khôngscanner** trênTest trước. `recovery-sql-check` chỉ kiểm target/inventorybinding/boundedreceiptquery, luôn trả `ScannerChecked=false`, `EligibleForApply=false`; không thay preflight, không cấp quyền public/Apply.
2. Nếu SQLtimeout: giữtemplate/fingerprint/session/transaction/stage/waits, reconcile trước hành động kế tiếp; không quét lại hàng loạt để chờSQL. Dừng/giới hạnreproduction theo bằngchứng mới; không tăngtimeout/NOLOCK/đổiindex hoặcAUTO_CLOSE theo suy đoán.
3. Sau SQLreadiness đạt, chỉ refreshSHA đangstale cần cho packet, tái dùng đúngjob/budget/SQLreceipts.3L chỉ18SHAjob9, không60legacy. Giữstrictengine+definitions+updatedtime+TTL+policy+bytehash+nativeprovider/receipt. Không khóadefinitions hoặc sửaproof cũ.
4. FreshTest/Devpreview/preflight; nếuproofset đổi,planproofSHA/BatchID/CreatedUtc thay đổi → trình đúngpacket mới. Khác biệtRows phải được giải thích;3L9Rows giống3K, chỉ3trườngtoplevel thay đổi. Không tái dùngapproval3I/3K.
5. BackupSQLDev/Test+media/keys/snapshot/config và exactsource/proof dependencies. Addendum phải chứaSQLdurablereceipts saurefresh và byteproof hiện hành. Backup3Lbase+after-proof9 đãđạt; approval chưaký nên phải bổsungactualsignedapproval/receipt trướcApplysau này.
6. Kiểmchứng chỉđọc CurrentUsercertificate/nativeAuthenticode/pin/EKU/chain/hạn; certificate hợp lệ không phải businessapproval hoặc bằngchứng privatekey có thể ký. Chỉ sauexactownerconfirmation mới được thao tác ký; nếuCNG/privatekey lỗi thìdừng, không export/grant/trust lại.
7. Ngay trước trình/ký/Apply: health/definitions/TTL/reviewSHA/manifestSHA/target/stamps/aliasconflicts/backup phùhợp. Nếudrift, dừng và tạo lạipacket/exactconfirmation khihash đổi. Trạng tháiREADY_FOR_APPROVAL không phảiAPPLIED và không đảm bảoproof cònvalid ngày hôm sau.

Ví dụdiagnostic **đọcSQLTest**, không refresh/Apply:

```powershell
# Dùng NEW_UNIQUE report, inventory/sha từ new9-ready.json thực.
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/diagnose-phase3l.ps1 `
 -ReportRoot .local/evidence/phase3f/<NEW_RUN>/sql-readiness `
 -Inventory <EXACT_INVENTORY_PATH> -InventorySha <EXACT_INVENTORY_SHA>
```

Observer dùngmastermetadata, không mởTesttrước đểwarm; nếuobserver mấtkết nối, giữerror và đểCLI chạy đến deadline riêng, không coi thiếuwaitsample là không cówait. SQLTestdiagnostics chỉ trongDevelopment/privatephase3e/f. VớiDevpreview/preflight, đặt `MigrationDiagnostics__RecoveryTestRoot=<PRIVATE_PHASE3F_ROOT>` trongProcess, để `MigrationDiagnostics__Root` rỗng choouterDev; innerRecoveryEvidenceTest vẫn chịu guard Development/Test. Không nới guard diagnostics sangDev/server khác.

## Chuẩn bị ownerapproval / Apply sau này — chưa thực hiện

Packet3L córeview `PENDING_USER_APPROVAL`, khôngapproval Authenticode. Chủdựán phải xác nhận exactBatchID+manifestSHA+9content/9revision/18asset/18alias+MinhAnhSQLEXPRESS/LandPortal_Dev. Sau đó dùng `scripts/sign-dev-import-approval.ps1` với Review/ReviewSha/Thumbprint/ConfirmPlanSha đúngpacket, **ApprovedBy/ApprovalReference thật**, ExpiresUtc phùhợp vàConfirmUserApproval; không dùngliteralplaceholder làm identity/phêduyệt. Chỉ cấpactionApply;Rollbackaction cầnthẩmquyền đúngphạmvi.

TrongphiênDevelopment đượcduyệt, pinProcess `SafeImport__ApprovedSignerThumbprints=94797BD79E275B09DEF613036BC00DF4EA91345F`, khôngpersist.env/User/Machine. Dùng reviewedCLI SHA từpointer. Future`safe-apply` phải có exact`--plan/--plan-sha`, `--checkpoint/--checkpoint-sha`, `--inventory/--inventory-sha`, `--proof-set/--proof-set-sha`, actual`--approval`, currentcomplete`--backup-receipt` vàreportunique; targetDevphảikhớpđãkiểm. Không gọi`batch-apply` thôngthườngđểné signer.

Saucommit:reconcile/readback→safe-verify→idempotency bằngreceipts/stamps, không blindreapply→hash/date/source/category/alias/ACL/oldDevpreservation→HTTPsmoke bài/ảnhPDF liênquan. NếuuncertainCOMMIT, reconciletrướcretry. `safe-rollback` chỉwithdrawnewpublications batch-owned/unedited theoapprovalRollback; giữsource/revisions/files/audit/aliases, khôngdatabase-reset hoặc xóa dữliệu ngoài batch. RestoretoànDevkhôngphải bước mặcđịnh cho lỗi nhỏ; phải reviewdữliệuphátsinhsau backup vàmedia/keys.

## Restore và UAT

Restore3L đã thựcchạy từbackupnewestDev vào `LandPortal_Restore3J_d9d16f42`, READ_ONLY. Reportnằm `.local/evidence/phase3j/phase3l-20261022T031058Z-5a15a959/restore`;3Jprefix là guard kỹthuật hiệncó, khôngphải backup235 cũ. Không chạy lại vàoDB/reportđãcó, khôngDROP hoặc restoređèDev/Test. Kiểm250/261/584/500 đượcderive từ backup/restoredrows vàmatchallcolumns, khôngsửaaudit235 đểépPASS. Giữcảrecovery235 cũ. TargethạtầngBộ/RPO-RTO chưa nghiệmthu.

[18canonical/PDFproposal](phase-3l-pdf-and-exceptions.md), [hosting/MFA/SMTP/UAT/NukeViet](phase-3l-infrastructure-handoff.md). Không tự tăngcapPDF hoặc mởresourcehost, khôngclaimtechnicalreadback thànhUATcánbộ. Fullregression/websitewideUI không nằm trongrunbookFASTDELIVERYnày.
