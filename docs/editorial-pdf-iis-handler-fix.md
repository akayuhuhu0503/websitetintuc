# PDF QHKHSDD: handler IIS và kiểm định STT 1

## Kết quả ngày 19/11/2026

- Người vận hành xác nhận `/admin/documents` hết HTTP 404 sau khi bổ sung handler `aspNetCore` tại IIS. Mẫu Publish trước đó chỉ khai báo request limit tại location này và dựa vào kế thừa handler.
- Publish nay khai báo rõ handler ANCM tại `admin/documents`, dùng `remove` đúng tên trước `add` để tránh trùng khi handler đã kế thừa. Không `clear` các handler khác. Áp dụng cho GET, POST và đường dẫn con.
- Giữ request limit chung 20 MiB; riêng tài liệu 105 MiB gồm overhead multipart. Giới hạn PDF thực vẫn 104 MiB/2.000 trang, Office 10 MiB. Không đổi parser, chính sách NotScanned, quyền hoặc dữ liệu.
- Chuyển WebBundle diễn ra trước khi tính hash web.config vào manifest. Không chỉnh web.config, uat.json hoặc manifest sau Publish.

## Bằng chứng PDF

PDF STT 1 chuẩn hóa có 11.516.204 byte, 52 trang, SHA256 `a46151db75a057b90584da7de0f977baff3b84c0e24b14df97e05ed074fd98f2`, khớp manifest và pdf-overrides. Chạy `planning-pdf-check --editorial` bằng CLI đã bàn giao: PASS.

Upload đúng một lần qua công cụ CMS HTTPS vào bản nháp hiện có: thành công, `NotScanned`, `usable=true`. Công cụ xác minh byte/hash và gắn tệp, journal STT 1 chuyển `Verified`; ngày `2026-06-01`, chuyên mục 77, trạng thái Draft được giữ. Không tạo bài thay thế, không công bố.

Lỗi parser của lần upload thủ công trước không tái hiện với file đã xác minh. Chưa có hash của file được chọn thủ công hoặc exception IIS gốc nên không khẳng định đó là chọn nhầm PDF. Không nới lỏng parser để che lỗi chưa tái hiện.

## Kiểm tra và triển khai

- Release Web/Worker: PASS, 0 warning/error.
- `scripts/test-editorial-pdf-publish.ps1`: 10 kiểm tra PASS qua chuyển đổi WebBundle thật; xác minh handler rõ ràng, request limits, private protection và hash manifest. Đây là kiểm tra XML/package, không phải thử IIS trên Developer.
- Chưa merge hoặc deploy. IIS hiện đã được người vận hành sửa handler nên không cần cập nhật lại chỉ để tiếp tục import khi upload đang hoạt động.
- Sau khi duyệt PR, lần Publish tiếp theo dùng lệnh build hiện hành và triển khai nguyên `web` gồm `.runtime`; sửa đóng gói này giúp giữ handler qua các lần cập nhật. Không chạy migration.
