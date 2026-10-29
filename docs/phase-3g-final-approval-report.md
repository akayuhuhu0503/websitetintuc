# Phase 3G — hồ sơ duyệt batch Dev cuối, 20/10/2026

**Bị thay thế bởi checkpoint21/10: [sửa tracking PARTIAL](phase-3g-tracking-fix-report.md).** Chủ dự án chọn sửa trước ký, chưa phê duyệt batch này. Code nhãn đã sửa/buildPASS nhưng scopedSQLtest/preflight bị timeout; liveDefender118 khác proof112. Vì vậy READY_FOR_APPROVAL dưới đây chỉ mô tả20/10, **không dùng để ký/Apply hiện tại**. Raw/hash/review cũ giữ nguyên, cần freshpacket sau khi đủ gate.

**READY_FOR_APPROVAL cho batch ưu tiên dưới đây; chưa ký approval thật, chưa Apply Dev.** Toàn dự án **CHƯA PRODUCTION READY**. Run `phase3g-final-20261020T031249Z-93f55984`; pointer private `.local/phase3g-final-current.json`. Không sử dụng lại nhãn/raw của run cũ.

## Batch chính xác cần chủ dự án xác nhận

- Batch ID: **`14849e55-a85c-4d7f-94cf-1253ac5103ef`**.
- Import manifest máy đọc chính là `plan.json`, SHA-256 **`bad4234f01ff503762c966ccf675cdc9168ca945e5753a8eaa1b21ab85bcbafe`**.
- Plan: `.local/evidence/phase3f/phase3g-final-20261020T031249Z-93f55984/dev-preview/plan.json`.
- Danh sách chi tiết, metadata nguồn, mapping và proof: cùng run, `batch-review.json`. Dự thảo phê duyệt: `.local/signing/reviews/phase3g-final-20261020T031249Z-93f55984/review.json`, SHA `79a2916cfd168b7029e5c31e0688aada9527ae35bead3f6257179f9ea3e3d708`, **PENDING_USER_APPROVAL**, ApprovedBy/reference null.
- Đích: `MinhAnh\SQLEXPRESS / LandPortal_Dev`; connection target `.\SQLEXPRESS`, expected database `LandPortal_Dev`. `@@SERVERNAME` còn `THANGPD\SQLEXPRESS`; người dùng đã xác nhận lịch sử đổi tên/cùng instance. Kiểm tra mới khớp database GUID `b499750c-a0c8-4003-b7ee-dce4938f78dc`, service `MSSQL$SQLEXPRESS` Running, machine/instance đúng. Không sửa metadata SQL/guard.

|Nguồn thật QLDD|Tiêu đề|Thay đổi dự kiến|
|---|---|---|
|Article:3666|Hội thảo lấy ý kiến về dự án Luật Đất đai (sửa đổi) tại thành phố Đà Nẵng|1 content + 1 revision + 3 ảnh|
|Article:3670|Tháo gỡ khó khăn trong thực hiện chính sách, pháp luật đất đai tại thành phố Đà Nẵng|1 content + 1 revision + 2 ảnh|
|Article:3673|Hội thảo lấy ý kiến về dự án Luật Đất đai (sửa đổi) tại Thành phố Hồ Chí Minh|1 content + 1 revision + 2 ảnh|
|DraftDocument:5|Giữ metadata/nội dung nguồn trong manifest chi tiết|1 revision đối chiếu nguồn; không đổi bản đang công khai/biên tập|

Tổng delta dự kiến: **3 Contents, 3 SourceRecords, 4 revisions, 7 JPEG/2.048.267 byte, 6 URL aliases**. Nếu Apply đúng sẽ có 40 contents, 51 revisions, 60 assets, 80 aliases; đây **không phải số đã nhập**. Không thay ngày đăng/nguồn gốc/chuyên mục/ảnh của 37 bản hiện có, Identity hoặc trạng thái original53. 6 dòng Keep và 30 dòng Blocked được ghi rõ trong plan; 30 dòng là các danh tính CMS hiện có, không phải 30 bài mới cần nhập. Các dòng Blocked không được áp dụng; không bỏ gate scanner để mở chúng.

