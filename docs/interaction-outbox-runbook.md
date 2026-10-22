# Outbox Uncertain — delta 2C-Finish, 15/10/2026

Transport vẫn chỉ TestSink loopback/SQL Test/kho Test/Development; Dev Disabled, SMTP thật chưa triển khai. Không sửa .env, gửi thư thật hoặc cài service. Không hứa exactly-once qua SQL + SMTP; Accepted chỉ server sink đã nhận, không xác nhận người dân đọc/inbox.

Outbox dùng trạng thái Pending/Leased/Accepted/Failed/Uncertain/Cancelled: claim SQL applock, lease 2 phút, fencing, backoff/giới hạn 5 lần trước DATA. Lỗi sau DATA hoặc lease hết hạn không biết acceptance → Uncertain; worker không tự retry. Lookup OTP purpose/expiry/revoked/consumed được kiểm trước gửi, thư trả lời kiểm fresh người yêu cầu/read/respond/phạm vi. Lỗi thư không rollback câu trả lời/hồ sơ.

`/admin/outbox` đếm và paging SQL các dòng Uncertain, metadata Message-ID/trạng thái/lần thử/mã lỗi, không payload/OTP/email nguyên văn. Cần outbox.reconcile; thư gắn hồ sơ còn phải thuộc phạm vi inbox được đọc. Refresh/GET không tạo thư mới. Trực ca phải xem hàng đợi và phân người đối soát; chưa có cảnh báo hạ tầng hoặc escalation Production tự động.

Tại `/admin/outbox/{id}`, lưu quyết định với rowversion + command + bằng chứng:

1. **accepted**: receipt .eml thật ở kho sink Test/evidence phase2c, đường dẫn tương đối. Server chặn traversal/reparse/ngoài project, kiểm Message-ID/To/body bằng payload mã hóa đã gửi và ghi SHA256 receipt. Không có receipt đúng thì từ chối; không ghi Sent bằng khẳng định trống. Receipt xác nhận sink lưu mail, không chứng minh người nhận đã đọc.
2. **not-sent**: người có trách nhiệm đối soát xác nhận trước khi DATA đã gửi, căn cứ bắt đầu NOT_SENT_CONFIRMED: kèm log/diễn biến. Hệ thống lưu bằng chứng mã hóa và audit; không tự suy luận chưa gửi khi ACK bị mất. Tạo event mới PENDING, SupersedesId trỏ thư cũ, thư cũ Cancelled; worker vẫn kiểm quyền/OTP khi thực chạy.
3. **unknown**: giữ Uncertain, lưu audit, không resend. Nếu chưa đủ chứng cứ, chọn mục này.
4. **resend-risk**: quyết định có chủ ý sau đối soát, bắt buộc checkbox xác nhận nguy cơ mail trùng và căn cứ; tạo đúng một event mới, thư cũ Cancelled, audit risk=true. Không dùng nút này thay retry mù.

Retry cùng command/payload trả cùng quyết định/event; command dùng với payload khác bị từ chối. Concurrent rowversion stale trả409, tải lại; không sửa payload cũ. Evidence nguyên văn được mã hóa SQL, chỉ audit metadata/receiptHash. Không đưa OTP, email người dân hoặc password vào căn cứ/report. Production cần provider mail log/receipt tích hợp thật; nhánh accepted hiện chỉ SQL Test sink, không tự nhận receipt Production.

Kiểm thử thật: TCP sink nhận DATA rồi cắt kết nối trước ACK; row Uncertain không bị gửi lại, operator403 bị chặn, accepted đối soát receipt/idempotency, unknown không retry, not-sent cho tình huống crash trước transport, resend-risk cần xác nhận và có mail sink mới. Worker test riêng/real child process, không SMTP giả trả success. Raw .eml/fixture credential trong .local ignored.

Chạy: `rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-interactions-test.ps1 -Finish -Component Worker -NoBuild` (terminal sink riêng trên2525); -Once xử lý một lượt. Không chạy mail worker trên Dev. Scheduler độc lập giữ2B, scanner là nhánh --scan riêng/Test-only, xem runbook scanner.

