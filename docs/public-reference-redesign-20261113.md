# Giao diện công khai GDLA — thiết kế số 2

PR #12 bắt đầu từ `main` sau PR #11 (`8db5224`). Bản đính chính ngày 13/11/2026 lấy **ảnh mới trong yêu cầu của chủ dự án làm chuẩn duy nhất**, thay thế bố cục 50:25:25 và hàng Văn bản–Thông báo–Video của bản trước.

## Bố cục

- Theo yêu cầu bổ sung về chiều rộng và khả năng đọc, khung công khai tối đa **1400px**, căn giữa; header/menu/footer cùng lề. Thanh đầu 28px, nhận diện 104px, menu desktop 36px. Bỏ logo/quốc huy hiển thị, không thêm biểu tượng thay thế; giữ dữ liệu logo trong CMS.
- Dải tra cứu nhanh có sáu đường dẫn thật. Cụm tin có bài lớn bên trái, tối đa bốn bài phụ giữa và tối đa bốn bài chưa xuất hiện ở danh sách đánh số “Tin tức nổi bật” bên phải. Khối Featured được ưu tiên trước Recent, loại trùng theo ID; tin mới chỉ lấy phần còn lại. Thiếu bài thì giảm số mục, không lặp bài để lấp chỗ. Tiêu đề trên ảnh thật; bài không ảnh dùng trình bày chữ, không ảnh giả.
- Hàng tiếp theo: “Dịch vụ công & Tiện ích” chiếm chiều rộng hai cột tin, VIDEO bên phải. Tiện ích dùng Hỏi đáp, văn bản, dự thảo, sổ tay và liên hệ; tìm kiếm gọi `/tim-kiem`. Không dựng nút nộp hồ sơ/thanh toán giả, không coi liên kết cơ quan trong CMS là dịch vụ công.
- Bốn khối tiếp theo: văn bản, thông báo và hai chuyên mục thật đầu tiên theo thứ tự CMS. Giữ tên, URL và toàn bộ mục của các khối. Các chuyên mục còn lại, tin mới, banner, liên kết và thống kê tiếp tục phía dưới; trang không bị ép thành một màn hình.
- Roboto tự phục vụ, đỏ `#8B2E2E`, hover `#702424`, nền nhấn `#F7EEEE`, viền mảnh và bo 6px. Tăng tiêu đề tin phụ lên 14px, metadata 12px và tiêu đề tin chính 22px trên desktop. Một tin mới dùng card tối đa 760px với ảnh khoảng 32%, không phóng ảnh toàn chiều ngang. Mobile chuyển thành một cột, giữ chữ đọc được và điều hướng menu.

Configured/fallback dùng các partial chung. Không đổi truy vấn xuất bản, giới hạn CMS, video readiness, workflow, SQL, migration, `.env` hoặc yêu cầu IIS. Banner giữ 117:10, autoplay 5 giây, dots và liên kết; chấm điều hướng nằm trong khoảng riêng dưới ảnh, không che chữ trong ảnh và không thêm chú thích thô. Tin đọc nhiều 7/30 ngày vẫn dùng thống kê thật. VIDEO chỉ render một lần từ component hiện có.

## Đối chiếu và khác biệt có chủ đích

Đối chiếu trực quan render với ảnh mới hiển thị 1536×1024 trong yêu cầu. Không có tệp raster gốc trong thư mục attachment nên không tuyên bố tỷ lệ giống theo pixel. Không dùng lại ảnh cũ làm chuẩn.

Các khác biệt bắt buộc: bỏ quốc huy; nội dung/ảnh từ CMS; tiện ích có thật; không tạo tab “Quan tâm” thiếu dữ liệu hoặc thao tác giả. Không hardcode tên “Thông tin chuyên đề”/“Tin tức địa phương” nếu CMS cấu hình chuyên mục khác. Thiếu video/bài hoặc khối bị tắt thì co bố cục tương ứng. Nền header dùng họa tiết CSS nhẹ, không lấy ảnh phong cảnh chưa xác minh quyền sử dụng. Các chuyên mục hiện có khiến trang dài hơn ảnh minh họa.

