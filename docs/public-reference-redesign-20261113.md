# Giao diện công khai GDLA — thiết kế số 2

PR #12 bắt đầu từ `main` sau PR #11 (`8db5224`). Bản đính chính ngày 13/11/2026 lấy **ảnh mới trong yêu cầu của chủ dự án làm chuẩn duy nhất**, thay thế bố cục 50:25:25 và hàng Văn bản–Thông báo–Video của bản trước.

## Bố cục

- Khung công khai tối đa 1216px, căn giữa; header/menu/footer cùng lề. Thanh đầu 28px, nhận diện 104px, menu desktop 36px. Bỏ logo/quốc huy hiển thị, không thêm biểu tượng thay thế; giữ dữ liệu logo trong CMS.
- Dải tra cứu nhanh có sáu đường dẫn thật. Cụm tin có bài lớn bên trái, bốn bài phụ giữa và danh sách đánh số “Tin tức nổi bật” bên phải. Tiêu đề trên ảnh thật; bài không ảnh dùng trình bày chữ, không ảnh giả.
- Hàng tiếp theo: “Dịch vụ công & Tiện ích” chiếm chiều rộng hai cột tin, VIDEO bên phải. Tiện ích dùng Hỏi đáp, văn bản, dự thảo, sổ tay và liên hệ; tìm kiếm gọi `/tim-kiem`. Không dựng nút nộp hồ sơ/thanh toán giả, không coi liên kết cơ quan trong CMS là dịch vụ công.
- Bốn khối tiếp theo: văn bản, thông báo và hai chuyên mục thật đầu tiên theo thứ tự CMS. Giữ tên, URL và toàn bộ mục của các khối. Các chuyên mục còn lại, tin mới, banner, liên kết và thống kê tiếp tục phía dưới; trang không bị ép thành một màn hình.
- Roboto tự phục vụ, đỏ `#8B2E2E`, hover `#702424`, nền nhấn `#F7EEEE`, viền mảnh và bo 6px. Mobile chuyển thành một cột, giữ chữ đọc được và điều hướng menu.

Configured/fallback dùng các partial chung. Không đổi truy vấn xuất bản, giới hạn CMS, video readiness, workflow, SQL, migration, `.env` hoặc yêu cầu IIS. Banner giữ 117:10, autoplay 5 giây, dots và liên kết. Tin đọc nhiều 7/30 ngày vẫn dùng thống kê thật. VIDEO chỉ render một lần từ component hiện có.

## Đối chiếu và khác biệt có chủ đích

Đối chiếu trực quan render với ảnh mới hiển thị 1536×1024 trong yêu cầu. Không có tệp raster gốc trong thư mục attachment nên không tuyên bố tỷ lệ giống theo pixel. Không dùng lại ảnh cũ làm chuẩn.

Các khác biệt bắt buộc: bỏ quốc huy; nội dung/ảnh từ CMS; tiện ích có thật; không tạo tab “Quan tâm” thiếu dữ liệu hoặc thao tác giả. Không hardcode tên “Thông tin chuyên đề”/“Tin tức địa phương” nếu CMS cấu hình chuyên mục khác. Thiếu video/bài hoặc khối bị tắt thì co bố cục tương ứng. Nền header dùng họa tiết CSS nhẹ, không lấy ảnh phong cảnh chưa xác minh quyền sử dụng. Các chuyên mục hiện có khiến trang dài hơn ảnh minh họa.

## Kiểm tra

Host kiểm thử render Razor Web đã biên dịch, CSS/JS/font thật qua loopback; read model và endpoint bài/tìm kiếm là fixture. Không đọc `.env`, mở SQL, dùng tài khoản thật hoặc deploy IIS. Ảnh fixture chỉ nằm trong `.local`, không commit.

Kiểm tra tập trung Configured/fallback tại 1536/1366/768/390/320px; ba cột tin, Tiện ích–Video, bốn khối; thiếu tin/ảnh/video/chuyên mục; 0/1/3 banner/video; menu bàn phím/mobile, tìm kiếm và liên kết. MP4 fixture phát thật, kiểm tra HTTP Range. Không chạy lại E2E CMS/SQL hoặc full regression.

Kết quả bản đính chính: **Release Web + host Razor PASS, 0 lỗi/cảnh báo; 182 kiểm tra trình duyệt PASS; 64 kiểm tra banner/CMS PASS**. Sau chỉnh CSS metadata/ô tìm kiếm, kiểm tra riêng 20 điều kiện trên Configured/fallback tại 1536/1366/768/390px PASS. Chỉnh khoảng cách cuối cùng được xác minh bằng 8 điều kiện desktop/mobile PASS, không chạy lại toàn bộ bộ kiểm thử. Các nhóm này là kiểm tra giao diện cô lập, không phải E2E nghiệp vụ hoặc nghiệm thu IIS. Đã xem render desktop, tablet, mobile và footer so với ảnh mới; không có logo hiển thị.

Cách chạy: `tests/LandPortal.PublicPresentationHost/README.md`. Chưa merge/deploy; sau review vẫn dùng `git pull` → `scripts/build-for-iis.ps1` → cập nhật nguyên `web`, không thêm cấu hình máy chủ.
