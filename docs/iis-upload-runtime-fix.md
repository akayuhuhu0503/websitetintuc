# Sửa upload và nhận diện bộ xử lý trên IIS WebBundle

## Nguyên nhân đã tái hiện

- **Banner:** giao diện gọi `POST /admin/media/upload?site=true`, qua `AdminMediaController` và `MediaService.UploadAsync`. Không có endpoint Banner riêng bắt buộc scanner. Code cũ so sánh đúng chuỗi `"true"`; JSON boolean `true` được provider .NET đọc thành `"True"`, nên chính sách ảnh bị hiểu là tắt. Đã dùng parser boolean, vẫn yêu cầu Staging/UAT hợp lệ.
- **Tài liệu:** `EditorialDocumentService` nhận cùng `MediaUploadOptions`, có cùng lỗi đọc cờ. Sửa parser áp dụng cho PDF/DOCX/XLSX qua controller tài liệu; không đổi kiểm định định dạng, phân quyền hoặc trạng thái `NotScanned`.
- **Video:** `Video.Enabled` và cờ video no-AV có cùng lỗi boolean. Khi video bị coi là tắt, health chưa tìm công cụ nhưng UI cũ báo “Chưa tìm thấy”. UI mới ghi rõ chưa kiểm tra khi tính năng tắt. Kiểm thử đường dẫn thực thi có dấu phân cách cuối cũng phát hiện sai vị trí công cụ; đường dẫn component phải được chuẩn hóa nhất quán trước khi xác định thư mục cha.
- **Worker:** thông báo cũ gộp thiếu tệp trạng thái, không đọc được, sai keyring và dữ liệu lỗi. Chẩn đoán mới phân biệt các trường hợp này, không lộ đường dẫn hoặc secret. Recycle pool không khởi động tiến trình Worker riêng; chưa có bằng chứng từ máy IIS để kết luận dịch vụ đang chạy hay đã dừng.

`true`, `True`, `TRUE` và JSON boolean đều được hiểu đúng. Thiếu cờ, `false` hoặc giá trị không phải boolean không bật ngoại lệ. Không đổi Runtime18/TLS/Application Role, scanner của upload công khai/phụ đề, SQL hoặc media cũ.

## Cập nhật sau khi PR được duyệt

Giữ `.env` private hiện tại với ba cờ no-AV đã được chủ hệ thống duyệt và `Video__Enabled=true`. Không sửa `uat.json` hoặc manifest sau build.

```powershell
git pull --ff-only
powershell -NoProfile -File scripts/build-for-iis.ps1
```

Dừng pool `WWW_GDLA` và các Worker đang chạy; giữ nguyên bản `web` cũ ngoài webroot để rollback. Thay **nguyên** `.local\publish\TTDL-IIS\web`, bao gồm `.runtime`, tại `D:\WWW_GDLA\publish\TTDL-IIS\web`. Giữ nguyên sibling `private` và `tools\ffmpeg`; không copy lại 703 media. Khởi động lại pool và các Worker đã dừng.

Đối chiếu Action của supervisor video hiện có, chỉ đổi một lần nếu còn trỏ Worker cũ:

```powershell
powershell -NoProfile -NonInteractive -File "D:\WWW_GDLA\publish\TTDL-IIS\web\.runtime\run-worker.ps1" -Mode Video
```

Đây là lệnh chạy liên tục dưới **tài khoản dịch vụ ít quyền hiện có**, không phải chạy bằng Administrator/SYSTEM và không phải lệnh đăng ký dịch vụ mới. Không tạo tiến trình trùng. `-Health` chỉ kiểm tra rồi thoát, không thay thế Worker. Tài khoản Worker và pool cần đọc gói/công cụ, dùng chung `temp`/`keys` theo cấu hình runtime và có quyền kho dữ liệu theo runbook hiện có. Không cấp quyền rộng hoặc sửa ACL tự động.

Trong `/admin/videos`, kiểm tra riêng bộ xử lý theo tài khoản Web và cập nhật Worker. Nếu báo lỗi đọc trạng thái, kiểm tra quyền tài khoản hiện hữu; nếu không giải mã được, kiểm tra cùng `keys`; nếu thiếu/cũ, kiểm tra supervisor/process video. Không xóa keys để thử khắc phục. Khi bộ xử lý/Worker sẵn sàng, thử một video được phép dùng qua lưu nháp → gửi duyệt → duyệt → xuất bản.

Kiểm tra upload Banner PNG/JPG, tài liệu PDF/DOCX/XLSX và trạng thái kiểm định thực; không kỳ vọng `Clean` khi no-AV bật. Nếu triển khai lỗi, dừng các tiến trình và trả nguyên `web` cũ, khôi phục Action Worker cũ nếu đã chuyển. Không rollback SQL/storage.

## Kiểm thử tập trung

Hai chế độ test độc lập: `LandPortal.WebBundleTests --upload-policy` (HTTP Kestrel/SQLite cô lập, không SQL vận hành) và `LandPortal.VideoToolTests --iis-runtime <repo-có-tool-local>` (Windows filesystem, FFmpeg/FFprobe thật, heartbeat được bảo vệ). Chỉ chế độ này được chạy; không chạy lại 48/60 E2E. Giới hạn: không xác nhận quyền hoặc tiến trình đang chạy trên IIS từ máy Developer.

Kết quả bản sửa: Web/Worker Release build không lỗi/cảnh báo; 58 kiểm tra cấu hình/upload HTTP/tải tài liệu PASS; 23 kiểm tra tìm và thực thi công cụ PASS; 12 kiểm tra trạng thái Worker PASS. `worker-package.ps1` chạy launcher thật với `-VerifyPackage` và cấu hình JSON boolean tổng hợp: PASS, tiến trình xác nhận không mở SQL hoặc chạy queue. Đây là fixture, không phải xác nhận IIS vận hành.

Đường dẫn `D:\WWW_GDLA\...` được đối chiếu bằng kiểm tra xử lý đường dẫn. Máy kiểm thử chỉ có C:, nên kiểm tra file/tool thật dùng cùng cấu trúc `WWW_GDLA\publish\TTDL-IIS` trong thư mục tạm Windows, không tuyên bố đã chạy trên ổ D: của IIS. Lỗi dấu phân cách cuối được tái hiện FAIL trước sửa, PASS sau sửa. Một lỗi khôi phục ACL trong harness đã sửa; lượt cuối 12 kiểm tra Worker PASS, chỉ tác động fixture riêng.
