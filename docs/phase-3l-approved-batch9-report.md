# Phase3L — Batch9 APPLIED/VERIFIED LOCAL; Article3075 Test PASS; PDF3205 BLOCKED

Ngày 22/10/2026. Run `phase3l-approved-20261022T043053Z-7004e491`; evidence riêng `.local/evidence/phase3f/phase3l-approved-20261022T043053Z-7004e491`. Pointer `.local/phase3l-approved-current.json`, được liên kết từ checkpoint3L. Kết quả này thay trạng thái **chưa ký/chưa Apply** của báo cáo3L trước; không thực hiện lại12packet3I. **CHƯA PRODUCTION READY.**

## Batch được chủ dự án duyệt và đã thực hiện

- Batch `e02a8cb2-c7a4-4e9e-8884-737ee3b3cd01`; manifest SHA-256 `6e2374fd20fd20d74b48f1fd5c9d9c3d7437a119723f7a37b54514956d710eed`. Plan/checkpoint/inventory/proof-set/review giữ nguyên byte, không mở rộng phạm vi.
- SQL đích `SERVERPROPERTY('ServerName')=MinhAnh\SQLEXPRESS`, database `LandPortal_Dev`, database GUID `b499750c-a0c8-4003-b7ee-dce4938f78dc`; `Sql__ExpectedDatabase` khớp. `@@SERVERNAME=THANGPD\SQLEXPRESS` là định danh đăng ký cũ của cùng instance đã được chủ máy xác nhận. Không sửa metadata SQL.
- Reconcile đầu: `ABSENT_CONFIRMED`, Expected9/Committed0/Errors0. Schema `20261015091551_V05EditorialDocuments` không đổi. Đã kiểm tra dung lượng SQL Express và ổ đĩa; không thay cấu hình/timeout/quyền.
- Approval Authenticode Valid, CurrentUser chain/EKU/hạn hợp lệ; thumbprint `94797BD79E275B09DEF613036BC00DF4EA91345F`. Chỉ action Apply và exact batch/hash/operator/target. Owner confirmation lưu riêng với tham chiếu/hash trong approval. Pin signer chỉ trong Process; không thay trust hoặc `.env`.
- Native18 proof đã có được tái xác minh đúng byte/hash/SQL receipt/current identity/TTL. Defender1.1.26090.9, definitions1.461.143.0, updated21/10/2026 20:00:09UTC; TTL sớm nhất23/10/2026 03:28:36UTC. Không cần quét lại18ảnh/PDF; không quét hoặc promote53 tài nguyên Dev gốc.
- CLI Apply là binary đã review SHA `f13f2485110c15aae44ba714bd04667fe904366ab8a2539135db9aae10aa937a`. Parser được sửa **sau** Apply/verify; không thay binary giữa lượt Apply.

| Chỉ tiêu Dev | Trước | Thêm thực tế | Sau |
|---|---:|---:|---:|
| Nội dung nguồn / SourceRecords |250|9|259|
| Revision |261|9|270|
| Media asset |584|18|602|
| Ảnh |357|8 PNG|365|
| PDF |227|10|237|
| URL alias |500|18|518|

Chín SourceIdentity: Article3186,3191,3197,3198,3200,3201,3202,3211,3231. Tổng byte18tệp61.571.537. Verify/readback và đối soát độc lập: **0 mismatch** về identity/hash/title/summary/body/author/ngày/chuyên mục/alias/public revision;18file đều Clean, đúng byte/hash. Dev có53NotScanned+549Clean.

Chạy lại **cùng signed `safe-apply`** sau reconcile COMMITTED_VERIFIED:9`AlreadyCommitted`,0content/revision/asset/alias mới;9receipts, DataSHA không đổi. Đối chiếu lại17nhóm sau repeat0diff. Không chỉ suy luận idempotency từ số lượng.

## Backup và rollback

Backup mới trước Apply: `.local/backups/LandPortal_Test-20261021T015310Z/la0494e318/`.

- SQLDev và SQLTest COPY_ONLY/CHECKSUM, VERIFYONLY, HEADERONLY và SHA đều PASS. Receipt `backup/backup-complete.json`, `backup/backup-LandPortal_Dev.json` trong run.
-661tệp Dev,674.553.583byte, thêm166dependency: source raw/capture, checkpoint, current nativeproof/quarantine, review, **actual signed approval**, CLI và runtime dependencies. Capture/verify0thiếu/0hash mismatch; restore-check661tệp PASS; không đổi ACL, không ghi đè backup cũ. File manifest và RESTORE.md trong bộ backup.
- Chưa restore SQL **của bộ backup mới này**. Actual SQL restore PASS trước đó ở `LandPortal_Restore3J_d9d16f42` thuộc backup3L trước, giữ riêng bằng chứng; không gọi VERIFYONLY là restore.
- Không cần rollback: mọi receipt/verify/readback/idempotency đạt. Backup là điểm trước9bài. Nếu sự cố về sau: reconcile trước, chỉ withdrawal batch-owned/unedited theo đúng approval action Rollback; approval hiện tại **chỉ Apply**, không tự mở rộng. Không restore đè toàn Dev hoặc xóa dữ liệu ngoài batch.

