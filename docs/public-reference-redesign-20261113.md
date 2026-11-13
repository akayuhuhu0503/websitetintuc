# Giao diện công khai theo ảnh tham chiếu — 13/11/2026

Nhánh bắt đầu từ `main` sau khi PR #11 đã merge (`8db522401765641e96ac0a4b524e6e2ad8f736c1`). Chỉ thay trình bày công khai; không thay truy vấn xuất bản, CMS, cấu hình triển khai hay dữ liệu.

## Đối chiếu thiết kế

Ảnh được hiển thị trong yêu cầu ở kích thước 1536 × 1024. Thư mục đính kèm trên máy chỉ có văn bản yêu cầu, không có tệp ảnh gốc để xác minh độ phân giải hoặc chồng pixel. Vì vậy không tuyên bố “99%”; số đo tham chiếu là xấp xỉ từ ảnh hiển thị và thông số người dùng chốt. Có đối chiếu hình render trình duyệt nội bộ.

| Vùng | Cách triển khai |
| --- | --- |
| Khung nội dung | Tối đa 1360px theo điểm xuất phát của yêu cầu; cùng lề header, tin, footer. Desktop 1536px: lề 88px; 1366px: lề 24px. |
| Thanh thông tin/header/menu | Mục tiêu khoảng 30/112/44px; giữ tên cơ quan, ngày/phút giờ Việt Nam; bỏ logo hiển thị, không xóa cấu hình. |
| Tìm kiếm | 400 × 40px desktop, bo 7px, viền mảnh, nút đỏ; mobile chuyển xuống dưới nhận diện. |
| Cụm tin | Xấp xỉ 50:25:25, khoảng cách 16px; tiêu đề trắng trên ảnh thật, ba tin phụ, sáu tiện ích có đường dẫn thật. |
| Văn bản/Thông báo/Video | 5:4:3, cùng hàng tiêu đề; danh sách gọn; video không kéo giãn thành ô rỗng. Thiếu khối thì tự chia lại. |
| Chữ/màu/viền | Roboto tự phục vụ; đỏ `#8B2E2E`, hover `#702424`, nền nhấn `#F7EEEE`, viền `#EADFDF`, bo 6px. Tin phụ 14px; tiêu đề chuyên mục 18px. |
| Trang dài | Tin còn lại → banner sự kiện → chuyên mục đang bật → các khối bổ sung → tin đọc nhiều/thống kê → tiện ích/footer. Không ép chiều cao màn hình. |

Khác biệt chủ động: không quốc huy/logo; tiện ích dùng chức năng thật; không bịa banner/video khi thiếu dữ liệu; giữ trang dài. Họa tiết masthead dùng đường đồng tâm CSS mờ vì không có tài nguyên trống đồng đã xác minh trong source, không tải ảnh ngoài. Nội dung và chiều dài tiêu đề thực tế có thể làm chiều cao khối khác ảnh minh họa.

## Phạm vi thay đổi

- `public-design.css`: biến thiết kế và khung công khai; `public-shell.css`: header/menu/footer; `home.css`: thay bộ quy tắc trang chủ, giới hạn trong `.portal-public .gdla-home`.
- Hai trang chủ Configured/fallback và partial tin, văn bản, tiện ích, video dùng chung bố cục. Tiêu đề/URL/ảnh vẫn lấy từ read model hiện có.
- Gộp các bài trùng trong cụm Featured/Recent bằng ID; đưa phần chưa hiển thị vào Tin mới. Không sửa dữ liệu, giới hạn CMS hoặc bật khối đã tắt.
- VIDEO vẫn dùng `HomeVideosViewComponent` hiện có: chỉ nội dung đã công bố có tệp đầu ra hợp lệ. Không đổi kiểm định, thời lượng, poster, chính sách no-AV hoặc quyền.
- Banner sự kiện giữ ảnh, lịch, thứ tự, URL, autoplay 5 giây và dots; tỷ lệ 117:10. Dots trên điện thoại nằm sát dưới ảnh để không che chữ. Bài không ảnh có mẫu chữ, không ảnh giả.
- Giữ tin đọc nhiều 7/30 ngày, menu nhiều cấp, dải tin chạy và các khối CMS còn lại. Sửa chữ lỗi mã hóa có sẵn trong fallback.

## Kiểm tra và giới hạn

Kiểm tra trình duyệt dùng MVC/Razor đã biên dịch thật, CSS/JS/font thật và read model cô lập, chỉ nghe loopback. Không đọc `.env`, không đăng nhập tài khoản thật hoặc mở SQL. Ảnh dùng cho so sánh nội bộ là bản sao chỉ đọc từ bộ media hiện có; không đưa vào Git hoặc dữ liệu website.

- Viewport 1536/1366/768/390/320: kiểm tra lề chung, tràn ngang, chồng cột, tiêu đề dài/không ảnh, khối bật/tắt, 0/1/3 banner và video.
- Kiểm tra menu bàn phím/mobile, tìm kiếm/đường dẫn bài, dots, giảm chuyển động, không tải tài nguyên ngoài. Video MP4 mẫu cô lập thực sự giải mã/phát và trả Range 206; đây không phải kiểm thử lại pipeline xử lý video/SQL.
- Bộ kiểm tra banner chuyên biệt tiếp tục kiểm tra ảnh ngang, rất lớn, vuông, dọc và cảnh báo crop trong CMS.
- Release build và Publish bằng `scripts/build-for-iis.ps1` với đầu vào kiểm tra offline, giữ script và guard hiện có; có Web, Worker, công cụ và manifest. Gói kiểm tra không dùng để deploy.
- CMS được bảo toàn theo diff và kiểm tra tách stylesheet. Không chạy lại workflow E2E, không tuyên bố nghiệm thu IIS hoặc dữ liệu vận hành.

Kết quả: **Release build PASS (0 lỗi/cảnh báo); Publish PASS; 142/142 kiểm tra trình duyệt PASS; 64 kiểm tra banner/CMS PASS**. Hai kiểm tra bổ sung cho thiếu Featured ở 1024px và thiếu Thông báo ở 768px PASS. Sau chỉnh căn trái header cuối cùng, kiểm tra riêng 768/390/320px PASS: nhận diện, tìm kiếm và nội dung có cùng mép trái, không tràn ngang. Không cộng các nhóm này thành số bài E2E nghiệp vụ.

Cách chạy lại kiểm tra nhẹ: xem `tests/LandPortal.PublicPresentationHost/README.md`. Chưa merge hoặc deploy. Sau review, quy trình IIS vẫn là `git pull` → `powershell -NoProfile -File scripts/build-for-iis.ps1` → cập nhật nguyên `web`; không đổi `.env`, SQL hoặc quyền Windows.
