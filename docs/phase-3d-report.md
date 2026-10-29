# Phase 3D — SRS v1.0 / Data Migration, 19/10/2026

**Phần mềm trong phạm vi kiểm thử PASS LOCAL; tổng thể PARTIAL. Không Apply Dev, không tăng số nội dung đã chuyển trong lượt này. CHƯA PRODUCTION READY.** [Runbook](phase-3d-runbook.md).

Run riêng `.local/evidence/phase3d/phase3d-20261019T044027Z-5990f061/`; pointer `.local/phase3d-current.json`. Đã đọc toàn bộ attachment và SRS v1.0 sau khi người dùng bổ sung tệp:380 đoạn gồm các ô bảng, SHA trong `srs-v1-sha.json`, bản đọc `srs-v1-extracted.txt`. Không suy bản0.5 là1.0. Baseline3C đối chiếu với artifact/code/SQL thực, không sao chép6/6 thành kết quả mới.

## Kết quả bàn giao

| Câu hỏi | Kết quả |
|---|---|
|Đã hoàn thiện gì?|Audit offline512URL/270keys; namespace/parser private NukeViet + gợi ý trùng; schema3 bind media-root;301 `.html`/HTTP/HTTPS có xử lý xung đột; FR17 tên chốt/Test-only decision; gói trình duyệt Dev mới/runbook|
|Có thể chuyển tiếp bao nhiêu?|Test37/40 danh tính đủ điều kiện theo freshpreview (37Keep/3Blocked). Dev chỉ10 dòng đủ đề nghị:3new+1comparison+6Keep,7ảnh; không phải37 bài mới|
|Còn blocker thực?|3 danh tính Test:PDF3233/Video40/Video41. Dev vẫn30 blocked do53originalsNotScanned và các nguyên nhân giao nhau. Thêm gate tiền kiểm SQLTesttimeout/approval/Production riêng|
|QLDD mới kiểm chứng/nhập thêm?|**0/0**;40 verified giữ nguyên.228 ứng viên thiếu snapshot chi tiết;2 snapshot không đủ bóc tách. Không gán số candidate thành số bài nguồn|
|NukeViet?|Đã chính thức trong phạm vi nghiệp vụ. Code offline/namespace/dedupe/301 đã Test bằng fixture kỹ thuật. Chưa có snapshot/export thật, category mapping chưa duyệt, chưa có SQL write adapter nguồn này;0 nội dung NukeViet chuyển|
|Dev batch trình duyệt được?|Có gói đề nghị/hashes/lệnh; **PRESENTABLE / NEEDS_APPROVAL**, chưa READY để thực thi. Không có signed authority/trustpolicy được cung cấp; cần freshbackup và tiền kiểm SQL ổn định|
|Cần quyền/quyết định gì?|Signer/cert/operator và batch Apply/Rollback; policy quét/áp53Dev riêng; phạm vi thu thập/export QLDD/NukeViet/mapping; danh sách quyền Dev chưa cấp; targethost/TLS/scanner/MFA/SMTP/restore/UAT|
|Bước ngắn nhất?|Xử lý tiền kiểmSQLTest + duyệt authority/gói3bài độc lập; đồng thời cấp export hoặc approval bounded để kiểm chứng228 URL thiếu snapshot. Không chờ ba blocker để chuẩn bị phần độc lập|

## Chênh lệch SRS và triển khai

SRS1.0 chốt `gdla.gov.vn`, SQLServer/CSDL mới, hai nguồn, đủ14 chuyên mục +3FR17; CAPTCHA công khai, lịchWord/revision. Không thay kiến trúc10/MVC/Razor hoặc dựng lại các module2A–2D. Các phần phù hợp được giữ. FR17group2 tên đã chốt; bỏ nhãn pending viết cứng ở admin. `name-decision-test` kiểm Development/SQLTest/backup/decisionSHA + transaction/audit/idempotence; Dev bị chặn. Không migration, seed/model history không sửa; latest migration hai database vẫn `20261015091551_V05EditorialDocuments`.

Tra cứu chi tiết hỏi đáp vẫn khóa: SRS có mã+email nhưng CAPTCHA không xác minh sở hữu, chưa có phương án an toàn được duyệt. Video2D tải trực tiếp giữ nguyên; không lấyYouTube thuộcID41 thayID40. Không làm thống kê đất/xếp hạng hoặc các mốc khác trong lượt này.

## Độ bao phủ có bằng chứng

`offline-audit/coverage.json`, `candidates.csv`, `url-ledger.json`:512URL,270khóa candidate theo Kind/ID,40verified.230pending gồm **228không có snapshot chi tiết +2extraction review**. Audit kiểm SHA/byte từng snapshot chi tiết hiện có, không sửa checkpoint/counter. Hai lỗi hiện có: Draft3 thiếu tiêu đề; Draft2024 có canonical trang tin nhưng h1 rỗng, không có bảng metadata dự thảo. Không dùng khối liên quan/sidebar thay nội dung. Không có lỗi phân trang nào được xác minh đủ để sửa; không giả vờ sửa parser để tăng số.

