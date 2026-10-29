# Delta 2C-Finish — 15/10/2026

Checkpoint mới **PARTIAL, CHƯA PRODUCTION READY**, xem [báo cáo Finish](phase-2c-finish-report.md). F1 private attachment đã code/negative nhưng scanner thật BLOCKED; F2/F3/F4 ca Test PASS LOCAL. Migration20261014163306_InquiryFinish Test→Dev backupverified; nguồn0mismatch17/37/53/70capture68SQL/8/2metadata0file,53NotScanned,Identity/env nguyên. Bốn luồng core2C và2A/2B/V04 được giữ.

Full runner97/97 mới trước delta khóa gia hạn cuối; sau sửa cuối focusedFinish8/8 và HTTP6/UI35/5actions riêng. Không copy89 cũ, không cộngtestcount; code frozen242/0drift. Lịch sử checkpoint core dưới đây giữ nguyên, các “chưa có” attachment/lookup/SLA/Excel/Uncertain UI ở báo cáo cũ đã được thay bằng delta Finish, giới hạn còn lại ở report mới.

---

Lịch sử checkpoint trước Finish (giữ nguyên):

# Giai đoạn 2C — checkpoint 14/10/2026

**2C PARTIAL; CHƯA PRODUCTION READY.** Đã triển khai các luồng cơ bản thật, không khởi tạo lại 2A/2B/V04. Bình luận hoàn thành vertical slice public → OTP → SQL Pending → admin duyệt → public/ẩn. Hỏi đáp, liên hệ, góp ý dùng cùng nền nhưng giữ loại, quyền và phạm vi công khai riêng. Hỏi đáp nâng cao và các ca rủi ro chưa đủ còn ở checkpoint bên dưới; không ghi toàn mốc PASS LOCAL.

## Triển khai thực

- Public Razor form có CSRF, honeypot, validation/giới hạn payload, rate limit POST; OTP ngẫu nhiên 6 số, 5 phút, 5 lần sai, cách 60 giây, tối đa 5 mã/email/giờ. HMAC với khóa riêng challenge được Data Protection bảo vệ; email chỉ chuẩn hóa domain, giữ local-part. Challenge gắn hash payload/mục đích/email/session/target/published revision; resend vô hiệu mã cũ. Submit kiểm tra và tiêu thụ một lần trong transaction SQL/applock; retry cùng yêu cầu đã xác minh không sinh bản ghi thứ hai.
- Original chứa thông tin người gửi và câu trả lời riêng được mã hóa bằng Data Protection ngoài wwwroot. Public chỉ đọc snapshot immutable đã duyệt, không tự sao chép original. Bình luận/câu hỏi cần consent; liên hệ/góp ý không có đường công khai. Nội dung công khai là text Razor encode, nhãn phản hồi cơ quan riêng. Public không trả email/điện thoại/OTP/audit/original.
- Bốn inbox có số pending thật, lọc trạng thái/count/OFFSET SQL và quyền riêng; stale rowversion trả409, command ID/hash ngăn thao tác trùng. Endpoint dùng quyền SQL hiện thời và scope chính + mọi phụ; publisher không tự có quyền đọc hồ sơ công dân. Lý do xử lý được mã hóa trong audit. Duyệt tạo snapshot mới; ẩn bỏ public pointer, giữ original/history.
- Bình luận chỉ Article đang công khai và cả cờ toàn site/bài đều bật; thông báo không có bình luận. Có quản lý tiếp nhận live theo quyền, expected value/rowversion/audit. Tắt nhận mới không xóa nội dung đã duyệt.
- Góp ý kiểm tra published revision và cửa sổ server trong transaction có khóa live pointer; cuối ngày Việt Nam đóng ở đầu ngày tiếp theo. Lưu revision và deadline đã áp dụng; sửa deadline draft không đổi public, hết hạn/gỡ bị từ chối.
- Outbox SQL có event key unique, lease token, attempts/backoff, Accepted/Failed/Cancelled/Uncertain. Worker `--mail` chạy độc lập, chỉ transport TestSink loopback + Development + SQL Test + kho Test. Thư trả lời kiểm tra lại quyền người xử lý trước khi gửi. SMTP lỗi không xóa câu trả lời/nghiệp vụ. Mất ACK hoặc lease sau crash giữ Uncertain để đối soát, không tự resend rồi hứa exactly-once. Dev thiếu SMTP hiện rõ hạn chế, không bypass OTP.

