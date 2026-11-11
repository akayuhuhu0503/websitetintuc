# FFmpeg/FFprobe tự nhận diện — IIS UAT

Không cần nhập bốn khóa `Video__FfmpegPath`, `Video__FfprobePath`, `Video__FfmpegSha256`, `Video__FfprobeSha256`. Để trống hoặc bỏ chúng khỏi `.env`. Nếu vẫn giữ giá trị cũ, ứng dụng tôn trọng override/pin cũ và báo lỗi nếu không khớp, không âm thầm bỏ kiểm tra.

## Cài một lần, dùng qua các lần cập nhật

Vận hành cài bộ công cụ có nguồn phát hành/giấy phép đã được đơn vị kiểm tra vào:

```text
D:\WWW_GDLA\publish\TTDL-IIS\tools\ffmpeg\ffmpeg.exe
D:\WWW_GDLA\publish\TTDL-IIS\tools\ffmpeg\ffprobe.exe
```

Đây là thư mục cố định tương đối với gói ứng dụng, dùng chung Web/Worker; không phải cấu hình riêng cho tên máy. Nếu không có binary ở đây, chỉ tìm đúng hai tên trên các thư mục PATH tuyệt đối của tiến trình (tối đa 128 mục, không quét ổ đĩa). Không tìm trong current directory, không dùng thư mục upload/temp/keys/snapshots, UNC hoặc reparse. Binary đã tìm thấy nhưng lỗi không được che bằng cách chạy một bản khác trên PATH.

Publish tạo hướng dẫn tại `tools/ffmpeg/README.md`, không tải, chạy hay đóng gói FFmpeg. Khi cập nhật gói, **giữ nguyên binary do vận hành cài trong tools/ffmpeg**; không mirror/xóa thư mục tools. Nếu công cụ nằm trong PATH, tài khoản dịch vụ phải nhận được PATH đó sau restart; PATH của phiên đăng nhập Admin không nhất thiết là PATH của IIS/Worker.

Không có binary thì trang quản trị báo chưa tìm thấy; website không tự tải binary. Việc cài từ nguồn tin cậy vẫn thuộc vận hành, không được thay bằng hash tự tính. Không có cơ chế mới tự phê duyệt nguồn/công cụ.

## Chính sách giữ nguyên

- `Video__Enabled` và ba cờ no-AV là lựa chọn vận hành, mặc định tắt; no-AV vẫn chỉ hợp lệ trong Staging/UAT. Không còn yêu cầu `Video__ProcessorApproved`/`Video__LicenseReference`: hai chuỗi này không xác minh nguồn hay giấy phép. Video tắt thì không thực thi bộ xử lý. Vận hành vẫn phải cài bản FFmpeg từ nguồn tin cậy, tuân thủ giấy phép/phân phối của bản đó; không có mã xác nhận nội bộ hay cơ chế tự phê duyệt thay thế.
- Phiên bản FFmpeg/FFprobe được kiểm tra bằng thực thi `-version`; encoder phải có đúng `libx264`, `aac`, `mjpeg`. Thực thi thử theo tài khoản tiến trình hiện tại, không nâng quyền. Worker `--video` kiểm tra lại định kỳ và trước xử lý.
- SHA256 được tự tính; kiểm tra lại và giữ file handle chống thay byte trong mỗi lần thực thi Windows. Thay byte giữa các lệnh của một lượt xử lý sẽ bị từ chối. Lượt kiểm tra mới có thể nhận binary đã cập nhật sau kiểm tra phiên bản/encoder; hash cũ/mới được ghi nhận. Đây không phải chữ ký hay chứng minh nguồn gốc.
- Cache MP4/poster gắn hash binary thực tế, không dùng hash rỗng trong cấu hình. Pipeline/quota/chunk/timeout/codec/SHA/duyệt/xuất bản và trạng thái NotScanned không thay đổi. Không sửa SQL/migration, video cũ hoặc 703 media.

## Trạng thái quản trị và Worker

`/admin/videos` hiển thị riêng FFmpeg, FFprobe dưới tài khoản Web, kết quả Worker và lý do bị chặn. Kết quả Web không được dùng để khẳng định Worker có quyền thực thi.

Worker ghi heartbeat được bảo vệ bằng Data Protection trong `Storage.TempRoot/video-worker-status.dat`; Web/Worker phải dùng cùng temp và keys, với quyền đọc/ghi thực tế đã cấp. Thiếu/hỏng/quá hạn heartbeat hiển thị chưa xác nhận/mất cập nhật, không giả Ready. Đây chỉ là chẩn đoán, không cấp quyền xuất bản. `--health` là phép kiểm tra riêng, không giả rằng dịch vụ vòng lặp đang chạy. Nhật ký Worker và audit tác vụ video ghi hash/version quan sát được; không ghi secret.

Tài khoản Worker không thuộc Administrators/SYSTEM, cần Read/Execute bộ công cụ, Read cấu hình, quyền kho media/temp/keys theo quy trình hiện hành. Không cấp quyền mới tự động. Kiểm tra trên IIS sau khi review/merge:

```powershell
powershell -NoProfile -File .\ops\uat-runtime.ps1 -PublishRoot D:\WWW_GDLA\publish\TTDL-IIS -Mode Video -Health
```

Chạy dưới đúng tài khoản Worker. Dịch vụ dùng cùng lệnh với `-Mode Video`, bỏ `-Health`; wrapper truyền `--video`. Không tự bật Mail/scanner hoặc sửa SQL.

## Build, kiểm tra và rollback

Build/publish đầy đủ Web/Worker bằng quy trình `scripts/build-for-iis.ps1` hiện có. Chưa deploy tự động. Backup bộ binary/config/manifest trước cập nhật; giữ bộ công cụ và dữ liệu tại chỗ. Rollback đồng bộ binary/config/manifest. Nếu quay về binary trước cơ chế tự tìm, dùng lại cấu hình đường dẫn/hash đã backup của bản cũ; không phục hồi SQL hoặc xóa media.

Kiểm thử tập trung: `dotnet run --project tests/LandPortal.VideoToolTests -c Release -- <repo-root>`. Cần bộ công cụ đã được duyệt cho test trong `.local/video-tools.json`; chỉ tạo cây file riêng dưới `.local/tests`, không kết nối SQL/IIS. Bao gồm discovery, trạng thái bật/tắt, phiên bản/encoder thật, một chuyển mã MP4/poster, cache, hash drift và heartbeat. Không chạy lại 48/60 E2E.
