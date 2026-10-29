# Phase 3G — sửa tracking trước ký, 21/10/2026

**Checkpoint sau đó:** [Stabilization](phase-3g-stabilization-report.md) đã chạy lại4SQLgroupsPASS,giữrootUNKNOWNvìkhôngrepro;strictfreshproof118/newpacketREADY_FOR_APPROVAL,chưaký/Apply. CácFAIL/PARTIAL bên dưới là raw/trạngthái tại lượt tracking-fix, không ghi lại thànhPASS giả.

**PARTIAL: đã sửa code, build PASS; nghiệm thu SQL bị chặn bởi timeout. Chưa ký approval, chưa Apply Dev.** Người dùng chọn “Sửa nhãn tracking trước khi ký”, không xác nhận batch/hash để ký. Trạng thái READY_FOR_APPROVAL của packet20/10 là lịch sử, hiện **không còn đủ tiền kiểm**.

Run `phase3g-tracking-20261021T015245Z-a42b32ea`, pointer `.local/phase3g-tracking-current.json`, evidence `.local/evidence/phase3f/phase3g-tracking-20261021T015245Z-a42b32ea`. Không ghi đè raw/plan/proof/review cũ.

## Phần đã triển khai

Trong `src/MigrationCli/SourceBatch.cs`, chỉ sửa nhánh tạo CrawlRun khi inventory chưa có tracking:

- Status mới: **`Phase3BCheckpointReplay`**.
- Checkpoint mới: **`Checkpoint replay; inventory={inventoryId}; sha256={checkpointHash}`**.
- Nhãn mô tả replay checkpoint, không tự khẳng định acquisition thực hay fixture. Giữ nguyên CrawlRun đã tồn tại, gồm status, checkpoint, budget, timestamps và provenance.
- Tracking vẫn ở trong transaction import từng item; không đổi scanner, chữ ký, approval, cách publish, source identity, revision, asset hoặc URL mapping. Không sửa các hàng tracking lịch sử trong SQL.

File thay đổi/thêm: `src/MigrationCli/SourceBatch.cs`, `tests/LandPortal.Tests/BatchTrackingTests.cs`, nhánh `--batch-tracking-only` của `tests/LandPortal.Tests/Program.cs`, `scripts/test-phase3g-tracking.ps1`. **Không migration.** Isolated build `.local/build/phase3g/tracking-current`; không ghi đè build3F/packet20/10. Runner dùng SQL Test/storage Test hiện có, không chép lại Dev53, yêu cầu backup Test<=12h được tool kiểm bằng SHA/header/quyền sẵn có. Test dùng fixture không có media, không mock scanner và không được tính là dữ liệu nguồn.

## Kiểm tra thực tế

|Kiểm tra|Kết quả|
|---|---|
|Backup Test trước kiểm thử|PASS: COPY_ONLY/CHECKSUM/RESTORE VERIFYONLY thật; 2.668 file đọc lại/0 hash mismatch. Chỉ NTFS dịch vụ SQL trên thư mục backup riêng; không grant SQL/Identity|
|Build sau thay đổi cuối|PASS: 0 warning,0 error; `tests-after-reconcile/build.log`|
|Lần SQL đầu|**0/4 PASS,4 FAIL timeout**; lỗi đầu Execution Timeout, ba lỗi sau Connection Timeout post-login. Raw giữ trong `tests/`|
|Reconcile trước thử lại|PASS: 4 SourceIdentity fixture/3 inventory chưa có CrawlRun/ImportRun/SourceRecord; metadata/count probe hoàn thành, không thấy transaction chặn trong các mẫu đọc|
|Lần SQL cuối, sau reconcile|**0/1 PASS,1 FAIL Execution Timeout;3 NOT RUN**. Runner dừng ngay lỗi đầu; codeDrift0. `tests-after-reconcile/sql-tests.json`|
|Reconcile cuối|PASS: inventory/2 SourceIdentity của lần cuối đều0 CrawlRun/ImportRun/SourceRecord; không partial commit fixture; `final-test-reconcile.json`|
|Fresh preflight với CLI mới + packet cũ|**FAIL/BLOCKED SqlException Execution Timeout**, exit1, chưa có reconcile PASS; `old-packet-preflight.log`/exit.json|
|Đối chiếu Dev/Identity/.env|PASS: 0 sai khác/17 nhóm, Identity và .env SHA không đổi|
|Full regression/HTTP/UI/scanner/ký/Apply/rollback Dev|NOT RUN|

