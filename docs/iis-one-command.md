# Bàn giao IIS — TTDL

**03/11/2026 — ngoại lệ TLS UAT được owner duyệt:** `Encrypt=True;TrustServerCertificate=True` chỉ cho45.120.229.43,1433 / WIN-S1IBJB84OOO / website_tin_tuc / website_tin_tuc_app. Điền `Deployment__SqlTlsExceptionApproved=true` và `Deployment__SqlTlsExceptionReference` đúng phê duyệt trong private `.env`; Production/sai target/thiếu reference vẫn bị chặn. Quy trình một file `.env`/một lệnh giữ nguyên; [bàn giao một trang](TTDL-UAT-ONE-PAGE.md) liệt kê12 inputTTDL còn thiếu. Gói cũ chưa có policy mới cần build từ source mới khi đủ input, không Publish bằng fixture.

1. Clone `https://github.com/akayuhuhu0503/websitetintuc.git`, hoặc `git pull --ff-only`. Đối chiếu commit bàn giao; thay đổi chưa push chưa có trên GitHub.
2. Copy `.env` riêng do Developer bàn giao vào gốc source. Mẫu để điền: `scripts/ttdl.env.example`. Không dùng `.env` Development.
3. Tại gốc source, chạy **một lệnh**:

   ```powershell
   powershell -NoProfile -File scripts/build-for-iis.ps1
   ```

4. Output: `.local/publish/TTDL-IIS/`. **Chỉ `web/` là IIS Physical Path**, không phải toàn bộ gói hoặc `web/wwwroot/`.
5. Giữ `worker/`, `tools/`, `ops/` ngoài webroot. `web.config` đã trỏ đúng private home theo `.env`; không sửa đường dẫn bằng tay.
   - Mặc định **Transfer**: JSON được tạo riêng, ACL chỉ người build + SYSTEM, tại đường dẫn `PRIVATE CONFIG` in cuối lệnh; không nằm trong gói publish. Chuyển riêng `uat.json` qua kênh an toàn tới `LANDPORTAL_HOME/uat.json` trên IIS, tạo kho và cấp quyền cho danh tính Web/Worker được duyệt. Không copy vào `web/`. ACL nguồn không thay thế ACL đích.
   - Build ngay trên IIS: thêm `-PrivateMode Local` vào cùng lệnh. CNTT tạo/cấp ACL private home trước; script tạo JSON tại đó, không ghi đè config cũ. Cấp quyền đọc JSON cho Web/Worker và quyền kho theo runbook; script không tự cấp quyền dịch vụ.
6. Chỉ bật IIS/Worker khi SQL/schema/runtime role, HTTPS và scanner đã được duyệt/kiểm chứng. Worker dùng chung JSON, chọn `Scheduler`, `Documents`, `InquiryScan`, `Video`, `Mail`:

   ```powershell
   powershell -NoProfile -File <PACKAGE>/ops/uat-runtime.ps1 -DataHome <PRIVATE_HOME> -Config <PRIVATE_HOME>/uat.json -PublishRoot <PACKAGE> -Mode Scheduler
   ```

**Điều kiện/lỗi:** Windows PowerShell5.1, SDK đúng `global.json`, Node/npm và mạng/cache restore. `Required field` nêu khóa thiếu, không in giá trị. Chỉ hỗ trợ UAT/Staging theo guard hiện hành, không tự mở Production/TLS bypass. Video/SMTP tắt có thể publish nhưng chưa nghiệm thu vận hành tương ứng. Build khác máy chỉ kiểm tra cấu hình offline; phải kiểm chứng binary/pin/ACL thật trên IIS.

Output đã có: mặc định dừng. `-Replace` chỉ thay bản chưa sealed/chưa dùng, giữ bản cũ ở `TTDL-IIS.previous`. Có previous hoặc JSON đích thì dừng, không tự xóa. Không đặt IIS trực tiếp vào output build nếu còn định thay gói.

**PUBLISH PASS không phải IIS DEPLOY PASS.** Không mở SQL/chạy app/worker/migration/import/backup/tạo tài khoản/bật SMTP/cài IIS/service/firewall/DNS khi build. Database `website_tin_tuc` đã70bảng/17migration, role và SQLruntime probePASS; không chạy lại Apply. IISPreflight cònBLOCKED do12inputTTDL. CHƯA PRODUCTION READY.
