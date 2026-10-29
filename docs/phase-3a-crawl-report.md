# Phase 3A — báo cáo triển khai, 16/10/2026

**Tổng thể PARTIAL.** Pipeline kiểm kê và thu thập mẫu giới hạn đã chạy trên SQL Server Test; quét diện rộng **NEEDS_APPROVAL**, tổng nguồn **UNKNOWN**. Website **CHƯA PRODUCTION READY**. [Danh mục và số liệu](phase-3a-source-inventory.md), [runbook](phase-3a-runbook.md).

## Kết quả và phạm vi

Đã thực hiện Discover → Fetch → Snapshot → Extract → Download → Normalize → Stage → Validate bằng dữ liệu [QLDD](https://qldd.mae.gov.vn/). Session `9d95c26f-85a5-4a8a-a2df-61def03cf360` có512 URL,270 danh tính ứng viên,40 danh tính đã kiểm chứng (37 cũ +3 thêm),67 file URL và62 tệp hợp lệ private. Trong đó7 ảnh tải mới được quét engine thật Clean;55 bản sao cache vẫn NotScanned. Không nhập/publish vào CMS, không thay37 nội dung hoặc53 tài nguyên nguồn. Video vẫn2 metadata/0 file.

39 collector GET có39 phản hồi hoàn tất,125 capture dùng lại,164 capture/160 snapshot SQL/512 CrawlItems. Browser khảo sát nguồn có20 GET riêng, kết quả PARTIAL. Báo cáo JSON/CSV/HTML ở `.local/evidence/phase3a/inventory-20261015T183702Z-7be2669b`; ảnh nguồn thật và rendered DOM ở `.local/evidence/phase3a/browser-20261015T180204746Z-87eaf5bd`. Ảnh regression desktop/mobile thuộc `.local/evidence/phase3a/phase3a-tests-20261015T182634Z-0cf9b9da`, tách khỏi bằng chứng nguồn. Không tạo giao diện public mới cho Phase3A; HTML kiểm kê là báo cáo dữ liệu local.

Báo cáo kiểm kê đã mở thật bằng Edge ở390/1440px:19 hàng gồm header và18 mục, không tràn body, không request ngoài hoặc lỗi JS. [Ảnh mobile](../.local/evidence/phase3a/phase3a-20261015T172420Z-1ddfed6d/inventory-ui-1792089538181-99344c8e-8b33-4dd7-abf1-78bb085933f5/inventory-390.png), [ảnh desktop](../.local/evidence/phase3a/phase3a-20261015T172420Z-1ddfed6d/inventory-ui-1792089538181-99344c8e-8b33-4dd7-abf1-78bb085933f5/inventory-1440.png). Đây là hai kiểm tra report riêng, không cộng vào129 nhóm full.

Readback SQL riêng xác minh pointer checkpoint SHA,512 CrawlItems/160 snapshot/0 ImportRuns. Checkpoint schema3; metadata SQL snapshot giữ123 hàng prototype schema1 và37 hàng schema3, không ép đồng loạt thành3 hoặc tăng số hàng. Cả164 capture đúng SHA/độ dài trên đĩa. Phản hồi304 thật trong mẫu này là0; conditional304 được chứng minh bằng FixtureTransport, không gọi là nguồn đã trả304. HTTP header bảy ảnh khai báo PNG trong khi bytes là JPEG; validator xác định theo bytes, raw header vẫn được bảo toàn.

## Triển khai

Tái sử dụng SourceParser, HtmlPolicy, MediaService, OfficeDocumentPolicy, InquiryScannerFactory và **SqlImporter.PersistCrawlAsync**. Không gọi ImportAsync để tạo lại nội dung. Dùng các bảng hiện có CrawlRuns/CrawlItems/SourceSnapshots/ImportIssues; **không có migration mới**, không migrate Dev.

Checkpoint lưu content-addressed SHA-256, fsync, Brotli `LP3A-BR1`; giới hạn128MiB sau giải nén, vẫn đọc JSON prototype. Tệp private được ghi trước transaction SQL Serializable cập nhật pointer/trạng thái. CLI file lock + SQL session applock + expected-pointer check ngăn ghi đè đồng thời. Raw/checkpoint cũ không bị xóa; file orphan giữ riêng. Prototype chưa nén đã tốn khoảng633MB, được bảo toàn; format mới giảm tăng trưởng. Dung lượng trống dưới512MiB chặn ghi, chưa nghiệm thu restore drill.

Reservation bền vững trước mỗi GET, kể cả redirect; giới hạn cộng dồn40 GET khi chưa có approval. Một kết nối, nghỉ thận trọng ít nhất2 giây, backoff/RetryAfter và tối đa3 attempt. Resume đọc lại SQL cutover trước request, phục hồi Fetching thành Retry có ghi nhận kết quả cũ chưa rõ. Conditional GET dùng validator/cache đã kiểm hash. Không có fallback từ offline sang HTTP, không dò ID tuần tự, không login hoặc POST nguồn. URL/admin/private/network/reparse-path đều có guard. Bản ghi approval chỉ ghi nhận hồ sơ ngoài đã được người vận hành kiểm tra; không tự tạo, không xác minh chữ ký pháp lý tự động.

Kho `.local/phase3a/9d95c26f85a54a8aa2df61def03cf360` ngoài static root. Exact-byte scanner chỉ chọn file network mới có định dạng hợp lệ, không chọn cache hoặc53 original. Bảy ảnh có Microsoft Defender1.1.26090.9, signature1.461.41.0; proof/hash lưu riêng. Không đổi cloud/AV/firewall/service policy. Không có route public hoặc MediaAsset mới để lộ tệp staging. Adapter DOC/XLS cũ/video lớn chưa hoàn thiện; báo lỗi thay vì gọi đã chuyển.

## File thay đổi

So với code manifest V05-Finish123/123 trước lượt này:

- `scripts/phase3a.ps1` (new).
- `scripts/test.ps1` (modified).
- `scripts/test-phase3a.ps1` (new).
- `src/Infrastructure/OutboxReconciliationService.cs` (modified).
- `src/MigrationCli/InventoryCommand.cs` (new).
- `src/MigrationCli/InventoryDiscovery.cs` (new).
- `src/MigrationCli/InventoryModel.cs` (new).
- `src/MigrationCli/InventoryProcessing.cs` (new).
- `src/MigrationCli/InventoryReport.cs` (new).
- `src/MigrationCli/Program.cs` (modified).
- `src/MigrationCli/SafeSourceClient.cs` (modified).
- `src/MigrationCli/SourceInventory.cs` (new).
- `tests/browser/finish-check.mjs` (modified).
- `tests/browser/source-inventory-check.mjs` (new).
- `tests/LandPortal.Tests/InquiryFinishHttpTests.cs` (modified).
- `tests/LandPortal.Tests/InventoryTests.cs` (new).
- `tests/LandPortal.Tests/Program.cs` (modified).
- `tests/LandPortal.Tests/TestEvidencePaths.cs` (modified).

AGENTS và các tài liệu bàn giao cũng được cập nhật. Danh sách SHA từng file tại `.local/evidence/phase3a/phase3a-20261015T172420Z-1ddfed6d`/`changed-code.json`. Không thay kiến trúc, package, .env, Dev Identity hoặc quyền vận hành.

## Kiểm thử thực

Full regression sau sửa code/script cuối: **129/129 PASS, exit0**, run `phase3a-tests-20261015T182634Z-0cf9b9da`; build Web/Worker/CLI/Tests0 warning/0 error. Frozen runner kiểm hash code và bảo toàn raw cũ; số liệu cuối đọc từ run.json/sql-tests.json, không lấy123/123 cũ hoặc cộng các lần chạy. Sáu nhóm3A mới gồm URL/robots/XML;37 canonical thật và next/last; SQL offline/resume/dedupe/noCMSwrite; retry/budget/conditional cache; tệp hỏng/tamper/stale writer; target/cutover. SQL, filesystem, web/worker và scanner là thật; lỗi429/network/304 dùng FixtureTransport ghi nhãn, không giả làm bằng chứng lỗi mạng nguồn.

HTTP/UI/worker là bằng chứng con của full, không cộng thành suite mới. Kiểm tra thật desktop/mobile gồm admin/public/CAPTCHA/media/Word/menu/search/inbox/video; xem JSON và PNG theo thư mục trong run. Default Clamd unavailable là kiểm thử fail-closed có scannerIntegration BLOCKED riêng; nhánh DefenderTest thật Clean/EICAR/worker/ACL chạy riêng trong cùng full. Không dùng scanner mock để đạt PASS.

Các lần không đạt được giữ nguyên:

- `phase3a-tests-20261015T174703Z-efddf690`: console4 PASS/2 FAIL rồi harness abort, không có structured final JSON. Guard đường dẫn evidence chưa nhận Phase3A.
- `phase3a-tests-20261015T174835Z-ac8aff01`:5/6; đã sửa retry dùng lại cache429 sai.
- `phase3a-tests-20261015T175202Z-1742987e`:6/6, trước các sửa bổ sung; không phải full code cuối.
- `phase3a-tests-20261015T180627Z-5d08bdca`:126/129, ba lỗi receipt Test do allowlist raw chỉ nhận phase2c/phase2d. Đã thêm phạm vi phase3a, giữ guard SQLTest/.eml/permission/hash và SMTP sink.
- `phase3a-tests-20261015T181623Z-aa1ec4e0`:127/129, hai lỗi browser lịch. Fixture cũ dùng CreatedUtc2098 nên danh sách50 bản gần nhất bỏ bản2026. Không xóa dữ liệu Test hoặc đổi nghiệp vụ; kiểm tra mới chờ POST302, dùng policy GUID riêng và đọc SQL độc lập owner/ngày/temporary/hai ngoại lệ. Full cuối mới là kết quả nghiệm thu sau sửa này.

Parser canonical article/legal/draft/video và category-pN đã được sửa theo raw thật; derived inventory dựng lại offline, raw/checkpoint/lỗi prototype vẫn giữ. Không sửa nguồn hoặc dùng AI viết lại.

## Bảo toàn

Trước lượt:815 tệp manifest cũ không drift;17 nhóm source không khác. Sau sửa và full cuối: `source-after-final.json`, `identity-after-final.json`, `preservation-final.json` trong `.local/evidence/phase3a/phase3a-20261015T172420Z-1ddfed6d`. Giữ37 nội dung/47 revisions/74 aliases,53 tài nguyên (41 ảnh/12 PDF) **NotScanned**,70 capture/68 snapshot,8 ngoại lệ,2 metadata/0 file;17 nhóm0 khác. Dev Identity/.env/SRS giữ nguyên. Manifest lịch sử815 tệp còn nguyên:804 tệp vẫn đúng hash,11 file code/hướng dẫn hiện hành thay đổi có chủ đích trong phạm vi lượt này; raw artifact cũ0 thay đổi ngoài dự kiến. Không gọi toàn bộ815 hash hiện tại còn giống trước khi sửa code. Danh sách chi tiết ở preservation-final.json, không cập nhật ngược seal lịch sử. Không có schema migration, Dev vẫn migration hiện có20261015091551. Số liệu staging Test không cộng vào baseline Dev.

## Trạng thái nghiệm thu

| Tiêu chí | Trạng thái | Bằng chứng/giới hạn |
|---|---|---|
| Kiểm kê/thu thập mẫu có giới hạn, raw/hash/SQL checkpoint/dedupe/resume/retry | PASS LOCAL | Session thật +6 nhóm mới trong full129 |
| Bảy ảnh mới scan đúng byte | PASS LOCAL | Defender thật, private; không áp53 original |
| Bảo toàn và regression phạm vi code cuối | PASS LOCAL |129/129;17 nhóm0 khác; manifest readback |
| Quét toàn nguồn | NEEDS_APPROVAL | Chưa có hồ sơ phê duyệt rộng |
| Tổng toàn nguồn/83/81/cơ cấu/chỉ đạo | PARTIAL / NEEDS_SOURCE | Browser partial, thiếu export/tổng đối chứng |
| Tệp gdla/video/DOC cũ và adapter còn thiếu | BLOCKED / PARTIAL | Ngoài scope hoặc chưa hỗ trợ;0 video file |
| Collector kill giữa response, restore Stage3A, import/publish batch | NOT RUN | Không thay bằng test new-context hoặc fixture |
| SQL timeout | MONITORING / ROOT CAUSE UNKNOWN | Không tăng timeout hoặc stress Dev |
| Incident raw V04/D15/v05 lịch sử | PARTIAL | Không phục dựng hoặc đóng giả |

Các gate cũ PO group2/tra cứu riêng tư/ba Word nghiệp vụ/Production scanner/MFA/SMTP/restore/UAT vẫn riêng. Không email thật, .env, Dev grants, commit/push/deploy/DNS, AV/service/firewall hoặc full crawl không duyệt.

## Tiếp tục

Bộ bàn giao được khóa tại `.local/evidence/phase3a/phase3a-20261015T172420Z-1ddfed6d/manifest-final.json`; kiểm hash độc lập ở `manifest-readback.json` cùng thư mục. Manifest bao gồm code cuối, tài liệu, artifact Phase3A và kho checkpoint/raw của session thật; không sửa manifest lịch sử. `summary.json` và bản tham chiếu `docs/evidence/phase3a/phase3a-20261015T172420Z-1ddfed6d/summary.json` ghi run ID và giới hạn.

Lệnh Report/Resume/Test trong [runbook](phase-3a-runbook.md), dùng lại đúng RunId. Không tạo session để lách quota. Có phê duyệt thì mở batch thận trọng, bổ sung public adapter/export đối chứng và giữ mọi ngoại lệ. Trước Phase3B cần backup gồm SQL **và `.local/phase3a`**, quyền batch rõ ràng, quyết định scan/ACL, đối chiếu revision và alias; chưa import hàng loạt hay cutover trong lượt này.