## Kiểm tra

### Lần mở rộng cuối: 1740px (thay giới hạn trang chủ 1400px ở lượt trước)

Đo trên trình duyệt Local: `innerWidth=1920`, `clientWidth=1905`, DPR=1, visual viewport scale=1, CSS zoom=1, không transform. Khung cũ 1400px có lề 252,5/267,5px. Thử 1760px cho lề 72,5/87,5px; chọn 1740px để đạt **82,5/97,5px** khi có thanh cuộn dọc 15px. Không phóng toàn trang bằng zoom/scale.

Phạm vi mở rộng là `body.portal-public.is-home`; các trang khác giữ giới hạn 1400px. Desktop co theo vùng nhìn với đệm 32px. Ảnh tin chính giới hạn chiều cao khoảng 330–400px, tiêu đề tối đa 48ch, thumbnail tin phụ giữ 96px. Danh sách ít tin tự co chiều cao; không ép khối chữ đơn thành ảnh lớn.

Kết quả trình duyệt cô lập không có thanh cuộn chiếm chỗ:

| Viewport | Khung | Lề mỗi bên | Cao tin chính |
|---|---|---|---|
| 1366px | 1302px | 32px | 365px |
| 1536px | 1472px | 32px | 368,6px |
| 1920px | 1740px | 90px | 400px |
| 2560px | 1740px | 410px | 400px |

**Release build PASS, 0 lỗi/cảnh báo; 254 điều kiện giao diện PASS** bằng `homepage-wide-check.mjs`: bốn viewport trên và 768/390/320px, Configured/fallback, căn lề, không tràn ngang, ba cột, banner/dots, một tin mới, ít/không có tin/video/banner. Đã kiểm tra bỏ class trang chủ thì CSS trở về khung 1400px. Không chạy lại bộ 303 kiểm tra, E2E CMS/SQL hoặc full regression. Kết quả bên dưới là các lượt trước, không cộng vào 254 kiểm tra lần này.

Host kiểm thử render Razor Web đã biên dịch, CSS/JS/font thật qua loopback; read model và endpoint bài/tìm kiếm là fixture. Không đọc `.env`, mở SQL, dùng tài khoản thật hoặc deploy IIS. Ảnh fixture chỉ nằm trong `.local`, không commit.

Kiểm tra tập trung Configured/fallback tại 1536/1366/1920/768/390/320px; ba cột tin không trùng ID, Featured/Recent đổi thứ tự và chứa ID lặp, 1/2/5/6/9/10/12 bài, Tiện ích–Video, bốn khối; thiếu tin/ảnh/video/chuyên mục; 0/1/3 banner/video; menu bàn phím/mobile, tìm kiếm và liên kết. MP4 fixture phát thật, kiểm tra HTTP Range. Không chạy lại E2E CMS/SQL hoặc full regression.

Kết quả cập nhật chiều rộng/khả năng đọc: **Release Web + host Razor PASS, 0 lỗi/cảnh báo; 303 kiểm tra trình duyệt tập trung PASS**. Sau chỉnh CSS cân chiều cao ba cột, kiểm tra riêng **78 điều kiện hình học PASS** tại sáu viewport: không tràn ngang, không chồng khối khi thiếu tin/video, tin chữ đơn không bị ép chiều cao. Không chạy lại 303 kiểm tra. Bộ trình duyệt đã kiểm tra tỷ lệ banner và vị trí dots ở sáu viewport; không chạy lại toàn bộ nhóm kiểm tra CMS crop-warning vốn không đổi. Kết quả 182/64 và 20/8 của bản đính chính trước là lịch sử, không cộng vào số lần kiểm tra hiện tại. Đây là kiểm tra giao diện cô lập, không phải E2E nghiệp vụ hoặc nghiệm thu IIS. Đã xem render desktop, tablet/mobile và footer để đối chiếu; không có logo hiển thị.

Cách chạy: `tests/LandPortal.PublicPresentationHost/README.md`. Chưa merge/deploy; sau review vẫn dùng `git pull` → `scripts/build-for-iis.ps1` → cập nhật nguyên `web`, không thêm cấu hình máy chủ.
