# Quản trị cấu hình cổng thông tin — Giai đoạn 2B

Phạm vi local, chưa production ready. Đọc `account-admin-guide.md` để quản trị tài khoản/nhóm và `scheduler-runbook.md` để vận hành lịch đăng. Không tự gán các quyền mới cho tài khoản vận hành.

## Quyền và màn hình

`/admin/site` cần một trong `site.view`, `site.manage`, `site.publish`. `site.manage` cho lưu/khôi phục cấu hình nháp; `site.publish` cho áp dụng đúng phiên bản đang là nháp. Hai quyền không suy ra lẫn nhau và không suy ra từ `settings.manage`. Upload còn cần `media.manage` và policy môi trường. Các endpoint kiểm tra quyền hiện thời; ẩn nút không thay kiểm tra server.

Cấu hình SQL được khởi tạo **một lần** bằng lệnh CLI `site-initialize` sau migration có kiểm chứng target/backup. Web không seed lúc startup. Lệnh chạy lại trả `initialized=False`, giữ live/draft hiện có. Backfill lấy menu/hạng mục, footer và thông tin liên hệ đã có; không sửa payload bài nguồn, không tự tạo logo hay nhập lại corpus. Tệp nhận diện ngoài 53 source assets chưa được tự hợp thức hóa bằng cách gắn metadata vào SQL.

## Lưu nháp, xem trước, áp dụng và khôi phục

