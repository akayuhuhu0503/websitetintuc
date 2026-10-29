# Phase3H — exact batch approval và Apply bị chặn, 21/10/2026

> Cập nhật21/10: **b01 aa52120d / SHA41c7bf... đã APPLIED / VERIFIED LOCAL 20/20/60/40** sau sửa CNG public verifier. Dev60/120;remaining190/12packetchưaApply/chưađượcduyệt;52blockedgiữnguyên. Không chạy lại khối Apply cũ hoặc coi approvalb01coverpacketkhác. [Báo cáo mới](phase-3h-cng-apply-report.md), `.local/phase3h-cng-current.json`. Các trạng thái BLOCKED/unsigned/210READY bên dưới là lịch sử.


**BLOCKED BEFORE APPLY; chưa APPLIED. CHƯA PRODUCTION READY.** Run `phase3h-apply-20261021T071528Z-4099b799`, pointer `.local/phase3h-apply-current.json`; bằng chứng `.local/evidence/phase3f/phase3h-apply-20261021T071528Z-4099b799`. Xác nhận mới nhất của chủ dự án chỉ cho phép batch dưới đây, không áp dụng cho210 bài hoặc packet khác.

|Hạng mục|Kết quả thực|
|---|---|
|Exact packet|`aa52120d-a6b9-41b9-b4b5-22ced4330113`, SHA256 `41c7bf061b472ec0d8aa5129eaa9384bb27e1966baa977fc74c82e78f63d6dfa`; không thay đổi|
|Scope|Tối đa20content/20SourceRecords/20revision/60asset/40alias;20 rows CreateNeedsApproval,40alias,60SHA|
|Target|Read-only xác nhận SERVERPROPERTY MinhAnh\SQLEXPRESS / LandPortal_Dev; registered @@SERVERNAME THANGPD\SQLEXPRESS là tên cũ đã được chủ máy xác nhận; migration20261015091551_V05EditorialDocuments|
|Strict scanner proof|Initial fresh preflight PASS;60 resource proof bound đúng byte/SQL receipt, engine1.1.26090.9/defs1.461.118.0 hiện hành. Không scan mới, không sửa proof hoặc53 tài nguyên gốc. Earliestexpiry22/10/2026 11:05:12VN; driftdefinitions invalid ngay|
|Approval|Đã ký thật actionApply, ID `33b5a7ff-1ae6-4c1e-b8d3-f143469d5de1`; SHA `24f8352fe2582172546b99be85120b7df0f08f85eee8cf8a4f825b94b5cc2eb1`; hạn `2026-10-21T13:16:09.9715865+00:00` (20:16:09VN21/10). CurrentUser signer94797BD79E275B09DEF613036BC00DF4EA91345F, nativeAuthenticodeValid/chain/EKU/hạn hợp lệ|
|Initial preflight|PASS ABSENT_CONFIRMED20,0commit,0errors; chưa ghi dữ liệu|
|Backup hoàn chỉnh|PASS137Devfiles/48.806.964byte +2SQL +215dependencies/62.189.622byte;0missing/0hashmismatch; file restore-checkPASS; SQLrestore drillNOTRUN|
|Final signature guard|**BLOCKED** tại `Get-DevSigningCertificateFacts` → `RSACertificateExtensions.GetRSAPrivateKey`: CNG ephemeral key handle/open option error. Dừng trước final preflight và trước gọi safe-apply; không retry Apply/nới guard|
|Stop reconcile|ABSENT_CONFIRMED20/0commit/20missing/0errors; missing ở đây là20 chưa nhập, không phải mất dữ liệu|
|Thực nhập|**0content /0revision /0asset /0alias**|
|Dev sau dừng|40content/40SourceRecords/51revision/80alias/60media(48ảnh12PDF),53originalNotScanned+7Clean;70capture/68SQLsnapshot/8ngoại lệ/2videometadata/0videofile|
|Bảo toàn|PASS17groups0diff so với đầu lượt và checkpoint3H-Fix; fileSHA đúng;Identity/envdigestkhôngđổi; blocker ledger hash giữ nguyên,52rows không bị tác động|
|Verify/idempotency/HTTP sau Apply|**NOT RUN**, vì chưa gọi Apply; không biến stopreconcile hoặc preflight thành verify/import/idempotencyPASS|
|Rollback|Không cần,0commit. Approval chỉ actionApply; rollback tương lai vẫn cần actionRollback đúng quy trình|