SQL timeout vẫn MONITORING / ROOT CAUSE UNKNOWN, raw smoke2B V04 đã mất vẫn PARTIAL. Chưa scanner thật, SMTP Production/TLS/rotation/alerts, MFA/restore/UAT/retention pháp lý. Toàn dự án CHƯA PRODUCTION READY.

---

Bản hướng dẫn checkpoint 2C trước Finish (giữ lịch sử; phần mới phía trên có hiệu lực):

# OTP và outbox Test — 2C PARTIAL

Transport mặc định Disabled; Dev form báo chưa có xác minh và không gửi thành công giả. TestSink được khóa tại startup: Development, LandPortal_Test, base URL loopback, media/temp/keys dưới `.local/tests`, cổng cao tường minh. Staging/Production/Dev DB hoặc transport khác bị chặn; không có SMTP credential/provider và không relay ra ngoài. Worker mail chỉ dùng TCP loopback. SMTP thật/rotation secrets/TLS/alerts chưa triển khai.

Chạy `scripts/test-phase2c.ps1 -Region Interactions` cho E2E tự sở hữu sink/web/child worker và dừng chúng, giữ artifact unique. Chạy thủ công bằng ba lệnh `scripts/run-interactions-test.ps1 -Component Sink|Worker|Web` (mỗi terminal một process); mặc định web5219/sink2525. Không cài Windows service; Ctrl+C dừng. Kho source fixture Test sao chép từ snapshot có sẵn, không fetch/import Dev. Credential Test của E2E ở private `interactions-http/browser-fixture.json`, không đưa vào chat/report.

Env process được script đặt: `Mail__Transport=TestSink`, `Mail__SinkPort=2525`, `DOTNET_ENVIRONMENT=Development`, `Sql__ExpectedDatabase=LandPortal_Test`; connection được common.ps1 nạp/kiểm đúng target, không in chuỗi. Web có cùng Data Protection application name/keys với worker. Giữ SQL + keys khi backup; keys thiếu là blocker giải mã/challenge. Không sửa .env hoặc dùng keys/SQL của dự án khác.

OTP6 số/5phút/5 sai; resend>=60s; 5/email/giờ bền SQL; POST30/IP/phút local process, không lưu IP thô. CSRF/honeypot/64KB request/8000 ký tự. Payload/cookie-session/email/purpose/target/revision phải giữ đúng; sửa nội dung cần mã mới. Không có endpoint trả mã; raw OTP chỉ trong mail sink ignored, không log. Cookie HttpOnly/SameSite Strict; production HTTPS/abuse policy còn nghiệm thu.

Worker claim SQL/applock, lease2phút, gửi15giây, backoff30/60/120/240s…giới hạn900s; tối đa5 attempts nếu lỗi trước DATA. `Accepted` sau250, không đồng nghĩa người nhận đọc. OTP đã revoked/consumed/expired bị Cancelled. Thư trả lời kiểm tra lại user active/read/respond/scope trước gửi. Không đổi trạng thái nghiệp vụ khi SMTP lỗi.

Mất ACK sau DATA hoặc worker chết với lease không rõ acceptance: `Uncertain`, không retry tự động. Kiểm Message-ID trong sink bằng công cụ local và SQL target; **chưa có UI/CLI reconciliation/requeue**, không sửa trực tiếp thành Accepted/Failed để giả kết quả. Heartbeat/alerts/retention và gửi thật cần triển khai/approval vận hành riêng. Không hứa exactly-once qua SQL+SMTP khi không có transaction chung.

Raw `.eml`, credentials và snapshots đầy đủ nằm ngoài publish/git. Tệp người dân/scanner chưa triển khai ở checkpoint này; không chuyển source NotScanned thành Clean hoặc đưa upload vào wwwroot. File/range/quarantine cần làm trước khi nghiệm thu hỏi đáp đầy đủ.