## Migration, dữ liệu và backup

File chính thay đổi: Domain `InteractionEntities.cs`; Application `TextNormalizer.cs` (catalog quyền); Infrastructure `InteractionContracts/InteractionModel/InteractionService/InteractionOutboxService`, `PortalDbContext`, `RuntimeRoleInterceptor`, `AccountAdministrationService` (quyền mới nhạy cảm); migration `.cs`/Designer/ModelSnapshot. Web `InteractionsController/InteractionAdminController`, models/labels/ViewComponent, các Razor form/inbox/snapshot/reception, Article/PortalController/layout, `interactions.css`, DI/rate limiter Program. Worker Program thêm nhánh mail độc lập. Tests `InteractionTests/InteractionHttpTests/LocalMailSink`, browser2C/TestEvidencePaths; harness2A/2B/V04 chỉ route output vào run mới, không bỏ assert. Scripts backup/test/run-dev và `test-phase2c.ps1`/`run-interactions-test.ps1`, Node sink; AGENTS/docs cập nhật. `final-code-manifest.json` lưu SHA từng file; không đổi architecture/dependency/SDK/locks trong2C.

Migration **`20261014043339_InteractionModeration`** thêm 5 bảng: InteractionChallenges, InteractionSubmissions, InteractionPublicSnapshots, InteractionOutbox, InteractionCommands; FK/index/unique/rowversion. Up không UPDATE/DROP bảng nguồn. Runtime role được DENY UPDATE/DELETE public snapshot ngoài bảo vệ immutable tại EF. Migrations 2A/2B/V04 nguyên, không startup migrate/Down/reset.

Task evidence: `.local/evidence/phase2c/20261014T042052Z-fb5949aa`. Test trước: 1453 Contents/2026 Revisions/250 Media là fixture tích lũy, không dữ liệu nguồn mới. Backup Test thực254 tệp, Dev130; COPY_ONLY/CHECKSUM/VERIFYONLY, hash readback Dev trước migrate. Lượt backup Test đầu đã copy kho Dev fixture và gặp lỗi overload PowerShell5.1: giữ hồ sơ đó riêng; backup Test dùng đúng kho Test đã làm lại trước migration. **Không RESTORE**.

Dev trước/sau: **37 contents/source records,47 revisions,74 alias,53 tài nguyên (41 ảnh/12PDF),70 capture/68 SQL snapshot,8 ngoại lệ,2 video metadata/0 tệp video**. 17 nhóm identity/payload/source revision/category/alias/media/bytes/snapshot/ngoại lệ/document giữ0 mismatch. Identity/password/stamps/roles/claims và .env hash nguyên. Migration backfill search0. Không tạo hồ sơ/demonstration trên Dev, không nhập/crawl nguồn.

## Kiểm thử và ảnh

Kết quả run cuối, raw logs/hash/code state và số kiểm tra độc lập được cập nhật trong đoạn bàn giao cuối báo cáo. Không dùng83/83 V04 cũ làm kết quả2C, không cộng SQL/HTTP/UI/worker.

**Run cuối sau sửa runtime/views/test: `tests-20261014T045727Z-8e402a5c`,04:57:27–05:01:43UTC (11:57:27–12:01:43VN):89/89 runner groups PASS, exit0, locked build0warning/error, audit7projects không advisory.** 83 vùng kế thừa và6group mới thực chạy trong cùng runner;6group không là6 ca nguyên tử. 2C HTTP7checkpoints/4child worker OTP/real sink reply failure→retry; UI40 page checks,8routes×5width,1keyboard action,0JS/off-origin. CMS40/typed14/accounts18/site34/scheduler10 và V04HTTP6 là đơn vị riêng, không cộng89.

Release packages mới web/worker có manifest riêng; actual Release web6routes200 trên Test, actual Release worker--once Test-only job không tồn tại,0publicationeffect; chưa deploy. Dev preview5208 đã GET7route200 tuần tự, không stress/write. Source/Identity/env đối soát0/17 sau migration, Dev0submission/challenge/outbox/publicsnapshot/command và53NotScanned. Summary tracked: `docs/evidence/phase2c/20261014T042052Z-fb5949aa/summary.json`; raw/code/exit/hash private dưới task và test run. Live preview log có thể tiếp tục tăng khi mở trang, manifest là checksum tại thời điểm chụp.

