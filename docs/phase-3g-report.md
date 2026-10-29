# Phase 3G — Approved Dev data import

**BLOCKED trước Apply. Chưa nhập dữ liệu Dev. CHƯA PRODUCTION READY.**

Ngày 19/10/2026, run `phase3g-20261019T092316Z-24fe1477`. Bằng chứng mới, không ghi đè run cũ: `.local/evidence/phase3f/phase3g-20261019T092316Z-24fe1477/`; pointer `.local/phase3g-current.json`. Dùng namespace evidence `phase3f` hiện được CLI cho phép; không sửa allowlist hoặc công cụ bảo mật để chạy mốc mới.

## Phê duyệt và ba blocker thực tế

Chủ dự án/người dùng trong phiên chat đã **USER_CONFIRMED** việc nhập batch nguồn có manifest hợp lệ từ Test vào `MinhAnh\SQLEXPRESS / LandPortal_Dev`, với backup, đúng đích, bảo toàn và verify/rollback. Không yêu cầu lại phê duyệt kinh doanh chỉ vì thiếu một tệp xác nhận riêng. `scope.json` chỉ ghi nhận yêu cầu thật, không phải tài liệu ký hoặc tự cấp quyền.

1. **BLOCKED_TARGET_IDENTITY_MISMATCH.** Truy vấn trực tiếp cùng connection ` .\SQLEXPRESS` trả `@@SERVERNAME = THANGPD\SQLEXPRESS`, nhưng `SERVERPROPERTY('ServerName') = MinhAnh\SQLEXPRESS`, `MachineName = MinhAnh`, Windows `MINHANH`, instance `SQLEXPRESS`, transport `Shared memory`. `DB_NAME()` và `Sql__ExpectedDatabase` đều là `LandPortal_Dev`. Điều kiện người dùng yêu cầu `@@SERVERNAME` đúng tên đã duyệt chưa đạt. Có thể là metadata tên đăng ký cũ, nhưng chưa xác minh nguyên nhân; không tự sửa đăng ký server, cấu hình, alias, `.env` hoặc dùng giá trị khác để bỏ kiểm tra. Báo cáo cấu hình ở lượt trước chỉ kiểm `SERVERPROPERTY`, chưa kiểm `@@SERVERNAME`; cần đọc hai giá trị riêng biệt. Xem `target-preflight.json`.
2. **BLOCKED_EXTERNAL_SIGNATURE_OR_TRUST_POLICY.** `SafeImport__ApprovedSignerThumbprints` có 0 signer; `.local/approvals/phase3c` chưa tồn tại, 0 tài liệu approval Authenticode. `ApprovedBatchExecution.CreateAsync` bắt buộc signer trong policy và chữ ký `Valid`, exact plan/target/operator/action/expiry. Phê duyệt chat không thay các điều kiện này theo chính yêu cầu 3G. Không tạo cert, self-sign, pin trust, grant hoặc giả lập signed approval. Xem `signer-preflight.json` và [code kiểm quyền](../src/MigrationCli/ApprovedBatchExecution.cs).
3. **BLOCKED_PROOF_REVALIDATION.** Đã chạy đúng một dry-run chỉ đọc bằng CLI hiện hành với checkpoint/plan dependency SHA. CLI exit 1 trước khi tạo plan: `Stale/different engine proof; scan again.` Proof recovery còn hạn 24 giờ và 60 tệp proof đúng hash, nhưng dùng engine `1.1.26090.9`, definitions `1.461.96.0`; Defender hiện có cùng engine, definitions `1.461.102.0`, cập nhật `2026-10-19T03:22:45Z` (10:22:45 giờ Việt Nam). Policy bắt buộc khớp engine/definitions hiện tại, không chỉ TTL. Không sửa proof, đổi Clean, tắt overlay hoặc scan original Dev53. Xem `dev-preview-priority.log`, `dev-preview-priority-exit.json`, `scanner-proof-preflight.json`.

Đã dừng đường Apply theo yêu cầu sai đích. Chỉ tiếp tục các bước metadata/dry-run/đối chiếu không ghi dữ liệu. **Backup mới, Apply, positive signed path, verify sau Apply, repeat/idempotency, rollback và smoke dữ liệu mới đều NOT RUN**, không phải PASS. Không chạy lại preview mở rộng vì cùng dependency recovery đã bị từ chối; không retry mù.

## Manifest và phạm vi dữ liệu

Đã kiểm lại 65 dependency của manifest ưu tiên Phase3F: **0 khác hash**; 7 ảnh nguồn thật đúng byte/hash/dung lượng. Đây là bảo toàn artifact, **không làm proof scanner trở lại hợp lệ**.

Plan lịch sử schema3 `965727ba-89e6-4c60-99e8-fbf948aecc9a`, SHA `fd8870b9977d890419b66acea06f7850ddd9340ba0ee593fbbfefc2faf6e8e04`, có số dự kiến sau; không gọi là kết quả dry-run 3G mới:

