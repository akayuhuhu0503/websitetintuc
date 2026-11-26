# Thao tác hàng loạt Đồng bộ QLDD

Tại `/admin/source-sync`, lọc chuyên mục/ngày nguồn, chọn tối đa 100 bài qua các trang trong cùng bộ lọc. Đổi bộ lọc hoặc tab sẽ bỏ lựa chọn cũ. Lựa chọn và mã thao tác được giữ trong sessionStorage của tab, theo tài khoản; không lưu mật khẩu/cookie.

- **Đăng trực tiếp:** tạo nháp qua workflow nguồn rồi công bố đúng revision vừa tạo. Cần quyền quản lý website, tạo bài, quản lý media và đăng trực tiếp, trong phạm vi chuyên mục.
- **Chuyển vào bản nháp:** tạo bản nháp, không công bố.
- **Bỏ qua:** giữ snapshot/checkpoint và chuyển mục sang Lịch sử.

Hộp xác nhận liệt kê số bài đủ điều kiện và lý do loại từng bài. Mỗi bài chạy riêng, có tiến độ và link kết quả; lỗi một bài không dừng cả nhóm. Không đóng tab khi đang thực hiện. Khi mất phản hồi, giữ nguyên lựa chọn và thử lại với mã thao tác đã lưu. Không tự tạo bản nháp mới để thay bài đã xử lý.

## Bảo vệ dữ liệu

Máy chủ kiểm tra lại quyền, dấu phiên bản nguồn, SourceId/URL và tiêu đề đã tồn tại. Lịch tuần có ứng viên trùng hoặc cảnh báo phải đối soát riêng. Khi tạo nháp thành công nhưng công bố thất bại, trả link nháp để cán bộ bổ sung. Không công bố lại bài đã công khai, bị thu hồi, đã được sửa hoặc đang hẹn giờ. Workflow dùng revision/rowversion và command receipt; retry không tạo thêm bài.

Các tệp vẫn đi qua bộ tiếp nhận/kiểm định hiện có. Không thay PDF/ZIP, chính sách no-AV, pipeline ảnh hoặc quyền công bố. Sapo nguồn được giữ; bài cũ có đoạn mở đầu trong `strong` được trích nguyên văn khi sapo trống, không tự sáng tác.

Khi giải phóng hàng chờ, kể cả đã tạo nháp nhưng công bố bị giữ lại, chu kỳ đang dừng do dung lượng sẽ được đánh thức sau 20 giây nếu còn chỗ. Giữ giới hạn 200, checkpoint, giới hạn truy cập nguồn, lease và backoff. IIS nghỉ thì tác vụ chỉ tiếp tục khi ứng dụng hoạt động lại. Chu kỳ đã bắt đầu giữ danh sách chuyên mục của nó; ánh xạ thêm áp dụng vào chu kỳ kế tiếp.

## Kiểm tra tập trung

- Release Web/Worker: PASS, 0 lỗi/cảnh báo.
- SQL Server LocalDB riêng, schema 18, Application Role, HTTP thật: 45 kiểm tra PASS. Gồm quyền, CSRF, 100 mục, phiên nguồn cũ, trùng nội dung, mất phản hồi, nháp sửa, bài thu hồi, lịch hẹn, xử lý độc lập và tiếp tục chu kỳ cả khi đã tạo nháp nhưng chưa đủ điều kiện công bố.
- Chromium/Chrome cô lập: 10 kiểm tra PASS (chọn/phân trang/tải lại/đổi bộ lọc/nháp/bỏ qua/link kết quả/mobile/JavaScript).
- Sapo: 3 kiểm tra PASS. Không chạy full regression.
- Bộ test không dùng SQL vận hành hoặc phiên CMS thật. Các lần sửa selector trong harness đã chạy lại trước khi ghi PASS; không chụp ảnh/quay video.

Chạy HTTP/SQL bằng `LandPortal.SourceSyncTests <checkout> <LocalDB instance mới tên LPSync_12hex> <thư mục mới dưới .local/tests> unused --bulk-only`. Đặt `SOURCE_SYNC_BROWSER=1` trong tiến trình test để giữ host cho `node tests/browser/source-bulk.cjs <thư mục test>`; trình duyệt chỉ chấp nhận loopback và dùng tài khoản giả.

## Triển khai

PR này chưa deploy hoặc merge. Khi được duyệt, giữ quy trình `git pull --ff-only` → `powershell -NoProfile -File scripts/build-for-iis.ps1` → cập nhật nguyên `web` gồm `.runtime`. Không cần migration hoặc cấu hình IIS mới.
