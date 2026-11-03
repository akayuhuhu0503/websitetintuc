# Trạng thái bàn giao — cập nhật SQL 03/11/2026

**11:13VN — UAT SQL runtime PASS theo ngoại lệ owner mới:** Encrypt=True/TrustServerCertificate=True + Deployment__SqlTlsExceptionApproved=true/reference, chỉ đúngUAT/Staging/45.120.229.43,1433/WIN-S1IBJB84OOO/website_tin_tuc/appwebsite_tin_tuc_app. App kết nối/kíchhoạtLandPortalRuntime/secretMATCH/70tables/exact17historyPASS,0writes; Production/sa/saitarget/thiếuapproval bị chặn. Sharedinterceptor kiểm traactualtarget/schema/role khi mởconnection. Private.env vàJSON đồng bộ đúngsecretcũ,khôngApplymới. Còn12inputTTDL, **IISPreflightNOTRUN**. GóiPublish02Novcũ chưa chứapolicy mới: sauđủinputchạy1lệnhPublish từcommit mới, không dùngfixture. Xemonepagebàngiao; cácmụcTLSBLOCKEDdoCAcũ dưới đây là lịch sử, không phảiđiềukiệnđổicertbắtbuộcđểUATtheophêduyệt.

**TLS được chẩn đoán10:14VN:** SQL đưa CN=SSL_Self_Signed_Fallback,khôngSAN,chainUntrustedRoot+nameMismatchIP45.120.229.43; chưa hết hạn. Không dùngcredential hoặc đổiCA/trust. Runtimeactivation/IISPreflight vẫnBLOCKED. [Hướng dẫn bàn giao một trang cho Trưởng phòng/DBA/CNTT](docs/TTDL-UAT-ONE-PAGE.md) nêu đúngcertificate/binding/CAclient/DNS và các inputTTDL. Private `ttdl.env.pending` cạnhuat.json đã điền đúngexistingSQLcredential/rolesecret,roundtripMATCH; chưa phải configruntimeREADY. Giữ 70/17/role/login/secret, khôngApply/backup/Publish mới.

**Runtime security10:02VN:** owner-approved LandPortalRuntime **APPLIED/VERIFIED**, exact19GRANT/DENY; appmapping/directCONNECT/0DBrole được giữ, SQL70/17PASS. Private secret và receipts tại `C:\Users\ADMIN\LandPortal-Private\runtime-security-20261103T025642Z-d4d3bd33`, không chuyển vàoGit/webroot. `uat.json` tại đó giữ secret đúng đã dùngprovisioning; cần điền các allocationTTDL còn thiếu rồi chuyển riêng cho Web/5Worker cùng dùng. Không Applyrole/schema lại. Backup cũverify lạiPASS/0backup mới;0migration/import/bootstrap/SMTP/IISstart.

**IISPreflight vẫn BLOCKED/NOTRUN:** appprobe strictTLS bị SQL-2146893019/certificatechain trước activation; không coi roleprovisionPASS là runtimeactivationPASS. Cần SQLcertificate/CA/CN-SAN,TTDLHTTPS/privatehome/4storage/serviceidentities+ACL/Defenderpolicy và targetconfigapproval. Hướng dẫn hiện hành đầu docs/shared-sql-iis.md; các mục awaitingsecurity/waitingDBA phía dưới là lịch sử. Web/Worker binary không thay đổi, không Publish lại.

**Post-schema09:39:** DBA kiểm kê chỉ đọc lại PASS70/17/2cộtFR06; appmapping/SID PASS, directCONNECT/không role database bổ sung. LandPortalRuntime chưa có, **AWAITING_SECURITY_APPROVAL**; không tự tạo hoặc cấp quyền. TemplateTTDL đã điền SQLidentity khôngbí mật nhưng còn thiếu runtime secret/HTTPS/privatepaths/identities/scanner/certificate. Web+5Worker dùng cùngprivateJSON theo source; runtime/IISPreflight **BLOCKED/NOTRUN**. Lệnh cấp role sau duyệt và danh sáchinput tại [hướng dẫn SQL/IIS hiện hành](docs/shared-sql-iis.md). Không chạy lại schema/backup/Publish trong lượt này.

