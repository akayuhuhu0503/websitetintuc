# Một file .env, một lệnh Publish

**Quy trình ngắn (Auto):** đặt `.env` ở gốc clone → chạy `powershell -NoProfile -File scripts/build-for-iis.ps1` → copy thư mục Publish lên IIS và trỏ site vào `web/`. Nếu build trên cùng IIS và đủ điều kiện, cấu hình private được tạo/gắn tự động. Nếu build ở máy khác, mở `CAU-HINH-IIS.cmd` trên máy IIS, chọn cùng `.env`; không tạo hoặc chuyển `uat.json` thủ công. Site/pool phải dừng khi gắn cấu hình. Xem [hướng dẫn ngắn](TTDL-GITHUB-IIS-HANDOFF.md) cho các khóa mục tiêu/phê duyệt thật cần có trong .env.

Những lệnh JSON/ApprovalFile bên dưới là cách chẩn đoán nâng cao, không phải thao tác thủ công bắt buộc. Thiếu phê duyệt/HTTPS/service account/Defender sẽ được báo tiếng Việt và giữ ứng dụng đóng, không tự cấp quyền hay tạo phê duyệt. Với gói UAT chưa gắn cấu hình, trang503 có mã `IIS-CONFIG-001/002/003` thay cho việc cố mở ứng dụng không hợp lệ.

**Developer:** chuẩn bị `scripts/ttdl.env.example` bằng credential hiện hành của hai connection string (`ConnectionStrings__Portal`, `ConnectionStrings__GiayHuyReadOnly`) và `Runtime__RolePassword`. Giữ hai khóa phê duyệt `GiayHuy__PublicationScope`, `GiayHuy__LanUatTlsException` trong mẫu. Physical SQL và ngoại lệ TLS chỉ áp dụng UAT đã xác nhận; không dùng sa, không tạo secret mới, không chỉnh `.env` Dev. Giao file hoàn chỉnh riêng; người vận hành không phải tạo lại SQL/schema/login/role. Hướng dẫn ngắn: [TTDL-GITHUB-IIS-HANDOFF.md](TTDL-GITHUB-IIS-HANDOFF.md).

**Người build:** đặt file thành `.env` ở gốc clone, chạy:

```powershell
powershell -NoProfile -File scripts/build-for-iis.ps1
```

Output `.local/publish/TTDL-IIS/`: Web, một Worker với 5 chế độ, Tools, Ops, START.md và manifest SHA-256. Cấu hình được tự chuyển sang JSON tại đường dẫn `PRIVATE CONFIG` ngoài source/webroot; ACL người build + SYSTEM. Không kết nối SQL hoặc khởi động ứng dụng. Không cần hostname/storage/scanner/approval để Publish. Gói chưa gắn đích có cờ chặn khởi động.

Nếu build trên máy khác, chuyển gói code và bàn giao riêng **cùng file `.env`** qua kênh an toàn. Trên IIS mở `CAU-HINH-IIS.cmd`, chọn .env; công cụ tự tạo/gắn JSON private ngoài webroot, không cần người triển khai chuyển JSON từ máy build. Không gửi `.env`/JSON lên Git hoặc đặt trong `web/`. Giữ gói bất biến để kiểm tra manifest; overlay web.config khi gắn đích được ghi receipt riêng. Chỉ thư mục `web/` làm IIS Physical Path.

## Một lần chuẩn bị trên IIS đích — CNTT

