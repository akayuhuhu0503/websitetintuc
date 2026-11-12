# Event Banner toàn chiều rộng khung tin, tỉ lệ 117:10

Chỉ thay giao diện dải `home_event_strip`: khung rộng 100% khu vực tin tức, không còn giới hạn 1170px, căn thẳng hàng với các bài viết,
`aspect-ratio: 117 / 10`, `overflow: hidden`. Desktop không bo góc, không viền/bóng/nền đệm; khoảng cách 28–30 px, giống dải banner sự kiện báo điện tử. Mobile (≤640 px) giữ dải ảnh đúng tỉ lệ, thêm tên sự kiện bên dưới để không cần đọc chữ rất nhỏ trong ảnh; nút chọn banner chuyển xuống dưới tên. Ảnh và liên kết đầy khung, ảnh dùng
`object-fit: cover`. Không đổi ảnh gốc, metadata, trạng thái, thứ tự hoặc lịch CMS.
CMS dùng kích thước ảnh preview thực để cảnh báo nếu crop dự kiến vượt 10%, không
đặt điều kiện bắt buộc mới. Ảnh không tải được không được suy đoán kích thước.

Configured dùng partial `_HomeEventStrip` sau cụm tin mới. Fallback chỉ được dùng
khi không có cấu hình công khai; không có banner và không tạo khoảng trống.
VIDEO, thumbnail, ảnh bài viết và activity slider `home` giữ nguyên.

Kiểm tra: Release Web/Razor PASS, 0 warning/error. Edge headless PASS tại
1920/1366/768/390 với ảnh thử nội tại 1170×100, 11700×1000, 4000×4000,
1000×6000, 8000×200. Khung phủ hết chiều ngang vùng nội dung; desktop đúng tỉ lệ, mobile có chú thích nên khung cao hơn ảnh; căn giữa,
không tràn ngang; 0/1/3 banner, dots/link, autoplay 5 giây và cảnh báo CMS PASS.

```powershell
node tests/browser/banner-strip-check.mjs
```

48 assertions trình bày trong fixture trình duyệt dùng CSS/JS thật và markup
article lấy từ partial; đây không phải 48 E2E upload hoặc CMS/SQL workflow.
Controller/Configured/fallback đã đối chiếu source, Razor đã compile Release.
Không kiểm thử trên IIS thật, không kết nối SQL, thay dữ liệu hay triển khai.
