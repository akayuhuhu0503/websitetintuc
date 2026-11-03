# Bàn giao IIS UAT — Cục Quản lý đất đai / TTDL

1. Clone repository hoặc `git pull --ff-only`.
2. Cài .NET SDK theo `global.json`, Node.js/npm. Máy IIS cần ASP.NET Core 10 Hosting Bundle.
3. Nhận **một file `.env` private đã điền sẵn từ Developer**, copy vào gốc source. Không dùng file Dev, không đưa lên Git. Không cần tự điền 12 biến hạ tầng.
4. Chạy tại gốc source:

   ```powershell
   powershell -NoProfile -File scripts/build-for-iis.ps1
   ```

5. Nhận gói `.local/publish/TTDL-IIS/` khi có **PUBLISH PASS**. IIS Physical Path là thư mục **`web/`** trong gói. Giữ `worker/`, `tools/`, `ops/` ngoài webroot.
6. Lệnh đã tự chuyển `.env` thành `uat.json` private, ACL người build + SYSTEM, ngoài Git/webroot. Đường dẫn `PRIVATE CONFIG` được in cuối lệnh. Nếu build khác máy IIS, chuyển riêng file này qua kênh an toàn; không đặt vào gói public.
7. CNTT tạo site/pool riêng **chưa khởi động**, binding HTTPS và service account hiện có. Chạy một lệnh [chuẩn bị cấu hình trên máy IIS](docs/iis-one-command.md): tự lấy hostname, storage, Defender SHA và identity; dùng hồ sơ duyệt triển khai/ATTT thật. Sau đó chạy Preflight bằng service account, rồi mới khởi động theo quyền.

SQL schema **70 bảng / 17 migration**, login và `LandPortalRuntime` đã hoàn tất: **không chạy lại Apply**. Hai secret hiện hành do Developer chuẩn bị, không tạo lại. Ngoại lệ `Encrypt=True;TrustServerCertificate=True` chỉ được chấp nhận đúng UAT đã duyệt; Production vẫn bị chặn. SMTP giữ Disabled.

Publish chỉ đóng gói, không cần SQL đang kết nối, hostname được cấp hay scanner được duyệt. **Preflight/Activation vẫn bắt buộc** đúng SQL/role, HTTPS, private storage/ACL, scanner và phê duyệt đích. Phát hiện Defender không có nghĩa ATTT đã duyệt. Hiện chưa có máy IIS TTDL để nghiệm thu: **CHƯA PRODUCTION READY**.

Output cũ được bảo toàn: `-Replace` chỉ dùng cho bản chưa sealed/chưa chạy; không tự xóa `TTDL-IIS.previous`. Xem [hướng dẫn triển khai ngắn](docs/iis-one-command.md).
