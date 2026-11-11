# IIS: một file .env, một lệnh Publish, chỉ copy web

Giữ `.env` private tại gốc source, pull code đã được duyệt và chạy:

```powershell
powershell -NoProfile -File scripts/build-for-iis.ps1
```

Gói mặc định **WebBundle**: chỉ lấy toàn bộ `.local/publish/TTDL-IIS/web`, gồm
`.runtime`. Dừng IIS/Worker, giữ bản web cũ ngoài webroot để rollback, thay web
và restart. IIS vẫn trỏ vào web. Không merge từng DLL hoặc chỉnh JSON/manifest.

Web chứa cấu hình private đã bind hash và Worker độc lập. Không có secret trong
wwwroot; IIS và middleware chặn truy cập HTTP vào `.runtime`. Gói web có secret,
chỉ chuyển bằng kênh riêng, không đưa lên Git hoặc URL tải công khai.

Dữ liệu mặc định giữ nguyên ở `TTDL-IIS/private/{media,snapshots,temp,keys}`,
FFmpeg giữ ở `TTDL-IIS/tools/ffmpeg` hoặc PATH. Không xóa/copy đè các thư mục này.
Web/Worker đọc cùng cấu hình mới sinh từ `.env`, không lấy cờ upload cũ từ
biến môi trường ứng dụng. Staging/UAT, HTTPS, SQL identity/Runtime18, Application
Role và kiểm định file vẫn bắt buộc. Chưa quét vẫn là NotScanned, không giả Clean.

**Chuyển đổi lần đầu:** đổi Action của trình giám sát Worker hiện có sang
`web/.runtime/run-worker.ps1 -Mode Video` (các mode khác giữ đúng mode).
Giữ nguyên tài khoản, lịch và quyền; không tạo Worker trùng hoặc bật Mail.
Tài khoản pool/Worker cần đọc gói mới và ghi bốn kho dữ liệu cũ.

[Hướng dẫn chuyển đổi, cập nhật và rollback](iis-web-only.md).
Trong gói chỉ-copy-web, hướng dẫn nằm tại `.runtime/README.md`.

Build không mở SQL, chạy migration, cấp quyền hoặc deploy. Gói build cũ chưa
triển khai được giữ lại với tên `.previous-<timestamp>`; gói đang chạy/IIS tham
chiếu hoặc không có build receipt hợp lệ sẽ bị từ chối ghi đè. Không cần đổi
lệnh build mỗi lần. `-PrivateMode Portable` vẫn dành cho gói legacy đầy đủ;
không áp dụng hướng dẫn chỉ-copy-web cho gói legacy.

Nếu khởi động bị chặn, đọc mã cụ thể: `PRIVATE_HASH` có thể do trộn config/binary;
`IDENTITY` cần tài khoản dịch vụ ít quyền; `STORAGE_BOUNDARY` cần kho nằm ngoài
ứng dụng/IIS mappings; `HTTPS` cần đúng HTTPS origin. Không tắt guard để khởi động.
Publish PASS không thay nghiệm thu trên IIS thật.
