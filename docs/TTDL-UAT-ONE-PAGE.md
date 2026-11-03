# Bàn giao TLS SQL và IIS UAT — 03/11/2026

**Hiện trạng:** `WIN-S1IBJB84OOO / website_tin_tuc` đã có 70 bảng/17 migration; LandPortalRuntime và mapping app login đã PASS. Không chạy lại Apply. TLS hiện BLOCKED: endpoint `45.120.229.43,1433` cung cấp `CN=SSL_Self_Signed_Fallback`, issuer cùng tên, không SAN; Windows báo **UntrustedRoot + RemoteCertificateNameMismatch**. Chứng chỉ còn hạn đến19/10/2056. Chẩn đoán chỉ PRELOGIN/TLS, không đăng nhập SQL hoặc thay cấu hình.

## DBA thực hiện trên SQL Server

1. Cung cấp tên DNS SQL chính thức và bộ chứng chỉ CA. Chứng chỉ phải phù hợp SQL Server2022: còn hạn, EKU **Server Authentication**, private key/KeySpec **AT_KEYEXCHANGE**, SAN chứa đúng tên kết nối. Nếu tiếp tục kết nối bằng IP, phải đáp ứng xác minh IP `45.120.229.43`; không tự thay bằng một DNS chưa được xác nhận. Xem [yêu cầu chứng chỉ Microsoft](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/certificate-requirements?view=sql-server-ver16).
2. Cài chứng chỉ server tại Local Computer/Personal, cho đúng tài khoản dịch vụ SQL đọc private key, chọn chứng chỉ cho đúng instance/cổng1433 trong SQL Server Configuration Manager. DBA lên lịch bảo trì nếu cần restart; SQL dùng chung có thể ảnh hưởng ứng dụng khác. Kiểm tra SQL ERRORLOG xác nhận chứng chỉ mới được nạp. Xem [hướng dẫn cấu hình Microsoft](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/configure-sql-server-encryption?view=sql-server-ver16).
3. Bàn giao DNS/cổng, public certificate, SHA-256, root/intermediate CA và thời điểm đổi chứng chỉ; không chuyển private key SQL sang máy IIS. Giữ nguyên database/login/role/secret.

## CNTT/ATTT thực hiện trên máy IIS TTDL

- Qua kênh được duyệt, triển khai root/intermediate CA vào kho Local Computer phù hợp cho máy IIS và máy Worker. Không tin cậy chứng chỉ fallback. Kiểm tra DNS, giờ hệ thống và xác minh TLS từ chính máy đích.
- Cấp HTTPS/Hosting Bundle ASP.NET Core10, danh tính AppPool/Worker và ACL cho private home. Giữ `Encrypt=True;TrustServerCertificate=False`. Nếu đổi endpoint sang DNS đã duyệt, cập nhật đồng bộ `ConnectionStrings__Portal` và `Deployment__ExpectedSqlServer`; physical identity vẫn `WIN-S1IBJB84OOO`.

| Thông tin TTDL còn phải cấp | Biến cấu hình |
|---|---|
| Địa chỉ HTTPS | `Deployment__ExpectedHost`, `Public__BaseUrl` |
| Private home và4 kho riêng | `LANDPORTAL_HOME`, `Storage__MediaRoot/SnapshotRoot/TempRoot/KeysRoot` |
| Danh tính dịch vụ/quyền tệp; Defender được duyệt | `Scanner__DefenderServiceIdentity`, `Scanner__DefenderBinarySha256` và ACL thực tế |
| Phê duyệt đích/scanner/quyền riêng tư | `Deployment__TargetApprovalReference`, `Scanner__PolicyReference`, `Scanner__DefenderPrivacyPolicyReference` |

Đặt `Deployment__TargetApproved=true` sau khi đích thực tế được duyệt. Password SQL/secret role **đã có**, không tạo lại hoặc gửi qua chat. File private `ttdl.env.pending` cạnh `uat.json` đã điền đúng hai secret hiện có, chưa đủ input để chạy. Điền phần còn thiếu, chuyển qua kênh an toàn; Web và5Worker dùng chung `LANDPORTAL_CONFIG=<PRIVATE_HOME>\uat.json` ngoài webroot. Giữ MailDisabled; video chỉ bật khi đường dẫn/hash/license FFmpeg được duyệt.

## Developer kiểm tra sau khi nhận đủ cấu hình

Chạy trên máy TTDL bằng đúng identity vận hành. Preflight dưới đây kết nối bằng app login, kích hoạt role đã có rồi đối chiếu SQL/schema, không cấp quyền hoặc khởi động ứng dụng:

```powershell
powershell -NoProfile -File scripts/uat-runtime.ps1 -DataHome '<PRIVATE_HOME>' -Config '<PRIVATE_HOME>\uat.json' -PublishRoot '<TTDL-IIS>' -Mode Preflight -Online -Report '<PRIVATE_HOME>\reports\uat-readonly-new.json'
```

Chỉ nhận `READ_ONLY_SQL_PREFLIGHT_PASS` khi có receipt thật. HTTPS/IIS, scanner và UAT nghiệp vụ vẫn cần nghiệm thu riêng. Chưa bật IIS/Worker/SMTP/Production hoặc đổi DNS trong lượt chuẩn bị này.
