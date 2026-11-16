# Tin tức, video tại trang chủ và Tài liệu

Đã đồng bộ với `main` sau khi merge PR #14 tại `87beab0a6cc6a0026edd65f7626c67acb105616f`. Giữ tối ưu kết nối SQL theo request, Application Role, ticker JSON và phản hồi điều hướng; không thay lịch đăng hoặc quyền công khai.

- **Tin tức:** dùng toàn bộ danh mục của phiên bản cấu hình đang công bố/xem trước, gồm chuyên mục chưa có bài. Giữ tên, thứ tự, slug và cây cha/con. Ẩn chuyên mục bị tắt và con của nó; không tự bật menu đã tắt. Dropdown nhiều cột trên desktop, accordion trên mobile, hỗ trợ bàn phím. `/tin-tuc` có danh mục đầy đủ. Khi hệ thống chưa có cấu hình website, fallback dùng danh mục hiện có; cấu hình chỉ có nháp không được coi là fallback.
- **Video:** bấm poster/Play để tạo trình phát HTML5 ngay tại chỗ; không tải MP4/phụ đề trước khi bấm. Tiêu đề vẫn mở bài chi tiết. Giữ endpoint phát, HTTP Range, SHA256 và điều kiện công bố; danh sách phụ đề chỉ lấy từ đúng bản đã công bố. Phụ đề vẫn bắt buộc đủ chính sách quét hiện có, kể cả khi video cha sử dụng chính sách no-AV đã được duyệt. Tệp bị thu hồi/lỗi hiển thị thông báo tại trình phát.
- **Tài liệu:** `/tai-lieu` là nhóm riêng, chỉ lấy tệp đính kèm PDF/DOCX/XLSX của đúng chuyên mục được cấu hình và phiên bản bài đã công bố. Tái sử dụng upload, phân quyền, kiểm định và tuyến tải hiện hành; không lấy tệp từ hai nhóm Văn bản pháp luật/Dự thảo.

## Cách dùng trong CMS

1. Mở `/admin/site?section=menus`, chọn **Thêm nhóm Tài liệu vào nháp**. Hệ thống tạo chuyên mục riêng và mục thứ ba dưới nhóm Văn bản; giữ tên hai mục cũ. Nếu cấu trúc Văn bản đã tùy biến, dùng **Thêm menu → Mục hệ thống → Tài liệu**, chọn chuyên mục và nhóm cha đúng thay vì tự đoán cấu trúc.
2. Xem trước rồi **Áp dụng cấu hình** bằng tài khoản có quyền. Không tự công bố cấu hình khi khởi động ứng dụng.
3. Soạn bài, chọn chuyên mục **Tài liệu**, đính kèm PDF/Word/Excel qua trình biên tập hiện hành; gửi duyệt và công bố theo quyền. Bản nháp/tệp không đủ điều kiện không xuất hiện trên `/tai-lieu`.

Chủ dự án xác nhận chưa rõ “hạng mục 2.4” và yêu cầu tạm xây dựng nhóm mới **Tài liệu**. Không có ánh xạ 2.4, không dùng ID 24, không chuyển nội dung giữa các nhóm. Khi có định nghĩa chính thức mới đối chiếu phạm vi.

## Kiểm chứng tập trung

- Release Web, Worker và các host kiểm thử: PASS, 0 lỗi/cảnh báo.
- HTTP + SQL Server LocalDB mới, riêng biệt, schema 18: **48/48** kiểm tra, gồm CMS/CSRF/quyền/công bố cấu hình, nhóm tài liệu, tải PDF, giữ hai nhóm văn bản, MP4 thật được FFmpeg xử lý, Range 206, phụ đề đúng phiên bản, từ chối Pending/thu hồi/sai hash. Đây là số assertion của chương trình, gồm kiểm tra trạng thái HTTP và xác nhận bộ browser đã hoàn tất, không phải 48 kịch bản nghiệp vụ độc lập.
- Trình duyệt Edge thật: **52/52** kiểm tra ở 1920/1366/768/390/320px; menu, bàn phím, không tràn ngang, phát/tạm dừng/tua video tại chỗ, tiêu đề mở đúng bài, lỗi tệp và trang Tài liệu. Không tạo ảnh/video bằng chứng.
- Phụ đề Clean trong fixture là dữ liệu mô phỏng riêng để kiểm tra phạm vi truy cập; không tuyên bố đã quét antivirus thật. MP4/poster và kiểm định/hash dùng tệp thật của fixture.
- Không chạy lại bộ upload 5 định dạng hoặc toàn bộ E2E/performance. Chưa kiểm chứng IIS thật. Các lỗi chuẩn bị host thử nghiệm đã sửa trước lượt PASS cuối; không cộng các lượt thất bại vào số PASS.