## Backup và các lỗi được giữ nguyên

Bộ backup hợp lệ: `C:\TrungTamDuLieuVaThongTinDatDai\website_tin_tuc\.local\backups\LandPortal_Test-20261021T015310Z\ha88e46496`; receipt `.local/evidence/phase3f/phase3h-apply-20261021T071528Z-4099b799/backup-complete/backup-complete.json`. SQL COPY_ONLY/CHECKSUM/VERIFYONLY/HEADERONLY thực chạy:

- Dev11.690.496byte, SHA `874d5a7b73207c0b9215ce20ab6ea462ea8997ae5c4ad1d2b436644eefb71e7a`.
- Test198.011.392byte, SHA `d2e717e1f8594f27a9db2f5e00b005bee6a7cd41c7e1f4d6c43a93b2c80c117d`; giữ các SQL scan receipts và proof dependencies cho đúng batch.
- File manifest: `C:\TrungTamDuLieuVaThongTinDatDai\website_tin_tuc\.local\backups\LandPortal_Test-20261021T015310Z\ha88e46496\d\file-manifest.json`, SHA `291729097f3d2c4a2b2cdc11d5628fd2cdccaa3420d9dc7a75f57950e940662d`. `RESTORE.md` trong backup hướng dẫn phục hồi; không restore đè database nào.

Lần backup đầu ở `haf1354be1` **INCOMPLETE**: AdditionalFiles dùng đường dẫn approval tuyệt đối làm key `dependency/C:` nên Windows báo WinError123. Không sửa helper hoặc guard; gọi lại helper hiện có với đường dẫn tương đối và ID mới `ha88e46496`, đầy đủ backup/hash/source readback/restore-check. Không ghi đè hoặc gọi bộ lỗi là hoàn chỉnh. Vị trí backup không nằm trong nội dung plan ký; plan/approval SHA giữ nguyên.

Harness init ban đầu dùng sai tên thuộc tính JSON `matches` thay vì `equal`; dữ liệu thực17/17equal=True. Giữ `stop.json` lỗi harness, dùng bước kiểm tra đọc riêng; không có SQL timeout hoặc commit. Không cộng các lần lỗi/chạy lại thành số testPASS.

## Blocker chính xác và checkpoint

Approval đã được chủ dự án xác nhận và ký đúng scope; **không thiếu approval nghiệp vụ**. `approval-preflight.json` ngay sau ký PASS. Lần xác minh riêng trước Apply bị lỗi CNG khi mở private key để đọc keyExportPolicy trong `scripts/dev-signing-common.ps1` (`Get-DevSigningCertificateFacts`, dòng40), được gọi từ `scripts/test-dev-import-signature.ps1`. Public-only inspection sau đó vẫn nativeAuthenticodeValid, chainValid/EKUCodeSigning và approvalSHA không đổi; điều này **không thay thế guard private-key facts đang lỗi**. Chưa đủ bằng chứng xác định nguyên nhân CNG hoặc sửa lỗi.

Không sửa C#/signer policy/scanner/importer/backup helper/migration; không bypass, thay trust, quyền, .env, Production hoặc DNS. Chỉ tạo harness riêng trong `.local` và cập nhật tài liệu. Không build vì source không thay; không fullregression/UI/crawl/email/push. SQL không timeout trong các thao tác đọc đã chạy; **SQL ROOT CAUSE UNKNOWN / MONITORING**, incident lịch sử giữ nguyên.

Bước tiếp theo: xử lý có kiểm soát lỗi CNG/certificate facts bằng fixture và đúng certificate hiện có, giữ fail-closed; kiểm lại approval còn hạn, hash/scope/currentproof, backup/currentDev và freshpreflight trước bất kỳ Apply nào. Không ký approval mới hoặc tạo packet khác tự động. Nếu scope/hash thay đổi hoặc approval hết hạn, dừng theo runbook và lấy xác nhận cần thiết.210content Phase3F vẫn chưa nhập Dev;52blocked giữ nguyên; không tự chạy packet khác. [Runbook](phase-3h-fix-runbook.md).
