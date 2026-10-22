# Gói UAT — chưa triển khai, chưa nghiệm thu hạ tầng

Yêu cầu: Windows/IIS + ASP.NET Core 10 Hosting Bundle (x64), SQL Server
compatibility 160, chứng chỉ HTTPS và SQL TLS hợp lệ; website/app pool riêng.
Operator xác nhận server/host/LandPortal_Uat trước mọi thao tác ghi. Không dùng
LandPortal_Dev/Test; không copy toàn SQL Test có fixture. Không mang .env máy Dev.

1. Kiểm manifest SHA của gói; đặt web/worker/tools ở thư mục chỉ đọc của code.
   Copy ops/uat-config.example.json vào **kho riêng ngoài code** dưới tên uat.json.
   Điền đường dẫn thực, exact SQL/HTTPS host, runtime role secret >=24 ký tự.
   Chỉ operator và service account được đọc config/keys. Không ghi secret vào log.
   Web/Worker cần quyền ghi media/temp/keys theo phạm vi, không quyền sửa code.
2. DBA tạo LandPortal_Uat với backup trước, chạy schema-idempotent.sql sau review.
   Script chỉ có schema, không có dữ liệu nguồn/tài khoản. Không migrate khi web start.
   Provision APPLICATION ROLE LandPortalRuntime bằng secret riêng từ secret store:
   GRANT SELECT,INSERT,UPDATE,DELETE ON SCHEMA::dbo; DENY ALTER;
   DENY UPDATE,DELETE cho AuditLogs,InteractionPublicSnapshots,InquiryAssignments,
   InquiryCalendars,InquiryCalendarExceptions,InquiryExtensions,OutboxReconciliations.
   Windows login Web/Worker chỉ CONNECT + quyền kích hoạt approle, không db_owner.
   DBA phải kiểm __EFMigrationsHistory cuối = 20261015091551_V05EditorialDocuments.
3. Chưa có quyền cấp tài khoản UAT trong lượt Dev này. Người được phân công bootstrap
   admin riêng bằng công cụ đã có trên target (LANDPORTAL_HOME/CONFIG, Staging;
   dotnet tools/MigrationCli.dll bootstrap). Trong uat.json private, dùng hai khóa
   Bootstrap.AdminEmail và Bootstrap.AdminPassword (mật khẩu riêng theo policy,
   tối thiểu12 ký tự); không đưa giá trị vào command line/log. Đặt process env
   LANDPORTAL_HOME=<PRIVATE_HOME>, LANDPORTAL_CONFIG=<PRIVATE_HOME>\uat.json,
   DOTNET_ENVIRONMENT=Staging và ASPNETCORE_ENVIRONMENT=Staging trước lệnh CLI.
   Xác minh tài khoản/role bằng đăng nhập thật; không coi dòng CLI success là
   chứng cứ nếu cấu hình bootstrap trống. Xóa hai bootstrap secret sau hoàn tất.
   Không đặt mật khẩu mẫu, không chạy bootstrap Dev hoặc reset người đã có.
   Admin đăng nhập CAPTCHA/password -> /admin/account/mfa -> nhập password, TOTP
   thủ công, lưu 10 recovery codes riêng. Mọi admin UAT phải có amr=mfa.
   Reset thiết bị cần password + recovery một lần; không tự reset người khác.
4. Operator cấp scanner Clamd nội bộ đáng tin cậy, policy/version/StreamMaxLength
   đủ 1GiB nếu bật video, cách ly khỏi public, TLS tunnel nếu scanner ở mạng khác.
   Kiểm Clean/EICAR/failure thật tại đích trước bật nghiệp vụ. DefenderTest và
   AllowUnscanned bị chặn ở UAT. File Word/PDF/Excel và hỏi đáp đi queue private;
   ảnh CMS UAT quét Clamd đồng bộ, file không sạch giữ quarantine.
   **Media nguồn hiện có vẫn bị fence Development của proof cũ**: phải có quy trình
   target scan/release được duyệt trước nhập/hiển thị tại UAT. Không sửa 53 NotScanned.
5. Cấp FFmpeg/ffprobe bên ngoài repository, ghi SHA256/nguồn/giấy phép/approval trong
   Video config. Không lấy binary hoặc đường dẫn máy Dev sang target mặc định.
6. SMTP mặc định Disabled. Chỉ sau relay/policy và người nhận Test được duyệt, đặt
   Transport=Smtp, DeliveryEnabled=true, Host/Port/From/Tls=StartTls, credential đôi
   nếu relay yêu cầu. Chứng chỉ relay phải hợp lệ; AUTH chỉ sau TLS. Outbox Uncertain
   phải đối soát receipt bởi người có quyền, không tự retry. Không gửi thư thật khi
   chưa được duyệt. Worker TestSink chỉ Development/SQL Test/loopback.
7. IIS trỏ physical path vào web, app pool No Managed Code x64; operator sửa hai
   đường dẫn LANDPORTAL trong web.config, gắn HTTPS đúng host. Không expose private
   store bằng virtual directory. HSTS/redirect bật; giữ request limit 20MiB vì video
   upload theo chunk 8MiB. Không có cài service/IIS tự động trong gói.
8. Chạy foreground từng Worker dưới service identity qua:
   powershell -File ops/uat-runtime.ps1 -Home D:\LandPortal-Uat-Private
     -Config D:\LandPortal-Uat-Private\uat.json -PublishRoot D:\LandPortal-Uat-Code
     -Mode Scheduler|Documents|InquiryScan|Video|Mail
   Dùng -Once để chạy một vòng, -Health cho InquiryScan/Video. Operator thiết lập
   supervisor/restart sau nghiệm thu; SQL leases bảo vệ restart/claim.
9. Trước import hoặc thay đổi: dừng writers/scheduler phù hợp; SQL COPY_ONLY,CHECKSUM
   -> VERIFYONLY/HEADERONLY; snapshot media/snapshots/keys/config đồng thời + file
   SHA256 manifest. SQL service cần backup folder riêng đã duyệt. Thực hành RESTORE
   vào DB phục hồi riêng với MOVE mới, không WITH REPLACE vào Dev/Test/UAT đang dùng;
   đối chiếu schema/content/revision/asset/alias/quyền/hash và readback HTTP. Đánh giá
   độ lệch SQL/media và writers sau backup trước rollback. Công cụ backup Dev đã có
   không được tự đổi target sang UAT để lách guard.
10. UAT thật phải nghiệm thu: HTTPS, SQL TLS/runtime role, Clean/EICAR/failure, SMTP
    sink/relay, MFA/recovery/revoke, upload+duyệt đúng revision, draft/private ACL,
    video Range, restart worker, search tiếng Việt, restore, 3 Word lịch thật và
    dữ liệu QLDD/NukeViet. Gói publish local không phải bằng chứng các mục này PASS.
    Cutover chỉ sau PO/ATTT duyệt mapping/301/DNS/rollback và tắt nguồn sync.