Nguồn kiểm thử: `tests/LandPortal.PublicFunctionsTests` và `tests/browser/public-functions-check.mjs`. Host chỉ chấp nhận instance LocalDB mới có tiền tố `LPPublic_` và thư mục mới dưới `.local/tests`; không đọc `.env` hay credential vận hành. Browser chỉ nhận địa chỉ loopback từ host. Tool-facts JSON của fixture chỉ cần `ffmpegPath` và `ffprobePath` tới công cụ hợp lệ đã có trên máy thử.

## Bàn giao

Không migration mới, không sửa SQL vận hành, `.env`, tài khoản, media hoặc cấu hình IIS. Quy trình vẫn là `git pull --ff-only` → `powershell -NoProfile -File scripts/build-for-iis.ps1` → cập nhật nguyên `web` gồm `.runtime`. Không trộn DLL/cấu hình của hai bản build hoặc sửa `uat.json`/manifest sau Publish. Không thêm thao tác máy chủ; chỉ cấu hình nhóm Tài liệu trong CMS theo các bước trên. Chưa triển khai IIS trong đợt tích hợp này.

## Kiểm tra tích hợp PR #14 + #15 — 16/11/2026

- Bổ sung `Materials` vào cùng phạm vi GET/HEAD xử lý lịch đăng và giữ kết nối SQL của các trang nội dung. POST, Admin, media và endpoint sức khỏe vẫn không bị đưa vào phạm vi đó.
- Release Web/Worker PASS, 0 lỗi/cảnh báo. Không chạy lại toàn bộ upload, chuyển mã video hoặc phép đo hiệu năng.
- Host HTTP thật trên SQL Server LocalDB riêng, schema 18, dùng `RuntimeRoleInterceptor` thật: **25/25 assertion tích hợp PASS**. Mỗi trang `/`, `/tin-tuc`, chuyên mục, bài viết và `/tai-lieu` có một kết nối cho kiểm tra lịch đăng và một kết nối nội dung dùng Application Role, đóng sau request. Có kiểm tra CMS, CSRF, hai menu Văn bản cũ, Range 206, video Pending/thu hồi bị chặn và tuyến Tài liệu.
- **21/21 kiểm tra kết nối/role/đọc mới/thống kê PASS**, tái sử dụng kiểm tra của PR #14. Fixture bài trong ticker được xác nhận tồn tại trước khi kiểm tra thu hồi. Không suy diễn đây là phép đo hiệu năng mới.
- **53/53 assertion trình duyệt Edge PASS** tại 1920/1366/768/390/320px: menu đầy đủ, bàn phím/mobile, không tràn ngang; không tải MP4/phụ đề trước Play, video phát/tạm dừng/tua tại chỗ, tiêu đề mở bài; Play không kích hoạt trạng thái chờ điều hướng; không lỗi JavaScript.
- Các số trên là nhóm assertion riêng, có kiểm tra tổng hợp trùng phạm vi; không cộng thành số kịch bản nghiệp vụ. Kết quả 48/52 ở mục trước là kiểm tra chức năng trước tích hợp, không chạy lại pipeline.
- Chế độ `integration <checkout> <fixture> <output>` của host chỉ nhận fixture `LPPublic_*` đã hoàn tất, kiểm tra LocalDB/máy/database/đường dẫn MDF/schema trước thao tác, không đọc cấu hình vận hành. Dùng tệp mẫu đã có, không tải lên/chuyển mã lại. Fixture được dừng sau kiểm tra.
- Cập nhật endpoint ticker JSON trong host trình bày cô lập để tương thích JavaScript PR #14. Đây chỉ là dữ liệu test.
- Chưa kiểm chứng trên IIS thật. “Hạng mục 2.4” vẫn chờ xác định; không tự gán chuyên mục hay công bố cấu hình CMS.