1. Mở vùng cần sửa: liên hệ & nhận diện, chuyên mục, menu & liên kết, banner, trang chủ hoặc danh mục văn bản.
2. Chọn **Sửa**/**Thêm mục vào bản nháp**, nhập trường theo mẫu và **Lưu phiên bản nháp**. Mỗi lần lưu tạo một revision bất biến mới, gồm toàn bộ cấu hình; public vẫn lấy live trước đó.
3. Chọn **Xem trước nháp**. Trang dùng thiết kế public với header/menu/footer của revision được chọn. Preview cần phiên đăng nhập có quyền và không cấp grant media công khai. Nội dung bài trong khối vẫn lấy published revision của bài.
4. Người có `site.publish` chọn **Áp dụng bản nháp**. Server kiểm tra exact revision, rowversion, cấu trúc, đích public, bytes media và scan policy trước khi chuyển live trong một transaction. Header/menu/footer cùng lấy một revision cho mỗi request.
5. Ở **Phiên bản**, chọn **Khôi phục thành nháp** rồi kiểm tra và áp dụng riêng. Lịch sử không bị sửa. ID danh mục phụ bổ sung sau phiên bản cũ được giữ ở trạng thái ngừng chọn để không mất tham chiếu.

Xung đột trả HTTP 409, giữ live và bản đang lưu hợp lệ. Tải lại phiên bản mới trước khi tiếp tục; hệ thống không âm thầm ghi đè. Không xem thông báo lưu nháp như đã áp dụng công khai.

## Chuyên mục và điều hướng

14 chuyên mục tin và các hạng mục nguồn luôn được giữ ID, tên gốc, slug, cấu trúc nguồn và đường tới. Mục nguồn có dấu **Mục nguồn**, không xóa/đổi/gộp. Mục đã có nội dung/lịch sử không hard-delete. Có thể sửa mô tả/thứ tự phù hợp. Chuyên mục bổ sung được lưu ở cấu hình nháp; stable Category chỉ được tạo khi áp dụng cấu hình. Slug đã dùng giữ nguyên trong 2B, tránh mất route/alias. Self-parent, chu trình, cây quá ba cấp và slug trùng bị chặn.

Menu có vị trí `main`, `footer`, `links`, `supplementary`; nhóm cha cùng vị trí. Dùng picker **Mục hệ thống/Chuyên mục/Nội dung công khai**, hoặc nhập liên kết HTTP(S) có nhãn. Đích admin/preview, URL script/data/protocol-relative/credential/backslash không được nhận. Server không fetch URL ngoài, không làm proxy hoặc open redirect. Mục nguồn chỉ đổi thứ tự hợp lệ; nút ↑/↓ dùng được bằng bàn phím. Nội dung đích bị gỡ sẽ tự biến mất khỏi menu; nhãn/ảnh draft không được lấy thay thế.

Với chuyên mục bổ sung, **Hiển thị trong điều hướng** điều khiển các menu trỏ trực tiếp tới chuyên mục sau khi áp dụng; tắt trong nháp không đổi menu live. Tuyến chuyên mục và nội dung không bị xóa. 14 mục nguồn luôn bật, không dùng cờ này để làm mất hạng mục.

## Trang chủ

Các mẫu khối có schema rõ: tin nổi bật, tin mới, thông báo, chuyên mục, văn bản pháp luật, dự thảo, video và liên kết/sổ tay. Chọn thứ tự, bật/tắt khối và số lượng 1–12. Khối Chuyên mục cần category; pin dùng content ID, lấy published revision lúc render, đúng loại của khối. Không tạo bản sao bài hoặc số lượt truy cập giả. Pin bị gỡ không hiển thị; query SQL lấy bản hợp lệ khác, không tải toàn kho về browser.

Picker nội dung/ảnh giới hạn 60 kết quả SQL; dùng từ khóa **trước khi nhập sửa**, vì tìm lại picker qua GET tải form từ bản nháp đã lưu. Nội dung đã chọn còn hợp lệ được giữ trong danh sách. UI video vẫn ghi hai metadata nguồn chưa có tệp, không tạo player hoặc gắn YouTube.

## Banner, logo và liên hệ

Chỉ nhập thông tin công khai đã được xác nhận: tên cơ quan, địa chỉ, điện thoại, đường dây nóng, email và ghi chú footer thuần văn bản. Không có ô SQL/SMTP/scanner bypass/filesystem/secret. Logo chọn từ kho riêng; không tự tạo biểu trưng cơ quan.

Banner gồm ảnh, nhãn, alt, vị trí, đích, thứ tự và mốc tùy chọn. Nhập giờ Việt Nam UTC+7; hệ thống lưu instant UTC. Mốc cuối là exclusive. Hết hạn được kiểm tra mỗi request, không cần worker chạy. Bỏ một banner không phá ảnh vẫn dùng bởi logo/bài/banner live khác; khi hết tham chiếu công khai, original không còn grant public, lịch sử private vẫn xem được theo quyền. Không hứa thu hồi bytes khách đã tải trước đó.

Upload ảnh từ máy đi qua pipeline ảnh/PDF 2A, decode thật và kho Test/Dev riêng. `-AllowUnscannedMedia` chỉ ngoại lệ Development local tường minh; tệp vẫn `NotScanned`. Production/Staging và worker không biến flag thành scan thành công. Máy quét thật chưa triển khai vẫn là blocker trước production.

## Danh mục văn bản

Tạo/sửa cơ quan ban hành, loại văn bản và lĩnh vực qua mẫu, lưu nháp rồi áp dụng. Không hard-delete ID/đổi loại danh mục đã công khai; ngừng cho chọn vẫn giữ lịch sử. Metadata bài/văn bản là snapshot thuộc revision riêng, đổi tên cấu hình không viết lại raw source hoặc bản biên tập đã xuất bản. Giá trị nguồn chưa ánh xạ giữ text gốc và trạng thái cần rà soát.

## Phạm vi còn lại

2C: tiếp nhận/kiểm duyệt câu hỏi, liên hệ, bình luận dưới bài và góp ý dự thảo; không có nút gửi thành công giả ở 2B. 2D: video upload trực tiếp từ máy và xử lý tệp. Chuyển toàn nguồn, scanner, MFA đặc quyền, restore toàn hệ, UAT và production thuộc các checkpoint sau. `RESTORE VERIFYONLY` không phải diễn tập restore toàn hệ.