| Phạm vi lịch sử đã đối chiếu artifact | Số lượng |
|---|---:|
| Nội dung mới | 3 |
| Danh tính Dev đã có | 37; trong đó 6 Keep, 1 comparison đủ điều kiện ở lần preview cũ, 30 Blocked |
| Revision dự kiến thêm | 4: 3 bài mới + 1 source-comparison, không đổi public/editorial cũ |
| Tài nguyên dự kiến thêm | 7 JPEG |
| URL mapping trong phần đủ điều kiện | 20; delta alias dự kiến 6 |
| Nội dung còn chặn trong cohort40 ở preview cũ | 30 |

Fresh Dev plan/count hiện tại **chưa có**, vì dry-run bị proof gate chặn. Phải preview lại sau xử lý đích và proof, ký đúng SHA mới; không sửa plan cũ hoặc dùng chữ ký cho một SHA khác.

Đối chiếu 210 `ContentId` phân biệt có `CreatePublishedTest` trong hai batch thật `30f5f690-0920-4580-8659-7a42193f1e88` (169) và `869e46ee-815e-4304-a72c-0bb27a9d3cc3` (41): **Test đủ 210 content + 210 SourceRecord của qldd; Dev có 0 trong tập này**. Không lấy toàn bộ hàng Contents Test hoặc technical fixture làm đầu vào Dev. Batch mở rộng cần preview Dev và manifest target-bound riêng, không đổi tên target trong plan Test. Xem `source-batch-readback.json`.

## Schema, transaction và bảo toàn

- Cả hai DB có cùng 10 migration, mới nhất `20261015091551_V05EditorialDocuments`; không migration mới.
- Dev không có receipt `Phase3B.*`/`Phase3C.*`; 5 ImportRuns lịch sử đều `SampleImported`, không có Running trong kết quả đọc. Tại thời điểm lấy metadata không có user read/write transaction trong DB theo `sys.dm_tran_database_transactions`; transaction count phiên kiểm tra bằng 0. Đây là ảnh chụp metadata, không thay mutex/reconciliation ngay trước một Apply tương lai.
- Trước/sau lượt: **37 Contents, 37 SourceRecords, 47 Revisions, 74 aliases; 53 tài nguyên vẫn NotScanned (41 ảnh/12 PDF)**. **0 sai khác/17 nhóm**. Đủ 70 capture tương ứng 68 SQL snapshot, 8 ngoại lệ, 2 video metadata/0 tệp video. Media/snapshot đúng hash, không mất file.
- Digest Identity/password/stamps/roles/claims bằng checkpoint 3F và không đổi trước/sau lượt; `.env` giữ nguyên SHA. Không xuất secrets. Xem `dev-before.json`, `dev-after.json`, `identity-before.json`, `identity-after.json`.
- SQL category timeout vẫn **MONITORING / ROOT CAUSE UNKNOWN**; khác biệt tên server không được tuyên bố là nguyên nhân timeout. Incident artifact lịch sử V04/D15/v05 vẫn PARTIAL, không phục dựng.

## Kiểm tra thực chạy

| Vùng | Kết quả |
|---|---|
| Metadata target | BLOCKED: hai tên SQL khác nhau như trên |
| Signer/trust inventory | BLOCKED: 0 signer/0 signed approval |
| Dry-run ưu tiên bằng CLI | BLOCKED: exit1, scanner proof khác definitions; không sinh plan mới |
| Schema Dev/Test | PASS đối chiếu 10 migration giống nhau |
| Nguồn Phase3F trên SQL | PASS readback đúng 210 trên Test/0 trên Dev |
| Artifact ưu tiên | PASS 65 dependency/7 ảnh đúng hash |
| Bảo toàn Dev/Identity/env | PASS: 0 khác/17 nhóm, digest/env nguyên |
| Build/full137/video/UI toàn site | NOT RUN: không sửa code ứng dụng, không chạy kiểm thử không liên quan |
| Backup mới/Apply/verify/repeat/rollback/smoke mới | NOT RUN: chưa đủ điều kiện đích và ký/quét |

Các kiểm tra metadata không được cộng thành suite regression. Không có PASS nghiệm thu Apply 3G; lỗi CLI được giữ nguyên trong raw log, phân loại blocker policy, không báo thành công giả.

Thay đổi lượt này chỉ gồm tài liệu `AGENTS.md`, `PROJECT_STATE`, `acceptance-matrix`, báo cáo/runbook 3G và helper/evidence private `.local/phase3g-*`. 17 đường code/script nằm trong seal 3F đã đối chiếu không drift; không đổi source ứng dụng, SQL schema/index/config/timeout, Identity, `.env`, không build lại hoặc chạy migration/crawl/email/deploy/push.

## Tiếp tục

Đọc [runbook 3G](phase-3g-runbook.md): cần người quản trị SQL xử lý/xác nhận khác biệt tên đăng ký theo quy trình phù hợp; cần authority cung cấp signer/trust policy và signed approval thật; refresh proof trên **bản Test được phép**, giữ Dev53 nguyên. Sau đó fresh read-only preview từng batch trên Dev, backup SQL/media mới + VERIFYONLY/header/hash/readback, ký exact plan, Apply checkpoint → verify → idempotency → smoke → đối chiếu. Không tái nhập dữ liệu Test hoặc dựng lại dự án.
