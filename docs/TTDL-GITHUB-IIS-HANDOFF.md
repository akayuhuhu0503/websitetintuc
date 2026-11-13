# IIS: pull → build → thay web

Giữ file .env private đã được cấu hình một lần tại gốc source:

`powershell
git pull --ff-only
powershell -NoProfile -File scripts/build-for-iis.ps1
`

Dừng ứng dụng IIS, giữ nguyên bản web cũ ngoài webroot để rollback, thay **toàn bộ**
.local\publish\TTDL-IIS\web (gồm .runtime), rồi khởi động lại. Không trộn DLL,
không sửa JSON/manifest và không chép đè TTDL-IIS\private đang giữ dữ liệu.

Web tự chạy hàng đợi video, lịch xuất bản và các hàng đợi phụ theo chính sách hiện có.
Không cần tạo Task Scheduler, Windows Service hoặc tài khoản Worker mới. Khi video
được bật, build tự lấy bản FFmpeg cố định đã kiểm tra từ nhà phát hành, giữ giấy phép,
và đưa vào .runtime/tools/ffmpeg. Các lần build sau dùng bộ nhớ đệm đã kiểm tra.
Máy build cần truy cập HTTPS tới GitHub lần đầu; máy IIS không tự tải công cụ.

Ảnh/tài liệu CMS theo chính sách no-AV đã duyệt được kiểm định ngay trong Web.
Tệp chưa quét vẫn là NotScanned. Tệp công dân/phụ đề và dữ liệu cách ly cũ giữ
đúng yêu cầu quét hiện có; không tự mở ngoại lệ. SMTP Disabled vẫn không gửi thư.

**IIS nghỉ hoặc dừng:** xử lý nền tạm dừng. Hàng đợi SQL vẫn còn; Web hoạt động lại
thì tiếp tục. Trước khi trả trang công khai, Web xử lý lịch đến hạn qua đúng quy trình
xuất bản và phiên bản đã duyệt. Không hứa xử lý nền liên tục khi IIS đã dừng.

Kiểm tra /, /tin-tuc, /admin/login, /health, upload CMS và /admin/videos.
Nếu cập nhật lỗi, trả nguyên web cũ và khởi động lại; không rollback SQL/storage.

Gói web có cấu hình bí mật: chỉ chuyển qua kênh riêng; không đưa lên Git hoặc URL
tải công khai. Runtime18, HTTPS, SQL identity, Application Role, hash gói và ranh giới
kho private vẫn bắt buộc. [Chi tiết giới hạn và chuyển đổi](iis-web-only.md).
