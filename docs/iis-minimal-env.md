# Một file .env cho IIS GDLA

Cập nhật cấu trúc WebBundle: [chỉ copy web và restart](iis-web-only.md).
Các kết quả kiểm thử bên dưới là checkpoint minimal-env trước WebBundle.

Đặt file `.env` private đã được bàn giao vào gốc source có bản sửa minimal-env, rồi chạy:

```powershell
powershell -NoProfile -File scripts/build-for-iis.ps1
```

Publish tạo cấu hình nội bộ dùng chung cho Web/Worker. Không tạo/sửa `uat.json`,
nhập tên máy/IIS Site hoặc tính hash thủ công. Bản Publish mới được cập nhật theo
quy trình IIS hiện có, bảo toàn private/media, snapshots, temp, keys và bộ công cụ
đã cài; không mirror/xóa toàn bộ kho đang chạy. Không chạy migration.

File GDLA chuẩn gồm 19 khóa: hai môi trường Staging + profile UAT; định danh
physical SQL; ngoại lệ TLS hiện hành; hai connection string + role secret;
phạm vi công bố/TLS của nguồn tra cứu; URL HTTPS + host; SMTP Disabled; bốn cờ
upload ảnh/tài liệu/video. Secret chỉ lấy từ private input hiện có, không từ chat.
Các tham chiếu TLS/nguồn có sẵn được giữ nguyên, không phát sinh mã xác nhận mới.

Đã loại khỏi đầu vào:

- Bốn path/hash FFmpeg: tự tìm `tools/ffmpeg` rồi PATH, không tự tải binary.
- `Video__ProcessorApproved`, `Video__LicenseReference`: bỏ kiểm tra cờ/chuỗi thủ
  công trong Options, processor và script. File cũ có hai khóa này vẫn đọc được,
  nhưng chúng bị bỏ qua, không được chuyển thành trạng thái phê duyệt giả.
- SQL endpoint/login/database lặp lại: parser suy ra từ connection string; runtime
  vẫn đối chiếu physical server/database/login/role và schema 18 như trước.
- Đường dẫn storage trống: giữ cơ chế portable hiện có, không đổi nơi chứa dữ liệu.
- Các trường SMTP/Scanner/idle/giới hạn đã bằng mặc định hoặc để trống: dùng chính
  default hiện hành. Mail vẫn ghi rõ Disabled/false để tránh gửi thật.

Chỉ bốn lựa chọn vận hành đã yêu cầu chuyển sang `true`. No-AV vẫn chỉ hợp lệ
trong Staging/UAT; upload công dân/phụ đề không kế thừa ngoại lệ. Không giả Clean.
Khi video tắt thì không thực thi FFmpeg. Khi bật, vẫn phải tìm thấy công cụ thật,
chạy được dưới tài khoản dịch vụ, có đúng encoder và vượt kiểm định kỹ thuật.
Không có cờ hay mã mới thay thế hai trường đã bỏ. Trách nhiệm tuân thủ giấy phép
FFmpeg/bộ codec thực tế vẫn thuộc đơn vị cài đặt; hash/phiên bản không chứng minh
giấy phép hay nguồn gốc.

Kiểm chứng: Web/Worker Release PASS (0 warnings/errors), parser Publish offline
PASS, 14 kiểm tra cấu hình runtime offline PASS, 8 kiểm tra PowerShell PASS,
36 kiểm tra công cụ/chuyển mã cô lập PASS. Hai connection string và role secret
giữ nguyên tuyệt đối; tất cả giá trị cấu hình sau parser ngoài bốn cờ đều giữ nguyên.
File private có ACL người vận hành và SYSTEM; không đưa vào Git.

Chưa chạy Publish đầy đủ, IIS startup hoặc SQL probe trên môi trường thật.
Không chạy lại E2E 48/60, không ghi SQL hoặc thay đổi 703 media. Điều kiện còn
phụ thuộc máy IIS: có FFmpeg/FFprobe từ nguồn tin cậy, Worker đang chạy và quyền
đọc/chạy công cụ, đọc/ghi kho dùng chung. Không cần cung cấp thêm mật khẩu/mã.