Ưu tiên category76/77 (0 verified trong số10candidate mỗi nhóm),81/83 (không có hàng truy cập được, tổngUNKNOWN),75(32pending),48/49(18mỗi),51(17). Các số theo nhóm giao nhau; không cộng thành tổng230.14 chuyên mục cũ và các mục nguồn khác giữ nguyên. `next-batch.json`:0newReady, cần collection/export có phép.0GETnguồn lượt3D,39GET3A vẫn nguyên; không tạo session mới/láchquota. NukeViet không được tự đưa vào allowlist collectorQLDD.

`MigrationSourceIdentity` tách source/module/loại/ID, giữ ngôn ngữ trong module. `nukeviet-preview` bound rawSHA/8MiB/private path/canonical cùngidentity/verifiedbody; lưu file/nguồn và đề xuất so trùng theo body. Trùng title không hợp nhất; trùng body vẫn NEEDS_REVIEW. Bảng category mapping chưa có dòng thực để điền, định dạng trong runbook; chưa đủ chứng cứ nhập NukeViet SQL hoặc nghiệm thuAC05.1 hai nguồn.

## Gói Dev và an toàn kho đích

Fresh plan schema3 `dev-preview/plan.json`, SHA **08ea3bcae6385c023ed9a2c8c1ceb6a2f8c4ef78ca5db50eb18dc29018b58e93**, batch `6c2a9c30-a76b-4a99-827c-c09a3ccd3c41`.40rows:34unchanged/3changed/3new;30Blocked/6Keep/3CreateNeedsApproval/1SourceComparisonOnly. Existing3differences giữ phân tích3C, không phải ba cập nhật live vừa tải.

`dev-apply-manifest.json` SHA **a9e43beef6e54edce6fbb50481ef7c340048cb6d67b7a557f554d7b277a7ef95**,3new/7freshJPEG/1comparison/6Keep/20mapping,65dependency files cóhash/bytes; bảo toàn53Dev. Store SHA khác Test và đúng `.local/media`Dev; Commit/Verify/Rollback từ chối store khác. Schema1/2 vẫn đọc/Test để bảo toàn batch cũ; Dev authorization mới yêu cầu3. Chưa thực thi bất cứDevApply nào.

Authority thực tế là **approval file ký Authenticode** +Windows chainValid +thumbprint tổ chức đã duyệt trong SafeImport, exactplan/target/operatorSID/actions/expiry. Không phải chữ ký PDF. Operator đang có db_owner/sysadmin local theo probe, nhưng thiếu authority ký được cung cấp; không tự cấu hình trust. Approval protocol/path Phase3C giữ nguyên để tái sử dụng công cụ đã Test. Positive external-signed path NOT RUN. FreshbackupDev/restore drill NOT RUN trong3D; dùng lại backupTest3C thật<=12h cho testsafeapply, đã kiểmBAKSHA/header/permission trong code. Không cấp thêmSQL/Identity.

Lệnh dự kiến `proposed-dev-apply-command.txt` chưa chạy, cần đường dẫn approval/backup thật và reportduynhất. Thay đổi cấu hình/kho/stamp hoặc proof hết hạn/engine khác phải freshpreview/duyệt lại. Backup cần cả SQLTest receipts/proof files recovery, không chỉ databaseDev. Rollback withdraw publication mới chưa sửa, giữ source/revisions/files/audit/aliases; không reset/restore hay đăng lại bằng replayCommit.

## Ba blocker

`blockers-current.json` đọc lại bytes thực từcache. PDF3233 rawSHA `6b6064ed8725c5105dd5baa905a3d8d899578fc5d5bd3072bd274eeb7fe5b308`,2.919.460bytes, MIMEcaptureapplication/pdf, magic `%PDF-` ởoffset21. Reader raw cầnrepair; candidate bỏ21bytes SHA `f27fc2f1c00ef72f77fde2b54cf3c42365edf6d3f987e9fdd8e87b2b0f9e1c08` mở3trang khôngrepair, pixels3/3giống. Có ByteRange; crypto chưa kiểm. **SRS không đặt yêu cầu mọiPDF phải ký số.** Blocker là rawkhông đạt format gate; candidate đổi bytes/hash chưa có provenance recovery được chấp thuận/chưaquét; không tự áp derivedfile, không sửa raw/NotScanned.

Video40 ownmetadata không cófile; genericYouTube thuộc41 không dùng thay. Video41 ownmetadata cóMP4 gdla; **không còn ngoài phạm vi nghiệp vụ**, nhưng chưa có tệp được tải theo scope/quota được duyệt, nên giữ acquisitionNEEDS_APPROVAL.0video transferred/converted;2DNOTRUN, không mock.