Không cộng hai lượt SQL thành suite mới. Ba nhóm NOT RUN cuối: giữ tracking cũ qua comparison revision, rollback tracking khi lỗi trước commit, negative media/Production/Dev authorization. Chưa có bằng chứng persistence/idempotency/rollback của bản sửa nên **không gọi tracking SQL PASS**. Bản sửa chưa được ghi vào hàng Dev nào.

SQL timeout đã tái hiện ở Test và preflight, nhưng **ROOT CAUSE UNKNOWN**. Các mẫu metadata chỉ thấy request đọc của probe, không chứng minh nguyên nhân hay loại trừ mọi blocking tại thời điểm khác. Không tăng timeout, đổi SQL config/service/connection policy, kill session/process, nâng quyền hoặc retry tiếp. `sql-timeout-requests.json`, `sql-during-preflight.json`, `before-controlled-test-repeat.json`, hai reconcile ghi rõ căn cứ của đúng một lần thử lại có kiểm soát.

Lệnh đã chạy cho lượt cuối (backup receipt chỉ có hiệu lực<=12h; lượt tiếp theo phải backup mới nếu hết hạn):

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase3g-tracking.ps1 -BackupReceipt .local/evidence/phase3c/phase3g-tracking-20261021T015245Z-a42b32ea-test-backup/backup-LandPortal_Test.json -ReportRoot .local/evidence/phase3f/phase3g-tracking-20261021T015245Z-a42b32ea/tests-after-reconcile
```

**Không chạy lại lệnh vào report cũ.** Dùng report unique mới sau khi giải quyết/đánh giá tình trạng SQL và reconcile trước retry. Không chạy full137.

## Packet ký/Apply chưa sẵn sàng

Packet cũ: batch `14849e55-a85c-4d7f-94cf-1253ac5103ef`, manifestSHA `bad4234f01ff503762c966ccf675cdc9168ca945e5753a8eaa1b21ab85bcbafe`. Giữ file/hash/review unsigned, không coi câu trả lời sửa tracking là phê duyệt. Số 3content/4revision/7ảnh/6alias là preview **ngày20/10**, không phải fresh preflight PASS của code mới.

Defender hiện `1.1.26090.9 / definitions1.461.118.0`, khác proof112 của packet. Proof không hợp lệ theo gate exact engine/definitions, dù TTL sớm nhất lúc kiểm tra ban đầu chưa hết. Preflight thực tế dừng ở SQL timeout, **không** giả báo đã chạy tới/đạt guard scanner hoặc reconcile. Job cũ vẫn **180 receipts/3resource**, không thêm proof, tăng limit hay tự đổi NotScanned thành Clean.

Chuẩn bị **đề xuất chưa phê duyệt** cho đúng60 bản sao public Test đã kiểm SHA (53protectedTestcopies+7ảnh mới), không gồm originalDev53:

- Job đề xuất `2fb80968-969a-4223-bd13-c33295b2badb`.
- Inventory `.local/evidence/phase3c/phase3g-tracking-20261021T015245Z-a42b32ea-recovery-review/inventory.json`.
- SHA `685b0bb56c72aecc4b6ddd5268c57120fab9a54964fb3cbb4ebca81397508f67`.
- `recovery-job-review.json` = **PENDING_RECOVERY_JOB_REVIEW**, chưa register SQL, chưa scan. Đây không phải approval mới hay cách reset retry budget. Quy tắc hiện hành trong AGENTS: “nextrefresh requiresreview, neverraisebudget”.

Tiếp theo: kiểm tra SQL có kiểm soát để chạy được scoped SQL tests; review recovery job mới rồi register/quét Test thật qua tool khi đủ quyền; tạo fresh preview/manifest bằng CLI tracking mới, đối chiếu delta và trình lại exact batch/hash trước ký. Sau xác nhận mới backup Dev + dependency/SQLTest proof receipts, preflight/Apply/verify/idempotency/smoke. Không dùng lệnh ký/Apply packet cũ lúc còn blocked; không tự cấp quyền hoặc sửa .env.

## Bảo toàn

Dev vẫn **37 Contents/37 SourceRecords/47 revisions/74 aliases/53 NotScanned (41 ảnh,12 PDF),70 capture/68 SQL snapshots,8 ngoại lệ,2 video metadata/0 tệp**. `dev-before/after.json`: **0 khác biệt/17 nhóm**; `identity-before/after.json`: PASS; `.env` SHA nguyên. Không nhập fixture thành nguồn, không sửa nguồn Dev, không sign/Apply, không crawl/deploy/push/email. Các incident artifact cũ giữ **PARTIAL**; toàn dự án **CHƯA PRODUCTION READY**.