**Thay đổi theo dõi/ngoại lệ phải đọc trước khi duyệt:** 10 dòng hợp lệ sẽ có receipt/audit theo công cụ và 1 ImportRun mới. Dev chưa có CrawlRun của inventory `9d95c26f-85a5-4a8a-a2df-61def03cf360`, nên code hiện tại còn tạo 1 CrawlRun với nhãn fallback cũ **`Phase3BTechnicalFixture` / `Fixture-only; not source acquisition`** (`SourceBatch.cs`, tracking-preview.json). Đây là nhãn tracking không chính xác đối với replay nguồn thật; đã công bố để người duyệt quyết định, không dùng nhãn đó làm bằng chứng nguồn/acceptance. Không chép content fixture hoặc database Test; byte và nội dung đều từ checkpoint QLDD thực đã hash-bound. Lượt này không sửa importer để che ngoại lệ. Nếu chủ dự án yêu cầu sửa nhãn trước Apply, cần delta nhỏ có kiểm thử rồi xác minh lại packet; chưa chấp nhận mặc nhiên.

## 7 ảnh và proof mới

Tất cả là JPEG **Clean từ Microsoft Defender thật**, hash byte nguồn khớp. Không dùng kết quả mock; không scan hoặc StageTest/đổi original Dev53.

|SourceIdentity|Byte|SHA-256|
|---|---:|---|
|Article:3670|271730|`2943bacaef10a76f7ff3a42014f2d69b333ed087c844db79d3141191caa06b3c`|
|Article:3673|346967|`5544e6c466cdded37ff92d05e8ee91f1ec29a9aba7410cabdbb3a1528b7df316`|
|Article:3666|346215|`8b38df2bccca8a00a54b09be7d6c4800270922a30bcc1b8b7e2321d8d5eeb87b`|
|Article:3666|331234|`a3c5a6a020a34964be243199405dfe219e5d910afab6faac5ac00b1014d77eac`|
|Article:3673|283643|`e6746503de43d26b4961d5b278981e2c2c75ed5681231501f0aa2344da5698d1`|
|Article:3666|265736|`ef989d2beefe9a8a9ab021d8d898ceab99143444ef2a1af55bc34d2703ef4962`|
|Article:3670|202742|`fe5e8fe6a73006e92e325847bdc46649cecc868a8a76304779e9eb88e6a18b0b`|

Proof-set `.local/evidence/phase3c/phase3g-final-20261020T031249Z-93f55984/scan/proof-set.json`, SHA **`4d1ca5679f6f20da13bc6d885032485b62bc54759ac341f5c5033d590f7a52d3`**. Engine `1.1.26090.9`, definitions **`1.461.112.0`**, updated `2026-10-19T19:35:17Z`. Theo gate overlay đang có, đã refresh cả 60 bản sao public Test cùng job, 60 Clean/0 Infected/0 ScanFailed, 0 byte/proof-hash mismatch, 0 SQL receipt thiếu; chỉ 7 proof không protected được overlay vào Dev. Đây không phải kiểm thử EICAR/ScanFailed mới; các control đó không chạy lại trong lượt chuẩn bị này.

Hạn sớm nhất **21/10/2026 10:16:08 giờ Việt Nam** (`2026-10-21T03:16:08.1776318Z`), còn phải đúng definitions/policy hiện hành lúc Apply. Job hiện **180 receipts, 3/resource**, đã chạm retry budget. Definitions đổi/hết TTL thì dừng để review recovery job mới; không bỏ check hoặc tăng giới hạn. Plan bind proof-set SHA, không thay proof-set của plan đã được duyệt một cách ngầm định.

## Signer policy và kiểm tra đã thực hiện

Chứng chỉ `94797BD79E275B09DEF613036BC00DF4EA91345F`: CurrentUser/My + Root đúng profile, Code Signing EKU, RSA3072, private key NonExportable, chain hợp lệ, hạn `19/11/2026 09:16:50` Việt Nam. Native Authenticode fixture hiện **Valid**, đúng signer. Không ký batch thật từ fixture.

