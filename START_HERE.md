# Bàn giao website Cục Quản lý đất đai — TTDL

**Điều kiện SQL/IIS hiện tại:** xem [hướng dẫn một trang cho Trưởng phòng, DBA và CNTT](docs/TTDL-UAT-ONE-PAGE.md). Schema70/17, role và SQLruntime đãPASS qua ngoại lệ TrustServerCertificate=True được duyệt riêng cho đúngUAT/Staging/SQLtarget. Cần12inputTTDL trước IISPreflight; không yêu cầu đổi certificate để chạyUAT theo ngoại lệ này. Không Apply lại hoặc dùng fixture để triển khai; gói cũ chưa có policy mới phải build từ source mới khi đủ `.env`.

1. Clone `https://github.com/akayuhuhu0503/websitetintuc.git` hoặc `git pull --ff-only`. Đọc trạng thái trong [RELEASE_HANDOFF.md](RELEASE_HANDOFF.md).
2. Cài .NET SDK đúng `global.json`, Node.js/npm; IIS đích cần ASP.NET Core 10 Hosting Bundle, HTTPS và SQL Server 2022. Restore package cần mạng hoặc cache sẵn, không cần kết nối SQL.
3. Developer/CNTT điền cấu hình đích theo `scripts/ttdl.env.example`, chuyển riêng qua kênh an toàn; copy thành **`.env` ở gốc source**. Không dùng `.env` Dev và không commit file này. Các thông tin chưa cấp phải được điền thật, không dùng fixture kiểm tra.
4. Chạy tại gốc source:

   ```powershell
   powershell -NoProfile -File scripts/build-for-iis.ps1
   ```

5. Chỉ nhận gói khi lệnh kết thúc **PUBLISH PASS**. Output `.local/publish/TTDL-IIS/`, có `manifest.json`. **IIS Physical Path là thư mục `web/` bên trong**, không phải cả gói hoặc `wwwroot/`.
6. Cấu hình được chuyển tự động sang JSON ngoài Git/webroot, ACL người build + SYSTEM. Lệnh in đường dẫn `PRIVATE CONFIG`. Nếu máy build khác máy IIS, chuyển riêng `uat.json` đến đúng `LANDPORTAL_HOME/uat.json` đã khai báo trên máy IIS; CNTT cấp ACL đọc cho danh tính Web/Worker và quyền riêng cho kho dữ liệu. `web.config` đã có đường dẫn đích. JSON không tự xuất hiện trên máy IIS.
7. Giữ `worker/`, `tools/`, `ops/` ngoài webroot. Worker có 5 chế độ Scheduler/Documents/InquiryScan/Video/Mail, cùng dùng private JSON; xem `START.md` trong gói và `ops/uat-operator-steps.md`. Không tự bật Mail trước khi đối soát outbox.

Output cũ: mặc định từ chối ghi đè. `-Replace` chỉ dùng khi bản cũ chưa sealed/chưa được IIS/process sử dụng và chưa có `TTDL-IIS.previous`; bảo toàn bản trước, không xóa tùy tiện. Xem [hướng dẫn chi tiết](docs/iis-one-command.md).

**Trước khởi động IIS:** DBA hoàn tất schema/role đúng đích; CNTT cấp HTTPS, private storage/ACL, Defender đã pin/duyệt, cấu hình video và SMTP nếu dùng. Build không tạo database, migration, tài khoản hoặc service. PUBLISH PASS không phải DEPLOY/UAT/PRODUCTION PASS.

Thông tin TTDL cần cấp trong `scripts/ttdl.env.example`: DBA điền `ConnectionStrings__Portal`, `Sql__ExpectedDatabase`, `Deployment__ExpectedSqlServer`, `Deployment__ExpectedSqlServerIdentity`, `Deployment__ExpectedSqlLogin`, `Runtime__RolePassword`; CNTT điền `LANDPORTAL_HOME`, bốn biến `Storage__*Root`, `Public__BaseUrl`, `Deployment__ExpectedHost`, phê duyệt `Deployment__TargetApproved/TargetApprovalReference`; ATTT/CNTT cấp `Scanner__PolicyReference`, `Scanner__DefenderPrivacyPolicyReference`, `Scanner__DefenderBinarySha256`, `Scanner__DefenderServiceIdentity`, các biến `Video__Ffmpeg*/Ffprobe*/LicenseReference`. SMTP giữ `Mail__Transport=Disabled`, `Mail__DeliveryEnabled=false` đến khi duyệt cấu hình `Mail__Host/Port/From/Tls/UserName/Password/PolicyReference` và đối soát outbox.
