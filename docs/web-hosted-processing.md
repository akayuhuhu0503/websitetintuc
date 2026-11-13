# Vận hành GDLA bằng một gói Web

Thay đổi theo yêu cầu chủ hệ thống ngày 12/11/2026. PR #9 về quyền đọc metadata
IIS của Worker được tạm dừng; không đưa bản sửa đó vào nhánh này.

## Đối chiếu yêu cầu nghiệp vụ

Đã đọc trực tiếp `SRS_CTTDT-Cuc-QLDD_v1.2.docx`:

| Mục | Giữ nguyên | Cách thực hiện |
|---|---|---|
| RB-01 | Developer đẩy Git, Trưởng phòng build/cài đặt | Pull → build-for-iis → thay web |
| FR-09, FR-13 | Upload, quản lý và công bố video trên Cổng | Dùng lại dịch vụ video, xử lý nền trong Web |
| FR-02, BR-02/03 | Khối video chỉ hiện nội dung công bố, khối trống ẩn | Không đổi truy vấn công khai hoặc tiêu chí Ready |
| BR-01, NFR-03 | Phân quyền server, CSRF, kiểm định tệp | Giữ nguyên kiểm tra; no-AV chỉ theo chính sách UAT đã bật |
| AC-10.2 | Lịch gắn đúng thời điểm/phiên bản đã duyệt | Chạy trong Web và kiểm tra lịch đến hạn trước đọc trang công khai |

SRS không bắt buộc Windows Service, Task Scheduler, tài khoản Worker riêng hoặc
antivirus trên từng tệp. Cụm “quét lỗ hổng trước khi vận hành” không đồng nghĩa với
quét virus mọi upload. Tệp công dân, phụ đề và dữ liệu cách ly vẫn theo chính sách
hiện có, không được tự mở no-AV.

Chủ hệ thống chấp nhận giới hạn tác vụ nền khi IIS dừng. Do đó không tuyên bố đáp ứng
mốc 5 phút của AC-10.2 khi toàn bộ ứng dụng không chạy. Lịch còn hợp lệ được chạy bù
khi Web hoạt động trở lại; không tự sửa trạng thái lịch đã hủy/từ chối.

## Thiết kế tối thiểu

- Không có bảng/queue mới. Video, lịch xuất bản, thư và quét tệp dùng queue/lease SQL
  hiện có. Các vòng xử lý độc lập và có giới hạn, không chặn khởi động toàn website
  khi scanner hoặc FFmpeg chưa sẵn sàng.
- Video: một claim toàn cục; hủy đúng lease khi dừng bình thường, không tiêu hao lượt
  thử vì recycle. Ngắt đột ngột giữ cơ chế lease hết hạn/retry có giới hạn. FFmpeg
  giữ giới hạn thread, bộ nhớ, thời gian, protocol; MP4/poster phải đạt kiểm định.
- Lịch: bộ điều phối chung cho nền và HTTP, đi qua `SchedulerService` và
  `WorkflowService`, kiểm tra lại quyền, revision/hash, người duyệt và media. Yêu cầu
  HTTP không cấp quyền xuất bản; chỉ kích hoạt thực thi lịch đã có. Không dùng cờ
  hiển thị để lách workflow. Ngắt kết nối của khách không hủy claim đã nhận.
- Mỗi request công khai xử lý tối đa 8 lịch, tối đa 2 giây chờ khóa và ngân sách mềm
  2 giây giữa các giao dịch. Một giao dịch có thể lâu hơn; backlog tiếp tục ở lần
  đọc/vòng nền sau. Các endpoint admin, health, static và media không chịu việc này.
- SMTP Disabled không mở kết nối/gửi thư; gửi không chắc chắn vẫn là Uncertain,
  không tự retry gây thư trùng. Scanner hỏng không tiêu hết lượt thử các tệp chờ.
- FFmpeg được lấy ở build từ archive cố định có checksum công bố, giữ giấy phép;
  bundle được xác minh hash cả công cụ và thông tin nguồn. Runtime không tải binary.
- WebBundle Worker CLI cũ tự kết thúc trước loader/SQL sau kiểm tra binding, cho phép
  task cũ không cần đổi Action. Không thể tự thay mã trong bộ nhớ của một tiến trình
  Worker cũ đã chạy trước deploy; nếu có, dừng nó trong khoảng bảo trì. Không cấp
  quyền quản trị cho Web để sửa task hay tắt dịch vụ.

## Kết quả kiểm tra tập trung

- Release build và Publish bằng cấu hình giả lập 19 khóa: PASS, không kết nối SQL.
- Video HTTP/SQL LocalDB schema18: **24/24 PASS**, gồm lease/cancel/restart,
  chuyển mã và poster thật, `NotScanned`, không xử lý trùng.
- Hẹn giờ HTTP/SQL cô lập: **16/16 PASS**, gồm lịch quá 15 phút, tương lai,
  quyền bị thu hồi, revision thay đổi, lịch hủy, HTTP/nền đồng thời không trùng.
- Vòng bảo trì: **6/6 PASS**, scanner thiếu/lỗi không làm host dừng; Mail Disabled
  không mở dịch vụ gửi thư hoặc SQL.
- Đóng gói dependency: **13/13 PASS**; archive thật đúng checksum, cache hoạt động
  khi mạng bị chặn. Công cụ trong bundle: **26/26 PASS**, gồm thiếu/sai hash,
  manifest lỗi và chạy FFmpeg/FFprobe thật kiểm tra encoder.
- WebBundle HTTP/SQLite: **38/38 PASS**, gồm PNG CMS, chặn secret/binary qua HTTP,
  cấu hình mới thắng cờ môi trường cũ và rollback nguyên thư mục.
- Gói Publish thật: 442 hash PASS, 5 launcher cũ tự kết thúc trước config/SQL.

Không chạy lại các bộ 48/60 E2E cũ. Các lỗi đường dẫn/cấu hình harness đã được sửa
trong kiểm thử; không dùng kết quả lỗi làm PASS. Tất cả SQL test nằm trong instance
LocalDB riêng đã dừng. Chưa kiểm thử/cài đặt trên IIS thật; không đổi SQL vận hành,
schema, tài khoản, kho dữ liệu hay 703 media. Hướng dẫn bàn giao:
[iis-one-command.md](iis-one-command.md).