**Shared SQL đã nâng schema thành công lúc08:56 ngày03/11/2026:** WIN-S1IBJB84OOO/website_tin_tuc,70 bảng/17 migration. Exact packet207 và FR06 đã Apply/readback, hai backup COPY_ONLY/CHECKSUM/HEADERONLY/VERIFYONLY PASS. Không chạy lại lệnh Apply trong hướng dẫn lịch sử bên dưới. Receipt private: `C:\Users\ADMIN\LandPortal-Private\approved-schema-20261103T015340Z-dcbdfc40`. Application rights và committed prefix/history IDs giữ nguyên; không runtime role hoặc import. Hai cột email FR06 đã có đúng kiểu/nullability trên shared SQL. LAN là database khác, vẫn chưa nâng migration FR06.

Bước tiếp theo: DBA phê duyệt/cấu hình LandPortalRuntime và quyền runtime tối thiểu; TTDL cung cấp cấu hình thật/HTTPS/private storage/scanner để triển khai IIS; nhập dữ liệu theo gói/phê duyệt riêng. Backup đã VERIFYONLY, **chưa restore thử**. Không xem schema PASS là Production ready.

Source closure: [`d99584a0ae6b1c95362942c56a88c750bb315de0`](https://github.com/akayuhuhu0503/websitetintuc/commit/d99584a0ae6b1c95362942c56a88c750bb315de0), nhánh `main`. Commit tiếp theo chỉ ghi SHA này vào tài liệu; mã ứng dụng/publish không đổi. Dùng `git rev-parse HEAD` và kết quả push để đối chiếu HEAD cuối. Không tuyên bố PRODUCTION READY.

Publish cuối 02/11/2026: **PASS**, Release Web/Worker/MigrationCli/UatDataHandoff; `.local/publish/TTDL-IIS`, 396 file đối chiếu manifest, 0 mismatch. Đây là **cấu hình fixture kỹ thuật**, không phải cấu hình TTDL được cấp. `.env` thật không thay đổi; không kết nối SQL hoặc cài IIS/service. Hai tài liệu bàn giao ở gốc Git này đi kèm `START.md` trong gói. LAN public smoke 6 URL HTTP200; kiểm tra JS UUID ở HTTP context PASS, không thực hiện restore revision. Không chạy lại full regression/E2E BA.

## Phần đã lập trình và giới hạn bằng chứng

- CMS revision/draft/autosave/duyệt-trả bài/xuất bản/gỡ-khôi phục/hẹn giờ, quyền theo vai trò/chuyên mục, mật khẩu BA 8 ký tự đủ 4 nhóm/đổi lần đầu, CAPTCHA/lockout/idle; không MFA. Giữ các PASS LOCAL đã ghi nhận, không chạy lại toàn suite.
- Tin/chuyên mục/tìm kiếm tiếng Việt, văn bản/dự thảo, công khai thông tin, lịch tuần có phiên bản, Word/PDF/Excel, thống kê 34 tỉnh, video nội bộ, tương tác có kiểm duyệt/phân công/ACL/outbox, menu/footer, tin liên quan/xem nhiều có source. Dữ liệu và cấu hình thực chưa đầy đủ không được thay bằng dữ liệu giả.
- FR-06 email nhận góp ý: CODE COMPLETE, nullable theo revision, validate khi có nhập, preview/history/public đúng revision. **Shared SQL schema APPLIED/VERIFIED03/11**; còn cấu hình runtime/IIS. TECHNICAL HTTP/SQL PASS trên database cô lập, vẫn không coi là PASS LAN; LAN chưa nâng migration email.
- FR-10 featured fingerprint đã triển khai trên LAN; kiểm tra offline PASS. TECHNICAL HTTP/SQL E2E bật/tắt→gửi→trả→duyệt và bản công khai giữ nguyên **PASS trên database cô lập**; nghiệm thu LAN **CHỜ BA** dùng credential hiện hành; credential cũ Writer/Reviewer MISMATCH, không reset. Kịch bản tại đầu `docs/PROJECT_STATE.md`.
- Giao diện dùng layout/CSS/partials chung cho public và admin; không social URL/chứng nhận NCSC giả. Rà soát source và HTTP không thay nghiệm thu giao diện đầy đủ của BA.
- Delta cuối: sửa UUID khi khôi phục lịch sử trên HTTP LAN; launcher nhận biết đúng process đang chạy ngay khi CIM thiếu State, không mở trùng/ghi đè process record.

## SQL — quy trình lịch sử đã thực hiện, không replay

Source có **17 migration**. Checkpoint31 bảng/2 migration đã được Inspect lại và nâng thành70 bảng/17 migration trên `WIN-S1IBJB84OOO/website_tin_tuc` ngày03/11. Những lệnh sau giữ làm tài liệu quy trình đã sử dụng, không phải yêu cầu chạy lại.

1. Khi backup được phép, dùng packet riêng `shared-sql-207-resume` SHA `664de13e864e69a989e56d17e2a927b7477ca5c038030dbe19bfc62451a6e1d7`. Fresh Inspect/prefix/history/transaction/backup phải PASS. Packet chỉ tiếp tục **14 migration cũ**, từ sau `20261012083717_CmsRevisionWorkflow` đến `20261027025057_AuditRecoveryLookup`; không mở rộng packet sang FR-06.
2. Lệnh Inspect của packet (DBA chạy khi được phép, credential được hỏi an toàn):
   ```powershell
   powershell -NoProfile -File <PACKET>/recover-207.ps1 -PacketDirectory <PACKET> -Mode Inspect -ManifestSha256 664de13e864e69a989e56d17e2a927b7477ca5c038030dbe19bfc62451a6e1d7 -TemporaryCertificateTrust -ReportFile <NEW_PRIVATE_JSONL>
   ```
   Chỉ sau phê duyệt scope và backup hợp lệ: cùng lệnh đổi `-Mode Apply`, thêm `-ConfirmTarget WIN-S1IBJB84OOO/website_tin_tuc -BackupFile <VERIFIED_SERVER_BAK>`. Không chạy thẳng schema.sqlcmd, không DROP/RESTORE/history cleanup.
3. Sau đối soát 70 bảng/16 migration, chuẩn bị packet delta riêng `20261102042625_DraftConsultationEmail` (hai cột nullable, vẫn 70 bảng/17 migration). Cần fresh state, backup hợp lệ, exact approval mới; packet cũ không bao phủ. Có thể sinh SQL **offline** từ tools/MigrationCli `schema-script --from-migration 20261027025057_AuditRecoveryLookup --to-migration 20261102042625_DraftConsultationEmail --output <SOURCE>/.local/publish/<NEW>/fr06.sql`; đây không phải lệnh Apply.
4. Runtime role `LandPortalRuntime` và mapping login ứng dụng là bước DBA riêng theo `docs/shared-sql-iis.md`; không dùng `sa`, không cấp db_owner cho Web/Worker. Ngoại lệ TLS đã duyệt cho công cụ shared SQL không tự nới mặc định TLS của UAT.

Ngày 02/11 chủ dự án đã thay thế quyết định hoãn backup: cho phép backup và nâng schema **chỉ website_tin_tuc** theo đúng hai packet/hash nêu trên, có DBA hợp lệ và đủ guard. Không mở rộng sang LAN, runtime role, import hoặc triển khai. LAN giữ binary tương thích trước migration email, hết hạn 09/11/2026 10:55:08 VN, SMTP Disabled. Không copy Identity/dữ liệu LAN sang server.

### Thực thi phạm vi đã duyệt bằng DBA

`scripts/run-approved-shared-schema.ps1` mặc định chỉ Plan offline. Lệnh dưới đây hỏi credential qua `Get-Credential`, giữ trong bộ nhớ, không lưu mật khẩu. Chỉ dùng tài khoản DBA được đơn vị quản lý cho phép; không dùng login ứng dụng. Dùng Windows PowerShell 5.1 trong phiên tương tác trên máy đã có hai packet nguyên hash:

```powershell
$receipt = Join-Path $env:USERPROFILE ('LandPortal-Private/schema-approved-' + [guid]::NewGuid().ToString('N'))
powershell -NoProfile -File scripts/run-approved-shared-schema.ps1 -ExecuteApprovedUpgrade -ReportDirectory $receipt
```

Trình điều phối gọi **runner trong từng packet**, không dùng source runner mới thay bản khóa hash cũ. Thứ tự bắt buộc: Inspect prefix2 → backup1/HEADERONLY/VERIFYONLY → Apply14/readback70/16 → InspectFR06 → backup2/HEADERONLY/VERIFYONLY → ApplyFR06/readback70/17. Backup helper chỉ chấp nhận hai hash đã duyệt, Inspect mới trong12giờ, đúng database/transaction/đường dẫn server, còn dung lượng; backup sau Inspect, không ghi đè, không tự retry. Tên file backup đầu giữ prefix ngày cũ để tương thích allowlist nhưng GUID là mới; ngày thực tế đối chiếu từ SQL header, không suy ra từ tên file.

Nếu state đã thay đổi/DDL lỗi: dừng, giữ receipt, không chạy lại lệnh Both. Chỉ sau DBA xác minh đã70/16 và cần bước FR06 thì chạy cùng lệnh với `-StartAt Fr06`; guard vẫn bắt buộc exact16. Nếu đã70/17 thì chỉ readback bằng DBA, không chạy Apply nữa. Các receipt nằm ngoài Git, ACL current operator+SYSTEM. Thiếu credential hoặc không thấy hộp thoại thì BLOCKED, chưa được coi backup/schema thành công.

## Bàn giao ngoài Git và điều kiện đưa vào vận hành

- Developer: gói Publish, packet recovery nguyên hash, package UAT Data Handoff đã kiểm kê + manifest + media/provenance riêng qua kênh an toàn. 33 Ready /252 PendingReview /29 Excluded là checkpoint gói trước, không phải kiểm kê mới hoặc dữ liệu đã nhập đích. Không nhập Excluded, người dân, Identity/secret/audit Dev; scanner đích phải xác nhận đúng byte.
- TTDL/DBA: máy IIS, SQL/schema/role, hostname HTTPS, private home/kho media/keys/backup, danh tính dịch vụ/ACL, Defender policy/pin, FFmpeg/ffprobe được phép, SMTP chính thức. File `.env` thật chưa có đủ thông tin môi trường đích; không dùng cấu hình fixture Publish để chạy server.
- BA: FR-10 E2E; FR-06 sau schema; UAT nghiệp vụ toàn bộ. Lịch Word còn cần hai tuần nghiệp vụ khác; không công bố lịch TTDL thành lịch lãnh đạo Cục. Xác nhận thông tin footer/Dịch vụ công và dữ liệu thống kê chính thức.
- Trước SMTP thật: giữ Mail Disabled/DeliveryEnabled=false; kiểm kê ba thư thử Pending theo ID/event/receipt, người có quyền quyết định xử lý riêng. Không retry/gửi mù hoặc chuyển outbox LAN lên Production.
- NukeViet cần nguồn public truy cập được hoặc export chính thức và mapping duyệt. QLDD còn ngoại lệ file/coverage và media cần quét; không đồng nghĩa chuyển đủ nguồn.
- Nghiệm thu UAT/ATTT, restore đúng bản đích, giám sát SQL timeout, cutover/DNS/rollback do TTDL và chủ dự án duyệt. Thiếu các điều kiện này thì chưa PRODUCTION READY.

Kết luận: source và quy trình Publish bàn giao được khi kiểm tra cuối/push xác nhận; **chưa đủ bằng chứng nghiệm thu toàn SRS**, không che các mục BA/SQL chưa nghiệm thu bằng PASS build.

## Kết quả closure 02/11/2026 — thay thế trạng thái kiểm tra kỹ thuật cũ

- **29/29 kiểm tra tập trung PASS** trong lần chạy cuối `closure-20261102T075058Z-d55665a4-final`, SQL thật `LandPortal_ClosureTest_dc855ce55e54`, chỉ loopback. 17 migration EF /70 bảng và migrate lại không thêm history; ba tài khoản Identity test riêng, quyền Writer/Reviewer/Publisher đúng phạm vi, CAPTCHA thật. FR-10 bật/tắt/no-op revision, gửi/trả/duyệt, nháp chưa công khai; FR-06 preview/history/email-only revision/publish/readback, bản đã xuất bản không đổi khi sửa nháp. Không dùng credential BA, không sửa Dev/Test/LAN/shared SQL. Web thử đã dừng; database fixture được giữ, không backup/copy database.
- Ảnh test lấy từ một ảnh công khai sẵn, Defender native Clean/hash không đổi, không mở ngoại lệ NotScanned. PDF ACL/xem/tải/counter giữ bằng chứng đã PASS trước, không chạy lại hoặc nhận là kết quả mới.
- Hai lần harness trước chưa hoàn tất (fixture Identity thiếu email; gọi compare sai revision) giữ evidence/database riêng; không tính gộp thành 29/29 cuối và không gọi đó là lỗi sản phẩm. Không có backup/restore/reset LAN.
- UI: 10 route public HTTP200/header/footer chung/0 link `#` giả/0 mã FR-BR; trang chủ, văn bản và hỏi đáp không tràn ngang tại390px; trang chủ desktop1366px không tràn. Đây là kiểm tra kỹ thuật layout, BA vẫn xác nhận thẩm mỹ theo ảnh mẫu. Sửa câu nội bộ “Giai đoạn1/adapter” ở Văn bản chỉ đạo điều hành; bản sửa có trong Publish, chưa đưa binary FR-06 lên LAN cũ.
- Publish Release cuối PASS;396 file/0 hash mismatch; cấu hình mẫu, chưa là môi trường thật. Launcher xác nhận đúng PID41476/binaryFR10, không tạo process trùng; LAN vẫn SMTP Disabled, hạn09/11 10:55:08VN.

## Packet FR-06 dành cho DBA — chưa được Apply

Packet riêng `.local/publish/fr06-closure-final-20261102`, manifest SHA `13594b2be96f46b7bd517b5160572800ab63ee4d93ce26ce64b4edf7df8d9a8e`. Đây là expected prefix70/16, **chưa phải kiểm kê server đích**. Original packet207/14 migration giữ nguyên hash. Generator mới `scripts/prepare-fr06-upgrade.ps1` sinh delta bằng EF chính thức và đóng gói runner có target/history/PK/backup/lease/transaction/hash/rights guard; Plan offline PASS, ba negative guards PASS trước kết nối; Apply server thật NOT RUN. Tệp role do CLI xuất riêng không nằm trong manifest/luồng thực thi này.

Sau DBA hoàn tất14 migration cũ và được phép Inspect, dùng đúng runner trong packet:
```powershell
$packet = '<THU_MUC_PACKET_FR06>'
$sha = '13594b2be96f46b7bd517b5160572800ab63ee4d93ce26ce64b4edf7df8d9a8e'
& "$packet/recover-207.ps1" -PacketDirectory $packet -Mode Inspect -ManifestSha256 $sha -TemporaryCertificateTrust -ReportFile '<PRIVATE_NEW_INSPECT_JSONL>'
```
Chỉ khi Inspect PASS exact16, backup COPY_ONLY/CHECKSUM sau thời điểm Inspect được duyệt/tạo/xác minh và chủ dự án duyệt exact packet mới:
```powershell
& "$packet/recover-207.ps1" -PacketDirectory $packet -Mode Apply -ManifestSha256 $sha -ConfirmTarget 'WIN-S1IBJB84OOO/website_tin_tuc' -TemporaryCertificateTrust -PrefixInspectionReport '<PRIVATE_SUCCESS_INSPECT_JSONL>' -BackupFile '<SERVER_VERIFIED_BAK>' -ReportFile '<PRIVATE_NEW_APPLY_JSONL>'
```
Inspect phải còn trong12giờ; backup phải sau Inspect. Mọi prefix/hash/guard lệch dừng trước ghi. Đích đã có17migration phải readback/reconcile, không replay. Không chạy trực tiếp schema.sqlcmd, không chạy Down/DROP/RESTORE để thử lại. Rollback bằng backup là quyết định DBA riêng có đối soát dữ liệu phát sinh, không tự thực hiện. Công cụ này không nâng schema LAN; LAN vẫn cần quy trình/backup hợp lệ được duyệt.