1. Cài Hosting Bundle ASP.NET Core 10 và IIS. Tạo site/pool riêng, **site và pool đều Stopped**, No Managed Code, Load User Profile=true. Chọn tài khoản dịch vụ riêng ít quyền đã được cấp; dùng cùng identity cho Web và 5 Worker. Không dùng Admin/sa. Không cần tạo tài khoản trong script.
2. Đặt physical path vào `PACKAGE\web`, cấu hình binding HTTPS với hostname/chứng chỉ thật; không khởi động. Nếu nhiều HTTPS binding, thêm `-HostName` khi chạy lệnh bên dưới.
3. TTDL xác nhận triển khai và chính sách Defender/privacy bằng hồ sơ thật. Dùng `ops/iis-approval.example.json` làm dữ liệu tham chiếu, điền tên máy/site thực và các reference hợp lệ; bản PENDING không phải phê duyệt. Không đưa hồ sơ private lên Git. Không cần sửa SQL/schema/role.
4. Trong PowerShell quản trị trên máy IIS, chạy lệnh sau; mặc định chỉ đọc discovery. Thêm `-Apply` khi đã kiểm tra đúng site, đường dẫn và phê duyệt:

   ```powershell
   & '<PACKAGE>\ops\configure-iis-uat.ps1' -PublishRoot '<PACKAGE>' -SiteName '<TÊN SITE IIS THẬT>' -PrivateConfig '<FILE JSON PRIVATE ĐÃ CHUYỂN>' -ApprovalFile '<HỒ SƠ DUYỆT TTDL.json>' -Apply
   ```

   Lệnh lấy host/port từ IIS, suy ra URL, lấy service account từ pool, phát hiện Defender thật/chữ ký Microsoft/SHA/health/privacy. Storage mặc định theo **CommonApplicationData của máy đích** và ID site, bốn thư mục con riêng `media/snapshots/temp/keys`; có thể chỉ định `-DataHome` theo ổ lưu trữ được cấp. Không hardcode ổ của Developer. Chỉ tạo home mới/ACL đúng identity, không đè cấu hình hoặc storage có sẵn. Thiếu policy/phê duyệt, HTTPS hay Defender hợp lệ thì chặn Activation. Không thay chính sách AV.

   JSON đích là `DATA_HOME\uat.json`. Web.config được gắn file này; private `activation.json` ghi manifest gốc và SHA overlay web.config. Manifest gốc không bị sửa. Không mở SQL, bật IIS/Worker hoặc SMTP. Nếu lệnh bị gián đoạn, giữ trạng thái để đối soát, không xóa home hay chạy lại mù.
5. **Bằng đúng service account**, kiểm tra read-only trước khi khởi động:

   ```powershell
   & '<PACKAGE>\ops\probe-uat-defender.ps1' -Settings '<DATA_HOME>\uat.json'
   & '<PACKAGE>\ops\uat-runtime.ps1' -DataHome '<DATA_HOME>' -Config '<DATA_HOME>\uat.json' -PublishRoot '<PACKAGE>' -Mode Preflight -Online -Report '<DATA_HOME>\preflight-<RUN-ID>.json'
   ```

   SQL dùng app login và kích hoạt LandPortalRuntime, kiểm tra physical server/db/schema trước truy vấn nghiệp vụ. Preflight không phải nghiệm thu scanner E2E/HTTPS/UAT. CNTT phải kiểm tra ACL và scanner Clean/EICAR/ScanFailed trên máy đích theo runbook trước xử lý upload. Chưa có máy thật thì giữ BLOCKED/NOT RUN.
6. Sau khi các kiểm tra/duyệt đích đạt, CNTT khởi động site và cấu hình tác vụ theo `ops/export-uat-worker-tasks.ps1`. Cả 5 chế độ **Scheduler/Documents/InquiryScan/Video/Mail** cùng dùng `DATA_HOME\uat.json`; script chỉ xuất XML, không tự cài tác vụ. Video cần FFmpeg/FFprobe và giấy phép/pin được duyệt riêng mới bật xử lý. SMTP Disabled/Mail chưa bật đến khi được duyệt và đối soát outbox.

**Ranh giới:** PUBLISH PASS ≠ IIS/UAT PASS. Production không kế thừa ngoại lệ TrustServerCertificate. Không migration/import/bootstrap/backup/DNS trong quy trình đóng gói. SQL70/17 và role đã có, không Apply lại. Đã có output thì mặc định dừng; `-Replace` chỉ bảo toàn bản trước của gói chưa dùng/chưa sealed, không xóa previous. `-EnvFile` cho phép Developer kiểm tra file private ngoài source mà không sửa `.env` Dev; mặc định vẫn một lệnh trên.