Hai lỗi chuẩn bị PowerShell (enumeration mảng và hash dependency) được giữ trong `stop-sign-backup.json`, `stop-backup-resume.json`; xảy ra trước backup/Apply. Một approval được ký, không ký lại. Dependency manifest v2 dùng165file chính xác và1additionalfile; backup hoàn chỉnh mới được dùng. Không có lỗi signer/proof/target bị bỏ qua.

## Article3075 — sửa parser, staging mới và nhập SQL Test

Thay `srcset.Split(',')`/cắt ở space bằng `SourceSrcset.Urls`: tách URL token/descriptor, giữ comma trong URL và thứ tự candidate, kiểm tra descriptor hợp lệ. Ngoại lệ cho filename QLDD có khoảng trắng chỉ khi toàn URL được chứng thực độc lập từ `src`/`data-src`; không ghép đoán hoặc đổi raw. Tham chiếu thuật toán [HTML Standard](https://html.spec.whatwg.org/multipage/images.html#parsing-a-srcset-attribute).

- Fixture raw thực SHA `6784c4fb93abcf41f5d48005f1e6f4961459c7d2ce2d6b24ba9d6deb2a1a3ca7` được giữ nguyên.
-4/4 nhóm kiểm thử parser PASS: descriptor/whitespace/order, comma/data/encodedURL, malformed/conflicting descriptor, Article3075 thật.
- Replay282item cache:282parse được, chỉ **Article3075** đổi asset list, bỏ URL cụt do lỗi parser; giữ5URLảnh đầy đủ đúng thứ tự. Không có bài khác trong tập này bị ảnh hưởng; không suy rộng ra URL chưa có snapshot.0network request.
- Tạo checkpoint parent/projection mới có provenance/hash; không sửa checkpoint hay raw cũ, không ghi đè con trỏ SQL crawler. Private staging reset trạng thái của đúng5file trong bản dẫn xuất vềNotScanned trước quét.
- Private maintenance harness tái sử dụng `DefenderInquiryScanner`: Development/SQLTest/storageTest, exact5publicSHA, cap32MiB; không nhận53DevprotectedSHA. Scanner tạo Test copies, quét thật5Clean143, kiểm hash trước/sau, lưu5proof và5auditSQLTest. Các proof này **không phải approval/proof packet Apply Dev**.
- Test backup SQL mới sau5scan +22dependency, CHECKSUM/VERIFYONLY/SHA/copy-readbackPASS ở `la0494e318/srcset-test`; không sửa file Test cũ.
- Test batch `d9e9bd5a-f2f2-466c-8f0d-db7e4351abd2`, planSHA `772292f7aa45fb8d45735a5035221f8f7ab05dadaa0c1d094c5205a1d807e6cf`: **thêm1content/1revision/5ảnh/2alias**; reconcile/verifyPASS, repeat`AlreadyCommitted`. SQL proof/audit và5mediahash được đọc lại độc lập.
- HTTPTest8/8PASS: bài và5ảnh đúnghash/byte, anonymouspreview bị chặn, kho vật lý không lộ. **0ApplyDev cho3075**, chưa tạo/chấp thuận packetDev mới. Bước tiếp: strict public-recovery proof/manifestDev riêng và owner exact batch/hash trước nhậpDev.

Build utility đầu bị lỗi vì gọi API nội bộ; đã sửa chỉ trong helper riêng. Lần scan đầu bị privatehelper guard chặn do separator của prefix Windows chưa normalized; đã chuẩn hóa đường dẫn, giữ nguyên guard Development/Test/32MiB/allowlist, rồi build0warning/0error và quét thật thành công. Log lỗi cũ giữ nguyên; không tính các lần thất bại thànhPASS. Production importer/scanner/signature guards không đổi.

`sql-test-readback.json` có alias truy vấn `realQlddSourceRecords` đặt tên chưa chính xác:527 là số SourceRecords trong namespaceQLDD **bao gồm fixture**, không phải527nội dung nguồn thật.4795totalTestContents cũng có fixture. Xem `sql-test-count-semantics.json`; metric chuyển dữ liệu mới của phần này chỉ1Article3075 có raw network proof/5ảnh, không cộng fixture.

## PDF3205 — BLOCKED_POLICY_APPROVAL

39.192.764byte là Content-Length từ HEAD đã lưu ở3K, lớn hơn32MiB. Chưa có body đầy đủ, chưa biết SHA, chưa validate định dạng/trang/chữ kýPDF hoặc quét Defender cho file này. Không coi Content-Type/HEAD là validatePDF. Không tải lại nguồn trong lượt này.

Chưa có phê duyệt policy48MiB; yêu cầu hiện tại chỉ cho tăng **nếu đã được duyệt**. Giữcap32MiB ởcrawler/import/recovery/scanner và toàn bộ upload công khai/editor. Đề xuất riêng nguồnPDF48MiB/version/URLallowlist/finitebudget/streaming/diskreserve/PDFvalidation/scanner exactbytes đã ở [bảngpolicy](phase-3l-pdf-and-exceptions.md); không implement bypass bằng biến môi trường.

## Kiểm tra thực chạy và bảo toàn

| Phạm vi | Kết quả |
|---|---|
| Exact signer/manifest/target/strict18proof/preflight | PASS; preflight2679ms, Apply10175ms, không timeout mới |
| Backup SQL/media/dependencies/file restore | PASS; SQLrestore của backup mới NOT RUN |
| Dev9commit/reconcile/readback/independent verify/repeat | PASS;0mismatch/0delta repeat |
| HTTPDev |27/27PASS:3bài,18ảnh/PDFhash/MIME/length,category/search/301/privateACL |
| Parser focused |4/4PASS trên applicationcode cuối;282cacheditem replay riêng |
| Native3075/Testimport/verify/repeat |5Clean thật;1bài/1rev/5ảnh/2alias;PASS |
| HTTPTest3075 |8/8PASS, tách khỏi27Dev và4nhómparser |
| Preservation |250content/261revision cũ giữ nguyên tất cả cột;17nhóm dữ liệu cũ0diff;Identity/.env unchanged |
| Baseline nguồn cũ |53NotScanned,70capture/68snapshot,8ngoạilệ,2videometadata/0file giữ nguyên |
| SQL incident |0timeout mới quan sát; **ROOT CAUSE UNKNOWN / MONITORING** |
| Full regression / websitewideUI / EICARcontrols mới / Production UAT |NOT RUN; không cộng các hàng trên thành fullsuite |

52dòng ledger và34Dev-gap lịch sử không bị chỉnh để ép khớp. Trong18canonical chặn tài nguyên trước đây,3075 đã xử lý trênTest;17case khác còn theo bảng3L;3075 vẫn chưaDev. Không gọi các tập giao nhau này là các bài độc lập để cộng tổng. Incident artifact lịch sử giữ PARTIAL, không phục dựng raw.

## File, lệnh và bàn giao

Applicationdelta: `src/MigrationCli/SourceSrcset.cs` (mới), `SourceParser.cs`; tests:`tests/LandPortal.Tests/SrcsetParserTests.cs` (mới), `Program.cs` thêm `--srcset-only`; runner mới `scripts/test-source-srcset.ps1`. **0migration**. Privateharness/scripts dưới `.local/phase3l-approved-*`, `.local/phase3l-srcset-*`; không chuyển thành featureProduction hoặc tăng quyền.

**Kiểm tra focused cuối:** `srcset-tests-20261022T045614Z-3a188b6f`, build0warning/0error,4/4PASS,0codeDrift,0SQL/network. Đây là lần kiểm tra cuối thay thế kết quả4/4 trước đó khi nói về parser hiện hành; không cộng thành8nhóm. Runner dưới đây tạo run/build mới mỗi lần, không copy fixture hoặc mở database:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-source-srcset.ps1
```

Các lệnh **đã chạy**, artifact đã có nên **không chạy lại vào cùng run**:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3l-approved-init.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3l-approved-backup-v2.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3l-approved-apply.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3l-approved-repeat.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3l-approved-http.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3l-srcset-build-test.ps1 -Tag v2
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3l-srcset-scan.ps1 -Tag v3
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3l-srcset-test.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3l-srcset-http.ps1
```

Smoke thực ở`http://127.0.0.1:5394`Dev và`:5395`Test; cả hai temporaryprocess đã dừng, không dừng preview cũ. Ví dụDev đã kiểm:`/bai-viet/621c10c9-93dc-42cd-a61d-4415306cfbea`. Để mởDev mới bằng lệnh chuẩn của dự án: `rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -Port 5394` (khôngAllowUnscannedMedia); lệnh này chưa được dùng thay cho smoke ở trên.

Không sửa.env,Identity/quyềnDev,SQLmetadata/timeout,Production,DNS; khôngcommit/push/crawl/email. Còn chờ: policyPDF48MiB, host/mapping/file lỗi, Devpacket3075 exactapproval,34gap gốc/53scannerapplicationauthority, Production scanner/SMTP/MFA/target/UAT/Word/privateLookup và các điều kiện vận hành. **CHƯA PRODUCTION READY.**