Các run fail4/5 ban đầu giữ nguyên: MVC empty-string validation, so sánh raw HTML với text đã encode, chuẩn bị request sau khi quyền bị ẩn form, selector browser trùng nút search/login. Đã sửa implementation/harness đúng hành vi, không bỏ assert. Lần focused tiếp theo5/5 và run mở rộng đầu89/89 đã chạy thực; sau đó thêm nhãn Việt và kiểm tra thư trả lời hỏi đáp, nên có lượt regression cuối riêng.

Ảnh public/admin 390/1440 và smoke360/768/1024, HTML labels/reflow/keyboard, không JavaScript error/off-origin; raw trong từng `tests-<run>/interactions-http/ui`. Đã xem trực tiếp mobile form và desktop detail, sẽ đối chiếu ảnh bản cuối. Edge headless, chưa Safari/iOS/WCAG/UAT. Mail raw/credential fixture chỉ ignored private; không copy vào docs.

## Checkpoint còn thiếu

| Mốc | Trạng thái/phần tiếp theo |
|---|---|
|2C.1|Nền OTP/outbox/TestSink có test thật; PARTIAL về công cụ vận hành đối soát/requeue Uncertain, SMTP thật/config an toàn hoàn chỉnh, retention được phê duyệt và spam nâng cao|
|2C.2|Bình luận core vertical slice chạy thật; còn batch moderation theo từng hồ sơ, tìm/lọc nâng cao, preview trước duyệt, audit history UI và đầy đủ race/idempotency đồng thời/negative reception endpoint|
|2C.3|Hỏi đáp/liên hệ form/OTP/SQL/inbox/private/answer outbox/public redaction core; **chưa** upload một tệp riêng10MB/scanner, phân công, lookup OTP mã hồ sơ, calendar/SLA tạm10 ngày/holiday/makeup/extension, Excel chống formula và export quyền; không có nút giả|
|2C.4|Góp ý core/published window/boundary/draft isolation/gỡ chạy thật; còn race đóng hạn đồng thời thật (hiện test server-lock/boundary tuần tự), nhãn tên/hạn dự thảo trên form, lọc theo kỳ hạn/dự thảo đầy đủ|
|2C.5|Scope/quyền/rowversion/CSRF/privacy core có test; còn test revocation outbox cho thư trả lời, mass assignment/rate-limit HTTP exhaust/enumeration sâu, scanner/file/range/Excel (module chưa có), vận hành queue/alerts và chính sách pháp chế|

**Không có attachment upload/lookup/SLA/Excel giả.** Scanner thực, SMTP thật/MFA/HTTPS/restore/UAT/full migration chưa hoàn tất.10 quyền Dev cũ chưa cấp; các quyền interaction mới cũng chưa cấp Dev. SQL timeout vẫn **MONITORING / ROOT CAUSE UNKNOWN**. V04 artifact PARTIAL độc lập, raw smoke2B không phục hồi/không tái tạo; artifact mới dùng run ID.

## Lệnh và tiếp nối

Public Dev `http://127.0.0.1:5208/`, `/tuong-tac/Inquiry`, `/tuong-tac/Contact`; admin `/admin/login`/`/admin/tuong-tac` cần quyền thực. Preview V04 cũ5198 được giữ. Dev mail disabled, bình luận nguồn vẫn theo cờ gốc; không tự bật nhận trên bài nguồn.

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -Phase2C -NoBuild -Port 5208
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase2c.ps1 -Region All
# Ba terminal Test riêng (SQL Test/kho Test, không .env mới):
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-interactions-test.ps1 -Component Sink
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-interactions-test.ps1 -Component Worker -NoBuild
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-interactions-test.ps1 -Component Web -NoBuild
```

Test web5219, sink2525 loopback, worker không cài service; không mở port đang được test dùng. Tài khoản Test sinh ngẫu nhiên trong E2E, private fixture của run, không mật khẩu mặc định/reset. Xem [hướng dẫn admin](interaction-admin-guide.md), [outbox runbook](interaction-outbox-runbook.md). Tiếp theo hoàn thiện 2C.3 hỏi đáp nâng cao rồi các negative/ops còn thiếu; tái dùng schema/flow này, không dựng lại.2D video upload máy,2E/2F/Phase3/4 giữ backlog.
