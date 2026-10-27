# LAN Demo cho BA — SRS v1.2

Website trên LAN được cấp: `http://192.168.0.122:5188/`; quản trị `/admin/login`.
Đây là demo hạn chế trên máy phát triển, chưa phải UAT/Production. Không dùng dữ liệu cá nhân thật.
Tài khoản và mật khẩu cấp riêng ngoài Git; không đăng thông tin đăng nhập, khóa MFA hoặc tệp cấu hình lên repository.

Đăng nhập bằng tài khoản riêng → đổi mật khẩu lần đầu nếu được yêu cầu → đăng ký/xác nhận TOTP tại `/admin/account/mfa`.
Không reset tài khoản hoặc tắt MFA để kiểm thử. Người viết gửi duyệt; người duyệt xuất bản đúng revision; quản trị quản lý cấu hình theo quyền.

## FR-14: thử Excel thống kê

1. Sau khi hoàn tất MFA, mở `/admin/thong-ke-dat-dai` bằng tài khoản Quản trị.
2. Tải **SAMPLE/TEST hợp lệ** hoặc **SAMPLE/TEST lỗi** từ chính trang quản trị.
3. Upload lại nguyên tệp, chọn năm từ 2025, đơn vị và nguồn có nhãn SAMPLE/TEST.
4. Mẫu hợp lệ: Defender thật quét Clean → xem trước 34 tỉnh → xác nhận lưu nháp.
5. Mẫu sai: hiển thị lỗi dòng 2/cột C, không ghi dataset.

Máy LAN hiện có cloud reporting của AV: policy riêng chỉ nhận đúng hash của các mẫu công khai sinh từ source,
chặn byte không được duyệt **trong bộ nhớ trước khi ghi đĩa/scanner**. File đã chỉnh sửa/tệp nghiệp vụ khác bị từ chối.
Kiểm tra extension/MIME/XLSX/10 MiB, CSRF, quyền, audit và transaction vẫn áp dụng. Không có kết quả Clean giả.
Mọi dữ liệu SAMPLE trên LAN chỉ được lưu nháp; endpoint xuất bản bị chặn. `/thong-ke-dat-dai` tiếp tục hiển thị chưa có số liệu chính thức.
Muốn thử Excel nghiệp vụ khác cần policy riêng được phê duyệt; không tự thay đổi AV toàn máy.

## Vận hành

`powershell -NoProfile -File scripts/run-lan-demo.ps1` khởi động; `scripts/stop-lan-demo.ps1` dừng.
Giữ database `LandPortal_LanDemo`, private config/media/keys và tài khoản hiện có. Không chạy bootstrap/reseed khi nâng code.
SMTP và Worker LAN vẫn tắt. Không mở firewall hoặc đổi profile mạng nếu chưa được cấp quyền.
BA cần xác minh từ máy mình; HTTP smoke từ máy chủ không thay nghiệm thu MFA/ATTT/UAT.
