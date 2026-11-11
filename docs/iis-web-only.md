# Cập nhật IIS bằng duy nhất thư mục web

Gói mới mặc định của `scripts/build-for-iis.ps1` là **WebBundle**. Đầu vào vẫn là `.env` private tại gốc source. Không sửa SQL, không migration, không tự triển khai IIS.

```powershell
git pull --ff-only
powershell -NoProfile -File scripts/build-for-iis.ps1
```

Lấy toàn bộ `.local\publish\TTDL-IIS\web`, **bao gồm thư mục ẩn `.runtime`**. Đây là gói bí mật, không đưa lên Git/share công khai. `.runtime/private/uat.json` được tạo từ `.env` và bind SHA256 trong `.runtime/manifest.json`; Web và Worker cùng dùng bản này. Biến môi trường ứng dụng cũ không ghi đè cấu hình WebBundle; môi trường tiến trình vẫn bắt buộc Staging. Không chỉnh JSON/manifest sau build.

## Chuyển đổi một lần

IIS vẫn trỏ `D:\WWW_GDLA\publish\TTDL-IIS\web`. Giữ nguyên `TTDL-IIS\private` (media, snapshots, temp, keys) và `TTDL-IIS\tools\ffmpeg`; **không chép kho dữ liệu vào web**. Bản `.env` 19 khóa để trống storage dùng đúng bốn thư mục cũ này. Nếu trước đây dùng đường dẫn tùy chỉnh, giữ các cấu hình storage đó trong `.env`, đối chiếu trước khi chuyển; không tự chuyển dữ liệu.

Một lần duy nhất, đổi Action của trình giám sát Worker hiện có sang launcher mới dưới đây (giữ nguyên tài khoản dịch vụ, lịch, chế độ tự khởi động và quyền). `Video` dùng `--video` qua launcher; các mode khác: `Scheduler`, `Documents`, `InquiryScan`, `Mail`.

```powershell
powershell -NoProfile -NonInteractive -File "D:\WWW_GDLA\publish\TTDL-IIS\web\.runtime\run-worker.ps1" -Mode Video
```

Đây là **tiến trình riêng**, không chạy trong IIS. Không đăng ký thêm Worker nếu Worker cũ còn đang chạy. Tài khoản hiện có cần Read/Execute trên web và công cụ, Read trên `.runtime/private`, Modify trên bốn kho dữ liệu, khả năng đọc metadata IIS theo guard hiện có. Không dùng Administrator/SYSTEM, không cấp quyền rộng. Giữ quyền đọc cấu hình cho đúng tài khoản; copy gói không đồng nghĩa ACL máy đích đã đúng. Không tạo hoặc bật Worker Mail trong đợt chuyển này.

## Cập nhật thường lệ và rollback

1. Dừng App Pool `WWW_GDLA` và đúng các Worker đang chạy. Giữ bản `web` cũ nguyên vẹn **ngoài webroot** để rollback; lần chuyển đầu giữ cả định nghĩa Action cũ và các thư mục sibling cũ.
2. Thay nguyên thư mục `web` bằng `web` vừa build, không trộn DLL/manifest từ nhiều bản. Không ghi đè/xóa sibling `private` hoặc `tools`. Kiểm tra tài khoản pool/Worker đọc được cấu hình mới; không copy ACL của máy Developer đè ACL máy chủ.
3. Khởi động lại các tiến trình đã dừng. Kiểm tra `/`, `/tin-tuc`, `/admin/login`, `/health`, upload PNG CMS và trạng thái Video Worker. Các URL `/.runtime/private/uat.json`, `/.runtime/manifest.json`, `/.runtime/worker/Worker.dll` phải bị từ chối; tuyệt đối không có nội dung file.
4. Nếu lỗi: dừng các tiến trình, trả nguyên `web` cũ vào vị trí ban đầu rồi khởi động lại. Nếu rollback lần chuyển đầu, trả Action Worker cũ. Không rollback SQL/storage. Lưu ý: khôi phục cấu hình cũ cũng khôi phục các chính sách upload cũ; tệp mới vẫn được giữ nguyên, có thể bị chính sách cũ từ chối phục vụ.

Không cần copy `private`, `manifest.json`, `ops`, `worker` bên ngoài `web` ở các lần cập nhật tiếp theo. Full Publish vẫn tạo tools quản trị bên trong `.runtime`, nhưng không tự chạy chúng. Build không tải FFmpeg, không mở SQL, không bật SMTP. Phiên bản .NET Hosting Bundle và FFmpeg/FFprobe khả dụng vẫn là điều kiện thực tế của máy chủ.

## Giới hạn xác minh

Kiểm thử cô lập kiểm tra cấu trúc gói, cấu hình, hash, storage và HTTP. Chỉ xác nhận IIS thực tế sau khi người vận hành cập nhật; máy Developer không tự deploy hoặc sửa quyền IIS.
