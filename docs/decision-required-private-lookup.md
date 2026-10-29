# Tra cứu hồ sơ riêng tư — NEEDS_PO / SECURITY RELEASE GATE

v0.5 bỏ OTP khi gửi biểu mẫu. CAPTCHA chỉ chống gửi tự động, không xác minh email hoặc sở hữu hồ sơ. Mã tiếp nhận hiển thị màn hình/email có thể được chia sẻ; ghép mã này với email không cấp quyền xem câu trả lời riêng. Áp dụng cả hồ sơ cũ.

Trong delta hiện tại, controller `/tra-cuu-ho-so` chỉ hiện giải thích; mọi POST xin mã/xác minh trả403, GET kết quả không truy vấn hồ sơ và không trả dữ liệu riêng, kể cả cookie tra cứu cũ. Dịch vụ/SQL OTP lịch sử được giữ cho dữ liệu và regression nội bộ; không có endpoint mint OTP mới cho lookup/submission.

Đề xuất cần PO và phụ trách an ninh quyết định: phát riêng **bí mật truy cập ngẫu nhiên32byte** một lần khi nộp hồ sơ, tách mã tiếp nhận công khai. Chỉ lưu hash bí mật, liên kết đúng hồ sơ; không log, không đưa vào URL/referrer, không xem mã tiếp nhận là secret. Xác minh server-side, rate limit, session TTL và audit sự kiện không chứa PII; không trả original/file/assignment/internal notes. Cần chính sách mất bí mật, hỗ trợ người nhập sai email và xoay/thu hồi bí mật. Không tự cấp quyền mới cho hồ sơ chỉ có code/email cũ. Một cơ chế xác minh người nhận khác chỉ mở khi có quyết định được lưu và test tương ứng.

Người dùng xác nhận trong lượt15/10/2026: giữ khóa chi tiết public; chỉ đề xuất secret đủ mạnh/hash/revoke/rate-limit, chưa bật khi PO và ATTT chưa duyệt. Admin tiếp nhận/phân công/xử lý/trả lời tiếp tục theo quyền. Chưa triển khai secret access để tránh tự phê duyệt [GĐ]. Chủ trì quyết định theo SRS: PO/đơn vị nghiệp vụ và phụ trách an ninh, chưa có người được chỉ định. Deadline trước UAT/cutover; không waiver riêng tư bằng CAPTCHA. Public hỏi đáp vẫn chỉ dùng snapshot đã kiểm duyệt và loại PII.
