# Bàn giao IIS UAT — ngoại lệ TLS được duyệt 03/11/2026

**SQL runtime đã PASS:** `website_tin_tuc_app` kết nối đúng `WIN-S1IBJB84OOO / website_tin_tuc`, kích hoạt `LandPortalRuntime` với secret hiện có, đọc đủ70 bảng/17 migration. Probe chỉ đọc, không Apply/backup/import. **IIS Preflight chưa chạy** vì còn thiếu hạ tầng TTDL.

## Chính sách TLS cho lần nghiệm thu UAT

Chủ dự án cho phép `Encrypt=True;TrustServerCertificate=True` riêng endpoint `45.120.229.43,1433`, database `website_tin_tuc`, app `website_tin_tuc_app`. UAT theo ngoại lệ này không cần đổi chứng chỉ SQL. Các trường phải đi cùng nhau:

```ini
DOTNET_ENVIRONMENT=Staging
ASPNETCORE_ENVIRONMENT=Staging
Deployment__Profile=UAT
Deployment__SqlTlsExceptionApproved=true
Deployment__SqlTlsExceptionReference=OWNER-UAT-SQL-TLS-20261103-WIN-S1IBJB84OOO-website_tin_tuc
```

Production/sai target/thiếu reference/tắt mã hóa đều bị chặn. Default template vẫn xác minh CA. Chứng chỉ fallback chưa được sửa; đây là bỏ kiểm tra CA/tên theo phê duyệt UAT. Kết thúc ngoại lệ thì đổi cờ vềfalse và TrustServerCertificate=False sau khi có chứng chỉ phù hợp. Vận hành chính thức được duyệt riêng.

## DBA và CNTT cần cung cấp

DBA giữ schema/role/login/secret đã tạo; không cấp thêm role quản trị hoặc chạy lại migration. CNTT cấp ASP.NET Core10 Hosting Bundle, danh tính AppPool/Worker và ACL thực tế.

| Thông tin TTDL còn phải cấp | Biến cấu hình |
|---|---|
| Địa chỉ HTTPS | `Deployment__ExpectedHost`, `Public__BaseUrl` |
| Private home và4 kho riêng | `LANDPORTAL_HOME`, `Storage__MediaRoot/SnapshotRoot/TempRoot/KeysRoot` |
| Danh tính dịch vụ/quyền tệp; Defender được duyệt | `Scanner__DefenderServiceIdentity`, `Scanner__DefenderBinarySha256` và ACL thực tế |
| Phê duyệt đích/scanner/quyền riêng tư | `Deployment__TargetApprovalReference`, `Scanner__PolicyReference`, `Scanner__DefenderPrivacyPolicyReference` |

Tổng12 biến. Đặt `Deployment__TargetApproved=true` sau khi máy đích được duyệt. Password SQL/secret role **đã có**, không tạo lại hoặc gửi qua chat. `ttdl.env.pending` cạnh `uat.json` giữ hai secret và cùng ngoại lệ TLS; điền12 biến thật rồi chuyển riêng. Web/5Worker dùng chung `LANDPORTAL_HOME`/`LANDPORTAL_CONFIG=<PRIVATE_HOME>\uat.json` ngoài webroot. Giữ MailDisabled; video cần FFmpeg path/hash/license được duyệt khi bật.

## Một file cấu hình, một lệnh Publish

Pull code mới, đặt private file đầy đủ thành `.env` ở gốc source:

```powershell
powershell -NoProfile -File scripts/build-for-iis.ps1
```

Nhận `.local/publish/TTDL-IIS/`, IIS dùng `web/`; chuyển privateJSON tới đúngPRIVATE_HOME. Gói cũ chưa có policy mới; tạo gói từ code mới khi đủ input, không dùng fixture thay cấu hình TTDL.

## Developer kiểm tra sau khi nhận đủ cấu hình

Chạy trên máy TTDL bằng đúng identity vận hành. Preflight dưới đây kết nối bằng app login, kích hoạt role đã có rồi đối chiếu SQL/schema, không cấp quyền hoặc khởi động ứng dụng:

```powershell
powershell -NoProfile -File scripts/uat-runtime.ps1 -DataHome '<PRIVATE_HOME>' -Config '<PRIVATE_HOME>\uat.json' -PublishRoot '<TTDL-IIS>' -Mode Preflight -Online -Report '<PRIVATE_HOME>\reports\uat-readonly-new.json'
```

Chỉ nhận `READ_ONLY_SQL_PREFLIGHT_PASS` khi có receipt thật. HTTPS/IIS, scanner và UAT nghiệp vụ vẫn cần nghiệm thu riêng. Chưa bật IIS/Worker/SMTP/Production hoặc đổi DNS trong lượt chuẩn bị này.
