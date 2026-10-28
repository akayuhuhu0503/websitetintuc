# SQL dùng chung → IIS TTDL (SRS v1.2)

Đích được chủ dự án khai báo: **45.120.229.43,1433 / website_tin_tuc**, SQL Server 2022, SQL Login **website_tin_tuc_app**, đã mapping user/public. Không tạo lại database/login, không dùng `sa`, không cấp db_owner/sysadmin. Đã đọc credential từ file private do chủ dự án cung cấp; không đưa secret vào repo. Cấu hình gốc `TrustServerCertificate=true` bị guard chặn trước kết nối. Một bản sao strict (`false`, không sửa file gốc) đã thử kết nối và thất bại TLS **SQL -2146893019 / chuỗi chứng chỉ chưa được tin cậy**. Dừng, chưa xác minh được login/physical identity/schema, chưa chạy SELECT metadata hoặc ghi SQL. DBA cần chứng chỉ/CA hợp lệ và tên CN/SAN phù hợp; không bật bypass để tiếp tục. [Microsoft: certificate validation](https://learn.microsoft.com/en-us/troubleshoot/sql/database-engine/connect/certificate-validation-failure).

Mọi bước ghi dưới đây **chưa được thực hiện**, phải được duyệt riêng.

## 1. Build và cấu hình private

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-for-iis.ps1
```

Lệnh vẫn chỉ build, không cần SQL hoặc `.env`. Nếu đã có output chốt thì giữ nguyên; chỉ `-Replace` cho output chưa dùng theo hướng dẫn `iis-one-command.md`. Web copy từ `web/`, các công cụ giữ ngoài webroot.

Copy `scripts/shared-sql.env.example` đến private home ngoài Git/webroot; điền endpoint/database/login trên và mật khẩu qua kênh an toàn. `Deployment__TargetApproved` chỉ xác nhận **target đã được cấp**, không phải quyền migration/import. Bật `true` và ghi reference quyết định target sau review. Giữ `Encrypt=true;TrustServerCertificate=false;Persist Security Info=false`. Nếu certificate SQL có tên DNS thay vì IP, DBA cấp CN/SAN và CA phù hợp; dùng `HostNameInCertificate` đã xác nhận, không tắt xác minh TLS.

```powershell
powershell -NoProfile -File scripts/prepare-iis-config.ps1 -EnvFile <PRIVATE_ENV> -DataHome <PRIVATE_HOME> -PublishRoot <PACKAGE>
```

Sinh `uat.json` và `iis.web.config` private, không in secret. File ban đầu chỉ operator/SYSTEM đọc; CNTT cấp read cho đúng identity Web/Worker. Không đặt `.env` trong IIS, không đưa credential vào command line. Web và 5 chế độ Worker cùng dùng `LANDPORTAL_HOME` + `LANDPORTAL_CONFIG`, Staging/UAT. Password SQL Login và `Runtime__RolePassword` là **hai bí mật khác nhau**. Cấu hình thiếu physical identity/HTTPS/scanner/runtime role sẽ chặn khởi động; không fallback Dev.

## 2. Chỉ probe metadata khi đã có config private hợp lệ

```powershell
dotnet <PACKAGE>\tools\MigrationCli.dll uat-sql-probe --home <PRIVATE_HOME> --config <PRIVATE_HOME>\uat.json --report <PRIVATE_HOME>\reports\sql-discovery.json
```

Không mở app role trước schema. Probe chỉ SELECT server identity, DB, login, compatibility và dấu hiệu schema/quyền quản trị, không tạo hay sửa đối tượng. Nếu chưa khai báo physical identity, kết quả là **DISCOVERY/NEEDS DBA CONFIRMATION**. DBA xác nhận `SERVERPROPERTY('ServerName')`; điền `Deployment__ExpectedSqlServerIdentity` đúng tên đó, **khác IP/cổng kết nối**. Nếu TLS/login/quyền lỗi, dừng, không retry mù hoặc bypass. Không ghi secret vào report.

## 3. Schema và application role — DBA sau phê duyệt exact artifact

Migration hiện hành: `20261027025057_AuditRecoveryLookup`. Template schema đầy đủ được sinh offline từ EF migrations, không chứa dữ liệu/Identity accounts. Từ checkout source, dùng MigrationCli vừa build để sinh **hai file mới** sau khi có physical identity:

```powershell
dotnet <BUILT_MIGRATIONCLI_DLL> schema-script --output .local/publish/shared-sql-bound/schema.sqlcmd --target-database website_tin_tuc --server-identity "<DBA_CONFIRMED_SERVER_NAME>" --runtime-role-output .local/publish/shared-sql-bound/runtime-role.sqlcmd --app-login website_tin_tuc_app
```

Đối chiếu SHA-256 của từng file và duyệt đúng target/schema trước khi chạy. **Không dùng login ứng dụng chạy schema.** DBA dùng tài khoản triển khai riêng có quyền DDL/security đúng database này; không thay đổi database khác. Lệnh schema sau là bước tương lai, không phải phê duyệt đã cấp:

```powershell
sqlcmd -S "45.120.229.43,1433" -d "website_tin_tuc" -U "<DBA_OPERATOR_LOGIN>" -N -b -i "<SCHEMA_BOUND.sqlcmd>"
```

Không truyền `-P`; công cụ hỏi mật khẩu. Không dùng `-C`. Nếu cần hostname certificate, dùng phiên bản sqlcmd hỗ trợ cấu hình certificate hostname hoặc SSMS với TLS đã kiểm chứng. Chỉ chạy trong SQLCMD mode để `:ON ERROR EXIT` dừng ngay khi sai đích. Không tạo database/login, không tự nâng compatibility; nếu thấp hơn 160, DBA xử lý theo quyết định riêng trước vận hành.

Trong **cùng kết nối SSMS SQLCMD mode đã kiểm đúng đích**, DBA đặt `SESSION_CONTEXT` key `LandPortal.RuntimeRoleSecret` bằng tham số bí mật (24–128 ký tự) từ private config rồi chạy file `runtime-role.sqlcmd`. Không lưu câu lệnh chứa giá trị bí mật hoặc chụp màn hình nó. Script tạo role nếu chưa có; nếu role đã tồn tại thì dừng để reconcile, không reset password. CONNECT cấp cho user đã mapping; đối chiếu `mappedUser` trong probe nếu tên user khác login. Role được DML schema dbo theo cơ chế hiện có; giữ DENY ALTER, DENY sửa/xóa AuditLogs và các bảng audit bất biến. Login ứng dụng chỉ public + CONNECT; **không db_datareader/db_datawriter/db_ddladmin/db_owner**, không quyền backup/restore hoặc tạo role. `sp_setapprole` được runtime gọi mỗi kết nối, pooling vẫn tắt. [Microsoft: application roles](https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/application-roles).

Chỉ sau schema/role/scanner/HTTPS/ACL sẵn sàng mới chạy `ops/uat-runtime.ps1 -Mode Preflight -Online ...` để kiểm đúng physical server/database/schema **và app role thật**. Không coi metadata probe là runtime PASS.

## 4. Chuyển dữ liệu — operator riêng và exact approval

Tái sử dụng package `9ab72d10-521f-4e4a-8a2a-a2fd80f3ee89`, manifest `afc5f4eba1fbdf3dd3026f404e638bf51bd41d31fc7dfc6a0dc7a140bf43f3ae`; không sửa package đã chốt. **Ready 33 bài /33 published revision/0 media/66 alias**; **Pending 252 bài/703 media** cần native target scan + release approval. Selection sau duyệt tối đa **285 bài/285 revision/703 media/570 alias**. **Excluded 29 bài**: 27 phụ thuộc 53 NotScanned, 2 video metadata-only. Chuyên mục 18 rows, không phải thay đổi 17 nhóm bảo toàn lịch sử. Không copy 325 revision toàn Dev, Identity, user Test/LAN, private submissions, secret hoặc audit Dev.

Theo `uat-data-handoff.md`: inspect → offline-plan → target plan → signer TTDL + exact manifest/plan/physical identity/SID/selection → fresh backup → native scan từng media → import transaction → readback/receipt → alias/media smoke; rollback theo batch khi cần. App login/public không có quyền operator import/backup: dùng private operator config riêng do DBA cấp, không nâng quyền lâu dài của Web. TargetPlan/approval mới đã bind thêm physical identity nên **không tái sử dụng approval/plan cũ**. Package cũ vẫn nguyên hash; source công cụ mới phải được build riêng.

Các thao tác ghi schema/role, cấp quyền SQL, initialize/bootstrap tài khoản mới, backup, import và publish config/data đều chờ phê duyệt cụ thể. Chưa chạy lên server chung; chưa có dữ liệu nào được chuyển sang database đó.
