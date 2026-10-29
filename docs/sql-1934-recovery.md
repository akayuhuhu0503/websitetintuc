# Hiện hành — một backup trạng thái prefix và lệnh Apply sau duyệt, 29/10/2026

Owner đã chạy packetInspect PASS, receipt `.local/inspect-207-5abbda122d0243b7b40acf7a6c9c1a0b.jsonl`:31tables/2committed migrations exactprefix, **backupVerified=false**. Manifest vẫn `664de13e864e69a989e56d17e2a927b7477ca5c038030dbe19bfc62451a6e1d7`, mọi filehash giữ nguyên. Agent chưa chạy SQLbackup/verification/schema. Không có backupreceipt mới; các đường dẫn sau là đã chuẩn bị.

**DBA chạy lệnh backup/xác minh sau khi được duyệt**, từ workspace:

```powershell
$plan = 'C:\Users\ADMIN\LandPortal-Private\sql-207-backup\7ca7b91f4f0b46aeb96fe62b5e7c5739\backup-plan.json'
powershell -NoProfile -File .\scripts\backup-shared-sql-207.ps1 -PlanFile $plan -Mode Backup -TemporaryCertificateTrust
```

DBAcredential nhập secureprompt, không dùng applicationlogin. Nếu chỉ muốn xem kế hoạch offline, thayModeBackup bằngModePlan. Operator mới không tạo role/import/migration; nó đọc/hash đúngpacket/priorInspect, freshprefix/authority/TX/sharedlease, chỉtạo01backup nếu fixedfile chưa có. SQL native là BACKUP DATABASE [website_tin_tuc] TO DISK=@file WITH COPY_ONLY,CHECKSUM,NOINIT,NOFORMAT,NOSKIP,NAME=@tag,MEDIANAME=@tag. Khi file đãcó, chỉ đọcheader đúngtag/target/single-set+reuse, khôngappend/overwrite; file khôngkhớp/partial haythiếupermission/parent dừng. ExistingpacketInspect -BackupFile kiểm HEADERONLY/VERIFYONLY+freshprefix sau đó. VERIFYONLY không phải restore database. [Microsoft BACKUP options](https://learn.microsoft.com/en-us/sql/t-sql/statements/backup-transact-sql?view=sql-server-ver16).

- File BAK trên SQLserver: `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\website_tin_tuc_prefix2_20261029_7ca7b91f4f0b46aeb96fe62b5e7c5739.bak`.
- Receipt local sau thành công: `C:\Users\ADMIN\LandPortal-Private\sql-207-backup\7ca7b91f4f0b46aeb96fe62b5e7c5739\backup-receipt.jsonl`; stage cuối phải BACKUP_VERIFIED, target+manifestSHA+BackupSetGUID/path/size/checkpointLSN/2historyIDs và verificationReceiptSHA được liên kết. Localreceipt privateACLoperator+SYSTEM. Rerun cùngplan chỉverify/reusevalidfile; khôngchạybackupmới. Nếu timeout/uncertain thì dừngreconcile trước khi owner chủ động chạy lại.
- Thư mụcSQLservice đã từngbackup thànhcông nhưng dunglượng/quyềnghi hiện tại chưa được kiểm chứng tronglượt này. DBA cần đủ quyềnBACKUP/fullmetadata+transactionvisibility và thư mục đủchỗ/quyềnSQLservice. Chưa tạo BAK/receipt khi chỉ chuẩn bị.

**Apply chỉ sau owner duyệt exactpacket/phạmvi14migration**, backupPASS và maintenancewindow; không chạy khối này trướcduyệt:

```powershell
$plan = 'C:\Users\ADMIN\LandPortal-Private\sql-207-backup\7ca7b91f4f0b46aeb96fe62b5e7c5739\backup-plan.json'
$cfg = Get-Content -LiteralPath $plan -Raw | ConvertFrom-Json
$proof = @(Get-Content -LiteralPath $cfg.receiptFile | ForEach-Object { $_ | ConvertFrom-Json })[-1]
if ($proof.stage -cne 'BACKUP_VERIFIED' -or $proof.manifestSha256 -cne '664de13e864e69a989e56d17e2a927b7477ca5c038030dbe19bfc62451a6e1d7' -or $proof.target -cne 'WIN-S1IBJB84OOO/website_tin_tuc' -or $proof.backupFile -cne $cfg.backupFile) { throw 'Backup receipt chưa PASS đúng phạm vi; không Apply.' }
$report = Join-Path "$($cfg.packetDirectory)\..\.." ('apply-207-' + [guid]::NewGuid().ToString('N') + '.jsonl')
powershell -NoProfile -File "$($cfg.packetDirectory)\recover-207.ps1" -PacketDirectory $cfg.packetDirectory -Mode Apply -ManifestSha256 664de13e864e69a989e56d17e2a927b7477ca5c038030dbe19bfc62451a6e1d7 -ConfirmTarget 'WIN-S1IBJB84OOO/website_tin_tuc' -BackupFile $cfg.backupFile -TemporaryCertificateTrust -ReportFile $report
```

Applyoperator unchanged tự rehash packet+scope, freshfullprefix/history/permission/transaction, backupheader/freshness/VERIFYONLY rồi freshprefix lần nữa trướcDDL. Schema chỉ14pending sauCmsRevisionWorkflow; khôngreplayInitial/Cms, khôngDROPtable/resetHistory/role/import. Cần cuối cùng SCHEMA_RECOVERY_VERIFIED với **70tables/16migration IDs/5filteredindexes**, prefix31tableIDs+history/PKIDs+appSID/rights giữ nguyên, không business/Identityimport. Nếu lỗi: STOP_RECONCILE_REQUIRED, khôngblind-retry/restore/drop. Lượtchuẩn bị chỉsyntax/Plan/scope-negative/packetintegrity, khôngSQLconnection/build/ZIP/fullsuite. Các checkpoint dưới là lịch sử.

# Hiện hành — resume đúng prefix sau SQL207, 29/10/2026

Đã đọc đủ13file DBA từ `C:\Users\ADMIN\LandPortal-Private\sql-207-inspection\`: **31bảng,66index,98đối tượng,211cột,2migration đã commit** là InitialPortal và CmsRevisionWorkflow. Không có ApprovedByUserId/các bảng migration3. DMV complete không trả transaction tại snapshot01:58:47UTC; kiểm tra mới vẫn bắt buộc. Mô hình EF tại migration2 khớp names/types/nullability/PK/indexkeys/FKs (0diff những trường đã đối chiếu). Guard mới còn pin exactobjectIDs/defaults/filters/SID/permissions từDBAmetadata. Dữ liệu nghiệp vụ estimate0, futureguard đọc EXISTS thật trước DDL.

Nguyên nhân207: raw UPDATE ApprovedByUserId bind trước ALTER ADD trong cùng batch7; migration nguồn đã dùng EXEC đúng transaction. CLI schema-script hỗ trợ `--from-migration 20261012083717_CmsRevisionWorkflow --to-migration 20261027025057_AuditRecoveryLookup`; output chỉ14migration còn thiếu, không replay2migration/historybootstrap. Không có database/login recreation/role/import. Hai DROP CONSTRAINT cóguard do EF sinh trong AuditRecoveryLookup là xử lý default Actor/Action nếu tồn tại khi ALTER, không DROP bảng; DBAprefix hiện ghi cả hai default NULL. Chưa thực thi DDL nào tronglượt chuẩn bị.

**Packet mới:** `.local/publish/shared-sql-207-resume/`.

**Manifest SHA-256:** `664de13e864e69a989e56d17e2a927b7477ca5c038030dbe19bfc62451a6e1d7`.

Source: `scripts/recover-shared-sql-207.ps1`, `scripts/prepare-shared-sql-207.py`, migrationPortalAdministration và CLIrangegenerator. Oldpacket451+4filehash nguyên byte. Packet mới không kế thừa approval cũ. Backup empty-state28Oct không còn phù hợp để bảo vệ31table prefix; cần một COPY_ONLY/CHECKSUM SQLbackup mới sau timestampbaseline, kiểmHEADERONLY/VERIFYONLY bằngDBA. Agent không tạo/verifybackup hay chạySQL. Media không có record; vẫn bảo vệ mọi kho riêng được cấu hình nếu có.

**Inspect tiếp theo**, từ PowerShell (DBAcredential qua hộp thoại, không trên command line):

```powershell
$p = 'C:\TrungTamDuLieuVaThongTinDatDai\website_tin_tuc\.local\publish\shared-sql-207-resume'
$r = Join-Path "$p\..\.." ('inspect-207-' + [guid]::NewGuid().ToString('N') + '.jsonl')
powershell -NoProfile -File "$p\recover-207.ps1" -PacketDirectory $p -Mode Inspect -ManifestSha256 664de13e864e69a989e56d17e2a927b7477ca5c038030dbe19bfc62451a6e1d7 -TemporaryCertificateTrust -ReportFile $r
```

Inspect không truyềnBackupFile chỉ kiểm state/lease/rights/transaction, ghi `backupVerified=false`, `applyReady=false`, chưa đủ Apply. Nếu đã có backup mới, Inspect có thể thêm `-BackupFile '<đường dẫn tuyệt đối trên SQL server>'` để kiểm header/checksum (không restore database). Chỉ sau exactstateInspect PASS, backup phù hợp và ownerduyệt **newhash/phạmvi14remaining**, mới được operator dùngModeApply+ConfirmTarget+BackupFile. Không tự chạy schema.sqlcmd hoặc retrybootstrap runner. Bất kỳ state drift/guarderror/commit không rõ thì dừngreconcile, không DROP/restore/reset history.

Kiểm tra đã chạy sau code cuối: CLI/testbuild0warning/error,4focusedofflinebackfill, providerprefixmodelcomparison, generation14+2range-negative, PSsyntax/Plan/integrity/wronghash/oldbackup/tampernegative. GuardSQL mới **chưa chạy trênengine**, fullDBAInspect/backup/Apply NOTRUN; không fullsuite/WebWorkerpublish. PROJECT_STATE/matrix đã cập nhật; không báo schema70/16 đã hoàn tất. Các phần dưới là lịch sử trước13CSVreadback.

# Hiện hành 29/10 — lỗi207 ở batch7, KHÔNG chạy lại bootstrap packet

Receipt `.local/apply-451-6b77465b5ccc4c1bb3ec1e536e7c2e13.jsonl` xác nhận modeApply/DDL/query schema.batch.7/SHA92beae743cf0fb53c0f20ef3f034346f9998792756b9d61d02cb571a5dd3593a, SQL207 line446. Statement là `UPDATE c SET ApprovedByUserId=... FROM dbo.Contents c ...`; cột được ADD đầu cùng batch trên bảng đã có. Lỗi binding trước thực thi ALTER; source migration20261013030300_PortalAdministration đã bọc riêng UPDATE trong EXEC(N'...') để compile sau AddColumn, giữ đúng backfill/EFtransaction/history. Không đổi tên cột/order hoặc thêm migration. [Microsoft EF: EXEC cho lỗi parser của idempotent script khi cột chưa tồn tại](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing).

Hai batch migration trước có COMMIT riêng và đã trả về trước khi runner tới batch7; không kết luận tất cả rollback. Prefix dự kiến là31bảng/2migration (InitialPortal,CmsRevisionWorkflow); số thực vẫn UNKNOWN vì appcredential không có VIEWDEFINITION/historySELECT/DMVpermission. Không thể dùng visible0 để coi DB trống. Cần fullDBA readback để xác nhận cả indexes/constraints/transactions và loại trừ DDL partial khác.

**Inspect tiếp theo**: chạy toàn bộ `scripts/inspect-shared-sql-207.sql` trong cửa sổ SSMS mới bằng DBA; hoặc từ workspace:

```powershell
sqlcmd -I -S "45.120.229.43,1433" -d website_tin_tuc -U "<DBA_LOGIN>" -N -C -b -i scripts/inspect-shared-sql-207.sql
```

Không dùng -P; nhập password khi được hỏi. Đây chỉ là SELECT metadata/history và session settings, không schema/backup/restore/role/import. File guard đúng physical/database/fullmetadata/CONTROL/newsession, lấy names/columns/defaults/keys/indexes/FKs/history và transactions, không đọc dữ liệu người dân/passwordhash hoặc raw sessionSQL. PermissionDMV thiếu được ghi UNKNOWN, không coi là0transaction. Lưu kết quả private và cung cấp đường dẫn cho bước đối chiếu; không cần chia sẻ DBAcredential.

Sau readback, nếu đúng prefix2migration/schema/indexes và không activeTX/drift, tạo packet riêng chỉ từ CmsRevisionWorkflow đến migration16 với guard exactprefix/cấu trúc/SIDquyền; bảo vệ trạng thái partial bằng backup phù hợp và target-bound verification. Nếu có DDL migration3 residual hoặc object lệch, phải thiết kế repair exactdiff trước; không xóa bảng/history để về0. Manifest/hash mới chỉ tạo **sau khi xác định trạng thái thực**, rồi cần owner phê duyệt riêng trước Apply. Packet451 cũ byte/hash nguyên; approval cũ không tự áp dụng cho packet mới.

Kiểm tra cuối: source build CLI/test/deps PASS0warning/error;4focusedoffline migration operations/generatedSQL PASS, static inspection guard/noDDL-DML PASS. NoWebWorkerbuild/publish/fullregression/SQLengineDDL/Apply/backup/restore. Evidence `.local/sql207-20261029/summary.json`. Chưa tạo packet/hash resume do thiếu DBAmetadata. Các lệnh recovery/Inspect bootstrap ở phần dưới là lịch sử, không dùng cho DB sau lần Apply207.

# Bổ sung hiện hành — sửa PRECHECK SQL451, chờ Inspect DBA mới

Query gây lỗi là `Rights-Fingerprint`, định danh `metadata.application-rights-fingerprint`, cột3 `Detail`. Đã tái hiện đúng SQL451 trên target bằng SELECT metadata: `sys.database_principals.name` dùng `SQL_Latin1_General_CP1_CI_AS`, trong khi `type_desc`, `sys.database_permissions.state_desc/permission_name` dùng `Latin1_General_CI_AS_KS_WS`. Phép nối chuỗi và projection UNION ALL mang collation xung đột. Bản sửa `COLLATE DATABASE_DEFAULT` tại toán hạng/output đã SELECT PASS; không thay collation database/server, không thay dữ liệu/quyền hoặc binary structural guards. [Microsoft: collation precedence](https://learn.microsoft.com/en-us/sql/t-sql/statements/collation-precedence-transact-sql).

Source: `scripts/recover-shared-sql-bootstrap.ps1`. Receipt v2 ghi stage/queryId/querySha256/sqlNumber và sqlErrors(number/state/severity/line), thông báo lọc theo allowlist; không ghi querytext/parameter/connectionstring/arbitrary error value. Guard target/history/PK/0migration/backup/transaction/manifest vẫn giữ nguyên. Đã chạy PSsyntax, local Plan/filehash, oldhash rejection và diagnostic redaction/querycontext; không build/publish/fullregression.

Packet mới **`.local/publish/shared-sql-1934-recovery-451/`**; SHA manifest:

`b57c8a64531f787d97975ac6c3c9b400c74db19985ef35e6d1f4478c6bf9cff5`

Oldpacket8d13b555... và bốn file giữ nguyên. Schema và bootstrap SQL guard trong packet mới nguyên byte; chỉ helper/START/manifest thay đổi. Bằng chứng `.local/sql451-20261028/live-select-repro.json` (4SELECT/1connection/0writes) và offline-checks.json. Đây không phải full DBA Inspect: metadata visibility của application login có giới hạn, backup/transaction/full structural guards chưa được chạy lại bằng DBA.

Chạy từ PowerShell tại workspace; credential DBA nhập qua hộp thoại, không đặt password trên command line. Receipt mới tự đặt ACL operator/SYSTEM. **Chỉ Inspect:**

```powershell
$p = Join-Path (Get-Location) '.local\publish\shared-sql-1934-recovery-451'
$r = Join-Path (Get-Location) ('.local\inspect-451-' + [guid]::NewGuid().ToString('N') + '.jsonl')
powershell -NoProfile -File "$p\recover-bootstrap.ps1" -PacketDirectory $p -Mode Inspect -ManifestSha256 b57c8a64531f787d97975ac6c3c9b400c74db19985ef35e6d1f4478c6bf9cff5 -TemporaryCertificateTrust -ReportFile $r
```

Inspect có SELECT metadata/cooperative session lease và HEADERONLY/VERIFYONLY backup đã có, không chạy schema/khôi phục database. Cần toàn bộ Inspect PASS, maintenance window/không transaction khác, backup đúng target/khả dụng, rồi phê duyệt riêng exact **new manifest** phạm vi schema-only trước Apply. Không tái dùng approval hash cũ. Nếu guard lỗi, dừng/reconcile; không auto retry, DROP, RESTORE hoặc tạo runtime role/import. Các phần dưới là checkpoint lịch sử, không thay thế trạng thái mới này.

# SQL 1934 — nguồn đã sửa, chờ kiểm kê DBA

**Cập nhật owner DBA 28/10:** đã xác nhận chính xác `dbo.__EFMigrationsHistory` (USER_TABLE), `dbo.PK___EFMigrationsHistory` (PRIMARY_KEY_CONSTRAINT), 0 history rows, không có bảng nghiệp vụ khác. Chọn giữ cả hai và roll-forward có guard, không DROP/restore. Đây là thông tin owner cung cấp, không phải agent tự query. Cấu trúc/PK/zero-row/transaction/backup vẫn được kiểm tra lại khi operator chạy.

Packet mới `.local/publish/shared-sql-1934-recovery/`, manifest SHA `8d13b555f8c3fa571ab3cdf29f3d6a126c059df8c4125c217ee02741f7b97f31`, **PREPARED_NOT_APPROVED_NOT_EXECUTED**. Chỉ copy nguyênbyte schema đã sửa; không có file tạo runtime role/import. `scripts/verify-shared-sql-bootstrap.sql` là guard chỉ đọc; `scripts/recover-shared-sql-bootstrap.ps1`/bản packet `recover-bootstrap.ps1` mặc định Plan, có Inspect chỉ đọc và Apply cần exact SHA + `WIN-S1IBJB84OOO/website_tin_tuc`. Hash-lock toàn packet, credentialDBA qua prompt/SqlCredential, target cố định, TLS exception tạm tường minh.

Guard không dựa riêng tên/số lượng: xác nhận hai cột nvarchar150/32 NOT NULL đúng ordinal/collation, không default/identity/computed/encryption/masking/extra; PK clustered unique ASC MigrationId đúng tên, không index/trigger/FK lạ; 0 row; SID login/user app khớp, role vẫn absent. Operator phải có full metadata/CONTROL/server transaction visibility, không còn target user transaction khác; cooperative lease và maintenance window. Re-HEADERONLY/VERIFYONLY backup cũ đúng physicalserver/database/COPY_ONLY/full/checksum/single-set; không tạo backup mới/không restore DB. Freshguard lặp trước DDL. Sau schema so đúng70tênbảng/16ID/5filteredindex, lịch sử/PK objectID giữ nguyên, quyền/SID/roles app không đổi, không có role/source-content/media/Identity account import. Bất kỳ lỗi nào dừng và reconcile, không retry hoặc tự rollback các migration đã commit.

Kiểm tra lượt chuẩn bị này: cú pháp PowerShell, Plan/hash4file và negative wrong-manifest PASS; **0SQLconnection/0DDL**, không build/publish/fullsuite. Xem START.md trong packet cho lệnh operator; approval mới phải đúng SHA trên và scope **schema only**, không role/import. Các nhận định thiếu dữ liệu ở phần dưới là lịch sử trước khi owner bổ sung tên đối tượng.

Đích duy nhất: **WIN-S1IBJB84OOO / website_tin_tuc**. Không retry schema/role/import trong lúc chưa biết trạng thái sau lỗi. Không sửa/drop bảng, xóa migration history, KILL session hoặc RESTORE WITH REPLACE.

## Nguyên nhân và statement

Trích log owner cung cấp: Error1934, level16/state1, batchLine1, `QUOTED_IDENTIFIER` sai. ODBC sqlcmd mặc định QI=OFF khi không có `-I`; generator cũ không đặt QI. Index lọc đầu tiên trong artifact cũ:

```sql
-- schema.sqlcmd cũ, dòng468, migration 20261012055549_InitialPortal
EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
```

`UserNameIndex` ở dòng508 cũng chịu ảnh hưởng. Line1 trong trích log thuộc nested batch EXEC, không phải dòng1 file; trích log không nêu tên index nên không khẳng định chỉ một index đã lỗi. [Microsoft: QI](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-quoted-identifier-transact-sql) và [ODBC sqlcmd -I](https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility).

## Fix nguồn

`UatSqlScripts` và `Program` sinh prologue session trước các batch EF/nested EXEC, giữ qua GO:

```sql
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO
```

`prepare-uat-target.ps1` từ chối template cũ thiếu policy này. Không ALTER DATABASE/server SET options. Schema/role/unbound mới ở `.local/publish/shared-sql-1934-fixed/`, chưa được chạy/duyệt. DDL và16migration/70bảng không đổi; EF chỉ đổi tên local variables mặc định constraint do tái sử dụng một lần generate. Hash artifact mới phải duyệt lại; không giả định bằng hash cũ. Cảnh báo Identity composite-key length là vấn đề riêng, không phải nguyên nhân1934; chưa đổi key/cột hoặc migration để che cảnh báo.

## Trạng thái transaction chưa xác minh

History table được tạo trước `BEGIN TRANSACTION` đầu (dòng19); `COMMIT` đầu ở dòng696; các migration tiếp theo có commit riêng. Script cũ không có XACT_ABORT ON. Do đó lỗi không chứng minh cả16migration đã rollback: có thể chỉ còn history table, có migration commit với index thiếu, hoặc session/rollback chưa kết thúc. Không suy trạng thái live từ transaction/SET options của kết nối kiểm tra mới. Cần so cả history **và index thực tế**.

Owner báo backup COPY_ONLY/CHECKSUM, VERIFYONLY/HEADERONLY PASS, đường dẫn trên **SQL server**:

```text
C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\website_tin_tuc_pre_schema_20261028_074711_584F7E5FF87E4AF79DFCD8D70065B8B9.bak
```

Đây là bằng chứng từ phần log trích owner gửi, không phải agent verify lại. Giữ backup này, không ghi đè; không yêu cầu hash log raw cũ.

## Lệnh tiếp theo: chỉ đọc

Trong SSMS, DBA mở **cửa sổ mới**, chạy toàn bộ [inspect-shared-sql-1934.sql](../scripts/inspect-shared-sql-1934.sql). Hoặc từ workspace dùng ODBC sqlcmd:

```powershell
sqlcmd -I -S "45.120.229.43,1433" -d website_tin_tuc -U "<DBA_LOGIN>" -N -C -b -i scripts/inspect-shared-sql-1934.sql
```

Không -P; mật khẩu qua prompt. Chỉ đọc metadata target: bảng/index/cột,16migrationID/diff,5filteredindexes,role,transactionDMV nếu có quyền, backup history msdb lọc đúngdatabase. Không đọc nội dung người dân/SQL text/secret, không thay quyền. Metadata lockwait giới hạn5giây của diagnostic session; lỗi1222 hoặc DMV thiếu quyền không phải bằng chứng không có transaction. `msdb` chỉ SELECT, không ghi.

Sau kết quả DBA mới chốt recovery:

- Trống hoặc chỉ history table đúng cấu trúc/0row, không residual objects/transaction: giữ history, chuẩn bị wrapper roll-forward đúng trạng thái, fresh backupverify rồi xin duyệt exact hash mới. Không dùng wrapper cũ đòi database trống hoặc drop history để vượt guard.
- Có migration đã commit: kiểm định nghĩa và index thiếu trước; idempotent script sẽ skip migration có marker, nên không tự sửa missing index. Cần repair additive đúng diff và approval riêng.
- Có objects/history lệch hoặc transaction còn mở: dừng, reconcile với DBA; chưa thể chọn continue/restore. Mọi restore nếu cần phải có phạm vi và phê duyệt riêng.

Kiểm tra đã chạy: CLI build0warning/error;7nhóm SQL offline; sinh SQL offline; bind template đúng và từ chối template thiếuQI. Không thử CREATE INDEX trên engine, không query/sharedSQLwrite/backup/restore/import, không Web/Worker publish hay full regression. Trạng thái: **FIXED GENERATOR / RECOVERY BLOCKED ON DBA READBACK**.
