# SQL dùng chung → IIS TTDL (SRS v1.2)

**SQL1934 sau lần owner apply schema:** tạm dừng mọi lệnh apply/schema/role bên dưới. Xem [sql-1934-recovery.md](sql-1934-recovery.md) và chạy query DBA `scripts/inspect-shared-sql-1934.sql` trước. Nguồn generator đã bổ sung required SET options; bộ cũ/hash gốc giữ nguyên, bộ mới ở `.local/publish/shared-sql-1934-fixed/` chưa được phép chạy. Chưa có kết quả live để xác nhận rollback hay commit một phần. Trích log và backup path do owner cung cấp, không phải raw log đầy đủ hay agent verify lại.

Đích được chủ dự án khai báo: **45.120.229.43,1433 / website_tin_tuc**, SQL Server 2022, SQL Login **website_tin_tuc_app**, đã mapping user/public. Không tạo lại database/login, không dùng `sa`, không cấp db_owner/sysadmin. Đã đọc credential từ file private do chủ dự án cung cấp; không đưa secret vào repo. Cấu hình gốc `TrustServerCertificate=true` bị guard chặn trước kết nối. Một bản sao strict (`false`, không sửa file gốc) đã thử kết nối và thất bại TLS **SQL -2146893019 / chuỗi chứng chỉ chưa được tin cậy**. Dừng, chưa xác minh được login/physical identity/schema, chưa chạy SELECT metadata hoặc ghi SQL. DBA cần chứng chỉ/CA hợp lệ và tên CN/SAN phù hợp; không bật bypass để tiếp tục. [Microsoft: certificate validation](https://learn.microsoft.com/en-us/troubleshoot/sql/database-engine/connect/certificate-validation-failure).

**Cập nhật 28/10 — ngoại lệ tạm chỉ cho metadata probe:** owner cho phép `TrustServerCertificate=true` cho đúng endpoint/database/login trên. Một kết nối read-only đã PASS: physical server `WIN-S1IBJB84OOO`, SQL Server `16.0.1000.6` Enterprise, database `website_tin_tuc`, login/user `website_tin_tuc_app`, SQL/TCP, compatibility160. Role chỉpublic; quyền database CONNECT và hai quyền xem định nghĩa khóa mã hóa mặc định, khôngsysadmin/db_owner. DMV encryption bị từ chối SQL300; client yêu cầu Encrypt=true, không kiểm chứng chứng chỉ theo ngoại lệ. Không đổi mặc định strictTLS Web/Worker/UAT/Production, không sửa `.env` nguồn/Dev, không build/publish hoặc ghi SQL.

Visible tables=0 nhưng VIEW DEFINITION=0: **chưa thể kết luận database trống**. DBA cần kiểm tra trong đúngdatabase bằng `SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0;`, đối chiếu `dbo.__EFMigrationsHistory` và applicationrole `LandPortalRuntime` qua tài khoản DBA. Không cấp thêm quyềnserverstate/VIEW DEFINITION cho app chỉ để probe. [Microsoft: metadata visibility](https://learn.microsoft.com/en-us/sql/relational-databases/security/metadata-visibility-configuration).

Đã chuẩn bị offline `.local/publish/shared-sql-website_tin_tuc/schema.sqlcmd` và `runtime-role.sqlcmd`, bind đúngphysicalserver/database, hash tại manifest cùngthưmục. Schema/role **chưa chạy/chưa được duyệt**. Không dùng kết quả probe làm quyền migration/import hoặc tự áp dụng TLS exception lên runtime.

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


## Chốt kiểm kê DBA trước schema — 28/10

Mở [scripts/inspect-shared-sql.sql](../scripts/inspect-shared-sql.sql) trong SSMS bằng DBA rồi chạy toàn bộ. Chỉ SELECT metadata, dừng khi sai physicalserver/database hoặc không có VIEW DEFINITION; kết quả so70bảng (gồmhistory),16migrationIDs và role/grants/mappeduser. Chưa có kết quảDBA nên trạng thái deploy vẫn AWAITING_DBA_READONLY_RESULTS, không coi public thấy0bảng là database trống.

Nếu target thật sự trống (0userobjects/historyabsent/roleabsent), owner duyệt artifactSHA và quyềnDBA/backup trước. Packet `.local/publish/shared-sql-website_tin_tuc/` bổ sung `apply-schema-after-approval.sqlcmd`, `DBA-STEPS.md`, `deployment-manifest.json`; schema/role cũ nguyênhash. Wrapper sẽ kiểmđúngđích+DBA+empty+compat, tạo1COPYONLYCHECKSUMbackupGUID tại InstanceDefaultBackupPath,VERIFYONLY/HEADERONLY rồi mới include schema và readback70/16. Chưa chạyfilewrapper haybackup. Role riêng cần secret target quaSESSION_CONTEXT cùngsession và approvalriêng; nếu roleđãcó thìreconcile,khôngreset. DBA/control/DDL/backup không cấp choapp(public+CONNECT+approle).

Sau đó mới plan/sign/backup/import33Ready;252Pending chỉ sau native target scan+review+exactapproval riêng;29Excluded/IdentityDev/privatecitizen/Devsecrets/auditDev không chuyển. Không fullregression/build/publish/testSQL haykết nối mới trong lượt chốt này.


## DBA đã báo database trống — chuẩn bị manual execution, 28/10

Owner cung cấp kết quả SSMS/DBA: WIN-S1IBJB84OOO/website_tin_tuc,16.0.1000.6,compat160,0bảng/0migration,LandPortalRuntimeABSENT,appuserEXISTS. Đây là kết quả owner báo; agent không query SQL mới. Bộ schema/role/backup runner giữ nguyênhash, deployment-manifest SHA `a1f06b6c631d8e2dc24d71eec556d20f3d4137c0a24a15104398c83cc5403999`. Đủ để chuẩn bị thao tác DBA; runner vẫn kiểm tra fresh full-metadata/empty-userobjects/đúngđích và backupverify trướcDDL, không tự suy mọiuserobjectđềuabsent từtablecount.

Schema: từpacketdirectory, dùng ODBCsqlcmd lệnh `sqlcmd -S "45.120.229.43,1433" -d website_tin_tuc -U "<DBA_LOGIN>" -N -C -b -i apply-schema-after-approval.sqlcmd`. Không -P; DBA nhập password qua prompt. Ghi lại trường `VerifiedPreSchemaBackup` để đối soát và bướcrole. GiữSQLCMD -b/:ON ERROR EXIT; saiđích/backupfail/schemafail thìdừng,khôngretrymù.

Role: helper `scripts/apply-shared-runtime-role.ps1` mặc định PLAN, chỉ ghi khi operator tự truyền `-Apply`. Helper đọc roleSQL đã hash-lock nguyênbyte, không viết lạiDDL; xác minh đúngphysicalserver/database/70bảng/16migration/DBACONTROL/roleabsent, HEADERONLY+VERIFYONLY backup đúngdatabase/checksum trước khi nạp role secret qua parameterized `sp_set_session_context`. DBA credential nhận qua Get-Credential/SqlCredential, không trong connectionstring/log; secret role đọc từ privateJSON. Không tạo database/login, không cấp admin choapp, không reset role cũ. Helper chỉ kiểm cúpháp, **chưa chạy SQL**, không tuyên bố E2E/manual deploy PASS.

Đã tạo private template `C:\Users\ADMIN\LandPortal-Private\runtime-role.private-template.json`, ACL operator/SYSTEM, RolePassword rỗng, không copy mật khẩu SQLLogin. DBA điền secret role mới 24–128 ký tự riêng tư và giữ cùnggiátrị cho cấu hình Web/Worker sau này; không gửi qua chat. Từworkspace chạy:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/apply-shared-runtime-role.ps1 -PacketDirectory .local/publish/shared-sql-website_tin_tuc -PrivateConfig "C:\Users\ADMIN\LandPortal-Private\runtime-role.private-template.json" -VerifiedBackupPath "<VerifiedPreSchemaBackup trên server>" -TemporaryCertificateTrust -Apply
```

Đây là lệnh để owner/DBA tựchạy sau quyếtđịnhthựcthi, không phải quyền agent tựchạy. Nếu chỉ dùng SSMS: SQLCMD mode + cùngsession đãnạp private secret vào `SESSION_CONTEXT('LandPortal.RuntimeRoleSecret')`, rồi `:r` file runtime-role.sqlcmd nguyênbản; thiếusecret sẽfailclosed. Import vẫnchưaduyệt/chưachạy, không build/publish/fullsuite, private SQL config gốc không đổi. Xem `manual-operator-manifest.json` cạnhpacket cho hashhelper và nguồn DBAresults; không sửa/chữký lại manifest cũ.