## Test và sự cố SQL mới

Sau sửa code/script cuối: **11/11PASS,0FAIL**, `phase3d-tests-20261019T044754Z-39e2caf3`,24targetedpaths/0drift, Web/CLI/Tests/dependencies build0warnings/0errors. Không full137/Worker/video/UAT.10/10trước sửa301 là lượt riêng; lần8/10FAIL và lỗi build đầu giữraw, không cộng/chuyển thành11.

- Audit thật512/270/40, hash/checkpoint không đổi; NukeViet canonical/namespace/rawSHA/title-onlynegative bằng fixture kỹ thuật có nhãn.
- Schema3 wrongstore từ chối cả3actions trước SQLwrites; FR17 quyết định/audit/idempotence/Testonly, giữ menu và public pointers.
- HTTP `.html`/httpalias301 đúngbài, collisionHTTP/HTTPS404, withdrawn404, group2Test200. Không AC05.1 với50URL thật từhai nguồn.
- GenuineDefender control PNGClean/EICARZIPInfected/health-cancelScanFailed; reuse60sourceproof còn hạn0native receipt mới. Không gọi reuse là60scan mới.
- SafeApplyTestbackup thật/fault sau materialize/orphanprivate/retry/verify/repeat/rollback giữrevision/file; staleeditor/conflict; HTTP60media exactSHA/12PDFRange/ACL/publicaffectedpages. Đây là các nhóm có trong11, không cộng checkpointHTTP riêng.

**Phần batch nguồn thật sau đó PARTIAL:** fresh Testpreview40 gồm37Keep/3Blocked, không cần thêm source revision mới. Commit gặp SqlException timeout **trước khi tạo report directory**; lần thử lại có `real-apply-retry.log` cũng timeout. Không cóapply.json của batch mới, không gọi đã commit/repeat/verify37thành công. Lầnreadback đầy đủ40Testcuối cũng timeout; kiểm traSQLTestreceipt count riêng15s thất bại. Dừng retry, không tăngtimeout/chỉnhSQL/restart/kill.

`sql-timeout-observation.json`: SELECT Test trong một thời điểm chờ **SLEEP_BPOOL_STEAL**, blocking_session_id0; observer cótimeout. Đây là quan sát, chưa chứng minh rootcause hoặc quan hệ với timeoutcategorylịch sử. Giữ **MONITORING / ROOT CAUSE UNKNOWN**. Lần commit đầu/readback lỗi chỉ có tooltranscript, không córawfile đầy đủ; báo cáo structuredobservation không được gọi là raw phục hồi. Lầnprobe đầu lỗi biếnhelper sauquery được sửa và chạy mới, không tạo lại kết quả cũ.

No reportfolder nghĩa code chưa tớiCommitAsync (overlay/tiềnkiểm trước mkdir); không có kết quả thao tác ghi batch để nghiệm thu. Các testSQL kỹ thuật đãPASS trước sự cố vẫn là bằng chứng đúng phạm vi; không dùng chúng thay finalrealbatchreadback.

## Bảo toàn và phần chưa làm

Dev trước/sau **0khác/17nhóm**:37Content/SourceRecord,47revision,74alias;53NotScanned41ảnh/12PDF;70capture/68SQLsnapshot;8ngoại lệ;2videometadata/0file. Identity/credentials/stamps/claims/roles giữ nguyên. `.env` và sealed3A checkpoint đối chiếuhash.40realTest/60asset được readback trước realbatch; finalSQLreadback BLOCKEDtimeout, filesystem60hash đọc lại riêng. FixtureTest tách khỏi nguồn, không reset/xóa; sourcecontent0mới trong3D.

Files mới: MigrationCli `SourceIdentity.cs`, `CoverageAudit.cs`, `Phase3DCommand.cs`; scripts `phase3d.ps1`, `test-phase3d.ps1`; Tests `Phase3DTests.cs`, `Phase3DHttpTests.cs`. Sửa `BatchModel`, `SourceBatch`, `ApprovedBatchExecution`, `BatchCommand`, `SafeBatchCommand`, CLIProgram; PortalController/admin metadata; test runner/evidenceallowlist. Không migration/package/.env/Identitygrant.

Lệnh test trongrunbook. HTTPowned5303/5305 đãdừng bằngfinally; preview3C5304/oldports giữ nguyên, không gọi là UIcode3D. Không mở preview3Dmới hoặc targetdomain. Chưa cóProdscanner/HTTPS/MFA/SMTP/restore/UAT/3Wordnghiệpvụ/lookupapproved, fullsourcecount/cutover. ProgramWeb vẫnloopback; targethostWeb/WorkerNOTRUN. Các incidentartifactV04/D15/v05 vẫnPARTIAL, không phục dựng. Không crawl/email/deploy/DNS/push/Devapply.
