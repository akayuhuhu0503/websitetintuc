# IIS: một file .env, một lệnh Publish

## Developer chuẩn bị trước khi gửi

Hoàn thiện một `.env` theo `scripts/ttdl.env.example`, dùng ba secret hiện hành của hai SQL connection và Application Role; không tạo/reset secret. Giữ SQL website `website_tin_tuc`, role `LandPortalRuntime`, source reader `landportal_giayhuy_reader` và View đã có. Không chạy migration/import/provision lại.

Không có trường mã phê duyệt triển khai/scanner bắt buộc. Thiếu scanner không chặn website: upload cần quét bị từ chối, không giả Clean. HTTPS, SQL identity/Application Role và quyền private vẫn được kiểm tra. Không cần tên máy/site. Developer điền URL HTTPS thực tế trước khi bàn giao nếu IIS không cung cấp duy nhất một binding để tự nhận.

## Trưởng phòng triển khai

1. Clone repository hoặc `git pull --ff-only origin main`.
2. Đặt nguyên `.env` đã nhận vào gốc source, không sửa nội dung.
3. Chạy đúng một lệnh:

   ```powershell
   powershell -NoProfile -File scripts/build-for-iis.ps1
   ```

4. Chuyển **nguyên thư mục** `.local/publish/TTDL-IIS/` lên IIS bằng kênh riêng. IIS Physical Path chỉ trỏ `web/`. Giữ nguyên thư mục `private/` bên cạnh `web/`; không mở, sửa hoặc copy riêng `uat.json`.
5. Cấu hình IIS/HTTPS theo quy trình quen thuộc: Hosting Bundle ASP.NET Core10, pool No Managed Code/Load User Profile, tài khoản dịch vụ riêng ít quyền, quyền đọc cấu hình private và ghi các kho `private/media`, `snapshots`, `temp`, `keys`. Không cấp quyền cho Everyone/Users/IIS_IUSRS vào private. Bật site khi các điều kiện kỹ thuật thực tế đã đạt. Web/Worker sử dụng cùng cấu hình; Worker/SMTP không tự bật.

Không chạy `CAU-HINH-IIS.cmd` hoặc `configure-iis-uat.ps1`. Publish tự sinh cấu hình, Web tự tìm theo vị trí gói trên máy IIS, không mang đường dẫn máy Developer sang máy sếp. Các guard SQL, Application Role, HTTPS, scanner, quyền vẫn giữ; thiếu cấu hình SQL/HTTPS/quyền thì ứng dụng đóng với trang503 tiếng Việt, không mở admin/SQL để vượt chặn.

**Gói Publish có chứa secret trong private/.** Chỉ bàn giao cho người được phép, không đưa gói/.env/private lên Git hoặc URL tải công khai. Không trỏ IIS vào gốc Publish. `web.config` ở gốc cũng chặn HTTP vào private nếu trỏ nhầm. JSON được ràng buộc hash trong manifest; muốn thay cấu hình phải republish từ .env gốc, không sửa JSON.

Publish không mở SQL, không thay dữ liệu, không cấp quyền database, không cài/bật IIS hoặc đổi DNS. Mặc định không ghi đè gói cũ đã có; Developer có thể dùng `-OutputName ("TTDL-IIS-"+(Get-Date -Format yyyyMMdd-HHmmss))` khi cần một gói mới riêng. UAT TLS exception không áp dụng Production. Publish PASS không thay nghiệm thu thực tế IIS/UAT.
