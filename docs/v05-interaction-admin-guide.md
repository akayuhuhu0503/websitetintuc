# Biểu mẫu v0.5 và hòm thư

Public dùng câu hỏi CAPTCHA toán bằng chữ, sinh trên máy chủ, hỗ trợ bàn phím/trình đọc màn hình; không cần Internet hay đăng ký công dân. Mỗi challenge ràng buộc phiên/địa chỉ đã HMAC/loại/bài/revision,5phút,mộtlần; sai tạo câu mới. SQL giới hạn mặc định5lần xác thực/10phút/IP, cấu hình `Captcha__AttemptsPer10Minutes`(1–20), `Captcha__LifetimeMinutes`(1–10). Cân nhắc NAT và bot giải toán; chưa thay đánh giá chốngspam/ATTT production. Khóa HMAC riêng lưu private trong KeysRoot, giữ cùng backup/ACL; không log IP/answer/key/token. `Captcha__TestScope` chỉ SQLTest+Development, ngoài đó startup từ chối; không phải bypass đáp án.

`/tuong-tac/Comment/{bài}`, `/tuong-tac/Inquiry`, `/tuong-tac/Contact`, `/tuong-tac/Consultation/{dự-thảo}` gửi thẳng qua CAPTCHA→SQLPending/private. Endpoint OTP gửi mới trả410, không xóa bảng/hồ sơ/audit OTP cũ. Sau khi SQLcommit hiển thị mã tiếp nhận; mail chỉenqueue khi có transport được phép. “Đã xếp hàng” không phải “đã gửi”. Chỉ TestSink được bật theo guard2C; chưa SMTPthật.

Liên hệ và hòm thư góp ý là **một luồng Contact**. Họ tên/email/tiêu đề/nội dung bắt buộc; điện thoại tùy chọn; mộtPDF/JPEG/PNG≤10MiB. Tệp lưu riêng/cách ly và gắn hồ sơ; tệp sai/failedscan không được tải. Form lỗi phải chọn lại tệp. Giới hạn kho/quota/GC/scanworker2C được tái sử dụng, không hotlink hay public URL. Chưa có scanner Production cho arbitraryPII.

Admin inbox lọc theo trạng thái, phân trangSQL20. `interaction.contact.list` chỉ metadata; `interaction.contact.read` mới đọc PII; `interaction.contact.process` xử lý/ghi chú nội bộ có concurrency/audit. `interaction.contact.file.read` kèmread mới tải tệpCleanđúngbyte. Excel tối thiểu cần `interaction.export`+contact.read; cóPII cần thêm`interaction.contact.export.pii`; XLSX ghi cellinlineString để không chạy công thức. Không tự grantDev các quyền mới. Inquiryassignment/SLA/reply/batch/outboxUncertain theo guide2C giữ nguyên.

Tra cứu riêng tư hiện khóa theo quyết định tạm thời của người dùng; mã+email+CAPTCHA không cấp quyền. [Decision](decision-required-private-lookup.md). Admin vẫn xử lý theo quyền. Mọi bản public chỉ qua snapshot kiểm duyệt, không tự đưa originalPII/file/ghi chú ra ngoài.
# Cập nhật V05-Finish

Đăng nhập và xác thực lại editor nay đều yêu cầu CAPTCHA SQL một lần, mục đích riêng; Identity/lockout/CSRF giữ nguyên. Tìm kiếm chung tích hợp chỉ snapshot hỏi đáp đã duyệt và còn đồng ý công khai. [Hướng dẫn hiện hành](v05-finish-admin-guide.md). Tra cứu hồ sơ riêng tư vẫn khóa chờ PO/ATTT; không mở bằng CAPTCHA hoặc mã/email. Nội dung dưới giữ lịch sử v0.5 delta và hướng dẫn bốn luồng đã có.
