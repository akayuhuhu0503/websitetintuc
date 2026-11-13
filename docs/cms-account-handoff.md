# Bàn giao tài khoản trong CMS

Áp dụng cho tạo/đặt lại mật khẩu qua `/admin/users`. Không đổi schema, email vẫn không bắt buộc, không gửi SMTP và không truy xuất mật khẩu cũ.

1. Người có quyền quản lý tài khoản tạo tài khoản hoặc đặt lại mật khẩu của tài khoản được phép quản lý.
2. Trong 10 phút, ngay tại trang tài khoản trên **HTTPS**, nhập lại mật khẩu hiện tại của chính người quản trị và trả lời CAPTCHA để xem mật khẩu tạm một lần.
3. Bàn giao trực tiếp qua kênh riêng. Sau đó gán nhóm/phạm vi chuyên mục nếu cần. Người nhận bắt buộc đổi mật khẩu trước khi sử dụng CMS.

Mật khẩu tạm được sinh ngẫu nhiên 24 ký tự. Luồng CMS lưu phong bì mã hóa bằng Data Protection trong kho temp private, ràng buộc người thực hiện, tài khoản đích và security stamp. Tệp chỉ cho danh tính chạy ứng dụng và SYSTEM truy cập. Nội dung mật khẩu bị xóa khỏi phong bì trước khi gửi phản hồi. Lần xem lại, phiên hết 10 phút, đổi security stamp, thiếu quyền hoặc kiểm tra toàn vẹn không đạt đều bị từ chối. Khi bàn giao đã hết hiệu lực, chỉ có thể tạo mật khẩu tạm mới theo quyền hiện hành.

Trang bàn giao chỉ nhận POST có CSRF, kiểm tra mật khẩu/CAPTCHA và giới hạn số lần; phản hồi không được cache, không mang referrer. Audit ghi thao tác và định danh, không ghi mật khẩu. Phiên lỗi sau khi tiêu thụ sẽ không được phát lại mật khẩu. Công cụ CLI/bootstrap cũ giữ kênh bàn giao private riêng; CMS không tạo tệp mật khẩu plaintext của kênh đó.

Các bộ quyền gợi ý Người viết / Người duyệt / Quản lý nội dung chỉ chọn thêm checkbox, không tự lưu hoặc cấp quyền. Người duyệt không có quyền xuất bản trong bộ gợi ý. Quyền chi tiết vẫn hiện theo nhóm; server tiếp tục chặn việc cấp quyền ngoài phạm vi hoặc sửa quyền của nhóm chính mình. Chuyên mục được giao riêng ở trang tài khoản.

Kiểm chứng tập trung: `LandPortal.CmsAccountTests` chạy HTTPS thật với chứng chỉ fixture được pin, SQL Server LocalDB mới schema 18 và tài khoản giả. 30 kiểm tra PASS gồm mã hóa/tamper, 10 phút, atomic single-use và cạnh tranh, ACL, HTTPS, CSRF, quyền hiện hành, reauthentication, security stamp, Identity/first-login, audit không secret và chống cấp quyền vượt phạm vi. Không dùng hoặc sửa tài khoản, database, IIS hay cấu hình vận hành. Chưa nghiệm thu trên IIS thật.

Chạy lại khi sửa đúng phần này:

```powershell
dotnet build tests/LandPortal.CmsAccountTests/LandPortal.CmsAccountTests.csproj -c Release
powershell -NoProfile -File scripts/test-cms-accounts.ps1 -DotnetPath (Get-Command dotnet).Source
```

Script chỉ tạo instance LocalDB tên `LPCms_<12 hex>`, xác minh database chưa tồn tại trước khi tạo schema fixture, và dừng instance khi kết thúc. Không đọc `.env` hoặc credential thật.