Theo yêu cầu mới của chủ dự án, đã cấu hình **process-only** `SafeImport__ApprovedSignerThumbprints` trong helper Development có guard máy/target/cert. Probe gọi **assembly thật `PortalConfiguration.Load`** đã đọc đúng 1 thumbprint và operator SID hiện hành; không phải kiểm tra biến môi trường đơn thuần. Helper không có lệnh Sign/Apply. Tiến trình mới phải đặt lại đúng pin như runbook; không ghi pin vào `.env`, User/Machine environment, LocalMachine trust, TrustedPublisher, global ExecutionPolicy hoặc quyền ứng dụng. Cert này không dành cho Production.

|Kiểm tra thực của lượt này|Kết quả / artifact trong run|
|---|---|
|Certificate EKU/chain/key/expiry + native Authenticode|PASS — certificate.json, authenticode.json|
|Signer policy qua config loader thật|PASS — policy-probe.json; build harness cuối 0 warning/0 error|
|SQL identity/schema/capacity|PASS — sql-identity.json, migrations.json, capacity.json; Dev ROWS 72 MiB/~10,75 MiB used; C còn ~163,8 GiB lúc đo|
|Backup Test trước append proof|PASS — COPY_ONLY/CHECKSUM/VERIFYONLY thật; đọc lại 2.668 file/0 mismatch; test-backup-check.json|
|Exact-byte Defender + SQL receipts|PASS — scan.log, input-hashes.json, scanner-verification.json|
|Fresh Dev preview|PASS — dev-preview/plan.json, summary.json, mapping.csv, findings.csv; exit0|
|Fresh Dev preflight|PASS — dev-preflight/reconcile.json: ABSENT_CONFIRMED, 10 eligible/0 committed/0 errors; exit0|
|Dữ liệu cũ, Identity và .env|PASS — dev-before/after, identity-before/after; 0 sai khác/17 nhóm; .env SHA không đổi|
|Phê duyệt exact batch/nhãn tracking, ký thật|PENDING_USER_APPROVAL / NOT RUN|
|Backup Dev, backup SQLTest receipts sau scan, Apply/verify/idempotency/smoke/rollback|NOT RUN — chỉ thực hiện sau phê duyệt exact và signed approval hợp lệ|
|Full regression, UI toàn site, crawl, migration|NOT RUN — không thuộc thay đổi|

Hai lỗi chuẩn bị được giữ rõ: backup Test đầu thất bại khi service chưa có ACL thư mục mới; lần sau dùng flag helper cấp **NTFS Modify chỉ cho dịch vụ SQL trên chính thư mục backup mới**, không grant SQL/Identity. Probe đầu dùng so tên máy phân biệt hoa/thường sai trong harness ignored; sửa OrdinalIgnoreCase, build/probe cuối PASS. Script xuất manifest gặp lỗi encoding khi in console sau khi đã ghi JSON UTF-8 thành công; không tái chạy/ghi đè file đó. Không cộng các lần này hay 16/16 của lượt trước thành full suite mới.

Dev vẫn **37 contents/37 SourceRecords/47 revisions/74 aliases/53 NotScanned (41 ảnh,12 PDF),70 captures/68 SQL snapshots,8 ngoại lệ,2 video metadata/0 tệp**. Không SQL Dev business mutation, không chỉnh `.env`/Identity/SQL metadata. SQL timeout vẫn **ROOT CAUSE UNKNOWN**, incidents artifact lịch sử vẫn **PARTIAL**.

## Tiếp tục sau khi chủ dự án xác nhận

Xem [lệnh chính xác ký/backup/Apply/verify/rollback](phase-3g-final-approval-runbook.md). Chủ dự án cần xác nhận **đúng Batch ID + manifest SHA ở trên và phạm vi/ngoại lệ đã trình**, trước thao tác ký. Chứng chỉ hợp lệ và user pin không thay thế phê duyệt batch. Tiếp theo: ký approval action Apply, backup mới đúng Dev và dependency/SQLTest proof receipts, fresh exact-plan preflight, Apply, verify/readback, repeat an toàn, smoke cục bộ. Không tự thêm 207 nội dung hay 521 asset của plan mở rộng. Rollback cần signed action Rollback riêng, chỉ withdraw publication thuộc batch chưa bị editor sửa; không xóa dữ liệu ngoài batch.
